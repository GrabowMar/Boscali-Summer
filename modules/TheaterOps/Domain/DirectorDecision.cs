using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.TheaterOps.Domain
{
    /// <summary>
    /// One objective as the director senses it: both sides' ground presence near it. No
    /// custody flag — custody is the sign of friendly minus hostile, read off the same
    /// numbers that size the attack, so the assessment cannot disagree with itself.
    /// </summary>
    internal readonly struct ObjectiveRead
    {
        public string Key { get; }
        public string Label { get; }
        public int Hostile { get; }
        public int Friendly { get; }
        public int HostileFieldworks { get; }
        public int FriendlyFieldworks { get; }
        public int SuppressedFieldworks { get; }

        public ObjectiveRead(string key, string label, int hostile, int friendly,
            int hostileFieldworks = 0, int friendlyFieldworks = 0, int suppressedFieldworks = 0)
        {
            Key = key;
            Label = label;
            Hostile = hostile < 0 ? 0 : hostile;
            Friendly = friendly < 0 ? 0 : friendly;
            HostileFieldworks = Math.Max(0, hostileFieldworks);
            FriendlyFieldworks = Math.Max(0, friendlyFieldworks);
            SuppressedFieldworks = Math.Max(0, suppressedFieldworks);
        }
    }

    /// <summary>Everything one strategic review decides from. Built by the service, judged pure.</summary>
    internal readonly struct DirectorAssessment
    {
        public IReadOnlyList<ObjectiveRead> Objectives { get; }
        public InfluenceState Influence { get; }
        public float Funds { get; }
        public float OverheadCost { get; }
        public float WaveBudget { get; }
        public int ActiveOffensives { get; }
        public int MaxOffensives { get; }
        public string CommittedTarget { get; }
        public int ReviewsHeld { get; }
        public string LaunchedTarget { get; }
        public string CurrentEffortKey { get; }
        public IReadOnlyList<string> RunningTargets { get; }

        public DirectorAssessment(
            IReadOnlyList<ObjectiveRead> objectives, InfluenceState influence, float funds,
            float overheadCost, float waveBudget, int activeOffensives, int maxOffensives,
            string committedTarget, int reviewsHeld, string launchedTarget, string currentEffortKey,
            IReadOnlyList<string> runningTargets)
        {
            Objectives = objectives;
            Influence = influence;
            Funds = funds;
            OverheadCost = overheadCost;
            WaveBudget = waveBudget;
            ActiveOffensives = activeOffensives;
            MaxOffensives = maxOffensives;
            CommittedTarget = committedTarget;
            ReviewsHeld = reviewsHeld;
            LaunchedTarget = launchedTarget;
            CurrentEffortKey = currentEffortKey;
            RunningTargets = runningTargets;
        }
    }

    /// <summary>One review's orders: what opens, where the effort points, and what the staff says.</summary>
    internal readonly struct DirectorOrders
    {
        public bool OpenOffensive { get; }
        public string TargetKey { get; }
        public int Waves { get; }
        public string EffortKey { get; }
        public bool ClearEffort { get; }
        public bool EffortIsDefense { get; }
        public string DefenseKey { get; }
        public string DefenseLabel { get; }

        /// <summary>
        /// The staff's preferred target after hysteresis, whether or not it opens there.
        /// The service tracks this as the commitment so a review that cannot open still
        /// holds its aim for the next one.
        /// </summary>
        public string PreferredKey { get; }
        public string[] Log { get; }

        public DirectorOrders(
            bool openOffensive, string targetKey, int waves,
            string effortKey, bool clearEffort, bool effortIsDefense,
            string defenseKey, string defenseLabel, string preferredKey, string[] log)
        {
            OpenOffensive = openOffensive;
            TargetKey = targetKey;
            Waves = waves;
            EffortKey = effortKey;
            ClearEffort = clearEffort;
            EffortIsDefense = effortIsDefense;
            DefenseKey = defenseKey;
            DefenseLabel = defenseLabel;
            PreferredKey = preferredKey;
            Log = log;
        }
    }

    /// <summary>
    /// The staff's judgment, kept pure: which objective is worth an offensive and how many
    /// waves it deserves, which held objective needs guarding, and which of the two owns
    /// the faction's single main effort. The service senses the theater and spends the
    /// pool; everything it spends was decided here, so the tests can disagree with the
    /// judgment without a game install.
    /// </summary>
    internal static class DirectorDecision
    {
        /// <summary>Hostile ground near a held objective that makes it a defense, not a watch.</summary>
        internal const int DefenseThreatMinimum = 3;

        /// <summary>Reviews a committed target survives a better offer. Without this the staff
        /// re-aims every review and no plan ever reaches H-hour.</summary>
        internal const int CommitReviews = 2;

        /// <summary>Threat count that reads as a full-strength raid for effort arbitration.</summary>
        private const float FullThreat = 8f;

        private static bool IsRunning(DirectorAssessment assessment, string key)
        {
            IReadOnlyList<string> running = assessment.RunningTargets;
            if (running == null) return false;
            for (int i = 0; i < running.Count; i++)
                if (string.Equals(running[i], key, StringComparison.Ordinal)) return true;
            return false;
        }

        private static string EffortLabel(IReadOnlyList<ObjectiveRead> objectives, string key)
        {
            if (objectives != null)
                for (int i = 0; i < objectives.Count; i++)
                    if (string.Equals(objectives[i].Key, key, StringComparison.Ordinal) &&
                        !string.IsNullOrEmpty(objectives[i].Label))
                        return objectives[i].Label;
            return key;
        }

        /// <summary>
        /// Waves an attack deserves for the observed defense: one plus one per three hostile,
        /// inside the plan's own ceiling. Pure, so the sizing the board shows is the sizing
        /// the tests pinned.
        /// </summary>
        internal static int SizeWaves(int observedDefense)
        {
            if (observedDefense < 0) observedDefense = 0;
            int waves = 1 + observedDefense / 3;
            if (waves < 1) return 1;
            if (waves > OffensivePlan.MaximumWaves) return OffensivePlan.MaximumWaves;
            return waves;
        }

        internal static DirectorOrders Review(DirectorAssessment assessment)
        {
            var log = new List<string>(3);
            IReadOnlyList<ObjectiveRead> objectives = assessment.Objectives;
            if (objectives == null || objectives.Count == 0)
                return new DirectorOrders(
                    false, null, 0, null, false, false, null, null, null, log.ToArray());

            InfluenceState influence = assessment.Influence;
            float stance = influence != null ? influence.Stance : InfluenceState.DefaultStance;
            bool hold = influence != null && influence.HoldOffense;
            float reserve = influence != null ? influence.ReserveFloor : 0f;
            float maxEscrow = influence != null ? influence.MaxEscrowPerPlan : InfluenceState.DefaultMaxEscrow;

            string bestKey = null;
            string bestLabel = null;
            int bestHostile = 0;
            int bestFieldworks = 0;
            float bestScore = 0f;
            string committedKey = assessment.CommittedTarget;
            bool committedValid = false;

            for (int i = 0; i < objectives.Count; i++)
            {
                ObjectiveRead objective = objectives[i];
                if (string.IsNullOrEmpty(objective.Key)) continue;
                // Fully held ground is not a target: there is nothing there to attack.
                // Ground already under an offensive is not a second one. Neither can hold
                // a commitment: a promise to attack held or running ground is the staff
                // arguing with a map it already drew on.
                int resistance = objective.Hostile + objective.HostileFieldworks * 2;
                if ((resistance == 0 && objective.Friendly + objective.FriendlyFieldworks > 0) ||
                    IsRunning(assessment, objective.Key))
                    continue;
                float axis = influence != null ? influence.WeightOf(objective.Key) : 0f;
                float score = (1f / (1f + resistance)) * (0.5f + stance) * (1f + axis);
                if (string.Equals(objective.Key, committedKey, StringComparison.Ordinal))
                    committedValid = true;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestKey = objective.Key;
                    bestLabel = objective.Label;
                    bestHostile = resistance;
                    bestFieldworks = objective.HostileFieldworks;
                }
            }

            // The committed target survives better offers until its reviews run out.
            if (!string.IsNullOrEmpty(committedKey) && !string.Equals(bestKey, committedKey, StringComparison.Ordinal) &&
                committedValid && assessment.ReviewsHeld < CommitReviews)
            {
                for (int i = 0; i < objectives.Count; i++)
                {
                    if (!string.Equals(objectives[i].Key, committedKey, StringComparison.Ordinal)) continue;
                    bestKey = committedKey;
                    bestLabel = objectives[i].Label;
                    bestHostile = objectives[i].Hostile + objectives[i].HostileFieldworks * 2;
                    bestFieldworks = objectives[i].HostileFieldworks;
                    break;
                }
            }

            string defenseKey = null;
            string defenseLabel = null;
            int defenseThreat = 0;
            for (int i = 0; i < objectives.Count; i++)
            {
                ObjectiveRead objective = objectives[i];
                if (string.IsNullOrEmpty(objective.Key)) continue;
                int defenders = objective.Friendly + objective.FriendlyFieldworks;
                int threat = objective.Hostile + Math.Min(3, objective.SuppressedFieldworks);
                if (defenders == 0 ||
                    (threat < defenders && objective.SuppressedFieldworks < DefenseThreatMinimum)) continue;
                if (threat > defenseThreat)
                {
                    defenseThreat = threat;
                    defenseKey = objective.Key;
                    defenseLabel = objective.Label;
                }
            }
            bool defending = !string.IsNullOrEmpty(defenseKey) && defenseThreat >= DefenseThreatMinimum;
            if (!defending)
            {
                defenseKey = null;
                defenseLabel = null;
            }

            float funds = float.IsNaN(assessment.Funds) || float.IsInfinity(assessment.Funds)
                ? 0f : assessment.Funds;
            float opening = Math.Max(0f, assessment.OverheadCost) + Math.Max(0f, assessment.WaveBudget);
            bool affordable = funds - reserve >= opening && assessment.WaveBudget > 0f;
            bool chestFits = maxEscrow >= opening;
            bool room = assessment.ActiveOffensives < assessment.MaxOffensives;

            bool open = !string.IsNullOrEmpty(bestKey) && affordable && chestFits && !hold && room;
            int waves = open ? SizeWaves(bestHostile) : 0;

            // One effort, two claimants. Defense wins on threat at low stance, offense on
            // opportunity at high stance; a tie keeps the current order rather than flapping.
            string effortKey = null;
            bool effortIsDefense = false;
            bool wantsEffort = false;
            if (defending || !string.IsNullOrEmpty(assessment.LaunchedTarget))
            {
                float threatNorm = defenseThreat / FullThreat;
                if (threatNorm > 1f) threatNorm = 1f;
                float defenseScore = defending ? threatNorm * (1.2f - stance) * 1.5f : 0f;
                float offenseScore = !string.IsNullOrEmpty(assessment.LaunchedTarget)
                    ? Math.Max(0.2f, bestScore) * (0.4f + stance) : 0f;
                if (defenseScore > offenseScore && defending)
                {
                    effortKey = defenseKey;
                    effortIsDefense = true;
                    wantsEffort = true;
                }
                else if (offenseScore > defenseScore && !string.IsNullOrEmpty(assessment.LaunchedTarget))
                {
                    effortKey = assessment.LaunchedTarget;
                    wantsEffort = true;
                }
                else if (!string.IsNullOrEmpty(assessment.CurrentEffortKey))
                {
                    effortKey = assessment.CurrentEffortKey;
                    effortIsDefense = string.Equals(effortKey, defenseKey, StringComparison.Ordinal);
                    wantsEffort = true;
                }
            }
            bool clearEffort = !wantsEffort && !string.IsNullOrEmpty(assessment.CurrentEffortKey);

            if (open)
            {
                log.Add("OPENING " + (bestLabel ?? bestKey).ToUpperInvariant() + ": " + waves +
                        (waves == 1 ? " WAVE" : " WAVES"));
                if (bestFieldworks > 0)
                    log.Add("FORTIFIED APPROACH — " + bestFieldworks + " OBSERVED DEFENDERS");
            }
            else if (!string.IsNullOrEmpty(bestKey) && (hold || !affordable || !chestFits || !room))
                log.Add("HOLDING OFFENSE — " + (hold ? "STANDING ORDER"
                    : !affordable ? "WAR CHEST" : !chestFits ? "CHEST CAP" : "STAFF AT CAPACITY"));
            if (defending)
                log.Add("DEFENDING " + (defenseLabel ?? defenseKey).ToUpperInvariant());
            if (wantsEffort && !string.Equals(effortKey, assessment.CurrentEffortKey, StringComparison.Ordinal))
                log.Add("EFFORT → " + EffortLabel(objectives, effortKey).ToUpperInvariant());
            else if (clearEffort)
                log.Add("EFFORT RELEASED");

            return new DirectorOrders(
                open, open ? bestKey : null, waves,
                wantsEffort ? effortKey : null, clearEffort, effortIsDefense,
                defenseKey, defenseLabel, bestKey, log.ToArray());
        }
    }
}
