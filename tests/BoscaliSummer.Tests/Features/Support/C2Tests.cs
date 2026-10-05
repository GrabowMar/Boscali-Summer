using System;
using BoscaliSummer.Modules.Support.Domain.C2;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>C2 terminal words, console ring and tab keys.</summary>
    internal static class C2Tests
    {
        public static void Run()
        {
            Eq(C2Words.Banner("Boscali", C2Area.Orbital), "TOP SECRET // BOSCALI EYES ONLY // ORBITAL SUPPORT C2", "banner orbital");
            Eq(C2Words.Banner("Primeva", C2Area.Cyber), "TOP SECRET // PRIMEVA EYES ONLY // CYBER-EW", "banner cyber");
            Eq(C2Words.Banner(null, C2Area.Sof), "TOP SECRET // FACTION EYES ONLY // SOF-JTAC", "banner no faction");
            Eq(C2Words.Session(76561198000000001UL, 3), C2Words.Session(76561198000000001UL, 3), "session deterministic");
            TestAssert.That(C2Words.Session(1, 3) != C2Words.Session(2, 3), "session differs per player");
            TestAssert.That(System.Text.RegularExpressions.Regex.IsMatch(C2Words.Session(9, 1), "^[0-9A-F]{2}-[0-9A-F]{2}-[0-9A-F]{2}$"), "session shape");
            Eq(C2Words.KeyRotation(0f), "5:00", "key rot start");
            Eq(C2Words.KeyRotation(48f), "4:12", "key rot 48 s");
            Eq(C2Words.KeyRotation(300f), "5:00", "key rot wraps");
            Eq(C2Words.KeyRotation(float.NaN), "5:00", "key rot nan");
            TestAssert.That(System.Text.RegularExpressions.Regex.IsMatch(C2Words.AuthCode(6, 'H'), "^[0-9A-F]{4}-H$"), "auth shape");
            Eq(C2Words.AuthCode(6, 'H'), C2Words.AuthCode(6, 'H'), "auth deterministic");
            Eq(C2Words.TabLabel(C2Tab.Cap, 0), "[1] CAP", "tab cap");
            Eq(C2Words.TabLabel(C2Tab.Board, 0), "[5] BOARD", "tab board empty");
            Eq(C2Words.TabLabel(C2Tab.Board, 3), "[5] BOARD·3", "tab board count");
            Eq(C2Words.TabLabel(C2Tab.Board, 120), "[5] BOARD·99", "tab board clamps");
            Eq(C2Words.Fit("VIPER-2", 10), "VIPER-2", "fit short");
            Eq(C2Words.Fit("AVERYLONGCALLSIGNNAME123", 10), "AVERYLONG…", "fit long");
            Eq(C2Words.Fit(null, 5), "", "fit null");
            Eq(C2Words.Clock(592f), "9:52", "clock");
            Eq(C2Words.Clock(-1f), "—", "clock negative");
            Eq(C2Words.Clock(float.NaN), "—", "clock nan");

            var console = new C2Console();
            var buf = new C2Line[8];
            Eq(console.CopyNewest(buf, 8), 0, "empty console");
            int v0 = console.Version;
            console.Add("handshake ok", C2Tone.Info, 1f);
            TestAssert.That(console.Version != v0, "version bumps");
            console.Add("NEGATIVE: LOW CREDIT — NEED 120 CR", C2Tone.Warn, 2f);
            console.Add("NEGATIVE: LOW CREDIT — NEED 120 CR", C2Tone.Warn, 3f);
            console.Add("NEGATIVE: LOW CREDIT — NEED 120 CR", C2Tone.Warn, 3.5f);
            int n = console.CopyNewest(buf, 8);
            Eq(n, 2, "coalesced repeats");
            Eq(buf[1].Count, 3, "repeat count");
            Eq(C2Console.Render(buf[1]), "> NEGATIVE: LOW CREDIT — NEED 120 CR ×3", "render repeat");
            Eq(C2Console.Render(buf[0]), "> handshake ok", "render single");
            console.Add("NEGATIVE: LOW CREDIT — NEED 120 CR", C2Tone.Warn, 9f);
            Eq(console.CopyNewest(buf, 8), 3, "repeat after 2 s is a new line");
            for (int i = 0; i < 50; i++) console.Add("line " + i, C2Tone.Info, 10f + i);
            var all = new C2Line[64];
            Eq(console.CopyNewest(all, 64), C2Console.Capacity, "ring capacity");
            Eq(all[C2Console.Capacity - 1].Text, "line 49", "newest last");
            Eq(console.CopyNewest(buf, 4), 4, "max respected");
            Eq(buf[3].Text, "line 49", "max keeps newest");
            console.Clear();
            Eq(console.CopyNewest(buf, 8), 0, "cleared");
            console.Add(null, C2Tone.Info, 1f);
            Eq(console.CopyNewest(buf, 8), 0, "null ignored");

            TestAssert.That(!C2Tabs.KeyAllowed(false, false, false), "keys not taken while flying");
            TestAssert.That(C2Tabs.KeyAllowed(true, false, false), "keys over page");
            TestAssert.That(C2Tabs.KeyAllowed(false, true, false), "keys in full screen");
            TestAssert.That(!C2Tabs.KeyAllowed(true, true, true), "never while typing");
            Eq(C2Tabs.FromKey(5), C2Tab.Board, "key 5");
            Eq(C2Tabs.FromKey(9), C2Tab.Cap, "bad key");
        }

        private static void Eq<T>(T actual, T expected, string message) =>
            TestAssert.That(Equals(actual, expected), message + " (got " + actual + ", want " + expected + ")");
    }
}
