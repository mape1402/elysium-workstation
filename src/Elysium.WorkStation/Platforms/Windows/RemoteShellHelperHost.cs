#if WINDOWS
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Elysium.WorkStation.WinUI
{
    internal static class RemoteShellHelperHost
    {
        private const string DefaultPipeName = "Elysium.WorkStation.RemoteShell.Helper.v1";
        private const string HelperMutexName = "Global\\Elysium.WorkStation.RemoteShell.Helper.Singleton.v1";
        private static readonly ConcurrentDictionary<string, SessionShell> Sessions = new(StringComparer.Ordinal);
        private static DateTime _lastActivityUtc = DateTime.UtcNow;
        private static int? _ownerPid;
        private static string _pipeName = DefaultPipeName;

        public static bool IsHelperMode(string[] args) =>
            args.Any(a => string.Equals(a, "--remote-shell-helper", StringComparison.OrdinalIgnoreCase));

        public static int Run()
        {
            var args = Environment.GetCommandLineArgs();
            _ownerPid = TryGetOwnerPid(args);
            _pipeName = TryGetPipeName(args) ?? DefaultPipeName;
            TryStartOwnerWatchdog(_ownerPid);
            TryStartIdleWatchdog();

#if !DEBUG
            using var singletonMutex = new Mutex(initiallyOwned: true, name: $"{HelperMutexName}.{_pipeName}", createdNew: out var isFirstInstance);
            if (!isFirstInstance)
            {
                return 0;
            }
#endif

            while (true)
            {
                var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                server.WaitForConnection();
                _ = Task.Run(() => HandleClientAsync(server));
            }
        }

        private static async Task HandleClientAsync(NamedPipeServerStream server)
        {
            try
            {
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                HelperRequest request;
                try
                {
                    request = JsonSerializer.Deserialize<HelperRequest>(line) ?? new HelperRequest();
                }
                catch
                {
                    await WriteAsync(writer, new HelperResponse { Type = "error", Text = "Solicitud invalida.", ExitCode = 1 });
                    return;
                }

                _lastActivityUtc = DateTime.UtcNow;

                if (string.Equals(request.Type, "ping", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteAsync(writer, new HelperResponse { Type = "pong", ExitCode = 0 });
                    return;
                }

                if (string.Equals(request.Type, "shutdown", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteAsync(writer, new HelperResponse { Type = "bye", ExitCode = 0 });
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100);
                        Environment.Exit(0);
                    });
                    return;
                }

                if (string.Equals(request.Type, "interrupt", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(request.SessionKey))
                    {
                        await WriteAsync(writer, new HelperResponse { Type = "error", Text = "SessionKey vacio.", ExitCode = 2 });
                        return;
                    }

                    var interrupted = InterruptSession(request.SessionKey);
                    await WriteAsync(writer, new HelperResponse { Type = "done", ExitCode = interrupted ? 130 : 0 });
                    return;
                }

                if (!string.Equals(request.Type, "exec", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteAsync(writer, new HelperResponse { Type = "error", Text = "Tipo no soportado.", ExitCode = 2 });
                    return;
                }

                if (string.IsNullOrWhiteSpace(request.SessionKey))
                {
                    await WriteAsync(writer, new HelperResponse { Type = "error", Text = "SessionKey vacio.", ExitCode = 2 });
                    return;
                }

                var shell = GetOrCreateShell(request.SessionKey, request.WorkingDirectory);
                await shell.Gate.WaitAsync();
                Channel<HelperResponse> outputQueue = null;
                Task outputPump = null;
                try
                {
                    outputQueue = Channel.CreateUnbounded<HelperResponse>(
                        new UnboundedChannelOptions
                        {
                            SingleReader = true,
                            SingleWriter = false
                        });
                    outputPump = Task.Run(async () =>
                    {
                        await foreach (var response in outputQueue.Reader.ReadAllAsync())
                        {
                            await WriteAsync(writer, response);
                        }
                    });

                    var exitCode = await ExecuteInSessionAsync(shell, request.Command ?? string.Empty, async (txt, isErr) =>
                    {
                        if (shell.IsInterrupted) return;
                        outputQueue.Writer.TryWrite(new HelperResponse { Type = "line", Text = txt, IsError = isErr, ExitCode = 0 });
                        await Task.CompletedTask;
                    });

                    outputQueue.Writer.TryComplete();
                    await outputPump;
                    await WriteAsync(writer, new HelperResponse { Type = "done", ExitCode = exitCode });
                    _lastActivityUtc = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    try { outputQueue?.Writer.TryComplete(); } catch { }
                    if (outputPump is not null)
                    {
                        try { await outputPump; } catch { }
                    }

                    await WriteAsync(writer, new HelperResponse
                    {
                        Type = "error",
                        Text = ex.Message,
                        ExitCode = 1
                    });
                }
                finally
                {
                    shell.Gate.Release();
                }
            }
            catch
            {
                // no-op
            }
            finally
            {
                try { server.Dispose(); } catch { }
            }
        }

        private static SessionShell GetOrCreateShell(string sessionKey, string workingDirectory)
        {
            return Sessions.GetOrAdd(sessionKey, _ =>
            {
                var initialWorkingDirectory = Directory.Exists(workingDirectory)
                    ? workingDirectory
                    : Environment.CurrentDirectory;
                return new SessionShell(initialWorkingDirectory);
            });
        }

        private static async Task<int> ExecuteInSessionAsync(SessionShell session, string commandText, Func<string, bool, Task> onLineAsync)
        {
            var marker = "__CODEX_DONE__" + Guid.NewGuid().ToString("N") + ":";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                WorkingDirectory = Directory.Exists(session.WorkingDirectory)
                    ? session.WorkingDirectory
                    : Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(BuildEncodedPowerShellCommand(commandText, marker));
            psi.Environment["TERM"] = "xterm-256color";
            psi.Environment["CLICOLOR_FORCE"] = "1";
            psi.Environment["FORCE_COLOR"] = "1";

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var commandCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token);

            lock (session.ProcessGate)
            {
                session.ActiveProcess = process;
                session.ActiveCommandCancellation = commandCts;
                session.IsInterrupted = false;
            }

            int? markerExitCode = null;
            string markerWorkingDirectory = null;

            async Task PumpAsync(StreamReader reader, bool isError)
            {
                while (true)
                {
                    var line = await reader.ReadLineAsync();
                    if (line is null)
                    {
                        return;
                    }

                    if (!isError && TryReadPowerShellCommandMarker(line, marker, out var exitCode, out var workingDirectory))
                    {
                        markerExitCode = exitCode;
                        markerWorkingDirectory = workingDirectory;
                        continue;
                    }

                    await onLineAsync(line, isError);
                }
            }

            try
            {
                process.Start();
                var stdOutTask = PumpAsync(process.StandardOutput, isError: false);
                var stdErrTask = PumpAsync(process.StandardError, isError: true);

                var canceled = false;
                try
                {
                    await process.WaitForExitAsync(commandCts.Token);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                        // Best effort.
                    }

                    await process.WaitForExitAsync();
                }

                await Task.WhenAll(stdOutTask, stdErrTask);

                if (!string.IsNullOrWhiteSpace(markerWorkingDirectory) && Directory.Exists(markerWorkingDirectory))
                {
                    session.WorkingDirectory = markerWorkingDirectory;
                }

                if (timeoutCts.IsCancellationRequested)
                {
                    throw new TimeoutException("Timeout ejecutando comando.");
                }

                if (canceled || session.IsInterrupted)
                {
                    return 130;
                }

                return markerExitCode ?? process.ExitCode;
            }
            finally
            {
                lock (session.ProcessGate)
                {
                    if (ReferenceEquals(session.ActiveProcess, process))
                    {
                        session.ActiveProcess = null;
                    }

                    if (ReferenceEquals(session.ActiveCommandCancellation, commandCts))
                    {
                        session.ActiveCommandCancellation = null;
                    }
                }

                process.Dispose();
            }
        }

        private static string BuildEncodedPowerShellCommand(string commandText, string marker)
        {
            var commandPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(commandText ?? string.Empty));
            var script = new StringBuilder();
            script.AppendLine("[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)");
            script.AppendLine("$OutputEncoding = [System.Text.UTF8Encoding]::new($false)");
            script.AppendLine("$ProgressPreference = 'SilentlyContinue'");
            script.AppendLine($"$__codex_command = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{commandPayload}'))");
            script.AppendLine("$__codex_exit = 0");
            script.AppendLine("try {");
            script.AppendLine("  Invoke-Expression $__codex_command");
            script.AppendLine("} catch {");
            script.AppendLine("  Write-Error $_");
            script.AppendLine("  $__codex_exit = 1");
            script.AppendLine("}");
            script.AppendLine("if ($__codex_exit -eq 0) {");
            script.AppendLine("  if ($LASTEXITCODE -ne $null) { $__codex_exit = [int]$LASTEXITCODE }");
            script.AppendLine("}");
            script.AppendLine("$__codex_pwd = ''");
            script.AppendLine("try { $__codex_pwd = (Get-Location).ProviderPath } catch { $__codex_pwd = '' }");
            script.AppendLine("$__codex_pwd_payload = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($__codex_pwd))");
            script.AppendLine($"Write-Output \"{marker}$($__codex_exit)|$__codex_pwd_payload\"");
            script.AppendLine("exit $__codex_exit");

            return Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));
        }

        private static bool TryReadPowerShellCommandMarker(
            string line,
            string marker,
            out int exitCode,
            out string workingDirectory)
        {
            exitCode = 1;
            workingDirectory = string.Empty;
            if (string.IsNullOrEmpty(line) || !line.StartsWith(marker, StringComparison.Ordinal))
            {
                return false;
            }

            var payload = line[marker.Length..];
            var parts = payload.Split('|', 2);
            if (!int.TryParse(parts[0], out exitCode))
            {
                exitCode = 1;
            }

            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                try
                {
                    workingDirectory = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                }
                catch
                {
                    workingDirectory = string.Empty;
                }
            }

            return true;
        }

        private static Task WriteAsync(StreamWriter writer, HelperResponse response)
        {
            var payload = JsonSerializer.Serialize(response);
            return writer.WriteLineAsync(payload);
        }

        private static bool InterruptSession(string sessionKey)
        {
            if (!Sessions.TryGetValue(sessionKey, out var shell))
            {
                return false;
            }

            shell.IsInterrupted = true;
            try
            {
                Process activeProcess;
                CancellationTokenSource activeCommandCancellation;
                lock (shell.ProcessGate)
                {
                    activeProcess = shell.ActiveProcess;
                    activeCommandCancellation = shell.ActiveCommandCancellation;
                }

                activeCommandCancellation?.Cancel();
                if (activeProcess is not null && !activeProcess.HasExited)
                {
                    activeProcess.Kill(entireProcessTree: true);
                    _lastActivityUtc = DateTime.UtcNow;
                    return true;
                }

                _lastActivityUtc = DateTime.UtcNow;
                return activeProcess is not null;
            }
            catch
            {
                // Best effort.
            }

            return false;
        }

        private static int? TryGetOwnerPid(string[] args)
        {
            if (args is null || args.Length == 0)
            {
                return null;
            }

            for (var i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--owner-pid", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    return null;
                }

                if (int.TryParse(args[i + 1], out var pid) && pid > 0)
                {
                    return pid;
                }
            }

            return null;
        }

        private static string TryGetPipeName(string[] args)
        {
            if (args is null || args.Length == 0)
            {
                return null;
            }

            for (var i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--pipe-name", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    return null;
                }

                var value = args[i + 1]?.Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return null;
        }

        private static void TryStartOwnerWatchdog(int? ownerPid)
        {
            if (!ownerPid.HasValue)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    using var owner = Process.GetProcessById(ownerPid.Value);
                    owner.WaitForExit();
                }
                catch
                {
                    // Si no existe/ya salio, continuar cierre del helper.
                }
                finally
                {
                    try { Environment.Exit(0); } catch { }
                }
            });
        }

        private static void TryStartIdleWatchdog()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));
                    if (DateTime.UtcNow - _lastActivityUtc < TimeSpan.FromMinutes(3))
                    {
                        continue;
                    }

                    if (Sessions.Values.Any(session =>
                    {
                        lock (session.ProcessGate)
                        {
                            return session.ActiveProcess is not null && !session.ActiveProcess.HasExited;
                        }
                    }))
                    {
                        continue;
                    }

                    Sessions.Clear();
                    try { Environment.Exit(0); } catch { }
                }
            });
        }

        private sealed class HelperRequest
        {
            public string Type { get; set; } = string.Empty;
            public string SessionKey { get; set; } = string.Empty;
            public string WorkingDirectory { get; set; } = string.Empty;
            public string Command { get; set; } = string.Empty;
        }

        private sealed class HelperResponse
        {
            public string Type { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
            public bool IsError { get; set; }
            public int ExitCode { get; set; }
        }

        private sealed class SessionShell
        {
            public object ProcessGate { get; } = new();
            public Process ActiveProcess { get; set; }
            public CancellationTokenSource ActiveCommandCancellation { get; set; }
            public string WorkingDirectory { get; set; }
            public SemaphoreSlim Gate { get; } = new(1, 1);
            public volatile bool IsInterrupted;

            public SessionShell(string workingDirectory)
            {
                WorkingDirectory = workingDirectory;
            }
        }
    }
}
#endif
