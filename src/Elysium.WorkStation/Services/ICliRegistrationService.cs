namespace Elysium.WorkStation.Services
{
    public sealed class CliRegistrationStatus
    {
        public bool IsSupported { get; init; }
        public bool CliExists { get; init; }
        public bool IsRegistered { get; init; }
        public string InstallDirectory { get; init; } = string.Empty;
        public string CliPath { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    public interface ICliRegistrationService
    {
        CliRegistrationStatus GetStatus();
        Task<CliRegistrationStatus> RegisterAsync();
        Task<CliRegistrationStatus> UnregisterAsync();
    }

    public sealed class CliRegistrationService : ICliRegistrationService
    {
        private static readonly SemaphoreSlim RegistrationLock = new(1, 1);

        public CliRegistrationStatus GetStatus()
        {
            if (!OperatingSystem.IsWindows())
            {
                return new CliRegistrationStatus
                {
                    IsSupported = false,
                    Message = "El registro automatico del CLI solo esta disponible en Windows."
                };
            }

            var installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var cliPath = Path.Combine(installDirectory, "mws.exe");
            var cliExists = File.Exists(cliPath);
            var registered = IsPathRegistered(installDirectory);

            return new CliRegistrationStatus
            {
                IsSupported = true,
                CliExists = cliExists,
                IsRegistered = registered,
                InstallDirectory = installDirectory,
                CliPath = cliPath,
                Message = cliExists
                    ? registered
                        ? "CLI registrado en PATH."
                        : "CLI disponible, pendiente de registrar en PATH."
                    : "No encontre mws.exe junto a la app. En DEBUG compila el CLI o usa un release que lo incluya."
            };
        }

        public async Task<CliRegistrationStatus> RegisterAsync()
        {
            await RegistrationLock.WaitAsync();
            try
            {
                return await Task.Run(() =>
                {
                    var status = GetStatus();
                    if (!status.IsSupported || !status.CliExists)
                    {
                        return status;
                    }

                    if (!status.IsRegistered)
                    {
                        var entries = GetUserPathEntries().ToList();
                        entries.Add(status.InstallDirectory);
                        Environment.SetEnvironmentVariable("Path", string.Join(';', entries), EnvironmentVariableTarget.User);
                        BroadcastEnvironmentChanged();
                    }

                    return GetStatus();
                });
            }
            finally
            {
                RegistrationLock.Release();
            }
        }

        public async Task<CliRegistrationStatus> UnregisterAsync()
        {
            await RegistrationLock.WaitAsync();
            try
            {
                return await Task.Run(() =>
                {
                    var status = GetStatus();
                    if (!status.IsSupported)
                    {
                        return status;
                    }

                    var entries = GetUserPathEntries()
                        .Where(entry => !PathsEqual(entry, status.InstallDirectory))
                        .ToList();
                    Environment.SetEnvironmentVariable("Path", string.Join(';', entries), EnvironmentVariableTarget.User);
                    BroadcastEnvironmentChanged();
                    return GetStatus();
                });
            }
            finally
            {
                RegistrationLock.Release();
            }
        }

        private static IEnumerable<string> GetUserPathEntries()
        {
            var path = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
            return path
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsPathRegistered(string installDirectory) =>
            GetUserPathEntries().Any(entry => PathsEqual(entry, installDirectory));

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void BroadcastEnvironmentChanged()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                NativeMethods.SendNotifyMessage(
                    new IntPtr(0xffff),
                    0x001A,
                    IntPtr.Zero,
                    "Environment");
            }
            catch
            {
                // PATH queda actualizado aunque no se pueda notificar a ventanas abiertas.
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
            public static extern bool SendNotifyMessage(
                IntPtr hWnd,
                uint msg,
                IntPtr wParam,
                string lParam);
        }
    }
}
