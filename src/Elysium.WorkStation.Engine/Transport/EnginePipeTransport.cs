using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Elysium.WorkStation.Engine.Contracts;

namespace Elysium.WorkStation.Engine.Transport;

public sealed class EnginePipeClient
{
    private readonly string _pipeName;

    public EnginePipeClient(string? pipeName = null)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? EngineDefaults.ResolvePipeName()
            : pipeName;
    }

    public async Task<EngineCommandResponse> SendAsync(
        EngineCommandRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await pipe.ConnectAsync(2000, cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

            var payload = JsonSerializer.Serialize(request, EngineJson.Options);
            await writer.WriteLineAsync(payload.AsMemory(), timeoutCts.Token);

            var line = await reader.ReadLineAsync(timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(line))
            {
                return EngineCommandResponse.Fail(request.RequestId, "El Engine no devolvio respuesta.", 2);
            }

            return JsonSerializer.Deserialize<EngineCommandResponse>(line, EngineJson.Options)
                ?? EngineCommandResponse.Fail(request.RequestId, "La respuesta del Engine no se pudo leer.", 2);
        }
        catch (OperationCanceledException)
        {
            return EngineCommandResponse.Fail(request.RequestId, "Timeout esperando respuesta del Engine.", 124);
        }
        catch (Exception ex)
        {
            return EngineCommandResponse.Fail(
                request.RequestId,
                "No se pudo conectar con MyWorkStation Engine. Abre la app y vuelve a intentar.",
                2,
                ex.Message);
        }
    }
}

public sealed class EnginePipeServer : IAsyncDisposable
{
    private readonly Func<EngineCommandRequest, CancellationToken, Task<EngineCommandResponse>> _handler;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stopCts = new();
    private Task? _runTask;

    public EnginePipeServer(
        Func<EngineCommandRequest, CancellationToken, Task<EngineCommandResponse>> handler,
        string? pipeName = null)
    {
        _handler = handler;
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? EngineDefaults.ResolvePipeName()
            : pipeName;
    }

    public bool IsRunning => _runTask is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _runTask = Task.Run(() => RunAsync(_stopCts.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(pipe, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                break;
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
                {
                    AutoFlush = true
                };

                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                var request = JsonSerializer.Deserialize<EngineCommandRequest>(line, EngineJson.Options);
                var response = request is null
                    ? EngineCommandResponse.Fail(string.Empty, "Solicitud invalida.", 2)
                    : await _handler(request, cancellationToken).ConfigureAwait(false);

                var payload = JsonSerializer.Serialize(response, EngineJson.Options);
                await writer.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The CLI side will surface connection/timeout failures. Keep the host alive.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stopCts.Cancel();
        if (_runTask is not null)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch
            {
                // Best effort shutdown.
            }
        }

        _stopCts.Dispose();
    }
}
