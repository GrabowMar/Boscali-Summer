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
            TestAssert.Eq(C2Words.Banner("Boscali", C2Area.Orbital), "TOP SECRET // BOSCALI EYES ONLY // ORBITAL SUPPORT C2", "banner orbital");
            TestAssert.Eq(C2Words.Banner("Primeva", C2Area.Cyber), "TOP SECRET // PRIMEVA EYES ONLY // CYBER-EW", "banner cyber");
            TestAssert.Eq(C2Words.Banner(null, C2Area.Sof), "TOP SECRET // FACTION EYES ONLY // SOF-JTAC", "banner no faction");
            TestAssert.Eq(C2Words.Session(76561198000000001UL, 3), C2Words.Session(76561198000000001UL, 3), "session deterministic");
            TestAssert.That(C2Words.Session(1, 3) != C2Words.Session(2, 3), "session differs per player");
            TestAssert.That(System.Text.RegularExpressions.Regex.IsMatch(C2Words.Session(9, 1), "^[0-9A-F]{2}-[0-9A-F]{2}-[0-9A-F]{2}$"), "session shape");
            TestAssert.Eq(C2Words.KeyRotation(0f), "5:00", "key rot start");
            TestAssert.Eq(C2Words.KeyRotation(48f), "4:12", "key rot 48 s");
            TestAssert.Eq(C2Words.KeyRotation(300f), "5:00", "key rot wraps");
            TestAssert.Eq(C2Words.KeyRotation(float.NaN), "5:00", "key rot nan");
            TestAssert.That(System.Text.RegularExpressions.Regex.IsMatch(C2Words.AuthCode(6, 'H'), "^[0-9A-F]{4}-H$"), "auth shape");
            TestAssert.Eq(C2Words.AuthCode(6, 'H'), C2Words.AuthCode(6, 'H'), "auth deterministic");
            TestAssert.Eq(C2Words.TabLabel(C2Tab.Cap, 0), "[1] CAP", "tab cap");
            TestAssert.Eq(C2Words.TabLabel(C2Tab.Board, 0), "[5] BOARD", "tab board empty");
            TestAssert.Eq(C2Words.TabLabel(C2Tab.Board, 3), "[5] BOARD·3", "tab board count");
            TestAssert.Eq(C2Words.TabLabel(C2Tab.Board, 120), "[5] BOARD·99", "tab board clamps");
            TestAssert.Eq(C2Words.Fit("VIPER-2", 10), "VIPER-2", "fit short");
            TestAssert.Eq(C2Words.Fit("AVERYLONGCALLSIGNNAME123", 10), "AVERYLONG…", "fit long");
            TestAssert.Eq(C2Words.Fit(null, 5), "", "fit null");
            TestAssert.Eq(C2Words.Clock(592f), "9:52", "clock");
            TestAssert.Eq(C2Words.Clock(-1f), "—", "clock negative");
            TestAssert.Eq(C2Words.Clock(float.NaN), "—", "clock nan");
            TestAssert.Eq(C2Words.HudStrip(C2HudKind.Tasked), "C2 // TASKED // NEW POST", "hud strip tasked");
            TestAssert.Eq(C2Words.HudStrip(C2HudKind.Inbound), "C2 // INBOUND", "hud strip inbound");
            TestAssert.Eq(C2Words.HudStrip(C2HudKind.Intent), "C2 // INT", "hud strip intent");
            TestAssert.Eq(C2Words.HudStripTone(C2HudKind.Tasked), C2Tone.Warn, "hud tone tasked");
            TestAssert.Eq(C2Words.HudStripTone(C2HudKind.Inbound), C2Tone.Danger, "hud tone inbound");
            TestAssert.Eq(C2Words.HudStripTone(C2HudKind.Intent), C2Tone.Info, "hud tone intent");
            TestAssert.Eq(C2Board.Title(3, 0), "LIVE POSTS · 3", "board title");
            TestAssert.Eq(C2Board.Title(3, 2), "LIVE POSTS · 3 · STALE 2", "board title stale");
            TestAssert.Eq(C2Board.QuietWord(true), "QUIET MODE · ON", "quiet on");
            TestAssert.Eq(C2Board.QuietWord(false), "QUIET MODE · OFF", "quiet off");
            TestAssert.That(C2Board.QuietHelp(true).Contains("Inbound") && C2Board.QuietHelp(false).Contains("Inbound"), "quiet help keeps inbound audible");

            var console = new C2Console();
            var buf = new C2Line[8];
            TestAssert.Eq(console.CopyNewest(buf, 8), 0, "empty console");
            int v0 = console.Version;
            console.Add("handshake ok", C2Tone.Info, 1f);
            TestAssert.That(console.Version != v0, "version bumps");
            console.Add("NEGATIVE: LOW CREDIT — NEED 120 CR", C2Tone.Warn, 2f);
            console.Add("NEGATIVE: LOW CREDIT — NEED 120 CR", C2Tone.Warn, 3f);
            console.Add("NEGATIVE: LOW CREDIT — NEED 120 CR", C2Tone.Warn, 3.5f);
            int n = console.CopyNewest(buf, 8);
            TestAssert.Eq(n, 2, "coalesced repeats");
            TestAssert.Eq(buf[1].Count, 3, "repeat count");
            TestAssert.Eq(C2Console.Render(buf[1]), "> NEGATIVE: LOW CREDIT — NEED 120 CR ×3", "render repeat");
            TestAssert.Eq(C2Console.Render(buf[0]), "> handshake ok", "render single");
            console.Add("NEGATIVE: LOW CREDIT — NEED 120 CR", C2Tone.Warn, 9f);
            TestAssert.Eq(console.CopyNewest(buf, 8), 3, "repeat after 2 s is a new line");
            for (int i = 0; i < 50; i++) console.Add("line " + i, C2Tone.Info, 10f + i);
            var all = new C2Line[64];
            TestAssert.Eq(console.CopyNewest(all, 64), C2Console.Capacity, "ring capacity");
            TestAssert.Eq(all[C2Console.Capacity - 1].Text, "line 49", "newest last");
            TestAssert.Eq(console.CopyNewest(buf, 4), 4, "max respected");
            TestAssert.Eq(buf[3].Text, "line 49", "max keeps newest");
            console.Clear();
            TestAssert.Eq(console.CopyNewest(buf, 8), 0, "cleared");
            console.Add(null, C2Tone.Info, 1f);
            TestAssert.Eq(console.CopyNewest(buf, 8), 0, "null ignored");

            Cap();

            TestAssert.That(!C2Tabs.KeyAllowed(false, false, false), "keys not taken while flying");
            TestAssert.That(C2Tabs.KeyAllowed(true, false, false), "keys over page");
            TestAssert.That(C2Tabs.KeyAllowed(false, true, false), "keys in full screen");
            TestAssert.That(!C2Tabs.KeyAllowed(true, true, true), "never while typing");
            TestAssert.Eq(C2Words.ThreatClasses("TERRAIN · MISSILE WARNING · BANDIT 12 KM"), "TERRAIN · MISSILE WARNING · BANDIT", "threat classes drop the distance");
            TestAssert.Eq(C2Words.ThreatClasses("BANDIT 3 KM"), C2Words.ThreatClasses("BANDIT 40 KM"), "a closing bandit is the same class");
            TestAssert.Eq(C2Words.ThreatClasses(""), "", "no threat");
            TestAssert.Eq(C2Cap.CallsReady(new[]
            {
                Tile(SupportActionId.Prsm, CallState.Ready, "READY"), Tile(SupportActionId.Prsm, CallState.Armed, "ARMED"),
                Tile(SupportActionId.Prsm, CallState.Pending, "WAIT"), Tile(SupportActionId.Prsm, CallState.Locked, "LOCKED"),
                Tile(SupportActionId.Prsm, CallState.Cooldown, "9s"), Tile(SupportActionId.Prsm, CallState.LowCredit, "NEED 1 ALLOC")
            }), 3, "calls ready counts ready, armed and pending only");
            TestAssert.Eq(C2Tabs.FromKey(5), C2Tab.Board, "key 5");
            TestAssert.Eq(C2Tabs.FromKey(9), C2Tab.Cap, "bad key");
        }

        private static CallTile Tile(SupportActionId id, CallState state, string word, string cost = "16 ALLOC", string reason = "", string tier = "SPACE R4") =>
            new CallTile(id, "LABEL", tier, cost, reason, state, word, state == CallState.Ready || state == CallState.Armed);

        private static void Cap()
        {
            TestAssert.Eq(C2Cap.ChipKind(""), C2Chip.None, "chip none");
            TestAssert.Eq(C2Cap.ChipKind("-30 % UNDERDOG"), C2Chip.Discount, "chip discount");
            TestAssert.Eq(C2Cap.ChipKind("+25 % CONTESTED"), C2Chip.Surcharge, "chip surcharge");
            TestAssert.Eq(C2Cap.ChipKind("WEATHER"), C2Chip.Other, "chip other");

            TestAssert.Eq(C2Cap.StateWord(Tile(SupportActionId.Prsm, CallState.Ready, "READY")), "READY", "state ready");
            TestAssert.Eq(C2Cap.StateWord(Tile(SupportActionId.Prsm, CallState.Armed, "ARMED — PRESS AGAIN")), "ARMED", "state armed");
            TestAssert.Eq(C2Cap.StateWord(Tile(SupportActionId.Prsm, CallState.Cooldown, "72s")), "COOL 1:12", "state cooldown clock");
            TestAssert.Eq(C2Cap.StateWord(Tile(SupportActionId.Prsm, CallState.Locked, "NEEDS STRIKE QUALIFICATION")), "LOCKED", "state locked");
            TestAssert.Eq(C2Cap.StateWord(Tile(SupportActionId.Prsm, CallState.LowCredit, "NEED 16 ALLOC")), "LOW ALLOC", "state low allocation");
            TestAssert.Eq(C2Cap.StateWord(Tile(SupportActionId.Prsm, CallState.Offline, "OFFLINE")), "OFFLINE", "state offline");

            CallTile locked = Tile(SupportActionId.Cruise, CallState.Locked, "NEEDS STRIKE QUALIFICATION");
            TestAssert.Eq(C2Cap.UnlockText(locked), "NEEDS STRIKE QUALIFICATION", "unlock text");
            TestAssert.Eq(C2Cap.UnlockText(Tile(SupportActionId.Cruise, CallState.Locked, "LOCKED")), "", "bare locked has no unlock text");
            TestAssert.Eq(C2Cap.Sub(locked, false), "LOCKED · NEEDS STRIKE QUALIFICATION", "locked sub-line carries the gate");
            TestAssert.That(System.Text.RegularExpressions.Regex.IsMatch(C2Cap.Sub(Tile(SupportActionId.Cruise, CallState.Ready, "READY"), false), "^AUTH [0-9A-F]{4}-S · SPACE R4$"), "sub-line auth and rung");
            TestAssert.That(C2Cap.Sub(Tile(SupportActionId.JtacMark, CallState.Ready, "READY", tier: "SOF R1"), true).EndsWith("· UNLASE FREE"), "jtac sub-line says unlase is free");

            TestAssert.Eq(C2Cap.RowTip(locked), CallWords.Refusal(CallRefusal.Locked, unlock: "NEEDS STRIKE QUALIFICATION"), "locked tip is the refusal");
            TestAssert.Eq(C2Cap.RowTip(Tile(SupportActionId.Prsm, CallState.Cooldown, "72s")), CallWords.Refusal(CallRefusal.Cooldown, seconds: 72), "cooldown tip");
            TestAssert.Eq(C2Cap.RowTip(Tile(SupportActionId.Prsm, CallState.LowCredit, "NEED 400 ALLOC", "400 ALLOC")), CallWords.Refusal(CallRefusal.LowCredit, need: 400), "low allocation tip");
            TestAssert.Eq(C2Cap.RowTip(Tile(SupportActionId.Prsm, CallState.Offline, "OFFLINE")), CallWords.Refusal(CallRefusal.Offline), "offline tip");
            TestAssert.That(C2Cap.RowTip(Tile(SupportActionId.Prsm, CallState.Ready, "READY", "25 ALLOC", "-25 % EXPLOIT")).Contains("25 ALLOC (-25 % EXPLOIT)"), "ready tip quotes price and reason");

            TestAssert.Eq(C2Cap.Slab(true, false, ""), "ARMED", "slab armed");
            TestAssert.Eq(C2Cap.Slab(false, true, ""), "WAIT", "slab pending");
            TestAssert.Eq(C2Cap.Slab(false, false, "NEGATIVE: NO AIM — DESIGNATE OR RIGHT-CLICK MAP"), "NEG", "slab refusal");
            TestAssert.Eq(C2Cap.Slab(false, false, ""), "READY", "slab ready");
            TestAssert.Eq(C2Cap.FooterWords(""), "HOTLINE OPEN · PRESS A PERK TO ARM", "footer plain");
            TestAssert.Eq(C2Cap.FooterWords("SHOT · RADAR"), "SHOT · RADAR", "footer words win");
            TestAssert.Eq(C2Cap.Delta(0), "NO CHANGE", "delta none");
            TestAssert.Eq(C2Cap.Delta(9), "+9 ALLOC", "delta up");
            TestAssert.Eq(C2Cap.Delta(-140), "-140 ALLOC", "delta down");
            TestAssert.Eq(C2Cap.LineTone("NEGATIVE: LOW ALLOCATION — NEED 400"), C2Tone.Warn, "refusal line is amber");
            TestAssert.Eq(C2Cap.LineTone("DISARMED"), C2Tone.Info, "other lines are info");
        }
    }
}
