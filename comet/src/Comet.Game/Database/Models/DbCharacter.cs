namespace Comet.Game.Database.Models
{
    using System;
    using System.ComponentModel.DataAnnotations.Schema;

    [Table("character")]
    public partial class DbCharacter
    {
        public DbCharacter(uint accountID, string name)
        {
            this.AccountID = accountID;
            this.Name = name;
        }

        public DbCharacter(uint accountID, uint characterID, string name)
        {
            this.AccountID = accountID;
            this.CharacterID = characterID;
            this.Name = name;
        }

        public virtual uint CharacterID { get; private set; }
        public virtual uint AccountID { get; private set; }
        public virtual string Name { get; private set; }
        public virtual string Spouse { get; set; } = "None";

        public void Rename(string name) => this.Name = name;
        public virtual uint Mesh { get; set; }
        public virtual ushort Avatar { get; set; }
        public virtual ushort Hairstyle { get; set; }
        public virtual uint Silver { get; set; }
        public virtual uint Jewels { get; set; }

        // Silver kept in the warehouses (one purse for all of them)
        public virtual uint StoredSilver { get; set; }
        public virtual byte CurrentClass { get; set; }
        public virtual byte PreviousClass { get; set; }
        public virtual byte AncestorClass { get; set; }
        public virtual byte Rebirths { get; set; }

        // The level each past life was reborn at, or 0
        public virtual byte FirstLifeLevel { get; set; }
        public virtual byte SecondLifeLevel { get; set; }
        public virtual byte Level { get; set; }
        public virtual ulong Experience { get; set; }
        public virtual uint MapID { get; set; }
        public virtual ushort X { get; set; }
        public virtual ushort Y { get; set; }
        public virtual uint Virtue { get; set; }
        public virtual ushort Strength { get; set; }
        public virtual ushort Agility { get; set; }
        public virtual ushort Vitality { get; set; }
        public virtual ushort Spirit { get; set; }
        public virtual ushort AttributePoints { get; set; }
        public virtual ushort HealthPoints { get; set; }
        public virtual ushort ManaPoints { get; set; }
        public virtual ushort KillPoints { get; set; }
        public virtual ushort EnlightenPoints { get; set; }
        public virtual uint QuizPoints { get; set; }
        public virtual uint VIPLevel { get; set; }

        // When the heaven blessing runs out (UTC); null if never blessed or expired
        public virtual DateTime? HeavenBlessing { get; set; }

        // Heaven blessing EXP not yet claimed, in MsgGodExp units (6,000,000 an EXP ball)
        public virtual uint OnlineTrainingExp { get; set; }
        public virtual uint BlessedHuntingExp { get; set; }

        // Offline training: since when (UTC), and where to go back to after claiming
        public virtual DateTime? OfflineTrainingSince { get; set; }
        public virtual uint OfflineReturnMapID { get; set; }
        public virtual ushort OfflineReturnX { get; set; }
        public virtual ushort OfflineReturnY { get; set; }

        // EXP balls used on the server's local day ExpBallDay
        public virtual DateTime? ExpBallDay { get; set; }
        public virtual byte ExpBallsUsed { get; set; }

        // Server-local day the character last sent their daily free flower
        public virtual DateTime? FlowerSentDay { get; set; }

        // Asked before warehouses and unlocking items; 0 if none is set
        public virtual uint SecondPassword { get; set; }

        // Until when (UTC) a moderator's /mute keeps the player from chatting; null if not muted
        public virtual DateTime? MutedUntil { get; set; }
        public virtual bool NameChanged { get; set; }
        public virtual byte ActiveSubClass { get; set; }
        public virtual ulong StudyPoints { get; set; }
        public virtual uint LuckyTime { get; set; }
        public virtual DateTime Registered { get; set; }
    }
}
