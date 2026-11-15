using System;
using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// The one look of a game-log line (the map's old FIELD LOG, brought back 2026-11-15 for EVN LOG and the map's corner log alike): the
    /// mission-clock stamp quiet, the category word and "//" in the line's tone, the report itself warm white. The tone is read from the game's
    /// own verbs, one way: an unrecognised line stays neutral. The word always carries the meaning; the colour only adds to it.
    /// </summary>
    internal static class LogLine
    {
        public const string StampHex = "90A5AE", NeutralHex = "F3F0E8", ReadyHex = "8ED9BE", CautionHex = "F5BA56", DangerHex = "F39A96";

        private static readonly string[] DangerWords =
            { "destroyed", "demolished", "shot down", "downed", "sunk", "sank", "crashed", "killed", "levelled", "leveled", "wrecked", "nuclear", "mia" };
        private static readonly string[] ReadyWords =
            { "captured", "secured", "recaptured", "landed", "repaired", "resupplied", "defended", "escaped", "recovered", "rescued", "deployed", "complete" };
        private static readonly string[] CautionWords =
            { "intercepted", "engaged", "damaged", "launched", "fired", "jammed", "detected", "spotted", "incoming", "hit ", " hit", "strafed", "breach" };

        /// <summary>LOSS (danger), UPDATE (ready), CONTACT (caution), or a plain report (inert).</summary>
        public static AvState Tone(string text)
        {
            if (string.IsNullOrEmpty(text)) return AvState.Inert;
            if (Has(text, DangerWords)) return AvState.Danger;
            if (Has(text, ReadyWords)) return AvState.Ready;
            return Has(text, CautionWords) ? AvState.Caution : AvState.Inert;
        }

        public static string Hex(AvState tone) =>
            tone == AvState.Danger ? DangerHex : tone == AvState.Ready ? ReadyHex : tone == AvState.Caution ? CautionHex : NeutralHex;

        /// <summary>"14:39  GROUND //  CI-22 CRICKET DEMOLISHED ..." as TMP rich text; the report is noparse, so a '&lt;' in chat stays text.</summary>
        public static string Rich(TacticalLogEntry entry) =>
            "<color=#" + StampHex + ">" + entry.Stamp + "</color>  <color=#" + Hex(Tone(entry.Text)) + ">" + entry.Category +
            " //</color>  <color=#" + NeutralHex + "><noparse>" + entry.Text + "</noparse></color>";

        private static bool Has(string line, string[] words)
        {
            foreach (string word in words)
            {
                int at = line.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                // Whole words only for the short ones ("mia" inside "academia" is no loss).
                if (at >= 0 && (word.Length > 3 || Edge(line, at - 1) && Edge(line, at + word.Length))) return true;
            }
            return false;
        }

        private static bool Edge(string line, int i) => i < 0 || i >= line.Length || !char.IsLetter(line[i]);
    }
}
