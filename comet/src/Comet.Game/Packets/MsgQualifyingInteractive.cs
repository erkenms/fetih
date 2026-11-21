namespace Comet.Game.Packets
{
    using System.Threading.Tasks;
    using Comet.Game.Arena;
    using Comet.Game.States;
    using Comet.Network.Packets;

    // The arena window's buttons and the server's arena dialogs. 52 bytes, as the 5619 client
    // sends it: action at 4, option at 8, then a player: ID at 12, name (16 bytes) at 16,
    // rank at 32, class at 36, 1 at 40, arena points at 44, level at 48.
    public sealed class MsgQualifyingInteractive : MsgBase<Client>
    {
        public const int Size = 52;

        // Dialog options: Answer 1 accepts, 2 gives up; Dialog 1 won, 3 lost;
        // EndDialog 1 won, 0 lost; Match 5 shows the match window
        public const uint Accept = 1, GiveUp = 2;
        public const uint Won = 1, Lost = 3;
        public const uint MatchOn = 5;

        public QualifierAction Action { get; set; }
        public uint Option { get; set; }
        public Fighter Fighter { get; set; }

        public MsgQualifyingInteractive() => base.Type = PacketType.MsgQualifyingInteractive;

        public MsgQualifyingInteractive(QualifierAction action, uint option = 0, Fighter fighter = null) : this()
        {
            this.Action = action;
            this.Option = option;
            this.Fighter = fighter;
        }

        public override void Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            this.Length = reader.ReadUInt16();
            this.Type = (PacketType)reader.ReadUInt16();
            this.Action = (QualifierAction)reader.ReadUInt32();
            this.Option = reader.ReadUInt32();
        }

        public override byte[] Encode()
        {
            var fighter = this.Fighter;
            var writer = new PacketWriter();
            writer.Write((ushort)base.Type);
            writer.Write((uint)this.Action);                // 4
            writer.Write(this.Option);                      // 8
            writer.Write(fighter?.ID ?? 0);                 // 12
            writer.Write(fighter?.Name ?? string.Empty, 16);// 16
            writer.Write(fighter?.Rank ?? 0);               // 32
            writer.Write(fighter?.Class ?? 0);              // 36
            writer.Write(fighter == null ? 0u : 1u);        // 40
            writer.Write(fighter?.Points ?? 0);             // 44
            writer.Write(fighter?.Level ?? 0);              // 48
            return writer.ToArray();
        }

        public override Task ProcessAsync(Client client) => Qualifier.HandleAsync(client, this);

        public enum QualifierAction : uint
        {
            // From the client: sign up / from the server: the arena icon on
            SignUp = 0,
            // From the client: quit waiting / from the server: the arena icon off
            Quit = 1,
            // An opponent was found; the client shows the accept dialog
            Countdown = 2,
            // The client's answer to the accept dialog (Accept or GiveUp in Option)
            Answer = 3,
            // To the server: give up the match; to the client: the opponent gave up
            OpponentGaveUp = 4,
            BuyPoints = 5,
            Match = 6,
            Kicked = 7,
            // The match begins, with the opponent's details
            StartFight = 8,
            Dialog = 9,
            EndDialog = 10,
            // The end dialog's button to join again
            Rejoin = 11,
        }
    }
}
