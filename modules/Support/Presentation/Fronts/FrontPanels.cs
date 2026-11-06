using System;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// What the rooms' tabs share about the front: an offer by programme, a queued row by id, one CHIP IN and its press, the standing orders' words and lock,
    /// and a build clock in words.
    /// </summary>
    internal static class FrontPanels
    {
        /// <summary>The most allocation one CHIP IN puts into a funding bar.</summary>
        private const int Chip = 25;

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
            return ChipFor(total - Mathf.Min(total, Mathf.RoundToInt(q.Cost * q.Progress)));
        }

        /// <summary>One chip-in: up to <see cref="Chip"/> AP, never more than the funding bar still needs. 0 when nothing is needed.</summary>
        public static int ChipFor(int need) => need <= 0 ? 0 : Mathf.Min(Chip, Mathf.CeilToInt(need / FrontRules.DonateRate));

        /// <summary>"25 AP → 75 FUNDS": both sides of the donation, the funds from the current conversion rate.</summary>
        public static string Pays(int ap) => ap + " AP → " + Mathf.RoundToInt(ap * FrontRules.DonateRate) + " FUNDS";

        /// <summary>CHIP IN on a queued row of <paramref name="front"/>, judged again on the press (the row may have started building since the paint).</summary>
        public static void Donate(IFrontActions actions, Front front, FrontRoomView v, ushort row)
        {
            actions.Touch();
            if (v != null && QueueRow(v, row, out FrontQueueView q) && ChipAp(q) > 0) actions.Donate(front, row, ChipAp(q));
        }

        /// <summary>
        /// A build clock in words: <paramref name="prefix"/> and m:ss while time is left, else <paramref name="done"/> (ROLLING OUT): a finished build reads 0:00 until
        /// the host's next snapshot shows it standing.
        /// </summary>
        public static string Clocked(string prefix, int left, string done = "ROLLING OUT") => left > 0 ? prefix + FrontKit.Clock(left) : done;

        /// <summary>A card's second line under a <see cref="Clocked"/> state once the clock reads 0:00.</summary>
        public const string AnyMoment = "ANY MOMENT NOW";

        // The host sends a lock holder as printable ASCII, cleaned to 20 then cut to 16 (SpaceWire.Clean, FrontBook.Trim): the local callsign is compared in that form, so a long or non-ASCII name still reads as its own lock.
        public static bool SameName(string by, string callsign)
        {
            if (string.IsNullOrEmpty(by) || callsign == null) return false;
            string mine = Domain.Space.SpaceWire.Clean(callsign, Domain.Space.SpaceReply.MaxClaimant).Trim();
            if (mine.Length > FrontWire.MaxName) mine = mine.Substring(0, FrontWire.MaxName);
            return string.Equals(by, mine, StringComparison.OrdinalIgnoreCase);
        }

        public static string LockWord(bool mine, string by, float left) =>
            mine ? "HELD BY YOU · " + FrontKit.Clock(left) : "LOCKED · " + (string.IsNullOrEmpty(by) ? "?" : by) + " · " + FrontKit.Clock(left);

        /// <summary>What a standing order has the front's director do when nobody drives.</summary>
        public static string Describe(Front f, FrontDirective d)
        {
            switch (f)
            {
                case Front.Space:
                    return d == FrontDirective.Recon ? "Radar and optical passes over the front; contacts reveal themselves."
                        : d == FrontDirective.Strike ? "Keeps the kinetic bird ready and marks high-value targets."
                        : "Hardens the pads and rephases the satellites to dodge ASAT.";
                case Front.Cyber:
                    return d == FrontDirective.Defend ? "Hunts intrusions and protects the data centers."
                        : d == FrontDirective.Balanced ? "Splits effort between hunting intrusions and probing SAM nodes."
                        : "Hops SAM and radar nodes and burns them near friendly pilots.";
                default:
                    return d == FrontDirective.Recon ? "Teams sweep the ground near the front."
                        : d == FrontDirective.Sabotage ? "Hunts enemy anchors: uplinks, trucks, launch sites."
                        : "Seizes buildings and keeps them.";
            }
        }
    }
}
