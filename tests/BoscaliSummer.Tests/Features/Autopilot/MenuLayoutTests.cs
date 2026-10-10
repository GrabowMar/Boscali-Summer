using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    /// <summary>Ring and dock geometry at 1280x720, 1920x1080 and 2560x1080 over every catalog scenario (the prototype's geometry matrix).</summary>
    internal static class MenuLayoutTests
    {
        public static readonly int[][] Screens = { new[] { 1920, 1080 }, new[] { 1280, 720 }, new[] { 2560, 1080 } };

        public static void Run()
        {
            GeometryMatrix();
            ChromeSizes();
            DockHeightIsStableWhileScrolling();
            CorridorReachesTowardTheHubButNeverOverAPlate();
            SectorPickHasAngularHysteresis();
            RowGeometryIsOrdered();
        }

        /// <summary>The shared checks (<see cref="MenuGeometryChecks"/>) for every scenario at every screen.</summary>
        private static void GeometryMatrix()
        {
            var metrics = new MenuTestMetrics();
            foreach (int[] s in Screens)
                foreach (var scenario in MenuWorlds.All())
                {
                    MenuModel m = MenuCatalog.Build(scenario.Value);
                    MenuGeometryChecks.All(TestAssert.That, m, MenuLayout.Compute(s[0], s[1], m.Categories, metrics), metrics);
                }
        }

        private static void ChromeSizes()
        {
            var metrics = new MenuTestMetrics();
            MenuModel m = MenuCatalog.Build(MenuWorlds.Default());
            foreach (int[] s in Screens)
            {
                MenuLayout l = MenuLayout.Compute(s[0], s[1], m.Categories, metrics);
                TestAssert.Near(l.Hint.W, 776f * l.T, "hint bar is 124 + 396 + 256 text units wide @" + s[0] + "x" + s[1]);
                TestAssert.Near(l.DockW, (float)Math.Round(384f * Math.Max(s[1] / 1080f, 0.93f)), "dock width follows the text scale, not the looser layout scale @" + s[0] + "x" + s[1]);
            }
        }

        private static void DockHeightIsStableWhileScrolling()
        {
            var metrics = new MenuTestMetrics();
            MenuModel m = MenuCatalog.Build(MenuWorlds.Default());
            var dock = new DockLayout();
            foreach (int[] s in Screens)
            {
                MenuLayout l = MenuLayout.Compute(s[0], s[1], m.Categories, metrics);
                MenuCategory comms = m.Category("comms");
                l.FillDock(dock, comms.Rows, false, 0, true, 1, false);
                string at = " @" + s[0] + "x" + s[1];
                float h0 = dock.Rect.H;
                int cap = dock.Cap;
                TestAssert.That(cap >= (s[1] >= 1080 ? 7 : 4), "COMMS shows at least 7 rows at 1080p" + at + " (" + cap + ")");
                if (s[1] == 720) TestAssert.That(cap == 7, "COMMS shows 7 rows at 720p, as the prototype's shot does: the three-heading case lands exactly on the limit" + at + " (" + cap + ")");
                TestAssert.That(dock.Overflow && dock.RailTrack.H > 0f && dock.ChipDown.W > 0f, "twelve calls overflow: rail and chips are drawn" + at);
                for (int off = 0; off <= comms.Rows.Count - cap; off++)
                {
                    l.FillDock(dock, comms.Rows, false, off, true, 1, false);
                    TestAssert.Near(dock.Rect.H, h0, "the dock keeps its height while scrolling" + at + " offset " + off);
                    TestAssert.That(dock.RailThumb.T >= dock.RailTrack.T - 0.01f && dock.RailThumb.B <= dock.RailTrack.B + 0.01f, "the rail thumb stays on its track" + at);
                }
                l.FillDock(dock, comms.Rows, false, 99, true, 1, false);
                TestAssert.Eq(dock.Offset, comms.Rows.Count - cap, "an offset past the end clamps" + at);
                l.FillDock(dock, m.Category("lights").Rows, false, 0, true, 1, false);
                TestAssert.That(!dock.Overflow && dock.RailTrack.H == 0f, "a short list draws no rail" + at);
            }
            MenuLayout l1080 = MenuLayout.Compute(1920, 1080, m.Categories, metrics);
            l1080.FillDock(dock, m.Category("comms").Rows, false, 0, true, 1, false);
            var ids = new List<string>();
            foreach (DockRow row in dock.Rows) ids.Add(m.Category("comms").Rows[row.Index].Id);
            TestAssert.That(ids.Contains("comms/ccopy") && ids.Contains("comms/cnegative"), "the first window holds TACTICAL and REPLY, so COPY and NEGATIVE need no scrolling");
        }

        private static void CorridorReachesTowardTheHubButNeverOverAPlate()
        {
            var metrics = new MenuTestMetrics();
            MenuModel m = MenuCatalog.Build(MenuWorlds.Default());
            var dock = new DockLayout();
            foreach (int[] s in Screens)
            {
                MenuLayout l = MenuLayout.Compute(s[0], s[1], m.Categories, metrics);
                foreach (string id in new[] { "flight", "defence", "weapons", "targets" })
                {
                    MenuCategory c = m.Category(id);
                    l.FillDock(dock, c.Rows, false, 0, true, MenuLayout.DockSide(c), false);
                    MenuRect p = l.PointerDock(dock);
                    string at = " " + id + " @" + s[0] + "x" + s[1];
                    TestAssert.That(dock.Side > 0 ? p.L < dock.Rect.L && p.R == dock.Rect.R : p.R > dock.Rect.R && p.L == dock.Rect.L, "the pointer dock reaches toward the hub" + at);
                    TestAssert.That(Math.Abs(p.W - dock.Rect.W) >= (s[1] >= 1080 ? 40f * l.Rho : 0f), "by at least the corridor" + at);
                    for (int i = 0; i < l.Count; i++) TestAssert.That(!p.Overlaps(l.Widest[i]), "and never over a label plate" + at);
                }
            }
        }

        private static void SectorPickHasAngularHysteresis()
        {
            var metrics = new MenuTestMetrics();
            MenuModel m = MenuCatalog.Build(MenuWorlds.Default());
            MenuLayout l = MenuLayout.Compute(1920, 1080, m.Categories, metrics);
            int flight = m.IndexOf("flight"), wing = m.IndexOf("wing");
            TestAssert.Eq(l.SectorPick(90f, -1), flight, "90 degrees is FLIGHT");
            TestAssert.Eq(l.SectorPick(108f + 3f, flight), flight, "3 degrees past the sector edge still FLIGHT (hysteresis)");
            TestAssert.Eq(l.SectorPick(108f + 9f, flight), wing, "9 degrees past the edge switches to WING");
            TestAssert.Eq(l.SectorPick(108f + 3f, -1), wing, "with nothing open the nearest category wins");
            TestAssert.Near(MenuLayout.AngleOf(0f, -10f), 0f, "straight up is 0 degrees");
            TestAssert.Near(MenuLayout.AngleOf(10f, 0f), 90f, "right is 90 degrees");
            TestAssert.Near(MenuLayout.AngleOf(0f, 10f), 180f, "down is 180 degrees");
            TestAssert.Near(MenuLayout.AngleOf(-10f, 0f), 270f, "left is 270 degrees");
            TestAssert.Near(MenuLayout.AngleDiff(350f, 10f), -20f, "angle difference wraps");
            TestAssert.Eq(l.DiscAt(l.DiscX[0], l.DiscY[0], 0f), 0, "a point on a disc picks it");
            TestAssert.Eq(l.DiscAt(l.Cx, l.Cy, 0f), -1, "the hub is no category");
            TestAssert.That(l.PlateContains(3, null, l.Widest[3].L + 2f, l.DiscY[3]), "a label plate picks its category too");
            TestAssert.That(!l.PlateContains(3, null, l.Widest[3].R - 1f, l.DiscY[3]), "the plate as drawn hugs its text: the air at the end of the widest plate is not the category");
        }

        private static void RowGeometryIsOrdered()
        {
            var metrics = new MenuTestMetrics();
            MenuModel m = MenuCatalog.Build(MenuWorlds.Default());
            MenuLayout l = MenuLayout.Compute(1920, 1080, m.Categories, metrics);
            var dock = new DockLayout();
            MenuCategory flight = m.Category("flight");
            l.FillDock(dock, flight.Rows, false, 0, true, 1, false);
            TestAssert.That(dock.RuleY > 0f, "EJECT sits below a rule");
            DockRow eject = dock.Rows[dock.Rows.Count - 1];
            TestAssert.That(dock.RuleY < eject.Rect.T && flight.Rows[eject.Index].Id == "flight/eject", "the rule is directly above the danger row");
            for (int i = 1; i < dock.Rows.Count; i++) TestAssert.That(dock.Rows[i].Rect.T >= dock.Rows[i - 1].Rect.B, "rows do not overlap");
            TestAssert.Near(dock.Rows[0].Rect.H, 58f, "a row is 58 px at 1080p");

            MenuRow select = m.Row("weapons/select");
            l.FillDock(dock, select.Children, true, 0, false, 1, false);
            TestAssert.That(dock.HasBack && dock.Rows[0].Index == -2, "a depth-3 list starts with the BACK row");
            TestAssert.That(!dock.HasFooter && dock.Footer.H == 0f, "no footer when the list asks for none");

            MenuCategory comms = m.Category("comms");
            l.FillDock(dock, comms.Rows, false, 0, true, 1, false);
            TestAssert.That(dock.Groups.Count >= 2 && dock.Groups[0].Text == "TACTICAL" && dock.Groups[1].Text == "REPLY", "group headings appear where the group changes");
            TestAssert.That(dock.Groups[0].Rect.B <= dock.Rows[0].Rect.T, "a heading sits above its first row");

            MenuModel noWing = MenuCatalog.Build(MenuWorlds.NoWing());
            l.FillDock(dock, noWing.Category("wing").Rows, false, 0, true, 1, true);
            TestAssert.That(dock.Empty && dock.Rows.Count == 0 && dock.Rect.H > 100f, "an unavailable category draws an empty body, no rows");
        }
    }
}
