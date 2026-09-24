using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>Why a zero-count tab has nothing to show; the copy names the real reason.</summary>
    internal enum BoardEmptyReason
    {
        Ready,
        Unavailable,
        LinkLost,
        LimitReached,
        DirectorExhausted,
    }

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
        /// <summary>One page never grows past the host board's own three-card limit.</summary>
        public const int MaxCards = 3;
        /// <summary>The cockpit marker's urgency gate; both surfaces read the same clock as amber.</summary>
        public const float UrgentSeconds = 120f;

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

        /// <summary>Splits camelCase, PascalCase and acronym boundaries into readable words.</summary>
        public static string Humanize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
            var sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (i > 0)
                {
                    char prev = value[i - 1];
                    if (char.IsLower(prev) && char.IsUpper(c))
                    {
                        sb.Append(' ');
                    }
                    else if (char.IsUpper(prev) && char.IsUpper(c) && i + 1 < value.Length && char.IsLower(value[i + 1]))
                    {
                        sb.Append(' ');
                    }
                    else if (char.IsLetter(prev) && char.IsDigit(c))
                    {
                        sb.Append(' ');
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
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

        /// <summary>
        /// Dossier height for that row count; taller panels grow the card, never the row count.
        /// A multi-row card is capped at its own pitch, or its background paints over the
        /// action row of the dossier above it.
        /// </summary>
        public static float CardHeightFor(float bodyHeight, int rows)
        {
            if (rows < 1) rows = 1;
            float available = Math.Max(MinCardHeight, bodyHeight - BoardChromeHeight);
            float height = Math.Max(MinCardHeight, Math.Min(MaxCardHeight, available - (rows - 1) * RowPitch));
            return rows >= 2 ? Math.Min(height, RowPitch) : height;
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

        /// <summary>The cockpit marker's title line, shared by the HUD, the map tag and this board.</summary>
        public static string TitleLine(int id, string title) =>
            "#" + id + " " + (string.IsNullOrEmpty(title) ? "SECONDARY OBJECTIVE" : title);

        /// <summary>The cockpit marker's own countdown rule: T-45s under a minute, T-5:00 above it.</summary>
        public static string Countdown(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return "";
            int total = (int)Math.Ceiling(seconds);
            if (total < 60) return "T-" + total.ToString(CultureInfo.InvariantCulture) + "s";
            return "T-" + (total / 60).ToString(CultureInfo.InvariantCulture) + ":" +
                (total % 60).ToString("00", CultureInfo.InvariantCulture);
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
                    : Countdown(objective.SecondsRemaining);
            return objective.IsOffered ? "OFFER ENDED" : objective.IsActive ? "TIME ENDED" : "CLOSED";
        }

        /// <summary>An offer is acceptable only while the host's clock is finite and still running.</summary>
        public static bool OfferLive(SecondaryObjectiveView objective) =>
            objective != null && objective.IsOffered &&
            !float.IsNaN(objective.SecondsRemaining) && !float.IsInfinity(objective.SecondsRemaining) &&
            objective.SecondsRemaining > 0f;

        /// <summary>The accept action is live only when the offer is and the faction has room.</summary>
        public static bool CanAccept(SecondaryObjectiveView objective, bool hasCapacity) =>
            hasCapacity && OfferLive(objective);

        /// <summary>The action row reads exactly one state: accept, ceiling, lapsed, tracked or lost.</summary>
        public static string AcceptLabel(SecondaryObjectiveView objective, bool hasCapacity)
        {
            if (objective != null && objective.IsOffered)
            {
                if (!OfferLive(objective)) return "OFFER ENDED";
                return hasCapacity ? "ACCEPT CONTRACT" : "ACTIVE LIMIT REACHED";
            }
            if (objective != null && objective.IsActive)
                return objective.HasMarker ? "TRACKED ON MAP" : "CONTACT LOST";
            return "CONTRACT ENDED";
        }

        /// <summary>Pay is only reported as collected when the host reported completion.</summary>
        public static string PayoutLabel(SecondaryObjectiveView objective)
        {
            if (objective == null) return "—";
            string money = "$" + Math.Max(0, objective.Money).ToString("N0", CultureInfo.InvariantCulture);
            string xp = Math.Max(0, objective.Xp).ToString("N0", CultureInfo.InvariantCulture) + " XP";
            string prefix = objective.IsComplete ? "PAID  " :
                objective.IsOffered || objective.IsActive ? "" : "UNPAID  ";
            return prefix + money + "   +   " + xp;
        }

        /// <summary>A count line that never invents a ceiling: an unknown limit reads as a plain count.</summary>
        public static string BoardSummary(int available, int active, int activeLimit, int results)
        {
            string offers = available == 1 ? "1 OFFER" : available + " OFFERS";
            return offers + "  ·  " + ShortCount(active, activeLimit) + " ACTIVE  ·  " + results + " CLOSED";
        }

        /// <summary>"1/2" while the host reports a ceiling, a plain count when it does not.</summary>
        public static string ShortCount(int active, int activeLimit) =>
            activeLimit > 0 ? active + "/" + activeLimit : active.ToString();

        /// <summary>
        /// The zero-count line. A missing director, a silent host or a full active roster is
        /// named for what it is; the contract-cycle copy only prints when the board is serving.
        /// </summary>
        public static string EmptyMessage(int filter, BoardEmptyReason reason)
        {
            switch (reason)
            {
                case BoardEmptyReason.Unavailable:
                    return "CONTRACT BOARD UNAVAILABLE\nDynamic operations are not running in this mission.";
                case BoardEmptyReason.LinkLost:
                    return "WAITING FOR THE HOST BOARD\nNo authoritative contract state has arrived yet.";
                case BoardEmptyReason.LimitReached:
                    return "ACTIVE LIMIT REACHED\nFinish or abort an active contract before accepting another.";
                case BoardEmptyReason.DirectorExhausted:
                    return "NO CONTRACTS TO ISSUE\nThe mission director has no eligible work for this faction.";
                default:
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
}
