namespace Comet.Game.Npcs
{
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.Social;
    using Comet.Game.States;
    using Comet.Game.World;

    // Guild War NPCs: the Twin City GuildController (380), the arena gates (6701, 6702),
    // the four Guild Conductors (101611..101617), the BeastGuard (101633) and the Flame Stone
    // chain (4454..4462) on map 1038.
    public static class GuildWarNpc
    {
        public const uint ControllerID = 380;
        public const uint BeastGuardID = 101633;

        private const byte ControllerFace = 123;
        private const byte GateFace = 7;
        private const byte ConductorFace = 130;
        private const byte FlameStoneFace = 67;

        private const byte EnterOption = 1, ClaimPrizeOption = 2, StatusOption = 3;
        private const byte GateEnterOption = 1, GateToggleOption = 2;
        private const byte ConductorTeleportOption = 1;
        private const uint ConductorFare = 1000;

        // Guild Conductors on map 1038: free for members of the ruling guild, 1,000 silver for others
        public static readonly Dictionary<uint, (string City, uint MapID, ushort X, ushort Y)> Conductors =
            new Dictionary<uint, (string, uint, ushort, ushort)>
            {
                [101611] = ("Phoenix Castle", 1011, 193, 266),
                [101613] = ("Ape City", 1020, 571, 585),
                [101615] = ("Desert City", 1000, 492, 669),
                [101617] = ("Bird Island", 1015, 744, 630),
            };

        // Flame Stones on map 1038: each exchanges the previous stone for the next
        public static readonly Dictionary<uint, (uint NeedItem, uint GiveItem)> FlameStones =
            new Dictionary<uint, (uint, uint)>
            {
                [4454] = (729961, 729962),
                [4455] = (729962, 729963),
                [4456] = (729963, 729964),
                [4457] = (729964, 729965),
                [4458] = (729965, 729966),
                [4459] = (729966, 729967),
                [4460] = (729967, 729968),
                [4462] = (729969, 729970),
            };

        public static async Task ControllerAsync(NpcTalk talk)
        {
            var client = talk.Client;
            switch (talk.Option)
            {
                case 0:
                    await talk
                        .Text("What can I do for you? The Guild War runs every week (" + GuildWar.ScheduleText + ").")
                        .Choice("Enter the guild area.", EnterOption)
                        .Choice("Claim the Guild War prize.", ClaimPrizeOption)
                        .Choice("Check Guild War status.", StatusOption)
                        .Choice("Just passing by.", MsgTaskDialog.NoOption)
                        .Face(ControllerFace)
                        .ShowAsync();
                    return;

                case EnterOption:
                    await Movement.TeleportAsync(client, GuildWar.MapID, 350, 339);
                    return;

                case ClaimPrizeOption:
                    var prizeText = GuildWar.PrizeConquerPoints.ToString("N0", CultureInfo.InvariantCulture);
                    var refused = await GuildWar.ClaimPrizeAsync(client);
                    await SayAsync(talk, ControllerFace, refused switch
                    {
                        null => $"Congratulations! You have claimed the Guild War prize of {prizeText} CPs.",
                        "war_active" => "The Guild War is still underway. Come back after it ends to claim the prize.",
                        "already_claimed" => "The Guild War prize has already been claimed.",
                        "cps_overflow" => "You cannot carry any more CPs.",
                        _ => "Only the leader of the guild that won the last Guild War can claim the prize.",
                    });
                    return;

                case StatusOption:
                    var holder = GuildWar.HolderGuildName;
                    var winner = GuildWar.WinnerGuildName;
                    string summary = GuildWar.Active
                        ? holder != null
                            ? $"Guild War is underway! {holder} currently holds the Guild Pole."
                            : "Guild War is underway! No guild holds the Guild Pole yet."
                        : winner != null
                            ? $"Guild War is not running ({GuildWar.ScheduleText}). {winner} won the last Guild War."
                            : $"Guild War is not running ({GuildWar.ScheduleText}). No guild holds the Guild Arena yet.";
                    await SayAsync(talk, ControllerFace, summary);
                    return;
            }
        }

        public static Task LeftGateAsync(NpcTalk talk) => GateAsync(talk, GuildWar.LeftGateID, 162, 199);

        public static Task RightGateAsync(NpcTalk talk) => GateAsync(talk, GuildWar.RightGateID, 209, 177);

        private static async Task GateAsync(NpcTalk talk, uint gateID, ushort insideX, ushort insideY)
        {
            var client = talk.Client;
            uint ruling = GuildWar.RulingGuildID;
            bool isMember = ruling != 0 && Guilds.Of(client.ID)?.Guild.ID == ruling;
            if (!isMember)
            {
                await SayAsync(talk, GateFace, "Only members of the guild holding the pole may pass through or operate this gate.");
                return;
            }

            bool open = GuildWar.IsGateOpen(gateID);
            switch (talk.Option)
            {
                case 0:
                    await talk
                        .Text("Your guild is currently Victorious. I can let you inside if you wish!")
                        .Choice("Let me in.", GateEnterOption)
                        .Choice(open ? "Close the Gate." : "Open the Gate.", GateToggleOption)
                        .Choice("No Thanks.", MsgTaskDialog.NoOption)
                        .Face(GateFace)
                        .ShowAsync();
                    return;

                case GateEnterOption:
                    await Movement.TeleportAsync(client, GuildWar.MapID, insideX, insideY);
                    return;

                case GateToggleOption:
                    bool nowOpen = await GuildWar.ToggleGateAsync(gateID, client.Character.Name);
                    await SayAsync(talk, GateFace, nowOpen ? "The gate is now open." : "The gate is now closed.");
                    return;
            }
        }

        public static async Task ConductorAsync(NpcTalk talk)
        {
            if (!Conductors.TryGetValue(talk.Npc.NpcID, out var dest))
                return;
            var client = talk.Client;
            uint ruling = GuildWar.RulingGuildID;
            bool free = ruling != 0 && Guilds.Of(client.ID)?.Guild.ID == ruling;

            if (talk.Option != ConductorTeleportOption)
            {
                await talk
                    .Text($"Are you heading for {dest.City}? It is free for our members, and 1,000 silver for others.")
                    .Choice("Please teleport me there.", ConductorTeleportOption)
                    .Choice("Just passing by.", MsgTaskDialog.NoOption)
                    .Face(ConductorFace)
                    .ShowAsync();
                return;
            }

            if (!free && client.Character.Silver < ConductorFare)
            {
                await SayAsync(talk, ConductorFace, "Sorry, you do not have enough silver.");
                return;
            }
            if (!await Movement.TeleportAsync(client, dest.MapID, dest.X, dest.Y))
                return;
            if (!free)
                await Progress.ChangeSilverAsync(client, -ConductorFare, "guild_conductor");
        }

        public static async Task BeastGuardAsync(NpcTalk talk)
        {
            var client = talk.Client;
            if (talk.Option == 1)
            {
                await Movement.TeleportAsync(client, 1002, 430, 380);
                return;
            }
            var holder = GuildWar.HolderGuildName ?? GuildWar.WinnerGuildName;
            var line = holder != null
                ? $"I guard the Guild Arena. {holder} currently rules this ground."
                : "I guard the Guild Arena. No guild rules this ground yet.";
            await talk
                .Text(line)
                .Choice("Send me back to Twin City.", 1)
                .Choice("I will stay.", MsgTaskDialog.NoOption)
                .Face(GateFace)
                .ShowAsync();
        }

        public static async Task FlameStoneAsync(NpcTalk talk)
        {
            if (!FlameStones.TryGetValue(talk.Npc.NpcID, out var pair))
                return;
            var client = talk.Client;
            if (!client.Inventory.Items.Any(i => i.TypeID == pair.NeedItem))
            {
                await SayAsync(talk, FlameStoneFace, "You do not have the required Flame Stone.");
                return;
            }
            if (!Kernel.ItemTypes.TryGet(pair.GiveItem, out var next))
            {
                await SayAsync(talk, FlameStoneFace, "The Flame Stone is silent.");
                return;
            }
            if (!await ItemActions.TakeAsync(client, pair.NeedItem, 1, "flame_stone"))
                return;
            if (await ItemActions.GiveAsync(client, next, "flame_stone") == null)
            {
                if (Kernel.ItemTypes.TryGet(pair.NeedItem, out var back))
                    await ItemActions.GiveAsync(client, back, "flame_stone_refund");
                await SayAsync(talk, FlameStoneFace, "Your inventory is full.");
                return;
            }
            await SayAsync(talk, FlameStoneFace, "The Flame Stone glows and grants you the next stone.");
        }

        private static Task SayAsync(NpcTalk talk, byte face, string text) => talk
            .Text(text)
            .Choice("I see.", MsgTaskDialog.NoOption)
            .Face(face)
            .ShowAsync();
    }
}
