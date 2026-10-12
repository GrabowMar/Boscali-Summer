using System;
using System.Globalization;
using System.Text.RegularExpressions;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// Pure: the captions the Station Board panel composes out of model fields, so a part only copies a string. Nothing here reads a service,
    /// a clock or the engine; an unknown is a dash or nothing, never a zero.
    /// </summary>
    internal static class StrPanelText
    {
        private static readonly char[] Space = { ' ' };

        /// <summary>The four digits "mmss" of a countdown flap, or "" for an unknown (negative) time; saturates at 99:59.</summary>
        public static string Flap(int seconds)
        {
            if (seconds < 0) return "";
            int s = Math.Min(seconds, 99 * 60 + 59);
            return (s / 60).ToString("00", CultureInfo.InvariantCulture) + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Heading of the hero lane's sortie list: the platform it is tied to, how many, and what finishing more does for the wave.</summary>
        public static string SortiesCaption(StrViewModel vm)
        {
            if (vm == null) return "";
            string tie = vm.Effort.FrontTag.Length > 0 ? "TIED TO " + vm.Effort.FrontTag : "TIED TO THE OPERATION";
            int n = vm.SortieCount + vm.SortieMore;
            string text = tie + StrWords.Dot + n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " SORTIE" : " SORTIES");
            if (vm.Wave.Present && vm.Wave.StateText.StartsWith("EARNING", StringComparison.Ordinal))
            {
                int left = vm.Wave.PerWave - vm.Wave.Credits;
                if (left > 0) text += StrWords.Dot + left.ToString(CultureInfo.InvariantCulture) + (left == 1 ? " MORE DONE LAUNCHES THE WAVE" : " MORE DONE LAUNCH THE WAVE");
            }
            return text;
        }

        /// <summary>"+2 MORE ON THE MAP", or "" when every tied sortie has its ticket.</summary>
        public static string MoreOnTheMap(StrViewModel vm)
        {
            int more = StrViewBuilder.MoreOnTheMap(vm);
            return more > 0 ? "+" + more.ToString(CultureInfo.InvariantCulture) + " MORE ON THE MAP" : "";
        }

        /// <summary>Second line of the wave strip: group, price (once) and the pool arithmetic. "" when the wave desk is not there.</summary>
        public static string WaveLine(StrViewModel vm)
        {
            StrWaveView w = vm?.Wave;
            if (w == null || !w.Present) return "";
            string pool = w.PoolText ?? "";
            bool priced = pool.Contains("COST") || pool.Contains("NEEDS");
            string text = w.GroupText ?? "";
            if (!priced) text += (text.Length > 0 ? StrWords.Dot : "") + "COST " + (w.Cost >= 0 ? w.Cost.ToString("N0", CultureInfo.InvariantCulture) : StrWords.Dash);
            if (pool.Length > 0) text += (text.Length > 0 ? StrWords.Dot : "") + pool;
            return text;
        }

        /// <summary>The check or the cross of the price against the pool; nothing while either is unknown.</summary>
        public static string WaveCheck(StrViewModel vm)
        {
            if (vm == null || vm.Wave == null || !vm.Wave.Present) return "";
            switch (vm.Wave.Affordable)
            {
                case StrTri.Yes: return StrWords.Yes + " AFFORDABLE";
                case StrTri.No: return StrWords.No + " POOL SHORT";
                default: return "";
            }
        }

        /// <summary>REPLAN until the first press arms it, then the platform it will call off.</summary>
        public static string ReplanLabel(StrViewModel vm)
        {
            if (vm == null || !vm.Effort.ReplanArmed) return "REPLAN";
            return vm.Effort.FrontTag.Length > 0 ? "CALLS OFF " + vm.Effort.FrontTag : "CALLS OFF";
        }

        /// <summary>The one line under a ticket's data when its main verb is refused (accept while offered, the wing once accepted); "" otherwise.</summary>
        public static string TicketReason(StrSortieRow row)
        {
            if (row == null) return "";
            if (row.Offered) return row.Accept.Allowed ? "" : row.Accept.Text;
            if (row.Active) return row.Wing.Allowed ? "" : row.Wing.Text;
            return "";
        }

        public static string PlateState(StrProposalRow row) => row != null && row.Recommended ? "DEFAULT" : "OPEN";

        /// <summary>"WAVE 1,850 ✓" as the word and the price the plate stacks.</summary>
        public static void PlateWave(StrProposalRow row, out string word, out string price)
        {
            string text = row?.WaveText ?? "";
            string[] parts = text.Split(Space, 2);
            word = parts.Length > 0 ? parts[0] : "";
            price = parts.Length > 1 ? parts[1] : "";
        }

        public static string RiskChip(StrProposalRow row) => "RISK " + (row == null || string.IsNullOrEmpty(row.RiskText) ? StrWords.Dash : row.RiskText);

        /// <summary>What the LOG tab says when it has no lines: the staff has not written yet, or there is no staff report to read.</summary>
        public static void LogNote(StrViewModel vm, out string title, out string detail)
        {
            bool report = vm != null && (vm.Mode == StrBoardMode.Operation || vm.Mode == StrBoardMode.Proposals || vm.Mode == StrBoardMode.Assessing ||
                                         vm.Mode == StrBoardMode.NoOpening || vm.Mode == StrBoardMode.Reduced);
            if (report)
            {
                title = "NO STAFF LOG YET";
                detail = "The staff writes a line when it deals, picks, holds or pays.";
                return;
            }
            title = "NO STAFF LOG";
            string head = vm == null || string.IsNullOrEmpty(vm.Banner.Head) ? "NO STAFF REPORT" : vm.Banner.Head;
            detail = head + StrWords.Dot + "THE LOG READS THE STAFF'S REPORT";
        }
    }

    /// <summary>The control-id grammar (annex 05 P11): dotted lower case, indices 1-based. One spelling for the panel, the overlay, the sim and the harness.</summary>
    internal static class StrControlIds
    {
        private static readonly Regex Grammar = new Regex(
            @"^(tab\.(board|sitrep|log)|posture\.(cautious|steady|bold)|replan|hero\.locate|prop\.[1-3]\.(pick|locate)|front\.[1-8]\.(redirect|locate)|" +
            @"sortie\.[1-3]\.(accept|wing|locate)|layer\.(fronts|operation|sorties)|map\.card\.(primary|secondary|close)|map\.tag\.(effort|prop|front|sortie|post)\.[1-9][0-9]*)$",
            RegexOptions.CultureInvariant);

        public static bool IsValid(string id) => !string.IsNullOrEmpty(id) && Grammar.IsMatch(id);

        public static string Tab(int page) => page == 0 ? "tab.board" : page == 1 ? "tab.sitrep" : "tab.log";

        public static string Posture(TheaterWarPosture posture) =>
            posture == TheaterWarPosture.Cautious ? "posture.cautious" : posture == TheaterWarPosture.Bold ? "posture.bold" : "posture.steady";

        public static string Prop(int n, string verb) => "prop." + n.ToString(CultureInfo.InvariantCulture) + "." + verb;
        public static string Front(int n, string verb) => "front." + n.ToString(CultureInfo.InvariantCulture) + "." + verb;
        public static string Sortie(int n, string verb) => "sortie." + n.ToString(CultureInfo.InvariantCulture) + "." + verb;

        public const string Replan = "replan", HeroLocate = "hero.locate";
    }
}
