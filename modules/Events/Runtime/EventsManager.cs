using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Events.Configuration;
using BoscaliSummer.Features.Events.Domain;
using BoscaliSummer.Features.Events.Networking;
using BoscaliSummer.Framework.Contracts;
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

        private EventsSettings settings;
        private EventsNet network;
        private ManualLogSource logger;

        private readonly List<ActiveEventView> history = new List<ActiveEventView>(16);
        private readonly List<int> recent = new List<int>(EventSelector.RecentWindow);
        private readonly Dictionary<ulong, byte> responses = new Dictionary<ulong, byte>(16);

        private ActiveEventView current;
        private object missionIdentity;
        private int missionGeneration;
        private int rotationCounter;
        private int currentIndex = -1;
        private int queriedIndex = -1;
        private float currentStart;
        private float currentEnd;
        private float nextRotation;
        private float nextHeartbeat;
        private float nextTick;
        private bool wasEnabled;
        private string signal = "";
        private float signalUntil;

        public bool Available { get; private set; }
        public ActiveEventView Current => current;
        public IReadOnlyList<ActiveEventView> History => history;

        /// <summary>Transient feedback line for the panel's status strip.</summary>
        internal string Signal => Time.unscaledTime < signalUntil ? signal : "";

        // ---- Pricing (IActiveEventsView) ---------------------------------------------------

        public float SupportCostMultiplier => SupportCostMultiplierFor(LocalPlayerId());

        public float SupportCostMultiplierFor(ulong playerId)
        {
            float multiplier = BaseMultiplier();
            if (playerId != PlayerIdentity.None && responses.TryGetValue(playerId, out byte kind))
                multiplier = EventSelector.ApplyResponse(multiplier, (EventResponseKind)kind);
            return multiplier;
        }

        // ---- Response state ----------------------------------------------------------------

        /// <summary>Allocation this player would pay to respond to the active event; 0 = none.</summary>
        internal int ResponseCost => currentIndex < 0 ? 0 : EventSelector.ResponseCost(BaseMultiplier());

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
            EventSelector.ResponseKind(BaseMultiplier()) != EventResponseKind.None;

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

            float baseMultiplier = BaseMultiplier();
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
            current = null;
            currentIndex = -1;
            queriedIndex = -1;
            currentStart = currentEnd = 0f;
            nextRotation = nextHeartbeat = nextTick = 0f;
            rotationCounter = 0;
            missionIdentity = null;
            Available = false;
            signal = "";
            signalUntil = 0f;
            network?.ResetScene();
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (settings == null) return;
            if (!settings.Enabled.Value)
            {
                if (wasEnabled) ResetForScene();
                wasEnabled = false;
                return;
            }
            wasEnabled = true;

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
            Available = true;

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

        // ---- Host rotation ----------------------------------------------------------------

        private void TickHost(float now)
        {
            if (current == null)
            {
                if (now < nextRotation) return;
                StartNext(now);
                return;
            }
            if (now >= currentEnd)
            {
                Retire();
                nextRotation = now + RollGap();
                network?.Broadcast(-1, 0f, 0f);
                return;
            }
            if (now >= nextHeartbeat)
            {
                // Cheap resend: covers a late joiner and a dropped broadcast alike.
                nextHeartbeat = now + HeartbeatSeconds;
                network?.Broadcast((sbyte)currentIndex, currentStart, currentEnd);
            }
        }

        private void StartNext(float now)
        {
            uint seed = Deterministic.Hash(missionGeneration, ++rotationCounter, 0x4556);
            int index = EventSelector.SelectIndex(seed, EventCatalog.Count, recent);
            EventDefinition definition = EventCatalog.At(index);
            int duration = EventSelector.RollDuration(
                Deterministic.Hash(unchecked((int)seed), 0x4455, 0x5555),
                definition.DurationMinSeconds, definition.DurationMaxSeconds);

            currentIndex = index;
            currentStart = now;
            currentEnd = now + duration;
            current = View(definition, currentStart, currentEnd);
            recent.Add(index);
            while (recent.Count > EventSelector.RecentWindow) recent.RemoveAt(0);
            nextHeartbeat = now + HeartbeatSeconds;

            logger?.LogInfo("[Events] " + definition.Title + " active for " + duration + "s (" +
                            current.EffectSummary + ").");
            network?.Broadcast((sbyte)index, currentStart, currentEnd);
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
            // Responses buy out the rest of one event's run, not the next one's.
            responses.Clear();
        }

        // ---- Client apply -----------------------------------------------------------------

        internal void ApplyRemote(sbyte catalogIndex, float start, float end)
        {
            if (!Finite(start) || !Finite(end)) return;
            if (catalogIndex < 0)
            {
                if (current != null) Retire();
                return;
            }
            if (catalogIndex >= EventCatalog.Count) return;

            if (currentIndex != catalogIndex)
            {
                if (current != null) Retire();
                currentIndex = catalogIndex;
            }
            currentStart = start;
            currentEnd = end;
            current = View(EventCatalog.At(catalogIndex), start, end);

            // A client that reconnected mid-event does not know what it already bought.
            if (!GameAccess.IsServer() && queriedIndex != currentIndex)
            {
                queriedIndex = currentIndex;
                network?.QueryState((sbyte)currentIndex);
            }
        }

        // ---- View -------------------------------------------------------------------------

        private float BaseMultiplier()
        {
            EventDefinition definition = EventCatalog.At(currentIndex);
            return definition == null || settings == null ? 1f
                : EventSelector.EffectiveSupportMultiplier(
                    definition.SupportCostMultiplier, settings.EffectStrength.Value);
        }

        private ActiveEventView View(EventDefinition definition, float start, float end)
        {
            float multiplier = EventSelector.EffectiveSupportMultiplier(
                definition.SupportCostMultiplier, settings.EffectStrength.Value);
            return new ActiveEventView(
                definition.Id, definition.Title, definition.FlavorText,
                EventCatalog.CategoryLabel(definition.Category), definition.IconKey,
                EventSelector.EffectSummary(multiplier), start, end);
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
                case EventResponseResult.NoEffect: return "RESPONSE DENIED · EVENT HAS NO COST EFFECT";
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
