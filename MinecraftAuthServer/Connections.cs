using static WriteLineC.Console;
using System.Net;
using System.Net.Sockets;

namespace MinecraftAuthServer
{
    public static class Connections
    {
        // Store port numbers and their connections
        private static readonly Dictionary<int, TcpRelay> connections = new();

        public static void ConnectionAction(Action<Dictionary<int, TcpRelay>> action)
        {
            lock (connections)
                action?.Invoke(connections);
        }

        private static bool started = false;
        public static async Task StartListening(int listeningPort, int serverPort, byte[]? auth = null)
        {
            if (started) return;
            started = true;
            await Task.Run(async () =>
            {
                TcpListener listener = new(IPAddress.Any, listeningPort);
                listener.Start();

                while (true)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    string? remoteEndPoint = client.Client.RemoteEndPoint?.ToString();

                    TcpClient server = new();
                    try
                    {
                        if (remoteEndPoint == null)
                        {
                            WriteLine(("Unknown remote endpoint", Red));
                            throw new Exception();
                        }

                        // Check for server password
                        if (auth != null)
                        {
                            NetworkStream authStream = client.GetStream();
                            byte[] clientAuth = new byte[64];
                            int count = 0, crntCount = 0;
                            int timeoutMilliseconds = 5000;
                            do
                            {
                                crntCount = await authStream.ReadAsync(clientAuth.AsMemory(count, 64 - count), timeoutMilliseconds);
                                count += crntCount;
                            } while (count < 64 && crntCount > 0);
                            if (crntCount == -1)
                            {
                                WriteLine((remoteEndPoint, Yellow), ($" Authorization timed out {timeoutMilliseconds / 1000.0}s", DarkYellow));
                                throw new TimeoutException();
                            }
                            if (!clientAuth.SequenceEqual(auth))
                            {
                                WriteLine((remoteEndPoint, Yellow), (" Unauthorized connection disconnected", Red));
                                throw new Exception();
                            }
                        }

                        await server.ConnectAsync(IPAddress.Loopback, serverPort);
                        int port = server.Client.LocalEndPoint.GetPort();
                        TcpRelay relay = client.RelayWith(server, () => ConnectionAction(c =>
                        {
                            c.Remove(port);
                        }));

                        if (port < 0)
                            relay.Dispose();
                        else
                        {
                            bool added = false;
                            ConnectionAction(c =>
                            {
                                added = c.TryAdd(port, relay);
                            });
                            if (!added)
                                relay.Dispose();
                        }
                    }
                    catch (Exception)
                    {
                        client.Dispose();
                        server.Dispose();
                    }
                }
            });
        }
    }

    public static class NetworkStreamEx
    {
        public static Task<int> ReadAsync(this NetworkStream @this, byte[] buffer, int offset, int count, int millisecondsTimeout) =>
            @this.ReadAsync(buffer.AsMemory(offset, count), millisecondsTimeout);
        public static async Task<int> ReadAsync(this NetworkStream @this, Memory<byte> buffer, int millisecondsTimeout)
        {
            CancellationTokenSource cts = new();
            try
            {
                cts.CancelAfter(millisecondsTimeout);
                return await @this.ReadAsync(buffer, cts.Token);
            }
            catch (Exception)
            {
                return -1;
            }
            finally
            {
                cts.Dispose();
            }
        }
    }
}
