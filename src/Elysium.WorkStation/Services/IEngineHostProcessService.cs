namespace Elysium.WorkStation.Services
{
    public interface IEngineHostProcessService
    {
        bool IsRunning { get; }
        int? ProcessId { get; }
        Task<bool> StartAsync(string appBridgePipeName);
        Task StopAsync();
    }

    public sealed class EngineHostProcessService : IEngineHostProcessService
    {
        private System.Diagnostics.Process _process;

        public bool IsRunning => _process is { HasExited: false };
        public int? ProcessId => IsRunning ? _process.Id : null;

        public Task<bool> StartAsync(string appBridgePipeName)
        {
            if (IsRunning)
            {
                return Task.FromResult(true);
            }

            var hostPath = Path.Combine(AppContext.BaseDirectory, "mws-engine-host.exe");
            if (!File.Exists(hostPath))
            {
                return Task.FromResult(false);
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = hostPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            startInfo.ArgumentList.Add("--app-pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("--app-pipe");
            startInfo.ArgumentList.Add(appBridgePipeName);
            startInfo.ArgumentList.Add("--public-pipe");
            startInfo.ArgumentList.Add(Elysium.WorkStation.Engine.EngineDefaults.ResolvePipeName());

            _process = System.Diagnostics.Process.Start(startInfo);
            return Task.FromResult(IsRunning);
        }

        public Task StopAsync()
        {
            if (_process is null)
            {
                return Task.CompletedTask;
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
            }
            catch
            {
                // Best effort shutdown.
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }

            return Task.CompletedTask;
        }
    }
}
