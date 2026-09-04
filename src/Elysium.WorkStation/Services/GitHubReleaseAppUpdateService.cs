using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Elysium.WorkStation.Services
{
    public sealed class GitHubReleaseAppUpdateService : IAppUpdateService
    {
        private const string AppName = "MyWorkStation";
        private const string Owner = "mape1402";
        private const string Repository = "elysium-workstation";
        private static readonly string[] CompatibleRuntimeIdentifiers = ["win10-x64", "win-x64"];
        private const string LatestReleaseUrl = "https://api.github.com/repos/mape1402/elysium-workstation/releases/latest";
        private const string UserAgent = "MyWorkStation-Updater";

        public string CurrentVersion => ResolveCurrentVersion();

        public async Task<AppUpdateInfo> CheckLatestAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var client = CreateHttpClient(TimeSpan.FromSeconds(30));
                using var response = await client.GetAsync(LatestReleaseUrl, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return BuildUnavailableUpdateResult();
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = document.RootElement;

                var latestTag = ReadString(root, "tag_name");
                var latestVersion = NormalizeVersion(latestTag);
                var releaseUrl = ReadString(root, "html_url");
                var currentVersion = CurrentVersion;

                if (string.IsNullOrWhiteSpace(latestTag) ||
                    !TryParseVersion(latestVersion, out _))
                {
                    return BuildUnavailableUpdateResult();
                }

                ReleaseAsset selectedAsset = null;
                if (root.TryGetProperty("assets", out var assets) &&
                    assets.ValueKind == JsonValueKind.Array)
                {
                    selectedAsset = SelectReleaseAsset(assets, latestTag);
                }

                var hasNewerVersion = IsNewerVersion(latestVersion, currentVersion);
                var hasAsset = selectedAsset is not null &&
                    !string.IsNullOrWhiteSpace(selectedAsset.DownloadUrl);

                if (!hasNewerVersion)
                {
                    return new AppUpdateInfo
                    {
                        CurrentVersion = currentVersion,
                        LatestTag = latestTag,
                        LatestVersion = latestVersion,
                        ReleaseUrl = releaseUrl,
                        AssetName = selectedAsset?.Name ?? string.Empty,
                        AssetDownloadUrl = selectedAsset?.DownloadUrl ?? string.Empty,
                        AssetSizeBytes = selectedAsset?.SizeBytes ?? 0,
                        IsUpdateAvailable = false,
                        Message = $"Ya estas en la version mas reciente ({currentVersion})."
                    };
                }

                if (!hasAsset)
                {
                    return new AppUpdateInfo
                    {
                        CurrentVersion = currentVersion,
                        LatestTag = latestTag,
                        LatestVersion = latestVersion,
                        ReleaseUrl = releaseUrl,
                        IsUpdateAvailable = false,
                        Message = $"Existe {latestTag}, pero no encontre un zip compatible para {string.Join("/", CompatibleRuntimeIdentifiers)}."
                    };
                }

                return new AppUpdateInfo
                {
                    CurrentVersion = currentVersion,
                    LatestTag = latestTag,
                    LatestVersion = latestVersion,
                    ReleaseUrl = releaseUrl,
                    AssetName = selectedAsset.Name,
                    AssetDownloadUrl = selectedAsset.DownloadUrl,
                    AssetSizeBytes = selectedAsset.SizeBytes,
                    IsUpdateAvailable = true,
                    Message = $"Nueva version disponible: {latestTag}."
                };
            }
            catch
            {
                return BuildUnavailableUpdateResult();
            }
        }

        public async Task DownloadAndApplyAsync(
            AppUpdateInfo update,
            IProgress<AppUpdateProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("El updater automatico solo esta disponible en Windows.");
            }

            if (update is null || !update.IsUpdateAvailable || string.IsNullOrWhiteSpace(update.AssetDownloadUrl))
            {
                throw new InvalidOperationException("No hay una actualizacion valida para instalar.");
            }

            var installDirectory = Path.GetFullPath(AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            var executablePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("No se pudo resolver el ejecutable actual.");

            var updatesRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppName,
                "updates");
            var updateDirectory = Path.Combine(updatesRoot, SanitizePathSegment(update.LatestTag));
            var payloadDirectory = Path.Combine(updateDirectory, "payload");
            var zipPath = Path.Combine(updateDirectory, update.AssetName);
            var scriptPath = Path.Combine(updateDirectory, "apply-update.ps1");

            if (Directory.Exists(updateDirectory))
            {
                Directory.Delete(updateDirectory, recursive: true);
            }

            Directory.CreateDirectory(payloadDirectory);

            progress?.Report(new AppUpdateProgress
            {
                Message = "Descargando actualizacion...",
                Progress = 0
            });

            await DownloadFileAsync(update, zipPath, progress, cancellationToken);

            progress?.Report(new AppUpdateProgress
            {
                Message = "Preparando archivos de instalacion...",
                Progress = 0.96
            });

            ZipFile.ExtractToDirectory(zipPath, payloadDirectory, overwriteFiles: true);
            await File.WriteAllTextAsync(scriptPath, BuildUpdaterScript(), cancellationToken);

            progress?.Report(new AppUpdateProgress
            {
                Message = "Actualizacion lista. Cerrando app para aplicar cambios...",
                Progress = 1
            });

            LaunchUpdater(scriptPath, payloadDirectory, installDirectory, executablePath);
        }

        private static async Task DownloadFileAsync(
            AppUpdateInfo update,
            string destinationPath,
            IProgress<AppUpdateProgress> progress,
            CancellationToken cancellationToken)
        {
            using var client = CreateHttpClient(TimeSpan.FromMinutes(10));
            using var response = await client.GetAsync(
                update.AssetDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? update.AssetSizeBytes;
            var receivedBytes = 0L;
            var buffer = new byte[81920];

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = File.Create(destinationPath);

            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                receivedBytes += read;

                var ratio = totalBytes > 0
                    ? Math.Clamp((double)receivedBytes / totalBytes, 0, 0.95)
                    : 0;

                progress?.Report(new AppUpdateProgress
                {
                    Message = totalBytes > 0
                        ? $"Descargando {FormatBytes(receivedBytes)} de {FormatBytes(totalBytes)}..."
                        : $"Descargando {FormatBytes(receivedBytes)}...",
                    Progress = ratio,
                    BytesReceived = receivedBytes,
                    TotalBytes = totalBytes
                });
            }
        }

        private static HttpClient CreateHttpClient(TimeSpan timeout)
        {
            var client = new HttpClient { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        private AppUpdateInfo BuildUnavailableUpdateResult() =>
            new()
            {
                CurrentVersion = CurrentVersion,
                IsUpdateAvailable = false,
                Message = "No se pueden buscar actualizaciones en este momento."
            };

        private static ReleaseAsset SelectReleaseAsset(JsonElement assets, string tag)
        {
            var expectedNames = CompatibleRuntimeIdentifiers
                .Select(rid => $"{AppName}-{tag}-{rid}.zip")
                .ToList();
            ReleaseAsset fallback = null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = ReadString(asset, "name");
                var downloadUrl = ReadString(asset, "browser_download_url");
                var sizeBytes = ReadLong(asset, "size");

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(downloadUrl))
                {
                    continue;
                }

                var releaseAsset = new ReleaseAsset(name, downloadUrl, sizeBytes);
                if (expectedNames.Any(expectedName => string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase)))
                {
                    return releaseAsset;
                }

                if (fallback is null &&
                    name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains(AppName, StringComparison.OrdinalIgnoreCase) &&
                    CompatibleRuntimeIdentifiers.Any(rid => name.Contains(rid, StringComparison.OrdinalIgnoreCase)))
                {
                    fallback = releaseAsset;
                }
            }

            return fallback;
        }

        private static void LaunchUpdater(
            string scriptPath,
            string payloadDirectory,
            string installDirectory,
            string executablePath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-STA");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("-AppPid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("-SourceDir");
            startInfo.ArgumentList.Add(payloadDirectory);
            startInfo.ArgumentList.Add("-InstallDir");
            startInfo.ArgumentList.Add(installDirectory);
            startInfo.ArgumentList.Add("-ExePath");
            startInfo.ArgumentList.Add(executablePath);

            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("No se pudo iniciar el aplicador de actualizacion.");
            }
        }

        private static string BuildUpdaterScript() =>
            """
            param(
                [int]$AppPid,
                [string]$SourceDir,
                [string]$InstallDir,
                [string]$ExePath
            )

            $ErrorActionPreference = 'Stop'
            $logDir = Join-Path $env:LOCALAPPDATA 'MyWorkStation\updates'
            New-Item -ItemType Directory -Force -Path $logDir | Out-Null
            $logPath = Join-Path $logDir 'updater.log'
            $script:UpdaterWindow = $null
            $script:UpdaterStatusLabel = $null
            $script:UpdaterDetailsLabel = $null
            $script:UpdaterLogBox = $null

            function Write-UpdaterLog {
                param([string]$Message)
                $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
                "$timestamp $Message" | Add-Content -Path $logPath
            }

            function Initialize-UpdaterWindow {
                try {
                    Add-Type -AssemblyName System.Windows.Forms
                    Add-Type -AssemblyName System.Drawing
                    [System.Windows.Forms.Application]::EnableVisualStyles()

                    $form = New-Object System.Windows.Forms.Form
                    $form.Text = 'Actualizando MyWorkStation'
                    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
                    $form.Size = New-Object System.Drawing.Size(520, 300)
                    $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
                    $form.MaximizeBox = $false
                    $form.MinimizeBox = $true
                    $form.TopMost = $true
                    $form.BackColor = [System.Drawing.Color]::FromArgb(244, 248, 255)

                    $title = New-Object System.Windows.Forms.Label
                    $title.Text = 'Actualizando MyWorkStation'
                    $title.AutoSize = $false
                    $title.Location = New-Object System.Drawing.Point(22, 18)
                    $title.Size = New-Object System.Drawing.Size(460, 28)
                    $title.Font = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Bold)
                    $title.ForeColor = [System.Drawing.Color]::FromArgb(16, 51, 118)

                    $status = New-Object System.Windows.Forms.Label
                    $status.Text = 'Preparando actualizacion...'
                    $status.AutoSize = $false
                    $status.Location = New-Object System.Drawing.Point(24, 58)
                    $status.Size = New-Object System.Drawing.Size(456, 24)
                    $status.Font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Regular)
                    $status.ForeColor = [System.Drawing.Color]::FromArgb(28, 45, 78)

                    $progress = New-Object System.Windows.Forms.ProgressBar
                    $progress.Location = New-Object System.Drawing.Point(24, 92)
                    $progress.Size = New-Object System.Drawing.Size(456, 18)
                    $progress.Style = [System.Windows.Forms.ProgressBarStyle]::Marquee
                    $progress.MarqueeAnimationSpeed = 35

                    $details = New-Object System.Windows.Forms.Label
                    $details.Text = 'No cierres esta ventana. La app se abrira de nuevo al terminar.'
                    $details.AutoSize = $false
                    $details.Location = New-Object System.Drawing.Point(24, 122)
                    $details.Size = New-Object System.Drawing.Size(456, 36)
                    $details.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Regular)
                    $details.ForeColor = [System.Drawing.Color]::FromArgb(84, 101, 132)

                    $logBox = New-Object System.Windows.Forms.TextBox
                    $logBox.Location = New-Object System.Drawing.Point(24, 166)
                    $logBox.Size = New-Object System.Drawing.Size(456, 72)
                    $logBox.Multiline = $true
                    $logBox.ReadOnly = $true
                    $logBox.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
                    $logBox.BackColor = [System.Drawing.Color]::White
                    $logBox.ForeColor = [System.Drawing.Color]::FromArgb(22, 38, 67)
                    $logBox.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
                    $logBox.Font = New-Object System.Drawing.Font('Consolas', 8.5, [System.Drawing.FontStyle]::Regular)

                    $form.Controls.Add($title)
                    $form.Controls.Add($status)
                    $form.Controls.Add($progress)
                    $form.Controls.Add($details)
                    $form.Controls.Add($logBox)

                    $script:UpdaterWindow = $form
                    $script:UpdaterStatusLabel = $status
                    $script:UpdaterDetailsLabel = $details
                    $script:UpdaterLogBox = $logBox

                    $form.Show()
                    $form.Activate()
                    [System.Windows.Forms.Application]::DoEvents()
                } catch {
                    Write-UpdaterLog "Progress window unavailable: $($_.Exception.Message)"
                }
            }

            function Set-UpdaterStatus {
                param(
                    [string]$Message,
                    [string]$Details = ''
                )

                if ($null -eq $script:UpdaterWindow) {
                    return
                }

                try {
                    $script:UpdaterStatusLabel.Text = $Message
                    if (-not [string]::IsNullOrWhiteSpace($Details)) {
                        $script:UpdaterDetailsLabel.Text = $Details
                    }

                    if ($null -ne $script:UpdaterLogBox -and -not [string]::IsNullOrWhiteSpace($Message)) {
                        $timestamp = Get-Date -Format 'HH:mm:ss'
                        $script:UpdaterLogBox.AppendText("$timestamp $Message`r`n")
                    }

                    $script:UpdaterWindow.Refresh()
                    [System.Windows.Forms.Application]::DoEvents()
                } catch {
                    Write-UpdaterLog "Progress window update failed: $($_.Exception.Message)"
                }
            }

            function Close-UpdaterWindow {
                param([int]$DelayMilliseconds = 0)

                if ($DelayMilliseconds -gt 0) {
                    Start-Sleep -Milliseconds $DelayMilliseconds
                }

                if ($null -eq $script:UpdaterWindow) {
                    return
                }

                try {
                    $script:UpdaterWindow.Close()
                    $script:UpdaterWindow.Dispose()
                    [System.Windows.Forms.Application]::DoEvents()
                } catch {
                    Write-UpdaterLog "Progress window close failed: $($_.Exception.Message)"
                }
            }

            try {
                Initialize-UpdaterWindow
                Set-UpdaterStatus 'Esperando a que MyWorkStation cierre...' 'Estamos preparando el reemplazo de archivos.'
                Write-UpdaterLog "Waiting for app pid $AppPid"

                if ($AppPid -gt 0) {
                    try {
                        Wait-Process -Id $AppPid -Timeout 90 -ErrorAction SilentlyContinue
                    } catch {
                        Write-UpdaterLog "Wait-Process warning: $($_.Exception.Message)"
                    }
                }

                Start-Sleep -Milliseconds 800
                Set-UpdaterStatus 'Validando paquete descargado...' 'Revisando carpeta temporal de actualizacion.'

                if (-not (Test-Path -LiteralPath $SourceDir)) {
                    throw "Payload folder not found: $SourceDir"
                }

                if (-not (Test-Path -LiteralPath $InstallDir)) {
                    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
                }

                Set-UpdaterStatus 'Aplicando actualizacion...' 'Copiando archivos nuevos al directorio de instalacion.'
                $copied = $false
                for ($i = 1; $i -le 60; $i++) {
                    try {
                        Copy-Item -Path (Join-Path $SourceDir '*') -Destination $InstallDir -Recurse -Force
                        $copied = $true
                        break
                    } catch {
                        Set-UpdaterStatus "Reintentando copia de archivos ($i/60)..." 'Algunos archivos siguen ocupados; esperamos un momento.'
                        Write-UpdaterLog "Copy attempt $i failed: $($_.Exception.Message)"
                        Start-Sleep -Milliseconds 500
                    }
                }

                if (-not $copied) {
                    throw "Could not copy update files into $InstallDir"
                }

                Write-UpdaterLog "Update files copied to $InstallDir"

                if (Test-Path -LiteralPath $ExePath) {
                    Set-UpdaterStatus 'Actualizacion aplicada.' 'Reiniciando MyWorkStation...'
                    Start-Process -FilePath $ExePath -WorkingDirectory $InstallDir
                    Write-UpdaterLog "App restarted: $ExePath"
                } else {
                    Set-UpdaterStatus 'Actualizacion aplicada, pero no se encontro el ejecutable.' 'Puedes abrir MyWorkStation manualmente.'
                    Write-UpdaterLog "Executable not found after copy: $ExePath"
                }

                Close-UpdaterWindow -DelayMilliseconds 1200
            } catch {
                Set-UpdaterStatus 'No se pudo aplicar la actualizacion.' "$($_.Exception.Message)"
                Write-UpdaterLog "Update failed: $($_.Exception.Message)"
                Close-UpdaterWindow -DelayMilliseconds 10000
            }
            """;

        private static bool IsNewerVersion(string candidate, string current)
        {
            if (!TryParseVersion(candidate, out var candidateVersion))
            {
                return false;
            }

            if (!TryParseVersion(current, out var currentVersion))
            {
                return true;
            }

            return candidateVersion.CompareTo(currentVersion) > 0;
        }

        private static bool TryParseVersion(string value, out Version version)
        {
            var normalized = NormalizeVersion(value);
            var metadataIndex = normalized.IndexOfAny(['-', '+']);
            if (metadataIndex >= 0)
            {
                normalized = normalized[..metadataIndex];
            }

            return Version.TryParse(normalized, out version);
        }

        private static string NormalizeVersion(string value)
        {
            value = (value ?? string.Empty).Trim();
            return value.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? value[1..]
                : value;
        }

        private static string ResolveCurrentVersion()
        {
            var assembly = typeof(GitHubReleaseAppUpdateService).Assembly;
            var info = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(info))
            {
                var plusIndex = info.IndexOf('+');
                return plusIndex > 0 ? info[..plusIndex] : info;
            }

            return assembly.GetName().Version?.ToString() ?? "0.0.0";
        }

        private static string ReadString(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                return property.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static long ReadLong(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out var property) &&
                property.TryGetInt64(out var value))
            {
                return value;
            }

            return 0;
        }

        private static string SanitizePathSegment(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? "latest" : value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '-');
            }

            return value;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            string[] units = ["B", "KB", "MB", "GB"];
            var size = (double)bytes;
            var unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:0.##} {units[unitIndex]}";
        }

        private sealed record ReleaseAsset(string Name, string DownloadUrl, long SizeBytes);
    }
}
