using Microsoft.Win32;
using System.Diagnostics;

namespace Elysium.WorkStation.Services
{
    public class StartupService : IStartupService
    {
        private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ElysiumWorkStation";

        private static string ExePath =>
            Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

        public bool IsEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
                return key?.GetValue(AppName) is string val
                    && string.Equals(val, ExePath, StringComparison.OrdinalIgnoreCase);
            }
        }

        public void Enable()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            key?.SetValue(AppName, ExePath);
        }

        public void Disable()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            key?.DeleteValue(AppName, false);
        }
    }
}
