namespace Comet.Game.States
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Comet.Core.Logging;
    using Comet.Game.Database.Models;
    using Comet.Game.Database.Repositories;
    using Comet.Game.Items;
    using Comet.Game.Packets;
    using Comet.Game.World;

    public sealed class SubClassEntry
    {
        public SubClassId Id { get; init; }
        public byte Level { get; set; } = 1;
        public byte Phase { get; set; } = 1;
    }

    // A character's learned sub-classes and the server operations for learning, upgrading with
    // study points, promoting phases, and activating one. Only the active sub-class gives its
    // bonus: the 5619 client's attribute window reads the current sub-class's phase alone.
    public sealed class SubClasses
    {
        private readonly Dictionary<SubClassId, SubClassEntry> Entries = new Dictionary<SubClassId, SubClassEntry>();

        public SubClasses() { }

        public SubClasses(IEnumerable<DbSubClass> rows)
        {
            foreach (var row in rows)
            {
                if (!Enum.IsDefined(typeof(SubClassId), row.SubClassID) || row.SubClassID == 0)
                    continue;
                var id = (SubClassId)row.SubClassID;
                this.Entries[id] = new SubClassEntry
                {
                    Id = id,
                    Level = Math.Max((byte)1, row.Level),
                    Phase = Math.Max((byte)1, row.Phase),
                };
            }
        }

        public IReadOnlyCollection<SubClassEntry> All => this.Entries.Values;

        public SubClassId Active { get; set; }

        public bool TryGet(SubClassId id, out SubClassEntry entry) => this.Entries.TryGetValue(id, out entry);

        public bool Has(SubClassId id) => this.Entries.ContainsKey(id);

        // Base-10 packed phases for MsgUserInfo (offset 98) and MsgPlayer (offset 207):
        // each sub-class ID 1..9 occupies digit 10^(id - 1) with its phase (0..9).
        public ulong AllSubProfMask()
        {
            ulong mask = 0;
            foreach (var entry in this.Entries.Values)
            {
                int id = (int)entry.Id;
                if (id is < 1 or > 9)
                    continue;
                ulong place = 1;
                for (int i = 1; i < id; i++)
                    place *= 10;
                mask += (ulong)Math.Min((byte)9, entry.Phase) * place;
            }
            return mask;
        }

        public int EffectOf(SubClassId id, SubClassTable table = null)
        {
            table ??= Kernel.SubClasses;
            if (id != this.Active || !this.Entries.TryGetValue(id, out var entry) || !table.TryGet(id, out var def))
                return 0;
            return def.Phase(entry.Phase)?.Effect ?? 0;
        }

        public int AttackBonus(SubClassTable table = null) => this.EffectOf(SubClassId.Performer, table);
        public int MagicAttackBonus(SubClassTable table = null) => this.EffectOf(SubClassId.Performer, table);
        public int MaxHealthBonus(SubClassTable table = null) => this.EffectOf(SubClassId.Wrangler, table);
        public int CriticalStrikePercent(SubClassTable table = null) => this.EffectOf(SubClassId.MartialArtist, table);
        public int SkillCriticalStrikePercent(SubClassTable table = null) => this.EffectOf(SubClassId.Warlock, table);
        public int ImmunityPercent(SubClassTable table = null) => this.EffectOf(SubClassId.ChiMaster, table);
        public int PenetrationPercent(SubClassTable table = null) => this.EffectOf(SubClassId.Sage, table);
        public int DetoxicationPercent(SubClassTable table = null) => this.EffectOf(SubClassId.Apothecary, table);

        public void Set(SubClassId id, byte level, byte phase) =>
            this.Entries[id] = new SubClassEntry { Id = id, Level = level, Phase = phase };

        public static async Task LoadAsync(Client client)
        {
            var rows = await SubClassesRepository.LoadAsync(client.ID);
            client.SubClasses = new SubClasses(rows);
            if (client.Character.ActiveSubClass != 0 &&
                !client.SubClasses.Has((SubClassId)client.Character.ActiveSubClass))
                client.Character.ActiveSubClass = 0;
            client.SubClasses.Active = (SubClassId)client.Character.ActiveSubClass;
        }

        public static Task SendAllAsync(Client client)
        {
            var entries = client.SubClasses.All
                .Select(e => (e.Id, e.Phase, e.Level))
                .ToList();
            return client.SendAsync(MsgSubPro.Show(client.Character.StudyPoints, entries));
        }

        public static string CanLearn(Client client, SubClassId id)
        {
            if (client.Character == null)
                return "no_character";
            if (!Kernel.SubClasses.TryGet(id, out var def))
                return "unknown";
            if (client.SubClasses.Has(id))
                return "already_learned";
            var p1 = def.Phase(1);
            if (p1 == null)
                return "unknown";
            if (client.Character.Rebirths < p1.RequiredRebirths)
                return "rebirths";
            if (client.Character.Level < p1.RequiredLevel)
                return "level";
            var itemReq = SubClassTable.LearnItemOf(id);
            if (itemReq != null)
            {
                if (client.Inventory.CountOf(itemReq.ItemTypeId) < itemReq.Count)
                    return "no_item";
            }
            return null;
        }

        public static async Task<string> LearnAsync(Client client, SubClassId id)
        {
            string reason = CanLearn(client, id);
            if (reason != null)
            {
                LogEvents.For(typeof(SubClasses), LogEvents.SubClassRejected).Information(
                    "{Name} could not learn sub-class {SubClass}: {Reason}",
                    client.Character?.Name, id, reason);
                return reason;
            }

            var itemReq = SubClassTable.LearnItemOf(id);
            if (itemReq != null && !await ItemActions.TakeAsync(client, itemReq.ItemTypeId, itemReq.Count, $"sub-class {id}"))
                return "no_item";

            client.SubClasses.Set(id, level: 1, phase: 1);
            await SubClassesRepository.SaveAsync(new DbSubClass
            {
                CharacterID = client.ID,
                SubClassID = (byte)id,
                Level = 1,
                Phase = 1,
            });
            await client.SendAsync(MsgSubPro.Learn(id, phase: 1));
            await SyncBonusStatsAsync(client, id);

            LogEvents.For(typeof(SubClasses), LogEvents.SubClassLearned).Information(
                "{Name} learned sub-class {SubClass}", client.Character.Name, id);
            return null;
        }

        public static async Task<string> UpgradeAsync(Client client, SubClassId id)
        {
            var character = client.Character;
            if (character == null)
                return "no_character";
            if (!Kernel.SubClasses.TryGet(id, out var def) || !client.SubClasses.TryGet(id, out var entry))
                return Reject(client, "upgrade", id, "not_learned");
            if (entry.Level >= def.MaxLevel)
                return Reject(client, "upgrade", id, "max_level");
            var cost = def.StudyCostForNextLevel(entry.Level);
            if (cost == null || cost.Value == 0)
                return Reject(client, "upgrade", id, "max_level");
            if (character.StudyPoints < cost.Value)
                return Reject(client, "upgrade", id, "study_points");

            character.StudyPoints -= cost.Value;
            entry.Level++;
            await SubClassesRepository.SaveAsync(new DbSubClass
            {
                CharacterID = client.ID,
                SubClassID = (byte)id,
                Level = entry.Level,
                Phase = entry.Phase,
            });
            await client.SaveAsync("subclass_upgrade");
            await client.SendAsync(MsgSubPro.StudyPointsPacket(character.StudyPoints, added: 0));
            await client.SendAsync(MsgSubPro.Upgrade(id));

            LogEvents.For(typeof(SubClasses), LogEvents.SubClassUpgraded).Information(
                "{Name} upgraded sub-class {SubClass} to level {Level} (spent {Cost} study points)",
                character.Name, id, entry.Level, cost.Value);
            return null;
        }

        public static string CanPromote(Client client, SubClassId id)
        {
            var character = client.Character;
            if (character == null)
                return "no_character";
            if (!Kernel.SubClasses.TryGet(id, out var def) || !client.SubClasses.TryGet(id, out var entry))
                return "not_learned";
            if (entry.Phase >= SubClassTable.MaxPhase)
                return "max_phase";
            var next = def.Phase((byte)(entry.Phase + 1));
            if (next == null)
                return "max_phase";
            if (character.Rebirths < next.RequiredRebirths)
                return "rebirths";
            if (character.Level < next.RequiredLevel)
                return "level";
            if (entry.Level < next.RequiredSubLevel)
                return "sub_level";
            return null;
        }

        public static async Task<string> PromoteAsync(Client client, SubClassId id)
        {
            string reason = CanPromote(client, id);
            if (reason != null)
                return Reject(client, "promote", id, reason);

            var entry = client.SubClasses.All.First(e => e.Id == id);
            entry.Phase++;
            await SubClassesRepository.SaveAsync(new DbSubClass
            {
                CharacterID = client.ID,
                SubClassID = (byte)id,
                Level = entry.Level,
                Phase = entry.Phase,
            });
            await client.SendAsync(MsgSubPro.Promote(id, entry.Phase));
            await SyncBonusStatsAsync(client, id);
            if (client.Map != null)
                await Movement.SendToViewAsync(client, new MsgPlayer(client));

            LogEvents.For(typeof(SubClasses), LogEvents.SubClassPromoted).Information(
                "{Name} promoted sub-class {SubClass} to phase {Phase}",
                client.Character.Name, id, entry.Phase);
            return null;
        }

        public static async Task<string> ActivateAsync(Client client, SubClassId id)
        {
            var character = client.Character;
            if (character == null)
                return "no_character";
            if (id != SubClassId.None && !client.SubClasses.Has(id))
                return Reject(client, "activate", id, "not_learned");

            character.ActiveSubClass = (byte)id;
            client.SubClasses.Active = id;
            await client.SaveAsync("subclass_activate");
            await client.SendAsync(MsgSubPro.Switch(id));
            await Items.Equipping.FollowMaximumsAsync(client);
            await client.SendAsync(MsgPlayerAttribInfo.Of(client));
            if (client.Map != null)
                await Movement.SendToViewAsync(client, new MsgPlayer(client));

            LogEvents.For(typeof(SubClasses), LogEvents.SubClassActivated).Information(
                "{Name} activated sub-class {SubClass}", character.Name, id);
            return null;
        }

        public static async Task AwardStudyPointsAsync(Client client, ulong amount)
        {
            var character = client.Character;
            if (character == null || amount == 0)
                return;
            ulong before = character.StudyPoints;
            ulong added = amount > ulong.MaxValue - before ? ulong.MaxValue - before : amount;
            if (added == 0)
                return;
            character.StudyPoints = before + added;
            await client.SaveAsync("study_points");
            await client.SendAsync(MsgSubPro.StudyPointsPacket(before, added));

            LogEvents.For(typeof(SubClasses), LogEvents.SubClassStudyPoints).Information(
                "{Name} received {Added} study points (total {Total})",
                character.Name, added, character.StudyPoints);
        }

        private static async Task SyncBonusStatsAsync(Client client, SubClassId id)
        {
            if (id != client.SubClasses.Active)
                return;
            await Items.Equipping.FollowMaximumsAsync(client);
            await client.SendAsync(MsgPlayerAttribInfo.Of(client));
        }

        private static string Reject(Client client, string action, SubClassId id, string reason)
        {
            LogEvents.For(typeof(SubClasses), LogEvents.SubClassRejected).Information(
                "{Name} could not {Action} sub-class {SubClass}: {Reason}",
                client.Character?.Name, action, id, reason);
            return reason;
        }
    }
}
