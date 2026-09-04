using System.Reflection;
using System.Text.Json;
using Elysium.WorkStation.Engine;
using Elysium.WorkStation.Engine.Aliases;
using Elysium.WorkStation.Engine.Contracts;
using Elysium.WorkStation.Engine.Transport;

namespace Elysium.WorkStation.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var aliasStore = CliAliasCatalog.LoadOrCreate();
        args = CliAliasCatalog.ExpandIfAlias(args, aliasStore);

        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        var wantsJson = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
        args = args.Where(a => !string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase)).ToArray();

        try
        {
            if (string.Equals(args[0], "version", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase))
            {
                var version = ResolveVersion();
                if (wantsJson)
                {
                    WriteJson(new { version });
                }
                else
                {
                    Console.WriteLine(version);
                }

                return 0;
            }

            if (string.Equals(args[0], "alias", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[0], "aliases", StringComparison.OrdinalIgnoreCase))
            {
                return HandleAliasCommand(args.Skip(1).ToArray(), aliasStore, wantsJson);
            }

            if (args.Length >= 2 &&
                string.Equals(args[0], "remote", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(args[1], "shell", StringComparison.OrdinalIgnoreCase))
            {
                return await RunRemoteShellAsync(args, wantsJson);
            }

            if (args.Length >= 2 &&
                string.Equals(args[0], "remote", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(args[1], "exec", StringComparison.OrdinalIgnoreCase))
            {
                return await RunRemoteExecAsync(args, wantsJson);
            }

            var request = BuildEngineRequest(args);
            var timeoutSeconds = request.GetIntArgument("timeout", EngineDefaults.DefaultTimeoutSeconds);
            timeoutSeconds = Math.Clamp(timeoutSeconds, 1, 3600);

            var client = new EnginePipeClient();
            var response = await client.SendAsync(request, TimeSpan.FromSeconds(timeoutSeconds + 5));
            PrintResponse(response, wantsJson);
            return response.ExitCode;
        }
        catch (Exception ex)
        {
            if (wantsJson)
            {
                WriteJson(new { success = false, exitCode = 1, message = ex.Message });
            }
            else
            {
                Console.Error.WriteLine(ex.Message);
            }

            return 1;
        }
    }

    private static async Task<int> RunRemoteExecAsync(string[] args, bool wantsJson)
    {
        var parsed = ParsedArgs.Parse(args);
        var syncId = ResolveSyncId(parsed);
        if (string.IsNullOrWhiteSpace(syncId))
        {
            Console.Error.WriteLine("Uso: mws remote exec --sync-id <id> -- <comando>");
            return 2;
        }

        var commandText = string.Join(' ', parsed.CommandAfterSeparator).Trim();
        if (string.IsNullOrWhiteSpace(commandText))
        {
            Console.Error.WriteLine("Uso: mws remote exec --sync-id <id> -- <comando>");
            return 2;
        }

        var timeoutSeconds = ResolveTimeout(parsed);
        var sessionId = parsed.Options.TryGetValue("session", out var rawSession) && !string.IsNullOrWhiteSpace(rawSession)
            ? rawSession
            : "mws-" + Guid.NewGuid().ToString("N");

        return await ExecuteRemoteStreamingAsync(syncId, sessionId, commandText, timeoutSeconds, wantsJson);
    }

    private static async Task<int> RunRemoteShellAsync(string[] args, bool wantsJson)
    {
        if (wantsJson)
        {
            Console.Error.WriteLine("remote shell es interactivo; usa remote exec con --json para automatizacion.");
            return 2;
        }

        var parsed = ParsedArgs.Parse(args);
        var syncId = ResolveSyncId(parsed);

        if (string.IsNullOrWhiteSpace(syncId))
        {
            Console.Error.WriteLine("Uso: mws remote shell --sync-id <id>");
            return 2;
        }

        var timeoutSeconds = ResolveTimeout(parsed);
        var sessionId = parsed.Options.TryGetValue("session", out var rawSession) && !string.IsNullOrWhiteSpace(rawSession)
            ? rawSession
            : "mws-shell-" + Guid.NewGuid().ToString("N");

        Console.WriteLine($"MyWorkStation remote shell");
        Console.WriteLine($"sync-id: {syncId}");
        Console.WriteLine($"session: {sessionId}");
        Console.WriteLine("Escribe exit para salir. Ctrl+C intenta detener el comando remoto en curso.");

        while (true)
        {
            Console.Write("remote> ");
            var commandText = Console.ReadLine();
            if (commandText is null)
            {
                return 0;
            }

            commandText = commandText.Trim();
            if (string.IsNullOrWhiteSpace(commandText))
            {
                continue;
            }

            if (commandText.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (commandText.Equals("cls", StringComparison.OrdinalIgnoreCase) ||
                commandText.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                Console.Clear();
                continue;
            }

            await ExecuteRemoteStreamingAsync(syncId, sessionId, commandText, timeoutSeconds, wantsJson: false);
        }
    }

    private static async Task<int> ExecuteRemoteStreamingAsync(
        string syncId,
        string sessionId,
        string commandText,
        int timeoutSeconds,
        bool wantsJson)
    {
        var client = new EnginePipeClient();
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        var cursor = 0;
        var exitCode = 1;
        var completed = false;
        var timedOut = false;
        var stopRequested = false;

        var startRequest = new EngineCommandRequest
        {
            Command = "remote.start",
            Arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sync-id"] = syncId,
                ["session"] = sessionId,
                ["commandText"] = commandText,
                ["timeout"] = timeoutSeconds.ToString()
            },
            WorkingDirectory = Environment.CurrentDirectory
        };

        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            if (stopRequested)
            {
                return;
            }

            stopRequested = true;
            _ = Task.Run(async () => await SendRemoteStopAsync(client, syncId, sessionId));
        };

        var start = await client.SendAsync(startRequest, TimeSpan.FromSeconds(15));
        if (!start.Success)
        {
            PrintResponse(start, wantsJson);
            return start.ExitCode;
        }

        Console.CancelKeyPress += cancelHandler;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (!completed)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    timedOut = true;
                    await SendRemoteStopAsync(client, syncId, sessionId);
                    exitCode = 124;
                    break;
                }

                var readRequest = new EngineCommandRequest
                {
                    Command = "remote.read",
                    Arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["session"] = sessionId,
                        ["cursor"] = cursor.ToString()
                    },
                    WorkingDirectory = Environment.CurrentDirectory
                };

                var read = await client.SendAsync(readRequest, TimeSpan.FromSeconds(15));
                if (!read.Success)
                {
                    PrintResponse(read, wantsJson);
                    return read.ExitCode;
                }

                if (read.Data is { } data)
                {
                    if (data.TryGetProperty("chunks", out var chunks) && chunks.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var chunk in chunks.EnumerateArray())
                        {
                            var text = chunk.TryGetProperty("text", out var textElement)
                                ? textElement.GetString() ?? string.Empty
                                : string.Empty;
                            var isError = chunk.TryGetProperty("isError", out var isErrorElement) &&
                                isErrorElement.ValueKind == JsonValueKind.True;

                            if (isError)
                            {
                                stderr.AppendLine(text);
                            }
                            else
                            {
                                stdout.AppendLine(text);
                            }

                            if (!wantsJson)
                            {
                                WriteChunk(text, isError);
                            }
                        }
                    }

                    if (data.TryGetProperty("nextCursor", out var nextCursorElement) &&
                        nextCursorElement.TryGetInt32(out var nextCursor))
                    {
                        cursor = nextCursor;
                    }

                    completed = data.TryGetProperty("isCompleted", out var completedElement) &&
                        completedElement.ValueKind == JsonValueKind.True;

                    if (data.TryGetProperty("exitCode", out var exitCodeElement) &&
                        exitCodeElement.TryGetInt32(out var parsedExitCode))
                    {
                        exitCode = parsedExitCode;
                    }
                }

                if (!completed)
                {
                    await Task.Delay(100);
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        if (wantsJson)
        {
            WriteJson(new
            {
                requestId = start.RequestId,
                success = exitCode == 0,
                exitCode,
                syncId,
                sessionId,
                commandText,
                timedOut,
                standardOutput = stdout.ToString(),
                standardError = stderr.ToString()
            });
        }
        else
        {
            Console.WriteLine($"[exit {exitCode}]");
        }

        return exitCode;
    }

    private static async Task SendRemoteStopAsync(EnginePipeClient client, string syncId, string sessionId)
    {
        var stopRequest = new EngineCommandRequest
        {
            Command = "remote.stop",
            Arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sync-id"] = syncId,
                ["session"] = sessionId
            },
            WorkingDirectory = Environment.CurrentDirectory
        };

        await client.SendAsync(stopRequest, TimeSpan.FromSeconds(10));
    }

    private static void WriteChunk(string text, bool isError)
    {
        var previousColor = Console.ForegroundColor;
        if (isError)
        {
            Console.ForegroundColor = ConsoleColor.Red;
        }

        Console.WriteLine(text);
        Console.ForegroundColor = previousColor;
    }

    private static string ResolveSyncId(ParsedArgs parsed) =>
        parsed.Options.TryGetValue("sync-id", out var optionSyncId)
            ? optionSyncId
            : parsed.Options.TryGetValue("id", out var optionId)
                ? optionId
                : parsed.Positionals.Count >= 3
                    ? parsed.Positionals[2]
                    : string.Empty;

    private static int ResolveTimeout(ParsedArgs parsed)
    {
        var timeoutSeconds = parsed.Options.TryGetValue("timeout", out var rawTimeout) && int.TryParse(rawTimeout, out var parsedTimeout)
            ? parsedTimeout
            : EngineDefaults.DefaultTimeoutSeconds;

        return Math.Clamp(timeoutSeconds, 1, 3600);
    }

    private static EngineCommandRequest BuildEngineRequest(string[] args)
    {
        var parsed = ParsedArgs.Parse(args);
        var command = ResolveCommand(parsed);
        var arguments = new Dictionary<string, string>(parsed.Options, StringComparer.OrdinalIgnoreCase)
        {
            ["workingDirectory"] = Environment.CurrentDirectory
        };

        if (command == "config.get" && parsed.Positionals.Count >= 3)
        {
            arguments["key"] = parsed.Positionals[2];
        }

        if (command == "config.set" && parsed.Positionals.Count >= 4)
        {
            arguments["key"] = parsed.Positionals[2];
            arguments["value"] = parsed.Positionals[3];
        }

        if (!arguments.ContainsKey("sync-id") && command.StartsWith("sync.", StringComparison.OrdinalIgnoreCase))
        {
            var syncIdIndex = command.StartsWith("sync.ignores.", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
            if (parsed.Positionals.Count > syncIdIndex)
            {
                arguments["sync-id"] = parsed.Positionals[syncIdIndex];
            }
        }

        if (!arguments.ContainsKey("sync-id") && command.StartsWith("remote.", StringComparison.OrdinalIgnoreCase) && parsed.Positionals.Count >= 3)
        {
            arguments["sync-id"] = parsed.Positionals[2];
        }

        if (parsed.CommandAfterSeparator.Count > 0)
        {
            arguments["commandText"] = string.Join(' ', parsed.CommandAfterSeparator);
        }

        if (command.StartsWith("git.", StringComparison.OrdinalIgnoreCase))
        {
            arguments["gitArguments"] = BuildGitArguments(args);
        }

        return new EngineCommandRequest
        {
            Command = command,
            Arguments = arguments,
            Tokens = args.ToList(),
            WorkingDirectory = Environment.CurrentDirectory
        };
    }

    private static string ResolveCommand(ParsedArgs parsed)
    {
        var tokens = parsed.Positionals;
        if (tokens.Count == 0)
        {
            return "help";
        }

        var first = tokens[0].ToLowerInvariant();
        if (first is "status" or "doctor")
        {
            return first;
        }

        if (first == "app" && tokens.Count > 1)
        {
            return "app." + tokens[1].ToLowerInvariant();
        }

        if (first == "config" && tokens.Count > 1)
        {
            return "config." + tokens[1].ToLowerInvariant();
        }

        if (first == "update" && tokens.Count > 1)
        {
            return "update." + tokens[1].ToLowerInvariant();
        }

        if (first == "files" && tokens.Count > 1)
        {
            return "files." + tokens[1].ToLowerInvariant();
        }

        if (first == "clipboard" && tokens.Count > 1)
        {
            return "clipboard." + tokens[1].ToLowerInvariant();
        }

        if (first == "sync" && tokens.Count > 1)
        {
            if (tokens[1].Equals("ignores", StringComparison.OrdinalIgnoreCase) && tokens.Count > 2)
            {
                return "sync.ignores." + tokens[2].ToLowerInvariant();
            }

            return "sync." + tokens[1].ToLowerInvariant();
        }

        if (first == "remote" && tokens.Count > 1)
        {
            return "remote." + tokens[1].ToLowerInvariant();
        }

        if (first == "git" && tokens.Count > 1)
        {
            return "git." + tokens[1].ToLowerInvariant();
        }

        if (first == "workflow" && tokens.Count > 1)
        {
            return "workflow." + tokens[1].ToLowerInvariant();
        }

        return first;
    }

    private static string BuildGitArguments(string[] args)
    {
        var gitIndex = Array.FindIndex(args, a => string.Equals(a, "git", StringComparison.OrdinalIgnoreCase));
        if (gitIndex < 0 || gitIndex == args.Length - 1)
        {
            return string.Empty;
        }

        var relevant = new List<string>();
        var raw = args.Skip(gitIndex + 1).ToArray();
        for (var index = 0; index < raw.Length; index++)
        {
            var token = raw[index];
            if (string.Equals(token, "--remote", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "--json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (token is "--sync-id" or "--id" or "--timeout")
            {
                index++;
                continue;
            }

            relevant.Add(token);
        }

        return string.Join(' ', relevant.Select(QuoteIfNeeded));
    }

    private static int HandleAliasCommand(string[] args, CliAliasStore store, bool wantsJson)
    {
        var action = args.FirstOrDefault()?.ToLowerInvariant() ?? "list";
        if (action is "list" or "ls")
        {
            if (wantsJson)
            {
                WriteJson(store);
            }
            else
            {
                foreach (var alias in store.Aliases.OrderBy(a => a.Name))
                {
                    Console.WriteLine($"{alias.Name,-12} {alias.Template}");
                    if (!string.IsNullOrWhiteSpace(alias.Description))
                    {
                        Console.WriteLine($"             {alias.Description}");
                    }
                }
            }

            return 0;
        }

        if (action == "path")
        {
            Console.WriteLine(EngineDefaults.AliasFilePath);
            return 0;
        }

        if (action == "init")
        {
            CliAliasCatalog.Save(CliAliasCatalog.CreateDefaultStore());
            Console.WriteLine($"Aliases inicializados en {EngineDefaults.AliasFilePath}");
            return 0;
        }

        if (action == "set")
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Uso: mws alias set <nombre> <template>");
                return 1;
            }

            var name = args[1];
            var template = string.Join(' ', args.Skip(2));
            var existing = store.Aliases.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                store.Aliases.Add(new CliAliasDefinition { Name = name, Template = template });
            }
            else
            {
                existing.Template = template;
            }

            CliAliasCatalog.Save(store);
            Console.WriteLine($"Alias guardado: {name}");
            return 0;
        }

        if (action is "remove" or "rm")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Uso: mws alias remove <nombre>");
                return 1;
            }

            store.Aliases.RemoveAll(a => string.Equals(a.Name, args[1], StringComparison.OrdinalIgnoreCase));
            CliAliasCatalog.Save(store);
            Console.WriteLine($"Alias eliminado: {args[1]}");
            return 0;
        }

        Console.Error.WriteLine("Accion de alias no soportada.");
        return 1;
    }

    private static void PrintResponse(EngineCommandResponse response, bool wantsJson)
    {
        if (wantsJson)
        {
            WriteJson(response);
            return;
        }

        if (!string.IsNullOrWhiteSpace(response.Message))
        {
            var output = response.Success ? Console.Out : Console.Error;
            output.WriteLine(response.Message);
        }

        if (!string.IsNullOrEmpty(response.StandardOutput))
        {
            Console.Out.Write(response.StandardOutput);
            if (!response.StandardOutput.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                Console.Out.WriteLine();
            }
        }

        if (!string.IsNullOrEmpty(response.StandardError))
        {
            Console.Error.Write(response.StandardError);
            if (!response.StandardError.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                Console.Error.WriteLine();
            }
        }

        if (response.Data is { } data &&
            string.IsNullOrEmpty(response.StandardOutput) &&
            string.IsNullOrEmpty(response.StandardError))
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });
            Console.WriteLine(json);
        }
    }

    private static void WriteJson(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }));
    }

    private static string ResolveVersion()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(info))
        {
            var plusIndex = info.IndexOf('+');
            return plusIndex > 0 ? info[..plusIndex] : info;
        }

        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
    }

    private static bool IsHelp(string value) => value is "help" or "-h" or "--help" or "/?";

    private static string QuoteIfNeeded(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace)
            ? '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"'
            : value;

    private static void PrintHelp()
    {
        Console.WriteLine("MyWorkStation CLI (mws)");
        Console.WriteLine();
        Console.WriteLine("Comandos principales:");
        Console.WriteLine("  mws status");
        Console.WriteLine("  mws doctor");
        Console.WriteLine("  mws sync create --name <nombre> --path <carpeta>");
        Console.WriteLine("  mws sync invite --id <id>");
        Console.WriteLine("  mws sync invites");
        Console.WriteLine("  mws sync accept --sync-id <syncId> --path <carpeta>");
        Console.WriteLine("  mws sync list");
        Console.WriteLine("  mws sync force --id <id>");
        Console.WriteLine("  mws sync logs --id <id> --tail 50");
        Console.WriteLine("  mws remote exec --sync-id <id> -- <comando>");
        Console.WriteLine("  mws remote shell --sync-id <id>");
        Console.WriteLine("  mws git status");
        Console.WriteLine("  mws git status --remote --sync-id <id>");
        Console.WriteLine("  mws update check");
        Console.WriteLine("  mws alias list");
        Console.WriteLine();
        Console.WriteLine("Agrega --json para respuestas amigables para agentes AI.");
        Console.WriteLine($"Para pruebas multi-instancia puedes apuntar a otro pipe con {EngineDefaults.PipeNameEnvironmentVariable}.");
    }

    private sealed class ParsedArgs
    {
        public List<string> Positionals { get; } = [];
        public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> CommandAfterSeparator { get; } = [];

        public static ParsedArgs Parse(string[] args)
        {
            var parsed = new ParsedArgs();
            var afterSeparator = false;

            for (var index = 0; index < args.Length; index++)
            {
                var token = args[index];
                if (afterSeparator)
                {
                    parsed.CommandAfterSeparator.Add(token);
                    continue;
                }

                if (token == "--")
                {
                    afterSeparator = true;
                    continue;
                }

                if (token.StartsWith("--", StringComparison.Ordinal))
                {
                    var key = token[2..];
                    if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        parsed.Options[key] = args[++index];
                    }
                    else
                    {
                        parsed.Options[key] = "true";
                    }

                    continue;
                }

                if (token == "-m")
                {
                    if (index + 1 < args.Length)
                    {
                        parsed.Options["message"] = args[++index];
                    }

                    continue;
                }

                parsed.Positionals.Add(token);
            }

            if (parsed.Options.TryGetValue("id", out var id) && !parsed.Options.ContainsKey("sync-id"))
            {
                parsed.Options["sync-id"] = id;
            }

            return parsed;
        }
    }
}
