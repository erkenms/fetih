namespace Comet.Game.Npcs
{
    using System.Threading.Tasks;
    using Comet.Game.Packets;
    using Comet.Game.States;

    // The market's NPCs of rebirth: Eternity for the first, Alex for the second (an Exemption
    // Token in place of its quest)
    public static class Rebirths
    {
        public const uint EternityID = 300500, AlexID = 3600;

        // Option FirstClass + i chooses Rebirth.Classes[i], Confirm + i is reborn into it
        private const byte MoreClasses = 1, FirstClass = 10, Confirm = 20;
        private const int ClassesPerPage = 4;

        public static Task EternityAsync(NpcTalk talk) => TalkAsync(talk, 1, "a Celestial Stone");

        public static Task AlexAsync(NpcTalk talk) => TalkAsync(talk, 2, "an Exemption Token");

        private static async Task TalkAsync(NpcTalk talk, byte life, string item)
        {
            var character = talk.Client.Character;
            if (talk.Option >= Confirm && talk.Option < Confirm + Rebirth.Classes.Length)
            {
                byte chosen = Rebirth.Classes[talk.Option - Confirm];
                string reason = await Rebirth.RebirthAsync(talk.Client, life, chosen, talk.Npc.NpcID.ToString());
                if (reason == null)
                {
                    await SayAsync(talk, $"You are born again, {A(Promotion.Name(chosen))} of level {Rebirth.NewLevel}. " +
                        "Your gear has gone back to its lowest level, and some of your skills with it.");
                    return;
                }
                if (reason != "class")
                {
                    await SayAsync(talk, Explain(talk, life, item, reason));
                    return;
                }
            }

            string unmet = Rebirth.Unmet(talk.Client, life, Rebirth.Classes[0]);
            if (unmet != null && unmet != "no_item")
            {
                await SayAsync(talk, Explain(talk, life, item, unmet));
                return;
            }

            if (talk.Option >= FirstClass && talk.Option < FirstClass + Rebirth.Classes.Length)
            {
                int index = talk.Option - FirstClass;
                await talk.Text($"You will be {A(Promotion.Name(Rebirth.Classes[index]))} of level {Rebirth.NewLevel}, " +
                        $"and {item} will be gone. This can't be undone. Are you sure?")
                    .Choice("I am sure.", (byte)(Confirm + index))
                    .Choice("Let me think again.", MsgTaskDialog.NoOption)
                    .ShowAsync();
                return;
            }

            int first = talk.Option == MoreClasses ? ClassesPerPage : 0;
            if (first == 0)
                talk.Text(Needs(character.CurrentClass, life, item));
            talk.Text(" Which life will you live next?");
            for (int i = first; i < first + ClassesPerPage && i < Rebirth.Classes.Length; i++)
                talk.Choice(Promotion.Name(Rebirth.Classes[i]) + ".", (byte)(FirstClass + i));
            if (first == 0)
                talk.Choice("Other classes.", MoreClasses);
            await talk.Choice("Not now.", MsgTaskDialog.NoOption).ShowAsync();
        }

        private static string Needs(byte currentClass, byte life, string item) =>
            $"At level {Rebirth.RequiredLevel(currentClass)}, a master of your class may be born again at level " +
            $"{Rebirth.NewLevel}{(life == 1 ? "" : " a second time")}, in any class, for {item}. Worn gear " +
            "goes back to its lowest level and some skills stay behind, but the new life has more attribute points.";

        private static string[] Explain(NpcTalk talk, byte life, string item, string reason)
        {
            byte current = talk.Client.Character.CurrentClass;
            return reason switch
            {
                "max_rebirths" => new[] { "You have been reborn twice already. No one can be reborn a third time." },
                "other_rebirth" => new[] { life == 1
                    ? "You have been reborn already. Alex, here in the market, helps those seeking a second rebirth."
                    : "You must be reborn once before you seek a second rebirth. Eternity, here in the market, can help you." },
                "promotion" => new[] { "Only a master of a class can be reborn: get promoted to the highest rank first. ",
                    Needs(current, life, item) },
                "level" or "no_item" => new[] { Needs(current, life, item) },
                _ => new[] { "I can't help you now." },
            };
        }

        private static string A(string name) => ("AEIOU".Contains(name[0]) ? "an " : "a ") + name;

        // Each text is its own part of the dialog, as one can't be longer than 255 bytes
        private static Task SayAsync(NpcTalk talk, params string[] texts)
        {
            foreach (var text in texts)
                talk.Text(text);
            return talk.Choice("I see.", MsgTaskDialog.NoOption).ShowAsync();
        }
    }
}
