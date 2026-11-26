namespace Fetih.Game.Npcs
{
    using System.Linq;
    using System.Threading.Tasks;
    using Fetih.Core.Logging;
    using Fetih.Game.Chat;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using Fetih.Game.World;
    using static Fetih.Game.Packets.MsgUserAttrib;

    // The client opens its own avatar window on clicking the Beautician, and sends the face
    // chosen there as MsgAction 151 with the face in Command and no NPC. Each face is 500
    // silver. The faces a player may pick are those the client's Common.ini offers: the
    // gender's own, and monks and pirates five more each; its VIP faces need a VIP level,
    // which no one has.
    public static class Beautician
    {
        public const uint Cost = 500;

        private static readonly (int First, int Last)[] MaleFaces = { (1, 107) };
        private static readonly (int First, int Last)[] FemaleFaces = { (201, 295) };
        private static readonly (int First, int Last) MaleMonkFaces = (108, 112), FemaleMonkFaces = (299, 303);
        private static readonly (int First, int Last) MalePirateFaces = (153, 157), FemalePirateFaces = (344, 348);

        public static bool Offers(Character character, uint face)
        {
            bool male = character.Mesh % 10000 < 2000;
            var faces = (male ? MaleFaces : FemaleFaces).AsEnumerable();
            if (character.CurrentClass / 10 == 6)
                faces = faces.Append(male ? MaleMonkFaces : FemaleMonkFaces);
            else if (character.CurrentClass / 10 == 7)
                faces = faces.Append(male ? MalePirateFaces : FemalePirateFaces);
            return faces.Any(f => face >= f.First && face <= f.Last);
        }

        public static async Task ChangeAsync(Client client, uint face)
        {
            var character = client.Character;
            if (character == null)
                return;
            string reason = null;
            if (!Kernel.Npcs.InView(character.MapID, character.X, character.Y).Any(n => n.Role == Npc.BeauticianRole))
                reason = "no_beautician";
            else if (client.Life.Dead)
                reason = "dead";
            else if (!Offers(character, face))
                reason = "face_not_offered";
            else if (face == character.Avatar)
                reason = "same_face";
            else if (!await Progress.ChangeSilverAsync(client, -Cost, "avatar"))
            {
                reason = "no_silver";
                await Talk.NoticeAsync(client, $"It costs {Cost} silver to change your avatar.");
            }

            if (reason != null)
            {
                LogEvents.For(typeof(Beautician), LogEvents.CharacterAvatarRejected).Warning(
                    "{Name} can't change avatar {Avatar} to {Face}: {Reason}",
                    character.Name, character.Avatar, face, reason);
                return;
            }

            ushort before = character.Avatar;
            character.Avatar = (ushort)face;
            var look = new MsgUserAttrib(client.ID, AttributeType.Mesh, client.Life.Mesh(character));
            await client.SendAsync(look);
            await Movement.SendToViewAsync(client, look);
            LogEvents.For(typeof(Beautician), LogEvents.CharacterAvatar).Information(
                "{Name} changed avatar {Before} to {Avatar}", character.Name, before, face);
        }
    }
}
