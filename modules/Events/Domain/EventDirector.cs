using System.Collections.Generic;
using BoscaliSummer.Core;

namespace BoscaliSummer.Features.Events.Domain
{
    /// <summary>
    /// Ground ownership read from live airbase custody: how many bases each side holds, who
    /// leads, and by how much. Counts only ground airbases; a carrier holds no territory.
    /// </summary>
    internal readonly struct TheaterBalance
    {
        public static readonly TheaterBalance Unknown = new TheaterBalance(0, 0, 0, 0, 0, 0);

        public int FactionCount { get; }
        public int LeaderBases { get; }
        public int LoserBases { get; }
        public int LeaderHash { get; }
        public int LoserHash { get; }
        public int NeutralBases { get; }

        public TheaterBalance(int factionCount, int leaderBases, int loserBases,
            int leaderHash, int loserHash, int neutralBases)
        {
            FactionCount = factionCount;
            LeaderBases = leaderBases;
            LoserBases = loserBases;
            LeaderHash = leaderHash;
            LoserHash = loserHash;
            NeutralBases = neutralBases;
        }

        /// <summary>True once any faction holds ground; the readout says unobserved otherwise.</summary>
        public bool Known => FactionCount > 0;

        public int Deficit => LeaderBases > LoserBases ? LeaderBases - LoserBases : 0;

        /// <summary>Two sides on the map and one of them clearly ahead.</summary>
        public bool Contested => FactionCount >= 2 && Deficit > 0;

        public int HashFor(EventTarget target)
        {
            switch (target)
            {
                case EventTarget.Losing: return LoserHash;
                case EventTarget.Leading: return LeaderHash;
                default: return 0;
            }
        }
    }

    /// <summary>The director's read of the mission when it rolls.</summary>
    internal readonly struct DirectorState
    {
        public float MissionTime { get; }
        public int SupersFired { get; }
        public float LastSuperAtMissionTime { get; }
        public TheaterBalance Balance { get; }

        /// <summary>
        /// The mission's first roll. The theater should open on something that actually
        /// moves a price, not on weather, so flavor-only entries are held back.
        /// </summary>
        public bool OpeningRoll { get; }

        public DirectorState(float missionTime, int supersFired, float lastSuperAtMissionTime,
            TheaterBalance balance, bool openingRoll = false)
        {
            MissionTime = missionTime;
            SupersFired = supersFired;
            LastSuperAtMissionTime = lastSuperAtMissionTime;
            Balance = balance;
            OpeningRoll = openingRoll;
        }
    }

    /// <summary>
    /// Pure directorship: which tier rolls next and which catalog entry within it. The host
    /// seeds this from the mission generation and the rotation counter, so a listen host
    /// reruns the same mission texture deterministically.
    ///
    /// <para>The director leans on the theater: a clear ground deficit both raises the odds
    /// of an intervention and gates the targeted supers, so aid and overstretch only land
    /// when there is a story to tell. Consequences stay inside the catalog's real seams —
    /// support prices, faction funds, per-player allocation and the vanilla convoy queue.</para>
    /// </summary>
    internal static class EventDirector
    {
        /// <summary>Supers per mission. The ceiling is what keeps them feeling like news.</summary>
        public const int MaximumSupers = 3;

        /// <summary>No superevent in the opening minutes; the situation has to exist first.</summary>
        public const float SuperMinimumMissionTime = 180f;

        /// <summary>Quiet between two supers, measured from the previous super's start.</summary>
        public const float SuperCooldownSeconds = 300f;

        /// <summary>Bases of deficit a side needs before the targeted supers unlock.</summary>
        public const int AidDeficitThreshold = 2;

        /// <summary>Baseline odds a roll escalates while a super is eligible.</summary>
        public const float BaseSuperChance = 0.3f;

        /// <summary>Odds once one side is losing ground; the theater is asking for a story.</summary>
        public const float EscalatedSuperChance = 0.55f;

        /// <summary>Share of ordinary rolls that stay minor instead of medium.</summary>
        public const float MinorChance = 0.45f;

        public static bool SuperEligible(in DirectorState state) =>
            state.SupersFired < MaximumSupers &&
            state.MissionTime >= SuperMinimumMissionTime &&
            state.MissionTime - state.LastSuperAtMissionTime >= SuperCooldownSeconds;

        /// <summary>Whether a super's target can be resolved right now.</summary>
        public static bool Fits(EventDefinition definition, in TheaterBalance balance)
        {
            if (definition == null) return false;
            if (definition.Target == EventTarget.All) return true;
            return balance.Contested && balance.Deficit >= AidDeficitThreshold;
        }



        /// <summary>
        /// The next catalog index. Escalates to a super when one is eligible and the roll
        /// lands, otherwise rolls minor or medium. Never returns a recent entry, and never
        /// returns a super already used this mission. Throws only on an empty catalog.
        /// </summary>
        public static int Select(uint seed, EventDefinition[] catalog, IReadOnlyList<int> recent,
            ISet<int> usedSupers, bool allowSupers, in DirectorState state)
        {
            if (catalog == null || catalog.Length == 0) return 0;

            // The opening roll is ordinary on purpose, even for a late first roll.
            if (allowSupers && !state.OpeningRoll && SuperEligible(state))
            {
                float chance = state.Balance.Contested && state.Balance.Deficit >= AidDeficitThreshold
                    ? EscalatedSuperChance
                    : BaseSuperChance;
                float roll = Deterministic.UnitFloat(Deterministic.Hash(unchecked((int)seed), 0x4557, 0x5375));
                if (roll < chance)
                {
                    // A super never repeats an entry still inside the recent window: with
                    // nothing fresh to escalate to, the roll falls through to an ordinary
                    // event instead of replaying last minute's news.
                    int superIndex = DrawWithinTier(
                        seed, catalog, EventTier.Super, recent, usedSupers, state.Balance,
                        allowRepeat: false);
                    if (superIndex >= 0) return superIndex;
                }
            }

            // The mission opens on a real price move: a flavor-only first event is a whole
            // minute of "nothing happened" for a system whose point is that things happen.
            float tierRoll = Deterministic.UnitFloat(
                Deterministic.Hash(unchecked((int)seed), 0x7A31, 0x4556));
            EventTier tier = state.OpeningRoll || tierRoll >= MinorChance
                ? EventTier.Medium
                : EventTier.Minor;
            int index = DrawWithinTier(seed, catalog, tier, recent, null, state.Balance, allowRepeat: true);
            if (index < 0) index = DrawWithinTier(seed, catalog, EventTier.Medium, recent, null, state.Balance, allowRepeat: true);
            if (index < 0) index = DrawWithinTier(seed, catalog, EventTier.Minor, recent, null, state.Balance, allowRepeat: true);
            if (index < 0) index = EventSelector.SelectIndex(seed, catalog.Length, recent);
            return index;
        }

        /// <summary>
        /// One draw inside a tier. Bounded passes over the catalog, no allocation. Recent
        /// entries are excluded while any fresh candidate fits; only a pool smaller than the
        /// recent window falls back to a repeat, which is the best-effort rule the plain
        /// selector already documents.
        /// </summary>
        private static int DrawWithinTier(uint seed, EventDefinition[] catalog, EventTier tier,
            IReadOnlyList<int> recent, ISet<int> excluded, in TheaterBalance balance,
            bool allowRepeat)
        {
            uint hash = Deterministic.Hash(
                unchecked((int)seed), (int)tier * 131 + 0x4556, 0x5375);

            int fresh = Count(catalog, tier, excluded, recent, balance);
            if (fresh > 0)
                return Ordinal(catalog, tier, excluded, recent, balance, (int)(hash % (uint)fresh));
            if (!allowRepeat) return -1;

            int any = Count(catalog, tier, excluded, null, balance);
            if (any == 0) return -1;
            return Ordinal(catalog, tier, excluded, null, balance, (int)(hash % (uint)any));
        }

        private static int Count(EventDefinition[] catalog, EventTier tier, ISet<int> excluded,
            IReadOnlyList<int> skip, in TheaterBalance balance)
        {
            int count = 0;
            for (int i = 0; i < catalog.Length; i++)
            {
                if (!Usable(catalog[i], tier, excluded, skip, i, balance)) continue;
                count++;
            }
            return count;
        }

        private static int Ordinal(EventDefinition[] catalog, EventTier tier, ISet<int> excluded,
            IReadOnlyList<int> skip, in TheaterBalance balance, int ordinal)
        {
            int seen = 0;
            for (int i = 0; i < catalog.Length; i++)
            {
                if (!Usable(catalog[i], tier, excluded, skip, i, balance)) continue;
                if (seen == ordinal) return i;
                seen++;
            }
            return -1;
        }

        private static bool Usable(EventDefinition definition, EventTier tier, ISet<int> excluded,
            IReadOnlyList<int> skip, int index, in TheaterBalance balance)
        {
            if (definition == null || definition.Tier != tier) return false;
            if (!Fits(definition, balance)) return false;
            if (excluded != null && excluded.Contains(index)) return false;
            if (skip == null) return true;
            for (int i = 0; i < skip.Count; i++)
                if (skip[i] == index) return false;
            return true;
        }
    }
}
