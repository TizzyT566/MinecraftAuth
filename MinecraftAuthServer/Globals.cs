using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MinecraftAuthServer
{
    public static class Globals
    {
        public sealed class GameModes
        {
            public static readonly GameModes Adventure = new("adventure");
            public static readonly GameModes Creative = new("creative");
            public static readonly GameModes Spectator = new("spectator");
            public static readonly GameModes Survival = new("survival");

            private readonly string _value;
            private GameModes(string value) => _value = value;
            public static implicit operator string(GameModes gm) => gm._value;
            public override string ToString() => _value.ToString();
        }

        public sealed class TellRawColors
        {
            public static readonly TellRawColors Black = new("black");
            public static readonly TellRawColors DarkBlue = new("dark_blue");
            public static readonly TellRawColors DarkGreen = new("dark_green");
            public static readonly TellRawColors DarkAqua = new("dark_aqua");
            public static readonly TellRawColors DarkRed = new("dark_red");
            public static readonly TellRawColors DarkPurple = new("dark_purple");
            public static readonly TellRawColors Gold = new("gold");
            public static readonly TellRawColors Gray = new("gray");
            public static readonly TellRawColors DarkGray = new("dark_gray");
            public static readonly TellRawColors Blue = new("blue");
            public static readonly TellRawColors Green = new("green");
            public static readonly TellRawColors Aqua = new("aqua");
            public static readonly TellRawColors Red = new("red");
            public static readonly TellRawColors LightPurple = new("light_purple");
            public static readonly TellRawColors Yellow = new("yellow");
            public static readonly TellRawColors White = new("white");

            private readonly string _value;
            private TellRawColors(string value) => _value = value;
            public TellRawColors(byte R, byte G, byte B) =>
                _value = $"#{R.ToString("X").PadLeft(2, '0')}{G.ToString("X").PadLeft(2, '0')}{B.ToString("X").PadLeft(2, '0')}";
            public static implicit operator string(TellRawColors gm) => gm._value;
            public override string ToString() => _value.ToString();
        }

        public class TellRawMessage
        {
            public string Text { get; set; }
            public TellRawColors Color { get; set; }
            private TellRawMessage(string text, TellRawColors? color = null)
            {
                Text = text.Replace("\"", "\\\"");
                Color = color ?? TellRawColors.White;
            }
            public static implicit operator TellRawMessage((string text, TellRawColors color) raw) => new(raw.text, raw.color);
            public static implicit operator TellRawMessage(string text) => new(text, TellRawColors.White);
            public override string ToString()
            {
                if (Text == null || Text == "\n") return "\"\\n\"";
                return @$"{{""text"":""{Text}"",""color"":""{Color}""}}";
            }
        }

        public sealed class Demensions
        {
            public static readonly Demensions TheNether = new("the_nether");
            public static readonly Demensions Overworld = new("overworld");
            public static readonly Demensions TheEnd = new("the_end");

            private readonly string _value;
            private Demensions(string value) => _value = value;
            public static implicit operator Demensions(byte b) =>
                b switch { 0 => TheNether, 1 => Overworld, _ => TheEnd };
            public static implicit operator byte(Demensions d) =>
                d._value == TheNether ? (byte)0 :
                d._value == Overworld ? (byte)1 :
                (byte)2;
            public static implicit operator string(Demensions gm) => gm._value;
            public override string ToString() => _value.ToString();
            public static Demensions ParseOrDefault(string str) =>
                str == TheNether ? TheNether :
                str == Overworld ? Overworld :
                str == TheEnd ? TheEnd :
                str.EndsWith("_nether") ? TheNether :
                str.EndsWith("_the_end") ? TheEnd :
                Overworld;
        }

        public struct Position
        {
            public readonly Demensions W;
            public readonly double X, Y, Z;
            public Position(Demensions world, double x, double y, double z)
            {
                W = world;
                X = x;
                Y = y;
                Z = z;
            }
            public override string ToString() => $"{W},{X},{Y},{Z}";
            public static Position Parse(byte[] bytes)
            {
                Demensions w = bytes[0];
                double x = BitConverter.ToDouble(bytes, 1);
                double y = BitConverter.ToDouble(bytes, 9);
                double z = BitConverter.ToDouble(bytes, 17);
                return new Position(w, x, y, z);
            }
            public byte[] GetBytes()
            {
                byte[] bytes = new byte[25];
                bytes[0] = W;
                Array.Copy(BitConverter.GetBytes(X), 0, bytes, 1, 8);
                Array.Copy(BitConverter.GetBytes(Y), 0, bytes, 9, 8);
                Array.Copy(BitConverter.GetBytes(Z), 0, bytes, 17, 8);
                return bytes;
            }
        }

        private static readonly Process p = new();
        private static int processLock = 0;
        private static bool readyToRun = true;

        public static void LaunchMinecraftServer(string[] args)
        {
            if (readyToRun)
            {
                readyToRun = false;

                // Ensure Windows OS (due to windows specific paths)
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Console.WriteLine("Only windows supported.");
                    throw new PlatformNotSupportedException();
                }

                // look for latest java version
                string[] folders = Directory.GetDirectories(@"C:\Program Files\Java");
                string javaFolder = string.Empty;
                foreach (string folder in folders)
                    if (Path.GetFileName(folder).StartsWith("jdk-")
                        && folder.CompareTo(javaFolder) == 1
                        && File.Exists(@$"{folder}\bin\java.exe"))
                        javaFolder = folder;
                if (javaFolder == string.Empty)
                {
                    Console.WriteLine("Java Development Kit not found, Install it and try again");
                    return;
                }

                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.FileName = @$"{javaFolder}\bin\java.exe";
                p.StartInfo.Arguments = $"{string.Join(" ", args)} -jar server.jar".Trim();
                p.OutputDataReceived += Sessions.ServerEvent;
                Console.WriteLine("Launching Minecraft Server ...");
                p.Start();
                p.BeginOutputReadLine();
            }
        }

        public static void WaitForServerExit()
        {
            if (readyToRun)
                throw new Exception("Server not started");
            p.WaitForExit();
        }

        public static void Teleport(string user, Position position) => Teleport(user, position.W, position.X, position.Y, position.Z);
        public static void Teleport(string user, Demensions w, double x, double y, double z)
        {
            while (Interlocked.Exchange(ref processLock, 1) == 1) ;
            p.StandardInput.WriteLine($"execute as {user} run execute in minecraft:{w} run tp @s {x} {y} {z}");
            Interlocked.Exchange(ref processLock, 0);
        }

        public static void Tell(string user, params TellRawMessage[] message)
        {
            if (message == null || message.Length == 0) return;
            StringBuilder sb = new();
            sb.Append($@"tellraw {user} [{message[0]}");
            foreach (TellRawMessage trm in message.Skip(1))
                sb.Append($@",{trm}");
            sb.Append(']');
            while (Interlocked.Exchange(ref processLock, 1) == 1) ;
            p.StandardInput.WriteLine(sb.ToString());
            Interlocked.Exchange(ref processLock, 0);
        }

        public static void Kick(string user, string reason = "")
        {
            while (Interlocked.Exchange(ref processLock, 1) == 1) ;
            p.StandardInput.WriteLine($"kick {user} {reason}");
            Interlocked.Exchange(ref processLock, 0);
        }

        public static GameModes GameMode(string user, GameModes gameMode)
        {
            while (Interlocked.Exchange(ref processLock, 1) == 1) ;
            p.StandardInput.WriteLine($"gamemode {gameMode} {user}");
            Interlocked.Exchange(ref processLock, 0);
            return gameMode;
        }

        private static readonly double Day = TimeSpan.FromDays(1).TotalMilliseconds;
        private static readonly double Hour = TimeSpan.FromHours(1).TotalMilliseconds;
        private static readonly double Minute = TimeSpan.FromMinutes(1).TotalMilliseconds;
        private static readonly double Second = TimeSpan.FromSeconds(1).TotalMilliseconds;
        public static string NormalizeMilliseconds(double milliseconds)
        {
            StringBuilder sb = new();
            if (milliseconds > Day)
            {
                long div = (long)(milliseconds / Day);
                sb.Append($"{div}d");
                milliseconds -= (div * Day);
            }
            if (milliseconds > Hour)
            {
                long div = (long)(milliseconds / Hour);
                sb.Append($"{div}h");
                milliseconds -= (div * Hour);
            }
            if (milliseconds > Minute)
            {
                long div = (long)(milliseconds / Minute);
                sb.Append($"{div}m");
                milliseconds -= (div * Minute);
            }
            if (milliseconds > Second)
            {
                long div = (long)(milliseconds / Second);
                sb.Append($"{div}s");
                milliseconds -= (div * Second);
            }
            if (milliseconds >= 1.0)
                sb.Append($"{Math.Floor(milliseconds)}ms");
            return sb.ToString();
        }

        public static void Execute(string msg)
        {
            if (readyToRun)
                throw new Exception("Server not started");
            while (Interlocked.Exchange(ref processLock, 1) == 1) ;
            p.StandardInput.WriteLine(msg);
            Interlocked.Exchange(ref processLock, 0);
        }

        public static bool IsNameValid(string name)
        {
            const string validChars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_";
            if (name.Length < 3 || name.Length > 16)
                return false;
            foreach (char c in name)
                if (!validChars.Contains(c))
                    return false;
            return true;
        }

        public static int GetPort(this EndPoint? ep)
        {
            if (ep != null)
            {
                string? epStr = ep.ToString();
                if (epStr != null && IPEndPoint.TryParse(epStr, out IPEndPoint? ipep))
                    return ipep.Port;
            }
            return -1;
        }

        private const int spawnMin = -10000;
        private const int spawnMax = 9999;
        private const int spawnD = 2000;
        private static int spawnX = spawnMin;
        private static int spawnZ = spawnMin;
        private static int spawnLocationLock = 0;
        public static Position GenerateSpawnLocation()
        {
            while (Interlocked.Exchange(ref spawnLocationLock, 1) == 1) ;
            double x = spawnX * spawnD;
            double y = 19999999;
            double z = spawnZ * spawnD;
            spawnX++;
            if (spawnX > spawnMax)
            {
                spawnX = spawnMin;
                spawnZ++;
            }
            if (spawnZ > spawnMax)
            {
                spawnZ = spawnMin;
            }
            Interlocked.Exchange(ref spawnLocationLock, 0);
            return new(Demensions.Overworld, x, y, z);
        }

        public static byte[] SHA512(this string @this, Encoding encoding)
        {
            using SHA512 hash = System.Security.Cryptography.SHA512.Create();
            return hash.ComputeHash(encoding.GetBytes(@this));
        }

        public static string HexString(this byte[] ba) =>
            BitConverter.ToString(ba).Replace("-", "");

        public static string Base64(this byte[] ba) =>
            Convert.ToBase64String(ba);
    }
}
