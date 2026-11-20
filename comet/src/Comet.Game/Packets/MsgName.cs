namespace Comet.Game.Packets
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // An ID or other number, the action as one byte, then a string list
    public sealed class MsgName : MsgBase<Client>
    {
        public uint Data { get; set; }
        public NameAction Action { get; set; }
        public List<string> Strings { get; set; } = new List<string>();

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Data = reader.ReadUInt32();
            this.Action = (NameAction)reader.ReadByte();
            this.Strings = reader.ReadStrings();
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)PacketType.MsgName);
            writer.Write(this.Data);
            writer.Write((byte)this.Action);
            writer.Write(this.Strings);
            return writer.ToArray();
        }

        public override async Task ProcessAsync(Client client)
        {
            if (Handlers.TryGetValue(this.Action, out var handler))
            {
                await handler(this, client);
                return;
            }

            await client.SendAsync(new MsgTalk(client.ID, MsgTalk.TalkChannel.Service,
                String.Format("Missing packet {0}, Action {1}, Length {2}",
                this.Type, this.Action, this.Length)));
            LogEvents.For(this.GetType(), LogEvents.PacketUnhandled).Warning(
                "Unhandled packet {PacketType} action {Action} ({Length} bytes): {Hex}",
                this.Type, this.Action, this.Length, Convert.ToHexString(this.Encode()));
        }

        // Asked by both sides of a whisper about the other one. The reply is the name and the details shown
        // in the whisper window: "ID level battle-power guild family spouse nobility male". The client drops
        // the first character of guild and family unless the field is just "#", hence the '#' prefix.
        public static string WhisperDetails(Character character) =>
            $"{character.CharacterID} {character.Level} {Attributes.BattlePower(character)} " +
            $"#{Social.Guilds.Of(character.CharacterID)?.Guild.Name ?? string.Empty} " +
            $"#{Social.Clans.Of(character.CharacterID)?.Clan.Name ?? string.Empty} None " +
            $"{(uint)Nobility.Peerage.RankOf(character.CharacterID)} " +
            $"{(character.Mesh % 10000 / 1000 == 1 ? 1 : 0)}";

        // No marriages yet: everyone's spouse is "None", as in MsgUserInfo
        public static MsgName SpouseOf(uint characterID) => new MsgName
        {
            Data = characterID,
            Action = NameAction.QuerySpouse,
            Strings = new List<string> { "None" },
        };

        private static readonly Dictionary<NameAction, Func<MsgName, Client, Task>> Handlers =
            new Dictionary<NameAction, Func<MsgName, Client, Task>>
            {
                // Not answered for a name that isn't online: the whisper itself tells the sender
                [NameAction.WhisperDetails] = (msg, client) =>
                {
                    var name = msg.Strings.Count > 0 ? msg.Strings[0] : string.Empty;
                    if (!Kernel.Players.TryGet(name, out var target) || target.Character == null)
                        return Task.CompletedTask;
                    msg.Strings = new List<string> { target.Character.Name, WhisperDetails(target.Character) };
                    return client.SendAsync(msg);
                },
                // Albetros (5517): asked about a player in view, for the gear window
                [NameAction.QuerySpouse] = (msg, client) => client.SendAsync(SpouseOf(msg.Data)),
                // The guild member list's kick button, with the member's name
                [NameAction.GuildKick] = (msg, client) =>
                    Social.Guilds.KickAsync(client, msg.Strings.Count > 0 ? msg.Strings[0] : null),
            };

        public static bool Handles(NameAction action) => Handlers.ContainsKey(action);

        public enum NameAction : byte
        {
            Fireworks = 1,
            GuildCreate,
            Guild,
            ChangeTitle,
            DeleteRole,
            Spouse,
            QueryNpc,
            Wanted,
            MapEffect,
            RoleEffect,
            GuildMembers,
            GuildKick,
            QueryWanted,
            QueryPoliceWanted,
            PoliceWanted,
            QuerySpouse,
            // A guild's ID and summary for the guild window's lists
            SetAlly = 21,
            SetEnemy,
            WhisperDetails = 26
        }
    }
}
