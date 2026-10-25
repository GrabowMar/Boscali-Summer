using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    internal static class BoardOptionsTests
    {
        public static void Run()
        {
            BoardOptions d = BoardOptions.Default;
            TestAssert.That(d.Enabled && d.ShowStores && d.Layout == BoardLayout.Wide, "Board and stores enabled, Wide layout");
            TestAssert.That(d.ScopeMode == BoardScopeMode.Manual && d.PageCount == 1 && d.Page1 == BoardPage.Threat, "One manually selected radar-warning page by default");
            TestAssert.That(d.Page2 == BoardPage.Shot && d.Page3 == BoardPage.Wing && d.Page4 == BoardPage.Objective, "Extra slots remember useful defaults");
            TestAssert.Eq(BoardOptions.MaxPages, 4, "Two across and two down at most");
            TestAssert.Near(d.Scale, .85f, "Default size");
            TestAssert.Near(d.Shade, .45f, "Default shade");
            TestAssert.Near(d.ThreatHoldSeconds, 6f, "RWR contact hold");
            TestAssert.That(d.ShowLamps && d.ShowGauges && d.ShowTicker && d.ShowSystems && d.Sway && d.Corners, "Major sections start enabled");
            BoardOptions clean = d;
            clean.Sanitize();
            TestAssert.That(clean.PageCount == d.PageCount && clean.Page1 == d.Page1 && clean.Page4 == d.Page4 && clean.Scale == d.Scale && clean.Opacity == d.Opacity, "Defaults survive sanitization");

            Ranges();
            SystemsIsNoSlot();
            ManualPages();
            Sections();
        }

        private static void Ranges()
        {
            BoardOptions o = BoardOptions.Default;
            o.Scale = .1f; o.OffsetX = -1f; o.OffsetY = -1f; o.Opacity = 0f; o.Shade = -1f; o.ThreatHoldSeconds = 0f; o.PageCount = 0;
            o.Sanitize();
            TestAssert.That(o.Scale == .6f && o.OffsetX == 0f && o.OffsetY == 0f && o.Opacity == .25f && o.Shade == 0f && o.ThreatHoldSeconds == 2f && o.PageCount == 1, "Every lower bound clamps");
            o.Scale = 9f; o.OffsetX = 5000f; o.OffsetY = 5000f; o.Opacity = 3f; o.Shade = 7f; o.ThreatHoldSeconds = 99f; o.PageCount = 9;
            o.Sanitize();
            TestAssert.That(o.Scale == 1.4f && o.OffsetX == 1200f && o.OffsetY == 600f && o.Opacity == 1f && o.Shade == 1f && o.ThreatHoldSeconds == 30f && o.PageCount == 4, "Every upper bound clamps");
            o.Scale = float.NaN; o.OffsetX = float.NaN; o.OffsetY = float.NaN; o.Opacity = float.NaN; o.Shade = float.NaN; o.ThreatHoldSeconds = float.NaN;
            o.Layout = (BoardLayout)9; o.Declutter = (BoardDeclutter)(-1); o.ScopeMode = (BoardScopeMode)8;
            o.Page1 = (BoardPage)100; o.Page2 = (BoardPage)(-1); o.Page3 = (BoardPage)10; o.Page4 = (BoardPage)55;
            o.Sanitize();
            BoardOptions d = BoardOptions.Default;
            TestAssert.That(o.Scale == d.Scale && o.OffsetX == d.OffsetX && o.OffsetY == d.OffsetY && o.Opacity == d.Opacity && o.Shade == d.Shade && o.ThreatHoldSeconds == d.ThreatHoldSeconds, "NaN falls back to defaults");
            TestAssert.That(o.Layout == d.Layout && o.Declutter == d.Declutter && o.ScopeMode == BoardScopeMode.Manual, "Unknown modes fall back to manual");
            TestAssert.That(o.Page1 == d.Page1 && o.Page2 == d.Page2 && o.Page3 == d.Page3 && o.Page4 == d.Page4, "Unknown page values fall back per slot");
            o.Page1 = o.Page2 = o.Page3 = o.Page4 = BoardPage.Cargo;
            o.Sanitize();
            TestAssert.That(o.Page1 == BoardPage.Cargo && Distinct(o), "Duplicate config slots resolve to distinct pages");
            o.Scale = float.PositiveInfinity; o.OffsetX = float.NegativeInfinity;
            o.Sanitize();
            TestAssert.That(o.Scale == 1.4f && o.OffsetX == 0f, "Infinity clamps to endpoints");
        }

        private static bool Distinct(in BoardOptions o)
        {
            BoardPage[] p = { o.Page1, o.Page2, o.Page3, o.Page4 };
            for (int i = 0; i < p.Length; i++)
                for (int k = 0; k < i; k++)
                    if (p[i] == p[k]) return false;
            return true;
        }

        /// <summary>SYSTEMS is the board's own always-on block: a slot never holds it, and a config saved when it could (0.2.5's default slot 1) moves off it.</summary>
        private static void SystemsIsNoSlot()
        {
            TestAssert.That(!BoardOptions.Choosable(BoardPage.Systems) && BoardOptions.Choosable(BoardPage.Threat) && BoardOptions.Choosable(BoardPage.Cargo), "SYSTEMS is not a slot choice");
            BoardOptions o = BoardOptions.Default;
            o.PageCount = 3; o.Page1 = BoardPage.Wing; o.Page2 = BoardPage.Systems; o.Page3 = BoardPage.MissileCam;      // the screenshot's board
            o.Sanitize();
            TestAssert.That(o.Page1 == BoardPage.Wing && o.Page3 == BoardPage.MissileCam, "Valid slots keep their pages");
            TestAssert.That(o.Page2 == BoardPage.Shot && Distinct(o), "A saved SYSTEMS slot takes that slot's default page");
            for (int slot = 0; slot < BoardOptions.MaxPages; slot++)
            {
                BoardOptions s = BoardOptions.Default;
                s.Page1 = s.Page2 = s.Page3 = s.Page4 = BoardPage.Systems;
                s.Sanitize();
                TestAssert.That(s.PageAt(slot) != ScopePage.Systems && Distinct(s), "No slot is left on SYSTEMS: " + slot);
            }
        }

        private static void ManualPages()
        {
            BoardOptions o = BoardOptions.Default;
            o.Page1 = BoardPage.Cargo; o.Page2 = BoardPage.Wing; o.Page3 = BoardPage.Threat; o.Page4 = BoardPage.Dock;
            // Retired whitelist values must never hide a manually selected page or mute unseen alerts.
            o.PageThreat = o.PageShot = o.PageMissileCam = o.PageApproach = o.PageWing = o.PageStrike = o.PageDock = o.PageObjective = o.PageSystems = o.PageCargo = false;
            foreach (BoardDeclutter level in new[] { BoardDeclutter.Full, BoardDeclutter.Lean, BoardDeclutter.Minimal })
            {
                o.Declutter = level;
                for (int count = 1; count <= BoardOptions.MaxPages; count++)
                {
                    o.PageCount = count;
                    ushort expected = CuePriority.Bit(ScopePage.Cargo);
                    if (count > 1) expected |= CuePriority.Bit(ScopePage.Wing);
                    if (count > 2) expected |= CuePriority.Bit(ScopePage.Threat);
                    if (count > 3) expected |= CuePriority.Bit(ScopePage.Dock);
                    TestAssert.Eq(o.Effective().Pages, expected, level + ": page count shows precisely the saved slots");
                    TestAssert.Eq(o.Effective().Voiced, (ushort)0x3ff, "Alerts are polled independently of chosen pages and old switches");
                }
            }
            o.ScopeMode = BoardScopeMode.Off;
            TestAssert.That(o.Effective().Pages == 0 && o.Effective().Voiced == 0x3ff, "Hiding tactical pages does not hide alerts");
            o.ScopeMode = BoardScopeMode.Auto;
            TestAssert.That(o.ScopeMode == BoardScopeMode.Manual && CuePriority.Has(o.Effective().Pages, ScopePage.Cargo), "Old Auto config spelling now means manual selection");
            o.PageCount = 1;
            TestAssert.That(o.PageAt(0) == ScopePage.Cargo && o.Page2 == BoardPage.Wing && o.Page3 == BoardPage.Threat && o.Page4 == BoardPage.Dock, "Reducing page count preserves hidden slot choices");
        }

        private static void Sections()
        {
            foreach (BoardDeclutter level in new[] { BoardDeclutter.Full, BoardDeclutter.Lean, BoardDeclutter.Minimal })
            {
                BoardOptions o = BoardOptions.Default;
                o.Declutter = level;
                BoardEffective e = o.Effective();
                TestAssert.That(e.ShowStores && e.ShowLamps && e.ShowTicker, level + ": enabled major sections remain enabled");
                TestAssert.That(e.Furniture == (level == BoardDeclutter.Full) && e.ShowGauges == (level == BoardDeclutter.Full), "Declutter changes decoration and secondary gauges");
                o.ShowStores = o.ShowLamps = o.ShowGauges = o.ShowTicker = o.ShowSystems = false;
                e = o.Effective();
                TestAssert.That(!e.ShowStores && !e.ShowLamps && !e.ShowGauges && !e.ShowTicker && !e.ShowSystems, "Disabled major elements stay disabled at every declutter level");
            }
            BoardOptions min = BoardOptions.Default;
            min.Declutter = BoardDeclutter.Minimal;
            TestAssert.That(!min.Effective().Posts(ScopePage.Systems, HudTone.Caution) && min.Effective().Posts(ScopePage.Systems, HudTone.Warning), "Minimal keeps critical system notices");
        }
    }
}
