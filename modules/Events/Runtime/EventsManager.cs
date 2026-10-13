using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Events.Configuration;
using BoscaliSummer.Modules.Events.Domain;
using BoscaliSummer.Modules.Events.Networking;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Events.Runtime
{
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
    /// </summary>
    internal sealed class EventsManager : MonoBehaviour, ISceneService, IActiveEventsView
    {
        private const float TickSeconds = 1f;
        private const float HeartbeatSeconds = 15f;
        private const int MaximumFactions = 8;
        private const int MaximumFactionPlayers = 64;

        private static readonly ActiveEventStep[] NoSteps = new ActiveEventStep[0];

        private EventsSettings settings;
        private EventsNet network;

        private readonly List<ActiveEventView> history = new List<ActiveEventView>(16);
        private readonly List<int> recent = new List<int>(EventSelector.RecentWindow);
        private readonly HashSet<int> usedSupers = new HashSet<int>();

        private readonly List<FactionHQ> factions = new List<FactionHQ>(MaximumFactions);
        private readonly List<int> factionBases = new List<int>(MaximumFactions);
        private readonly Dictionary<ulong, int> playerFactions = new Dictionary<ulong, int>(64);

        private ActiveEventView current;
        private object missionIdentity;
        private int missionGeneration;
        private int rotationCounter;

        /// <summary>
        /// One real-random value drawn per process launch. <see cref="missionGeneration"/> and
        /// <see cref="rotationCounter"/> both restart at the same values on every fresh game
        /// process, so without this salt the host's very first rolls of every session hash to
        /// the same seed and the theater opens on the same event every time the game is
        /// relaunched. Folding in a per-process salt keeps the roll itself pure and
        /// reproducible within one host's run while breaking that cross-session repeat; it
        /// never needs to match across peers because only the host ever calls
        /// <see cref="EventDirector.Select"/>.
        /// </summary>
        private int processSalt;
        private int currentIndex = -1;
        private int targetHash;
        private string targetName;
        private FactionHQ targetFaction;
        private int firedSteps;
        private int supersFired;
        private float lastSuperAt = float.NegativeInfinity;
        private float currentStart;
        private float currentEnd;
        private float viewedStrength = float.NaN;
        private float replicatedStrength = 1f;
        private float nextRotation;
        private float nextHeartbeat;
        private PhasedGate tickGate;
        private float nextBalance;
        private float lastMissionTime;
        private bool wasEnabled;
        private TheaterBalance balance = TheaterBalance.Unknown;

        public bool Available { get; private set; }
        public ActiveEventView Current => current;
        public event Action<int, float> MoraleAwarded;
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

        // ---- Pricing (IActiveEventsView) ---------------------------------------------------

        public float SupportCostMultiplier => SupportCostMultiplierFor(LocalPlayerId());

        public bool AffectsFaction(string factionName)
        {
            EventDefinition definition = EventCatalog.At(currentIndex);
            if (definition == null || string.IsNullOrEmpty(factionName)) return false;
            if (definition.Target == EventTarget.All) return true;
            int hash = unchecked((int)Deterministic.HashString(factionName));
            return targetHash != 0 && (hash == 0 ? 1 : hash) == targetHash;
        }

        public string PriceSummaryForFaction(string factionName)
        {
            EventDefinition definition = EventCatalog.At(currentIndex);
            if (definition == null) return "NO EFFECT";
            if (GameManager.GetLocalPlayer<Player>(out Player local) && local?.HQ?.faction != null &&
                string.Equals(local.HQ.faction.factionName, factionName, StringComparison.Ordinal))
                return EventSelector.EffectSummary(SupportCostMultiplier);
            return EventSelector.EffectSummary(AffectsFaction(factionName) ? Effective(definition) : 1f);
        }

        public float SupportCostMultiplierFor(ulong playerId) => BaseMultiplierFor(playerId);

        public float SupportCooldownMultiplierFor(ulong playerId)
        {
            EventDefinition definition = EventCatalog.At(currentIndex);
            if (definition == null || settings == null) return 1f;
            if (definition.Target != EventTarget.All &&
                (playerId == PlayerIdentity.None || !playerFactions.TryGetValue(playerId, out int hash) ||
                 hash == 0 || hash != targetHash)) return 1f;
            return EventSelector.EffectiveSupportMultiplier(definition.SupportCooldownMultiplier,
                GameAccess.IsServer() ? settings.EffectStrength.Value : replicatedStrength);
        }

        internal float LocalSupportCooldownMultiplier => SupportCooldownMultiplierFor(LocalPlayerId());

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

        private float Effective(EventDefinition definition) =>
            EventSelector.EffectiveSupportMultiplier(
                definition.SupportCostMultiplier, GameAccess.IsServer()
                    ? settings.EffectStrength.Value : replicatedStrength);

        // ---- Lifecycle --------------------------------------------------------------------

        public void Configure(EventsSettings configuration, EventsNet transport)
        {
            settings = configuration;
            network = transport;
            wasEnabled = settings.Enabled.Value;
        }

        public void ResetForScene()
        {
            history.Clear();
            recent.Clear();
            usedSupers.Clear();
            factions.Clear();
            factionBases.Clear();
            playerFactions.Clear();
            current = null;
            currentIndex = -1;
            targetHash = 0;
            targetName = null;
            targetFaction = null;
            firedSteps = 0;
            supersFired = 0;
            lastSuperAt = float.NegativeInfinity;
            currentStart = currentEnd = 0f;
            viewedStrength = float.NaN;
            replicatedStrength = 1f;
            nextRotation = nextHeartbeat = nextBalance = 0f;
            tickGate.Reset();
            lastMissionTime = 0f;
            rotationCounter = 0;
            missionIdentity = null;
            balance = TheaterBalance.Unknown;
            Available = false;
            SuperSerial = 0;
            network?.ResetScene();
        }

        private void Awake() => processSalt = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

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
            if (!float.IsFinite(now)) return;
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
            AnnounceBeats(now);

            if (GameAccess.IsServer())
            {
                if (!tickGate.Due(now, TickSeconds, 0.875f)) return;   // 1 Hz slot .875 (table in HighCommandManager); the balance read above runs first in every Update, so it precedes the first roll
                TickHost(now);
            }
            else if (current != null && now >= currentEnd)
            {
                // A missed end broadcast still retires locally at the stamped time.
                Retire();
            }
        }

        // ---- Superevent beats on the ticker ------------------------------------------------

        private string beatsEvent;
        private float beatsStart;
        private int beatsAnnounced;

        /// <summary>
        /// Each timed beat of a superevent's script lands on the board's ticker as it comes due ("SUPEREVENT · TITLE" over the beat's label), on every peer from the event the
        /// host broadcast: the pilot sees the second and third act, not only the opening alert. A beat already past when this peer first sees the event is not replayed.
        /// Presentation only, client-local.
        /// </summary>
        private void AnnounceBeats(float now)
        {
            ActiveEventView view = current;
            if (view == null || !view.IsSuper || view.Steps.Count == 0) { beatsEvent = null; return; }
            if (beatsEvent != view.Id || beatsStart != view.StartedAtMissionTime)
            {
                beatsEvent = view.Id;
                beatsStart = view.StartedAtMissionTime;
                beatsAnnounced = 0;
                while (beatsAnnounced < view.Steps.Count && now >= beatsStart + view.Steps[beatsAnnounced].AtSeconds) beatsAnnounced++;
            }
            if (beatsAnnounced >= view.Steps.Count || now < beatsStart + view.Steps[beatsAnnounced].AtSeconds) return;
            ActiveEventStep step = view.Steps[beatsAnnounced++];
            if (ModuleServices.TryGet(out IHudBoard board) && !string.IsNullOrEmpty(step.Label))
                board.Notice("events", HudTone.Caution, "SUPEREVENT · " + view.Title.ToUpperInvariant(), step.Label.ToUpperInvariant());
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
            if (Mathf.Abs(settings.EffectStrength.Value - viewedStrength) > 0.0001f)
            {
                current = View(EventCatalog.At(currentIndex), currentStart, currentEnd);
                network?.Broadcast((sbyte)currentIndex, targetHash, currentStart, currentEnd,
                    settings.EffectStrength.Value);
                nextHeartbeat = now + HeartbeatSeconds;
            }
            RunScript(now);
            if (now >= currentEnd)
            {
                Retire();
                nextRotation = now + RollGap();
                network?.Broadcast(-1, 0, 0f, 0f, settings.EffectStrength.Value);
                return;
            }
            if (now >= nextHeartbeat)
            {
                // Cheap resend: covers a late joiner and a dropped broadcast alike.
                nextHeartbeat = now + HeartbeatSeconds;
                network?.Broadcast((sbyte)currentIndex, targetHash, currentStart, currentEnd,
                    settings.EffectStrength.Value);
            }
        }

        private void StartNext(float now)
        {
            uint seed = Deterministic.Hash(missionGeneration, ++rotationCounter, 0x4556, processSalt);
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

            Plugin.Logger?.LogInfo("[Events] " + definition.Title + " (" + EventCatalog.TierLabel(definition.Tier) +
                            ", " + EventCatalog.TargetLabel(definition.Target) + ") active for " + duration +
                            "s (" + current.EffectSummary + ").");
            network?.Broadcast((sbyte)index, targetHash, currentStart, currentEnd,
                settings.EffectStrength.Value);
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
                Deterministic.Hash(missionGeneration, ++rotationCounter, 0x4741, processSalt), min, max);
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
            firedSteps = 0;
            targetHash = 0;
            targetName = null;
            targetFaction = null;
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
                Plugin.Logger?.LogInfo("[Events] " + definition.Title + " beat: " + step.Label + " (all factions).");
                return;
            }

            if (targetFaction == null) return;
            Apply(step, targetFaction, strength);
            Plugin.Logger?.LogInfo("[Events] " + definition.Title + " beat: " + step.Label + " (" +
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
                    if (amount < 0f)
                        amount = -Mathf.Min(-amount, Mathf.Max(0f, hq.factionFunds));
                    if (Mathf.Abs(amount) <= 0.001f) return;
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
                    FundConvoy(hq, step.Amount * strength);
                    break;
                case EventEffect.Morale:
                    MoraleAwarded?.Invoke(hq.GetInstanceID(), step.Amount * strength);
                    break;
            }
        }

        /// <summary>
        /// Queues the cheapest ready vanilla group. The beat may cover a bounded shortfall,
        /// so an otherwise empty treasury does not turn the event's battlefield move into
        /// a silent no-op. Nothing is spawned here; the game deploys the group itself.
        /// </summary>
        private void FundConvoy(FactionHQ hq, float subsidy)
        {
            if (hq == null || hq.faction == null) return;
            // The pool has one door (the reinforcement desk's), so every convoy it pays is priced, gated and written to the staff log.
            if (!ModuleServices.TryGet(out IConvoyPurchase purchase))
            {
                Plugin.Logger?.LogInfo("[Events] Convoy beat skipped: no reinforcement desk.");
                return;
            }
            if (purchase.TryBuyCheapest(hq.GetInstanceID(), subsidy, "EVENT", out string group, out int cost, out string why))
                Plugin.Logger?.LogInfo("[Events] Convoy " + group + " queued for " + hq.faction.factionName + " at " + cost + ".");
            else
                Plugin.Logger?.LogInfo("[Events] Convoy beat skipped for " + hq.faction.factionName + ": " + why + ".");
        }

        // ---- Client apply -----------------------------------------------------------------

        internal void ApplyRemote(sbyte catalogIndex, int remoteTargetHash, float start, float end,
            float effectStrength)
        {
            if (!float.IsFinite(start) || !float.IsFinite(end) || !float.IsFinite(effectStrength) ||
                effectStrength < 0f || effectStrength > 2f) return;
            replicatedStrength = effectStrength;
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
        }

        /// <summary>
        /// One transient HUD line where the board exists: the pilot should hear about a
        /// superevent without opening the map. Presentation only, client-local, and a
        /// missing board changes nothing.
        /// </summary>
        private void Announce(EventDefinition definition)
        {
            if (!ModuleServices.TryGet(out IHudBoard board) || settings == null) return;
            string aim = definition.Target == EventTarget.All
                ? "ALL THEATER"
                : NameOfHash(targetHash) ?? "A SIDE";
            float multiplier = Effective(definition);
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
            viewedStrength = GameAccess.IsServer() ? settings.EffectStrength.Value : replicatedStrength;
            float multiplier = Effective(definition);
            return new ActiveEventView(
                definition.Id, definition.Title, definition.FlavorText,
                EventCatalog.CategoryLabel(definition.Category),
                EventCatalog.TierLabel(definition.Tier),
                EventCatalog.TargetLabel(definition.Target),
                definition.IsSuper,
                definition.IconKey,
                EventSelector.EffectSummary(multiplier),
                StepsOf(definition), start, end,
                definition.Target == EventTarget.All || targetHash != 0,
                TempoSummary(EventSelector.EffectiveSupportMultiplier(definition.SupportCooldownMultiplier,
                    GameAccess.IsServer() ? settings.EffectStrength.Value : replicatedStrength)));
        }

        private static string TempoSummary(float multiplier)
        {
            int percent = Mathf.RoundToInt((multiplier - 1f) * 100f);
            return percent == 0 ? "" : "SUPPORT RESET " + (percent > 0 ? "+" : "") + percent + "%";
        }

        private static IReadOnlyList<ActiveEventStep> StepsOf(EventDefinition definition)
        {
            if (definition.Script.Length == 0) return NoSteps;
            var steps = new ActiveEventStep[definition.Script.Length];
            for (int i = 0; i < steps.Length; i++)
                steps[i] = new ActiveEventStep(definition.Script[i].Label, definition.Script[i].AtSeconds);
            return steps;
        }

        private static ulong LocalPlayerId() =>
            GameManager.GetLocalPlayer<Player>(out Player player) && player != null
                ? PlayerIdentity.Of(player)
                : PlayerIdentity.None;
    }
}
