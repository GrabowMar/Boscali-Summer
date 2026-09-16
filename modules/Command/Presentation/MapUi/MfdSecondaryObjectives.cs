using System;
using System.Collections.Generic;
using System.Text;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Pure layout and copy rules for the MIS contract board. The board is a paged grid:
    /// the panel decides how many dossiers fit from its own body height, and every label
    /// states only what the host actually reported.
    /// </summary>
    internal static class MfdSecondaryObjectives
    {
        /// <summary>Three filter tabs: offers, accepted work, closed work.</summary>
        public const int FilterAvailable = 0;
        public const int FilterActive = 1;
        public const int FilterResults = 2;

        /// <summary>Every dossier occupies one 198 px slot so a page never straddles the pager.</summary>
        public const float RowPitch = 198f;
        public const float MinCardHeight = 190f;
        public const float MaxCardHeight = 220f;
        public const int MaxCards = 4;

        /// <summary>Header, filters, summary line and pager measured above and below the grid.</summary>
        private const float BoardChromeHeight = 140f;

        public static string PlainObjective(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "OBJECTIVE";
            var text = new StringBuilder(value.Length);
            bool tag = false, space = true;
            foreach (char c in value)
            {
                if (c == '<') { tag = true; continue; }
                if (c == '>') { tag = false; continue; }
                if (tag) continue;
                if (char.IsWhiteSpace(c) || c == '_')
                { if (!space) text.Append(' '); space = true; }
                else { text.Append(c); space = false; }
            }
            return text.ToString().Trim();
        }

        public static int PageCount(int count, int perPage) => count <= 0 ? 1 : 1 + (count - 1) / Math.Max(1, perPage);

        public static int ClampPage(int page, int count, int perPage) => Math.Max(0, Math.Min(page, PageCount(count, perPage) - 1));

        public static string TimeLabel(bool complete, float secondsRemaining)
        {
            if (complete) return "COMPLETE";
            if (float.IsNaN(secondsRemaining) || float.IsInfinity(secondsRemaining)) return "TIME UNKNOWN";
            if (secondsRemaining <= 0f) return "ENDED";
            int seconds = (int)Math.Ceiling(Math.Min(86400f, secondsRemaining));
            return "LEFT " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }

        /// <summary>How many dossiers the body height can hold without clipping the pager.</summary>
        public static int RowsFor(float bodyHeight)
        {
            float available = Math.Max(0f, bodyHeight - BoardChromeHeight);
            return Math.Max(1, Math.Min(MaxCards, (int)((available + AvTokens.Gap) / RowPitch)));
        }

        /// <summary>Dossier height for that row count; taller panels grow the card, never the row count.</summary>
        public static float CardHeightFor(float bodyHeight, int rows)
        {
            if (rows < 1) rows = 1;
            float available = Math.Max(MinCardHeight, bodyHeight - BoardChromeHeight);
            return Math.Max(MinCardHeight, Math.Min(MaxCardHeight, available - (rows - 1) * RowPitch));
        }

        /// <summary>
        /// Offers and accepted work read soonest-deadline-first; closed work reads newest-first.
        /// A missing clock never jumps the queue.
        /// </summary>
        public static void SortForDisplay(List<SecondaryObjectiveView> entries, int filter)
        {
            if (entries == null || entries.Count < 2) return;
            entries.Sort(filter == FilterResults ? CompareNewest : CompareDeadline);
        }

        private static int CompareDeadline(SecondaryObjectiveView a, SecondaryObjectiveView b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int byClock = Remaining(a).CompareTo(Remaining(b));
            return byClock != 0 ? byClock : a.Id.CompareTo(b.Id);
        }

        private static int CompareNewest(SecondaryObjectiveView a, SecondaryObjectiveView b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            return b.Id.CompareTo(a.Id);
        }

        private static float Remaining(SecondaryObjectiveView objective)
        {
            float seconds = objective.SecondsRemaining;
            return float.IsNaN(seconds) || float.IsInfinity(seconds) ? float.MaxValue : seconds;
        }

        /// <summary>The urgency chip: the phase is named, then the clock the host reported.</summary>
        public static string ChipLabel(SecondaryObjectiveView objective)
        {
            if (objective == null) return "LINK LOST";
            if (objective.IsComplete) return "PAID";
            if (float.IsNaN(objective.SecondsRemaining) || float.IsInfinity(objective.SecondsRemaining)) return "TIME UNKNOWN";
            if (objective.SecondsRemaining > 0f)
                return objective.IsOffered
                    ? TimeLabel(false, objective.SecondsRemaining).Replace("LEFT", "OFFER")
                    : TimeLabel(false, objective.SecondsRemaining);
            return objective.IsOffered ? "OFFER ENDED" : objective.IsActive ? "TIME ENDED" : "CLOSED";
        }

        /// <summary>Pay is only reported as collected when the host reported completion.</summary>
        public static string PayoutLabel(SecondaryObjectiveView objective)
        {
            if (objective == null) return "—";
            string money = "$" + Math.Max(0, objective.Money).ToString("N0");
            string xp = Math.Max(0, objective.Xp).ToString("N0") + " XP";
            string prefix = objective.IsComplete ? "PAID  " :
                objective.IsOffered || objective.IsActive ? "" : "UNPAID  ";
            return prefix + money + "   +   " + xp;
        }

        /// <summary>A count line that never invents a ceiling: an unknown limit reads as a plain count.</summary>
        public static string BoardSummary(int available, int active, int activeLimit, int results)
        {
            return available + " OFFERS  ·  " + ShortCount(active, activeLimit) + " ACTIVE  ·  " + results + " CLOSED";
        }

        /// <summary>"1/2" while the host reports a ceiling, a plain count when it does not.</summary>
        public static string ShortCount(int active, int activeLimit) =>
            activeLimit > 0 ? active + "/" + activeLimit : active.ToString();

        public static string EmptyMessage(int filter)
        {
            switch (filter)
            {
                case FilterAvailable:
                    return "NO OFFERS ON THE BOARD\nContracts are drawn from the live battlefield as the front moves.";
                case FilterActive:
                    return "NO ACCEPTED CONTRACTS\nAccept an offer to place its marker on the map.";
                default:
                    return "NO CLOSED CONTRACTS\nCompleted and lapsed contracts stay listed for a short while.";
            }
        }
    }
}
