using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.TheaterOps.Domain
{
    /// <summary>
    /// The pacing knobs of an offensive, clamped once so a bad config can never divide by
    /// zero or freeze a plan. Seconds throughout.
    /// </summary>
    internal readonly struct OffensiveTiming
    {
        public float MusterSeconds { get; }
        public float PlanSeconds { get; }
        public float WaveSeconds { get; }
        public float WaveRetrySeconds { get; }
        public float HoldSeconds { get; }
        public float AssaultSeconds { get; }

        public OffensiveTiming(
            float musterSeconds, float planSeconds,
            float waveSeconds, float waveRetrySeconds, float holdSeconds, float assaultSeconds)
        {
            MusterSeconds = Positive(musterSeconds, 75f);
            PlanSeconds = Positive(planSeconds, 45f);
            WaveSeconds = Positive(waveSeconds, 35f);
            WaveRetrySeconds = Positive(waveRetrySeconds, 8f);
            HoldSeconds = Positive(holdSeconds, 90f);
            AssaultSeconds = Positive(assaultSeconds, 600f);
        }

        private static float Positive(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? fallback : value;
    }

    /// <summary>What one <see cref="OffensivePlan.Tick"/> step decided was due.</summary>
    internal readonly struct OffensiveTick
    {
        /// <summary>The plan reached H-hour and the assault begins.</summary>
        public bool LaunchDue { get; }

        /// <summary>A wave is due (whether or not one can actually be funded is the caller's problem).</summary>
        public bool WaveDue { get; }

        /// <summary>The assault window elapsed; the caller concludes the plan.</summary>
        public bool AssaultExpired { get; }

        public OffensiveTick(bool launchDue, bool waveDue, bool assaultExpired)
        {
            LaunchDue = launchDue;
            WaveDue = waveDue;
            AssaultExpired = assaultExpired;
        }
    }

    /// <summary>
    /// One prepared offensive, as pure state: gather, plan, aim, launch, fight, hold, report.
    ///
    /// <para>The plan owns no game object and asks the host for nothing. It is funded with
    /// an escrowed budget, and each delivered wave draws that budget down; whatever is left
    /// when the plan ends is the caller's to refund. Nothing here selects, spawns or orders
    /// a unit — the plan only says when it is time for the host to act.</para>
    ///
    /// <para>When the last funded wave is on the road the plan does not end: it holds. The
    /// hold is the commander's window to fund another wave, and it expires into an honest
    /// report either way.</para>
    /// </summary>
    internal sealed class OffensivePlan
    {
        internal const int MaximumWaves = 6;
        internal const int MaximumNameLength = 32;
        internal const int MaximumTargetKeyLength = PriorityDirective.MaximumKeyLength;
        internal const int MaximumTargetLabelLength = PriorityDirective.MaximumLabelLength;

        private float waveTimer;
        private float waveSeconds;
        private float sinceConclusion;

        public int Id { get; }
        public string Name { get; }
        public TheaterOperationPhase Phase { get; private set; }
        public TheaterOperationOutcome Outcome { get; private set; }
        public string TargetKey { get; private set; }
        public string TargetLabel { get; private set; }

        /// <summary>Unspent escrow, in millions, still available to the remaining waves.</summary>
        public float Budget { get; private set; }

        /// <summary>Everything taken from the faction pool for this plan, in millions.</summary>
        public float Committed { get; private set; }

        /// <summary>What the delivered waves actually drew, in millions.</summary>
        public float Spent { get; private set; }

        public int WavesPlanned { get; private set; }
        public int WavesLaunched { get; private set; }

        /// <summary>Progress through the current phase, 0..1. Full once the plan is ready.</summary>
        public float Progress { get; private set; }

        /// <summary>Seconds to H-hour while launching; -1 when no countdown is running.</summary>
        public float Countdown { get; private set; }

        /// <summary>Seconds since H-hour, through the assault and the hold; frozen at the conclusion.</summary>
        public float ElapsedSeconds { get; private set; }

        /// <summary>Seconds left in the hold, or -1 while no hold is running.</summary>
        public float HoldRemaining { get; private set; }

        /// <summary>Whether H-hour has passed and the push is (or was) on the road.</summary>
        public bool Launched { get; private set; }

        public OffensivePlan(int id, string name, float setupCost)
        {
            Id = id;
            Name = Bound(name, MaximumNameLength);
            Phase = TheaterOperationPhase.Mustering;
            Countdown = -1f;
            HoldRemaining = -1f;
            WavesPlanned = 1;
            Budget = FinitePositive(setupCost);
            Committed = Budget;
        }

        internal bool IsActive => Phase != TheaterOperationPhase.Concluded;

        internal bool CanCommit => IsActive && WavesPlanned < MaximumWaves;

        internal bool CanTarget => Phase == TheaterOperationPhase.AwaitingTarget;

        internal bool CanAbort => IsActive;

        /// <summary>Every funded wave is on the road; there is nothing left to lift.</summary>
        internal bool AllWavesDelivered => WavesLaunched >= WavesPlanned;

        /// <summary>Whether the countdown has run out and only the launch itself is missing.</summary>
        internal bool IsAtHHour => Phase == TheaterOperationPhase.Launching && Countdown <= 0f;

        /// <summary>Adds one more wave to the plan's strength. The caller has already debited the pool.</summary>
        internal bool Commit(float cost)
        {
            if (!CanCommit || float.IsNaN(cost) || float.IsInfinity(cost) || cost < 0f) return false;
            WavesPlanned++;
            Budget += cost;
            Committed += cost;

            // Funding a push that is holding puts it back on the road: the next tick raises
            // the new wave rather than making the commander wait out another interval.
            if (Phase == TheaterOperationPhase.Holding)
            {
                Phase = TheaterOperationPhase.Assault;
                HoldRemaining = -1f;
                waveTimer = 0f;
            }
            return true;
        }

        /// <summary>
        /// Names the objective. Only legal once the staff has finished planning, so the
        /// target always follows the plan rather than racing it.
        /// </summary>
        internal bool SetTarget(string key, string label, float x, float y, float z, float launchSeconds)
        {
            if (Phase != TheaterOperationPhase.AwaitingTarget) return false;
            if (string.IsNullOrEmpty(key) || !Finite(x) || !Finite(y) || !Finite(z)) return false;

            TargetKey = Bound(key, MaximumTargetKeyLength);
            TargetLabel = Bound(label, MaximumTargetLabelLength);
            Phase = TheaterOperationPhase.Launching;
            Countdown = launchSeconds > 0f && !float.IsInfinity(launchSeconds) ? launchSeconds : 0f;
            return true;
        }

        /// <summary>Brings H-hour forward. The offensive still launches itself; this only ends the wait.</summary>
        internal bool LaunchNow()
        {
            if (Phase != TheaterOperationPhase.Launching || Countdown <= 0f) return false;
            Countdown = 0f;
            return true;
        }

        /// <summary>Advances the plan by <paramref name="delta"/> seconds.</summary>
        internal OffensiveTick Tick(float delta, OffensiveTiming timing)
        {
            if (delta <= 0f || float.IsNaN(delta) || float.IsInfinity(delta)) return default;

            bool launchDue = false;
            bool waveDue = false;
            bool assaultExpired = false;

            switch (Phase)
            {
                case TheaterOperationPhase.Mustering:
                    Progress += delta / timing.MusterSeconds;
                    if (Progress >= 1f)
                    {
                        Progress = 0f;
                        Phase = TheaterOperationPhase.Planning;
                    }
                    break;

                case TheaterOperationPhase.Planning:
                    Progress += delta / timing.PlanSeconds;
                    if (Progress >= 1f)
                    {
                        Progress = 1f;
                        Phase = TheaterOperationPhase.AwaitingTarget;
                    }
                    break;

                case TheaterOperationPhase.AwaitingTarget:
                    break;

                case TheaterOperationPhase.Launching:
                    Countdown -= delta;
                    if (Countdown <= 0f)
                    {
                        Countdown = -1f;
                        Phase = TheaterOperationPhase.Assault;
                        Launched = true;
                        ElapsedSeconds = 0f;
                        Progress = 0f;
                        waveTimer = timing.WaveSeconds;
                        waveSeconds = timing.WaveSeconds;
                        launchDue = true;
                    }
                    break;

                case TheaterOperationPhase.Assault:
                    ElapsedSeconds += delta;
                    if (AllWavesDelivered)
                    {
                        HoldRemaining = timing.HoldSeconds;
                        Phase = TheaterOperationPhase.Holding;
                        Progress = 1f;
                        break;
                    }

                    waveTimer -= delta;
                    if (waveTimer <= 0f)
                    {
                        waveTimer = timing.WaveRetrySeconds;
                        waveDue = true;
                    }
                    Progress = Math.Min(1f, ElapsedSeconds / timing.AssaultSeconds);
                    if (ElapsedSeconds >= timing.AssaultSeconds) assaultExpired = true;
                    break;

                case TheaterOperationPhase.Holding:
                    ElapsedSeconds += delta;
                    HoldRemaining -= delta;
                    if (HoldRemaining <= 0f || ElapsedSeconds >= timing.AssaultSeconds)
                        assaultExpired = true;
                    break;

                case TheaterOperationPhase.Concluded:
                    sinceConclusion += delta;
                    break;
            }

            return new OffensiveTick(launchDue, waveDue, assaultExpired);
        }

        /// <summary>
        /// Records one wave that the host actually put on the road, drawing its cost from the
        /// escrow. Returns false when the plan had no wave left to deliver.
        /// </summary>
        internal bool WaveDelivered(float cost)
        {
            if (WavesLaunched >= WavesPlanned) return false;
            if (Phase != TheaterOperationPhase.Assault && Phase != TheaterOperationPhase.Holding) return false;

            WavesLaunched++;
            waveTimer = waveSeconds;
            if (cost > 0f && !float.IsNaN(cost) && !float.IsInfinity(cost))
            {
                float drawn = Math.Min(cost, Budget);
                Budget -= drawn;
                Spent += drawn;
            }
            return true;
        }

        /// <summary>
        /// Ends the plan and returns the unspent escrow, in millions, for the caller to move
        /// back into the faction pool. A second call is a no-op returning zero.
        /// </summary>
        internal float Conclude(TheaterOperationOutcome outcome)
        {
            if (Phase == TheaterOperationPhase.Concluded) return 0f;

            Phase = TheaterOperationPhase.Concluded;
            Outcome = outcome == TheaterOperationOutcome.None
                ? TheaterOperationOutcome.Cancelled
                : outcome;
            sinceConclusion = 0f;
            Countdown = -1f;
            HoldRemaining = -1f;
            Progress = 1f;

            float refund = Budget;
            Budget = 0f;
            return refund;
        }

        /// <summary>Whether a concluded plan has shown its outcome long enough to be dropped.</summary>
        internal bool ConcludedExpired(float holdSeconds) =>
            Phase == TheaterOperationPhase.Concluded && sinceConclusion >= holdSeconds;

        private static string Bound(string value, int length) =>
            string.IsNullOrEmpty(value) ? value
            : value.Length <= length ? value
            : value.Substring(0, length);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static float FinitePositive(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
    }

    /// <summary>
    /// The local faction's offensive board: at most two plans at once, in preparation order,
    /// with the finished ones dropped so a slot frees itself.
    /// </summary>
    internal sealed class FactionOffensives
    {
        private readonly OffensivePlan[] plans = new OffensivePlan[OffensiveTable.MaximumOperations];
        private int nextId = 1;

        public int Count { get; private set; }

        public OffensivePlan this[int index] =>
            index >= 0 && index < Count ? plans[index] : null;

        public OffensivePlan Find(int id)
        {
            for (int i = 0; i < Count; i++)
                if (plans[i] != null && plans[i].Id == id) return plans[i];
            return null;
        }

        public bool TryPrepare(float setupCost, out OffensivePlan plan)
        {
            plan = null;
            if (Count >= OffensiveTable.MaximumOperations) return false;

            int id = nextId++;
            plan = new OffensivePlan(id, OffensiveNames.At(id - 1), setupCost);
            plans[Count++] = plan;
            return true;
        }

        public bool Remove(int id)
        {
            for (int i = 0; i < Count; i++)
            {
                if (plans[i] == null || plans[i].Id != id) continue;
                for (int j = i; j < Count - 1; j++) plans[j] = plans[j + 1];
                plans[--Count] = null;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Bounded per-faction offensive store, keyed by faction name and capped at the game's
    /// eight factions, so a scene reload or a bad identity can never grow it.
    /// </summary>
    internal sealed class OffensiveTable
    {
        internal const int MaximumFactions = 8;
        internal const int MaximumOperations = 2;
        internal const int MaximumFactionLength = 64;

        private readonly Dictionary<string, FactionOffensives> entries =
            new Dictionary<string, FactionOffensives>(MaximumFactions, StringComparer.Ordinal);

        internal int Count => entries.Count;

        internal bool TryGet(string faction, out FactionOffensives offensives)
        {
            offensives = null;
            return !string.IsNullOrEmpty(faction) && entries.TryGetValue(faction, out offensives);
        }

        /// <summary>Gets the faction's board, creating it only while under the faction ceiling.</summary>
        internal bool TryCreate(string faction, out FactionOffensives offensives)
        {
            offensives = null;
            if (string.IsNullOrEmpty(faction) || faction.Length > MaximumFactionLength) return false;
            if (entries.TryGetValue(faction, out offensives)) return true;
            if (entries.Count >= MaximumFactions) return false;

            offensives = new FactionOffensives();
            entries.Add(faction, offensives);
            return true;
        }

        /// <summary>Drops a faction whose board emptied, so the table tracks live offensives only.</summary>
        internal void Prune(string faction)
        {
            if (string.IsNullOrEmpty(faction)) return;
            if (entries.TryGetValue(faction, out FactionOffensives offensives) && offensives.Count == 0)
                entries.Remove(faction);
        }

        internal void Clear() => entries.Clear();
    }

    /// <summary>
    /// The operation name pool. Names are flavour, but they are stable: the index is derived
    /// from the plan's own id, so the same plan is the same operation on every peer.
    /// </summary>
    internal static class OffensiveNames
    {
        private static readonly string[] Names =
        {
            "STEEL RAIN", "IRON HAMMER", "RED TIDE", "THUNDER RUN",
            "BROKEN SPEAR", "DEEP STRIKE", "TIDAL WAVE", "GRANITE FIST",
        };

        internal static string At(int index)
        {
            int wrapped = index % Names.Length;
            if (wrapped < 0) wrapped += Names.Length;
            return Names[wrapped];
        }
    }
}
