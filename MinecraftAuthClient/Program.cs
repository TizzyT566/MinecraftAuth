// Client

using MinecraftAuthClient;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

args = new string[] { "DarkArchives.ddns.net", "test", "TizzyT", "thisisapassword" };

if (args.Length >= 4 && args.Length <= 6)
{
    // First argument is the host address
    IPAddress? host;
    if (!TryResolveIP(args[0], out host) || host == null)
    {
        Console.WriteLine($"Host Address invalid ...");
        return;
    }
    Console.WriteLine($"HostAddr: {host}");

    // Second argument is the host password
    string hostPassword = args[1];

    // Third argument is username
    string username = args[2];
    byte[] userBytes = Encoding.UTF8.GetBytes(username);
    if (userBytes.Length < 3 || userBytes.Length > 16 || username.StartsWith("-"))
    {
        Console.WriteLine($"Invalid username");
        return;
    }
    Console.WriteLine($"Username: {args[2]}");

    // Forth argument is user password
    string password = args[3];

    // Fifth argument is optional custom host port
    int hostPort = 25565;
    if (args.Length >= 5)
    {
        if (!int.TryParse(args[4], out hostPort))
        {
            Console.WriteLine("Host Port invalid ...");
            return;
        }
        if (hostPort < 1024 || hostPort > 65565)
        {
            Console.WriteLine("Host Port out of range ...");
            return;
        }
    }
    Console.WriteLine($"HostPort: {hostPort}");

    // Sixth argument is optional custom listening port
    int lstnPort = 25565;
    if (args.Length >= 6)
    {
        if (!int.TryParse(args[5], out lstnPort))
        {
            Console.WriteLine("Listening Port invalid ...");
            return;
        }
        if (lstnPort < 1024 || lstnPort > 65565)
        {
            Console.WriteLine("Listening Port out of range ...");
            return;
        }
    }
    Console.WriteLine($"LstnPort: {lstnPort}");

    Console.WriteLine("==========================================");

    TcpListener listener = new(IPAddress.Loopback, lstnPort);
    listener.Start();

    string authString = hostPassword + ":" + username + ":" + password;
    using SHA512 hash = SHA512.Create();
    byte[]? authHash = hash.ComputeHash(Encoding.UTF8.GetBytes(authString));
    byte[] authBytes = new byte[80];
    Array.Copy(userBytes, 0, authBytes, 0, userBytes.Length);
    Array.Copy(authHash, 0, authBytes, 16, authHash.Length);
    authHash = null;


    while (true)
    {
        TcpClient newConnection = await listener.AcceptTcpClientAsync();
        _ = new PlayerConnection(newConnection, authBytes, host, hostPort);
    }
}
else
{
    Console.WriteLine("Invalid number of arguments.\nRequired:\n 1) Host Address\n 2) Host Password\n 3) User Name\n 4) User Password\nOptional:\n 5) Host Port\n 6) Listening Port");
}

static bool TryResolveIP(string host, out IPAddress? ip)
{
    try
    {
        if (IPAddress.TryParse(host, out ip))
        {
            return true;
        }
        else
        {
            ip = Dns.GetHostEntry(host).AddressList[0];
            return true;
        }
    }
    catch (Exception)
    {
        ip = null;
        return false;
    }
}