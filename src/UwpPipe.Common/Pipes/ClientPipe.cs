using System.IO.Pipes;

namespace UwpPipe.Common.Pipes;

public sealed partial class ClientPipe : PipeBase
{
    private readonly NamedPipeClientStream pipeStream;
    private readonly StreamReader streamReader;
    private readonly StreamWriter streamWriter;

    public override bool IsConnected => pipeStream.IsConnected;

    public ClientPipe(string pipeName)
        : this(new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
    {
    }

    public ClientPipe(NamedPipeClientStream stream)
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
        await pipeStream.ConnectAsync(ct);
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
