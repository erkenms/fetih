namespace Fetih.Game.Tests.Npcs
{
    using Fetih.Game.Database.Models;
    using Fetih.Game.Npcs;
    using Fetih.Game.States;
    using Xunit;

    public class BeauticianTests
    {
        private static Character Of(ushort mesh, byte currentClass) =>
            new Character(new DbCharacter(1, 1000001, "Hero") { Mesh = mesh, CurrentClass = currentClass });

        [Theory]
        [InlineData(1004, 15, 1u, true)]
        [InlineData(1004, 15, 107u, true)]
        [InlineData(1004, 15, 0u, false)]
        [InlineData(1004, 15, 201u, false)]
        [InlineData(1004, 15, 108u, false)]
        [InlineData(1004, 65, 108u, true)]
        [InlineData(1004, 75, 157u, true)]
        [InlineData(1004, 15, 134u, false)]
        [InlineData(2001, 15, 201u, true)]
        [InlineData(2001, 15, 295u, true)]
        [InlineData(2001, 15, 5u, false)]
        [InlineData(2001, 65, 303u, true)]
        [InlineData(2001, 75, 344u, true)]
        [InlineData(2001, 75, 303u, false)]
        public void FacesFollowGenderAndClass(ushort mesh, byte currentClass, uint face, bool offered) =>
            Assert.Equal(offered, Beautician.Offers(Of(mesh, currentClass), face));
    }
}
