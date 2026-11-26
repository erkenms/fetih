namespace Fetih.Game.Npcs
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Fetih.Game.World;
    using Fetih.Game.Packets;
    using Fetih.Game.States;
    using static Fetih.Game.Packets.MsgTaskDialog;

    // One step of a conversation: the option the player chose (0 when they clicked the
    // NPC), and a page to fill and show
    public sealed class NpcTalk
    {
        public Client Client { get; }
        public Npc Npc { get; }
        public byte Option { get; }
        public string Input { get; }

        private readonly List<MsgTaskDialog> Page = new List<MsgTaskDialog>();
        private byte FaceID;
        private readonly List<NpcPage> Recorded;

        public NpcTalk(Client client, Npc npc, byte option, string input = null)
        {
            this.Client = client;
            this.Npc = npc;
            this.Option = option;
            this.Input = input;
        }

        // A conversation with no player: its pages are kept instead of sent, for the web's dialog
        // viewer. Only for dialogs that build their first page from data alone (teleporters,
        // talkers, the default answer).
        public static NpcTalk Preview() => new NpcTalk(null, null, 0, recorded: new List<NpcPage>());

        private NpcTalk(Client client, Npc npc, byte option, List<NpcPage> recorded) : this(client, npc, option)
        {
            this.Recorded = recorded;
        }

        public IReadOnlyList<NpcPage> Shown => this.Recorded;

        public NpcTalk Text(string text)
        {
            this.Page.Add(new MsgTaskDialog(DialogAction.Text, NoOption, text));
            return this;
        }

        public NpcTalk Choice(string text, byte option)
        {
            this.Page.Add(new MsgTaskDialog(DialogAction.Option, option, text));
            return this;
        }

        // A text box; the client answers with the option and the text typed
        public NpcTalk TextBox(string text, byte option, ushort maxLength = 16)
        {
            this.Page.Add(new MsgTaskDialog(DialogAction.Input, option, text) { Data = maxLength });
            return this;
        }

        public NpcTalk Face(byte face)
        {
            this.FaceID = face;
            return this;
        }

        public async Task ShowAsync()
        {
            if (this.Recorded != null)
            {
                this.Recorded.Add(new NpcPage(new List<MsgTaskDialog>(this.Page), this.FaceID));
                this.Page.Clear();
                return;
            }
            foreach (var part in this.Page)
                await this.Client.SendAsync(part);
            await this.Client.SendAsync(new MsgTaskDialog(DialogAction.Face, this.FaceID));
            await this.Client.SendAsync(new MsgTaskDialog(DialogAction.Show, NoOption));
            this.Page.Clear();
        }

        // The client draws the shop window from its own Shop.dat
        public Task OpenShopAsync()
        {
            this.Client.ActiveNpc = 0;
            var character = this.Client.Character;
            return this.Client.SendAsync(MsgAction.OpenShop(this.Client.ID, this.Npc.NpcID, character.X, character.Y));
        }

        public Task OpenWindowAsync(MsgAction.Window window)
        {
            this.Client.ActiveNpc = 0;
            var character = this.Client.Character;
            return this.Client.SendAsync(MsgAction.OpenWindow(this.Client.ID, window, character.X, character.Y));
        }
    }

    // One page the server showed: its lines, options and text boxes in order, and the face
    public sealed record NpcPage(IReadOnlyList<MsgTaskDialog> Parts, byte Face);
}
