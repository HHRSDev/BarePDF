using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace BarePDF.Instances;

internal sealed class InstanceCoordinator : IDisposable
{
    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private CancellationTokenSource? _cts;

    public bool IsPrimary { get; private set; }

    public event Action<string>? PdfPathReceived;

    public InstanceCoordinator()
    {
        var user = Environment.UserName;
        _mutexName = $@"Local\BarePDF.SingleInstance.{user}";
        _pipeName = $"BarePDF.OpenPdf.{user}";
    }

    public bool TryAcquirePrimary()
    {
        _mutex = new Mutex(initiallyOwned: false, name: _mutexName, out var createdNew);
        try
        {
            if (createdNew && _mutex.WaitOne(0))
            {
                IsPrimary = true;
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            IsPrimary = true;
            return true;
        }
        IsPrimary = false;
        return false;
    }

    public void StartListening()
    {
        if (!IsPrimary) throw new InvalidOperationException("Only the primary instance can listen.");
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token);
                using var reader = new StreamReader(server);
                var payload = (await reader.ReadToEndAsync(token)).Trim();
                PdfPathReceived?.Invoke(payload);
            }
            catch (OperationCanceledException) { return; }
            catch { /* swallow per-connection errors and keep listening */ }
        }
    }

    public bool SendToPrimary(string payload, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect((int)timeout.TotalMilliseconds);
            using var writer = new StreamWriter(client);
            writer.Write(payload);
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (IsPrimary && _mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
        _mutex = null;
    }
}
