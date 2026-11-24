namespace Comet.Game.Social
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Comet.Game.Database.Models;

    public sealed class TotemEntry
    {
        public uint ItemID { get; }
        public uint CharacterID { get; }
        public string OwnerName { get; set; }
        public TotemPoleType PoleType { get; }
        public uint TypeID { get; set; }
        public byte Plus { get; set; }
        public byte Gem1 { get; set; }
        public byte Gem2 { get; set; }

        public TotemEntry(
            uint itemID, uint characterID, string ownerName, TotemPoleType poleType,
            uint typeID, byte plus, byte gem1, byte gem2)
        {
            this.ItemID = itemID;
            this.CharacterID = characterID;
            this.OwnerName = ownerName;
            this.PoleType = poleType;
            this.TypeID = typeID;
            this.Plus = plus;
            this.Gem1 = gem1;
            this.Gem2 = gem2;
        }

        public uint Donation(ArsenalConfig config) =>
            config.DonationOf(this.TypeID, this.Plus, this.Gem1, this.Gem2);

        public uint BattlePower =>
            ArsenalConfig.ItemBattlePower(this.TypeID, this.Plus, this.Gem1, this.Gem2);
    }

    public sealed class TotemPole
    {
        public TotemPoleType Type { get; }
        public byte Enhancement { get; set; }
        public uint EnhancementExpiration { get; set; }
        public Dictionary<uint, TotemEntry> Items { get; } = new Dictionary<uint, TotemEntry>();

        public ulong Donation { get; private set; }
        public byte DonationBattlePower { get; private set; }

        public TotemPole(TotemPoleType type) => this.Type = type;

        public byte ActiveEnhancement(uint today) =>
            this.Enhancement > 0 && this.EnhancementExpiration >= today ? this.Enhancement : (byte)0;

        public uint ActiveExpiration(uint today) =>
            this.ActiveEnhancement(today) > 0 ? this.EnhancementExpiration : 0u;

        public byte EffectiveBattlePower(uint today) =>
            (byte)Math.Min(ArsenalConfig.MaxPoleBattlePower, this.DonationBattlePower + this.ActiveEnhancement(today));

        public bool IsOverlord => ArsenalConfig.IsOverlord(this.Donation);

        public void Recalculate(ArsenalConfig config)
        {
            ulong sum = 0;
            foreach (var entry in this.Items.Values)
                sum += entry.Donation(config);
            this.Donation = sum;
            this.DonationBattlePower = ArsenalConfig.DonationBattlePower(sum);
        }

        public DbSyndicateTotemPole ToRow(uint guildID) => new DbSyndicateTotemPole
        {
            SyndicateID = guildID,
            Type = (byte)this.Type,
            Enhancement = this.Enhancement,
            EnhancementExpiration = this.EnhancementExpiration,
        };
    }

    public sealed class GuildArsenal
    {
        public Dictionary<TotemPoleType, TotemPole> Poles { get; } = new Dictionary<TotemPoleType, TotemPole>();
        public uint TotalBattlePower { get; private set; }
        public byte GuildLevel { get; private set; } = 1;

        public int UnlockedCount => this.Poles.Count;

        public TotemPole Get(TotemPoleType type) =>
            this.Poles.TryGetValue(type, out var pole) ? pole : null;

        public TotemPole Unlock(TotemPoleType type, byte enhancement = 0, uint expiration = 0)
        {
            var pole = new TotemPole(type)
            {
                Enhancement = enhancement,
                EnhancementExpiration = expiration,
            };
            this.Poles[type] = pole;
            return pole;
        }

        public void Recalculate(ArsenalConfig config, uint today, Dictionary<uint, GuildMember> members = null)
        {
            if (members != null)
                foreach (var member in members.Values)
                    member.ArsenalDonation = 0;

            int overlords = 0;
            foreach (var pole in this.Poles.Values)
            {
                pole.Recalculate(config);
                if (pole.IsOverlord)
                    overlords++;
                if (members != null)
                {
                    foreach (var entry in pole.Items.Values)
                    {
                        if (members.TryGetValue(entry.CharacterID, out var member))
                        {
                            ulong next = (ulong)member.ArsenalDonation + entry.Donation(config);
                            member.ArsenalDonation = (uint)Math.Min(next, uint.MaxValue);
                        }
                    }
                }
            }

            this.TotalBattlePower = (uint)this.Poles.Values
                .Select(p => (int)p.EffectiveBattlePower(today))
                .OrderByDescending(bp => bp)
                .Take(ArsenalConfig.TopPolesCount)
                .Sum();
            this.GuildLevel = (byte)(1 + overlords);
        }

        public uint MemberDonation(uint characterID, ArsenalConfig config)
        {
            ulong sum = 0;
            foreach (var pole in this.Poles.Values)
                foreach (var entry in pole.Items.Values)
                    if (entry.CharacterID == characterID)
                        sum += entry.Donation(config);
            return (uint)Math.Min(sum, uint.MaxValue);
        }
    }
}
