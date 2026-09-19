using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Events.Configuration;
using BoscaliSummer.Features.Events.Domain;
using BoscaliSummer.Features.Events.Networking;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Events.Runtime
{
    /// <summary>Outcome of one response intent, validated on the host.</summary>
    internal enum EventResponseResult : byte
    {
        Accepted = 0,
        NoEvent = 1,
        StaleEvent = 2,
        NoEffect = 3,
        Insufficient = 4,
        AlreadyResponded = 5,
        Disabled = 6,
        Busy = 7,
    }

    /// <summary>
    /// Host-authoritative world-event director. Rolls one curated event at a time on a
    /// randomized gap, keeps a bounded mission history, and publishes its live modifier
    /// through <see cref="IActiveEventsView"/>. Clients mirror the broadcast state; the
    /// host applies it in-process and never round-trips.
    ///
    /// <para>The director grades its rolls: minor weather, a medium move on one price, and
    /// rare scripted superevents aimed at the leading or losing side. A superevent's beats
    /// run from the catalog's authored table and its effects stay inside real seams —
    /// support prices, faction funds, per-player allocation, and the vanilla convoy queue
    /// the side already owns.</para>
    ///
    /// <para>A player may spend allocation on one response per event: contain a penalty or
    /// deepen a discount for the rest of its run. The host derives the cost and owns the
    /// deduction; the response is per player, so a faction's members decide for themselves.</para>
    /// </summary>
    internal sealed class EventsManager : MonoBehaviour, ISceneService, IActiveEventsView
    {
        private const float TickSeconds = 1f;
        private const float HeartbeatSeconds = 15f;
        private const float SignalSeconds = 12f;
        private const int MaximumResponses = 64;
        private const int MaximumFactions = 8;
        private const int MaximumFactionPlayers = 64;
        private const int MaximumConvoyGroups = 8;

        private static readonly ActiveEventStep[] NoSteps = new ActiveEventStep[0];

        private EventsSettings settings;
        private EventsNet network;
        private ManualLogSource logger;

        private readonly List<ActiveEventView> history = new List<ActiveEventView>(16);
        private readonly List<int> recent = new List<int>(EventSelector.RecentWindow);
        private readonly Dictionary<ulong, byte> responses = new Dictionary<ulong, byte>(16);
        private readonly HashSet<int> usedSupers = new HashSet<int>();

        private readonly List<FactionHQ> factions = new List<FactionHQ>(MaximumFactions);
        private readonly List<int> factionBases = new List<int>(MaximumFactions);
        private readonly Dictionary<ulong, int> playerFactions = new Dictionary<ulong, int>(64);

        private ActiveEventView current;
        private object missionIdentity;
        private int missionGeneration;
        private int rotationCounter;
        private int currentIndex = -1;
        private int queriedIndex = -1;
        private int targetHash;
        private string targetName;
        private FactionHQ targetFaction;
        private int firedSteps;
        private int supersFired;
        private float lastSuperAt = float.NegativeInfinity;
        private float currentStart;
        private float currentEnd;
        private float nextRotation;
        private float nextHeartbeat;
        private float nextTick;
        private float nextBalance;
        private float lastMissionTime;
        private bool wasEnabled;
        private TheaterBalance balance = TheaterBalance.Unknown;
        private string signal = "";
        private float signalUntil;

        public bool Available { get; private set; }
        public ActiveEventView Current => current;
        public IReadOnlyList<ActiveEventView> History => history;

        /// <summary>Ground ownership read at 1 Hz on every peer, for the director readout.</summary>
        internal TheaterBalance Balance => balance;

        /// <summary>Supers fired this mission; the host owns the count, clients read a mirror.</summary>
        internal int SupersFired => supersFired;

        /// <summary>
        /// Bumped every time a superevent begins on this peer — host at roll, client at
        /// receive. The alert overlay shows each serial once, so a late joiner still gets
        /// the notice for a super already running.
        /// </summary>
        internal int SuperSerial { get; private set; }

        /// <summary>True when the active event's modifier applies to the local player's side.</summary>
        internal bool LocalTargeted
        {
            get
            {
                EventDefinition definition = EventCatalog.At(currentIndex);
                if (definition == null || definition.Target == EventTarget.All) return false;
                ulong id = LocalPlayerId();
                return id != PlayerIdentity.None && playerFactions.TryGetValue(id, out int hash) &&
                       hash == targetHash && targetHash != 0;
            }
        }

        /// <summary>Transient feedback line for the panel's status strip.</summary>
        internal string Signal => Time.unscaledTime < signalUntil ? signal : "";

        // ---- Pricing (IActiveEventsView) ---------------------------------------------------

        public float SupportCostMultiplier => SupportCostMultiplierFor(LocalPlayerId());

        public float SupportCostMultiplierFor(ulong playerId)
        {
            float multiplier = BaseMultiplierFor(playerId);
            if (playerId != PlayerIdentity.None && responses.TryGetValue(playerId, out byte kind))
                multiplier = EventSelector.ApplyResponse(multiplier, (EventResponseKind)kind);
            return multiplier;
        }

        /// <summary>The active event's multiplier for the local side, before any response.</summary>
        internal float LocalBaseMultiplier => BaseMultiplierFor(LocalPlayerId());

        private float BaseMultiplierFor(ulong playerId)
        {
            EventDefinition definition = EventCatalog.At(currentIndex);
            if (definition == null || settings == null) return 1f;
            if (definition.Target == EventTarget.All) return Effective(definition);
            int hash = playerId != PlayerIdentity.None && playerFactions.TryGetValue(playerId, out int value)
                ? value
                : 0;
            return hash != 0 && hash == targetHash ? Effective(definition) : 1f;
        }

        private float BaseMultiplierFor(Player player) =>
            BaseMultiplierFor(PlayerIdentity.Of(player));

        private float Effective(EventDefinition definition) =>
            EventSelector.EffectiveSupportMultiplier(
                definition.SupportCostMultiplier, settings.EffectStrength.Value);

        // ---- Response state ----------------------------------------------------------------

        /// <summary>Allocation this player would pay to respond to the active event; 0 = none.</summary>
        internal int ResponseCost => currentIndex < 0 ? 0 : EventSelector.ResponseCost(LocalBaseMultiplier);

        internal EventResponseKind LocalResponse
        {
            get
            {
                ulong id = LocalPlayerId();
                return id != PlayerIdentity.None && responses.TryGetValue(id, out byte kind)
                    ? (EventResponseKind)kind
                    : EventResponseKind.None;
            }
        }

        internal bool CanRespond => currentIndex >= 0 &&
            LocalResponse == EventResponseKind.None &&
            EventSelector.ResponseKind(LocalBaseMultiplier) != EventResponseKind.None;

        internal void RequestResponse()
        {
            if (currentIndex < 0) return;
            if (LocalResponse != EventResponseKind.None)
            {
                SetSignal("RESPONSE ALREADY ACTIVE");
                return;
            }
            network?.RequestResponse((sbyte)currentIndex);
        }

        /// <summary>Host validation for one intent (respond or query), addressed to one player.</summary>
        internal EventResponseResult Act(Player player, byte action, sbyte catalogIndex,
            out EventResponseKind kind, out int cost)
        {
            kind = EventResponseKind.None;
            cost = 0;
            if (settings == null || !settings.Enabled.Value) return EventResponseResult.Disabled;
            if (player == null || currentIndex < 0 || current == null) return EventResponseResult.NoEvent;

            float baseMultiplier = BaseMultiplierFor(player);
            cost = EventSelector.ResponseCost(baseMultiplier);

            ulong id = PlayerIdentity.Of(player);
            if (responses.TryGetValue(id, out byte existing))
            {
                kind = (EventResponseKind)existing;
                return EventResponseResult.AlreadyResponded;
            }
            // A query only asks what this player already owns; the quote above is the answer.
            if (action == EventsNet.ActionQuery) return EventResponseResult.Accepted;
            if (catalogIndex != currentIndex) return EventResponseResult.StaleEvent;

            EventResponseKind available = EventSelector.ResponseKind(baseMultiplier);
            if (available == EventResponseKind.None || cost <= 0) return EventResponseResult.NoEffect;
            if (responses.Count >= MaximumResponses) return EventResponseResult.Busy;
            if (player.Allocation + 0.001f < cost) return EventResponseResult.Insufficient;

            player.SetAllocation(Mathf.Max(0f, player.Allocation - cost));
            responses[id] = (byte)available;
            kind = available;
            SetSignal("RESPONSE ACCEPTED · " + ResponseLabel(available) + " · " + cost + " ALLOC");
            logger?.LogInfo("[Events] " + ResponseLabel(available) + " bought for " + cost +
                            " allocation against " + EventCatalog.At(currentIndex).Title + ".");
            return EventResponseResult.Accepted;
        }

        /// <summary>Applies a host answer on the requesting peer.</summary>
        internal void ApplyResponseResult(byte resultCode, byte kind, sbyte catalogIndex, int cost)
        {
            var result = (EventResponseResult)resultCode;
            // A query answer with nothing owned is a no-op, not a failure line.
            if (result == EventResponseResult.Accepted && kind == 0) return;
            if (kind != 0 && catalogIndex == currentIndex &&
                (result == EventResponseResult.Accepted ||
                 result == EventResponseResult.AlreadyResponded))
            {
                ulong id = LocalPlayerId();
                if (id != PlayerIdentity.None) responses[id] = kind;
                SetSignal(result == EventResponseResult.Accepted
                    ? "RESPONSE ACCEPTED · " + ResponseLabel((EventResponseKind)kind) + " · " + cost + " ALLOC"
                    : "RESPONSE ALREADY ACTIVE · " + ResponseLabel((EventResponseKind)kind));
                return;
            }
            if (result == EventResponseResult.Accepted && kind != 0)
            {
                SetSignal("RESPONSE ACCEPTED · EVENT ALREADY CHANGED");
                return;
            }
            SetSignal(ResultText(result));
        }

        internal void ReportOffline() => SetSignal("HOST LINK UNAVAILABLE · RESPONSE NOT SENT");

        // ---- Lifecycle --------------------------------------------------------------------

        public void Configure(EventsSettings configuration, EventsNet transport, ManualLogSource log)
        {
            settings = configuration;
            network = transport;
            logger = log;
            wasEnabled = settings.Enabled.Value;
        }

        public void ResetForScene()
        {
            history.Clear();
            recent.Clear();
            responses.Clear();
            usedSupers.Clear();
            factions.Clear();
            factionBases.Clear();
            playerFactions.Clear();
            current = null;
            currentIndex = -1;
            queriedIndex = -1;
            targetHash = 0;
            targetName = null;
            targetFaction = null;
            firedSteps = 0;
            supersFired = 0;
            lastSuperAt = float.NegativeInfinity;
            currentStart = currentEnd = 0f;
            nextRotation = nextHeartbeat = nextTick = nextBalance = 0f;
            lastMissionTime = 0f;
            rotationCounter = 0;
            missionIdentity = null;
            balance = TheaterBalance.Unknown;
            Available = false;
            SuperSerial = 0;
            signal = "";
            signalUntil = 0f;
            network?.ResetScene();
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (settings == null) return;
            // The host owns rotation, so its switch really stops the director. A client
            // cannot opt out of a host-authoritative price: turning it off there hides the
            // presentation (panel and alert check the same switch) while the mirror keeps
            // running, so this peer still predicts the price the host will charge.
            if (!settings.Enabled.Value && GameAccess.IsServer())
            {
                if (wasEnabled) ResetForScene();
                wasEnabled = false;
                return;
            }
            wasEnabled = settings.Enabled.Value;

            object missionId = MissionManager.CurrentMission;
            MissionManager mission = NetworkSceneSingleton<MissionManager>.i;
            if (!ReferenceEquals(missionIdentity, missionId))
            {
                ResetForScene();
                missionIdentity = missionId;
                missionGeneration++;
            }
            if (missionId == null || mission == null || !MissionManager.IsRunning)
            {
                if (Available || history.Count > 0) ResetForScene();
                return;
            }

            float now = mission.MissionTime;
            if (!Finite(now)) return;
            // A warm restart of the same mission rewinds the clock under an unchanged
            // identity; without this the director would stay quiet until it caught up.
            if (now + 0.001f < lastMissionTime)
            {
                ResetForScene();
                missionIdentity = missionId;
                missionGeneration++;
                lastMissionTime = now;
                return;
            }
            lastMissionTime = now;
            Available = true;
            RefreshBalance();

            if (GameAccess.IsServer())
            {
                if (now < nextTick) return;
                nextTick = now + TickSeconds;
                TickHost(now);
            }
            else if (current != null && now >= currentEnd)
            {
                // A missed end broadcast still retires locally at the stamped time.
                Retire();
            }
        }

        // ---- Theater balance ---------------------------------------------------------------

        /// <summary>
        /// Counts ground airbase custody, 1 Hz on every peer: the same networked ownership
        /// the map shows, so host and clients agree on who is losing. Carriers hold no
        /// ground and are skipped. Also refreshes the player-to-side map the targeted
        /// pricing resolves through.
        /// </summary>
        private void RefreshBalance()
        {
            if (Time.unscaledTime < nextBalance) return;
            nextBalance = Time.unscaledTime + 1f;

            factions.Clear();
            factionBases.Clear();
            playerFactions.Clear();
            int neutral = 0;

            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null || factions.Count >= MaximumFactions) continue;
                factions.Add(hq);
                factionBases.Add(0);
            }

            var lookup = FactionRegistry.airbaseLookup;
            if (lookup != null)
            {
                foreach (Airbase airbase in lookup.Values)
                {
                    if (airbase == null || airbase.AttachedAirbase || airbase.UnitDestroyed()) continue;
                    FactionHQ owner = airbase.CurrentHQ;
                    if (owner == null)
                    {
                        neutral++;
                        continue;
                    }
                    int slot = IndexOf(owner);
                    if (slot >= 0) factionBases[slot]++;
                }
            }

            int leader = -1, loser = -1, holding = 0;
            for (int i = 0; i < factions.Count; i++)
            {
                int count = factionBases[i];
                if (count > 0) holding++;
                if (leader < 0 || count > factionBases[leader]) leader = i;
                if (loser < 0 || count < factionBases[loser]) loser = i;
            }

            int leaderBases = leader < 0 ? 0 : factionBases[leader];
            int loserBases = loser < 0 ? 0 : factionBases[loser];
            balance = new TheaterBalance(
                holding, leaderBases, loserBases,
                HashOf(leader < 0 ? null : factions[leader]),
                HashOf(loser < 0 ? null : factions[loser]),
                neutral);

            targetName = null;
            for (int i = 0; i < factions.Count; i++)
            {
                int hash = HashOf(factions[i]);
                if (hash == 0) continue;
                if (targetHash != 0 && hash == targetHash)
                    targetName = factions[i].faction != null ? factions[i].faction.factionName : null;

                var players = factions[i].factionPlayers;
                if (players == null) continue;
                int count = Mathf.Min(players.Count, MaximumFactionPlayers);
                for (int p = 0; p < count; p++)
                {
                    Player player = players[p].Player;
                    if (player == null) continue;
                    ulong id = PlayerIdentity.Of(player);
                    if (id != PlayerIdentity.None) playerFactions[id] = hash;
                }
            }
        }

        private int IndexOf(FactionHQ hq)
        {
            for (int i = 0; i < factions.Count; i++)
                if (ReferenceEquals(factions[i], hq)) return i;
            return -1;
        }

        private static int HashOf(FactionHQ hq)
        {
            string name = hq != null && hq.faction != null ? hq.faction.factionName : null;
            if (string.IsNullOrEmpty(name)) return 0;
            int hash = unchecked((int)Deterministic.HashString(name));
            return hash == 0 ? 1 : hash;
        }

        // ---- Host rotation ----------------------------------------------------------------

        private void TickHost(float now)
        {
            if (current == null)
            {
                if (now < nextRotation) return;
                StartNext(now);
                return;
            }
            RunScript(now);
            if (now >= currentEnd)
            {
                Retire();
                nextRotation = now + RollGap();
                network?.Broadcast(-1, 0, 0f, 0f);
                return;
            }
            if (now >= nextHeartbeat)
            {
                // Cheap resend: covers a late joiner and a dropped broadcast alike.
                nextHeartbeat = now + HeartbeatSeconds;
                network?.Broadcast((sbyte)currentIndex, targetHash, currentStart, currentEnd);
            }
        }

        private void StartNext(float now)
        {
            uint seed = Deterministic.Hash(missionGeneration, ++rotationCounter, 0x4556);
            var state = new DirectorState(now, supersFired, lastSuperAt, balance,
                openingRoll: rotationCounter == 1);
            int index = EventDirector.Select(seed, EventCatalog.All, recent, usedSupers,
                settings.SuperEventsEnabled.Value, state);
            EventDefinition definition = EventCatalog.At(index);
            if (definition == null) return;

            int duration = EventSelector.RollDuration(
                Deterministic.Hash(unchecked((int)seed), 0x4455, 0x5555),
                definition.DurationMinSeconds, definition.DurationMaxSeconds);

            currentIndex = index;
            currentStart = now;
            currentEnd = now + duration;
            firedSteps = 0;
            targetHash = definition.Target == EventTarget.All ? 0 : balance.HashFor(definition.Target);
            targetFaction = ResolveTarget();
            targetName = targetFaction?.faction != null ? targetFaction.faction.factionName : targetName;
            if (definition.Target != EventTarget.All && targetFaction == null)
            {
                // The side vanished between roll and start; the event runs unaimed rather
                // than charging the wrong faction.
                targetHash = 0;
            }
            current = View(definition, currentStart, currentEnd);
            recent.Add(index);
            while (recent.Count > EventSelector.RecentWindow) recent.RemoveAt(0);
            nextHeartbeat = now + HeartbeatSeconds;

            if (definition.Tier == EventTier.Super)
            {
                supersFired++;
                lastSuperAt = now;
                usedSupers.Add(index);
                SuperSerial++;
                Announce(definition);
            }

            logger?.LogInfo("[Events] " + definition.Title + " (" + EventCatalog.TierLabel(definition.Tier) +
                            ", " + EventCatalog.TargetLabel(definition.Target) + ") active for " + duration +
                            "s (" + current.EffectSummary + ").");
            network?.Broadcast((sbyte)index, targetHash, currentStart, currentEnd);
        }

        private FactionHQ ResolveTarget()
        {
            if (targetHash == 0) return null;
            for (int i = 0; i < factions.Count; i++)
                if (HashOf(factions[i]) == targetHash) return factions[i];
            return null;
        }

        private float RollGap()
        {
            int min = settings.RotationGapMinSeconds.Value;
            int max = Mathf.Max(min, settings.RotationGapMaxSeconds.Value);
            return EventSelector.RollDuration(
                Deterministic.Hash(missionGeneration, ++rotationCounter, 0x4741), min, max);
        }

        private void Retire()
        {
            if (current != null)
            {
                history.Add(current);
                int keep = Mathf.Max(0, settings.HistoryLength.Value);
                while (history.Count > keep) history.RemoveAt(0);
            }
            current = null;
            currentIndex = -1;
            queriedIndex = -1;
            firedSteps = 0;
            targetHash = 0;
            targetName = null;
            targetFaction = null;
            // Responses buy out the rest of one event's run, not the next one's.
            responses.Clear();
        }

        // ---- Scripted beats (host) ---------------------------------------------------------

        private void RunScript(float now)
        {
            EventDefinition definition = EventCatalog.At(currentIndex);
            if (definition == null || definition.Script.Length == 0) return;

            EventStep[] script = definition.Script;
            while (firedSteps < script.Length && now >= currentStart + script[firedSteps].AtSeconds)
            {
                FireStep(definition, script[firedSteps]);
                firedSteps++;
            }
        }

        /// <summary>
        /// Fires one authored beat against the event's target side. Allocation and funds are
        /// real host-side credits; a convoy is funded and queued through the vanilla group
        /// the side already owns. Every beat is a no-op below EffectStrength 0, and a beat
        /// that cannot resolve its side is skipped, never guessed.
        /// </summary>
        private void FireStep(EventDefinition definition, EventStep step)
        {
            if (step == null || step.Effect == EventEffect.None || settings == null) return;
            float strength = Mathf.Clamp(settings.EffectStrength.Value, 0f, 2f);
            if (strength <= 0.001f) return;

            if (definition.Target == EventTarget.All)
            {
                for (int i = 0; i < factions.Count; i++) Apply(step, factions[i], strength);
                logger?.LogInfo("[Events] " + definition.Title + " beat: " + step.Label + " (all factions).");
                return;
            }

            if (targetFaction == null) return;
            Apply(step, targetFaction, strength);
            logger?.LogInfo("[Events] " + definition.Title + " beat: " + step.Label + " (" +
                            (targetName ?? "target") + ").");
        }

        private void Apply(EventStep step, FactionHQ hq, float strength)
        {
            if (hq == null || hq.faction == null) return;
            switch (step.Effect)
            {
                case EventEffect.Funds:
                {
                    float amount = step.Amount * strength;
                    if (amount <= 0f) return;
                    hq.AddFunds(amount);
                    break;
                }
                case EventEffect.Allocation:
                {
                    float amount = step.Amount * strength;
                    if (amount <= 0f) return;
                    var players = hq.factionPlayers;
                    if (players == null) return;
                    int count = Mathf.Min(players.Count, MaximumFactionPlayers);
                    for (int i = 0; i < count; i++)
                    {
                        Player player = players[i].Player;
                        if (player != null) player.AddAllocation(amount);
                    }
                    break;
                }
                case EventEffect.Convoy:
                    FundConvoy(hq);
                    break;
            }
        }

        /// <summary>
        /// Funds and queues the first group the side can already afford and is not cooling
        /// down: the vanilla convoy path TheaterOps uses, reached for a target other than
        /// the local HQ. Nothing is spawned here; the game deploys the group itself.
        /// </summary>
        private void FundConvoy(FactionHQ hq)
        {
            if (hq == null || hq.faction == null || hq.preventDonation) return;
            List<Faction.ConvoyGroup> groups = hq.faction.GetConvoyGroups();
            if (groups == null) return;

            int count = Mathf.Min(groups.Count, MaximumConvoyGroups);
            for (int i = 0; i < count; i++)
            {
                Faction.ConvoyGroup group = groups[i];
                if (group == null) continue;
                float cost = group.GetCost();
                if (cost <= 0f || hq.factionFunds < cost) continue;
                if (hq.CmdGetDelaySpawnConvoy((byte)i) > 0f) continue;

                hq.AddFunds(-cost);
                hq.AddConvoy(group);
                logger?.LogInfo("[Events] Convoy " + group.Name + " funded for " +
                                hq.faction.factionName + " at " + cost.ToString("F0") + ".");
                return;
            }
            logger?.LogInfo("[Events] Convoy beat skipped: no group ready and affordable for " +
                            hq.faction.factionName + ".");
        }

        // ---- Client apply -----------------------------------------------------------------

        internal void ApplyRemote(sbyte catalogIndex, int remoteTargetHash, float start, float end)
        {
            if (!Finite(start) || !Finite(end)) return;
            if (catalogIndex < 0)
            {
                if (current != null) Retire();
                return;
            }
            if (catalogIndex >= EventCatalog.Count) return;

            EventDefinition applied = EventCatalog.At(catalogIndex);
            bool changed = currentIndex != catalogIndex;
            if (changed)
            {
                if (current != null) Retire();
                currentIndex = catalogIndex;
            }
            // Retire() clears the target, so the new one lands after it.
            targetHash = remoteTargetHash;
            if (changed && applied != null && applied.Tier == EventTier.Super)
            {
                supersFired++;
                SuperSerial++;
                Announce(applied);
            }
            currentStart = start;
            currentEnd = end;
            current = View(applied, start, end);

            // A client that reconnected mid-event does not know what it already bought.
            if (!GameAccess.IsServer() && queriedIndex != currentIndex)
            {
                queriedIndex = currentIndex;
                network?.QueryState((sbyte)currentIndex);
            }
        }

        /// <summary>
        /// One transient HUD line where the board exists: the pilot should hear about a
        /// superevent without opening the map. Presentation only, client-local, and a
        /// missing board changes nothing.
        /// </summary>
        private void Announce(EventDefinition definition)
        {
            if (!ModServices.TryGet(out IHudBoard board) || settings == null) return;
            board.DeclareChannel("events", "World events");
            string aim = definition.Target == EventTarget.All
                ? "ALL THEATER"
                : NameOfHash(targetHash) ?? "A SIDE";
            float multiplier = EventSelector.EffectiveSupportMultiplier(
                definition.SupportCostMultiplier, settings.EffectStrength.Value);
            board.Notice("events", HudTone.Warning,
                "SUPEREVENT · " + definition.Title.ToUpperInvariant(),
                aim + " · " + EventSelector.EffectSummary(multiplier));
        }

        private static string NameOfHash(int factionHash)
        {
            if (factionHash == 0) return null;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                string name = hq != null && hq.faction != null ? hq.faction.factionName : null;
                if (string.IsNullOrEmpty(name)) continue;
                if (unchecked((int)Deterministic.HashString(name)) == factionHash)
                    return name.ToUpperInvariant();
            }
            return null;
        }

        // ---- View -------------------------------------------------------------------------

        private ActiveEventView View(EventDefinition definition, float start, float end)
        {
            if (definition == null) return null;
            float multiplier = EventSelector.EffectiveSupportMultiplier(
                definition.SupportCostMultiplier, settings != null ? settings.EffectStrength.Value : 1f);
            return new ActiveEventView(
                definition.Id, definition.Title, definition.FlavorText,
                EventCatalog.CategoryLabel(definition.Category),
                EventCatalog.TierLabel(definition.Tier),
                EventCatalog.TargetLabel(definition.Target),
                definition.IsSuper,
                definition.IconKey,
                EventSelector.EffectSummary(multiplier),
                StepsOf(definition), start, end);
        }

        private static IReadOnlyList<ActiveEventStep> StepsOf(EventDefinition definition)
        {
            if (definition.Script.Length == 0) return NoSteps;
            var steps = new ActiveEventStep[definition.Script.Length];
            for (int i = 0; i < steps.Length; i++)
                steps[i] = new ActiveEventStep(definition.Script[i].Label, definition.Script[i].AtSeconds);
            return steps;
        }

        private void SetSignal(string text)
        {
            signal = text;
            signalUntil = Time.unscaledTime + SignalSeconds;
        }

        internal static string ResponseLabel(EventResponseKind kind) =>
            kind == EventResponseKind.Contain ? "CONTAIN" :
            kind == EventResponseKind.Leverage ? "LEVERAGE" : "NONE";

        private static string ResultText(EventResponseResult result)
        {
            switch (result)
            {
                case EventResponseResult.Insufficient: return "RESPONSE DENIED · INSUFFICIENT ALLOCATION";
                case EventResponseResult.NoEffect: return "RESPONSE DENIED · EVENT HAS NO COST EFFECT FOR YOU";
                case EventResponseResult.StaleEvent: return "RESPONSE DENIED · EVENT ALREADY CHANGED";
                case EventResponseResult.NoEvent: return "RESPONSE DENIED · NO ACTIVE EVENT";
                case EventResponseResult.Disabled: return "RESPONSE DENIED · EVENTS DISABLED";
                case EventResponseResult.Busy: return "RESPONSE DENIED · RESPONSE TABLE FULL";
                case EventResponseResult.AlreadyResponded: return "RESPONSE ALREADY ACTIVE";
                default: return "RESPONSE FAILED";
            }
        }

        private static ulong LocalPlayerId() =>
            GameManager.GetLocalPlayer<Player>(out Player player) && player != null
                ? PlayerIdentity.Of(player)
                : PlayerIdentity.None;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
