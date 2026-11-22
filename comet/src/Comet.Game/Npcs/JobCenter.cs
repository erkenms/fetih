namespace Comet.Game.Npcs
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.States;

    // The class masters of the Job Center, each promoting players of its own line
    public static class JobCenter
    {
        public const uint TrojanStarID = 10022, WarriorGodID = 10001, ArcherGodID = 400, WindSageID = 4972,
            HeadAbbotID = 8314, TaoistMoonID = 10000, PirateLordID = 10070;
        private const byte Promote = 1, Water = 2, Fire = 3;

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
                return SayAsync(talk, $"You are {A(Promotion.Name(current))}, the highest rank {A(master.Class)} can reach.");

            if (Promotion.ChoosesElement(current))
                return talk.Text($"You are {A(Promotion.Name(current))}. {Needs(current)} ")
                    .Text("A taoist chooses an element: water taoists heal and revive, fire taoists burn their " +
                        "foes. Once chosen, it can't be changed.")
                    .Choice("I choose water.", Water)
                    .Choice("I choose fire.", Fire)
                    .Choice("Let me think about it.", MsgTaskDialog.NoOption)
                    .ShowAsync();
            return talk.Text($"You are {A(Promotion.Name(current))}. {Needs(current)}")
                .Choice("Promote me.", Promote)
                .Choice("Not yet.", MsgTaskDialog.NoOption)
                .ShowAsync();
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
