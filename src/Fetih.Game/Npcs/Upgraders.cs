namespace Fetih.Game.Npcs
{
    using System.Threading.Tasks;
    using Fetih.Game.Items;
    using Fetih.Game.Packets;
    using static Fetih.Game.Items.Upgrading;

    // The market's two upgraders. Both work on worn gear, picked by slot, and never fail. The artisan raises quality for dragonballs and level for meteors, as many
    // as the upgrade's difficulty asks, and not past level 120; the other raises level for one
    // dragonball. Neither goes past the player's own level.
    public static class Upgraders
    {
        public const uint ArtisanID = 10062;
        public const uint DragonBallUpgraderID = 7050;

        // Options: a menu page, then a slot, then the confirmation, each slot's position added
        // to the step's base
        private const byte LevelMenu = 1, LevelMenuMore = 2, QualityMenu = 3, QualityMenuMore = 4, DegradeMenu = 5;
        private const byte LevelAsk = 20, LevelDo = 10, QualityAsk = 30, QualityDo = 40, More = 1, Back = 0;
        private const byte Face = 6, DragonBallFace = 28;

        public static async Task ArtisanAsync(NpcTalk talk)
        {
            byte option = talk.Option;
            switch (option)
            {
                case LevelMenu or QualityMenu:
                    await SlotsAsync(talk, Face, option == LevelMenu ? LevelAsk : QualityAsk, firstHalf: true,
                        option == LevelMenu ? LevelMenuMore : QualityMenuMore);
                    return;
                case LevelMenuMore or QualityMenuMore:
                    await SlotsAsync(talk, Face, option == LevelMenuMore ? LevelAsk : QualityAsk, firstHalf: false,
                        option == LevelMenuMore ? LevelMenu : QualityMenu);
                    return;
                case DegradeMenu:
                    await talk.OpenWindowAsync(MsgAction.Window.DegradeEquipment);
                    return;
            }

            if (Slot(option, LevelAsk) is { } levelSlot)
                await AskAsync(talk, levelSlot, Upgrade.Level, LevelDo);
            else if (Slot(option, QualityAsk) is { } qualitySlot)
                await AskAsync(talk, qualitySlot, Upgrade.Quality, QualityDo);
            else if (Slot(option, LevelDo) is { } levelDo)
                await DoAsync(talk, levelDo, Upgrade.Level, Meteor, MeteorCostOf(talk, levelDo), Face);
            else if (Slot(option, QualityDo) is { } qualityDo)
                await DoAsync(talk, qualityDo, Upgrade.Quality, DragonBall, DragonBallCostOf(talk, qualityDo), Face);
            else
                await talk.Text("Hello! I can always succeed in upgrading item quality, but I ask for a few more " +
                        "dragonballs than Artisan Wind. I can also upgrade or degrade item levels. How can I help you?")
                    .Choice("Upgrade Quality", QualityMenu)
                    .Choice("Upgrade Level", LevelMenu)
                    .Choice("Degrade Equipment", DegradeMenu)
                    .Face(Face).ShowAsync();
        }

        public static async Task DragonBallUpgraderAsync(NpcTalk talk)
        {
            byte option = talk.Option;
            if (option == More)
            {
                await talk.Text("He can't upgrade high-level equipment, but I can. What are you going to upgrade?")
                    .Choice("Ring", LevelAsk + (byte)ItemPosition.Ring)
                    .Choice("Boots", LevelAsk + (byte)ItemPosition.Boots)
                    .Choice("Previous Options", Back)
                    .Face(DragonBallFace).ShowAsync();
                return;
            }
            if (Slot(option, LevelAsk) is { } slot)
            {
                await DoAsync(talk, slot, Upgrade.Level, DragonBall, 1, DragonBallFace, capAtMaxLevel: false);
                return;
            }

            await talk.Text("MagicArtisan can always upgrade the level and quality of your equipment, but he " +
                    "can't upgrade high-level equipment. I charge one dragonball per level upgrade.")
                .Choice("Head Gear", LevelAsk + (byte)ItemPosition.Headwear)
                .Choice("Necklace", LevelAsk + (byte)ItemPosition.Necklace)
                .Choice("Armor", LevelAsk + (byte)ItemPosition.Armor)
                .Choice("Main Weapon", LevelAsk + (byte)ItemPosition.RightHand)
                .Choice("Secondary Weapon", LevelAsk + (byte)ItemPosition.LeftHand)
                .Choice("More Options", More)
                .Face(DragonBallFace).ShowAsync();
        }

        private static Task SlotsAsync(NpcTalk talk, byte face, byte step, bool firstHalf, byte other)
        {
            talk.Text("What item type would you like to upgrade?");
            if (firstHalf)
                talk.Choice("Head Gear", (byte)(step + (byte)ItemPosition.Headwear))
                    .Choice("Necklace", (byte)(step + (byte)ItemPosition.Necklace))
                    .Choice("Armor", (byte)(step + (byte)ItemPosition.Armor))
                    .Choice("Main Weapon", (byte)(step + (byte)ItemPosition.RightHand))
                    .Choice("Secondary Weapon", (byte)(step + (byte)ItemPosition.LeftHand))
                    .Choice("More Options", other);
            else
                talk.Choice("Ring", (byte)(step + (byte)ItemPosition.Ring))
                    .Choice("Boots", (byte)(step + (byte)ItemPosition.Boots))
                    .Choice("Previous Options", other);
            return talk.Face(face).ShowAsync();
        }

        private static async Task AskAsync(NpcTalk talk, ItemPosition slot, Upgrade upgrade, byte doStep)
        {
            var (item, _, reason) = Check(talk.Client, slot, upgrade, capAtMaxLevel: true);
            if (reason != null)
            {
                await SayAsync(talk, Explain(reason), Face);
                return;
            }
            string cost = upgrade == Upgrade.Level
                ? $"{MeteorCost(item.TypeID)} meteors"
                : $"{DragonBallCost(item.TypeID)} dragonballs";
            await talk.Text($"This upgrade will cost {cost}. Is this ok?")
                .Choice("Yes!", (byte)(doStep + (byte)slot))
                .Choice("No thanks.", MsgTaskDialog.NoOption)
                .Face(Face).ShowAsync();
        }

        private static async Task DoAsync(NpcTalk talk, ItemPosition slot, Upgrade upgrade, uint gemType, int cost,
            byte face, bool capAtMaxLevel = true)
        {
            string reason = await UpgradeAsync(talk.Client, slot, upgrade, gemType, cost, capAtMaxLevel,
                talk.Npc.NpcID.ToString());
            await SayAsync(talk, reason == null ? "It's done. Enjoy your new equipment!" : Explain(reason, cost, gemType), face);
        }

        // The cost asked for may have changed since; it's worked out again from the gear worn now
        private static int MeteorCostOf(NpcTalk talk, ItemPosition slot) =>
            talk.Client.Equipment?[slot] is { } item ? MeteorCost(item.TypeID) : 0;

        private static int DragonBallCostOf(NpcTalk talk, ItemPosition slot) =>
            talk.Client.Equipment?[slot] is { } item ? DragonBallCost(item.TypeID) : 0;

        // The slot an option names under a step's base: base plus the slot's position
        private static ItemPosition? Slot(byte option, byte step) =>
            option > step && option - step is var position && position <= (byte)ItemPosition.Boots &&
            System.Array.IndexOf(Slots, (ItemPosition)position) >= 0 ? (ItemPosition)position : null;

        private static Task SayAsync(NpcTalk talk, string text, byte face) => talk
            .Text(text)
            .Choice("I see.", MsgTaskDialog.NoOption)
            .Face(face).ShowAsync();
    }
}
