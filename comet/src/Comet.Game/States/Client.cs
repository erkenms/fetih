namespace Comet.Game.States
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Sockets;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.Pk;
    using Comet.Game.World;
    using Comet.Network.Security;
    using Comet.Network.Sockets;
    using Comet.Shared.Models;

    public sealed class Client : TcpServerActor, IMapObject
    {
        public Character Character = null;
        public Creation Creation = null;
        public Inventory Inventory = null;
        public Equipment Equipment = null;
        public Warehouses Warehouses = null;

        // Other players' packets change these too
        public Social.Trade Trade { get; set; }
        public Social.Team Team { get; set; }

        // Buyers' packets read it too
        public Booth Booth { get; set; }

        public bool Away { get; set; }

        public uint TradeRequest { get; set; }
        public uint FriendRequest { get; set; }
        public uint PartnerRequest { get; set; }
        public uint MarriageProposal { get; set; }

        // The guild recruiter this player asked to join, or the player this recruiter invited
        public uint GuildApply { get; set; }
        public uint GuildInvite { get; set; }

        // The clan leader this player asked to join, or the player this leader invited
        public uint ClanApply { get; set; }
        public uint ClanInvite { get; set; }

        public ConcurrentDictionary<uint, string> Friends { get; } = new ConcurrentDictionary<uint, string>();
        public ConcurrentDictionary<uint, (string Name, uint Mesh)> Enemies { get; } =
            new ConcurrentDictionary<uint, (string Name, uint Mesh)>();
        public ConcurrentDictionary<uint, Social.TradePartner> TradePartners { get; } =
            new ConcurrentDictionary<uint, Social.TradePartner>();

        public bool SecondPasswordVerified { get; set; }
        public int SecondPasswordFailures { get; set; }

        public Life Life { get; } = new Life();

        // The monster the player keeps attacking until it dies, they move or pick another;
        // each new attack or stop changes AttackRun, so a pending swing of an old one is dropped
        public uint AttackTarget { get; set; }
        public bool AttackShooting { get; set; }
        public int AttackRun { get; set; }
        public DateTime NextAttackAt { get; set; }

        // The dig going on while mining; on the player's own queue
        public Items.Mining.Dig Mining { get; set; }

        // Where Boxer Huang sends the player back to from the training ground; on the player's own queue
        public World.MapPoint TrainingReturn { get; set; }

        // When the next minute of online training counts; MinValue while it doesn't run
        public DateTime NextOnlineTrainingAt { get; set; }

        public PkState Pk { get; } = new PkState();

        // Statuses that run out: XP full and what spells give
        public TimedStatus Effects { get; } = new TimedStatus();

        // Changed on the player's own queue
        public Magic.Poisoning Poison { get; set; }

        // Energy and Skills change only on the player's own queue
        public Magic.Energy Energy { get; } = new Magic.Energy();
        public Magic.Skills Skills { get; set; } = new Magic.Skills();
        public Mount Mount { get; } = new Mount();
        public DateTime NextCastAt { get; set; }

        // The aura a monk keeps up, by magic type, or 0; on the monk's own queue
        public ushort Aura { get; set; }
        public ushort AuraLevel { get; set; }

        // A transformation spell's monster, until when; on the player's own queue
        public Magic.Transformation Transformation { get; set; }

        // Counter-Kill switched on; on the player's own queue
        public bool CounterKill { get; set; }

        // Shuriken Vortex's next spin; on the player's own queue
        public DateTime NextVortexAt { get; set; }
        // When the client shows the character landing from their last jump
        public DateTime LandsAt { get; set; }

        // When the last Fatal Strikes went off, oldest first; on the player's own queue
        public System.Collections.Generic.Queue<DateTime> FatalStrikes { get; } = new System.Collections.Generic.Queue<DateTime>();

        // Status flags for MsgUserAttrib 25 and MsgPlayer 22: dead, name color, flashing, spells
        public UInt128 Status => this.Life.Status | Pking.Status(this) | this.Effects.Status(DateTime.UtcNow) |
            (this.Mount.Riding ? StatusFlags.Riding : 0) |
            (HeavenBlessing.IsBlessed(this.Character, DateTime.UtcNow) ? StatusFlags.HeavenBlessing : 0);

        public uint ActiveNpc { get; set; }
        public bool DialogAnswered { get; set; }

        // Where the player came into the market from, for its Mark.Controller to send them back
        public (uint MapID, ushort X, ushort Y)? MarketReturn { get; set; }

        public DiffieHellman DiffieHellman = null;

        // Kept so the client can repeat MsgConnect on the same connection.
        public ulong AuthenticationToken { get; set; }
        public TransferAuthArgs Authentication { get; set; }

        public DateTime? LoggedInAt { get; set; }
        public bool Deleted { get; set; }

        public Authority Authority => (Authority)(this.Authentication?.AuthorityID ?? 1);

        public DateTime LastWorldTalk { get; set; }

        public uint ID => Character?.CharacterID ?? 0;
        public uint Identity => this.ID;

        public GameMap<Client> Map { get; set; }

        // Saves run one after another in the order asked for; none starts after the logout save
        private readonly object SaveLock = new object();
        private Task SaveChain = Task.CompletedTask;
        private Task LogoutTask;

        public Client(Socket socket, Memory<byte> buffer, uint partition) 
            : base(socket, buffer, new Cast5Cipher(), partition, "TQServer")
        {
            this.DiffieHellman = new DiffieHellman();
        }

        // Every caller awaits the same save, so a replacing login can wait for it to finish and
        // the disconnect handler can't start a second one.
        public Task LogoutAsync()
        {
            lock (this.SaveLock)
                return this.LogoutTask ??= this.SaveChain = this.ChainSave(this.SaveChain, "logout");
        }

        // False if the save failed (it's logged) or was skipped. Skipped once the player has
        // logged out: the logout save already wrote the character, and a later login may have
        // changed it since.
        public Task<bool> SaveAsync(string reason)
        {
            lock (this.SaveLock)
            {
                if (this.LogoutTask != null)
                    return Task.FromResult(false);
                var save = this.SaveChain = this.ChainSave(this.SaveChain, reason);
                return save.ContinueWith(t => t.IsCompletedSuccessfully, TaskScheduler.Default);
            }
        }

        // Wear is only saved as it crosses a hundred, so the rest is saved with the character.
        // The packet queue may change the equipment meanwhile; that save then skips it.
        private List<(uint ItemID, ushort Amount)> WornDurability()
        {
            try
            {
                return this.Equipment?.Items.Where(i => Durability.Wears(i.TypeID))
                    .Select(i => (i.ItemID, i.Amount)).ToList() ?? new List<(uint, ushort)>();
            }
            catch (InvalidOperationException)
            {
                return new List<(uint, ushort)>();
            }
        }

        private async Task ChainSave(Task previous, string reason)
        {
            try { await previous; }
            catch (Exception) { }

            var character = this.Character;
            if (character == null || this.Deleted)
                return;

            try
            {
                await CharactersRepository.SaveAsync(character.Snapshot());
                await SkillsRepository.SaveAsync(character.CharacterID, this.Skills.Snapshot());
                await ItemsRepository.SetAmountsAsync(this.WornDurability());
            }
            catch (Exception e)
            {
                LogEvents.For<Client>(LogEvents.CharacterSaveFailed).Error(e,
                    "Couldn't save {Name} ({Reason}; character {CharacterId})",
                    character.Name, reason, character.CharacterID);
                throw;
            }

            LogEvents.For<Client>(LogEvents.CharacterSaved).Debug(
                "Saved {Name} ({Reason}; character {CharacterId}, level {Level}, map {MapId} at {X},{Y})",
                character.Name, reason, character.CharacterID, character.Level,
                character.MapID, character.X, character.Y);
        }
    }
}
