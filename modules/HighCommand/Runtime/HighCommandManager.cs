using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.HighCommand.Configuration;
using BoscaliSummer.Modules.HighCommand.Domain;
using BoscaliSummer.Modules.HighCommand.Networking;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.HighCommand.Runtime
{
    /// <summary>
    /// Host-authoritative chain of command. Generates one staff per faction, puts each post
    /// on the map as a real building, runs VIP convoys between bases, pays stipends and
    /// bounties, and hides enemy dispositions behind local intel. Clients receive both
    /// staffs by identity; nothing here mutates vanilla AI, spawn rates or damage.
    /// </summary>
    internal sealed partial class HighCommandManager : MonoBehaviour, ISceneService, IHighCommandView
    {
        private const int MaximumFactions = 8;
        private const int MaximumAssets = 32;
        private const int MaximumConvoys = 8;
        private const float IntelPostRadius = StaffWords.PostSightMetres;
        private const float TransferDwellSeconds = 60f;
        private const float TransferTimeoutSeconds = 900f;
        private const float RefreshSeconds = 4f;
        private const float StaleSeconds = 20f;
        private const float AlertSeconds = 12f;

        internal enum AssetKind : byte
        {
            Post = 0,
            ConvoyLead = 1,
        }

        private sealed class AssetWatch
        {
            public FactionCommand Owner;
            public CommandSlot Slot;
            public Unit Unit;
            public AssetKind Kind;
            public int InstanceId;

            public void Watch()
            {
                if (Unit != null) Unit.onDisableUnit += OnDisabled;
            }

            public void Unwatch()
            {
                if (Unit != null) Unit.onDisableUnit -= OnDisabled;
                Unit = null;
            }

            private void OnDisabled(Unit unit)
            {
                HighCommandManager.Active?.OnAssetDisabled(this, unit);
            }
        }

        private sealed class Convoy
        {
            public FactionCommand Owner;
            public CommandSlot Slot;
            public Unit Lead;
            public readonly List<Unit> Vehicles = new List<Unit>(3);
            public Vector3 Home;
            public Vector3 Destination;
            public string DestinationName;
            public byte Phase;
            public float PhaseUntil;
            public float Deadline;
        }

        private sealed class FactionCommand
        {
            public FactionHQ Hq;
            public string FactionName;
            public int FactionToken;
            public CommandTree Tree;
            public readonly List<string> Sites = new List<string>(CommandTree.MaximumSites);
            /// <summary>GLOBAL metres (a local position goes stale at every floating-origin shift).</summary>
            public readonly Vector3[] AnchorsGlobal = new Vector3[CommandTier.MaximumSlots];
            /// <summary>Scene object name of a base to its display name: the name is the key, the display name is what players read.</summary>
            public Dictionary<string, string> Labels = new Dictionary<string, string>();
            public readonly bool[] HasAnchor = new bool[CommandTier.MaximumSlots];
            public readonly float[] RespawnAt = new float[CommandTier.MaximumSlots];
            public readonly bool[] RespawnScheduled = new bool[CommandTier.MaximumSlots];
            public float NextStipend;
            public float NextTransfer;
            public int StipendsPaid;
            public int SpawnSerial;
            public int SeedSerial;
            public int RespawnSerial;
            public readonly CommandLog Log = new CommandLog();
        }

        private readonly List<FactionCommand> factions = new List<FactionCommand>(MaximumFactions);
        private readonly List<AssetWatch> assets = new List<AssetWatch>(MaximumAssets);
        private readonly Dictionary<int, AssetWatch> assetLookup = new Dictionary<int, AssetWatch>(MaximumAssets);
        private readonly Dictionary<int, PersistentID> lastDamage = new Dictionary<int, PersistentID>(MaximumAssets);
        private readonly List<Convoy> convoys = new List<Convoy>(MaximumConvoys);
        private readonly PostIntel intel = new PostIntel();
        private readonly BountyLedger bounties = new BountyLedger();
        private readonly PostCandidate[] pickScratch = new PostCandidate[32];
        private readonly List<CommanderView> viewCommanders = new List<CommanderView>(CommandSnapshotRules.MaximumNodes);
        private readonly List<CommanderLogLine> viewLog = new List<CommanderLogLine>(CommandLog.Capacity);

        private HighCommandSettings settings;
        private HighCommandNet network;
        private object missionIdentity;
        private int missionGeneration;
        private PhasedGate tickGate;
        private float lastRefreshRequest = -100f;
        private string status = "Waiting for a running mission.";
        private float appliedAt = float.NegativeInfinity;
        private FactionHQ viewHq;
        private bool wasEnabled;

        /// <summary>Funds paid as command-post bounties this mission (the sim reads it: it stays 0 unless a real death was paid).</summary>
        internal int BountyPaidTotal { get; private set; }

        /// <summary>The office the last STRIKE COMMAND POST pick named (the sim kills it); -1 before any pick.</summary>
        internal int LastPickId { get; private set; } = -1;

        // ---- IHighCommandView -------------------------------------------------------------

        public float AgeSeconds => float.IsNegativeInfinity(appliedAt) ? float.PositiveInfinity : Time.unscaledTime - appliedAt;
        public bool Available => viewHq != null && AgeSeconds <= StaleSeconds;
        public string Status => viewHq != null && AgeSeconds > StaleSeconds ? StaffWords.Stale(AgeSeconds) : status;
        public IReadOnlyList<CommanderView> Commanders => viewCommanders;
        public IReadOnlyList<CommanderLogLine> Log => viewLog;

        public void Refresh()
        {
            if (settings == null || !settings.Enabled.Value) return;
            if (Time.unscaledTime - lastRefreshRequest < RefreshSeconds) return;
            lastRefreshRequest = Time.unscaledTime;
            network?.Request();
        }

        // ---- Lifecycle --------------------------------------------------------------------

        public void Configure(HighCommandSettings configuration, HighCommandNet transport)
        {
            settings = configuration;
            network = transport;
            wasEnabled = settings.Enabled.Value;
            Active = this;
        }

        public void ResetForScene()
        {
            for (int i = 0; i < assets.Count; i++) Unwatch(assets[i]);
            for (int i = 0; i < convoys.Count; i++) DestroyConvoyVehicles(convoys[i]);
            factions.Clear();
            assets.Clear();
            assetLookup.Clear();
            lastDamage.Clear();
            convoys.Clear();
            intel.Clear();
            bounties.Clear();
            BountyPaidTotal = 0;
            viewCommanders.Clear();
            viewLog.Clear();
            missionIdentity = null;
            tickGate.Reset();
            spawnFaction = -1;
            intelCursor = -1;
            viewHq = null;
            appliedAt = float.NegativeInfinity;
            status = StaffWords.WaitingMission;
            network?.ResetScene();
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            ResetForScene();
        }

        private void Update()
        {
            if (settings == null) return;
            if (!settings.Enabled.Value)
            {
                if (wasEnabled) ResetForScene();
                wasEnabled = false;
                status = StaffWords.ModuleOff;
                return;
            }
            wasEnabled = true;

            var current = MissionManager.CurrentMission;
            MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
            if (!ReferenceEquals(missionIdentity, current))
            {
                ResetForScene();
                missionIdentity = current;
                missionGeneration++;
            }
            if (current == null || mission == null || !MissionManager.IsRunning)
            {
                if (factions.Count > 0) ResetForScene();
                status = StaffWords.WaitingMission;
                return;
            }
            Refresh();       // the board is kept fresh with no surface open: the host applies its own snapshot, a client sends its query
            if (!GameAccess.IsServer()) return;

            float now = mission.MissionTime;
            if (!float.IsFinite(now)) return;
            // What the last tick left for the frames after it: one post spawn a frame, a slice of the units' sensing sweep a frame.
            if (spawnFaction >= 0) StepSpawnPass();
            if (intelCursor >= 0) StepIntel();
            if (!tickGate.Due(now, 1f, 0.125f)) return;   // 1 Hz slots (s): Space 0, HighCommand .125, Cyber .25, Operations .375, Sof .5, LivingFront .625, Ops .75, Events .875
            Tick(now);
        }

        private void Tick(float now)
        {
            EnsureFactions();
            SweepAssets();
            TickDisruption(now);
            TickStipends(now);
            TickTransfers(now);
            spawnFaction = spawnSlot = 0;   // BeginSpawnPass: the missing posts, one a frame from the next frame on
            TickConvoys(now);
            BeginIntel(now);
        }

        private void TickDisruption(float now)
        {
            for (int i = 0; i < factions.Count; i++)
            {
                CommandTree tree = factions[i].Tree;
                if (tree == null) continue;
                IReadOnlyList<CommandSlot> slots = tree.Slots;
                for (int j = 0; j < slots.Count; j++)
                {
                    CommandSlot slot = slots[j];
                    if (slot.Status == CommanderStatus.Disrupted && now >= slot.StatusUntil)
                        slot.Status = CommanderStatus.Active;
                }
            }
        }

        private void EnsureFactions()
        {
            int inspected = 0;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (++inspected > MaximumFactions) break;
                if (hq == null || hq.faction == null) continue;
                if (FindFaction(hq) != null || factions.Count >= MaximumFactions) continue;

                var sites = new List<string>(CommandTree.MaximumSites);
                var labels = new Dictionary<string, string>();
                CollectSites(hq, sites, labels);
                if (sites.Count == 0) continue;

                int seed = unchecked((int)Deterministic.Hash(
                    (int)Deterministic.HashString(hq.faction.factionName), missionGeneration, ++seedCounter, 0x5EED));
                var command = new FactionCommand
                {
                    Hq = hq,
                    FactionName = hq.faction.factionName,
                    FactionToken = factions.Count + 1,
                    Tree = CommandTree.Generate(seed, sites),
                    NextStipend = MissionTime + Math.Max(30, settings.StipendIntervalSeconds.Value),
                    NextTransfer = MissionTime + NextTransferDelay(),
                    SeedSerial = seed,
                    Labels = labels,
                };
                command.Sites.AddRange(sites);
                factions.Add(command);
                Plugin.Logger?.LogInfo("[HighCommand] Staff formed for " + command.FactionName + ": " +
                                command.Tree.Slots.Count + " posts over " + sites.Count + " bases.");
            }
        }

        private int seedCounter;

        private int NextTransferDelay()
        {
            int min = settings.TransferMinSeconds.Value;
            int max = Math.Max(min, settings.TransferMaxSeconds.Value);
            return min + (int)(Deterministic.UnitFloat(Deterministic.Hash(++seedCounter, missionGeneration, 17)) * (max - min));
        }

        // ---- Economy ----------------------------------------------------------------------

        private void TickStipends(float now)
        {
            for (int i = 0; i < factions.Count; i++)
            {
                FactionCommand command = factions[i];
                if (now < command.NextStipend) continue;
                command.NextStipend = now + Math.Max(30, settings.StipendIntervalSeconds.Value);
                if (!HasParticipant(command.Hq)) continue;

                float current = command.Tree.Cohesion(0f);
                float traitMultiplier = 1f;
                for (int s = 0; s < command.Tree.Slots.Count; s++)
                {
                    CommandSlot slot = command.Tree.Slots[s];
                    if (slot.Person == null) continue;
                    traitMultiplier = Mathf.Max(traitMultiplier, CommandTraits.StipendMultiplier(slot.Person.Traits));
                }

                if (settings.EconomyEnabled.Value && settings.StipendIntervalSeconds.Value > 0 &&
                    command.StipendsPaid < settings.MaximumStipends.Value)
                {
                    int amount = CommandEconomy.Stipend(
                        settings.StipendBaseAmount.Value, command.Tree.LiveWeight, current);
                    amount = amount <= 0 ? 0 : (int)Math.Round(amount * traitMultiplier);
                    if (amount > 0)
                    {
                        command.Hq.AddFunds(amount);
                        command.StipendsPaid++;
                        Broadcast(command, -1, CommanderLogTone.Economy,
                            "STIPEND PAID +" + amount + " · COHESION " + Percent(current), now);
                    }
                }
            }
        }

        // ---- Snapshot ---------------------------------------------------------------------

        internal HighCommandSnapshot Snapshot(Player player)
        {
            var snapshot = new HighCommandSnapshot
            {
                Protocol = HighCommandNet.ProtocolVersion,
                Status = status,
                Signal = "",
                Cohesion = 0f,
                Active = 0,
                Kia = 0,
                Nodes = Array.Empty<CommanderWire>(),
                Log = Array.Empty<CommanderLogWire>(),
                HostileLog = Array.Empty<CommanderLogWire>(),
            };
            if (player == null || player.HQ == null) { snapshot.Status = StaffWords.NoFaction; return snapshot; }
            FactionCommand own = FindFaction(player.HQ);
            if (own == null) { snapshot.Status = StaffWords.NoStaff; return snapshot; }

            float now = MissionTime;
            int observer = factions.IndexOf(own);
            var nodes = new List<CommanderWire>(CommandSnapshotRules.MaximumNodes);
            float total = Mathf.Max(1f, own.Tree.TotalWeight);
            for (int i = 0; i < own.Tree.Slots.Count && nodes.Count < CommandSnapshotRules.MaximumNodes; i++)
                nodes.Add(OwnWire(own, observer, own.Tree.Slots[i], total, now));
            int enemies = 0;
            for (int f = 0; f < factions.Count; f++)
            {
                if (f == observer) continue;
                FactionCommand owner = factions[f];
                for (int i = 0; i < owner.Tree.Slots.Count && nodes.Count < CommandSnapshotRules.MaximumNodes; i++)
                {
                    int key = GlobalId(f, owner.Tree.Slots[i].Id);
                    if (!intel.TryGet(observer, key, now, out PostSighting seen)) continue;
                    nodes.Add(EnemyWire(owner, key, owner.Tree.Slots[i], own, seen));
                    enemies++;
                }
            }

            snapshot.Nodes = nodes.ToArray();
            snapshot.Log = BuildLog(own, now);
            snapshot.Cohesion = own.Tree.Cohesion(0f);
            snapshot.Active = own.Tree.Slots.Count;
            snapshot.Status = enemies == 0 ? StaffWords.NoEnemyPost : "";
            return snapshot;
        }

        /// <summary>An own post: everything about it, with its current (global) position.</summary>
        private CommanderWire OwnWire(FactionCommand owner, int index, CommandSlot slot, float totalWeight, float now)
        {
            byte flags = (byte)(CommanderWire.Friendly | CommanderWire.Known);
            if (slot.Status == CommanderStatus.InTransit) flags |= CommanderWire.Transit;
            if (slot.Status == CommanderStatus.Disrupted) flags |= CommanderWire.Disrupted;
            if (now < slot.AlertUntil) flags |= CommanderWire.Alert;

            AssetWatch asset = FindActiveAsset(owner, slot);
            // The wire carries an absolute position: the map places icons at global metres, so a local
            // transform would be shifted by the floating datum. A post between spawns reports its base.
            GlobalPosition position = asset?.Unit != null
                ? asset.Unit.transform.position.ToGlobalPosition()
                : owner.HasAnchor[slot.Id]
                    ? new GlobalPosition(owner.AnchorsGlobal[slot.Id].x, owner.AnchorsGlobal[slot.Id].y, owner.AnchorsGlobal[slot.Id].z)
                    : default;
            string site = Label(owner, slot.SiteName);
            return new CommanderWire
            {
                Id = GlobalId(index, slot.Id),
                ParentId = slot.ParentId < 0 ? -1 : GlobalId(index, slot.ParentId),
                Tier = (byte)slot.Tier,
                Flags = flags,
                TraitMask = (byte)(slot.Person?.Traits ?? CommandTrait.None),
                Seed = slot.Person?.Seed ?? 0,
                IntelAge = -1f,
                Weight = Mathf.Clamp01(owner.Tree.EffectiveWeight(slot) / totalWeight),
                X = position.x,
                Z = position.z,
                Name = slot.Person?.Name ?? "UNKNOWN",
                Rank = slot.Person?.Rank ?? CommandTier.Rank(slot.Tier),
                Role = slot.Role,
                Location = slot.Status == CommanderStatus.InTransit && asset != null
                    ? "EN ROUTE · " + Label(owner, FindConvoy(owner, slot)?.DestinationName) : site,
                Site = site,
                Bounty = 0,
            };
        }

        /// <summary>A confirmed enemy post, only: where the observer last saw the building and how long ago. No internal state of the staff leaks (no travel, disruption or alert).</summary>
        private CommanderWire EnemyWire(FactionCommand owner, int key, CommandSlot slot, FactionCommand observer, PostSighting seen)
        {
            bool claimed = bounties.Claimed(observer.Hq.GetInstanceID(), key);
            string site = Label(owner, slot.SiteName);
            return new CommanderWire
            {
                Id = key,
                ParentId = slot.ParentId < 0 ? -1 : GlobalId(FactionOf(key), slot.ParentId),
                Tier = (byte)slot.Tier,
                Flags = (byte)(CommanderWire.Known | (claimed ? CommanderWire.Claimed : 0)),
                TraitMask = (byte)(slot.Person?.Traits ?? CommandTrait.None),
                Seed = slot.Person?.Seed ?? 0,
                IntelAge = seen.Age,
                Weight = 0f,
                X = seen.X,
                Z = seen.Z,
                Name = slot.Person?.Name ?? "UNKNOWN",
                Rank = slot.Person?.Rank ?? CommandTier.Rank(slot.Tier),
                Role = slot.Role,
                Location = site,
                Site = site,
                Bounty = claimed ? 0 : BountyFor(slot),
            };
        }

        /// <summary>What killing this post pays a human faction that has not yet taken the office; 0 when pay is off.</summary>
        private int BountyFor(CommandSlot slot) =>
            !settings.EconomyEnabled.Value || slot.Person == null ? 0 :
            Mathf.Clamp(CommandEconomy.BountyFunds(slot.Tier, settings.BountyBaseFunds.Value, settings.BountyComponentFunds.Value,
                settings.BountyTheaterFunds.Value, CommandTraits.BountyMultiplier(slot.Person.Traits)), 0, CommandSnapshotRules.MaximumBounty);

        /// <summary>The local faction's own log, complete: its own staff has no fog.</summary>
        private CommanderLogWire[] BuildLog(FactionCommand owner, float now)
        {
            int count = owner.Log.Count;
            var rows = new CommanderLogWire[count];
            for (int i = 0; i < count; i++)
            {
                CommandLogEntry entry = owner.Log[i];
                rows[i] = new CommanderLogWire
                {
                    TargetId = entry.TargetId,
                    Tone = (byte)entry.Tone,
                    Text = CommandLog.Bounded(entry.Text),
                    Age = LogAge(entry.Time, now),
                };
            }
            return rows;
        }

        /// <summary>
        /// The ring never expires entries, so a quiet faction's oldest line can outlive the
        /// wire's age bound; clamping keeps that line "15+ minutes old" instead of making every
        /// remote client reject the whole snapshot.
        /// </summary>
        private static float LogAge(float time, float now)
        {
            float age = now - time;
            if (!float.IsFinite(age) || age <= 0f) return 0f;
            return age < CommandSnapshotRules.MaximumLogAge ? age : CommandSnapshotRules.MaximumLogAge;
        }

        internal void Apply(HighCommandSnapshot snapshot)
        {
            viewCommanders.Clear();
            viewLog.Clear();
            AppendLog(viewLog, snapshot.Log);
            appliedAt = Time.unscaledTime;
            status = snapshot.Status ?? "";

            CommanderWire[] nodes = snapshot.Nodes;
            if (nodes != null)
            {
                bool host = GameAccess.IsServer();
                int localFaction = PortraitFactions.Local;
                int enemyFaction = host ? -1 : PortraitFactions.OpposingLocal;
                for (int i = 0; i < nodes.Length && viewCommanders.Count < CommandSnapshotRules.MaximumNodes; i++)
                {
                    CommanderWire node = nodes[i];
                    var traits = (CommandTrait)node.TraitMask;
                    bool friendly = (node.Flags & CommanderWire.Friendly) != 0;
                    int portraitFaction = friendly ? localFaction : enemyFaction;
                    int owner = FactionOf(node.Id);
                    if (host && owner >= 0 && owner < factions.Count)
                        portraitFaction = PortraitFactions.Of(factions[owner].Hq);
                    viewCommanders.Add(new CommanderView(
                        node.Id, node.Tier, friendly, (node.Flags & CommanderWire.Alert) != 0,
                        node.Name, node.Rank, node.Role, node.Site,
                        friendly ? CommandTraits.BonusLine(traits) : CommandTraits.StrikeLine(traits),
                        CommanderGenerator.Bio(node.Seed, traits),
                        node.Seed,
                        // Every staff post is a commander. The shared identity generator
                        // keeps faces stable and selects clothing/equipment for this role.
                        WingLink.PersonnelPortrait(node.Name, "", PortraitRole.Commander, portraitFaction),
                        node.IntelAge, node.X, node.Z, node.Bounty, (node.Flags & CommanderWire.Claimed) != 0, node.ParentId));
                }
            }

            if (viewCommanders.Count == 0) viewHq = null;
            else if (GameAccess.IsServer())
            {
                FactionCommand own = ViewCommand();
                if (own != null) viewHq = own.Hq;
            }
            else if (GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
            {
                viewHq = local.HQ;
            }
        }

        /// <summary>The board is not usable (no mission, no faction, no answer): drop it and say why.</summary>
        internal void SetLocalStatus(string value)
        {
            viewCommanders.Clear();
            viewLog.Clear();
            viewHq = null;
            appliedAt = float.NegativeInfinity;
            status = value;
        }

        /// <summary>Host only: the nearest confirmed standing enemy post this faction has not already taken the bounty of.</summary>
        public bool TryPickStrikePost(FactionHQ attacker, float nearX, float nearZ, float maxMetres,
            Predicate<int> skip, out CommandPostTarget target)
        {
            target = default;
            if (settings == null || !settings.Enabled.Value || !GameAccess.IsServer() || attacker == null) return false;
            int observer = FactionIndex(attacker);
            if (observer < 0) return false;
            float now = MissionTime;
            bool fix = float.IsFinite(nearX) && float.IsFinite(nearZ);
            int count = 0;
            for (int f = 0; f < factions.Count; f++)
            {
                if (f == observer) continue;
                FactionCommand owner = factions[f];
                for (int s = 0; s < owner.Tree.Slots.Count && count < pickScratch.Length; s++)
                {
                    CommandSlot slot = owner.Tree.Slots[s];
                    int key = GlobalId(f, slot.Id);
                    Unit building = FindAsset(owner, slot, AssetKind.Post)?.Unit;
                    // A commander away on a convoy would pay no bounty: the host does not offer a card it knows would pay nothing.
                    if (building == null || building.disabled || building.NetworkHQ != owner.Hq || slot.Status == CommanderStatus.InTransit ||
                        !intel.TryGet(observer, key, now, out PostSighting seen)) continue;
                    pickScratch[count++] = new PostCandidate
                    {
                        Id = key, Tier = slot.Tier,
                        Distance = fix ? Mathf.Sqrt((seen.X - nearX) * (seen.X - nearX) + (seen.Z - nearZ) * (seen.Z - nearZ)) : NearestOwnedBase(attacker, seen.X, seen.Z),
                        Claimed = bounties.Claimed(attacker.GetInstanceID(), key),
                    };
                }
            }
            int pick = StrikePostRules.Pick(pickScratch, count, maxMetres, skip);
            if (pick < 0) return false;
            int id = pickScratch[pick].Id;
            FactionCommand picked = factions[FactionOf(id)];
            CommandSlot chosen = picked.Tree.Find(LocalId(id));
            Unit post = FindAsset(picked, chosen, AssetKind.Post).Unit;
            intel.TryGet(observer, id, now, out PostSighting at);
            LastPickId = id;
            target = new CommandPostTarget(id, post, at.X, at.Y, at.Z, chosen.Person?.Rank ?? "", chosen.Person?.Name ?? "", chosen.Role,
                Label(picked, chosen.SiteName), BountyFor(chosen));
            return true;
        }

        /// <summary>Metres from a global point to the nearest airbase the faction owns; NaN when it owns none.</summary>
        private static float NearestOwnedBase(FactionHQ hq, float x, float z)
        {
            float best = float.NaN;
            int inspected = 0;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (++inspected > 64) break;
                if (airbase == null || airbase.CurrentHQ != hq || airbase.center == null) continue;
                GlobalPosition at = airbase.center.GlobalPosition();
                float d = Mathf.Sqrt((at.x - x) * (at.x - x) + (at.z - z) * (at.z - z));
                if (float.IsNaN(best) || d < best) best = d;
            }
            return best;
        }

        private static void AppendLog(List<CommanderLogLine> target, CommanderLogWire[] rows)
        {
            if (rows == null) return;
            for (int i = 0; i < rows.Length && target.Count < CommandLog.Capacity; i++)
            {
                CommanderLogWire row = rows[i];
                target.Add(new CommanderLogLine(row.TargetId, (CommanderLogTone)row.Tone,
                    row.Text ?? "", row.Age));
            }
        }

        private FactionCommand ViewCommand()
        {
            if (GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
                return FindFaction(local.HQ);
            return null;
        }

        private static string Percent(float value) => Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";

    }
}
