using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    /// <summary>The widest font the prototype was verified with: B612 Mono, 0.627 em per glyph plus the design tracking.</summary>
    internal sealed class MenuTestMetrics : IMenuMetrics
    {
        public float Width(string text, MenuFont font) => (text ?? "").Length * (0.627f + MenuFonts.Tracking(font)) * MenuFonts.Size(font);
    }

    /// <summary>
    /// The prototype's geometry matrix as one reusable check over a model and a layout: ring clears the heading tape, no plate or readout
    /// overlaps, every dock (and every depth-3 list) inside the screen with air to the ring, hub readout never clips, leader ties disc to dock.
    /// The unit tests run it with the fixed-advance test font; the offline Unity check runs it again with the real TMP metrics.
    /// Pure (Domain types only) and free of the test framework so the Unity project can compile it: results go through <c>ok</c>.
    /// </summary>
    internal static class MenuGeometryChecks
    {
        public static void All(Action<bool, string> ok, MenuModel m, MenuLayout l, IMenuMetrics metrics)
        {
            string at = " @" + l.W + "x" + l.H;
            Ring(ok, m, l, at);
            Chrome(ok, m, l, at);
            Docks(ok, m, l, metrics, at);
            Readout(ok, m, l, metrics, at);
            Leader(ok, m, l, at);
        }

        private static void Ring(Action<bool, string> ok, MenuModel m, MenuLayout l, string at)
        {
            float minTop = float.MaxValue;
            for (int i = 0; i < l.Count; i++) minTop = Math.Min(minTop, Math.Min(l.Widest[i].T, l.Disc(i).T));
            ok(minTop >= 300f * l.G - 1f, "ring clears the heading tape" + at + " (" + minTop + ")");
            for (int a = 0; a < l.Count; a++)
                for (int b = a + 1; b < l.Count; b++) ok(!l.Widest[a].Overlaps(l.Widest[b]), "ring plates " + a + "/" + b + " overlap" + at);
            for (int i = 0; i < l.Count; i++)
                for (int j = 0; j < l.Count; j++)
                    if (i != j) ok(!l.Widest[j].Overlaps(l.Disc(i), 2f), "plate " + j + " covers disc " + i + at);
            for (int i = 0; i < l.Count; i++)
            {
                MenuRect one = l.Plate(i, null), two = l.Plate(i, m.Categories[i].Badge);
                ok(one.W <= l.Widest[i].W + 0.5f && two.W <= l.Widest[i].W + 0.5f, "a hugging plate is never wider than the widest" + at);
                ok(two.H > one.H, "the badge line makes the plate taller" + at);
            }
        }

        private static void Chrome(Action<bool, string> ok, MenuModel m, MenuLayout l, string at)
        {
            for (int i = 0; i < l.Count; i++)
            {
                ok(!l.Readout.Overlaps(l.Disc(i), 1f), "hub readout clear of disc " + i + at);
                ok(!l.Readout.Overlaps(l.Widest[i], 1f), "hub readout clear of plate " + i + at);
            }
            float air = Math.Min(l.DiscX[m.IndexOf("wing")] - l.Rd - l.Readout.R, l.Readout.L - (l.DiscX[m.IndexOf("support")] + l.Rd));
            ok(air >= 18f * l.Rho - 1f, "readout keeps 18 px of air to the WING / SUPPORT discs" + at + " (" + air + ")");
            foreach (MenuRect r in new[] { l.Hint, l.Title })
                ok(r.L >= 0f && r.R <= l.W && r.T >= 0f && r.B <= l.H, "hint bar and title tab inside the screen" + at);
            ok(l.ToastTop + 40f * l.T <= l.H, "toast inside the screen" + at);
            ok(l.Title.B < l.Widest[m.IndexOf("flight")].T, "the title tab sits above the ring" + at);
        }

        private static void Docks(Action<bool, string> ok, MenuModel m, MenuLayout l, IMenuMetrics metrics, string at0)
        {
            var dock = new DockLayout();
            float top = (float)Math.Round(346f * l.G);
            foreach (MenuCategory c in m.Categories)
            {
                var lists = new List<KeyValuePair<string, List<MenuRow>>> { new KeyValuePair<string, List<MenuRow>>(c.Id, c.Rows) };
                foreach (MenuRow r in c.Rows) if (r.Kind == MenuKind.Submenu) lists.Add(new KeyValuePair<string, List<MenuRow>>(r.Id, r.Children));
                foreach (var entry in lists)
                {
                    bool sub = entry.Key != c.Id;
                    l.FillDock(dock, entry.Value, sub, 0, true, MenuLayout.DockSide(c), c.Rows.Count == 0);
                    string at = " " + entry.Key + at0;
                    MenuRect d = dock.Rect;
                    ok(d.L >= 15f && d.T >= 0f && d.R <= l.W - 15f && d.B <= l.H - l.BotMargin + 1f, "dock inside the screen and above the bottom margin" + at + " " + d.L + "," + d.T + "," + d.R + "," + d.B);
                    ok(Math.Abs(d.T - top) < 0.01f, "every dock starts at the same fixed HUD-safe top" + at);
                    for (int j = 0; j < l.Count; j++)
                    {
                        ok(!d.Overlaps(l.Widest[j]) && !d.Overlaps(l.Disc(j)), "dock never covers a ring disc or label" + at + " vs " + j);
                        ok(!new MenuRect(d.L - 14f, d.T, d.R + 14f, d.B).Overlaps(l.Widest[j]), "dock keeps 14 px of air to every ring label" + at + " vs " + j);
                    }
                    MenuRect hint = MenuRect.Of(l.Hint.L + l.HintShift(dock), l.Hint.T, l.Hint.W, l.Hint.H);
                    ok(!hint.Overlaps(d) && hint.L >= 0f && hint.R <= l.W, "the hint bar yields sideways instead of touching the dock" + at);
                    bool listIconless = c.Iconless || (sub && c.Rows.Find(x => x.Id == entry.Key).Iconless);
                    foreach (DockRow row in dock.Rows)
                    {
                        ok(row.Rect.T >= d.T && row.Rect.B <= d.B && row.Rect.L >= d.L && row.Rect.R <= d.R, "row inside the dock" + at);
                        if (row.Index < 0) continue;
                        MenuRow r = entry.Value[row.Index];
                        float room = l.RowTextWidth(listIconless, l.RowRightWidth(r));
                        float lw = metrics.Width(r.Label, MenuFont.Row) * l.T, sw = metrics.Width(r.State, MenuFont.State) * l.T;
                        ok(lw <= room + 0.5f, "row label fits" + at + ": " + r.Label + " " + lw + " > " + room);
                        ok(sw <= room + 0.5f, "row state fits" + at + ": " + r.State + " " + sw + " > " + room);
                    }
                }
            }
        }

        private static void Readout(Action<bool, string> ok, MenuModel m, MenuLayout l, IMenuMetrics metrics, string at)
        {
            var texts = new List<KeyValuePair<string, string>>();
            foreach (MenuCategory c in m.Categories)
            {
                foreach (MenuRow r in c.Rows)
                {
                    Add(texts, r);
                    if (r.Children != null) foreach (MenuRow k in r.Children) Add(texts, k);
                }
                texts.Add(new KeyValuePair<string, string>("BACK", "BACK TO " + c.Label));
            }
            var bad = new List<string>();
            float room = l.Readout.H - (l.ReadoutCompact ? 0f : l.ReadoutNameH + 3f * l.T);
            float width = l.Readout.W - 2f * ((l.ReadoutCompact ? 4f : 6f) + 1f);
            foreach (var tx in texts)
            {
                ReadoutFit fit = MenuReadoutFit.Fit(l, metrics, tx.Key, tx.Value);
                int maxLines = l.ReadoutCompact && fit.TitleLines == 1 ? 3 : 2;
                int lines = MenuReadoutFit.Lines(tx.Value, width, metrics, MenuFont.State, l.T);
                if (fit.TitleLines > 2 || lines > maxLines || fit.Height > room + 0.5f)
                    bad.Add(tx.Key + " / " + tx.Value + " (" + lines + " lines, " + fit.Height + " > " + room + ")");
            }
            ok(bad.Count == 0, "hub readout clips" + at + ": " + string.Join(" | ", bad));
            // the status summary (nothing focused) flows by measured width and fits the plate
            var cells = MenuReadoutFit.Flow(m.Summary, width, metrics, l.T, out int summaryLines);
            float gridH = summaryLines * MenuReadoutFit.SummaryLine(l.T) + Math.Max(0, summaryLines - 1) * MenuReadoutFit.SummaryGap * l.T;
            ok(gridH + 12f * l.T <= room + 0.5f || gridH <= room - 8f * l.T, "the status summary fits the hub plate" + at + " (" + summaryLines + " lines, " + gridH + " in " + room + ")");
            foreach (SummaryCell cell in cells) ok(cell.X >= -0.5f && cell.X + cell.KeyW <= width + 0.5f, "a summary cell stays inside the plate" + at + ": " + cell.Key);
        }

        private static void Add(List<KeyValuePair<string, string>> texts, MenuRow r)
        {
            texts.Add(new KeyValuePair<string, string>(MenuHint.RowLabel(r), r.Desc));
            if (r.IsBlocked) texts.Add(new KeyValuePair<string, string>(MenuHint.RowLabel(r), r.BlockedText));
        }

        private static void Leader(Action<bool, string> ok, MenuModel m, MenuLayout l, string at0)
        {
            var dock = new DockLayout();
            var pts = new float[MenuLeader.MaxPoints * 2];
            for (int ci = 0; ci < m.Categories.Count; ci++)
            {
                MenuCategory c = m.Categories[ci];
                if (!c.Available) continue;
                int side = MenuLayout.DockSide(c);
                l.FillDock(dock, c.Rows, false, 0, true, side, false);
                int n = MenuLeader.Compute(l, ci, c, dock, l.Readout.T + 87f * l.T, pts);
                string at = " " + c.Id + at0;
                ok(n >= 4 && n <= MenuLeader.MaxPoints, "leader has 4 to 6 points" + at);
                ok(pts[2] == l.DiscX[ci] && pts[3] == l.DiscY[ci], "the leader passes through the selected disc" + at);
                ok(Math.Abs(pts[(n - 1) * 2] - (side > 0 ? dock.Rect.L : dock.Rect.R)) < 0.01f, "the leader ends on the dock edge" + at);
                float lastY = pts[(n - 1) * 2 + 1];
                ok(lastY >= dock.Rect.T && lastY <= dock.Rect.B, "at a height the dock covers" + at);
                for (int i = 2; i < n; i++) ok((pts[i * 2] - pts[(i - 1) * 2]) * side >= -0.01f, "after the disc the leader only travels toward the dock" + at);
            }
        }
    }
}
