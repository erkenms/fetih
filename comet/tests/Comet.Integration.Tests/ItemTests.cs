namespace Comet.Integration.Tests
{
    using System.Threading.Tasks;
    using Comet.Client;
    using Xunit;

    public class ItemTests : IntegrationTest
    {
        public ItemTests(ServerFixture servers) : base(servers) { }

        [Fact]
        public async Task GivenItemsAreSentAndKeptAfterLogout()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 620, 540, GameMaster);
            Assert.Empty(player.Inventory);

            await CommandAsync(player, "/item " + ServerFixture.Blade.Id);
            var item = await player.Game.WaitForItemAsync(i => i.TypeId == ServerFixture.Blade.Id);
            Assert.NotNull(item);
            Assert.Equal((ServerFixture.Blade.Amount, ServerFixture.Blade.Amount, (ushort)1, (ushort)0),
                (item.Amount, item.AmountLimit, item.Action, item.Position));

            var logged = await this.Servers.WaitForEventAsync("game", "item.created", "name", player.User.Name);
            Assert.Equal((item.Id, ServerFixture.Blade.Id, "gm"), (logged.GetProperty("itemId").GetUInt32(),
                logged.GetProperty("typeId").GetUInt32(), logged.GetProperty("reason").GetString()));

            await this.LogOutAsync(player);
            await using var again = await this.Servers.PlayAsync(email, Password);
            var kept = Assert.Single(again.Inventory);
            Assert.Equal((item.Id, ServerFixture.Blade.Id), (kept.Id, kept.TypeId));
        }

        [Fact]
        public async Task TheInventoryHoldsFortyItems()
        {
            var (player, email) = await this.PlayingAtAsync(1002, 640, 540, GameMaster);
            await using var _ = player;
            await CommandAsync(player, $"/item {ServerFixture.Blade.Id} 39");
            await CommandAsync(player, $"/item {ServerFixture.Blade.Id} 2");
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text.Contains("inventory is full")));

            var count = await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id", ("@id", player.User.Id));
            Assert.Equal(40L, (long)count);
        }

        [Fact]
        public async Task UnknownTypesAreRefused()
        {
            await using var player = (await this.PlayingAtAsync(1002, 660, 540, GameMaster)).Player;
            await CommandAsync(player, "/item 999");
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text.Contains("no item type 999")));
        }

        [Fact]
        public async Task TeleportScrollsGoToTheirCityAndAreUsedUp()
        {
            await using var player = (await this.PlayingAtAsync(1006, 30, 30, GameMaster)).Player;
            var scroll = await GiveAsync(player, ServerFixture.TwinCityGate.Id);

            await player.Game.UseAsync(scroll.Id);
            var moved = await player.Game.WaitForActionAsync(ActionMessage.Teleport, player.User.Id);
            Assert.Equal((1002u, (ushort)430, (ushort)380), (moved?.Command, moved?.X, moved?.Y));
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, scroll.Id));
            var used = await this.Servers.WaitForEventAsync("game", "item.used", "name", player.User.Name);
            Assert.Equal(scroll.Id, used.GetProperty("itemId").GetUInt32());
            var count = await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id", ("@id", player.User.Id));
            Assert.Equal(0L, (long)count);
        }

        [Fact]
        public async Task TeleportScrollsDontWorkInJail()
        {
            await using var player = (await this.PlayingAtAsync(6000, 30, 30, GameMaster)).Player;
            var scroll = await GiveAsync(player, ServerFixture.TwinCityGate.Id);

            await player.Game.UseAsync(scroll.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "item.use_rejected", "name", player.User.Name);
            Assert.Equal("teleport_disabled", refused.GetProperty("reason").GetString());
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text.Contains("can't teleport")));
        }

        [Fact]
        public async Task PotionsRestoreHealthAndManaAndAreUsedUp()
        {
            var (first, email) = await this.PlayingAtAsync(1002, 680, 540, GameMaster);
            var name = first.User.Name;
            await this.LogOutAsync(first);
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE `character` SET HealthPoints = 10, ManaPoints = 0, Spirit = 100 WHERE Name = @name", ("@name", name));
            await using var player = await this.Servers.PlayAsync(email, Password);

            var potion = await GiveAsync(player, ServerFixture.Potion.Id);
            await player.Game.UseAsync(potion.Id);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.Health, out var hp) && hp == 10u + ServerFixture.Potion.Life));
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, potion.Id));
            var used = await this.Servers.WaitForEventAsync("game", "item.used", "name", name);
            Assert.Equal((potion.Id, 70, 0), (used.GetProperty("itemId").GetUInt32(),
                used.GetProperty("health").GetInt32(), used.GetProperty("mana").GetInt32()));

            var manaPotion = await GiveAsync(player, ServerFixture.ManaPotion.Id);
            await player.Game.UseAsync(manaPotion.Id);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.Mana, out var mp) && mp == ServerFixture.ManaPotion.Mana));
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, manaPotion.Id));
            var count = await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id", ("@id", player.User.Id));
            Assert.Equal(0L, (long)count);
        }

        [Fact]
        public async Task PotionsThatWouldRestoreNothingAreKept()
        {
            await using var player = (await this.PlayingAtAsync(1002, 700, 540, GameMaster)).Player;
            var potion = await GiveAsync(player, ServerFixture.Potion.Id);

            await player.Game.UseAsync(potion.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "item.use_rejected", "name", player.User.Name);
            Assert.Equal("full", refused.GetProperty("reason").GetString());
            Assert.NotNull(await this.Servers.QueryGameScalarAsync(
                "SELECT ItemID FROM item WHERE ItemID = @id", ("@id", potion.Id)));
        }

        [Fact]
        public async Task WornItemsRaiseMaxHealthAndTakingThemOffCutsHealth()
        {
            var (first, email) = await this.PlayingAtAsync(1002, 720, 540, GameMaster);
            var name = first.User.Name;
            ushort health = first.User.HealthPoints;
            var necklace = await GiveAsync(first, ServerFixture.Necklace.Id);
            await this.LogOutAsync(first);
            await this.Servers.ExecuteGameSqlAsync("UPDATE item SET Plus = 1 WHERE ItemID = @id", ("@id", necklace.Id));
            await using var player = await this.Servers.PlayAsync(email, Password);
            uint raised = health + (uint)ServerFixture.NecklaceHealth;

            // Putting it on raises only the maximum
            await player.Game.EquipAsync(necklace.Id, 2);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.MaxHealth, out var max) && max == raised));

            // A potion fills up to the raised maximum, and taking it off cuts health down again
            var potion = await GiveAsync(player, ServerFixture.Potion.Id);
            await player.Game.UseAsync(potion.Id);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.Health, out var hp) && hp == raised));

            await player.Game.UnequipAsync(necklace.Id, 2);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.Health, out var hp) && hp == health));
        }

        [Fact]
        public async Task WornItemsRaiseMaxManaAndTakingThemOffCutsMana()
        {
            await using var player = (await this.PlayingAtAsync(1002, 740, 540, GameMaster)).Player;
            uint mana = player.User.Spirit * 5u;
            var bottle = await GiveAsync(player, ServerFixture.Bottle.Id);

            await player.Game.EquipAsync(bottle.Id, 7);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.MaxMana, out var max) && max == mana + ServerFixture.Bottle.Mana));

            var potion = await GiveAsync(player, ServerFixture.ManaPotion.Id);
            await player.Game.UseAsync(potion.Id);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.Mana, out var mp) && mp == mana + ServerFixture.ManaPotion.Mana));

            await player.Game.UnequipAsync(bottle.Id, 7);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.Mana, out var mp) && mp == mana));
        }

        [Fact]
        public async Task ExpBallsGiveLevelUpTimeAndAreKeptAtTheCap()
        {
            await using var player = (await this.PlayingAtAsync(1002, 740, 940, GameMaster)).Player;
            var ball = await GiveAsync(player, ServerFixture.ExpBall.Id);
            await player.Game.UseAsync(ball.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, ball.Id));
            var used = await this.Servers.WaitForEventAsync("game", "item.used", "name", player.User.Name);
            Assert.True(used.GetProperty("experience").GetUInt64() > 0);

            await CommandAsync(player, "/level 120");
            var kept = await GiveAsync(player, ServerFixture.ExpBall.Id);
            await player.Game.UseAsync(kept.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "item.use_rejected", "name", player.User.Name);
            Assert.Equal("max_level", refused.GetProperty("reason").GetString());
        }

        [Fact]
        public async Task MeteorScrollsOpenIntoTenMeteors()
        {
            await using var player = (await this.PlayingAtAsync(1002, 760, 940, GameMaster)).Player;
            var scroll = await GiveAsync(player, ServerFixture.MeteorScroll.Id);
            await player.Game.UseAsync(scroll.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, scroll.Id));
            await this.Servers.WaitForEventAsync("game", "item.used", "name", player.User.Name);
            var meteors = await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id AND TypeID = @type",
                ("@id", player.User.Id), ("@type", ServerFixture.Meteor.Id));
            Assert.Equal(10L, (long)meteors);
        }

        [Fact]
        public async Task OnlyTenExpBallsADay()
        {
            var (first, email) = await this.PlayingAtAsync(1002, 780, 940, GameMaster);
            var name = first.User.Name;
            await this.LogOutAsync(first);
            await this.Servers.ExecuteGameSqlAsync(
                "UPDATE `character` SET ExpBallDay = @today, ExpBallsUsed = 9 WHERE Name = @name",
                ("@today", System.DateTime.UtcNow.Date), ("@name", name));
            await using var player = await this.Servers.PlayAsync(email, Password);

            var tenth = await GiveAsync(player, ServerFixture.ExpBall.Id);
            await player.Game.UseAsync(tenth.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, tenth.Id));

            var eleventh = await GiveAsync(player, ServerFixture.ExpBall.Id);
            await player.Game.UseAsync(eleventh.Id);
            var refused = await this.Servers.WaitForEventAsync("game", "item.use_rejected", "name", name);
            Assert.Equal("daily_limit", refused.GetProperty("reason").GetString());
            Assert.NotNull(await player.Game.WaitForTalkAsync(t => t.Text.Contains("10 EXP balls a day")));
        }

        [Fact]
        public async Task ExpPotionActivatesDoubleExpAndSurvivesRelog()
        {
            var (first, email) = await this.PlayingAtAsync(1002, 700, 940, GameMaster);
            var potion = await GiveAsync(first, ServerFixture.ExpPotion.Id);
            await first.Game.UseAsync(potion.Id);
            Assert.NotNull(await first.Game.WaitForItemActionAsync(ItemMessage.Remove, potion.Id));
            var attrib = await first.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.DoubleExp, out var low) && (low >> 32) > 3500 &&
                a.ValuesHigh.TryGetValue(UserAttribMessage.DoubleExp, out var hi) && (hi >> 32) == 200);
            Assert.NotNull(attrib);

            await this.LogOutAsync(first);
            await using var again = await this.Servers.PlayAsync(email, Password);
            var restored = await again.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.DoubleExp, out var low) && (low >> 32) > 3500 &&
                a.ValuesHigh.TryGetValue(UserAttribMessage.DoubleExp, out var hi) && (hi >> 32) == 200);
            Assert.NotNull(restored);
        }

        [Fact]
        public async Task PenitenceAmuletAndPkPotionReducePkPointsWithRules()
        {
            await using var player = (await this.PlayingAtAsync(1002, 710, 940, GameMaster)).Player;
            var amulet = await GiveAsync(player, ServerFixture.PenitenceAmulet.Id);

            // Needs at least 30 PK points
            await player.Game.UseAsync(amulet.Id);
            var lowPk = await this.Servers.WaitForEventAsync("game", "item.use_rejected", "name", player.User.Name);
            Assert.Equal("pk_points", lowPk.GetProperty("reason").GetString());

            // Works at 40 PK points -> 10
            await CommandAsync(player, "/pk 40");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.PkPoints, out var pk) && pk == 40));
            await player.Game.UseAsync(amulet.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, amulet.Id));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.PkPoints, out var pk) && pk == 10));

            // Second amulet within an hour hits cooldown
            await CommandAsync(player, "/pk 40");
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.PkPoints, out var pk) && pk == 40));
            var second = await GiveAsync(player, ServerFixture.PenitenceAmulet.Id);
            int mark = this.Servers.Mark();
            await player.Game.UseAsync(second.Id);
            var cd = await this.Servers.WaitForEventAsync("game", "item.use_rejected", "name", player.User.Name, after: mark);
            Assert.Equal("cooldown", cd.GetProperty("reason").GetString());

            // AtonementPotion has no cooldown and reduces by 10
            var potion = await GiveAsync(player, ServerFixture.AtonementPotion.Id);
            await player.Game.UseAsync(potion.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, potion.Id));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.PkPoints, out var pk) && pk == 30));
        }

        [Fact]
        public async Task HairDyeChangesHairColorForSelfAndViewer()
        {
            await using var player = (await this.PlayingAtAsync(1002, 720, 940, GameMaster)).Player;
            await using var viewer = (await this.PlayingAtAsync(1002, 722, 940, GameMaster)).Player;
            ushort style = (ushort)(player.User.Hairstyle % 100);
            uint dyeType = player.User.Hairstyle / 100 == 3 ? ServerFixture.WhiteDye.Id : ServerFixture.BlackDye.Id;
            ushort expected = (ushort)((dyeType == ServerFixture.WhiteDye.Id ? 9 : 3) * 100 + style);

            var dye = await GiveAsync(player, dyeType);
            await player.Game.UseAsync(dye.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, dye.Id));
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.Hairstyle, out var h) && h == expected));
            Assert.NotNull(await viewer.Game.WaitForAttribAsync(a =>
                a.Id == player.User.Id && a.Values.TryGetValue(UserAttribMessage.Hairstyle, out var h) && h == expected));
        }

        [Fact]
        public async Task FireworksBroadcastBurstAndTextToViewers()
        {
            await using var player = (await this.PlayingAtAsync(1002, 730, 940, GameMaster)).Player;
            await using var viewer = (await this.PlayingAtAsync(1002, 732, 940, GameMaster)).Player;

            var firework = await GiveAsync(player, ServerFixture.Firework.Id);
            await player.Game.UseAsync(firework.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Fireworks, player.User.Id));
            Assert.NotNull(await viewer.Game.WaitForItemActionAsync(ItemMessage.Fireworks, player.User.Id));

            var love = await GiveAsync(player, ServerFixture.EndlessLove.Id);
            await player.Game.UseAsync(love.Id);
            Assert.NotNull(await player.Game.WaitForNameAsync(n =>
                n.Action == NameMessage.Fireworks && n.Data == player.User.Id && n.Strings.Contains("Endless Love")));
            Assert.NotNull(await viewer.Game.WaitForNameAsync(n =>
                n.Action == NameMessage.Fireworks && n.Data == player.User.Id && n.Strings.Contains("Endless Love")));
        }

        [Fact]
        public async Task MoneyBagCpBagAndDrugBundleWork()
        {
            await using var player = (await this.PlayingAtAsync(1002, 750, 940, GameMaster)).Player;

            var bag = await GiveAsync(player, ServerFixture.Class1MoneyBag.Id);
            await player.Game.UseAsync(bag.Id);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.Silver, out var s) && s == player.User.Silver + 300_000));

            var cpBag = await GiveAsync(player, ServerFixture.CpBag.Id);
            await player.Game.UseAsync(cpBag.Id);
            Assert.NotNull(await player.Game.WaitForAttribAsync(a =>
                a.Values.TryGetValue(UserAttribMessage.Jewels, out var j) && j == 3));

            var bundle = await GiveAsync(player, ServerFixture.AmritaBundle.Id);
            await player.Game.UseAsync(bundle.Id);
            Assert.NotNull(await player.Game.WaitForItemActionAsync(ItemMessage.Remove, bundle.Id));
            await this.Servers.WaitForEventAsync("game", "item.used", e =>
                e.GetProperty("name").GetString() == player.User.Name && e.GetProperty("itemId").GetUInt32() == bundle.Id);
            var amritas = await this.Servers.QueryGameScalarAsync(
                "SELECT COUNT(*) FROM item WHERE CharacterID = @id AND TypeID = @type",
                ("@id", player.User.Id), ("@type", ServerFixture.Amrita.Id));
            Assert.Equal(3L, (long)amritas);
        }
    }
}
