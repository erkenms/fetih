namespace Comet.Game.States
{
    using System;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Threading.Tasks;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;

    public sealed class Character : DbCharacter
    {
        private DateTime LastSaveTimestamp { get; set; }

        // Direction the character faces (0 to 7), kept in memory only
        [NotMapped]
        public byte Direction { get; set; }

        // What the character is doing (standing, sitting, an emote), kept in memory only
        [NotMapped]
        public uint Stance { get; set; }

        public Character(DbCharacter character) 
            : base(character.AccountID, character.CharacterID, character.Name)
        {
            base.Agility = character.Agility;
            base.AncestorClass = character.AncestorClass;
            base.AttributePoints = character.AttributePoints;
            base.Avatar = character.Avatar;
            base.CurrentClass = character.CurrentClass;
            base.EnlightenPoints = character.EnlightenPoints;
            base.Experience = character.Experience;
            base.Hairstyle = character.Hairstyle;
            base.HealthPoints = character.HealthPoints;
            base.Jewels = character.Jewels;
            base.KillPoints = character.KillPoints;
            base.Level = character.Level;
            base.ManaPoints = character.ManaPoints;
            base.MapID = character.MapID;
            base.Mesh = character.Mesh;
            base.PreviousClass = character.PreviousClass;
            base.QuizPoints = character.QuizPoints;
            base.Rebirths = character.Rebirths;
            base.Registered = character.Registered;
            base.Silver = character.Silver;
            base.Spirit = character.Spirit;
            base.Strength = character.Strength;
            base.VIPLevel = character.VIPLevel;
            base.Virtue = character.Virtue;
            base.Vitality = character.Vitality;
            base.X = character.X;
            base.Y = character.Y;

            this.LastSaveTimestamp = DateTime.UtcNow;
        }

        public async Task SaveAsync(bool force = false)
        {
            DateTime now = DateTime.UtcNow;
            if (force || this.LastSaveTimestamp.AddMilliseconds(CharactersRepository.ThrottleMilliseconds) < now)
            {
                this.LastSaveTimestamp = now;
                await CharactersRepository.SaveAsync(this);
            }
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