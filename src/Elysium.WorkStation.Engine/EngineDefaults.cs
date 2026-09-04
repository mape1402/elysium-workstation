namespace Elysium.WorkStation.Engine;

public static class EngineDefaults
{
    public const string AppName = "MyWorkStation";
    public const string CliName = "mws";
    public const string PipeName = "Elysium.WorkStation.Engine";
    public const string PipeNameEnvironmentVariable = "MWS_ENGINE_PIPE";
    public const string AppBridgePipePrefix = "Elysium.WorkStation.Engine.AppBridge";
    public const int DefaultTimeoutSeconds = 300;

    public static string CliDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppName,
        "cli");

    public static string AliasFilePath => Path.Combine(CliDirectory, "aliases.json");

    public static string ResolvePipeName()
    {
        var configured = Environment.GetEnvironmentVariable(PipeNameEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured) ? PipeName : configured.Trim();
    }

    public static string CreateAppBridgePipeName(int processId) =>
        $"{AppBridgePipePrefix}.{processId}";
}
