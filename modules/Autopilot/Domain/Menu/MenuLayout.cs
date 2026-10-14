using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Autopilot.Domain
{
    /// <summary>The six type roles of the menu (design sizes at 1080p; the layout scales them by <see cref="MenuLayout.T"/>).</summary>
    internal enum MenuFont : byte { Plate, Badge, Row, State, Header, Hint, Title, Group }

    internal static class MenuFonts
    {
        /// <summary>Line pitch of every multi-line text, as a multiple of the font size (the prototype's CSS 1.2). The TMP label pool forces it with lineSpacing.</summary>
        public const float LinePitch = 1.2f;

        /// <summary>Design size in pixels at 1080p.</summary>
        public static float Size(MenuFont f)
        {
            switch (f)
            {
                case MenuFont.Plate: return 15f;
                case MenuFont.Badge: return 13f;
                case MenuFont.Row: return 17f;
                case MenuFont.State: return 13f;
                case MenuFont.Header: return 20f;
                case MenuFont.Group: return 13f;
                default: return 15f;
            }
        }

        /// <summary>Letter spacing in em.</summary>
        public static float Tracking(MenuFont f)
        {
            switch (f)
            {
                case MenuFont.Plate: return 0.05f;
                case MenuFont.Badge: return 0.04f;
                case MenuFont.Row: return 0.03f;
                case MenuFont.State: return 0.04f;
                case MenuFont.Header: return 0.08f;
                case MenuFont.Title: return 0.14f;
                case MenuFont.Group: return 0.16f;
                default: return 0.04f;
            }
        }
    }

    /// <summary>Text width at design size (scale 1), tracking included. The Unity side measures with TMP; tests use a fixed-advance font.</summary>
    internal interface IMenuMetrics
    {
        float Width(string text, MenuFont font);
    }

    /// <summary>Screen rectangle, pixels, origin top-left, y down.</summary>
    internal readonly struct MenuRect
    {
        public MenuRect(float l, float t, float r, float b) { L = l; T = t; R = r; B = b; }
        public float L { get; }
        public float T { get; }
        public float R { get; }
        public float B { get; }
        public float W => R - L;
        public float H => B - T;
        public bool Contains(float x, float y) => x >= L && x < R && y >= T && y < B;
        public bool Overlaps(MenuRect o, float margin = 0f) => L < o.R - margin && o.L < R - margin && T < o.B - margin && o.T < B - margin;
        public static MenuRect Of(float l, float t, float w, float h) => new MenuRect(l, t, l + w, t + h);
    }

    internal struct DockRow { public int Index; public MenuRect Rect; }

    internal struct DockGroup { public string Text; public MenuRect Rect; }

    /// <summary>One dock list laid out: where the header, group headings, rows, rule, chips, rail and footer are. Filled in place by <see cref="MenuLayout.FillDock"/>.</summary>
    internal sealed class DockLayout
    {
        public MenuRect Rect, Header, Footer, ChipUp, ChipDown, RailTrack, RailThumb;
        public int Side, Cap, Offset, Count;
        public bool Overflow, HasFooter, Empty;
        /// <summary>Y of the rule above the danger group, or -1.</summary>
        public float RuleY = -1f;
        public readonly List<DockRow> Rows = new List<DockRow>(10);
        public readonly List<DockGroup> Groups = new List<DockGroup>(4);
        /// <summary>BACK row rect, or default when the list has none.</summary>
        public bool HasBack;
    }

    /// <summary>
    /// Pure geometry of the v7 ring and dock for one screen size, ported from the prototype's layout().
    /// All numbers are design pixels at 1080p times a scale: <see cref="G"/> raw screen scale, <see cref="Rho"/> ring (floor .72),
    /// <see cref="T"/> text (floor .93), <see cref="Lg"/> dock geometry (floor .8). Origin top-left, y down.
    /// </summary>
    internal sealed class MenuLayout
    {
        public const float SectorDeg = 18f, HysteresisDeg = 5f, DeadPx = 45f, LockIn = 0.85f, LockOut = 0.6f;
        public const float RowH = 58f, BackH = 46f, HeaderH = 46f, FooterH = 30f, GroupH = 24f, GroupMargin = 3f, ListPad = 6f;
        public const int MaxCap = 8, MinCap = 4;
        /// <summary>How far past a row or the dock the pointer may stray and still count (a group heading is this tall).</summary>
        public const float SlopPx = 24f;
        /// <summary>How far outside a ring disc or the hub the cursor still counts as on it (design px, times <see cref="Rho"/>): with the 29 px disc the
        /// zones of two neighbours just meet (their centres are 121 px apart), so the whole ring band is somebody's.</summary>
        public const float DiscSlopPx = 30f;

        public readonly int W, H, Count;
        public readonly float G, Rho, T, Lg;
        public readonly float Cx, Cy, R, Rd, HubR, Gap, HudTop, BotMargin, DockW, KeyCapSize;
        public readonly float[] DiscX, DiscY, PlateOne, PlateTwo, Anchor;
        public readonly int[] Side;
        public readonly MenuRect[] Widest, KeyCap;
        public readonly float ExtL, ExtR;
        /// <summary>The bottom console: the help bar on the screen edge and, on top of it, the focused action's name and description.</summary>
        public readonly MenuRect Action, Hint;
        public readonly float ToastTop;

        private readonly IMenuMetrics metrics;
        private readonly string[] labels;
        private readonly float[] angles;

        private MenuLayout(int w, int h, IReadOnlyList<MenuCategory> cats, IMenuMetrics m)
        {
            W = w; H = h; metrics = m; Count = cats.Count;
            G = h / 1080f;
            Rho = Math.Max(G, 0.72f); T = Math.Max(G, 0.93f); Lg = Math.Max(G, 0.8f);
            R = (float)Math.Round(196f * Rho); Rd = (float)Math.Round(29f * Rho); HubR = (float)Math.Round(46f * Rho);
            Cx = w / 2f;
            double cos18 = Math.Cos(18.0 * Math.PI / 180.0);
            Cy = (float)Math.Round(Math.Max(0.4815 * h, 300 * G + R * cos18 + Rd + 2));
            HudTop = (float)Math.Round(346f * G); BotMargin = (float)Math.Round(40f * G);
            DockW = (float)Math.Round(384f * T); Gap = (float)Math.Round(34f * Rho);
            KeyCapSize = (float)Math.Round(20f * T);

            DiscX = new float[Count]; DiscY = new float[Count]; PlateOne = new float[Count]; PlateTwo = new float[Count]; Anchor = new float[Count];
            Side = new int[Count]; Widest = new MenuRect[Count]; KeyCap = new MenuRect[Count];
            labels = new string[Count]; angles = new float[Count];
            float extR = 0f, extL = w;
            float badgeMax = m.Width("MMMMMMMM", MenuFont.Badge) * T;
            for (int i = 0; i < Count; i++)
            {
                labels[i] = cats[i].Label; angles[i] = cats[i].Angle;
                double th = cats[i].Angle * Math.PI / 180.0;
                int side = Math.Sin(th) > 0 ? 1 : -1;
                float x = (float)Math.Round(Cx + R * Math.Sin(th)), y = (float)Math.Round(Cy - R * Math.Cos(th));
                bool up = Math.Cos(th) >= -1e-9;
                float ph1 = 2f * (float)Math.Round(15f * T), ph2 = 2f * (float)Math.Round(21.5f * T);
                float pw = (float)Math.Ceiling(Math.Max(m.Width(cats[i].Label, MenuFont.Plate) * T, badgeMax) + 18f * T);
                float pad = (float)Math.Round(6f * Rho);
                float anchor = side > 0 ? x + Rd + pad : x - Rd - pad;
                float pl = side > 0 ? anchor : anchor - pw;
                DiscX[i] = x; DiscY[i] = y; Side[i] = side; PlateOne[i] = ph1; PlateTwo[i] = ph2; Anchor[i] = anchor;
                Widest[i] = MenuRect.Of(pl, y - ph2 / 2f, pw, ph2);
                KeyCap[i] = MenuRect.Of((float)Math.Round(x - side * Rd - KeyCapSize / 2f), (float)Math.Round(y + (up ? -1 : 1) * Rd - KeyCapSize / 2f), KeyCapSize, KeyCapSize);
                if (side > 0) extR = Math.Max(extR, pl + pw); else extL = Math.Min(extL, pl);
            }
            ExtR = extR; ExtL = extL;

            float roTop = (float)Math.Round(Cy + HubR + 5f * Rho);

            // the help bar sits on the bottom edge of the screen, under the dock's bottom margin, out of the way of the ring
            float hintW = (124f + 396f + 256f) * T, hintH = 32f * T;
            Hint = MenuRect.Of(Cx - hintW / 2f, (float)Math.Round(h - hintH - 4f * G), hintW, hintH);
            float actionH = (float)Math.Round(46f * T);
            Action = MenuRect.Of(Hint.L, Hint.T - actionH + 1f, hintW, actionH); // shares the help bar's top rule
            ToastTop = (float)Math.Round(roTop + 4f * Rho);
        }

        public static MenuLayout Compute(int w, int h, IReadOnlyList<MenuCategory> cats, IMenuMetrics m) => new MenuLayout(w, h, cats, m);

        // ---------------------------------------------------------------- ring

        /// <summary>The plate as drawn: hugs its own text. <paramref name="badge"/> null = single line.</summary>
        public MenuRect Plate(int i, string badge)
        {
            bool two = badge != null;
            float pw = (float)Math.Ceiling(Math.Max(metrics.Width(labels[i], MenuFont.Plate) * T, two ? metrics.Width(badge, MenuFont.Badge) * T : 0f) + 18f * T);
            float ph = two ? PlateTwo[i] : PlateOne[i];
            float l = Side[i] > 0 ? Anchor[i] : Anchor[i] - pw;
            return MenuRect.Of(l, DiscY[i] - ph / 2f, pw, ph);
        }

        public bool PlateContains(int i, string badge, float x, float y) => Plate(i, badge).Contains(x, y);

        /// <summary>The plate of a ring slot in the controller wheel: a row's label over its state line, hugging the text.</summary>
        public MenuRect SlotPlate(int i, string label, string state)
        {
            float pw = (float)Math.Ceiling(Math.Max(metrics.Width(label, MenuFont.Plate), metrics.Width(state ?? "", MenuFont.Badge)) * T + 18f * T);
            float l = Side[i] > 0 ? Anchor[i] : Anchor[i] - pw;
            return MenuRect.Of(l, DiscY[i] - PlateTwo[i] / 2f, pw, PlateTwo[i]);
        }

        public MenuRect Disc(int i) => new MenuRect(DiscX[i] - Rd, DiscY[i] - Rd, DiscX[i] + Rd, DiscY[i] + Rd);

        /// <summary>How far past the outer rim of the ring discs the Voronoi zones reach (design px, times <see cref="Rho"/>).</summary>
        public const float RingSlopPx = 70f;

        /// <summary>Voronoi pick: anywhere in the band from the hub's zone out to <see cref="RingSlopPx"/> past the discs belongs to the nearest disc, or -1.</summary>
        public int DiscAt(float x, float y)
        {
            float dx = x - Cx, dy = y - Cy, r2 = dx * dx + dy * dy;
            float inner = HubR + DiscSlopPx * Rho, outer = R + Rd + RingSlopPx * Rho;
            if (r2 <= inner * inner || r2 > outer * outer) return -1;
            int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < Count; i++)
            {
                double d = Math.Sqrt((x - DiscX[i]) * (x - DiscX[i]) + (y - DiscY[i]) * (y - DiscY[i]));
                if (d <= bd) { bd = d; best = i; }
            }
            return best;
        }

        public static float AngleOf(float dx, float dy) => (float)(((Math.Atan2(dx, -dy) * 180.0 / Math.PI) + 360.0) % 360.0);

        public static float AngleDiff(float a, float b) => (float)((((a - b + 540.0) % 360.0) + 360.0) % 360.0 - 180.0);

        /// <summary>Sector pick with angular hysteresis: the open category keeps the pointer until it is 5 degrees past its edge.</summary>
        public int SectorPick(float angle, int current)
        {
            if (current >= 0 && Math.Abs(AngleDiff(angle, angles[current])) <= SectorDeg + HysteresisDeg) return current;
            int best = -1; float bd = 999f;
            for (int i = 0; i < Count; i++)
            {
                float d = Math.Abs(AngleDiff(angle, angles[i]));
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        // ---------------------------------------------------------------- dock

        public static int DockSide(MenuCategory c) => c.Angle < 180f ? 1 : -1;

        public float DockX(int side) => side > 0 ? Math.Min(ExtR + Gap, W - 16f - DockW) : Math.Max(ExtL - Gap - DockW, 16f);

        private static int HeadersIn(IReadOnlyList<MenuRow> list, int offset, int cap)
        {
            int n = 0; string last = null;
            for (int i = offset; i < Math.Min(list.Count, offset + cap); i++)
            {
                string g = list[i].Group;
                if (g != null && g != last) n++;
                last = g;
            }
            return n;
        }

        private static bool AnyDanger(IReadOnlyList<MenuRow> list, int offset, int cap)
        {
            for (int i = offset; i < Math.Min(list.Count, offset + cap); i++) if (list[i].Kind == MenuKind.Danger) return true;
            return false;
        }

        /// <summary>How many rows the dock shows: as many as fit between the HUD-safe top and the bottom margin, 4..8.</summary>
        public int Cap(IReadOnlyList<MenuRow> list, bool withBack)
        {
            int n = list.Count;
            float avail = H - HudTop - BotMargin;
            float chrome = (HeaderH + 12f + FooterH) * Lg + 2f + (withBack ? BackH * Lg : 0f) + (AnyDanger(list, 0, n) ? 13f * Lg : 0f);
            for (int c = MaxCap; c > MinCap; c--)
            {
                int hd = 0;
                for (int o = 0; o <= Math.Max(0, n - c); o++) hd = Math.Max(hd, HeadersIn(list, o, c));
                if (chrome + hd * (GroupH + GroupMargin) * Lg + Math.Min(n, c) * RowH * Lg <= avail + 0.5f) return c; // half a pixel: 720p lands exactly on the limit
            }
            return MinCap;
        }

        /// <summary>Width left for a row's label and state line.</summary>
        public float RowTextWidth(bool iconless, float rightWidth)
        {
            float inner = DockW - 2f - 12f * Lg;
            return inner - 5f * T - 22f * T - 9f * T - (iconless ? 0f : 45f * T) - (rightWidth > 0f ? 9f * T + rightWidth : 0f) - 10f * T;
        }

        /// <summary>Width of what a row draws at its right edge: ping pictograph, then checkbox / pip / chevron.</summary>
        public float RowRightWidth(MenuRow r)
        {
            float w = 0f;
            if (r.Ping != null) w += 18f * T;
            float tail = r.Kind == MenuKind.Submenu ? 14f * T : r.Kind == MenuKind.Picker ? 14f * T
                : r.Kind == MenuKind.Toggle ? (r.ArmedPip ? 14f * T : r.Checkbox ? 18f * T : 0f) : 0f;
            if (tail > 0f) w += (w > 0f ? 8f * T : 0f) + tail;
            return w;
        }

        /// <summary>Lays out one dock list. <paramref name="empty"/> draws the unavailable-category body instead of rows.</summary>
        public void FillDock(DockLayout d, IReadOnlyList<MenuRow> list, bool withBack, int offset, bool footer, int side, bool empty)
        {
            d.Rows.Clear(); d.Groups.Clear();
            d.Side = side; d.HasFooter = footer; d.Empty = empty; d.HasBack = withBack && !empty; d.RuleY = -1f;
            d.Footer = default; d.ChipUp = default; d.ChipDown = default; d.RailTrack = default; d.RailThumb = default;
            int n = empty ? 0 : list.Count;
            int cap = empty ? MinCap : Cap(list, withBack);
            offset = Math.Max(0, Math.Min(offset, Math.Max(0, n - cap)));
            d.Cap = cap; d.Offset = offset; d.Count = n; d.Overflow = n > cap;
            int k = Math.Min(n - offset, cap), hdrs = empty ? 0 : HeadersIn(list, offset, cap);
            bool danger = !empty && AnyDanger(list, offset, cap);
            int maxHdrs = hdrs;
            if (d.Overflow) for (int o = 0; o <= n - cap; o++) maxHdrs = Math.Max(maxHdrs, HeadersIn(list, o, cap));

            float h = 2f + HeaderH * Lg + 2f * ListPad * Lg + (footer ? FooterH * Lg : 0f);
            if (empty) h += 100f * T;
            else
            {
                h += (withBack ? BackH * Lg : 0f) + maxHdrs * (GroupH + GroupMargin) * Lg + (danger ? 13f * Lg : 0f) + k * RowH * Lg;
            }
            float left = DockX(side);
            float top = HudTop + h > H - BotMargin ? Math.Max(16f, H - BotMargin - h) : HudTop;
            d.Rect = MenuRect.Of(left, top, DockW, h);
            d.Header = MenuRect.Of(left + 1f, top + 1f, DockW - 2f, HeaderH * Lg - 1f);
            float innerL = left + 1f + ListPad * Lg, innerW = DockW - 2f - 2f * ListPad * Lg;
            float y = top + 1f + HeaderH * Lg + ListPad * Lg;
            float listTop = y - ListPad * Lg;
            if (!empty)
            {
                if (withBack) { d.Rows.Add(new DockRow { Index = -2, Rect = MenuRect.Of(innerL, y, innerW, BackH * Lg) }); y += BackH * Lg; }
                string last = null; bool ruled = false;
                for (int i = offset; i < offset + k; i++)
                {
                    string g = list[i].Group;
                    if (g != null && g != last)
                    {
                        d.Groups.Add(new DockGroup { Text = g, Rect = MenuRect.Of(innerL, y, innerW, GroupH * Lg) });
                        y += (GroupH + GroupMargin) * Lg;
                    }
                    last = g;
                    if (list[i].Kind == MenuKind.Danger && !ruled) { d.RuleY = y + 6f * Lg; y += 13f * Lg; ruled = true; }
                    d.Rows.Add(new DockRow { Index = i, Rect = MenuRect.Of(innerL, y, innerW, RowH * Lg) });
                    y += RowH * Lg;
                }
                y += (maxHdrs - hdrs) * (GroupH + GroupMargin) * Lg;
            }
            else y += 100f * T;
            float listBottom = y + ListPad * Lg;
            if (footer) d.Footer = MenuRect.Of(left + 1f, listBottom, DockW - 2f, FooterH * Lg - 1f);
            if (d.Overflow)
            {
                float cw = 6f * T, ch = 22f * T, cy = top + 1f + 10f * Lg;
                float wUp = metrics.Width("▲" + offset, MenuFont.Badge) * T + cw, wDn = metrics.Width("▼" + Math.Max(0, n - offset - cap), MenuFont.Badge) * T + cw;
                float right = left + DockW - 1f - 14f * T;
                d.ChipDown = MenuRect.Of(right - wDn, cy, wDn, ch);
                d.ChipUp = MenuRect.Of(right - wDn - wUp, cy, wUp, ch);
                float trackT = listTop + ListPad * Lg, trackH = listBottom - ListPad * Lg - trackT;
                d.RailTrack = MenuRect.Of(left + DockW - 1f - 4f, trackT, 2f, trackH);
                d.RailThumb = MenuRect.Of(left + DockW - 1f - 4f, trackT + trackH * offset / n, 2f, trackH * cap / n);
            }
        }

        /// <summary>The dock as the pointer sees it: widened toward the hub across the dead gap to the label plates (never over a plate),
        /// and by <see cref="SlopPx"/> on its other three sides so an overshoot stays in the list.</summary>
        public MenuRect PointerDock(DockLayout d)
        {
            float room = d.Side > 0 ? d.Rect.L - ExtR : ExtL - d.Rect.R;
            float m = Math.Max(0f, Math.Min(Gap - 10f * Rho, room)), s = SlopPx * Lg;
            return d.Side > 0 ? new MenuRect(d.Rect.L - m, d.Rect.T - s, d.Rect.R + s, d.Rect.B + s) : new MenuRect(d.Rect.L - s, d.Rect.T - s, d.Rect.R + m, d.Rect.B + s);
        }

        /// <summary>How far the bottom console must slide sideways so it never touches an open dock.</summary>
        public float HintShift(DockLayout d)
        {
            if (d == null || !(d.Rect.T < Hint.B && d.Rect.B > Action.T)) return 0f;
            return d.Side > 0 ? Math.Min(0f, d.Rect.L - 12f - Hint.R) : Math.Max(0f, d.Rect.R + 12f - Hint.L);
        }
    }
}
