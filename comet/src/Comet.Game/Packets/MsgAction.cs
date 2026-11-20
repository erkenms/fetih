namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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

        public static MsgAction Turn(uint identity, byte direction) => new MsgAction
        {
            Type = PacketType.MsgAction,
            CharacterID = identity,
            Arguments = new ushort[2],
            Timestamp = (uint)Environment.TickCount,
            Action = ActionType.CharacterDirection,
            Direction = direction
        };

        public static MsgAction LevelUp(uint identity) => new MsgAction
        {
            Type = PacketType.MsgAction,
            CharacterID = identity,
            Arguments = new ushort[2],
            Timestamp = (uint)Environment.TickCount,
            Action = ActionType.CharacterLevelUp
        };

        // Albetros (5517): the window in Command and the player's position in the arguments
        public static MsgAction OpenWindow(uint identity, Window window, ushort x, ushort y) => new MsgAction
        {
            Type = PacketType.MsgAction,
            CharacterID = identity,
            Command = (uint)window,
            Arguments = new ushort[] { x, y },
            Timestamp = (uint)Environment.TickCount,
            Action = ActionType.ClientDialog,
        };

        // Albetros (5517): action 160 with 32 in Command opens a shop; the shop's NPC goes in
        // the timestamp field and the player's position in X and Y
        public static MsgAction OpenShop(uint identity, uint npcID, ushort x, ushort y) => new MsgAction
        {
            Type = PacketType.MsgAction,
            CharacterID = identity,
            Command = 32,
            Arguments = new ushort[2],
            Timestamp = npcID,
            Action = ActionType.ClientCustomDialog,
            X = x,
            Y = y,
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
                    await client.SendAsync(MsgMapInfo.For(msg.Command));
                },

                [ActionType.LoginInventory] = async (msg, client) =>
                {
                    var items = (client.Inventory?.Items ?? Enumerable.Empty<Database.Models.DbItem>())
                        .Concat(client.Equipment?.Items ?? Enumerable.Empty<Database.Models.DbItem>());
                    foreach (var item in items)
                        await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Add));
                    await client.SendAsync(msg);
                    await client.SendAsync(MsgItem.DisplayGears(client.ID, client.Equipment));
                },

                [ActionType.LoginRelationships] = async (msg, client) =>
                {
                    await Social.Relationships.SendListsAsync(client);
                    await client.SendAsync(msg);
                },

                [ActionType.LoginProficiencies] = async (msg, client) =>
                {
                    await Magic.Proficiency.SendAllAsync(client);
                    await client.SendAsync(msg);
                },

                [ActionType.LoginSpells] = async (msg, client) =>
                {
                    await Magic.Casting.SendAllAsync(client);
                    await client.SendAsync(msg);
                },

                [ActionType.LoginGuild] = async (msg, client) =>
                {
                    await Social.Guilds.LoginAsync(client);
                    await client.SendAsync(msg);
                },

                // The last login step: the client has loaded the map. Not echoed.
                [ActionType.LoginComplete] = async (msg, client) =>
                {
                    await Visibility.EnterAsync(client);
                    if (Pk.Pking.Status(client) != 0)
                        await Pk.Pking.SendStatusAsync(client);
                },

                // Asks for the stat window (1040), sent after login and each equipment
                // change. Not answered until there are stats to show.
                [ActionType.QueryStatInfo] = (msg, client) => Task.CompletedTask,

                // Albetros (5517): the client asks for a booth and opens its window on the
                // echo. The 5619 client sends 114 after each map load (login, teleport); a
                // booth doesn't outlive that.
                [ActionType.BoothSpawn] = (msg, client) => Items.Vending.OpenAsync(client, msg),
                [ActionType.BoothLeave] = (msg, client) => Items.Vending.CloseAsync(client, "closed"),

                // Sent with the client's mode (3, capture) while logging in, and by the PK mode
                // button; the client waits for the echo
                [ActionType.CharacterPkMode] = (msg, client) => Pk.Pking.SetModeAsync(client, msg),

                [ActionType.MapJump] = (msg, client) => Movement.JumpAsync(client, msg),

                // The Revive button; refused until the player has been dead long enough
                [ActionType.CharacterRevive] = (msg, client) => Combat.Death.ReviveAsync(client, "button"),
                [ActionType.CharacterDead] = (msg, client) => Combat.Death.BecomeGhostAsync(client),
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
                // The 5619 client sits down (250) on opening a booth; standing up ends it
                [ActionType.CharacterEmote] = async (msg, client) =>
                {
                    if (client.Character == null)
                        return;
                    client.Character.Stance = msg.Command;
                    client.Character.Direction = (byte)(msg.Direction % 8);
                    msg.CharacterID = client.ID;
                    await Movement.SendToViewAsync(client, msg);
                    if (msg.Command != Items.Vending.SitStance)
                        await Items.Vending.CloseAsync(client, "stood_up");
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
            ClientCustomDialog = 160,
            CharacterAway = 161,
            MapPathfinding,
            LoginComplete = 251,
            QueryStatInfo = 408
        }

        // Windows opened with ClientDialog
        public enum Window : uint
        {
            Warehouse = 4,
        }
    }
}