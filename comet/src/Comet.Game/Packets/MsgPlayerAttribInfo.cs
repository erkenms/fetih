namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.Combat;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // The stat window, 136 bytes of u32s: ID at 4, max life 8, max mana 12, max attack 16, min attack 20, defense 24, magic
    // attack 28, magic defense 32, dodge 36, agility 40, accuracy 44. From 48 percentages:
    // dragon and phoenix gems, magic defense, curse, bless, critical strike, skill critical
    // strike, immunity, penetration, block, breakthrough, counteraction, detoxication; from 100
    // final attack, final magic attack, final defense, final magic defense; from 116 metal,
    // wood, water, fire and earth resistance. Only what the server counts is filled in.
    // The 5619 client asks for it with the same packet, empty but for an ID: its own when
    // opening the stat pane, another player's when observing them.
    public sealed class MsgPlayerAttribInfo : MsgBase<Client>
    {
        public const int Size = 136;

        public uint Identity { get; private set; }
        public uint MaxHealth { get; }
        public uint MaxMana { get; }
        public uint Agility { get; }
        public Stats Stats { get; }

        public MsgPlayerAttribInfo() => base.Type = PacketType.MsgPlayerAttribInfo;

        public MsgPlayerAttribInfo(uint identity, uint maxHealth, uint maxMana, uint agility, Stats stats)
        {
            base.Type = PacketType.MsgPlayerAttribInfo;
            this.Identity = identity;
            this.MaxHealth = maxHealth;
            this.MaxMana = maxMana;
            this.Agility = agility;
            this.Stats = stats;
        }

        public static MsgPlayerAttribInfo Of(Client client) => new MsgPlayerAttribInfo(
            client.ID, Attributes.MaxHealth(client), Attributes.MaxMana(client),
            client.Character.Agility, Stats.Of(client));

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Identity = reader.ReadUInt32();
        }

        public override Task ProcessAsync(Client client)
        {
            if (client.Character == null)
                return Task.CompletedTask;
            var target = client;
            if (this.Identity != client.ID &&
                (!Kernel.Players.TryGet(this.Identity, out target) || target.Character == null))
                return Task.CompletedTask;
            return client.SendAsync(Of(target));
        }

        public override byte[] Encode()
        {
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write(this.Identity);                    // 4
            writer.Write(this.MaxHealth);                   // 8
            writer.Write(this.MaxMana);                     // 12
            writer.Write((uint)this.Stats.MaxAttack);       // 16
            writer.Write((uint)this.Stats.MinAttack);       // 20
            writer.Write((uint)this.Stats.Defense);         // 24
            writer.Write((uint)this.Stats.MagicAttack);     // 28
            writer.Write((uint)this.Stats.MagicDefense);    // 32
            writer.Write((uint)this.Stats.Dodge);           // 36
            writer.Write(this.Agility);                     // 40
            writer.Write(0u);                               // 44 accuracy
            writer.Write((uint)this.Stats.AttackPercent);   // 48
            writer.Write((uint)this.Stats.MagicPercent);    // 52
            writer.Write((uint)this.Stats.TortoisePercent); // 56 damage taken off
            writer.Write(0u);                               // 60 curse
            writer.Write((uint)this.Stats.BlessPercent);                // 64
            writer.Write((uint)(this.Stats.CriticalStrike * 100));      // 68 critical strike (0.01% units)
            writer.Write((uint)(this.Stats.SkillCriticalStrike * 100)); // 72 skill critical strike (0.01% units)
            writer.Write((uint)(this.Stats.Immunity * 100));            // 76 immunity (0.01% units)
            writer.Write((uint)(this.Stats.Penetration * 100));         // 80 penetration (0.01% units)
            writer.Write((uint)(this.Stats.Block * 100));               // 84 block (0.01% units)
            writer.Write((uint)(this.Stats.Breakthrough * 10));         // 88 breakthrough (0.1% units)
            writer.Write((uint)(this.Stats.Counteraction * 10));        // 92 counteraction (0.1% units)
            writer.Write((uint)this.Stats.Detoxication);                // 96 detoxication (% units)
            writer.Write(new byte[Size - 100]);                         // 100
            return writer.ToArray();
        }
    }
}
