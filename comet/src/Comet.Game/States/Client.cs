namespace Comet.Game.States
{
    using System;
    using System.Collections.Concurrent;
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

        // Trades, teams and friends are changed by other players' packets too
        public Social.Trade Trade { get; set; }
        public Social.Team Team { get; set; }

        // The player's open booth in the market; buyers' packets read it too
        public Booth Booth { get; set; }

        // The player this one asked to trade with, or to be friends with (0 if none)
        public uint TradeRequest { get; set; }
        public uint FriendRequest { get; set; }

        // The guild recruiter this player asked to join, or the player this recruiter invited
        public uint GuildApply { get; set; }
        public uint GuildInvite { get; set; }

        // The clan leader this player asked to join, or the player this leader invited
        public uint ClanApply { get; set; }
        public uint ClanInvite { get; set; }

        // Names by character ID, loaded at login
        public ConcurrentDictionary<uint, string> Friends { get; } = new ConcurrentDictionary<uint, string>();
        public ConcurrentDictionary<uint, string> Enemies { get; } = new ConcurrentDictionary<uint, string>();

        public Life Life { get; } = new Life();

        // The monster the player keeps attacking until it dies, they move or pick another;
        // each new attack or stop changes AttackRun, so a pending swing of an old one is dropped
        public uint AttackTarget { get; set; }
        public bool AttackShooting { get; set; }
        public int AttackRun { get; set; }
        public DateTime NextAttackAt { get; set; }

        public PkState Pk { get; } = new PkState();

        // Statuses that run out: XP full and what spells give
        public TimedStatus Effects { get; } = new TimedStatus();

        // Stamina, the XP bar and learned skills, changed on the player's own queue
        public Magic.Energy Energy { get; } = new Magic.Energy();
        public Magic.Skills Skills { get; set; } = new Magic.Skills();
        public DateTime NextCastAt { get; set; }

        // Status flags for MsgUserAttrib 25 and MsgPlayer 22: dead, name color, flashing, spells
        public ulong Status => this.Life.Status | Pking.Status(this) | this.Effects.Status(DateTime.UtcNow);

        // The NPC the player is talking to, so dialog answers reach it
        public uint ActiveNpc { get; set; }
        public bool DialogAnswered { get; set; }
        public DiffieHellman DiffieHellman = null;

        // Login token this connection authenticated with, and what it was issued for.
        // Kept so the client can repeat MsgConnect on the same connection.
        public ulong AuthenticationToken { get; set; }
        public TransferAuthArgs Authentication { get; set; }

        // When the character entered the game (set on session.login)
        public DateTime? LoggedInAt { get; set; }

        public Authority Authority => (Authority)(this.Authentication?.AuthorityID ?? 1);

        public DateTime LastWorldTalk { get; set; }

        public uint ID => Character?.CharacterID ?? 0;
        public uint Identity => this.ID;

        // The map the character stands on (set when entering the game)
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

        private async Task ChainSave(Task previous, string reason)
        {
            try { await previous; }
            catch (Exception) { }

            var character = this.Character;
            if (character == null)
                return;

            try
            {
                await CharactersRepository.SaveAsync(character.Snapshot());
                await SkillsRepository.SaveAsync(character.CharacterID, this.Skills.Snapshot());
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
