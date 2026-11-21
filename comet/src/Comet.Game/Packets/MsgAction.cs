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

        // The window in Command and the player's position in the arguments
        public static MsgAction OpenWindow(uint identity, Window window, ushort x, ushort y) =>
            OpenWindow(identity, (uint)window, x, y, ActionType.ClientDialog);

        // ClientDialog or ClientCustomDialog, which number the client's windows separately
        public static MsgAction OpenWindow(uint identity, uint window, ushort x, ushort y, ActionType action) => new MsgAction
        {
            Type = PacketType.MsgAction,
            CharacterID = identity,
            Command = window,
            Arguments = new ushort[] { x, y },
            Timestamp = (uint)Environment.TickCount,
            Action = action,
        };

        // Command 32 opens a shop, with the shop's NPC in the timestamp field
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
                    await Training.OfflineTraining.LoginAsync(client);
                },

                [ActionType.LoginInventory] = async (msg, client) =>
                {
                    var unlocked = await Items.Locking.FinishUnlocksAsync(client);
                    var items = (client.Inventory?.Items ?? Enumerable.Empty<Database.Models.DbItem>())
                        .Concat(client.Equipment?.Items ?? Enumerable.Empty<Database.Models.DbItem>());
                    foreach (var item in items)
                        await client.SendAsync(new MsgItemInfo(item, MsgItemInfo.ItemAction.Add));
                    await Items.Locking.SendLoginAsync(client, unlocked);
                    await client.SendAsync(msg);
                    await client.SendAsync(MsgItem.DisplayGears(client.ID, client.Equipment));
                },

                [ActionType.LoginRelationships] = async (msg, client) =>
                {
                    await Social.Relationships.SendListsAsync(client);
                    await Social.TradePartners.SendListAsync(client);
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
                    await Social.Clans.LoginAsync(client);
                    await client.SendAsync(msg);
                },

                // The last login step: the client has loaded the map. Not echoed.
                [ActionType.LoginComplete] = async (msg, client) =>
                {
                    await Visibility.EnterAsync(client);
                    if (Pk.Pking.Status(client) != 0)
                        await Pk.Pking.SendStatusAsync(client);
                    await SecondPassword.LoginAsync(client);
                },

                [ActionType.CharacterPartnerInfo] = (msg, client) => Social.TradePartners.InfoAsync(client, msg),

                // Asks for the stat window, sent after login and each equipment change.
                // Answered with the player's own stats, not echoed.
                [ActionType.QueryStatInfo] = (msg, client) => client.Character == null
                    ? Task.CompletedTask
                    : client.SendAsync(MsgPlayerAttribInfo.Of(client)),

                // The client asks for a booth and opens its window on the
                // echo. The 5619 client sends 114 after each map load (login, teleport); a
                // booth doesn't outlive that.
                [ActionType.BoothSpawn] = (msg, client) => Items.Vending.OpenAsync(client, msg),
                [ActionType.BoothLeave] = (msg, client) => Items.Vending.CloseAsync(client, "closed"),

                // Sent with the client's mode (3, capture) while logging in, and by the PK mode
                // button; the client waits for the echo
                [ActionType.CharacterPkMode] = (msg, client) => Pk.Pking.SetModeAsync(client, msg),

                [ActionType.MapJump] = (msg, client) => Movement.JumpAsync(client, msg),

                // Non-zero Command asks to revive where the player lies
                [ActionType.CharacterRevive] = (msg, client) =>
                    Combat.Death.ReviveAsync(client, "button", here: msg.Command != 0),
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
                // The target's ID in Command; answered with the target's spawn,
                // each worn item and the spouse's name, not echoed
                [ActionType.CharacterObservation] = async (msg, client) =>
                {
                    if (client.Character == null || msg.Command == client.ID ||
                        !Kernel.Players.TryGet(msg.Command, out var target) ||
                        target.Character == null || target.Equipment == null)
                        return;
                    await client.SendAsync(new MsgPlayer(target));
                    foreach (var item in target.Equipment.Items)
                        await client.SendAsync(new MsgItemInfoEx(item, target.ID, 0, MsgItemInfoEx.ViewType.Equipment));
                    await client.SendAsync(MsgName.SpouseOf(target.ID));
                },
                [ActionType.CharacterAway] = (msg, client) =>
                {
                    if (client.Character == null)
                        return Task.CompletedTask;
                    client.Away = msg.Command != 0;
                    return Movement.SendToViewAsync(client, new MsgPlayer(client));
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

        public enum Window : uint
        {
            Warehouse = 4,
        }
    }
}