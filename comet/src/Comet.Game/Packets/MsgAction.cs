namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.States;
    using Comet.Game.World;
    using Comet.Network.Packets;

    public sealed class MsgAction : MsgBase<Client>
    {
        public uint CharacterID { get; set; }
        public uint Command { get; set; }
        public ushort[] Arguments { get; set; }
        public uint Timestamp { get; set; }
        public ActionType Action { get; set; }
        public ushort Direction { get; set; }
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public uint Map { get; set; }
        public uint Color { get; set; }
        public ushort Extra { get; set; }

        public static MsgAction Teleport(uint identity, uint mapID, ushort x, ushort y, byte direction) =>
            new MsgAction
            {
                Type = PacketType.MsgAction,
                CharacterID = identity,
                Command = mapID,
                Arguments = new ushort[2],
                Timestamp = (uint)Environment.TickCount,
                Action = ActionType.MapTeleport,
                Direction = direction,
                X = x,
                Y = y
            };

        public static MsgAction RemoveEntity(uint identity) => new MsgAction
        {
            Type = PacketType.MsgAction,
            CharacterID = identity,
            Arguments = new ushort[2],
            Timestamp = (uint)Environment.TickCount,
            Action = ActionType.MapRemoveSpawn
        };

        // While logging in, the client sends actions 74 to 78, 96 and 97, each after the reply to
        // the one before, then 251 (LoginComplete) with ID 0, which isn't answered.
        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.CharacterID = reader.ReadUInt32();
            this.Command = reader.ReadUInt32();
            this.Arguments = new ushort[2];
            for (int i = 0; i < this.Arguments.Length; i++)
                this.Arguments[i] = reader.ReadUInt16();
            this.Timestamp = reader.ReadUInt32();
            this.Action = (ActionType)reader.ReadUInt16();
            this.Direction = reader.ReadUInt16();
            this.X = reader.ReadUInt16();
            this.Y = reader.ReadUInt16();
            this.Map = reader.ReadUInt32();
            this.Color = reader.ReadUInt32();
            if (this.Length >= 38)
                this.Extra = reader.ReadUInt16();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.CharacterID);
            writer.Write(this.Command);
            for (int i = 0; i < this.Arguments.Length; i++)
                writer.Write(this.Arguments[i]);
            writer.Write(this.Timestamp);
            writer.Write((ushort)this.Action);
            writer.Write(this.Direction);
            writer.Write(this.X);
            writer.Write(this.Y);
            writer.Write(this.Map);
            writer.Write(this.Color);
            writer.Write(this.Extra);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            if (Handlers.TryGetValue(this.Action, out var handler))
            {
                await handler(this, client);
                return;
            }

            // Not handled yet: echo it, since the client waits for the reply to each
            // login step (75 to 78, 96, 97) before sending the next
            await client.SendAsync(this);
            await client.SendAsync(new MsgTalk(client.ID, MsgTalk.TalkChannel.Service,
                String.Format("Missing packet {0}, Action {1}, Length {2}",
                this.Type, this.Action, this.Length)));
            LogEvents.For(this.GetType(), LogEvents.PacketUnhandled).Warning(
                "Unhandled packet {PacketType} action {Action} ({Length} bytes): {Hex}",
                this.Type, this.Action, this.Length, Convert.ToHexString(this.Encode()));
        }

        private static readonly Dictionary<ActionType, Func<MsgAction, Client, Task>> Handlers =
            new Dictionary<ActionType, Func<MsgAction, Client, Task>>
            {
                [ActionType.LoginSpawn] = async (msg, client) =>
                {
                    msg.CharacterID = client.ID;
                    msg.Command = client.Character.MapID;
                    msg.X = client.Character.X;
                    msg.Y = client.Character.Y;
                    await client.SendAsync(msg);
                },

                // The last login step: the client has loaded the map. Not echoed.
                [ActionType.LoginComplete] = (msg, client) => Visibility.EnterAsync(client),

                [ActionType.MapJump] = (msg, client) => Movement.JumpAsync(client, msg),
                [ActionType.MapPortal] = (msg, client) => Movement.PortalAsync(client, msg),

                // Turning and emotes go to everyone in view, not back to the player
                [ActionType.CharacterDirection] = (msg, client) =>
                {
                    if (client.Character == null)
                        return Task.CompletedTask;
                    client.Character.Direction = (byte)(msg.Direction % 8);
                    msg.CharacterID = client.ID;
                    return Movement.SendToViewAsync(client, msg);
                },
                [ActionType.CharacterEmote] = (msg, client) =>
                {
                    if (client.Character == null)
                        return Task.CompletedTask;
                    client.Character.Stance = msg.Command;
                    client.Character.Direction = (byte)(msg.Direction % 8);
                    msg.CharacterID = client.ID;
                    return Movement.SendToViewAsync(client, msg);
                },
            };

        public static bool Handles(ActionType action) => Handlers.ContainsKey(action);

        public enum ActionType
        {
            LoginSpawn = 74,
            LoginInventory,
            LoginRelationships,
            LoginProficiencies,
            LoginSpells,
            CharacterDirection,
            CharacterEmote = 81,
            MapPortal = 85,
            MapTeleport,
            CharacterLevelUp = 92,
            SpellAbortXp,
            CharacterRevive,
            CharacterDelete,
            CharacterPkMode,
            LoginGuild,
            MapMine = 99,
            MapTeamLeaderStar = 101,
            MapQuery,
            MapSkyColor = 104,
            MapTeamMemberStar = 106,
            MapKickBack = 108,
            SpellRemove,
            ProficiencyRemove,
            BoothSpawn,
            BoothSuspend,
            BoothResume,
            BoothLeave,
            ClientCommand = 116,
            CharacterObservation,
            SpellAbortTransform,
            SpellAbortFlight = 120,
            MapGold,
            RelationshipsEnemy = 123,
            ClientDialog = 126,
            MapEffect = 133,
            LoginOfflineMessages,
            MapRemoveSpawn = 135,
            MapJump = 137,
            CharacterDead = 145,
            MapTeleportEnd = 146,
            RelationshipsFriend = 148,
            CharacterAvatar = 151,
            CharacterPartnerInfo,
            CharacterAway = 161,
            MapPathfinding,
            LoginComplete = 251
        }
    }
}