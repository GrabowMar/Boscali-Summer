using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The TAC BOARD's rows, wipe, lamp rule and gauge row.</summary>
    internal static class ScopeLayoutTests
    {
        public static void Run()
        {
            Layout();
            Wipe();
            Lamps();
            Gauges();
        }

        private static void Layout()
        {
            BoardGeometry empty = ScopeLayout.Compute(new BoardRows());
            TestAssert.Near(empty.Height, 0f, "A board with no row takes no room");

            var all = new BoardRows { Lamps = true, ScopeH = ScopeLayout.Full, Gauges = true, Ticker = true };
            BoardGeometry g = ScopeLayout.Compute(all);
            TestAssert.Near(g.Height, 434f, "Every row at full height is the prototype's 434 units");
            TestAssert.Near(g.Lamps.Y, 4f, "The lamp strip sits one pad down");
            TestAssert.Near(g.Scope.Y, g.Lamps.Bottom + 4f, "Rows are a gap apart: scope");
            TestAssert.Near(g.Gauges.Y, g.Scope.Bottom + 4f, "Rows are a gap apart: gauges");
            TestAssert.Near(g.Ticker.Y, g.Gauges.Bottom + 4f, "Rows are a gap apart: ticker");
            TestAssert.Near(g.Height, g.Ticker.Bottom + 4f, "...and the board ends one pad below the last row");

            var cruise = new BoardRows { Lamps = true, ScopeH = ScopeLayout.Compact, Gauges = true };
            TestAssert.Near(ScopeLayout.Compute(cruise).Height, 4f + 26f + 4f + 100f + 4f + 64f + 4f, "Compact scope, no ticker");
            var gaugesOnly = new BoardRows { Gauges = true };
            TestAssert.That(ScopeLayout.Compute(gaugesOnly).Lamps.H == 0f && ScopeLayout.Compute(gaugesOnly).Gauges.Y == 4f, "An absent row takes no room and the next one moves up");

            TestAssert.Near(ScopeLayout.Body(true, false), 100f, "The compact body is the whole strip");
            TestAssert.Near(ScopeLayout.Body(false, false), 300f, "A full page body is 300");
            TestAssert.Near(ScopeLayout.Body(false, true), 274f, "A page strip takes 26 off a full page");
            TestAssert.Near(ScopeLayout.Body(true, true), 100f, "The compact strip never has a page strip");

            TestAssert.Near(ScopeLayout.ThreatLampX(2) + ScopeLayout.ThreatLampWidth(2), ScopeLayout.Pad + ScopeLayout.Inner, "The MSL lamp ends at the strip's right edge");
            TestAssert.Near(ScopeLayout.ThreatLampX(1) + ScopeLayout.ThreatLampWidth(1) + ScopeLayout.LampGap, ScopeLayout.ThreatLampX(2), "SPKE sits left of MSL");
            TestAssert.That(ScopeLayout.SystemLampX(6) + ScopeLayout.LampW < ScopeLayout.ThreatLampX(0), "Seven system lamps clear the threat lamps");
            TestAssert.Near(ScopeLayout.GaugeX(3) + ScopeLayout.GaugeW, ScopeLayout.Pad + ScopeLayout.Inner, "Four gauge cells fill the row");
        }

        private static void Wipe()
        {
            var t = new ScopeTransition();
            t.Update(1, ScopeLayout.Compact, 0f);
            TestAssert.That(t.Height == ScopeLayout.Compact && !t.Running && t.Wipe >= 1f, "The first page snaps: no wipe at boot");
            t.Update(1, ScopeLayout.Compact, 1f);
            TestAssert.That(!t.Running, "A steady page stays still");
            t.Update(2, ScopeLayout.Full, 2f);
            TestAssert.That(t.Running && t.Wipe < 0.01f && t.Height == ScopeLayout.Compact, "A page change starts the wipe where the old height was");
            t.Update(2, ScopeLayout.Full, 2.08f);
            TestAssert.That(t.Wipe > 0.4f && t.Wipe < 0.6f && t.Height > ScopeLayout.Compact && t.Height < ScopeLayout.Full, "Half way through the wipe the height is between");
            t.Update(2, ScopeLayout.Full, 2.2f);
            TestAssert.That(!t.Running && t.Height == ScopeLayout.Full && t.Wipe >= 1f, "The wipe ends in 160 ms at the new height");
            t.Update(3, ScopeLayout.Compact, 3f);
            t.Update(3, ScopeLayout.Compact, 3.05f);
            float mid = t.Height;
            t.Update(4, ScopeLayout.Full, 3.06f);
            TestAssert.That(t.Height == mid, "A change mid-wipe eases from where the scope is, never jumps");
            t.Reset();
            TestAssert.That(t.Height == 0f && !t.Running, "Reset forgets the page");
        }

        private static void Lamps()
        {
            var c = new LampChange();
            TestAssert.That(!c.Recent(0, LampState.On, 10f), "The first reading is a baseline, not a change");
            TestAssert.That(!c.Recent(0, LampState.On, 11f), "A steady lamp is not recent");
            TestAssert.That(c.Recent(0, LampState.Dark, 12f), "A lamp that just went dark is recent: the change is seen once");
            TestAssert.That(c.Recent(0, LampState.Dark, 14f), "...for 2.5 seconds");
            TestAssert.That(!c.Recent(0, LampState.Dark, 14.6f), "...then it is just dark");
            TestAssert.That(c.Recent(0, LampState.Caution, 15f) && c.Recent(0, LampState.Warning, 15.1f), "Every change restarts the window");
            c.Forget(0);
            TestAssert.That(!c.Recent(0, LampState.On, 16f), "A lamp that left the airframe is a baseline again when it returns");
            TestAssert.That(!c.Recent(99, LampState.On, 0f), "An out-of-range lamp is never recent");
            c.Reset();
            TestAssert.That(!c.Recent(0, LampState.Dark, 20f), "Reset re-baselines every lamp");
        }

        private static void Gauges()
        {
            var page = new[] { new GaugeSpec { Id = "g", Label = "G" } };
            var reg = new[]
            {
                new GaugeSpec { Id = "lance", Rank = 0 }, new GaugeSpec { Id = "perk", Rank = 0 }, new GaugeSpec { Id = "radio", Rank = 0 },
                new GaugeSpec { Id = "ap", Rank = 0 }, new GaugeSpec { Id = "ace", Rank = 5 },
            };
            var into = new GaugeSpec[GaugeRow.Slots];
            int n = GaugeRow.Merge(page, 1, reg, 5, into);
            TestAssert.That(n == 4 && into[0].Id == "g" && into[1].Id == "ace" && into[2].Id == "lance" && into[3].Id == "perk", "Page gauge first, then registered by rank (registration order among equals): " + into[1].Id + into[2].Id + into[3].Id);
            var dup = new[] { new GaugeSpec { Id = "lance" } };
            n = GaugeRow.Merge(dup, 1, reg, 5, into);
            int lances = 0;
            for (int i = 0; i < n; i++) if (into[i].Id == "lance") lances++;
            TestAssert.That(n == 4 && lances == 1 && into[0].Id == "lance", "A page gauge displaces the registered one of the same id");
            TestAssert.Eq(GaugeRow.Merge(page, 0, reg, 0, into), 0, "No gauges, no row");
            TestAssert.Eq(GaugeRow.Merge(page, 1, reg, 0, into), 1, "A lone page gauge fills one slot");

            // A Mode gauge's progress rail is a plain 0..1 (a NAV leg 0.1, 0.3, 0.5...): the chevron only ever moves right as the progress grows.
            float prev = float.NegativeInfinity;
            for (float v = 0.1f; v < 0.95f; v += 0.2f)
            {
                float x = GaugeRow.RailX(10f, 109f, v);
                TestAssert.That(x > prev, "The progress chevron is monotonic in the progress: " + v);
                prev = x;
            }
            TestAssert.Near(GaugeRow.RailX(10f, 109f, 0f), 16f, "No progress sits at the rail's start");
            TestAssert.Near(GaugeRow.RailX(10f, 109f, 1f), 97f, "Full progress sits at the diamond");
            TestAssert.Near(GaugeRow.RailX(10f, 109f, 2f), 97f, "Progress beyond 1 is clamped");
        }
    }
}
