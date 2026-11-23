using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace EventGhostDotNet;

/// <summary>
/// Empêche plusieurs instances dans une même session Windows. La seconde instance
/// demande à la première de s'afficher via un pipe nommé, puis se ferme.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\EventGhostDotNet.Instance";
    private const string ActivationMessage = "activate";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;

    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    private static string PipeName =>
        $"EventGhostDotNet.Activate.{Process.GetCurrentProcess().SessionId}";

    public static SingleInstanceGuard? TryAcquire(Action activate)
    {
        var mutex = new Mutex(false, MutexName);
        bool owns;
        try
        {
            owns = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            owns = true;
        }

        if (!owns)
        {
            mutex.Dispose();
            RequestActivation();
            return null;
        }

        var guard = new SingleInstanceGuard(mutex);
        guard._listener = guard.ListenAsync(activate);
        return guard;
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _stop.Dispose();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
        _mutex.Dispose();
    }

    private async Task ListenAsync(Action activate)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(server);
                var message = await reader.ReadLineAsync(_stop.Token);
                if (string.Equals(message, ActivationMessage, StringComparison.Ordinal))
                    activate();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                try
                {
                    await Task.Delay(200, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static void RequestActivation()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(300);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(ActivationMessage);
                return;
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or
                                               UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
