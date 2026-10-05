using System;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Runtime;

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

            Cap();

            TestAssert.That(!C2Tabs.KeyAllowed(false, false, false), "keys not taken while flying");
            TestAssert.That(C2Tabs.KeyAllowed(true, false, false), "keys over page");
            TestAssert.That(C2Tabs.KeyAllowed(false, true, false), "keys in full screen");
            TestAssert.That(!C2Tabs.KeyAllowed(true, true, true), "never while typing");
            Eq(C2Tabs.FromKey(5), C2Tab.Board, "key 5");
            Eq(C2Tabs.FromKey(9), C2Tab.Cap, "bad key");
        }

        private static CallTile Tile(SupportActionId id, CallState state, string word, string cost = "140 CR", string reason = "", string tier = "HEAVY") =>
            new CallTile(id, "LABEL", tier, cost, reason, state, word, state == CallState.Ready || state == CallState.Armed);

        private static void Cap()
        {
            Eq(C2Cap.ChipKind(""), C2Chip.None, "chip none");
            Eq(C2Cap.ChipKind("-30 % UNDERDOG"), C2Chip.Discount, "chip discount");
            Eq(C2Cap.ChipKind("+25 % CONTESTED"), C2Chip.Surcharge, "chip surcharge");
            Eq(C2Cap.ChipKind("WEATHER"), C2Chip.Other, "chip other");

            Eq(C2Cap.StateWord(Tile(SupportActionId.Prsm, CallState.Ready, "READY")), "READY", "state ready");
            Eq(C2Cap.StateWord(Tile(SupportActionId.Prsm, CallState.Armed, "ARMED — PRESS AGAIN")), "ARMED", "state armed");
            Eq(C2Cap.StateWord(Tile(SupportActionId.Prsm, CallState.Cooldown, "72s")), "COOL 1:12", "state cooldown clock");
            Eq(C2Cap.StateWord(Tile(SupportActionId.Prsm, CallState.Cooldown, "FROZEN 3 MIN")), "FROZEN", "state frozen");
            Eq(C2Cap.StateWord(Tile(SupportActionId.Prsm, CallState.Locked, "HOLD A BASE → HEAVY")), "LOCKED", "state locked");
            Eq(C2Cap.StateWord(Tile(SupportActionId.Prsm, CallState.LowCredit, "NEED 140 CR")), "LOW CR", "state low credit");
            Eq(C2Cap.StateWord(Tile(SupportActionId.Prsm, CallState.Offline, "OFFLINE")), "OFFLINE", "state offline");

            CallTile locked = Tile(SupportActionId.Cruise, CallState.Locked, "HOLD A BASE → HEAVY");
            Eq(C2Cap.UnlockText(locked), "HOLD A BASE → HEAVY", "unlock text");
            Eq(C2Cap.UnlockText(Tile(SupportActionId.Cruise, CallState.Locked, "LOCKED")), "", "bare locked has no unlock text");
            Eq(C2Cap.Sub(locked, false), "LOCKED · HOLD A BASE → HEAVY", "locked sub-line carries the goal");
            TestAssert.That(System.Text.RegularExpressions.Regex.IsMatch(C2Cap.Sub(Tile(SupportActionId.Cruise, CallState.Ready, "READY"), false), "^AUTH [0-9A-F]{4}-H · HEAVY$"), "sub-line auth and tier");
            TestAssert.That(C2Cap.Sub(Tile(SupportActionId.JtacMark, CallState.Ready, "READY", tier: "LIGHT"), true).EndsWith("· UNLASE FREE"), "jtac sub-line says unlase is free");

            Eq(C2Cap.RowTip(locked), CallWords.Refusal(CallRefusal.Locked, unlock: "HOLD A BASE → HEAVY"), "locked tip is the refusal");
            Eq(C2Cap.RowTip(Tile(SupportActionId.Prsm, CallState.Cooldown, "72s")), CallWords.Refusal(CallRefusal.Cooldown, seconds: 72), "cooldown tip");
            Eq(C2Cap.RowTip(Tile(SupportActionId.Prsm, CallState.Cooldown, "FROZEN 3 MIN")), CallWords.Refusal(CallRefusal.Frozen, seconds: 180), "frozen tip");
            Eq(C2Cap.RowTip(Tile(SupportActionId.Prsm, CallState.LowCredit, "NEED 400 CR", "400 CR")), CallWords.Refusal(CallRefusal.LowCredit, need: 400), "low credit tip");
            Eq(C2Cap.RowTip(Tile(SupportActionId.Prsm, CallState.Offline, "OFFLINE")), CallWords.Refusal(CallRefusal.Offline), "offline tip");
            TestAssert.That(C2Cap.RowTip(Tile(SupportActionId.Prsm, CallState.Ready, "READY", "25 CR", "-30 % UNDERDOG")).Contains("25 CR (-30 % UNDERDOG)"), "ready tip quotes price and reason");

            Eq(C2Cap.Slab(true, false, ""), "ARMED", "slab armed");
            Eq(C2Cap.Slab(false, true, ""), "WAIT", "slab pending");
            Eq(C2Cap.Slab(false, false, "NEGATIVE: NO AIM — DESIGNATE OR RIGHT-CLICK MAP"), "NEG", "slab refusal");
            Eq(C2Cap.Slab(false, false, ""), "READY", "slab ready");
            Eq(C2Cap.FooterWords("", "HOLD A BASE → HEAVY"), "HOTLINE OPEN · PRESS A CALL TO ARM · NEXT: HOLD A BASE → HEAVY", "footer next unlock");
            Eq(C2Cap.FooterWords("", ""), "HOTLINE OPEN · PRESS A CALL TO ARM", "footer plain");
            Eq(C2Cap.FooterWords("SHOT · RADAR", "X"), "SHOT · RADAR", "footer words win");
            Eq(C2Cap.Delta(0), "NO CHANGE", "delta none");
            Eq(C2Cap.Delta(9), "+9 CR", "delta up");
            Eq(C2Cap.Delta(-140), "-140 CR", "delta down");
            Eq(C2Cap.LineTone("NEGATIVE: LOW CREDIT — NEED 400 CR"), C2Tone.Warn, "refusal line is amber");
            Eq(C2Cap.LineTone("DISARMED"), C2Tone.Info, "other lines are info");
        }

        private static void Eq<T>(T actual, T expected, string message) =>
            TestAssert.That(Equals(actual, expected), message + " (got " + actual + ", want " + expected + ")");
    }
}
