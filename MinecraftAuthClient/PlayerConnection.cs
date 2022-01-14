// Client

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace MinecraftAuthClient
{
    public class PlayerConnection : IDisposable
    {
        private const int BUFFERSIZE = 4096;
        private readonly ConcurrentDictionary<EndPoint, PlayerConnection> connections = new();
        private bool disposedValue;
        private readonly TcpClient? _client;
        private readonly NetworkStream? _clientStream;
        private readonly TcpClient? _server;
        private readonly NetworkStream? _serverStream;
        private readonly CancellationTokenSource? _tokenSource;

        public PlayerConnection(TcpClient client, byte[] auth, IPAddress ip, int port)
        {
            _client = client;

            if (client != null && client.Client != null && client.Client.RemoteEndPoint != null && connections.TryAdd(client.Client.RemoteEndPoint, this))
            {
                _clientStream = client.GetStream();
                _tokenSource = new();
                CancellationToken token = _tokenSource.Token;

                try
                {
                    Console.WriteLine($"Connecting: {client.Client.RemoteEndPoint}");
                    _server = new();
                    _server.Connect(ip, port);
                    _serverStream = _server.GetStream();

                    _serverStream.Write(auth, 0, auth.Length);

                    Console.WriteLine($"Connected: {client.Client.RemoteEndPoint}");

                    Task.Run(async () =>
                    {
                        // Relay to minecraft
                        try
                        {
                            byte[] buffer = new byte[BUFFERSIZE];
                            while (true)
                            {
                                int count = await _serverStream.ReadAsync(buffer, token);
                                await _clientStream.WriteAsync(buffer.AsMemory(0, count), token);
                            }
                        }
                        catch (Exception)
                        {
                            Dispose();
                        }
                    }, token);

                    Task.Run(async () =>
                    {
                        // Relay to server
                        try
                        {
                            byte[] buffer = new byte[BUFFERSIZE];
                            while (true)
                            {
                                int count = await _clientStream.ReadAsync(buffer, token);
                                await _serverStream.WriteAsync(buffer.AsMemory(0, count), token);
                            }
                        }
                        catch (Exception)
                        {
                            Dispose();
                        }
                    }, token);
                }
                catch (Exception)
                {
                    Dispose();
                }
            }
            else
            {
                Dispose();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (_client != null && _client.Client != null && _client.Client.RemoteEndPoint != null)
                    {
                        connections.TryRemove(_client.Client.RemoteEndPoint, out _);
                        Console.WriteLine($"Disconnected: {_client.Client.RemoteEndPoint}");
                    }

                    _tokenSource?.Cancel();

                    try
                    {
                        _client?.Client?.Shutdown(SocketShutdown.Both);
                    }
                    catch (Exception) { }
                    try
                    {
                        _client?.Client?.Disconnect(false);
                    }
                    catch (Exception) { }
                    _clientStream?.Dispose();
                    _client?.Dispose();

                    try
                    {
                        _server?.Client?.Shutdown(SocketShutdown.Both);
                    }
                    catch (Exception) { }
                    try
                    {
                        _server?.Client?.Disconnect(false);
                    }
                    catch (Exception) { }

                    _serverStream?.Dispose();
                    _server?.Dispose();

                    _tokenSource?.Dispose();
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
}