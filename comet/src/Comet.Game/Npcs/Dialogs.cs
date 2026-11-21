namespace Comet.Game.Npcs
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using Comet.Game.World;

    // Finds the NPC a player talks to and runs its dialog. Each NPC's dialog is a method
    // taking the step of the conversation. NPCs without one teleport, open their shop or
    // warehouse, or give a short default answer.
    public static class Dialogs
    {
        private static readonly Dictionary<uint, Func<NpcTalk, Task>> Scripts =
            new Dictionary<uint, Func<NpcTalk, Task>>
            {
                [TwinCity.GuardID] = TwinCity.GuardAsync,
                [Pk.Jail.WardenID] = Pk.Jail.WardenAsync,
                [GuildDirector.ID] = GuildDirector.TalkAsync,
                [ClanManager.ID] = ClanManager.TalkAsync,
                [MountTrainer.ID] = MountTrainer.TalkAsync,
                [OfflineTrainingAdmin.ID] = OfflineTrainingAdmin.TalkAsync,
                [Upgraders.ArtisanID] = Upgraders.ArtisanAsync,
                [Upgraders.DragonBallUpgraderID] = Upgraders.DragonBallUpgraderAsync,
            };

        public static bool Has(uint npcID) => Scripts.ContainsKey(npcID);

        public static Task StartAsync(Client client, uint npcID)
        {
            client.ActiveNpc = 0;
            client.DialogAnswered = false;
            return RunAsync(client, npcID, 0, null);
        }

        // The client sends 255 right after every answer, as its window closes; only a 255
        // on its own (closing without answering) ends the conversation
        public static Task AnswerAsync(Client client, byte option, string input)
        {
            if (option == MsgTaskDialog.NoOption)
            {
                if (!client.DialogAnswered)
                    client.ActiveNpc = 0;
                client.DialogAnswered = false;
                return Task.CompletedTask;
            }
            client.DialogAnswered = true;
            if (client.ActiveNpc == 0)
                return Task.CompletedTask;
            return RunAsync(client, client.ActiveNpc, option, input);
        }

        public static Npc Reach(Client client, uint npcID, string use)
        {
            var character = client.Character;
            if (character == null)
                return null;
            var npc = Kernel.Npcs.Find(character.MapID, npcID);
            if (npc != null && GameMap<Client>.InView(character.X, character.Y, npc.X, npc.Y))
                return npc;
            LogEvents.For(typeof(Dialogs), LogEvents.NpcRejected).Warning(
                "{Name} can't reach NPC {NpcId} to {Use} from map {MapId} at {X},{Y}: {Reason}",
                character.Name, npcID, use, character.MapID, character.X, character.Y,
                npc == null ? "not_on_map" : "too_far");
            return null;
        }

        private static async Task RunAsync(Client client, uint npcID, byte option, string input)
        {
            var npc = Reach(client, npcID, "talk");
            if (npc == null)
                return;

            client.ActiveNpc = npcID;
            LogEvents.For(typeof(Dialogs), LogEvents.NpcTalk).Debug(
                "{Name} talked to NPC {NpcId} (option {Option})", client.Character.Name, npcID, option);
            var talk = new NpcTalk(client, npc, option, input);
            if (Scripts.TryGetValue(npcID, out var script))
                await script(talk);
            else if (Kernel.Teleporters.Find(npcID) is Teleporter teleporter)
                await Teleporters.TalkAsync(talk, teleporter);
            else if (Kernel.Shops.Find(npcID) != null)
                await talk.OpenShopAsync();
            else if (npc.Role == Npc.StorageRole)
                await talk.OpenWindowAsync(MsgAction.Window.Warehouse);
            else
                await DefaultAsync(talk);
        }

        private static Task DefaultAsync(NpcTalk talk) => talk
            .Text("I have nothing to say to you yet.")
            .Choice("I see.", MsgTaskDialog.NoOption)
            .ShowAsync();
    }
}
