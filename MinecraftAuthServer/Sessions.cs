using static WriteLineC.Console;
using static MinecraftAuthServer.Globals;
using System.Diagnostics;

using System.Text.RegularPatterns;
using static System.Text.RegularPatterns.Pattern;
using System.Net;

namespace MinecraftAuthServer
{
    public static class Sessions
    {
        private static readonly Dictionary<string, Profile> profiles = new();
        private static int profileLock = 0;
        private const string profileFolderName = "MinecraftAuthProfiles";
        private const string profileExtension = ".profile";

        static Sessions()
        {
            if (Directory.Exists(profileFolderName))
            {
                SpinWait.SpinUntil(() => Interlocked.Exchange(ref profileLock, 1) == 0);
                string[] files = Directory.GetFiles(profileFolderName);
                foreach (string file in files)
                {
                    if (file.EndsWith(profileExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            string user = Path.GetFileNameWithoutExtension(file);
                            byte[] data = File.ReadAllBytes(file);
                            if (data != null && data.Length == 90)
                            {
                                Profile profile;
                                try
                                {
                                    profile = Profile.Parse(data);
                                    if (profiles.TryAdd(user, profile))
                                    {
                                        WriteLine(($"{user}", Yellow), " ", ("has been loaded", DarkBlue));
                                    }
                                    else
                                    {
                                        WriteLine(($"{user}", Yellow), " ", ("failed to add", DarkYellow));
                                    }
                                }
                                catch (Exception)
                                {
                                    try
                                    {
                                        File.Delete(file);
                                    }
                                    catch (Exception ex)
                                    {
                                        WriteLine(($"{user}", Yellow), (" failed to delete: ", DarkYellow), (ex.Message, Magenta));
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteLine(($"Profile load failed: {ex.Message}", Magenta));
                        }
                    }
                }
                Interlocked.Exchange(ref profileLock, 0);
            }
        }

        public static void Save(string user)
        {
            user = user.ToLower();
            if (profiles.TryGetValue(user, out Profile? profile))
            {
                try
                {
                    byte[] profileSave = profile.GetBytes();
                    if (!Directory.Exists(profileFolderName))
                        Directory.CreateDirectory(profileFolderName);
                    File.WriteAllBytes($"{profileFolderName}\\{user}.profile", profileSave);
                }
                catch (Exception ex)
                {
                    WriteLine(($"{user}", Yellow), (" profile save failed: ", DarkYellow), (ex.Message, Magenta));
                }
            }
        }

        public class Profile
        {
            public Position? position;
            public byte[]? passphrase;
            public CancellationTokenSource? timeOutToken;
            public TcpRelay? connection;
            public GameModes gameMode = GameModes.Spectator;
            public static Profile Parse(byte[] bytes)
            {
                if (bytes != null && bytes.Length == 90)
                {
                    Profile profile = new();
                    if ((bytes[0] & 1) > 0)
                        profile.position = Position.Parse(bytes[1..26]);
                    if ((bytes[0] & 2) > 0)
                        profile.passphrase = bytes[26..90];
                    return profile;
                }
                throw new Exception("Invalid Profile file");
            }
            public byte[] GetBytes()
            {
                byte[] bytes = new byte[90];
                if (position != null)
                {
                    bytes[0] += 1;
                    byte[] positionBytes = ((Position)position).GetBytes();
                    Array.Copy(positionBytes, 0, bytes, 1, positionBytes.Length);
                }
                if (passphrase != null)
                {
                    bytes[0] += 2;
                    Array.Copy(passphrase, 0, bytes, 26, 64);
                }
                return bytes;
            }
        }

        public static void ServerEvent(object o, DataReceivedEventArgs e)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (e.Data == null) return;
                if (ServerDone(e.Data)) return;
                if (UserJoined(e.Data)) return;
                if (UserLogin(e.Data)) return;
                if (UserLeft(e.Data)) return;
            });
        }

        private static bool ServerDone(string data)
        {
            if (data.Match(Text("["), Any, Text(" INFO]: Done ("), Any, Text(@"s)! For help, type ""help""")))
            {
                // Help prevent immortality exploit
                Execute("execute in minecraft:overworld run gamerule doImmediateRespawn true");
                Execute("execute in minecraft:the_nether run gamerule doImmediateRespawn true");
                Execute("execute in minecraft:the_end run gamerule doImmediateRespawn true");
                return true;
            }
            return false;
        }

        private static bool UserLeft(string data)
        {
            if (data.Match(Text("["), Any, Text(" INFO]: "), Any, Text(" left the game")))
            {
                string user = data.RangeOf(Text("["), Any, Text(" INFO]: ")).Between(Text(" left the game"));
                if (IsNameValid(user))
                {
                    SpinWait.SpinUntil(() => Interlocked.Exchange(ref profileLock, 1) == 0);
                    if (profiles.TryGetValue(user.ToLower(), out Profile? profile))
                    {
                        CancellationTokenSource? oldToken = Interlocked.Exchange(ref profile.timeOutToken, null);
                        try
                        {
                            oldToken?.Cancel();
                            oldToken?.Dispose();
                        }
                        catch (Exception) { }
                        Interlocked.Exchange(ref profile.connection, null)?.Dispose();
                    }
                    Interlocked.Exchange(ref profileLock, 0);
                    WriteLine(($"{user}", Yellow), " left");
                }
                else
                {
                    WriteLine(($"{user}", Yellow), (" kicked for invalid username", ConsoleColor.Red));
                }
                return true;
            }
            return false;
        }

        private static bool UserCommand(string data)
        {
            //StringRange beginning = data.RangeOf(Text("["), Any, Text(" INFO]: "), Any, Text(" issued server command: /"), Any);
            //StringRange command = data.RangeOf(beginning.End, Any);
            //string commandStr = command;
            //if (beginning.)
            //{

            //    return true;
            //}
            return false;
        }

        private static bool UserLogin(string data)
        {
            if (data.Match(Text("["), Any, Text(" INFO]: "), Any, Text(" issued server command: /login "), Any))
            {
                StringRange userStr = data.RangeOf(Text("["), Any, Text(" INFO]: ")).Between(Text(" issued server command: /login "));
                string user = userStr.ToString().ToLower();

                SpinWait.SpinUntil(() => Interlocked.Exchange(ref profileLock, 1) == 0);

                // Check if player profile exists
                if (!profiles.ContainsKey(user))
                {
                    Kick(userStr, "Unexpected error, Username not found, Try again later");
                    Interlocked.Exchange(ref profileLock, 0);
                    return true;
                }

                // Get player profile
                Profile profile = profiles[user];

                // possible already logged in
                if (profile.position == null)
                {
                    if (profile.gameMode == GameModes.Survival)
                        Tell(userStr, ("Already logged in", TellRawColors.Yellow));
                    else
                        Kick(userStr, "Unexpecteddd error, Coordinates lost, Contact administrator");
                    Interlocked.Exchange(ref profileLock, 0);
                    return true;
                }

                StringRange head = data.RangeOf(Text("["), Any, Text(" INFO]: "), Any, Text(" issued server command: /login "));
                string? passphrase = data[head.End..];
                if (passphrase == null || passphrase.Length < 6)
                {
                    Tell(userStr, ("<passphrase> at least 6 chars", TellRawColors.Red));
                    Interlocked.Exchange(ref profileLock, 0);
                    return true;
                }
                byte[] passBytes = passphrase.SHA512(System.Text.Encoding.UTF8);
                passphrase = null;

                // check passphrase
                if (profile.passphrase == null)
                {
                    profile.passphrase = passBytes;
                    Save(user);
                    CancellationTokenSource? crntToken = Interlocked.Exchange(ref profile.timeOutToken, null);
                    try
                    {
                        crntToken?.Cancel();
                        crntToken?.Dispose();
                    }
                    catch (Exception) { }
                    Tell(userStr, ("You are registered", TellRawColors.Green));
                    WriteLine(($"{userStr}", Yellow), ($" registered successfully", Green));
                }
                else
                {
                    if (profile.passphrase.SequenceEqual(passBytes))
                    {
                        CancellationTokenSource? crntToken = Interlocked.Exchange(ref profile.timeOutToken, null);
                        try
                        {
                            crntToken?.Cancel();
                            crntToken?.Dispose();
                        }
                        catch (Exception) { }
                        Tell(userStr, ("You are logged in", TellRawColors.Green));
                        WriteLine(($"{userStr}", Yellow), ($" logged in successfully", Green));
                    }
                    else
                    {
                        Tell(userStr, ("Wrong passphrase", TellRawColors.Red));
                        WriteLine(($"{user} entered the wrong password:", Red), "\n", (profile.passphrase.Base64(), Green), "\n", (passBytes.Base64(), DarkRed));
                        Interlocked.Exchange(ref profileLock, 0);
                        return true;
                    }
                }

                // teleport to prevLocation
                Teleport(userStr, (Position)profile.position);
                // change to survival
                profile.gameMode = GameMode(userStr, GameModes.Survival);
                // clear old position
                profile.position = null;
                Save(user);

                Interlocked.Exchange(ref profileLock, 0);
                return true;
            }
            return false;
        }

        private static bool UserJoined(string data)
        {
            if (data.Match(Text("["), Any, Text(" INFO]: "), Any, Text("[/"), Any, Text("] logged in with entity id"), Any))
            {
                StringRange userStr = data.RangeOf(Text("["), Any, Text(" INFO]: ")).Between(Text("[/"), Any, Except("[", "/"), Text("] logged in with entity id"));

                // Prevent user from doing anything until they login
                GameMode(userStr, GameModes.Spectator);
                Teleport(userStr, GenerateSpawnLocation());

                StringRange ipStr = data.RangeOf(userStr.End, Text($"[/")).Between(Text(":"));
                StringRange portStr = data.RangeOf(ipStr.End, Text(":")).Between(Text("]"));
                int port = int.Parse(portStr);

                StringRange worldStr = data.RangeOf(portStr.End, Text("] logged in with entity id "), Any, Text(" at ([")).Between(Text("]"));
                StringRange xStr = data.RangeOf(worldStr.End, Text("]")).Between(Text(", "));
                StringRange yStr = data.RangeOf(xStr.End, Text(", ")).Between(Text(", "));
                StringRange zStr = data.RangeOf(yStr.End, Text(", ")).Between(Text(")"));

                string user = userStr.ToString().ToLower();
                Demensions world = Demensions.ParseOrDefault(worldStr);
                double x = double.Parse(xStr);
                double y = double.Parse(yStr);
                double z = double.Parse(zStr);
                Position position = new(world, x, y, z);

                TcpRelay? relay = null;
                bool hasConnection = false;
                Connections.ConnectionAction(c => hasConnection = c.TryGetValue(port, out relay));
                if (!hasConnection || !IsNameValid(userStr) || ipStr != "127.0.0.1")
                {
                    // Close the connection to this user
                    relay?.Dispose();
                    return true;
                }
                else
                {
                    EndPoint? remoteEP = relay?.ClientA?.Client.RemoteEndPoint;
                    if (remoteEP != null)
                        WriteLine(($"{userStr}", Yellow), ($" joined from [{remoteEP}] on [{ipStr}:{portStr}] at {position}", Blue));
                    else
                    {
                        // Close the connection, direct connections are not allowed
                        relay?.Dispose();
                        WriteLine(($"{userStr}", Yellow), ($" disconnected, Direct connections not allowed", Red));
                        return true;
                    }
                }

                SpinWait.SpinUntil(() => Interlocked.Exchange(ref profileLock, 1) == 0);
                CancellationTokenSource timeOutTokenSource = new();

                bool userExists = profiles.ContainsKey(user);

                if (userExists && profiles[user].passphrase != null)
                {
                    Profile existingUser = profiles[user];
                    if (existingUser.position == null)
                    {
                        existingUser.position = position;
                        Save(user);
                    }
                    CancellationTokenSource? oldToken = Interlocked.Exchange(ref existingUser.timeOutToken, timeOutTokenSource);
                    try
                    {
                        oldToken?.Cancel();
                        oldToken?.Dispose();
                    }
                    catch (Exception) { }
                    Interlocked.Exchange(ref existingUser.connection, relay)?.Dispose();

                    double timeout = TimeSpan.FromSeconds(30).TotalMilliseconds;
                    string normalized = NormalizeMilliseconds(timeout);
                    Tell(userStr, ("Account registered", TellRawColors.Green), "\n", ($"Login within {normalized}", TellRawColors.Green), "\n", ("/login <passphrase>", TellRawColors.Green));

                    Task.Run(async () =>
                    {
                        while (timeout >= 1.0)
                        {
                            try
                            {
                                int remove = (int)Math.Min(int.MaxValue, timeout);
                                timeout -= remove;
                                await Task.Delay(remove, timeOutTokenSource.Token);
                            }
                            catch (Exception)
                            {
                                break;
                            }
                        }
                    }).ContinueWith(_ =>
                    {
                        if (!timeOutTokenSource.IsCancellationRequested)
                            Kick(userStr, $"Failed to login within {normalized}");
                    });
                }
                else
                {
                    Profile newProfile;

                    if (userExists)
                    {
                        newProfile = profiles[user];
                    }
                    else
                    {
                        newProfile = new();
                        newProfile.position = position;
                        Save(user);
                        if (!profiles.TryAdd(user, newProfile))
                        {
                            Kick(userStr, "Unexpected error, cannot add user");
                            Interlocked.Exchange(ref profileLock, 0);
                            return true;
                        }
                    }
                    CancellationTokenSource? oldToken = Interlocked.Exchange(ref newProfile.timeOutToken, timeOutTokenSource);
                    try
                    {
                        oldToken?.Cancel();
                        oldToken?.Dispose();
                    }
                    catch (Exception) { }
                    Interlocked.Exchange(ref newProfile.connection, relay)?.Dispose();

                    double timeout = TimeSpan.FromMinutes(1).TotalMilliseconds;
                    string normalized = NormalizeMilliseconds(timeout);
                    Tell(userStr, ("Account not registered", TellRawColors.Green), "\n", ($"Register within {normalized}", TellRawColors.Green), "\n", ("/login <passphrase>", TellRawColors.Green), "\n", ("<passphrase> at least 6 chars", TellRawColors.Green));

                    Task.Run(async () =>
                    {
                        while (timeout >= 1.0)
                        {
                            try
                            {
                                int remove = (int)Math.Min(int.MaxValue, timeout);
                                timeout -= remove;
                                await Task.Delay(remove, timeOutTokenSource.Token);
                            }
                            catch (Exception)
                            {
                                break;
                            }
                        }
                    }).ContinueWith(_ =>
                    {
                        if (!timeOutTokenSource.IsCancellationRequested)
                            Kick(userStr, $"Failed to register within {normalized}");
                    });
                }
                Interlocked.Exchange(ref profileLock, 0);

                //timeOutTokenSource.Cancel(); // Uncomment for testing

                return true;
            }
            return false;
        }
    }
}