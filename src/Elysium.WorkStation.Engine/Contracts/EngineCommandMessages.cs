using System.Text.Json;

namespace Elysium.WorkStation.Engine.Contracts;

public sealed class EngineCommandRequest
{
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");
    public string Command { get; init; } = string.Empty;
    public Dictionary<string, string> Arguments { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Tokens { get; init; } = [];
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;
    public DateTime RequestedAtUtc { get; init; } = DateTime.UtcNow;

    public string GetArgument(string name, string defaultValue = "") =>
        Arguments.TryGetValue(name, out var value) ? value : defaultValue;

    public int GetIntArgument(string name, int defaultValue = 0) =>
        Arguments.TryGetValue(name, out var raw) && int.TryParse(raw, out var value)
            ? value
            : defaultValue;

    public bool GetBoolArgument(string name, bool defaultValue = false) =>
        Arguments.TryGetValue(name, out var raw) && bool.TryParse(raw, out var value)
            ? value
            : defaultValue;
}

public sealed class EngineCommandResponse
{
    public string RequestId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public JsonElement? Data { get; init; }

    public static EngineCommandResponse Ok(string requestId, string message = "", object? data = null, string standardOutput = "") =>
        new()
        {
            RequestId = requestId,
            Success = true,
            ExitCode = 0,
            Message = message,
            StandardOutput = standardOutput,
            Data = ToJsonElement(data)
        };

    public static EngineCommandResponse Fail(string requestId, string message, int exitCode = 1, string standardError = "") =>
        new()
        {
            RequestId = requestId,
            Success = false,
            ExitCode = exitCode,
            Message = message,
            StandardError = string.IsNullOrWhiteSpace(standardError) ? message : standardError
        };

    private static JsonElement? ToJsonElement(object? data)
    {
        if (data is null)
        {
            return null;
        }

        return JsonSerializer.SerializeToElement(data, EngineJson.Options);
    }
}

public static class EngineJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}
