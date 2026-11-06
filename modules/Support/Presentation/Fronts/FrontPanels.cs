using BoscaliSummer.Modules.Support.Domain.Fronts;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>What the rooms' first tabs share about the front queue: an offer by programme, a queued row by id, one CHIP IN and its press.</summary>
    internal static class FrontPanels
    {
        public static FrontOfferView OfferOf(FrontRoomView v, ProgrammeId id)
        {
            foreach (FrontOfferView o in v.Offers) if (o.Id == id) return o;
            return new FrontOfferView { Id = id, Refusal = "WAITING FOR THE HOST" };
        }

        /// <summary>The queue row with the host's row id (v.Queue and v.Row.Queue share their order).</summary>
        public static bool QueueRow(FrontRoomView v, ushort row, out FrontQueueView q)
        {
            foreach (FrontQueueView r in v.Queue) if (r.Row == row) { q = r; return true; }
            q = default;
            return false;
        }

        /// <summary>The queued LAUNCH of one bird kind.</summary>
        public static bool LaunchRow(FrontRoomView v, int kind, out FrontQueueView q)
        {
            for (int i = 0; i < v.Queue.Count && i < v.Row.Queue.Count; i++)
                if (v.Row.Queue[i].Id == ProgrammeId.LaunchSatellite && v.Row.Queue[i].Kind == kind) { q = v.Queue[i]; return true; }
            q = default;
            return false;
        }

        /// <summary>One CHIP IN for a queued row: up to 25 AP, never more than the funding bar still needs; 0 once it builds or is funded.</summary>
        public static int ChipAp(in FrontQueueView q)
        {
            if (q.Building) return 0;
            int total = Mathf.RoundToInt(q.Cost);
            return FrontRail.ChipFor(total - Mathf.Min(total, Mathf.RoundToInt(q.Cost * q.Progress)));
        }

        /// <summary>CHIP IN on a queued row of <paramref name="front"/>, judged again on the press (the row may have started building since the paint).</summary>
        public static void Donate(IFrontActions actions, Front front, FrontRoomView v, ushort row)
        {
            actions.Touch();
            if (v != null && QueueRow(v, row, out FrontQueueView q) && ChipAp(q) > 0) actions.Donate(front, row, ChipAp(q));
        }
    }
}
