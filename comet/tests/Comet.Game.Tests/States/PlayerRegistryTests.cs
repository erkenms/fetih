namespace Comet.Game.Tests.States
{
    using Comet.Game.States;
    using Xunit;

    public class PlayerRegistryTests
    {
        private sealed class Player { }

        [Fact]
        public void FindsPlayersByIdAndNameIgnoringCase()
        {
            var registry = new PlayerRegistry<Player>();
            var hero = new Player();
            Assert.Null(registry.Add(1000001, "Hero", hero));

            Assert.True(registry.TryGet(1000001, out var byId));
            Assert.Same(hero, byId);
            Assert.True(registry.TryGet("hERO", out var byName));
            Assert.Same(hero, byName);
            Assert.Equal(1, registry.Count);
        }

        [Fact]
        public void RemovesPlayers()
        {
            var registry = new PlayerRegistry<Player>();
            var hero = new Player();
            registry.Add(1000001, "Hero", hero);

            Assert.True(registry.Remove(1000001, "Hero", hero));
            Assert.False(registry.TryGet(1000001, out _));
            Assert.False(registry.TryGet("Hero", out _));
            Assert.Equal(0, registry.Count);
        }

        [Fact]
        public void ReplacedPlayerCantRemoveTheOneWhoReplacedThem()
        {
            var registry = new PlayerRegistry<Player>();
            var first = new Player();
            var second = new Player();
            registry.Add(1000001, "Hero", first);
            Assert.Same(first, registry.Add(1000001, "Hero", second));

            // The first connection is torn down after the second logged in
            Assert.False(registry.Remove(1000001, "Hero", first));
            Assert.True(registry.TryGet(1000001, out var byId));
            Assert.Same(second, byId);
            Assert.True(registry.TryGet("Hero", out var byName));
            Assert.Same(second, byName);
        }
    }
}
