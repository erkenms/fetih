namespace Comet.Game.Tests.States
{
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Game.States;
    using Xunit;

    public class SessionRegistryTests
    {
        private sealed class Session { }

        [Fact]
        public void ReplaceReturnsNullForNewAccount()
        {
            var registry = new SessionRegistry<Session>();
            Assert.Null(registry.Replace(1, new Session()));
            Assert.Equal(1, registry.Count);
        }

        [Fact]
        public void ReplaceReturnsPreviousSession()
        {
            var registry = new SessionRegistry<Session>();
            var first = new Session();
            var second = new Session();
            registry.Replace(1, first);

            Assert.Same(first, registry.Replace(1, second));
            Assert.True(registry.TryGet(1, out var current));
            Assert.Same(second, current);
        }

        [Fact]
        public void RemoveDeletesOwnSession()
        {
            var registry = new SessionRegistry<Session>();
            var session = new Session();
            registry.Replace(1, session);

            Assert.True(registry.Remove(1, session));
            Assert.Equal(0, registry.Count);
        }

        [Fact]
        public void StaleSessionCannotEvictReplacement()
        {
            // Duplicate login: the kicked client's disconnect must not remove the new one.
            var registry = new SessionRegistry<Session>();
            var stale = new Session();
            var replacement = new Session();
            registry.Replace(1, stale);
            registry.Replace(1, replacement);

            Assert.False(registry.Remove(1, stale));
            Assert.True(registry.TryGet(1, out var current));
            Assert.Same(replacement, current);
        }

        [Fact]
        public void ConcurrentReplaceAndRemoveLeavesNoLeaks()
        {
            var registry = new SessionRegistry<Session>();
            Parallel.For(0, 10_000, i =>
            {
                var accountID = (uint)(i % 50);
                var session = new Session();
                registry.Replace(accountID, session);
                registry.Remove(accountID, session);
            });

            // Every session removed itself or was replaced by a session that removed
            // itself; at most one leftover per account is impossible to keep.
            Assert.Equal(0, registry.Count);
            Assert.Empty(registry.Values.ToList());
        }
    }
}
