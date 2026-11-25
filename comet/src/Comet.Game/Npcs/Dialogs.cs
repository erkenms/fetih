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
    // taking the step of the conversation. NPCs without one teleport, say their line, open
    // their shop or warehouse, or give a short default answer.
    public static class Dialogs
    {
        private static readonly Dictionary<uint, Func<NpcTalk, Task>> Scripts =
            new Dictionary<uint, Func<NpcTalk, Task>>
            {
                [TwinCity.GuardID] = TwinCity.GuardAsync,
                [Pk.Jail.WardenID] = Pk.Jail.WardenAsync,
                [Pk.Detaining.PrizeOfficerMarketID] = Pk.Detaining.PrizeOfficerAsync,
                [Pk.Detaining.PrizeOfficerJailID] = Pk.Detaining.PrizeOfficerAsync,
                [GuildDirector.ID] = GuildDirector.TalkAsync,
                [ClanManager.ID] = ClanManager.TalkAsync,
                [MountTrainer.ID] = MountTrainer.TalkAsync,
                [OfflineTrainingAdmin.ID] = OfflineTrainingAdmin.TalkAsync,
                [Upgraders.ArtisanID] = Upgraders.ArtisanAsync,
                [Upgraders.DragonBallUpgraderID] = Upgraders.DragonBallUpgraderAsync,
                [JobCenter.TrojanStarID] = JobCenter.TalkAsync,
                [JobCenter.WarriorGodID] = JobCenter.TalkAsync,
                [JobCenter.ArcherGodID] = JobCenter.TalkAsync,
                [JobCenter.WindSageID] = JobCenter.TalkAsync,
                [JobCenter.HeadAbbotID] = JobCenter.TalkAsync,
                [JobCenter.PirateLordID] = JobCenter.TalkAsync,
                [JobCenter.TaoistMoonID] = JobCenter.TalkAsync,
                [Training.TrainingGround.ExitID] = Training.TrainingGround.ExitAsync,
                [Rebirths.EternityID] = Rebirths.EternityAsync,
                [Rebirths.AlexID] = Rebirths.AlexAsync,
                [Rebirths.CelestialTaoID] = Rebirths.CelestialTaoAsync,
                [Rebirths.SeerID] = Rebirths.SeerAsync,
                [MarriageNpc.LoveStoneID] = MarriageNpc.LoveStoneAsync,
                [MarriageNpc.StarlitID] = MarriageNpc.StarlitAsync,
                [Market.MarkControllerID] = Market.MarkControllerAsync,
                [Market.CPAdminID] = Market.CPAdminAsync,
                [Market.SurgeonMiracleID] = Market.SurgeonMiracleAsync,
                [Market.NameRegistrarID] = Market.NameRegistrarAsync,
                [Market.RefineryStabilizerID] = Market.RefineryStabilizerAsync,
                [WuxingOven.OvenID] = WuxingOven.OvenAsync,
                [WuxingOven.EtherealID] = WuxingOven.EtherealAsync,
                [GuildWarNpc.ControllerID] = GuildWarNpc.ControllerAsync,
                [Social.GuildWar.LeftGateID] = GuildWarNpc.LeftGateAsync,
                [Social.GuildWar.RightGateID] = GuildWarNpc.RightGateAsync,
                [GuildWarNpc.BeastGuardID] = GuildWarNpc.BeastGuardAsync,
                [GuildWarNpc.FlagEnvoyID] = GuildWarNpc.FlagEnvoyAsync,
                [ElitePkEnvoy.ID] = ElitePkEnvoy.TalkAsync,
                [Tournaments.TeamPk.Team.ManagerID] = TeamPkManager.TalkAsync,
                [Tournaments.TeamPk.Skill.ManagerID] = TeamPkManager.TalkAsync,
                [Tournaments.GuildPk.ContestEmissaryID] = GuildPkNpcs.ContestEmissaryAsync,
                [Tournaments.GuildPk.PkEmissaryID] = GuildPkNpcs.PkEmissaryAsync,
            };

        static Dialogs()
        {
            foreach (var boxer in Training.TrainingGround.BoxerIDs)
                Scripts[boxer] = Training.TrainingGround.BoxerAsync;
            foreach (var assistant in Mines.AssistantIDs)
                Scripts[assistant] = Mines.AssistantAsync;
            foreach (var conductor in GuildWarNpc.Conductors.Keys)
                Scripts[conductor] = GuildWarNpc.ConductorAsync;
            foreach (var stone in GuildWarNpc.FlameStones.Keys)
                Scripts[stone] = GuildWarNpc.FlameStoneAsync;
            foreach (var master in SubClassMasters.AllIDs)
                Scripts[master] = SubClassMasters.TalkAsync;
            for (uint place = 0; place < Tournaments.GuildPkTable.Qualifiers; place++)
                Scripts[Tournaments.GuildPk.FirstAwarderID + place] = GuildPkNpcs.AwarderAsync;
        }

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
            else if (Kernel.Talkers.Find(npcID) is Talker said)
                await Talkers.TalkAsync(talk, said);
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
