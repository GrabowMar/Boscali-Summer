using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>Why a front was not offered. Counted per candidate by LivingWarRules.Choose.</summary>
    internal enum WarReject : byte { None, BadFix, Gone, NoPresence, NoSteering, NoCarrier, HeldQuiet, PostureRatio, Vetoed }

    internal struct RejectTally { internal int BadFix, Gone, NoPresence, NoSteering, NoCarrier, HeldQuiet, PostureRatio, Vetoed, Candidates; }

    internal struct EmptyInputs
    {
        internal bool Healthy, HasTerritory, OperationLive, MissionHasGroundWar;
        internal float SecondsToFirstDeal;
        internal GroundReason GroundReason;      // debounced over two reviews by the engine
        internal int DeckCount;
        internal RejectTally Tally;
    }

    /// <summary>The typed reason a deck is empty, in the order a pilot can act on it (annex 01 E3, design N-17).</summary>
    internal static class EmptyReasonRules
    {
        /// <summary>
        /// None when the deck has a card. seconds = the countdown the reason shows (first deal, veto expiry), else 0.
        /// Order: StaffFault, FirstReview, NoTerritoryField, NoGroundWar, NoFronts (no live candidate), then the ground reason when steering rejects dominate,
        /// OperationLive, then the dominant reject (Vetoed, then NoCarrier and NoPresence together, then PostureRatio, then HeldQuiet; ties in that order), else NoFronts.
        /// </summary>
        internal static TheaterEmptyReason Decide(in EmptyInputs i, float vetoSecondsLeft, out float seconds)
        {
            seconds = 0f;
            if (i.DeckCount > 0) return TheaterEmptyReason.None;
            if (!i.Healthy) return TheaterEmptyReason.StaffFault;
            if (i.SecondsToFirstDeal > 0f && !float.IsInfinity(i.SecondsToFirstDeal))
            {
                seconds = i.SecondsToFirstDeal;
                return TheaterEmptyReason.FirstReview;
            }
            if (!i.HasTerritory) return TheaterEmptyReason.NoTerritoryField;
            if (!i.MissionHasGroundWar) return TheaterEmptyReason.NoGroundWar;

            RejectTally t = i.Tally;
            if (t.Candidates - t.Gone - t.BadFix <= 0) return TheaterEmptyReason.NoFronts;

            int vetoed = t.Vetoed, ground = t.NoCarrier + t.NoPresence, posture = t.PostureRatio, quiet = t.HeldQuiet;
            int other = Math.Max(Math.Max(vetoed, ground), Math.Max(posture, quiet));
            if (t.NoSteering > 0 && t.NoSteering >= other)
            {
                switch (i.GroundReason)
                {
                    case GroundReason.TacticsOff: return TheaterEmptyReason.TacticsOff;
                    case GroundReason.RtsOwnsOrders: return TheaterEmptyReason.RtsOwnsOrders;
                    case GroundReason.Unsupported:
                    case GroundReason.NoVehicles: return TheaterEmptyReason.NoGroundWar;
                    case GroundReason.NoFront: return TheaterEmptyReason.NoFronts;
                }
            }
            if (i.OperationLive) return TheaterEmptyReason.OperationLive;
            if (other <= 0) return TheaterEmptyReason.NoFronts;
            if (vetoed == other)
            {
                seconds = vetoSecondsLeft > 0f && !float.IsInfinity(vetoSecondsLeft) ? vetoSecondsLeft : 0f;
                return TheaterEmptyReason.CalledOff;
            }
            if (ground == other) return TheaterEmptyReason.NoFriendlyGroundNearFront;
            if (posture == other) return TheaterEmptyReason.PostureTooCautious;
            return TheaterEmptyReason.FrontsQuiet;
        }
    }
}
