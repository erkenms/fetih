namespace Fetih.Game.Npcs
{
    using System.Threading.Tasks;
    using Fetih.Game.Packets;
    using Fetih.Game.States;

    // The market's NPCs of rebirth: Eternity for the first, Alex for the second (an Exemption
    // Token in place of its quest) and CelestialTao putting a reborn player's attribute
    // points back to allot
    public static class Rebirths
    {
        public const uint EternityID = 300500, AlexID = 3600, CelestialTaoID = 350050, SeerID = 46;

        // Option FirstClass + i chooses Rebirth.Classes[i], Confirm + i is reborn into it
        private const byte MoreClasses = 1, FirstClass = 10, Confirm = 20, Reallot = 1, ConfirmReallot = 2;
        private const byte OpenReincarnation = 1, WhereDew = 2, SeerFace = 6;
        private const int ClassesPerPage = 4;

        public static Task EternityAsync(NpcTalk talk) => TalkAsync(talk, 1, "a Celestial Stone");

        public static Task AlexAsync(NpcTalk talk) => TalkAsync(talk, 2, "an Exemption Token");

        public static Task SeerAsync(NpcTalk talk)
        {
            if (talk.Option == OpenReincarnation)
                return talk.OpenWindowAsync(MsgAction.Window.Reincarnation);
            if (talk.Option == WhereDew)
                return talk.Text("You can get Oblivion Dew from the Shopping Mall or special quests.")
                    .Choice("I see.", MsgTaskDialog.NoOption)
                    .Face(SeerFace).ShowAsync();

            return talk.Text("From now on, have total control of your life! If you've been Reborn twice and are at least Level 110, just bring me some Oblivion Dew and I'll help you become Reincarnated! ")
                .Text("After the Reincarnation, your class skills will revert to level 1, but they'll return to their previous levels when you reach Level 110, again. ")
                .Text("If your classes have been the same for all three lives, including your current one, you can learn the Pure Skills from your class trainer, in the job center.")
                .Choice("I want to get Reincarnated.", OpenReincarnation)
                .Choice("Where can I get the Dew?", WhereDew)
                .Choice("Not now.", MsgTaskDialog.NoOption)
                .Face(SeerFace).ShowAsync();
        }

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

        public static async Task CelestialTaoAsync(NpcTalk talk)
        {
            var client = talk.Client;
            string reason = Rebirth.ReallotUnmet(client.Character);
            if (reason == null && talk.Option == ConfirmReallot)
                reason = await Rebirth.ReallotAsync(client, talk.Npc.NpcID.ToString());
            if (reason == null && talk.Option == ConfirmReallot)
            {
                await SayAsync(talk, $"Done. You have {client.Character.AttributePoints} attribute points to allot again.");
                return;
            }
            if (reason == "not_reborn")
            {
                await SayAsync(talk, "Only those who have been reborn can allot their attribute points again.");
                return;
            }
            if (reason == "level")
            {
                await SayAsync(talk, "Come back at level 70 and I'll let you allot your attribute points again.");
                return;
            }
            if (talk.Option == Reallot)
            {
                await talk.Text("Your strength, agility, vitality and spirit will go back to where every life starts, " +
                        "and all the rest will be points to allot again. Are you sure?")
                    .Choice("I am sure.", ConfirmReallot)
                    .Choice("Not now.", MsgTaskDialog.NoOption)
                    .ShowAsync();
                return;
            }
            await talk.Text("A reborn hero may allot their attribute points again, once past level 70.")
                .Choice("Let me allot them again.", Reallot)
                .Choice("Not now.", MsgTaskDialog.NoOption)
                .ShowAsync();
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
