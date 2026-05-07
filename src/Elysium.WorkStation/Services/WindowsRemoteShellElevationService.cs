#if WINDOWS
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Elysium.WorkStation.Services
{
    public sealed class WindowsRemoteShellElevationService : IRemoteShellElevationService
    {
        private const string PipeBaseName = "Elysium.WorkStation.RemoteShell.Helper.v1";
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
        private readonly SemaphoreSlim _startGate = new(1, 1);
        private readonly string _pipeName = $"{PipeBaseName}.{Environment.ProcessId}";

        public async Task<bool> EnsureHelperStartedAsync(bool interactivePrompt, CancellationToken cancellationToken = default)
        {
            if (await IsHelperAvailableAsync(cancellationToken))
            {
                return true;
            }

            await _startGate.WaitAsync(cancellationToken);
            try
            {
                if (await IsHelperAvailableAsync(cancellationToken))
                {
                    return true;
                }

                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    return false;
                }

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        Verb = interactivePrompt ? "runas" : string.Empty,
                        Arguments = $"--remote-shell-helper --owner-pid {Environment.ProcessId} --pipe-name {_pipeName}"
                    };
                    Process.Start(psi);
                }
                catch
                {
                    return false;
                }

                var start = DateTime.UtcNow;
                while (DateTime.UtcNow - start < DefaultTimeout)
                {
                    if (await IsHelperAvailableAsync(cancellationToken))
                    {
                        return true;
                    }

                    await Task.Delay(250, cancellationToken);
                }

                return false;
            }
            finally
            {
                _startGate.Release();
            }
        }

        public async Task<bool> IsHelperAvailableAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var client = CreateClientPipe(_pipeName);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMilliseconds(700));
                await client.ConnectAsync(cts.Token);

                await SendRequestAsync(client, new HelperRequest
                {
                    Type = "ping",
                    SessionKey = string.Empty,
                    WorkingDirectory = string.Empty,
                    Command = string.Empty
                }, cts.Token);

                var response = await ReadResponseAsync(client, cts.Token);
                return response is not null && response.Type == "pong";
            }
            catch
            {
                return false;
            }
        }

        public async Task StopHelperAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var client = CreateClientPipe(_pipeName);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                await client.ConnectAsync(cts.Token);
                await SendRequestAsync(client, new HelperRequest
                {
                    Type = "shutdown",
                    SessionKey = string.Empty,
                    WorkingDirectory = string.Empty,
                    Command = string.Empty
                }, cts.Token);
            }
            catch
            {
                // Best effort.
            }
        }

        public async Task<int?> ExecuteInHelperSessionAsync(
            string sessionKey,
            string workingDirectory,
            string commandText,
            Func<string, bool, Task> onLineAsync,
            CancellationToken cancellationToken = default)
        {
            using var client = CreateClientPipe(_pipeName);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(6));
            await client.ConnectAsync(cts.Token);

            await SendRequestAsync(client, new HelperRequest
            {
                Type = "exec",
                SessionKey = sessionKey ?? string.Empty,
                WorkingDirectory = workingDirectory ?? string.Empty,
                Command = commandText ?? string.Empty
            }, cts.Token);

            while (true)
            {
                var message = await ReadResponseAsync(client, cts.Token);
                if (message is null)
                {
                    return 1;
                }

                if (message.Type == "line")
                {
                    await onLineAsync(message.Text ?? string.Empty, message.IsError);
                    continue;
                }

                if (message.Type == "done")
                {
                    return message.ExitCode;
                }

                if (message.Type == "error")
                {
                    await onLineAsync(message.Text ?? "Error en helper remoto.", true);
                    return message.ExitCode == 0 ? 1 : message.ExitCode;
                }
            }
        }

        private static NamedPipeClientStream CreateClientPipe(string pipeName) =>
            new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        private static async Task SendRequestAsync(
            NamedPipeClientStream client,
            HelperRequest request,
            CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(request) + "\n";
            var bytes = Encoding.UTF8.GetBytes(payload);
            await client.WriteAsync(bytes, cancellationToken);
            await client.FlushAsync(cancellationToken);
        }

        private static async Task<HelperResponse> ReadResponseAsync(
            NamedPipeClientStream client,
            CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<HelperResponse>(line);
            }
            catch
            {
                return new HelperResponse { Type = "error", Text = "Respuesta invalida del helper.", ExitCode = 1 };
            }
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
    }
}
#endif
