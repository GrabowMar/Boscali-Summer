using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core;
using BoscaliSummer.Features.HighCommand.Configuration;
using BoscaliSummer.Features.HighCommand.Domain;
using BoscaliSummer.Features.HighCommand.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.HighCommand.Runtime
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
        private const float IntelMemorySeconds = 45f;
        private const float IntelPostRadius = 2600f;
        private const float IntelConvoyRadius = 4200f;
        private const float IntelIntervalSeconds = 1f;
        private const float TransferDwellSeconds = 60f;
        private const float TransferTimeoutSeconds = 900f;
        private const float RefreshSeconds = 4f;
        private const float SignalSeconds = 75f;
        private const float CommendBoostSeconds = 120f;
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
            public Vector3 LastPosition;

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
            public readonly Vector3[] Anchors = new Vector3[CommandTier.MaximumSlots];
            public readonly bool[] HasAnchor = new bool[CommandTier.MaximumSlots];
            public readonly float[] RespawnAt = new float[CommandTier.MaximumSlots];
            public readonly bool[] RespawnScheduled = new bool[CommandTier.MaximumSlots];
            public float NextStipend;
            public float NextTransfer;
            public float CohesionBoost;
            public float BoostUntil;
            public int StipendsPaid;
            public int SpawnSerial;
            public int SeedSerial;
            public int RespawnSerial;
            public string Signal;
            public float SignalUntil;
            public readonly CommandLog Log = new CommandLog();
        }

        private readonly List<FactionCommand> factions = new List<FactionCommand>(MaximumFactions);
        private readonly List<AssetWatch> assets = new List<AssetWatch>(MaximumAssets);
        private readonly Dictionary<int, AssetWatch> assetLookup = new Dictionary<int, AssetWatch>(MaximumAssets);
        private readonly Dictionary<int, PersistentID> lastDamage = new Dictionary<int, PersistentID>(MaximumAssets);
        private readonly List<Convoy> convoys = new List<Convoy>(MaximumConvoys);
        private readonly float[,] sight = new float[MaximumFactions, MaximumFactions * CommandTier.MaximumSlots];
        private readonly List<CommanderView> viewCommanders = new List<CommanderView>(CommandSnapshotRules.MaximumNodes);
        private readonly List<CommanderLogLine> viewLog = new List<CommanderLogLine>(CommandLog.Capacity);
        private readonly List<CommanderLogLine> viewHostileLog = new List<CommanderLogLine>(CommandLog.Capacity);
        private readonly CommandLogEntry[] hostileScratch = new CommandLogEntry[CommandSnapshotRules.MaximumLogRows];

        private HighCommandSettings settings;
        private HighCommandNet network;
        private ManualLogSource logger;
        private object missionIdentity;
        private int missionGeneration;
        private float nextTick;
        private float lastRefreshRequest = -100f;
        private string status = "Waiting for a running mission.";
        private string signal = "";
        private float cohesion;
        private int points, active, kia;
        private FactionHQ viewHq;
        private bool wasEnabled;

        // ---- IHighCommandView -------------------------------------------------------------

        public bool Available => viewHq != null;
        public string Status => status;
        public string Signal => signal;
        public float FriendlyCohesion => cohesion;
        public int FriendlyActive => active;
        public int FriendlyKia => kia;
        public IReadOnlyList<CommanderView> Commanders => viewCommanders;
        public IReadOnlyList<CommanderLogLine> Log => viewLog;
        public IReadOnlyList<CommanderLogLine> HostileLog => viewHostileLog;

        public void Refresh()
        {
            if (settings == null || !settings.Enabled.Value) return;
            if (Time.unscaledTime - lastRefreshRequest < RefreshSeconds) return;
            lastRefreshRequest = Time.unscaledTime;
            network?.Request();
        }

        // ---- Lifecycle --------------------------------------------------------------------

        public void Configure(HighCommandSettings configuration, HighCommandNet transport, ManualLogSource log)
        {
            settings = configuration;
            network = transport;
            logger = log;
            wasEnabled = settings.Enabled.Value;
            Active = this;
        }

        public void ResetForScene()
        {
            for (int i = 0; i < assets.Count; i++) Unwatch(assets[i]);
            for (int i = 0; i < convoys.Count; i++) DestroyConvoy(convoys[i]);
            factions.Clear();
            assets.Clear();
            assetLookup.Clear();
            lastDamage.Clear();
            convoys.Clear();
            Array.Clear(sight, 0, sight.Length);
            viewCommanders.Clear();
            viewLog.Clear();
            viewHostileLog.Clear();
            Array.Clear(hostileScratch, 0, hostileScratch.Length);
            missionIdentity = null;
            nextTick = 0f;
            viewHq = null;
            status = "Waiting for a running mission.";
            signal = "";
            cohesion = 0f;
            points = active = kia = 0;
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
                status = "Chain of command is disabled on this host.";
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
                status = "Waiting for a running mission.";
                return;
            }
            if (!GameAccess.IsServer())
            {
                if (!GameManager.GetLocalPlayer<Player>(out Player local) || local == null || local.HQ != viewHq)
                    status = "Waiting for the staff board.";
                return;
            }

            float now = mission.MissionTime;
            if (!Finite(now)) return;
            if (now < nextTick) return;
            nextTick = now + 1f;
            Tick(now);
        }

        private void Tick(float now)
        {
            EnsureFactions();
            SweepAssets();
            TickDisruption(now);
            TickStipends(now);
            TickTransfers(now);
            SpawnMissingAssets();
            TickConvoys(now);
            TickIntel(now);
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
                CollectSites(hq, sites);
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
                };
                command.Sites.AddRange(sites);
                factions.Add(command);
                logger?.LogInfo("[HighCommand] Staff formed for " + command.FactionName + ": " +
                                command.Tree.LiveCount + " posts over " + sites.Count + " bases.");
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

                float boost = Boost(command, now);
                float current = command.Tree.Cohesion(boost);
                float traitMultiplier = 1f;
                for (int s = 0; s < command.Tree.Slots.Count; s++)
                {
                    CommandSlot slot = command.Tree.Slots[s];
                    if (!slot.Alive || slot.Person == null) continue;
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
            if (player == null || player.HQ == null) return snapshot;
            FactionCommand own = FindFaction(player.HQ);
            if (own == null)
            {
                snapshot.Status = "No staff has formed for your faction.";
                return snapshot;
            }

            float now = MissionTime;
            int observer = factions.IndexOf(own);
            var nodes = new List<CommanderWire>(CommandSnapshotRules.MaximumNodes);
            AppendFactionNodes(own, own, observer, now, nodes, true);
            for (int i = 0; i < factions.Count && nodes.Count < CommandSnapshotRules.MaximumNodes; i++)
            {
                if (factions[i] == own) continue;
                AppendFactionNodes(factions[i], own, observer, now, nodes, false);
            }

            snapshot.Nodes = nodes.ToArray();
            snapshot.Log = BuildLog(own, now);
            snapshot.HostileLog = BuildHostileLog(own, observer, now);
            snapshot.Cohesion = own.Tree.Cohesion(Boost(own, now));
            snapshot.Active = own.Tree.LiveCount;
            snapshot.Kia = own.Tree.KiaCount;
            snapshot.Signal = now < own.SignalUntil ? own.Signal : "";
            return snapshot;
        }

        /// <summary>Every post is listed. An unconfirmed enemy post keeps its identity and
        /// role — the console hosts both staffs — while disposition stays behind intel.</summary>
        private void AppendFactionNodes(FactionCommand owner, FactionCommand observerCommand, int observer,
            float now, List<CommanderWire> nodes, bool friendly)
        {
            float total = Mathf.Max(1f, owner.Tree.TotalWeight);
            for (int i = 0; i < owner.Tree.Slots.Count && nodes.Count < CommandSnapshotRules.MaximumNodes; i++)
            {
                CommandSlot slot = owner.Tree.Slots[i];
                bool known = friendly || IsKnown(observerCommand, owner, slot, now);
                nodes.Add(BuildWire(owner, observerCommand, slot, friendly, known, total, now));
            }
        }

        private CommanderWire BuildWire(FactionCommand owner, FactionCommand observer, CommandSlot slot,
            bool friendly, bool known, float totalWeight, float now)
        {
            byte flags = 0;
            if (friendly) flags |= CommanderWire.Friendly;
            if (known) flags |= CommanderWire.Known;
            if (!slot.Alive) flags |= CommanderWire.Kia;
            // An unconfirmed post is not allowed to leak where its commander is or whether
            // they are moving; only identity and office cross the wire.
            if (known && slot.Status == CommanderStatus.InTransit) flags |= CommanderWire.Transit;
            if (known && slot.Status == CommanderStatus.Disrupted) flags |= CommanderWire.Disrupted;
            if ((friendly || known) && now < slot.AlertUntil) flags |= CommanderWire.Alert;

            AssetWatch asset = FindActiveAsset(owner, slot);
            Vector3 position = asset?.Unit != null ? asset.Unit.transform.position : Vector3.zero;
            string location = !friendly && !known ? "UNCONFIRMED"
                : slot.Status == CommanderStatus.InTransit && asset != null
                    ? "EN ROUTE · " + (FindConvoy(owner, slot)?.DestinationName ?? "UNKNOWN")
                    : slot.SiteName;

            return new CommanderWire
            {
                // Global id: slot ids are only unique inside one tree, and the board lists
                // every faction, so the faction index is folded into the wire identity.
                Id = GlobalId(factions.IndexOf(owner), slot.Id),
                ParentId = slot.ParentId < 0 ? -1 : GlobalId(factions.IndexOf(owner), slot.ParentId),
                Tier = (byte)slot.Tier,
                Flags = flags,
                TraitMask = (byte)(slot.Person?.Traits ?? CommandTrait.None),
                Seed = slot.Person?.Seed ?? 0,
                IntelAge = friendly || !known ? -1f : Mathf.Max(0f, now - LastSight(factions.IndexOf(observer), owner, slot)),
                Weight = Mathf.Clamp01(owner.Tree.EffectiveWeight(slot) / totalWeight),
                X = friendly || known ? position.x : 0f,
                Z = friendly || known ? position.z : 0f,
                Name = slot.Person?.Name ?? "UNKNOWN",
                Rank = slot.Person?.Rank ?? CommandTier.Rank(slot.Tier),
                Role = slot.Role,
                Location = location,
            };
        }

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
        /// Enemy events this faction had eyes on: an entry is sent when the observer's last
        /// sight of the subject post is at or after the event, so the log cannot narrate a
        /// post the roster is still hiding.
        /// </summary>
        private CommanderLogWire[] BuildHostileLog(FactionCommand own, int observer, float now)
        {
            int shown = 0;
            for (int f = 0; f < factions.Count; f++)
            {
                FactionCommand owner = factions[f];
                if (owner == own) continue;
                for (int i = 0; i < owner.Log.Count; i++)
                {
                    CommandLogEntry entry = owner.Log[i];
                    if (entry.TargetId < 0) continue;
                    // The subject is read from the id, not from the ring's owner: an
                    // observer's own log also carries contact reports about enemy posts.
                    int subjectIndex = FactionOf(entry.TargetId);
                    if (subjectIndex < 0 || subjectIndex >= factions.Count || subjectIndex == observer) continue;
                    FactionCommand subject = factions[subjectIndex];
                    CommandSlot slot = subject.Tree.Find(LocalId(entry.TargetId));
                    if (slot == null || !CommandLog.VisibleToObserver(LastSight(observer, subject, slot), entry.Time))
                        continue;

                    int at = shown;
                    while (at > 0 && hostileScratch[at - 1].Time < entry.Time) at--;
                    if (at >= CommandSnapshotRules.MaximumLogRows) continue;
                    for (int j = Math.Min(shown, CommandSnapshotRules.MaximumLogRows - 1); j > at; j--)
                        hostileScratch[j] = hostileScratch[j - 1];
                    hostileScratch[at] = entry;
                    if (shown < CommandSnapshotRules.MaximumLogRows) shown++;
                }
            }

            var rows = new CommanderLogWire[shown];
            for (int i = 0; i < shown; i++)
            {
                rows[i] = new CommanderLogWire
                {
                    TargetId = hostileScratch[i].TargetId,
                    Tone = (byte)hostileScratch[i].Tone,
                    Text = CommandLog.Bounded(hostileScratch[i].Text),
                    Age = LogAge(hostileScratch[i].Time, now),
                };
            }
            return rows;
        }

        private static float LogAge(float time, float now)
        {
            float age = now - time;
            return Finite(age) && age > 0f ? age : 0f;
        }

        internal void Apply(HighCommandSnapshot snapshot)
        {
            viewCommanders.Clear();
            viewLog.Clear();
            viewHostileLog.Clear();
            AppendLog(viewLog, snapshot.Log);
            AppendLog(viewHostileLog, snapshot.HostileLog);
            cohesion = snapshot.Cohesion;
            active = snapshot.Active;
            kia = snapshot.Kia;
            signal = snapshot.Signal ?? "";
            if (!string.IsNullOrEmpty(snapshot.Status)) status = snapshot.Status;

            CommanderWire[] nodes = snapshot.Nodes;
            if (nodes == null) return;
            for (int i = 0; i < nodes.Length && viewCommanders.Count < CommandSnapshotRules.MaximumNodes; i++)
            {
                CommanderWire node = nodes[i];
                var traits = (CommandTrait)node.TraitMask;
                bool friendly = (node.Flags & CommanderWire.Friendly) != 0;
                bool isKia = (node.Flags & CommanderWire.Kia) != 0;
                viewCommanders.Add(new CommanderView(
                    node.Id, node.ParentId, node.Tier,
                    friendly,
                    (node.Flags & CommanderWire.Known) != 0,
                    isKia,
                    (node.Flags & CommanderWire.Transit) != 0,
                    (node.Flags & CommanderWire.Disrupted) != 0,
                    (node.Flags & CommanderWire.Alert) != 0,
                    node.Name, node.Rank, node.Role, node.Location,
                    CommandTraits.BonusLine(traits),
                    CommanderGenerator.Bio(node.Seed, traits),
                    node.Seed,
                    // The same generated portrait every ace and wingman uses. Wing Command
                    // owns the sprite; it is borrowed here and never destroyed.
                    WingLink.PilotPortrait(node.Name, ""),
                    node.IntelAge, node.Weight, node.X, node.Z));
            }

            if (GameAccess.IsServer() && viewCommanders.Count > 0)
            {
                FactionCommand own = ViewCommand();
                if (own != null)
                {
                    signal = MissionTime < own.SignalUntil ? own.Signal : "";
                    viewHq = own.Hq;
                }
            }
            else if (viewCommanders.Count > 0 && GameManager.GetLocalPlayer<Player>(out Player local) && local != null)
            {
                viewHq = local.HQ;
            }
        }

        internal void SetLocalStatus(string value) => status = value;

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

        private static float Boost(FactionCommand command, float now) =>
            command.BoostUntil > now ? command.CohesionBoost : 0f;

        private static string Percent(float value) => Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
