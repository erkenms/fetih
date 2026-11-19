namespace Comet.Game.States
{
    // The account_authority table
    public enum Authority : ushort
    {
        Player = 1,
        Moderator,
        GameMaster,
        ProjectManager,
        Administrator
    }
}
