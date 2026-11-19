namespace Comet.Game.Npcs
{
    using System.Threading.Tasks;
    using Comet.Game.Packets;

    public static class TwinCity
    {
        public const uint GuardID = 30104;

        public static Task GuardAsync(NpcTalk talk) => talk
            .Text("I am in charge of the security of the city. No bad guys can escape here.")
            .Choice("Wow, it is wonderful.", MsgTaskDialog.NoOption)
            .Face(30)
            .ShowAsync();
    }
}
