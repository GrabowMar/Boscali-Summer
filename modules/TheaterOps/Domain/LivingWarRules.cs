using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>One front as the review read it: where, what stands around it (3 km presence ring and the reach ring) and what kind of front it is.</summary>
    internal readonly struct WarFrontRead
    {
        internal readonly string Key, Name;
        internal readonly float X, Z, Pressure;
        internal readonly int NearFriendly, NearHostile, HostileAirNear, ReachFriendlyGround, ReachFriendlyOther, ReachHostile, Carriers;
        internal readonly FrontSource Source;
        internal readonly bool Held, Observed, Live;

        internal bool Objective => Source == FrontSource.Objective;
        internal bool Base => Source == FrontSource.Base;
        internal int Friendly => ReachFriendlyGround + ReachFriendlyOther;
        internal int HostileNear => NearHostile + HostileAirNear;

        internal WarFrontRead(string key, string name, float x, float z, float pressure, int nearFriendly, int nearHostile, int hostileAirNear,
            int reachFriendlyGround, int reachFriendlyOther, int reachHostile, int carriers, FrontSource source, bool held, bool observed, bool live)
        {
            Key = key; Name = name; X = x; Z = z; Pressure = pressure;
            NearFriendly = nearFriendly; NearHostile = nearHostile; HostileAirNear = hostileAirNear;
            ReachFriendlyGround = reachFriendlyGround; ReachFriendlyOther = reachFriendlyOther; ReachHostile = reachHostile; Carriers = carriers;
            Source = source; Held = held; Observed = observed; Live = live;
        }
    }

    internal readonly struct WarCapability
    {
        internal readonly bool GroundSteering;
        internal WarCapability(bool groundSteering) { GroundSteering = groundSteering; }
    }

    internal readonly struct WarOffer
    {
        internal readonly TheaterOpKind Kind;
        internal readonly WarFrontRead Front;
        internal readonly float Score;
        internal WarOffer(TheaterOpKind kind, WarFrontRead front, float score) { Kind = kind; Front = front; Score = score; }
    }

    /// <summary>Pure, bounded staff choices. A proposal needs physical forces and a map fix (annex 01 E3), and every constant of the staff's clock lives here.</summary>
    internal static class LivingWarRules
    {
        internal const int MaximumFronts = 8, MaximumOffers = 3, MaximumPlaces = 128, MaximumBasePosts = 32, ReservedBaseSlots = 2;
        internal const float OperationLimitSeconds = 600f, SnapshotFreshSeconds = 15f, ReviewSeconds = 10f, HoldTickSeconds = 1f,
            FirstDealSeconds = 15f, StaffPickHumanSeconds = 45f, StaffPickAiSeconds = 5f, PickLockSeconds = 120f,
            CallOffVetoSeconds = 180f, EndVetoSeconds = 120f, RewardCooldownSeconds = 300f,
            FollowRadius = 6000f, FollowStep = 1500f, AssaultRadius = 8000f, DefendRadius = 6000f, ReconRadius = 10000f,
            // ControlFieldCore.PresenceThreshold in Command; a module may not name another's namespace, so the value is repeated here.
            PresenceThreshold = 0.05f;

        /// <summary>Carriers and the force ratio count friendly ground inside ground's own reach (20 km).</summary>
        internal static float ReachRadius => GroundTasking.ReachMeters;

        /// <summary>What an operation of this kind asks of a sortie.</summary>
        internal static FocusRole RoleOf(TheaterOpKind kind) =>
            kind == TheaterOpKind.Assault ? FocusRole.Assault : kind == TheaterOpKind.Defend ? FocusRole.Defend : kind == TheaterOpKind.Recon ? FocusRole.Recon : FocusRole.None;

        /// <summary>Index in the ranking of the best own-base defence that has no card in the deck and is not the live operation's front; -1 when there is none.</summary>
        internal static int UrgentDefence(IReadOnlyList<WarOffer> ranked, IReadOnlyList<DeckCard> deck, string excludeKey)
        {
            for (int i = 0; ranked != null && i < ranked.Count; i++)
            {
                string key = ranked[i].Front.Key;
                if (!DeckRules.IsBaseDefence(ranked[i].Kind, key) || string.Equals(key, excludeKey, StringComparison.Ordinal)) continue;
                bool carded = false;
                for (int d = 0; deck != null && d < deck.Count; d++) carded |= string.Equals(deck[d].Key, key, StringComparison.Ordinal);
                if (!carded) return i;
            }
            return -1;
        }

        internal static float RadiusOf(TheaterOpKind kind) =>
            kind == TheaterOpKind.Defend ? DefendRadius : kind == TheaterOpKind.Recon ? ReconRadius : AssaultRadius;

        /// <summary>
        /// Score above 0 means the front is offered as <paramref name="kind"/>; otherwise <paramref name="reject"/> says why not. First match wins:
        /// a bad, gone or vetoed front; no presence; a held front with a hostile near is a DEFEND (a base is its own presence); a quiet held front;
        /// then ASSAULT and RECON, which need steering and a carrier.
        /// </summary>
        internal static float Evaluate(in WarFrontRead front, TheaterWarPosture posture, in WarCapability cap, bool vetoed,
            out TheaterOpKind kind, out WarReject reject)
        {
            kind = TheaterOpKind.Assault;
            reject = WarReject.BadFix;
            if (string.IsNullOrEmpty(front.Key) || !IsFinite(front.X) || !IsFinite(front.Z) || !IsFinite(front.Pressure) ||
                front.Pressure < 0f || front.Pressure > 1f || posture > TheaterWarPosture.Bold) return float.NegativeInfinity;
            if (!front.Live) { reject = WarReject.Gone; return float.NegativeInfinity; }
            if (vetoed) { reject = WarReject.Vetoed; return float.NegativeInfinity; }
            int ours = Math.Max(0, front.Friendly);
            if (ours == 0 && !front.Base) { reject = WarReject.NoPresence; return float.NegativeInfinity; }
            reject = WarReject.None;
            int hostileNear = Math.Max(0, front.HostileNear);
            if (front.Held && hostileNear > 0)
            {
                kind = TheaterOpKind.Defend;
                return 6f + Math.Min(8, hostileNear) + front.Pressure * 4f + (front.Objective ? 2f : 0f) + (front.Base ? 1f : 0f);
            }
            if (front.Held) { reject = WarReject.HeldQuiet; return float.NegativeInfinity; }
            if (!cap.GroundSteering) { reject = WarReject.NoSteering; return float.NegativeInfinity; }
            if (front.Carriers <= 0) { reject = WarReject.NoCarrier; return float.NegativeInfinity; }
            if (!front.Observed)
            {
                kind = TheaterOpKind.Recon;
                return 1f + (front.Objective ? 1f : 0f) + front.Pressure;
            }
            // Counts are observed local presence, not a combat-power prediction.
            int theirs = Math.Max(0, front.ReachHostile);
            if (front.ReachFriendlyGround < theirs * PostureRule.Ratio(posture)) { reject = WarReject.PostureRatio; return float.NegativeInfinity; }
            kind = TheaterOpKind.Assault;
            float appetite = posture == TheaterWarPosture.Bold ? 2f : posture == TheaterWarPosture.Cautious ? -2f : 0f;
            return 3f + appetite + Math.Min(8, ours) - Math.Min(8, theirs) * .8f + front.Pressure * 2f + (front.Objective ? 2f : 0f);
        }

        /// <summary>
        /// Ranks the offers (best first, ties by key, at most MaximumFronts) and counts every reject. Only the first MaximumFronts fronts are candidates.
        /// <paramref name="isVetoed"/> may be null.
        /// </summary>
        internal static void Choose(IReadOnlyList<WarFrontRead> fronts, TheaterWarPosture posture, in WarCapability cap,
            Func<WarFrontRead, bool> isVetoed, List<WarOffer> ranked, out RejectTally tally)
        {
            tally = default;
            ranked.Clear();
            if (fronts == null) return;
            for (int i = 0; i < fronts.Count && i < MaximumFronts; i++)
            {
                WarFrontRead front = fronts[i];
                tally.Candidates++;
                float score = Evaluate(front, posture, cap, isVetoed != null && isVetoed(front), out TheaterOpKind kind, out WarReject reject);
                switch (reject)
                {
                    case WarReject.BadFix: tally.BadFix++; break;
                    case WarReject.Gone: tally.Gone++; break;
                    case WarReject.NoPresence: tally.NoPresence++; break;
                    case WarReject.NoSteering: tally.NoSteering++; break;
                    case WarReject.NoCarrier: tally.NoCarrier++; break;
                    case WarReject.HeldQuiet: tally.HeldQuiet++; break;
                    case WarReject.PostureRatio: tally.PostureRatio++; break;
                    case WarReject.Vetoed: tally.Vetoed++; break;
                }
                if (!(score > 0f)) continue;
                int insert = 0;
                while (insert < ranked.Count && (ranked[insert].Score > score ||
                    ranked[insert].Score == score && string.CompareOrdinal(ranked[insert].Front.Key, front.Key) <= 0)) insert++;
                ranked.Insert(insert, new WarOffer(kind, front, score));
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
