namespace Comet.Client
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    /// <summary>A packet the real client sent, from a recorded packet trace.</summary>
    /// <param name="File">Capture file it came from</param>
    /// <param name="Server">"game" or "account"</param>
    /// <param name="ConnId">Connection within the capture (plus a billion per server restart)</param>
    /// <param name="Index">Position among the connection's client packets (0 is the first)</param>
    /// <param name="Type">Packet type</param>
    /// <param name="Action">Action of a MsgAction (offset 20) or MsgItem (offset 12), else null</param>
    /// <param name="Bytes">The packet as the server decrypted it, without the footer</param>
    public sealed record CapturedPacket(
        string File, string Server, long ConnId, int Index, ushort Type, uint? Action, byte[] Bytes)
    {
        /// <summary>
        /// True for the game server's key exchange reply: the first packet a game
        /// connection sends, which isn't a typed packet.
        /// </summary>
        public bool IsKeyExchange => this.Server == "game" && this.Index == 0;

        public override string ToString() =>
            $"{this.Type}{(this.Action is uint a ? $" action {a}" : "")} ({this.Bytes.Length} bytes, " +
            $"{Path.GetFileName(this.File)} connection {this.ConnId} #{this.Index})";
    }

    /// <summary>
    /// Reads recorded packet traces from the real 5619 client (see doc/Testing.md):
    /// JSON Lines files of packet.in and packet.out events, kept in tests/Captures/5619.
    /// </summary>
    public static class Captures
    {
        /// <summary>The capture folder, found above the running test's folder, or null.</summary>
        public static string FindDirectory()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Comet.slnx")))
                {
                    var captures = Path.Combine(dir.FullName, "tests", "Captures", "5619");
                    return Directory.Exists(captures) ? captures : null;
                }

            return null;
        }

        /// <summary>
        /// Loads the packets the client sent to one server in every capture file, in the
        /// order they were sent on each connection.
        /// </summary>
        /// <param name="directory">Capture folder</param>
        /// <param name="server">"game" or "account"</param>
        public static List<CapturedPacket> Load(string directory, string server)
        {
            var packets = new List<CapturedPacket>();
            if (directory == null)
                return packets;

            foreach (var file in Directory.GetFiles(directory, "*.jsonl").OrderBy(f => f))
            {
                var counts = new Dictionary<long, int>();
                int run = 0;
                foreach (var line in File.ReadLines(file))
                {
                    if (line.Trim().Length == 0)
                        continue;
                    using var json = JsonDocument.Parse(line);
                    var e = json.RootElement;
                    if (!e.TryGetProperty("server", out var s) || s.GetString() != server ||
                        !e.TryGetProperty("event", out var ev))
                        continue;

                    // Connection IDs start again when the server restarts
                    if (ev.GetString() == "server.start")
                    {
                        counts.Clear();
                        run++;
                        continue;
                    }

                    if (ev.GetString() != "packet.in")
                        continue;

                    long connId = e.GetProperty("connId").GetInt64() + run * 1_000_000_000L;
                    var bytes = Convert.FromHexString(e.GetProperty("hex").GetString());
                    counts.TryGetValue(connId, out int index);
                    counts[connId] = index + 1;

                    ushort type = bytes.Length >= 4 ? BitConverter.ToUInt16(bytes, 2) : (ushort)0;
                    uint? action = type switch
                    {
                        PacketTypes.MsgAction when bytes.Length >= 22 => BitConverter.ToUInt16(bytes, 20),
                        PacketTypes.MsgItem when bytes.Length >= 16 => BitConverter.ToUInt32(bytes, 12),
                        _ => null
                    };
                    packets.Add(new CapturedPacket(file, server, connId, index, type, action, bytes));
                }
            }

            return packets;
        }
    }
}
