namespace Comet.Game.States
{
    using System.ComponentModel.DataAnnotations.Schema;
    using Comet.Game.Database.Models;

    public sealed class Character : DbCharacter
    {
        [NotMapped]
        public byte Direction { get; set; }

        // Standing, sitting or an emote
        [NotMapped]
        public uint Stance { get; set; }

        public Character(DbCharacter character)
            : base(character.AccountID, character.CharacterID, character.Name)
        {
            CopyStored(character, this);
        }

        // A copy for the database, so a save never holds the live character. A map that
        // isn't recorded (RecordDisable) saves its reborn point instead.
        public DbCharacter Snapshot()
        {
            var copy = new DbCharacter(this.AccountID, this.CharacterID, this.Name);
            CopyStored(this, copy);
            var reborn = Kernel.MapTypes.LoginPointOf(this.MapID);
            if (reborn != null)
                (copy.MapID, copy.X, copy.Y) = (reborn.MapID, reborn.X, reborn.Y);
            return copy;
        }

        private static void CopyStored(DbCharacter from, DbCharacter to)
        {
            to.Agility = from.Agility;
            to.AncestorClass = from.AncestorClass;
            to.AttributePoints = from.AttributePoints;
            to.Avatar = from.Avatar;
            to.CurrentClass = from.CurrentClass;
            to.EnlightenPoints = from.EnlightenPoints;
            to.Experience = from.Experience;
            to.Hairstyle = from.Hairstyle;
            to.HeavenBlessing = from.HeavenBlessing;
            to.OnlineTrainingExp = from.OnlineTrainingExp;
            to.BlessedHuntingExp = from.BlessedHuntingExp;
            to.OfflineTrainingSince = from.OfflineTrainingSince;
            to.OfflineReturnMapID = from.OfflineReturnMapID;
            to.OfflineReturnX = from.OfflineReturnX;
            to.OfflineReturnY = from.OfflineReturnY;
            to.ExpBallDay = from.ExpBallDay;
            to.ExpBallsUsed = from.ExpBallsUsed;
            to.SecondPassword = from.SecondPassword;
            to.HealthPoints = from.HealthPoints;
            to.Jewels = from.Jewels;
            to.KillPoints = from.KillPoints;
            to.Level = from.Level;
            to.ManaPoints = from.ManaPoints;
            to.MapID = from.MapID;
            to.Mesh = from.Mesh;
            to.PreviousClass = from.PreviousClass;
            to.QuizPoints = from.QuizPoints;
            to.Rebirths = from.Rebirths;
            to.FirstLifeLevel = from.FirstLifeLevel;
            to.SecondLifeLevel = from.SecondLifeLevel;
            to.Registered = from.Registered;
            to.Silver = from.Silver;
            to.Spirit = from.Spirit;
            to.StoredSilver = from.StoredSilver;
            to.Strength = from.Strength;
            to.VIPLevel = from.VIPLevel;
            to.Virtue = from.Virtue;
            to.Vitality = from.Vitality;
            to.X = from.X;
            to.Y = from.Y;
        }
    }

    public enum BodyType : ushort
    {
        AgileMale = 1003,
        MuscularMale = 1004,
        AgileFemale = 2001,
        MuscularFemale = 2002
    }

    public enum BaseClassType : ushort
    {
        Trojan = 10,
        Warrior = 20,
        Archer = 40,
        Ninja = 50,
        Monk = 60,
        Pirate = 70,
        Taoist = 100
    }
}