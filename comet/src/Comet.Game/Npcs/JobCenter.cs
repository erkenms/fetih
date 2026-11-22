namespace Comet.Game.Npcs
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Magic;
    using Comet.Game.Packets;
    using Comet.Game.States;

    // The class masters of the Job Center, each promoting players of its own line and teaching
    // them its line's skills
    public static class JobCenter
    {
        public const uint TrojanStarID = 10022, WarriorGodID = 10001, ArcherGodID = 400, WindSageID = 4972,
            HeadAbbotID = 8314, TaoistMoonID = 10000, PirateLordID = 10070;
        private const byte Promote = 1, Water = 2, Fire = 3, Teach = 4;

        // Option FirstSkill + i learns the i-th skill the master teaches
        private const byte FirstSkill = 10;

        // As many skills as a page of the dialog lists
        private const int SkillsPerPage = 6;

        private sealed record Master(byte Line, string Class);

        private static readonly Dictionary<uint, Master> Masters = new Dictionary<uint, Master>
        {
            [TrojanStarID] = new Master(1, "trojan"),
            [WarriorGodID] = new Master(2, "warrior"),
            [ArcherGodID] = new Master(4, "archer"),
            [WindSageID] = new Master(5, "ninja"),
            [HeadAbbotID] = new Master(6, "monk"),
            [PirateLordID] = new Master(7, "pirate"),
            [TaoistMoonID] = new Master(10, "taoist"),
        };

        public static async Task TalkAsync(NpcTalk talk)
        {
            var master = Masters[talk.Npc.NpcID];
            if (talk.Option == Teach || talk.Option >= FirstSkill)
            {
                await TeachAsync(talk, master);
                return;
            }
            byte element = talk.Option switch { Water => Promotion.WaterTaoist, Fire => Promotion.FireTaoist, _ => 0 };
            if (talk.Option is Promote or Water or Fire)
            {
                string reason = await Promotion.PromoteAsync(talk.Client, master.Line, element, talk.Npc.NpcID.ToString());
                if (reason == null)
                {
                    await SayAsync(talk, $"Congratulations! You are now {A(Promotion.Name(talk.Client.Character.CurrentClass))}.");
                    return;
                }
                if (reason is "level" or "no_item")
                {
                    await SayAsync(talk, Needs(talk.Client.Character.CurrentClass));
                    return;
                }
            }
            await GreetAsync(talk, master);
        }

        private static Task GreetAsync(NpcTalk talk, Master master)
        {
            byte current = talk.Client.Character.CurrentClass;
            if (Promotion.Line(current) != master.Line)
                return SayAsync(talk, $"I only promote {master.Class}s. Your own master is somewhere in this hall.");
            if (Promotion.Rank(current) >= Promotion.MaxRank)
                return talk.Text($"You are {A(Promotion.Name(current))}, the highest rank {A(master.Class)} can reach.")
                    .Choice("Teach me skills.", Teach)
                    .Choice("I see.", MsgTaskDialog.NoOption)
                    .ShowAsync();

            if (Promotion.ChoosesElement(current))
                return talk.Text($"You are {A(Promotion.Name(current))}. {Needs(current)} ")
                    .Text("A taoist chooses an element: water taoists heal and revive, fire taoists burn their " +
                        "foes. Once chosen, it can't be changed.")
                    .Choice("I choose water.", Water)
                    .Choice("I choose fire.", Fire)
                    .Choice("Teach me skills.", Teach)
                    .Choice("Let me think about it.", MsgTaskDialog.NoOption)
                    .ShowAsync();
            return talk.Text($"You are {A(Promotion.Name(current))}. {Needs(current)}")
                .Choice("Promote me.", Promote)
                .Choice("Teach me skills.", Teach)
                .Choice("Not yet.", MsgTaskDialog.NoOption)
                .ShowAsync();
        }

        // Lists the skills of the master's line the player can learn now, and learns the one chosen
        private static async Task TeachAsync(NpcTalk talk, Master master)
        {
            var client = talk.Client;
            if (Promotion.Line(client.Character.CurrentClass) != master.Line)
            {
                await SayAsync(talk, $"I only teach {master.Class}s. Your own master is somewhere in this hall.");
                return;
            }

            var taught = Learning.TaughtBy(master.Line);
            if (talk.Option >= FirstSkill)
            {
                int index = talk.Option - FirstSkill;
                if (index >= taught.Count)
                    return;
                ushort magic = taught[index];
                string reason = await Learning.LearnAsync(client, magic, talk.Npc.NpcID.ToString());
                if (reason == null)
                {
                    await SayAsync(talk, $"You have learned {Learning.NameOf(magic)}.");
                    return;
                }
                if (reason != "learned")
                {
                    await SayAsync(talk, Learning.Explain(reason, magic));
                    return;
                }
            }

            var ready = taught.Select((magic, i) => (Magic: magic, Index: i))
                .Where(s => Learning.Unmet(client, s.Magic) == null)
                .Take(SkillsPerPage)
                .ToList();
            if (ready.Count == 0)
            {
                await SayAsync(talk, NothingToTeach(client, taught));
                return;
            }
            talk.Text("Which skill do you want to learn?");
            foreach (var (magic, i) in ready)
                talk.Choice(Learning.NameOf(magic), (byte)(FirstSkill + i));
            await talk.Choice("Not now.", MsgTaskDialog.NoOption).ShowAsync();
        }

        private static string NothingToTeach(Client client, IReadOnlyList<ushort> taught)
        {
            var next = taught
                .Where(m => Learning.Unmet(client, m) == "level")
                .OrderBy(Learning.RequiredLevel)
                .Select(m => (ushort?)m)
                .FirstOrDefault();
            if (next is ushort magic)
                return $"I have nothing more to teach you yet. At level {Learning.RequiredLevel(magic)} " +
                    $"I can teach you {Learning.NameOf(magic)}.";
            return "I have nothing more to teach you.";
        }

        private static string Needs(byte current)
        {
            byte level = Promotion.RequiredLevel(current);
            string next = Promotion.ChoosesElement(current) ? "a water or fire taoist"
                : A(Promotion.Name((byte)(current + 1)));
            return Promotion.RequiredItem(current) switch
            {
                Promotion.Emerald => $"At level {level}, bring me an Emerald and I'll promote you to {next}.",
                Promotion.Meteor => $"At level {level}, bring me a Meteor and I'll promote you to {next}.",
                Promotion.MoonBox => $"At level {level}, bring me a MoonBox and I'll promote you to {next}.",
                _ => $"At level {level} I can promote you to {next}.",
            };
        }

        private static string A(string name) => ("AEIOU".Contains(name[0]) ? "an " : "a ") + name;

        private static Task SayAsync(NpcTalk talk, string text) => talk
            .Text(text)
            .Choice("I see.", MsgTaskDialog.NoOption)
            .ShowAsync();
    }
}
