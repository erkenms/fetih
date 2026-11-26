namespace Fetih.Game.Npcs
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.Packets;
    using Fetih.Game.World;

    // The mine union's assistants let players into a city's mine once they repeat the
    // proverb they greeted them with
    public static class Mines
    {
        private sealed record Assistant(string Proverb, string[] Answers, uint MapID, ushort X, ushort Y);

        // The right answer is the one equal to the proverb
        private static readonly Dictionary<uint, Assistant> Assistants = new Dictionary<uint, Assistant>
        {
            [4293] = new Assistant("Custom is a second nature.",
                new[] { "Custom is a first nature.", "Custom is the only nature.", "Custom is a third nature.",
                    "Custom is a second nature." },
                1028, 148, 113),
            [125] = new Assistant("Fire and water have no mercy.",
                new[] { "Fire and water have no end.", "Fire and water have no mercy.", "Fire and wind have no mercy.",
                    "Fire and water have no fear." },
                1025, 27, 69),
            [127] = new Assistant("God helps those who help themselves.",
                new[] { "God helps those who help others.", "God helps those who pray.",
                    "God helps those who help themselves.", "God helps those who wait." },
                1027, 136, 103),
        };

        public static IEnumerable<uint> AssistantIDs => Assistants.Keys;

        private const byte Quiz = 1, Again = 2, FirstAnswer = 10;
        private const byte Face = 9;

        public static async Task AssistantAsync(NpcTalk talk)
        {
            var assistant = Assistants[talk.Npc.NpcID];
            int answer = talk.Option - FirstAnswer;
            if (answer >= 0 && answer < assistant.Answers.Length)
            {
                if (assistant.Answers[answer] == assistant.Proverb)
                {
                    await Movement.TeleportAsync(talk.Client, assistant.MapID, assistant.X, assistant.Y);
                    return;
                }
                await talk.Text("Aw... wrong answer. Would you like to try again?")
                    .Choice("Okay, let me try again.", Again)
                    .Choice("I'd give it up.", MsgTaskDialog.NoOption)
                    .Face(Face).ShowAsync();
                return;
            }

            if (talk.Option == Quiz)
            {
                talk.Text("What proverb did I speak just now?");
                for (int i = 0; i < assistant.Answers.Length; i++)
                    talk.Choice(assistant.Answers[i], (byte)(FirstAnswer + i));
                await talk.Face(Face).ShowAsync();
                return;
            }

            await talk.Text($"{assistant.Proverb} Hello, I am the assistant of the mine union. If you want to enter " +
                    "the mine cave, you must answer me a question first.")
                .Choice("No problem.", Quiz)
                .Choice("What!", MsgTaskDialog.NoOption)
                .Face(Face).ShowAsync();
        }
    }
}
