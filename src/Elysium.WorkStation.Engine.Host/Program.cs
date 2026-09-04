using System.Diagnostics;
using Elysium.WorkStation.Engine;
using Elysium.WorkStation.Engine.Contracts;
using Elysium.WorkStation.Engine.Transport;

namespace Elysium.WorkStation.Engine.Host;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = HostOptions.Parse(args);
        if (options.ShowHelp || string.IsNullOrWhiteSpace(options.AppPipeName))
        {
            PrintHelp();
            return options.ShowHelp ? 0 : 2;
        }

        using var stopCts = new CancellationTokenSource();
        var monitorTask = MonitorAppAsync(options.AppPid, stopCts);
        var appClient = new EnginePipeClient(options.AppPipeName);
        await using var server = new EnginePipeServer(
            async (request, cancellationToken) =>
            {
                var timeoutSeconds = request.GetIntArgument("timeout", EngineDefaults.DefaultTimeoutSeconds);
                timeoutSeconds = Math.Clamp(timeoutSeconds, 1, 3600);
                return await appClient.SendAsync(request, TimeSpan.FromSeconds(timeoutSeconds + 10), cancellationToken);
            },
            options.PublicPipeName);

        server.Start();

        try
        {
            await monitorTask.ConfigureAwait(false);
        }
        finally
        {
            stopCts.Cancel();
        }

        return 0;
    }

    private static async Task MonitorAppAsync(int appPid, CancellationTokenSource stopCts)
    {
        if (appPid <= 0)
        {
            await Task.Delay(Timeout.Infinite, stopCts.Token).ConfigureAwait(false);
            return;
        }

        try
        {
            using var process = Process.GetProcessById(appPid);
            await process.WaitForExitAsync(stopCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down normally.
        }
        catch
        {
            // If the app pid cannot be found, leave the host instead of orphaning it.
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("mws-engine-host --app-pid <pid> --app-pipe <pipe> [--public-pipe <pipe>]");
    }

    private sealed record HostOptions
    {
        public int AppPid { get; init; }
        public string AppPipeName { get; init; } = string.Empty;
        public string PublicPipeName { get; init; } = EngineDefaults.PipeName;
        public bool ShowHelp { get; init; }

        public static HostOptions Parse(string[] args)
        {
            var result = new HostOptions();
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index].ToLowerInvariant())
                {
                    case "--help":
                    case "-h":
                        result = result with { ShowHelp = true };
                        break;
                    case "--app-pid" when index + 1 < args.Length && int.TryParse(args[index + 1], out var pid):
                        result = result with { AppPid = pid };
                        index++;
                        break;
                    case "--app-pipe" when index + 1 < args.Length:
                        result = result with { AppPipeName = args[++index] };
                        break;
                    case "--public-pipe" when index + 1 < args.Length:
                        result = result with { PublicPipeName = args[++index] };
                        break;
                }
            }

            return result;
        }
    }
}
