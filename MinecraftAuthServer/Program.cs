// Server
using static WriteLineC.Console;
using MinecraftAuthServer;
using System.Text;

// Get server passphrase
string? serverPassphrase = Prompt(("Enter a server passphrase: ", Green));
byte[]? authBytes = null;
if (serverPassphrase != null && serverPassphrase.Length != 0)
    authBytes = serverPassphrase.SHA512(Encoding.UTF8);

// Launch minecraft server
Globals.LaunchMinecraftServer(args);

// Start listening for connections
_ = Connections.StartListening(25565, 25555, authBytes);

Globals.WaitForServerExit();