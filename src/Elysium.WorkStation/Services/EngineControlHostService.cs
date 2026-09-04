using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Elysium.WorkStation.Engine;
using Elysium.WorkStation.Engine.Contracts;
using Elysium.WorkStation.Engine.Transport;
using Elysium.WorkStation.Models;

namespace Elysium.WorkStation.Services
{
    public sealed class EngineControlHostService : IEngineControlHostService, IAsyncDisposable
    {
        private readonly IWorkspaceRuntimeService _runtimeService;
        private readonly ISettingsService _settingsService;
        private readonly IRoleService _roleService;
        private readonly IClipboardSyncService _clipboardSyncService;
        private readonly IFileTransferService _fileTransferService;
        private readonly IFolderSyncService _folderSyncService;
        private readonly IAppUpdateService _appUpdateService;
        private readonly IEngineHostProcessService _engineHostProcessService;
        private readonly ConcurrentDictionary<string, CliRemoteSession> _remoteSessions = new(StringComparer.Ordinal);
        private EnginePipeServer _server;
        private EnginePipeServer _publicFallbackServer;

        public bool IsRunning => _server?.IsRunning == true;
        public string AppBridgePipeName { get; } = Engine.EngineDefaults.CreateAppBridgePipeName(Environment.ProcessId);

        public EngineControlHostService(
            IWorkspaceRuntimeService runtimeService,
            ISettingsService settingsService,
            IRoleService roleService,
            IClipboardSyncService clipboardSyncService,
            IFileTransferService fileTransferService,
            IFolderSyncService folderSyncService,
            IAppUpdateService appUpdateService,
            IEngineHostProcessService engineHostProcessService)
        {
            _runtimeService = runtimeService;
            _settingsService = settingsService;
            _roleService = roleService;
            _clipboardSyncService = clipboardSyncService;
            _fileTransferService = fileTransferService;
            _folderSyncService = folderSyncService;
            _appUpdateService = appUpdateService;
            _engineHostProcessService = engineHostProcessService;
        }

        public Task StartAsync()
        {
            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            _server = new EnginePipeServer(HandleAsync, AppBridgePipeName);
            _server.Start();
            return Task.CompletedTask;
        }

        public Task StartPublicFallbackAsync()
        {
            if (_publicFallbackServer?.IsRunning == true)
            {
                return Task.CompletedTask;
            }

            _publicFallbackServer = new EnginePipeServer(HandleAsync, Engine.EngineDefaults.ResolvePipeName());
            _publicFallbackServer.Start();
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_publicFallbackServer is not null)
            {
                await _publicFallbackServer.DisposeAsync();
                _publicFallbackServer = null;
            }

            if (_server is null)
            {
                return;
            }

            await _server.DisposeAsync();
            _server = null;
        }

        private async Task<EngineCommandResponse> HandleAsync(EngineCommandRequest request, CancellationToken cancellationToken)
        {
            try
            {
                return request.Command.ToLowerInvariant() switch
                {
                    "status" => BuildStatusResponse(request),
                    "doctor" => BuildDoctorResponse(request),
                    "app.exit" => await ExitAppAsync(request),
                    "config.list" => BuildConfigResponse(request),
                    "config.get" => BuildConfigGetResponse(request),
                    "config.set" => BuildConfigSetResponse(request),
                    "sync.create" => await SyncCreateAsync(request),
                    "sync.invite" => await SyncInviteAsync(request),
                    "sync.invites" => await SyncInvitesAsync(request),
                    "sync.accept" => await SyncAcceptAsync(request),
                    "sync.reject" => await SyncRejectAsync(request),
                    "sync.delete" or "sync.remove" or "sync.rm" => await SyncDeleteAsync(request),
                    "sync.list" => await SyncListAsync(request),
                    "sync.status" => await SyncStatusAsync(request),
                    "sync.force" => await SyncForceAsync(request),
                    "sync.start" => await SyncSetContinuousAsync(request, true),
                    "sync.stop" => await SyncSetContinuousAsync(request, false),
                    "sync.switch-role" or "sync.switch" => await SyncSwitchRoleAsync(request),
                    "sync.logs" => await SyncLogsAsync(request),
                    "sync.summary" => await SyncSummaryAsync(request),
                    "sync.ignores.list" => await SyncIgnoresListAsync(request),
                    "sync.ignores.add" => await SyncIgnoresUpdateAsync(request, add: true),
                    "sync.ignores.remove" or "sync.ignores.rm" => await SyncIgnoresUpdateAsync(request, add: false),
                    "remote.start" => await RemoteStartAsync(request),
                    "remote.read" => RemoteRead(request),
                    "remote.exec" => await RemoteExecAsync(request, cancellationToken),
                    "remote.stop" or "remote.interrupt" => await RemoteStopAsync(request),
                    "git.status" or "git.pull" or "git.fetch" or "git.add" or "git.commit" or "git.push" or "git.branch" or "git.checkout" or "git.log" or "git.diff" => await GitAsync(request, cancellationToken),
                    "files.send" => await FilesSendAsync(request),
                    "update.check" => await UpdateCheckAsync(request, cancellationToken),
                    "update.install" => await UpdateInstallAsync(request, cancellationToken),
                    "workflow.pull-sync" or "workflow.pull-sync-send" => await WorkflowPullSyncAsync(request, cancellationToken),
                    "workflow.remote-build" => await WorkflowRemoteBuildAsync(request, cancellationToken),
                    _ => EngineCommandResponse.Fail(request.RequestId, $"Comando no soportado por el Engine: {request.Command}", 2)
                };
            }
            catch (Exception ex)
            {
                return EngineCommandResponse.Fail(request.RequestId, ex.Message, 1);
            }
        }

        private EngineCommandResponse BuildStatusResponse(EngineCommandRequest request)
        {
            var data = new
            {
                app = new
                {
                    name = EngineDefaults.AppName,
                    version = _appUpdateService.CurrentVersion,
                    pid = Environment.ProcessId,
                    baseDirectory = AppContext.BaseDirectory
                },
                engine = new
                {
                    pipe = EngineDefaults.ResolvePipeName(),
                    defaultPipe = EngineDefaults.PipeName,
                    appBridgePipe = AppBridgePipeName,
                    running = IsRunning,
                    runtimeStarted = _runtimeService.IsStarted,
                    externalHostRunning = _engineHostProcessService.IsRunning,
                    externalHostProcessId = _engineHostProcessService.ProcessId
                },
                role = _roleService.CurrentRole.ToString(),
                settings = new
                {
                    configured = _settingsService.IsConfigured,
                    serverUrl = _settingsService.ServerUrl,
                    hubUrl = _settingsService.HubUrl,
                    serverPort = _settingsService.ServerPort,
                    theme = _settingsService.ThemeMode,
                    sqliteDbPath = _settingsService.SqliteDbPath
                },
                services = new
                {
                    clipboardConnected = _clipboardSyncService.IsConnected,
                    fileTransferConnected = _fileTransferService.IsConnected,
                    folderSyncConnected = _folderSyncService.IsConnected,
                    folderSyncCount = _folderSyncService.Links.Count,
                    pendingInvites = _folderSyncService.PendingInvites.Count
                }
            };

            return EngineCommandResponse.Ok(request.RequestId, "Engine activo.", data);
        }

        private EngineCommandResponse BuildDoctorResponse(EngineCommandRequest request)
        {
            var issues = new List<string>();
            if (!_settingsService.IsConfigured)
            {
                issues.Add("La app no esta configurada.");
            }

            if (_roleService.CurrentRole == AppRole.Undetermined)
            {
                issues.Add("El rol de la instancia aun no esta definido.");
            }

            if (_runtimeService.IsStarted && !_folderSyncService.IsConnected)
            {
                issues.Add("Folder sync no esta conectado al hub.");
            }

            var cliPath = Path.Combine(AppContext.BaseDirectory, "mws.exe");
            if (!File.Exists(cliPath))
            {
                issues.Add("mws.exe no esta junto a la app; en release debe incluirse en el paquete.");
            }

            var hostPath = Path.Combine(AppContext.BaseDirectory, "mws-engine-host.exe");
            if (!File.Exists(hostPath))
            {
                issues.Add("mws-engine-host.exe no esta junto a la app; el CLI usara fallback si esta disponible, pero en release debe incluirse en el paquete.");
            }

            var data = new
            {
                healthy = issues.Count == 0,
                issues,
                status = BuildStatusObject()
            };

            return EngineCommandResponse.Ok(
                request.RequestId,
                issues.Count == 0 ? "Doctor OK." : "Doctor encontro observaciones.",
                data);
        }

        private object BuildStatusObject() => new
        {
            version = _appUpdateService.CurrentVersion,
            role = _roleService.CurrentRole.ToString(),
            configured = _settingsService.IsConfigured,
            runtimeStarted = _runtimeService.IsStarted,
            folderSyncConnected = _folderSyncService.IsConnected,
            syncLinks = _folderSyncService.Links.Count
        };

        private async Task<EngineCommandResponse> ExitAppAsync(EngineCommandRequest request)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                try { await _engineHostProcessService.StopAsync(); } catch { }
                try { await StopAsync(); } catch { }
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(() => Application.Current?.Quit());
                }
                catch
                {
                    // Environment.Exit below is the final safety net for CLI-driven exits.
                }

                await Task.Delay(500);
                Environment.Exit(0);
            });

            return EngineCommandResponse.Ok(request.RequestId, "Cerrando MyWorkStation...");
        }

        private EngineCommandResponse BuildConfigResponse(EngineCommandRequest request)
        {
            var data = new
            {
                serverUrl = _settingsService.ServerUrl,
                hubUrl = _settingsService.HubUrl,
                statusApiUrl = _settingsService.StatusApiUrl,
                serverPort = _settingsService.ServerPort,
                theme = _settingsService.ThemeMode,
                sqliteDbPath = _settingsService.SqliteDbPath,
                signalRReconnectMinutes = _settingsService.SignalRReconnectMinutes,
                fileRetentionHours = _settingsService.FileRetentionHours,
                clipboardRetentionHours = _settingsService.ClipboardRetentionHours,
                notificationRetentionHours = _settingsService.NotificationRetentionHours
            };

            return EngineCommandResponse.Ok(request.RequestId, "Configuracion actual.", data);
        }

        private EngineCommandResponse BuildConfigGetResponse(EngineCommandRequest request)
        {
            var key = GetRequired(request, "key");
            var value = key.ToLowerInvariant() switch
            {
                "server-url" or "serverurl" => _settingsService.ServerUrl,
                "hub-url" or "huburl" => _settingsService.HubUrl,
                "theme" => _settingsService.ThemeMode,
                "db-path" or "sqlite-db-path" => _settingsService.SqliteDbPath,
                "signalr-reconnect-minutes" => _settingsService.SignalRReconnectMinutes.ToString(),
                _ => throw new InvalidOperationException($"Configuracion no soportada: {key}")
            };

            return EngineCommandResponse.Ok(request.RequestId, string.Empty, new { key, value }, value + Environment.NewLine);
        }

        private EngineCommandResponse BuildConfigSetResponse(EngineCommandRequest request)
        {
            var key = GetRequired(request, "key");
            var value = GetRequired(request, "value");
            switch (key.ToLowerInvariant())
            {
                case "server-url":
                case "serverurl":
                    _settingsService.ServerUrl = value;
                    break;
                case "theme":
                    _settingsService.ThemeMode = value;
                    break;
                case "db-path":
                case "sqlite-db-path":
                    _settingsService.SqliteDbPath = value;
                    break;
                case "signalr-reconnect-minutes":
                    if (!int.TryParse(value, out var minutes))
                    {
                        throw new InvalidOperationException("signalr-reconnect-minutes debe ser numerico.");
                    }
                    _settingsService.SignalRReconnectMinutes = minutes;
                    break;
                default:
                    throw new InvalidOperationException($"Configuracion no soportada: {key}");
            }

            return EngineCommandResponse.Ok(request.RequestId, $"Configuracion actualizada: {key}", new { key, value });
        }

        private async Task<EngineCommandResponse> SyncCreateAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var path = request.GetArgument("path", request.GetArgument("folder", request.WorkingDirectory));
            var name = request.GetArgument("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
            }

            var description = request.GetArgument("description", request.GetArgument("desc"));
            var ignores = ParseListArgument(request.GetArgument("ignore", request.GetArgument("ignores")));
            var link = await _folderSyncService.CreateSyncRequestAsync(name, description, path, ignores);

            return EngineCommandResponse.Ok(
                request.RequestId,
                $"Sincronizacion creada: {link.Name}",
                ToSyncLinkDto(link),
                $"{link.Id} {link.SyncId}{Environment.NewLine}");
        }

        private async Task<EngineCommandResponse> SyncInviteAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var link = ResolveLink(request);
            await _folderSyncService.SendPairRequestAsync(link.Id);
            link = _folderSyncService.Links.FirstOrDefault(l => l.Id == link.Id) ?? link;

            return EngineCommandResponse.Ok(
                request.RequestId,
                $"Invitacion enviada: {link.Name}",
                ToSyncLinkDto(link));
        }

        private async Task<EngineCommandResponse> SyncInvitesAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var invites = _folderSyncService.PendingInvites
                .Select(ToSyncInviteDto)
                .ToList();

            var output = new StringBuilder();
            foreach (var invite in invites)
            {
                output.AppendLine($"{invite.InviteId}  {invite.SyncId}  {invite.Name}  {invite.RequesterName}");
            }

            return EngineCommandResponse.Ok(request.RequestId, $"{invites.Count} invitacion(es).", invites, output.ToString());
        }

        private async Task<EngineCommandResponse> SyncAcceptAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var path = request.GetArgument("path", request.GetArgument("folder"));
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Indica --path <carpeta-local> para aceptar la invitacion.");
            }

            var invite = ResolveInvite(request);
            var link = await _folderSyncService.AcceptInviteAsync(invite, path);

            return EngineCommandResponse.Ok(
                request.RequestId,
                $"Invitacion aceptada: {link.Name}",
                ToSyncLinkDto(link));
        }

        private async Task<EngineCommandResponse> SyncRejectAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var invite = ResolveInvite(request);
            await _folderSyncService.RejectInviteAsync(invite);

            return EngineCommandResponse.Ok(
                request.RequestId,
                $"Invitacion rechazada: {invite.Name}",
                ToSyncInviteDto(invite));
        }

        private async Task<EngineCommandResponse> SyncDeleteAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var link = ResolveLink(request);
            await _folderSyncService.DeleteSyncAsync(link.Id);

            return EngineCommandResponse.Ok(
                request.RequestId,
                $"Sincronizacion eliminada: {link.Name}",
                ToSyncLinkDto(link));
        }

        private async Task<EngineCommandResponse> SyncListAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var items = _folderSyncService.Links.Select(ToSyncLinkDto).ToList();
            var output = BuildSyncListOutput(items);
            return EngineCommandResponse.Ok(request.RequestId, $"{items.Count} sincronizacion(es).", items, output);
        }

        private async Task<EngineCommandResponse> SyncStatusAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var link = ResolveLink(request);
            return EngineCommandResponse.Ok(request.RequestId, link.Name, ToSyncLinkDto(link));
        }

        private async Task<EngineCommandResponse> SyncForceAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var link = ResolveLink(request);
            await _folderSyncService.ForceSyncAsync(link.Id);
            return EngineCommandResponse.Ok(request.RequestId, $"Sincronizacion forzada: {link.Name}", ToSyncLinkDto(link));
        }

        private async Task<EngineCommandResponse> SyncSetContinuousAsync(EngineCommandRequest request, bool enabled)
        {
            await EnsureRuntimeAsync();
            var link = ResolveLink(request);
            await _folderSyncService.SetContinuousAsync(link.Id, enabled);
            return EngineCommandResponse.Ok(request.RequestId, enabled ? "Sincronizacion continua iniciada." : "Sincronizacion continua detenida.", ToSyncLinkDto(link));
        }

        private async Task<EngineCommandResponse> SyncSwitchRoleAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var link = ResolveLink(request);
            await _folderSyncService.SwitchRoleAsync(link.Id);
            return EngineCommandResponse.Ok(request.RequestId, "Rol invertido.", ToSyncLinkDto(link));
        }

        private async Task<EngineCommandResponse> SyncLogsAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var link = ResolveLink(request);
            var tail = Math.Clamp(request.GetIntArgument("tail", 50), 1, 1000);
            var logs = _folderSyncService.GetLogs(link.SyncId)
                .OrderByDescending(l => l.Timestamp)
                .Take(tail)
                .OrderBy(l => l.Timestamp)
                .ToList();
            var output = new StringBuilder();
            foreach (var log in logs)
            {
                output.AppendLine($"{log.Timestamp:HH:mm:ss} [{(log.IsOutgoing ? "out" : "in ")}] {log.Action} {log.RelativePath} {log.Message}".TrimEnd());
            }

            return EngineCommandResponse.Ok(request.RequestId, $"{logs.Count} log(s).", logs, output.ToString());
        }

        private async Task<EngineCommandResponse> SyncSummaryAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var link = ResolveLink(request);
            var summary = _folderSyncService.GetSummary(link.SyncId).ToList();
            return EngineCommandResponse.Ok(request.RequestId, $"{summary.Count} archivo(s) en resumen.", summary);
        }

        private async Task<EngineCommandResponse> SyncIgnoresListAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var link = ResolveLink(request);
            var ignores = ReadIgnorePaths(link).ToList();
            return EngineCommandResponse.Ok(request.RequestId, $"{ignores.Count} ruta(s) ignorada(s).", ignores, string.Join(Environment.NewLine, ignores) + Environment.NewLine);
        }

        private async Task<EngineCommandResponse> SyncIgnoresUpdateAsync(EngineCommandRequest request, bool add)
        {
            await EnsureRuntimeAsync();
            var link = ResolveLink(request);
            var path = GetRequired(request, "path");
            var ignores = ReadIgnorePaths(link).ToList();
            if (add)
            {
                if (!ignores.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    ignores.Add(path);
                }
            }
            else
            {
                ignores.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
            }

            await _folderSyncService.UpdateIgnorePathsAsync(link.Id, ignores);
            return EngineCommandResponse.Ok(request.RequestId, add ? "Ruta ignorada agregada." : "Ruta ignorada removida.", ignores);
        }

        private async Task<EngineCommandResponse> RemoteStartAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            CleanupCompletedRemoteSessions();

            var link = ResolveLink(request);
            var commandText = request.GetArgument("commandText");
            if (string.IsNullOrWhiteSpace(commandText))
            {
                throw new InvalidOperationException("Usa: mws remote exec --sync-id <id> -- <comando>");
            }

            var sessionId = request.GetArgument("session");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = "mws-" + Guid.NewGuid().ToString("N");
            }

            if (_remoteSessions.ContainsKey(sessionId))
            {
                throw new InvalidOperationException($"Ya existe una sesion remota con id {sessionId}.");
            }

            var session = new CliRemoteSession(sessionId, link.Id, link.SyncId);
            EventHandler<RemoteTerminalOutputEventArgs> handler = null;
            handler = (_, e) =>
            {
                if (!string.Equals(e.SyncId, link.SyncId, StringComparison.Ordinal) ||
                    !string.Equals(e.SessionId, sessionId, StringComparison.Ordinal))
                {
                    return;
                }

                session.Add(e.Chunk, e.IsError, e.IsCompleted, e.ExitCode);
                if (e.IsCompleted && handler is not null)
                {
                    _folderSyncService.RemoteTerminalOutputReceived -= handler;
                    session.Detach = null;
                }
            };

            session.Detach = () => _folderSyncService.RemoteTerminalOutputReceived -= handler;
            _remoteSessions[sessionId] = session;
            _folderSyncService.RemoteTerminalOutputReceived += handler;

            try
            {
                await _folderSyncService.SendRemoteTerminalCommandAsync(link.Id, sessionId, commandText);
            }
            catch
            {
                session.Detach?.Invoke();
                _remoteSessions.TryRemove(sessionId, out _);
                throw;
            }

            return EngineCommandResponse.Ok(
                request.RequestId,
                $"Sesion remota iniciada: {sessionId}",
                new
                {
                    sessionId,
                    link.Id,
                    link.SyncId,
                    commandText
                });
        }

        private EngineCommandResponse RemoteRead(EngineCommandRequest request)
        {
            var sessionId = GetRequired(request, "session");
            var cursor = Math.Max(0, request.GetIntArgument("cursor", 0));
            if (!_remoteSessions.TryGetValue(sessionId, out var session))
            {
                return EngineCommandResponse.Fail(request.RequestId, $"Sesion remota no encontrada: {sessionId}", 404);
            }

            var read = session.Read(cursor);
            if (read.IsCompleted)
            {
                session.Detach?.Invoke();
                _remoteSessions.TryRemove(sessionId, out _);
            }

            return EngineCommandResponse.Ok(
                request.RequestId,
                string.Empty,
                new
                {
                    sessionId,
                    nextCursor = read.NextCursor,
                    isCompleted = read.IsCompleted,
                    exitCode = read.ExitCode,
                    chunks = read.Chunks
                });
        }

        private async Task<EngineCommandResponse> RemoteExecAsync(EngineCommandRequest request, CancellationToken cancellationToken)
        {
            await EnsureRuntimeAsync();
            var link = ResolveLink(request);
            var commandText = request.GetArgument("commandText");
            if (string.IsNullOrWhiteSpace(commandText))
            {
                throw new InvalidOperationException("Usa: mws remote exec --sync-id <id> -- <comando>");
            }

            var result = await ExecuteRemoteCommandAsync(link, commandText, request, cancellationToken);
            return new EngineCommandResponse
            {
                RequestId = request.RequestId,
                Success = result.ExitCode == 0,
                ExitCode = result.ExitCode,
                Message = result.TimedOut ? "Comando remoto agotado por timeout." : $"Comando remoto finalizado con exit {result.ExitCode}.",
                StandardOutput = result.StdOut,
                StandardError = result.StdErr,
                Data = JsonSerializer.SerializeToElement(new
                {
                    link.Id,
                    link.SyncId,
                    result.SessionId,
                    result.ExitCode,
                    result.TimedOut
                }, EngineJson.Options)
            };
        }

        private async Task<EngineCommandResponse> RemoteStopAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var sessionId = GetRequired(request, "session");
            if (_remoteSessions.TryGetValue(sessionId, out var session))
            {
                await _folderSyncService.SendRemoteTerminalInterruptAsync(session.LinkId, sessionId);
            }
            else
            {
                var link = ResolveLink(request);
                await _folderSyncService.SendRemoteTerminalInterruptAsync(link.Id, sessionId);
            }

            return EngineCommandResponse.Ok(request.RequestId, $"Interrupt enviado a sesion remota {sessionId}.");
        }

        private async Task<EngineCommandResponse> GitAsync(EngineCommandRequest request, CancellationToken cancellationToken)
        {
            var isRemote = request.GetBoolArgument("remote");
            var gitArguments = request.GetArgument("gitArguments");
            if (string.IsNullOrWhiteSpace(gitArguments))
            {
                gitArguments = request.Command["git.".Length..];
            }
            gitArguments = NormalizeGitArguments(gitArguments);

            if (isRemote)
            {
                await EnsureRuntimeAsync();
                var link = ResolveLink(request);
                var result = await ExecuteRemoteCommandAsync(link, "git " + gitArguments, request, cancellationToken);
                return new EngineCommandResponse
                {
                    RequestId = request.RequestId,
                    Success = result.ExitCode == 0,
                    ExitCode = result.ExitCode,
                    Message = $"Git remoto finalizado con exit {result.ExitCode}.",
                    StandardOutput = result.StdOut,
                    StandardError = result.StdErr
                };
            }

            var workingDirectory = request.GetArgument("workingDirectory", request.WorkingDirectory);
            var local = await ExecuteLocalProcessAsync("git", gitArguments, workingDirectory, request.GetIntArgument("timeout", 300), cancellationToken);
            return new EngineCommandResponse
            {
                RequestId = request.RequestId,
                Success = local.ExitCode == 0,
                ExitCode = local.ExitCode,
                Message = $"Git local finalizado con exit {local.ExitCode}.",
                StandardOutput = local.StdOut,
                StandardError = local.StdErr
            };
        }

        private async Task<EngineCommandResponse> FilesSendAsync(EngineCommandRequest request)
        {
            await EnsureRuntimeAsync();
            var paths = request.Tokens
                .SkipWhile(t => !string.Equals(t, "send", StringComparison.OrdinalIgnoreCase))
                .Skip(1)
                .Where(t => !t.StartsWith("--", StringComparison.Ordinal))
                .Select(Path.GetFullPath)
                .ToList();

            if (paths.Count == 0)
            {
                throw new InvalidOperationException("Usa: mws files send <ruta1> [ruta2]");
            }

            await _fileTransferService.SendFilesAsync(paths);
            return EngineCommandResponse.Ok(request.RequestId, $"{paths.Count} archivo(s) enviados.", paths);
        }

        private async Task<EngineCommandResponse> UpdateCheckAsync(EngineCommandRequest request, CancellationToken cancellationToken)
        {
            var update = await _appUpdateService.CheckLatestAsync(cancellationToken);
            return EngineCommandResponse.Ok(request.RequestId, update.Message, update);
        }

        private async Task<EngineCommandResponse> UpdateInstallAsync(EngineCommandRequest request, CancellationToken cancellationToken)
        {
            var update = await _appUpdateService.CheckLatestAsync(cancellationToken);
            if (!update.IsUpdateAvailable)
            {
                return EngineCommandResponse.Ok(request.RequestId, update.Message, update);
            }

            await _appUpdateService.DownloadAndApplyAsync(update, null, cancellationToken);
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Task.Delay(700);
                Application.Current?.Quit();
            });

            return EngineCommandResponse.Ok(request.RequestId, "Actualizacion descargada. Cerrando app para aplicar cambios.", update);
        }

        private async Task<EngineCommandResponse> WorkflowPullSyncAsync(EngineCommandRequest request, CancellationToken cancellationToken)
        {
            await EnsureRuntimeAsync();
            var link = ResolveLink(request);
            if (string.IsNullOrWhiteSpace(link.LocalFolderPath) || !Directory.Exists(link.LocalFolderPath))
            {
                throw new InvalidOperationException("La carpeta local de la sincronizacion no existe.");
            }

            var branch = request.GetArgument("branch");
            var output = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(branch))
            {
                var checkout = await ExecuteLocalProcessAsync("git", "checkout " + Quote(branch), link.LocalFolderPath, 300, cancellationToken);
                output.Append(checkout.StdOut);
                output.Append(checkout.StdErr);
                if (checkout.ExitCode != 0)
                {
                    return EngineCommandResponse.Fail(request.RequestId, "No se pudo cambiar de branch.", checkout.ExitCode, output.ToString());
                }
            }

            var pull = await ExecuteLocalProcessAsync("git", "pull", link.LocalFolderPath, 300, cancellationToken);
            output.Append(pull.StdOut);
            output.Append(pull.StdErr);
            if (pull.ExitCode != 0)
            {
                return EngineCommandResponse.Fail(request.RequestId, "Git pull fallo.", pull.ExitCode, output.ToString());
            }

            await _folderSyncService.ForceSyncAsync(link.Id);
            return EngineCommandResponse.Ok(request.RequestId, "Pull local y sincronizacion forzada completados.", ToSyncLinkDto(link), output.ToString());
        }

        private async Task<EngineCommandResponse> WorkflowRemoteBuildAsync(EngineCommandRequest request, CancellationToken cancellationToken)
        {
            await EnsureRuntimeAsync();
            var link = ResolveLink(request);
            var result = await ExecuteRemoteCommandAsync(link, "dotnet build", request, cancellationToken);
            return new EngineCommandResponse
            {
                RequestId = request.RequestId,
                Success = result.ExitCode == 0,
                ExitCode = result.ExitCode,
                Message = $"Build remoto finalizado con exit {result.ExitCode}.",
                StandardOutput = result.StdOut,
                StandardError = result.StdErr
            };
        }

        private async Task<RemoteExecutionResult> ExecuteRemoteCommandAsync(
            FolderSyncLink link,
            string commandText,
            EngineCommandRequest request,
            CancellationToken cancellationToken)
        {
            var sessionId = request.GetArgument("session", "mws-" + Guid.NewGuid().ToString("N"));
            var timeoutSeconds = Math.Clamp(request.GetIntArgument("timeout", EngineDefaults.DefaultTimeoutSeconds), 1, 3600);
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object sender, RemoteTerminalOutputEventArgs e)
            {
                if (!string.Equals(e.SyncId, link.SyncId, StringComparison.Ordinal) ||
                    !string.Equals(e.SessionId, sessionId, StringComparison.Ordinal))
                {
                    return;
                }

                if (!string.IsNullOrEmpty(e.Chunk))
                {
                    if (e.IsError)
                    {
                        stderr.AppendLine(e.Chunk);
                    }
                    else
                    {
                        stdout.AppendLine(e.Chunk);
                    }
                }

                if (e.IsCompleted)
                {
                    tcs.TrySetResult(e.ExitCode);
                }
            }

            _folderSyncService.RemoteTerminalOutputReceived += Handler;
            try
            {
                await _folderSyncService.SendRemoteTerminalCommandAsync(link.Id, sessionId, commandText);
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken));
                if (completed == tcs.Task)
                {
                    return new RemoteExecutionResult(sessionId, await tcs.Task, stdout.ToString(), stderr.ToString(), false);
                }

                try
                {
                    await _folderSyncService.SendRemoteTerminalInterruptAsync(link.Id, sessionId);
                }
                catch
                {
                    // Best effort timeout interrupt.
                }

                return new RemoteExecutionResult(sessionId, 124, stdout.ToString(), stderr.ToString(), true);
            }
            finally
            {
                _folderSyncService.RemoteTerminalOutputReceived -= Handler;
            }
        }

        private void CleanupCompletedRemoteSessions()
        {
            var threshold = DateTime.UtcNow.AddMinutes(-2);
            foreach (var pair in _remoteSessions.ToArray())
            {
                if (!pair.Value.IsCompleted || pair.Value.UpdatedAtUtc >= threshold)
                {
                    continue;
                }

                pair.Value.Detach?.Invoke();
                _remoteSessions.TryRemove(pair.Key, out _);
            }
        }

        private async Task<ProcessResult> ExecuteLocalProcessAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            if (!process.Start())
            {
                return new ProcessResult(1, string.Empty, "No se pudo iniciar proceso.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 3600)), cancellationToken);
            var completed = await Task.WhenAny(process.WaitForExitAsync(cancellationToken), timeoutTask);
            if (completed == timeoutTask)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new ProcessResult(124, stdout.ToString(), stderr + "Timeout de ejecucion local." + Environment.NewLine);
            }

            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }

        private async Task EnsureRuntimeAsync() => await _runtimeService.EnsureStartedAsync();

        private FolderSyncLink ResolveLink(EngineCommandRequest request)
        {
            var raw = request.GetArgument("sync-id", request.GetArgument("id"));
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("Indica --sync-id <id> o --id <id>.");
            }

            FolderSyncLink link = null;
            if (int.TryParse(raw, out var id))
            {
                link = _folderSyncService.Links.FirstOrDefault(l => l.Id == id);
            }

            link ??= _folderSyncService.Links.FirstOrDefault(l => string.Equals(l.SyncId, raw, StringComparison.OrdinalIgnoreCase));
            return link ?? throw new InvalidOperationException($"Sincronizacion no encontrada: {raw}");
        }

        private FolderSyncInvite ResolveInvite(EngineCommandRequest request)
        {
            var raw = request.GetArgument("sync-id", request.GetArgument("id", request.GetArgument("invite-id", request.GetArgument("name"))));
            FolderSyncInvite invite = null;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                invite = _folderSyncService.PendingInvites.FirstOrDefault(i =>
                    string.Equals(i.InviteId, raw, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.SyncId, raw, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.Name, raw, StringComparison.OrdinalIgnoreCase));
            }
            else if (_folderSyncService.PendingInvites.Count == 1)
            {
                invite = _folderSyncService.PendingInvites[0];
            }

            return invite ?? throw new InvalidOperationException("Invitacion no encontrada. Usa mws sync invites para ver las disponibles.");
        }

        private static SyncLinkDto ToSyncLinkDto(FolderSyncLink link) => new(
            link.Id,
            link.SyncId,
            link.Name,
            link.Description,
            link.LocalFolderPath,
            link.RemoteClientName,
            link.IsAccepted,
            link.IsPendingIncoming,
            link.IsPendingOutgoing,
            link.IsEmitter,
            link.RoleText,
            link.ContinuousSyncEnabled,
            link.SyncedVersionText,
            link.UpdatedAt);

        private static SyncInviteDto ToSyncInviteDto(FolderSyncInvite invite) => new(
            invite.InviteId,
            invite.SyncId,
            invite.Name,
            invite.Description,
            invite.RequesterClientId,
            invite.RequesterName,
            invite.RequesterFolderPath,
            invite.ReceivedAt);

        private static string BuildSyncListOutput(IEnumerable<SyncLinkDto> items)
        {
            var output = new StringBuilder();
            foreach (var item in items)
            {
                output.AppendLine($"{item.Id,3}  {item.Role,-8}  {(item.IsAccepted ? "OK" : "PEND"),-4}  {item.Name}  {item.LocalFolderPath}");
            }

            return output.ToString();
        }

        private static IReadOnlyList<string> ReadIgnorePaths(FolderSyncLink link)
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(link.IgnorePathsJson ?? "[]") ?? [];
            }
            catch
            {
                return [];
            }
        }

        private static IReadOnlyList<string> ParseListArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return [];
            }

            return value
                .Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string GetRequired(EngineCommandRequest request, string name)
        {
            var value = request.GetArgument(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Falta argumento requerido --{name}.");
            }

            return value;
        }

        private static string Quote(string value) => '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

        private static string NormalizeGitArguments(string arguments)
        {
            arguments = (arguments ?? string.Empty).Trim();
            if (arguments.StartsWith("branch create ", StringComparison.OrdinalIgnoreCase))
            {
                return "branch " + arguments["branch create ".Length..].Trim();
            }

            if (arguments.StartsWith("branch delete ", StringComparison.OrdinalIgnoreCase))
            {
                return "branch -d " + arguments["branch delete ".Length..].Trim();
            }

            if (arguments.StartsWith("checkout create ", StringComparison.OrdinalIgnoreCase))
            {
                return "checkout -b " + arguments["checkout create ".Length..].Trim();
            }

            return arguments;
        }

        public async ValueTask DisposeAsync() => await StopAsync();

        private sealed record RemoteExecutionResult(string SessionId, int ExitCode, string StdOut, string StdErr, bool TimedOut);
        private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
        private sealed record CliRemoteChunk(int Cursor, string Text, bool IsError);
        private sealed record CliRemoteReadResult(IReadOnlyList<CliRemoteChunk> Chunks, int NextCursor, bool IsCompleted, int ExitCode);
        private sealed class CliRemoteSession
        {
            private readonly object _gate = new();
            private readonly List<CliRemoteChunk> _chunks = [];
            private int _nextCursor;

            public string SessionId { get; }
            public int LinkId { get; }
            public string SyncId { get; }
            public bool IsCompleted { get; private set; }
            public int ExitCode { get; private set; }
            public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;
            public Action Detach { get; set; }

            public CliRemoteSession(string sessionId, int linkId, string syncId)
            {
                SessionId = sessionId;
                LinkId = linkId;
                SyncId = syncId;
            }

            public void Add(string text, bool isError, bool isCompleted, int exitCode)
            {
                lock (_gate)
                {
                    if (!string.IsNullOrEmpty(text))
                    {
                        _chunks.Add(new CliRemoteChunk(_nextCursor++, text, isError));
                    }

                    if (isCompleted)
                    {
                        IsCompleted = true;
                        ExitCode = exitCode;
                    }

                    UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            public CliRemoteReadResult Read(int cursor)
            {
                lock (_gate)
                {
                    var chunks = _chunks
                        .Where(chunk => chunk.Cursor >= cursor)
                        .ToList();

                    return new CliRemoteReadResult(chunks, _nextCursor, IsCompleted, ExitCode);
                }
            }
        }
        private sealed record SyncLinkDto(
            int Id,
            string SyncId,
            string Name,
            string Description,
            string LocalFolderPath,
            string RemoteClientName,
            bool IsAccepted,
            bool IsPendingIncoming,
            bool IsPendingOutgoing,
            bool IsEmitter,
            string Role,
            bool ContinuousSyncEnabled,
            string SyncedVersion,
            DateTime UpdatedAt);

        private sealed record SyncInviteDto(
            string InviteId,
            string SyncId,
            string Name,
            string Description,
            string RequesterClientId,
            string RequesterName,
            string RequesterFolderPath,
            DateTime ReceivedAt);
    }
}
