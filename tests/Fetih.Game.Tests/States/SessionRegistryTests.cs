namespace Fetih.Game.Tests.States
{
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Game.States;
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
        public void RemoveDeletesOwnSession()
        {
            var registry = new SessionRegistry<Session>();
            var session = new Session();
            registry.Replace(1, session);

            Assert.True(registry.Remove(1, session));
            Assert.Equal(0, registry.Count);
        }

        [Fact]
        public void ReplaceReturnsTheStaleSessionWhichCannotEvictItsReplacement()
        {
            // Duplicate login: the kicked client's disconnect must not remove the new one.
            var registry = new SessionRegistry<Session>();
            var stale = new Session();
            var replacement = new Session();
            registry.Replace(1, stale);

            Assert.Same(stale, registry.Replace(1, replacement));
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

            // Each account's last Replace is followed by that session's own Remove, so
            // nothing may be left over.
            Assert.Equal(0, registry.Count);
            Assert.Empty(registry.Values.ToList());
        }
    }
}
