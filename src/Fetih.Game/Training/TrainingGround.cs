namespace Fetih.Game.Training
{
    using System;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Combat;
    using Fetih.Game.Magic;
    using Fetih.Game.Npcs;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using Fetih.Game.World;

    // The training ground (1039): from level 20 the Boxers take players there for a fare, and
    // they gain experience hitting stakes with weapons and scarecrows with spells of their
    // level or below; weapon skills that go off in place of a swing hit stakes too. Nothing
    // wears or is used up there: durability, arrows, mana or stamina. A goal that runs out
    // of life stands again at once, full, and a hit on it is worth a tenth of the same damage
    // to a monster. Runs on the player's own queue, but goals' life is shared by all who hit
    // them.
    public static class TrainingGround
    {
        public const uint MapID = 1039;
        public const ushort EntranceX = 217, EntranceY = 215;
        public const byte MinLevel = 20;
        public const uint Fare = 1000;
        public const ulong ExperienceShare = 10;

        // The Boxers of Twin City, Phoenix Castle, Desert City, Ape Mountain and Bird Island,
        // and Boxer Huang in the ground, who sends players back
        public static readonly uint[] BoxerIDs = { 101607, 101619, 101621, 101623, 101625 };
        public const uint ExitID = 600075;

        private const byte Enter = 1, Leave = 1;
        private const byte BoxerFace = 20, ExitFace = 1;

        // Goals' life where it isn't full, by NPC ID
        private static readonly System.Collections.Generic.Dictionary<uint, uint> Lives =
            new System.Collections.Generic.Dictionary<uint, uint>();

        public static bool CostsNothing(Client client) => client.Character?.MapID == MapID;

        public static uint LifeOf(Npc goal)
        {
            lock (Lives)
                return Lives.TryGetValue(goal.NpcID, out var life) ? life : goal.Life;
        }

        // The damage comes off the goal's life; what it took, and whether it ran out and stands
        // again full
        private static (uint Dealt, bool Refilled) Wound(Npc goal, uint damage)
        {
            lock (Lives)
            {
                uint life = Lives.TryGetValue(goal.NpcID, out var left) ? left : goal.Life;
                if (damage < life)
                {
                    Lives[goal.NpcID] = life - damage;
                    return (damage, false);
                }
                Lives.Remove(goal.NpcID);
                return (life, true);
            }
        }

        public static Npc FindGoal(Client client, uint id) =>
            client.Character != null && Kernel.Npcs.Find(client.Character.MapID, id) is Npc npc && npc.IsGoal ? npc : null;

        // Why the player can't hit the goal, or null; a stake takes swings, a scarecrow spells
        public static string Problem(Client client, Npc goal, bool spell) =>
            !Takes(goal, spell) ? (spell ? "stake_needs_weapon" : "scarecrow_needs_spell") :
            goal.Level > client.Character.Level ? "goal_level" :
            null;

        public static bool Takes(Npc goal, bool spell) => goal.Role == (spell ? Npc.ScarecrowRole : Npc.StakeRole);

        // Goals in view that the player may hit
        public static System.Collections.Generic.IEnumerable<Npc> InView(Client client, bool spell)
        {
            var character = client.Character;
            foreach (var npc in Kernel.Npcs.InView(character.MapID, character.X, character.Y))
                if (npc.IsGoal && Problem(client, npc, spell) == null)
                    yield return npc;
        }

        public static ulong ExperienceOf(uint dealt, int level, Npc goal) =>
            dealt == 0 ? 0 : Math.Max(1, Damage.Experience(dealt, level, goal.Level) / ExperienceShare);

        // After what shows the hit was sent: the goal's life, experience, and from a swing as
        // much toward the weapons. A goal that ran out shows its life full again.
        public static async Task<ulong> AfterHitAsync(Client client, Npc goal, uint damage, string kind)
        {
            if (damage == 0)
                return 0;
            var (dealt, refilled) = Wound(goal, damage);
            if (refilled)
            {
                var full = new MsgUserAttrib(goal.NpcID, MsgUserAttrib.AttributeType.Health, goal.Life);
                await client.SendAsync(full);
                await Movement.SendToViewAsync(client, full);
            }
            ulong experience = ExperienceOf(dealt, client.Character.Level, goal);
            await Progress.AwardExperienceAsync(client, experience, "training_ground");
            if (kind is "melee" or "shoot")
                await Proficiency.GainAsync(client, (uint)Math.Min(experience, uint.MaxValue));
            LogEvents.For(typeof(TrainingGround), LogEvents.CombatHit).Debug(
                "{Name} hit goal {NpcId} (level {GoalLevel}) for {Damage}, {Experience} experience ({Kind})",
                client.Character.Name, goal.NpcID, goal.Level, damage, experience, kind);
            return experience;
        }

        public static async Task BoxerAsync(NpcTalk talk)
        {
            var client = talk.Client;
            var character = client.Character;
            if (talk.Option != Enter)
            {
                await talk
                    .Text("If you are level 20 or above, you may level up at the training grounds. It does")
                    .Text("not consume any durability, life and mana. Do you want me to teleport you")
                    .Text($"there? It is free for my guild members, and {Fare} silver for others.")
                    .Choice("Please teleport me there.", Enter)
                    .Choice("Just passing by.", MsgTaskDialog.NoOption)
                    .Face(BoxerFace)
                    .ShowAsync();
                return;
            }

            string refusal =
                character.Level < MinLevel ? $"Sorry, you must be level {MinLevel} or above to train there." :
                character.Silver < Fare ? $"Sorry, you do not have {Fare:N0} silver." :
                null;
            if (refusal != null)
            {
                await talk.Text(refusal).Choice("I see.", MsgTaskDialog.NoOption).Face(BoxerFace).ShowAsync();
                return;
            }

            var from = new MapPoint(character.MapID, character.X, character.Y);
            if (!await Movement.TeleportAsync(client, MapID, EntranceX, EntranceY))
                return;
            client.TrainingReturn = from;
            await Progress.ChangeSilverAsync(client, -Fare, "training_ground");
        }

        // Back where the player came from, or, after logging in there, where the map revives
        public static async Task ExitAsync(NpcTalk talk)
        {
            var client = talk.Client;
            if (talk.Option != Leave)
            {
                await talk
                    .Text("Although nothing will be consumed here, you cannot level up as fast as")
                    .Text("killing the monsters. Shall I teleport you out?")
                    .Choice("Yeah, please.", Leave)
                    .Choice("No, thanks.", MsgTaskDialog.NoOption)
                    .Face(ExitFace)
                    .ShowAsync();
                return;
            }

            var to = client.TrainingReturn ?? Kernel.MapTypes.RebornPointOf(MapID)
                ?? new MapPoint(StartPosition.MapID, StartPosition.X, StartPosition.Y);
            if (await Movement.TeleportAsync(client, to.MapID, to.X, to.Y))
                client.TrainingReturn = null;
        }
    }
}
