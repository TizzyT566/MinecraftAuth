using System.Net.Sockets;

namespace MinecraftAuthServer
{
    public class TcpRelay : IDisposable
    {
        private bool disposedValue;

        public TcpClient? ClientA { get; private set; }
        public TcpClient? ClientB { get; private set; }

        public bool Connected { get; private set; }

        public int BufferSize { get; set; } = 8192;

        private CancellationTokenSource? cts;
        private readonly Action? _onDisconnect;

        public TcpRelay(TcpClient clientA, TcpClient clientB, Action? onDisconnect = null)
        {
            ClientA = clientA;
            ClientB = clientB;
            _onDisconnect = onDisconnect;

            if (ClientA == null || ClientB == null)
                throw new Exception("Clients cannot be null.");
            if (ClientA.Client == null || ClientB.Client == null)
                throw new Exception("Underlying streams cannot be null");

            Connected = true;

            NetworkStream streamA = ClientA.GetStream();
            NetworkStream streamB = ClientB.GetStream();

            cts = new();
            CancellationToken token = cts.Token;

            // Relay from Client A to ClientB
            Task.Run(async () =>
            {
                try
                {
                    byte[] buffer = new byte[BufferSize];
                    int count;
                    do
                    {
                        count = await streamA.ReadAsync(buffer, token);
                        await streamB.WriteAsync(buffer.AsMemory(0, count), token);
                        if (BufferSize != buffer.Length) buffer = new byte[BufferSize];
                    } while (count > 0);
                }
                catch (Exception) { }
                finally
                {
                    Dispose();
                }
            });

            // Relay from ClientB to ClientA
            Task.Run(async () =>
            {
                try
                {
                    byte[] buffer = new byte[BufferSize];
                    int count;
                    do
                    {
                        count = await streamB.ReadAsync(buffer, token);
                        await streamA.WriteAsync(buffer.AsMemory(0, count), token);
                        if (BufferSize != buffer.Length) buffer = new byte[BufferSize];
                    } while (count > 0);
                }
                catch (Exception) { }
                finally
                {
                    Dispose();
                }
            });
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Connected = false;
                    cts?.Cancel();
                    ClientA?.Dispose();
                    ClientA = null;
                    ClientB?.Dispose();
                    ClientB = null;
                    cts = null;
                    cts?.Dispose();
                    _onDisconnect?.Invoke();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public static class TcpRelayEx
    {
        public static TcpRelay RelayWith(this TcpClient @this, TcpClient client, Action? onDisconnect = null) =>
            new(@this, client, onDisconnect);
    }
}
