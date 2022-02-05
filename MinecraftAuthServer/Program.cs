// Server
using static WriteLineC.Console;
using MinecraftAuthServer;
using System.Text;

string[] prevInfo = new string[3];
bool loaded = false;
if (File.Exists("MinecraftAuth.info"))
{
    string[] lines = await File.ReadAllLinesAsync("MinecraftAuth.info");
    if (lines.Length == 3)
    {
        prevInfo = lines;
        loaded = true;
    }
}

// Get server passphrase
string serverPassphrase = prevInfo[0] ?? Prompt(("Enter a passphrase (leave empty for no passphrase): ", Green));
prevInfo[0] = serverPassphrase;
byte[]? authBytes = serverPassphrase.Length == 0 ? null : serverPassphrase.SHA512(Encoding.UTF8);

int listenPort = 25565;
_ = int.TryParse(prevInfo[1], out listenPort);
if (listenPort < 1024 || listenPort > 65535)
    _ = int.TryParse(Prompt(("Enter a listening port: ", Green)), out listenPort);
while (listenPort < 1024 || listenPort > 65535)
    _ = int.TryParse(Prompt(("Enter a valid port number: ", Green)), out listenPort);
prevInfo[1] = listenPort.ToString();


int serverPort = 25555;
_ = int.TryParse(prevInfo[2], out serverPort);
if (serverPort < 1024 || serverPort > 65535)
    _ = int.TryParse(Prompt(("Enter a server port: ", Green)), out serverPort);
while (serverPort < 1024 || serverPort > 65535)
    _ = int.TryParse(Prompt(("Enter a valid port number: ", Green)), out serverPort);
prevInfo[2] = serverPort.ToString();

if (!loaded)
    await File.WriteAllLinesAsync("MinecraftAuth.info", prevInfo);

// Launch minecraft server
Globals.LaunchMinecraftServer(args);

// Start listening for connections
_ = Connections.StartListening(listenPort, serverPort, authBytes);

Globals.WaitForServerExit();