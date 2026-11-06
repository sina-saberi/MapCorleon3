using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace MapCorleon.Services;

internal sealed class InstanceActivation : IDisposable
{
    private readonly string pipeName;
    private readonly Mutex mutex;
    private readonly CancellationTokenSource lifetime = new();

    public InstanceActivation(string name)
    {
        pipeName = name;
        mutex = new Mutex(false, $@"Local\{name}");
        try { IsPrimary = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
    }

    public bool IsPrimary { get; }

    public async Task ListenAsync(Action activate)
    {
        var token = lifetime.Token;
        var request = new byte[1];
        while (!token.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                if (await server.ReadAsync(request, token).ConfigureAwait(false) == 1)
                    activate();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (IOException) { }
        }
    }

    public async Task RequestActivationAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync(timeout.Token);
            if (GetNamedPipeServerProcessId(client.SafePipeHandle, out var primaryProcessId))
                AllowSetForegroundWindow(primaryProcessId);

            await client.WriteAsync(new byte[] { 1 }, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        catch (IOException) { }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
        if (IsPrimary) mutex.ReleaseMutex();
        mutex.Dispose();
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
