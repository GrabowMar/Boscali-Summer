using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.DynamicOperations.Domain;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>The reinforcement desk's staff-log lines: short, upper case, never cut by the log, a dash for what is not known (annex 03 C12, N-24).</summary>
    internal static class WaveTextTests
    {
        private const string Dot = " · ";

        public static void Run()
        {
            LinesFollowTheAnnexFormats();
            TheCreditCountIsCappedAtTheWaveSize();
            UnknownNumbersPrintADash();
            BigNumbersAreCappedAtSevenDigits();
            VariableTextIsClippedAndUpperCased();
            EveryLineFitsTheStaffLogForEveryKindAndBlock();
            EveryBlockHasAShortDistinctToken();
        }

        private static void LinesFollowTheAnnexFormats()
        {
            TestAssert.Eq(WaveText.Counted(2, "HOLD THE LINE", "VIPER"), "SORTIE 2/3" + " DONE" + Dot + "HOLD THE LINE" + Dot + "VIPER", "counted line");
            TestAssert.Eq(WaveText.BankFull("HOLD THE LINE"), "SORTIE DONE" + Dot + "HOLD THE LINE" + Dot + "WAVE BANK FULL", "bank full line");
            TestAssert.Eq(WaveText.NoCredit, "SORTIE DONE" + Dot + "OPERATION CHANGED" + Dot + "NO WAVE CREDIT", "late line");
            TestAssert.Eq(WaveText.Bought("RIPSAW CONVOY", 45, 1950), "WAVE BOUGHT" + Dot + "RIPSAW CONVOY" + Dot + "-45" + Dot + "POOL 1950", "bought line");
            TestAssert.Eq(WaveText.Held(WaveBlock.PoolShort, 45, 20), "WAVE HELD" + Dot + "POOL SHORT" + Dot + "COST 45 POOL 20", "held line");
            TestAssert.Eq(WaveText.AiConvoy("RIPSAW CONVOY", 45), "AI CONVOY" + Dot + "RIPSAW CONVOY" + Dot + "-45", "AI line");
            TestAssert.Eq(WaveText.EventConvoy("RIPSAW CONVOY", 45), "EVENT CONVOY" + Dot + "RIPSAW CONVOY" + Dot + "-45", "event line");
        }

        private static void TheCreditCountIsCappedAtTheWaveSize()
        {
            TestAssert.That(WaveText.Counted(3, "T", "P").StartsWith("SORTIE 3/3 DONE"), "the third credit reads 3/3");
            TestAssert.That(WaveText.Counted(5, "T", "P").StartsWith("SORTIE 3/3 DONE"),
                "credits banked beyond one wave (a held wave) still read 3/3, never 5/3");
            TestAssert.That(WaveText.Counted(-1, "T", "P").StartsWith("SORTIE -/3 DONE"), "an unknown bank prints a dash");
        }

        private static void UnknownNumbersPrintADash()
        {
            TestAssert.Eq(WaveText.Bought("G", -1, -1), "WAVE BOUGHT" + Dot + "G" + Dot + "-" + Dot + "POOL -", "bought with nothing known");
            TestAssert.Eq(WaveText.Held(WaveBlock.NoDepot, -1, -1), "WAVE HELD" + Dot + "NO DEPOT" + Dot + "COST - POOL -", "held with nothing known");
            TestAssert.Eq(WaveText.AiConvoy("G", -1), "AI CONVOY" + Dot + "G" + Dot + "-", "AI line with an unknown price");
            TestAssert.Eq(WaveText.Bought("G", 0, 0), "WAVE BOUGHT" + Dot + "G" + Dot + "-0" + Dot + "POOL 0", "a real zero prints as zero");
            TestAssert.Eq(WaveText.Bought(null, 5, 5), "WAVE BOUGHT" + Dot + "-" + Dot + "-5" + Dot + "POOL 5", "an unknown group prints a dash");
            TestAssert.Eq(WaveText.Counted(1, "", null), "SORTIE 1/3 DONE" + Dot + "-" + Dot + "-", "an unknown title and pilot print dashes");
        }

        private static void BigNumbersAreCappedAtSevenDigits()
        {
            TestAssert.That(WaveText.Bought("G", int.MaxValue, 1000000).EndsWith("POOL 1000000") &&
                WaveText.Bought("G", int.MaxValue, 5).Contains("-9999999"), "a price beyond seven digits prints seven");
            TestAssert.That(WaveText.Held(WaveBlock.PoolShort, 1234567, int.MaxValue).EndsWith("COST 1234567 POOL 9999999"), "seven digits pass, ten are capped");
        }

        private static void VariableTextIsClippedAndUpperCased()
        {
            TestAssert.That(WaveText.Counted(1, "t", new string('p', 40)).EndsWith(Dot + new string('P', 12)), "a pilot is clipped to 12 and upper-cased");
            TestAssert.That(WaveText.Bought(new string('g', 40), 1, 1).Contains(Dot + new string('G', 20) + Dot), "a group is clipped to 20");
            TestAssert.That(WaveText.Counted(1, new string('t', 40), "p").Contains(Dot + new string('T', 27) + Dot), "a title is clipped to 27");
            TestAssert.That(WaveText.Bought("  ripsaw  ", 1, 1).Contains(Dot + "RIPSAW" + Dot), "surrounding space is trimmed");
        }

        private static void EveryLineFitsTheStaffLogForEveryKindAndBlock()
        {
            string group = new string('G', 40), pilot = new string('P', 32);
            var lines = new List<string> { WaveText.NoCredit };
            foreach (OperationKind kind in Enum.GetValues(typeof(OperationKind)))
            {
                string title = OperationTitles.Title(kind);
                lines.Add(WaveText.Counted(3, title, pilot));
                lines.Add(WaveText.BankFull(title));
            }
            foreach (WaveBlock block in Enum.GetValues(typeof(WaveBlock)))
                foreach (int number in new[] { -1, 0, 1234567, int.MaxValue })
                    lines.Add(WaveText.Held(block, number, number));
            foreach (int number in new[] { -1, 0, 1234567, int.MaxValue })
            {
                lines.Add(WaveText.Bought(group, number, number));
                lines.Add(WaveText.AiConvoy(group, number));
                lines.Add(WaveText.EventConvoy(group, number));
            }
            foreach (string line in lines)
                TestAssert.That(line.Length > 0 && line.Length <= StaffLog.MaximumTextLength, "fits the staff log whole (" + line.Length + "): " + line);
        }

        private static void EveryBlockHasAShortDistinctToken()
        {
            var seen = new HashSet<string>();
            foreach (WaveBlock block in Enum.GetValues(typeof(WaveBlock)))
            {
                string token = WaveText.Reason(block);
                TestAssert.That(token.Length > 0 && token.Length <= 16, "a staff-log token of at most 16 characters: " + block + " " + token);
                TestAssert.That(token == token.ToUpperInvariant() && seen.Add(token), "an upper-case, distinct token: " + block);
            }
            TestAssert.Eq(WaveText.Reason((WaveBlock)200), "-", "an undefined block prints a dash");
        }
    }
}
