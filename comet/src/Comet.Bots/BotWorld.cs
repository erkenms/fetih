namespace Comet.Bots
{
    using System;
    using System.Collections.Generic;
    using Comet.Client;

    public sealed class Seen
    {
        public uint Id;
        public string Name;
        public ushort X, Y;
        public bool Monster;
        public bool Dead;
    }

    // What a bot knows of the world, from the packets the server sends it
    public sealed class BotWorld
    {
        public const uint FirstMonster = 400001, LastMonster = 499999;

        public uint Me { get; }
        public uint MapId { get; private set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public uint Health { get; private set; }
        public uint MaxHealth { get; private set; }
        public bool Dead { get; private set; }
        public DateTime DiedAt { get; private set; }
        public Dictionary<uint, Seen> Around { get; } = new Dictionary<uint, Seen>();

        // Set when the server answers our jump; null while none is on its way
        public DateTime? JumpSentAt { get; set; }

        public BotWorld(uint me, ActionMessage spawn, uint health)
        {
            this.Me = me;
            this.MapId = spawn.Command;
            this.X = spawn.X;
            this.Y = spawn.Y;
            this.Health = this.MaxHealth = health;
        }

        public void Apply(byte[] packet, BotStats stats, DateTime now)
        {
            switch (BitConverter.ToUInt16(packet, 2))
            {
                case PacketTypes.MsgPlayer:
                {
                    var spawn = PlayerSpawn.Parse(packet);
                    if (spawn.Id == this.Me)
                        break;
                    bool monster = spawn.Id >= FirstMonster && spawn.Id <= LastMonster;
                    this.Around[spawn.Id] = new Seen
                    {
                        Id = spawn.Id, Name = spawn.Name, X = spawn.X, Y = spawn.Y, Monster = monster,
                        Dead = monster && spawn.MonsterHealth == 0,
                    };
                    break;
                }
                case PacketTypes.MsgWalk:
                {
                    var walk = WalkMessage.Parse(packet);
                    if (walk.CharacterId != this.Me && this.Around.TryGetValue(walk.CharacterId, out var seen))
                    {
                        int direction = (int)(walk.Direction % 8);
                        seen.X = (ushort)(seen.X + Steps.DeltaX[direction]);
                        seen.Y = (ushort)(seen.Y + Steps.DeltaY[direction]);
                    }
                    break;
                }
                case PacketTypes.MsgAction:
                {
                    var action = ActionMessage.Parse(packet);
                    if (action.Action == ActionMessage.RemoveEntity)
                        this.Around.Remove(action.CharacterId);
                    else if (action.Action == ActionMessage.Jump && action.CharacterId == this.Me)
                    {
                        if (this.JumpSentAt is DateTime sent)
                            stats.Echo(now - sent);
                        this.JumpSentAt = null;
                    }
                    else if (action.Action == ActionMessage.Jump && this.Around.TryGetValue(action.CharacterId, out var seen))
                    {
                        seen.X = (ushort)(action.Command & 0xFFFF);
                        seen.Y = (ushort)(action.Command >> 16);
                    }
                    else if (action.Action == ActionMessage.Teleport && action.CharacterId == this.Me)
                    {
                        // Also how the server takes back a move it refused
                        this.MapId = action.Command;
                        this.X = action.X;
                        this.Y = action.Y;
                        this.JumpSentAt = null;
                        this.Around.Clear();
                    }
                    break;
                }
                case PacketTypes.MsgInteract:
                {
                    var interact = InteractMessage.Parse(packet);
                    if (interact.Action != InteractMessage.Kill)
                        break;
                    if (interact.Target == this.Me)
                    {
                        this.Dead = true;
                        this.DiedAt = now;
                        stats.Died();
                    }
                    else if (this.Around.TryGetValue(interact.Target, out var victim))
                    {
                        victim.Dead = true;
                        if (interact.Sender == this.Me)
                            stats.Killed();
                    }
                    break;
                }
                case PacketTypes.MsgUserAttrib:
                {
                    var attrib = UserAttribMessage.Parse(packet);
                    if (attrib.Id != this.Me)
                        break;
                    if (attrib.Values.TryGetValue(UserAttribMessage.MaxHealth, out var max))
                        this.MaxHealth = (uint)max;
                    if (attrib.Values.TryGetValue(UserAttribMessage.Health, out var health))
                    {
                        this.Health = (uint)health;
                        this.MaxHealth = Math.Max(this.MaxHealth, this.Health);
                        if (this.Dead && health > 0)
                            this.Dead = false;
                    }
                    break;
                }
            }
        }
    }

    public static class Steps
    {
        public static readonly int[] DeltaX = { 0, -1, -1, -1, 0, 1, 1, 1 };
        public static readonly int[] DeltaY = { 1, 1, 0, -1, -1, -1, 0, 1 };

        public static int Distance(int x1, int y1, int x2, int y2) =>
            Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));

        public static int DirectionTo(int x, int y, int toX, int toY)
        {
            int dx = Math.Sign(toX - x), dy = Math.Sign(toY - y);
            for (int direction = 0; direction < 8; direction++)
                if (DeltaX[direction] == dx && DeltaY[direction] == dy)
                    return direction;
            return 0;
        }
    }
}
