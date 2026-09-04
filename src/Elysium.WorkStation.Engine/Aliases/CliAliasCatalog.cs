using System.Text.Json;

namespace Elysium.WorkStation.Engine.Aliases;

public sealed class CliAliasDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class CliAliasStore
{
    public List<CliAliasDefinition> Aliases { get; set; } = [];
}

public static class CliAliasCatalog
{
    public static CliAliasStore LoadOrCreate(string? path = null)
    {
        path ??= EngineDefaults.AliasFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? EngineDefaults.CliDirectory);

        if (!File.Exists(path))
        {
            var defaults = CreateDefaultStore();
            Save(defaults, path);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CliAliasStore>(json, AliasJsonOptions) ?? CreateDefaultStore();
        }
        catch
        {
            return CreateDefaultStore();
        }
    }

    public static void Save(CliAliasStore store, string? path = null)
    {
        path ??= EngineDefaults.AliasFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? EngineDefaults.CliDirectory);
        File.WriteAllText(path, JsonSerializer.Serialize(store, AliasJsonOptions));
    }

    public static CliAliasStore CreateDefaultStore() =>
        new()
        {
            Aliases =
            [
                new() { Name = "lsync", Template = "sync list", Description = "Lista sincronizaciones configuradas." },
                new() { Name = "fsync", Template = "sync force --id {0}", Description = "Fuerza sincronizacion por id." },
                new() { Name = "screate", Template = "sync create --name {0} --path {1}", Description = "Crea una sincronizacion local." },
                new() { Name = "sinvite", Template = "sync invite --id {0}", Description = "Invita al otro equipo a sincronizar." },
                new() { Name = "saccept", Template = "sync accept --sync-id {0} --path {1}", Description = "Acepta una invitacion de sincronizacion." },
                new() { Name = "slogs", Template = "sync logs --id {0} --tail 50", Description = "Muestra ultimos logs de una sincronizacion." },
                new() { Name = "rexec", Template = "remote exec --sync-id {0} -- {1}", Description = "Ejecuta un comando en la PC remota." },
                new() { Name = "rstatus", Template = "remote exec --sync-id {0} -- git status", Description = "Ejecuta git status en la PC remota." },
                new() { Name = "rbuild", Template = "remote exec --sync-id {0} -- dotnet build", Description = "Ejecuta dotnet build en la PC remota." },
                new() { Name = "rtest", Template = "remote exec --sync-id {0} -- dotnet test", Description = "Ejecuta dotnet test en la PC remota." },
                new() { Name = "rgit", Template = "remote exec --sync-id {0} -- git {1}", Description = "Ejecuta git remoto con argumentos libres." }
            ]
        };

    public static string[] ExpandIfAlias(string[] args, CliAliasStore store)
    {
        if (args.Length == 0)
        {
            return args;
        }

        var alias = store.Aliases.FirstOrDefault(a => string.Equals(a.Name, args[0], StringComparison.OrdinalIgnoreCase));
        if (alias is null)
        {
            return args;
        }

        var aliasArgs = args.Skip(1).ToArray();
        var template = alias.Template;
        for (var index = 0; index < aliasArgs.Length; index++)
        {
            template = template.Replace("{" + index + "}", QuoteIfNeeded(aliasArgs[index]), StringComparison.Ordinal);
        }

        var expanded = CommandLineTokenizer.Tokenize(template).ToList();
        var used = CountUsedPlaceholders(alias.Template);
        if (aliasArgs.Length > used)
        {
            expanded.AddRange(aliasArgs.Skip(used));
        }

        return expanded.ToArray();
    }

    private static int CountUsedPlaceholders(string template)
    {
        var count = 0;
        for (var index = 0; index < 32; index++)
        {
            if (template.Contains("{" + index + "}", StringComparison.Ordinal))
            {
                count = index + 1;
            }
        }

        return count;
    }

    private static string QuoteIfNeeded(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace)
            ? '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"'
            : value;

    private static JsonSerializerOptions AliasJsonOptions => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}

public static class CommandLineTokenizer
{
    public static IReadOnlyList<string> Tokenize(string commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return result;
        }

        var current = new System.Text.StringBuilder();
        var inQuote = false;
        var escaping = false;

        foreach (var ch in commandLine)
        {
            if (escaping)
            {
                current.Append(ch);
                escaping = false;
                continue;
            }

            if (ch == '\\' && inQuote)
            {
                escaping = true;
                continue;
            }

            if (ch == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuote)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
