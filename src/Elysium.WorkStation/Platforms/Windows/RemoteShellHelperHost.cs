#if WINDOWS
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

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
                using var server = new NamedPipeServerStream(
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
                try
                {
                    var exitCode = await ExecuteInSessionAsync(shell, request.Command ?? string.Empty, async (txt, isErr) =>
                    {
                    await WriteAsync(writer, new HelperResponse { Type = "line", Text = txt, IsError = isErr, ExitCode = 0 });
                });

                await WriteAsync(writer, new HelperResponse { Type = "done", ExitCode = exitCode });
                _lastActivityUtc = DateTime.UtcNow;
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
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-NoLogo");
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-ExecutionPolicy");
                psi.ArgumentList.Add("Bypass");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add("-");

                var process = new Process { StartInfo = psi };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return new SessionShell(process);
            });
        }

        private static async Task<int> ExecuteInSessionAsync(SessionShell session, string commandText, Func<string, bool, Task> onLineAsync)
        {
            if (session.Process.HasExited)
            {
                throw new InvalidOperationException("Shell helper finalizado.");
            }

            var marker = "__CODEX_DONE__" + Guid.NewGuid().ToString("N") + ":";
            var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            DataReceivedEventHandler outHandler = (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data))
                {
                    return;
                }

                if (e.Data.StartsWith(marker, StringComparison.Ordinal))
                {
                    var raw = e.Data[marker.Length..].Trim();
                    done.TrySetResult(int.TryParse(raw, out var parsed) ? parsed : 1);
                    return;
                }

                _ = onLineAsync(e.Data, false);
            };
            DataReceivedEventHandler errHandler = (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _ = onLineAsync(e.Data, true);
                }
            };

            session.Process.OutputDataReceived += outHandler;
            session.Process.ErrorDataReceived += errHandler;
            try
            {
                foreach (var line in BuildWrappedLines(commandText, marker))
                {
                    await session.Process.StandardInput.WriteLineAsync(line);
                }
                await session.Process.StandardInput.FlushAsync();

                var completed = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(5)));
                if (completed != done.Task)
                {
                    throw new TimeoutException("Timeout ejecutando comando.");
                }

                return await done.Task;
            }
            finally
            {
                session.Process.OutputDataReceived -= outHandler;
                session.Process.ErrorDataReceived -= errHandler;
            }
        }

        private static IEnumerable<string> BuildWrappedLines(string commandText, string marker)
        {
            var script = new StringBuilder();
            script.AppendLine("$__codex_exit = 0");
            script.AppendLine("try {");
            script.AppendLine(commandText ?? string.Empty);
            script.AppendLine("} catch {");
            script.AppendLine("  Write-Error $_");
            script.AppendLine("  $__codex_exit = 1");
            script.AppendLine("}");
            script.AppendLine("if ($__codex_exit -eq 0) {");
            script.AppendLine("  if ($LASTEXITCODE -ne $null) { $__codex_exit = [int]$LASTEXITCODE }");
            script.AppendLine("}");
            script.AppendLine($"Write-Output \"{marker}$($__codex_exit)\"");
            return script.ToString().Split(Environment.NewLine);
        }

        private static Task WriteAsync(StreamWriter writer, HelperResponse response)
        {
            var payload = JsonSerializer.Serialize(response);
            return writer.WriteLineAsync(payload);
        }

        private static bool InterruptSession(string sessionKey)
        {
            if (!Sessions.TryRemove(sessionKey, out var shell))
            {
                return false;
            }

            try
            {
                if (!shell.Process.HasExited)
                {
                    shell.Process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort.
            }

            _lastActivityUtc = DateTime.UtcNow;
            return true;
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

                    if (Sessions.Count > 0)
                    {
                        continue;
                    }

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
            public Process Process { get; }
            public SemaphoreSlim Gate { get; } = new(1, 1);

            public SessionShell(Process process)
            {
                Process = process;
            }
        }
    }
}
#endif
