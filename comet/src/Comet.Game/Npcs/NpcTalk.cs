namespace Comet.Game.Npcs
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Comet.Game.World;
    using Comet.Game.Packets;
    using Comet.Game.States;
    using static Comet.Game.Packets.MsgTaskDialog;

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

        public NpcTalk(Client client, Npc npc, byte option, string input = null)
        {
            this.Client = client;
            this.Npc = npc;
            this.Option = option;
            this.Input = input;
        }

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

        public NpcTalk Face(byte face)
        {
            this.FaceID = face;
            return this;
        }

        public async Task ShowAsync()
        {
            foreach (var part in this.Page)
                await this.Client.SendAsync(part);
            await this.Client.SendAsync(new MsgTaskDialog(DialogAction.Face, this.FaceID));
            await this.Client.SendAsync(new MsgTaskDialog(DialogAction.Show, NoOption));
            this.Page.Clear();
        }
    }
}
