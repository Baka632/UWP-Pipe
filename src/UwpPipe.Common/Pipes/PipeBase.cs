namespace UwpPipe.Common.Pipes;

public abstract class PipeBase : IDisposable, IAsyncDisposable
{
    public abstract bool IsConnected { get; }
    public abstract Task WriteAsync(string message, CancellationToken ct);
    public abstract Task<string?> ReadAsync(CancellationToken ct);
    public abstract Task StartAsync(CancellationToken ct);
    public abstract void Dispose();
    public abstract ValueTask DisposeAsync();
}
