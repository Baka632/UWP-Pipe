using System.IO.Pipes;

namespace UwpPipe.Common.Pipes;

public sealed partial class ServerPipe : PipeBase
{
    private readonly NamedPipeServerStream pipeStream;
    private readonly StreamReader streamReader;
    private readonly StreamWriter streamWriter;

    public override bool IsConnected => pipeStream.IsConnected;

    public ServerPipe(string pipeName)
        : this(new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
    {
    }

    public ServerPipe(NamedPipeServerStream stream)
    {
        pipeStream = stream;
        streamReader = new StreamReader(pipeStream);
        streamWriter = new StreamWriter(pipeStream);
    }

    public override async Task<string?> ReadAsync(CancellationToken ct)
    {
#if NETSTANDARD
        return await streamReader.ReadLineAsync();
#else
        return await streamReader.ReadLineAsync(ct);
#endif
    }

    public override async Task WriteAsync(string message, CancellationToken ct)
    {
#if NETSTANDARD
        await streamWriter.WriteLineAsync(message);
        await streamWriter.FlushAsync();
#else
        await streamWriter.WriteLineAsync(message.AsMemory(), ct);
        await streamWriter.FlushAsync(ct);
#endif
    }

    public override async Task StartAsync(CancellationToken ct)
    {
        await pipeStream.WaitForConnectionAsync(ct);
    }

    public override void Dispose()
    {
        pipeStream.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
#if NETSTANDARD
        await Task.Run(pipeStream.Dispose);
#else
        await pipeStream.DisposeAsync();
#endif
    }
}
