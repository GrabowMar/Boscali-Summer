using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The ROUTE slot (HUD zones, 2026-11-16): the left column's 281 x 133 face under the NOTICE line, showing the first live of APPROACH, DOCK, CARGO, OBJECTIVE (ScopePick.Route) as a compact card: the page header's shelf (its caption, and the page's headline as the status), four rows and a bar line. It reads what the four
    // full pages read (ScopeFeeds, the pages' own number formats and rules) and draws none of their plots: APPROACH has a small ILS cross, DOCK a small funnel dial, OBJECTIVE a small bearing dial, CARGO none. The board shows none
    // of the four (2026-11-16): this face is ROUTE. The pages stay polled every present, with the board on or off (Present / PollRoutePages), so the slot sees them live either way. It lives in the cue tree's gate (HUD up, corners on, not suppressed), has its own root, follows the sway and redraws only when
    // the numbers it shows, its place or the canvas moved (a signature, checked at the 10 Hz tick). Rows with no source in the scope (DOCK FLOW, CARGO OBJ, OBJECTIVE ETA) are not drawn.
    internal sealed partial class StatusPanel
    {
        private const int RouteQuads = 480;

        private RectTransform routeRoot, routeSurface;
        private AvVector routeVector;
        private ScopeText routeText;
        private ScopeCanvas routeCanvas;
        /// <summary>The first live face as of the last present, and whether there is one.</summary>
        private ScopePage routeFace;
        private bool routeLive;
        private AvUnits routeUnits;
        /// <summary>What the last paint drew: whether it is up, the face, its top, the canvas size and the signature of the values.</summary>
        private bool routeDrawn;
        private ScopePage routeFaceDrawn;
        private float routeTop;
        private Vector2 routeCanvasSeen;
        private int routeSig;

        /// <summary>The offline check reads these: the face drawn (when one is up), and where its top fell.</summary>
        internal bool RouteDrawn => routeDrawn;
        internal ScopePage RouteFaceDrawn => routeFaceDrawn;
        internal float RouteTop => routeTop;

        /// <summary>Which face is live, from what every page's last poll said.</summary>
        private void UpdateRoute()
        {
            int mask = 0;
            if (pageLive[(int)ScopePage.Approach]) mask |= CuePriority.Bit(ScopePage.Approach);
            if (pageLive[(int)ScopePage.Dock]) mask |= CuePriority.Bit(ScopePage.Dock);
            if (pageLive[(int)ScopePage.Cargo]) mask |= CuePriority.Bit(ScopePage.Cargo);
            if (pageLive[(int)ScopePage.Objective]) mask |= CuePriority.Bit(ScopePage.Objective);
            routeLive = ScopePick.Route((ushort)mask, out routeFace);
        }

        /// <summary>With the board switched off nothing polls the pages: the four faces' pages still do, silently (the board's notices stay with the board).</summary>
        private void PollRoutePages(float clock)
        {
            if (pages == null) return;
            var silent = new ScopeEnv(clock, routeUnits, null);
            pageLive[(int)ScopePage.Approach] = PollPage(pages[(int)ScopePage.Approach], silent);
            pageLive[(int)ScopePage.Dock] = PollPage(pages[(int)ScopePage.Dock], silent);
            pageLive[(int)ScopePage.Cargo] = PollPage(pages[(int)ScopePage.Cargo], silent);
            pageLive[(int)ScopePage.Objective] = PollPage(pages[(int)ScopePage.Objective], silent);
            UpdateRoute();
        }

        /// <summary>Every frame (<paramref name="tick"/> false) and at the end of every present (true), with the corners in charge, after the cue stack: place the face under the NOTICE line and draw it when its values moved.</summary>
        private void RouteFrame(bool tick)
        {
            if (!routeLive || canvasRect == null) { HideRoute(); return; }
            float canvasW = canvasRect.rect.width;
            ZoneRect zone = HudZones.Ref(HudZone.Route, canvasW / baseUnit);
            // A fixed home: the zone's top. The NOTICE line yields to it (PaintCues, HudZones.NoticeFits); the slot never slides.
            float top = Mathf.Round(zone.Y);
            Vector2 canvasSize = canvasRect.rect.size;
            bool moved = !routeDrawn || routeFaceDrawn != routeFace || top != routeTop || canvasSize != routeCanvasSeen;
            if (!tick && !moved) return;
            try { PaintRoute(zone, top, canvasSize, moved); }
            catch (Exception e)
            {
                StandDownRoute();
                BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Hud.Route", e);
            }
        }

        private void HideRoute()
        {
            if (routeRoot != null) Show(routeRoot, false);
            routeDrawn = false;
        }

        /// <summary>Nothing of the slot is up (corners off, or a paint failed).</summary>
        private void StandDownRoute()
        {
            HideRoute();
            routeSig = 0;
            routeLive = false;
        }

        private bool EnsureRouteTree()
        {
            if (routeRoot != null) return true;
            if (canvasRect == null) return false;
            routeRoot = CornerRoot("BoscaliRoute");
            routeGroup = routeRoot.gameObject.AddComponent<CanvasGroup>();
            routeGroup.ignoreParentGroups = true;
            routeGroup.interactable = routeGroup.blocksRaycasts = false;
            routeSurface = Pinned(routeRoot, "Route", false);
            routeVector = AvVector.Create(routeSurface, "Strokes", RouteQuads, BoscaliSummer.Core.Contracts.HudGlow.Stroke, BoscaliSummer.Core.Contracts.HudGlow.Alpha);
            routeText = new ScopeText(Holder(routeSurface, "Words"), font, fontMaterial);
            routeCanvas = new ScopeCanvas(routeVector.Buffer, routeText, "scope.route");
            PlaceCornerRoots();
            return true;
        }

        private void PaintRoute(in ZoneRect zone, float top, Vector2 canvasSize, bool moved)
        {
            if (!EnsureRouteTree()) return;
            DockScope dock = default;
            CargoScope cargo = default;
            ObjectiveScope objective = default;
            ApproachScope approach = default;
            bool ok, furniture = Options.Declutter == BoardDeclutter.Full;
            int sig;
            if (routeFace == ScopePage.Approach) { ok = ScopeFeeds.TryRead(out approach); sig = ok ? ApproachSig(approach) : 0; }
            else if (routeFace == ScopePage.Dock) { ok = ScopeFeeds.TryRead(out dock); sig = ok ? DockSig(dock) : 0; }
            else if (routeFace == ScopePage.Cargo) { ok = ScopeFeeds.TryRead(out cargo) && CargoManifestRules.Present(cargo); sig = ok ? CargoSig(cargo) : 0; }
            else { ok = ScopeFeeds.TryRead(out objective) && objective.Id != 0; sig = ok ? ObjectiveSig(objective) : 0; }
            if (!ok) { HideRoute(); return; }
            // The shelf's status is the face's page headline (refit 5.4); its beat counts while it flashes (and on DOCK's ENGAGE, whose call box pulses).
            ScopeHeadline hl = HeadlineOf(pages[(int)routeFace]);
            int beat = (int)(Time.unscaledTime * FlashHz * 2f) & 1;
            bool beats = hl.Flash && (hl.Tone == Amber || hl.Tone == Red) || routeFace == ScopePage.Dock && dock.Phase == DockPhase.Engage;
            sig = unchecked(((((sig * 31 + (int)routeFace) * 31 + (int)routeUnits) * 31 + (furniture ? 1 : 0)) * 31 + (hl.Text?.GetHashCode() ?? 0)) * 31 + (int)hl.Tone * 4 + (beats ? beat + 1 : 0));
            if (!moved && sig == routeSig) return;
            routeSig = sig;

            float canvasW = canvasRect.rect.width, canvasH = canvasRect.rect.height;
            Show(routeRoot, true);
            PlaceOnGrid(routeSurface, 0f, 0f, Whole(canvasW), Whole(canvasH));
            ScopeCanvas g = routeCanvas;
            g.Furniture = furniture;
            g.Begin(Palette(), baseUnit, Whole(canvasH), pixel);
            g.Origin(zone.X, top);      // everything below is in the slot's own units (RouteLayout)
            HeaderShelf(g, RouteLayout.W, RouteCaption(routeFace), hl.Text, hl.Tone, hl.Flash, beat == 0);
            if (routeFace == ScopePage.Approach) PaintApproach(g, approach);
            else if (routeFace == ScopePage.Dock) PaintDock(g, dock, beat);
            else if (routeFace == ScopePage.Cargo) PaintCargo(g, cargo);
            else PaintObjective(g, objective);
            g.End();
            routeVector.Commit();
            routeDrawn = true;
            routeFaceDrawn = routeFace;
            routeTop = top;
            routeCanvasSeen = canvasSize;
        }

        private void DestroyRoute()
        {
            if (routeRoot != null) UnityEngine.Object.Destroy(routeRoot.gameObject);
            routeRoot = routeSurface = null;
            routeVector = null;
            routeText = null;
            routeCanvas = null;
            routeDrawn = routeLive = false;
            routeSig = 0;
        }

        // ------------------------------------------------------------------------------------------------ shared parts

        private const float RowMid = RouteLayout.RowH * 0.5f;

        /// <summary>The card's caption (refit 5.4, design call D6): what the shelf says before the page's headline.</summary>
        private static string RouteCaption(ScopePage face) => face == ScopePage.Approach ? "APP" : face == ScopePage.Dock ? "DOCK" : face == ScopePage.Cargo ? "DROP" : "OBJ";

        /// <summary>
        /// A row: the label (LABEL DIM) at the left of <paramref name="r"/>, the value (BODY bold in <paramref name="tone"/>) and its unit (LABEL DIM, 4 after it) at the right edge. The value and the unit may be scratch spans:
        /// they are drawn by this very call.
        /// </summary>
        private static void RouteRow(ScopeCanvas g, in BoardRect r, string label, ReadOnlySpan<char> value, ReadOnlySpan<char> unit, ScopeTone tone)
        {
            float y = r.Y + RowMid, x = r.Right;
            if (!unit.IsEmpty) x -= g.Text(unit, r.Right, y, Dim, HudType.Label, ScopeAlign.Right) + 4f;
            g.Text(label, r.X, y, Dim, HudType.Label);
            g.Text(value, x, y, tone, HudType.Body, ScopeAlign.Right, true);
        }

        /// <summary>A <see cref="RouteRow"/> of a distance in the player's units, its figure and its unit apart.</summary>
        private static void RouteDistance(ScopeCanvas g, in BoardRect r, string label, float metres, AvUnits u, ScopeTone tone)
        {
            ReadOnlySpan<char> s = ScopeStr.Distance("", metres, u, 8);
            int k = Figure(s);
            RouteRow(g, r, label, s.Slice(0, k), s.Slice(k), tone);
        }

        /// <summary>How many leading characters of <paramref name="s"/> are its figure (digits, '.', ',' and '-'): the rest is its unit.</summary>
        private static int Figure(ReadOnlySpan<char> s)
        {
            int k = 0;
            while (k < s.Length && (char.IsDigit(s[k]) || s[k] == '.' || s[k] == ',' || s[k] == '-')) k++;
            return k;
        }

        /// <summary>The card's one hero figure: <paramref name="s"/>'s figure EMPHASIS bold in <paramref name="tone"/> at (<paramref name="x"/>, middle <paramref name="y"/>), its unit LABEL DIM 4 after it. Returns the width.</summary>
        private static float Hero(ScopeCanvas g, float x, float y, ReadOnlySpan<char> s, ScopeTone tone)
        {
            int k = Figure(s);
            float w = g.Text(s.Slice(0, k), x, y, tone, HudType.Emphasis, ScopeAlign.Left, true);
            if (k < s.Length) w += 4f + g.Text(s.Slice(k), x + w + 4f, y, Dim, HudType.Label);
            return w;
        }

        // ------------------------------------------------------------------------------------------------ APPROACH

        /// <summary>Pixels per dot of the ILS cross: two dots either side of the centre, the scales' ends 2.6 dots out (<see cref="ApproachMath.LocDots"/>).</summary>
        private const float ApproachDot = 20f;

        private static int ApproachSig(in ApproachScope d)
        {
            unchecked
            {
                int h = (int)d.Stage * 4 + (d.Pad ? 1 : 0) + (d.HasIls ? 2 : 0);
                h = h * 31 + (int)Math.Round(ApproachMath.LocDots(d.LocDeg) * ApproachDot);      // a pixel of the diamond
                h = h * 31 + (int)Math.Round(ApproachMath.GsDots(d.GsDeg) * ApproachDot);
                h = h * 31 + (int)(d.DistanceM / 20f);
                h = h * 31 + (int)Math.Round(d.SinkMps * 10f);
                h = h * 31 + (d.Pad ? (int)Math.Round(d.PadX) * 31 + (int)Math.Round(d.PadY) : 0);
                return h * 31 + ((d.Runway ?? d.Field)?.GetHashCode() ?? 0);
            }
        }

        /// <summary>
        /// APPROACH, mostly a picture: the ILS cross at the left, and beside it the runway (or pad), the distance to the threshold (the hero), the sink rate, and a bar that fills as the distance closes (from 10 km; a pad
        /// from 2 km: the INTERCEPT stage's own scale). The shelf's status is the page's headline (GEAR, FAST, a deviation, else the stage).
        /// </summary>
        private void PaintApproach(ScopeCanvas g, in ApproachScope d)
        {
            AvUnits u = routeUnits;
            ApproachCross(g, d);
            BoardRect r0 = RouteLayout.RowBesideDial(0);
            g.FitText(d.Runway ?? d.Field ?? (d.Pad ? "PAD" : "RUNWAY"), r0.X, r0.Y + RowMid, r0.W, Ink, HudType.Body, HudType.Label);
            Hero(g, r0.X, RouteLayout.RowBesideDial(1).Y + RowMid, ScopeStr.Distance("", d.DistanceM, u, 8), Hi);
            bool imperial = u == AvUnits.Imperial;
            RouteRow(g, RouteLayout.RowBesideDial(2), "SINK", ScopeStr.Cat("", AvUnitTable.Climb(d.SinkMps, u), imperial ? 0 : 1, null), imperial ? "FPM" : "M/S", d.SinkMps > ApproachMath.SinkCautionMps ? Amber : Ink);
            g.Bar(r0.X, RouteLayout.BarY, r0.W, 3f, ApproachMath.StageProgress(ApproachStage.Intercept, d.Pad, d.DistanceM, 0f, 0f, 0f, 0f), Ink);
        }

        /// <summary>
        /// The cross: the centre ring is the aircraft, the diamond where the path lies against it (right of and below the centre when the aircraft is left and high, as the old page's funnel had it), amber once a
        /// deviation is a caution. Over a pad the mark is the pad itself, nose up, the cross's arm standing for the distance left (never less than 50 m). In the flare and on the roll-out there is no beam to show.
        /// </summary>
        private static void ApproachCross(ScopeCanvas g, in ApproachScope d)
        {
            const float R = 52f;
            float cx = RouteLayout.Dial * 0.5f, cy = RouteLayout.DialTop + RouteLayout.Dial * 0.5f;
            g.Line(cx - R, cy, cx + R, cy, Faint);
            g.Line(cx, cy - R, cx, cy + R, Faint);
            for (int k = -2; k <= 2; k++)
            {
                if (k == 0) continue;
                g.Ring(cx + k * ApproachDot, cy, 2f, Dim);
                g.Ring(cx, cy + k * ApproachDot, 2f, Dim);
            }
            g.Ring(cx, cy, 4f, Hi);
            if (d.Pad)
            {
                float k = (R - 6f) / Math.Max(50f, d.DistanceM);
                g.Box(Mathf.Round(cx + d.PadX * k) - 5f, Mathf.Round(cy - d.PadY * k) - 5f, 11f, 11f, Hi);
            }
            else if (d.HasIls)
            {
                bool off = ApproachMath.LocCaution(d.LocDeg) || ApproachMath.GsCaution(d.GsDeg);
                g.Diamond(cx - ApproachMath.LocDots(d.LocDeg) * ApproachDot, cy + ApproachMath.GsDots(d.GsDeg) * ApproachDot, 6f, off ? Amber : Hi, true);
            }
        }

        // ------------------------------------------------------------------------------------------------ DOCK

        /// <summary>The numbers the face shows, a little finer than it shows them (feet are three to the metre; the probe moves a pixel per 0.6 m).</summary>
        private static int DockSig(in DockScope d)
        {
            unchecked
            {
                int h = (int)d.Phase * 2 + (d.ServiceZone ? 1 : 0);
                h = h * 31 + (int)Math.Round(d.RangeM * 3.3f);
                h = h * 31 + (int)Math.Round(DockPage.ConeOf(d) * 3.3f);
                h = h * 31 + (int)Math.Round(d.ClosureMps * 20f);
                h = h * 31 + (int)Math.Round(d.RelX * 4f);
                h = h * 31 + (int)Math.Round(d.RelY * 4f);
                h = h * 31 + (int)Math.Round(d.Fuel * 100f);
                h = h * 31 + (DockPage.OutOfFunnel(d) ? 1 : 0) + (DockPage.TooFastOf(d) ? 2 : 0);
                return h * 31 + (d.Tanker?.GetHashCode() ?? 0);
            }
        }

        /// <summary>
        /// DOCK: the funnel dial at the left (the funnel's one home), and beside it the tanker, the range (the hero; the fuel taken while it transfers), CLOSING and OFFSET. On ENGAGE the last row is the call to hold, an
        /// outline pulsing on <paramref name="beat"/> (refit C6); while it transfers a bar fills toward the target (FLOW has no source in <see cref="DockScope"/>).
        /// </summary>
        private void PaintDock(ScopeCanvas g, in DockScope d, int beat)
        {
            AvUnits u = routeUnits;
            bool bad = DockPage.OutOfFunnel(d), fast = DockPage.TooFastOf(d), fuel = d.Phase == DockPhase.Transfer || d.Phase == DockPhase.Served;
            DockDial(g, d, bad);
            float cv = DockPage.ClosureIn(d.ClosureMps, u);
            string len = DockPage.LengthUnitOf(u);
            BoardRect r0 = RouteLayout.RowBesideDial(0), r3 = RouteLayout.RowBesideDial(3);
            g.FitText(d.Tanker ?? "SKYWELL", r0.X, r0.Y + RowMid, r0.W, Ink, HudType.Body, HudType.Label);
            float hy = RouteLayout.RowBesideDial(1).Y + RowMid;
            if (fuel) Hero(g, r0.X, hy, ScopeStr.Pct((int)Math.Round(d.Fuel * 100f)), Hi);
            else Hero(g, r0.X, hy, ScopeStr.Cat("", DockPage.LengthIn(d.RangeM, u), len), Hi);
            RouteRow(g, RouteLayout.RowBesideDial(2), "CLOSING", ScopeStr.Cat("", cv, cv < 10f ? 1 : 0, null), DockPage.ClosureUnitOf(u), fast ? Amber : Ink);
            if (d.Phase == DockPhase.Engage)
            {
                // The call box (refit C6): an outline in HI pulsing on the beat, no fill.
                string call = d.ServiceZone ? "HOLD STEADY" : "HOLD BRAKE";
                float cw = g.Text(call, r3.X + 5f, r3.Y + RowMid, Hi, HudType.Value, ScopeAlign.Left, true);
                g.Box(r3.X, r3.Y + RowMid - 13f, cw + 10f, 26f, Hi, beat == 0 ? 1f : 0.4f);
            }
            else RouteRow(g, r3, "OFFSET", ScopeStr.Cat(d.RelX < 0f ? "L" : "R", Math.Abs(DockPage.LengthIn(d.RelX, u)), d.RelY < 0f ? " D" : " U", Math.Abs(DockPage.LengthIn(d.RelY, u)), null), len, bad ? Amber : Ink);
            if (fuel)
            {
                g.Bar(r0.X, RouteLayout.BarY, r0.W, 3f, d.Fuel, Hi);
                g.BarTick(r0.X, RouteLayout.BarY, r0.W, 3f, d.FuelTarget, Dim);
            }
        }

        /// <summary>The dial: the metre rings' outer bound, the funnel ring at the page's scale, the contact point and the probe (the page's bullseye at half size, no silhouette, no directors).</summary>
        private static void DockDial(ScopeCanvas g, in DockScope d, bool bad)
        {
            const float R = 50f, S = R / DockMath.BullseyeR;
            float cx = RouteLayout.Dial * 0.5f, cy = RouteLayout.DialTop + RouteLayout.Dial * 0.5f;
            g.Ring(cx, cy, R, Dim, 0.85f);
            g.Line(cx - R, cy, cx + R, cy, Faint, 1f, 1f, 2f, 4f);
            g.Line(cx, cy - R, cx, cy + R, Faint, 1f, 1f, 2f, 4f);
            ScopeTone ft = bad ? Amber : Ink;
            g.Ring(cx, cy, DockMath.ConePx(DockPage.ConeOf(d)) * S, ft, 0.95f, 1f, ft, 0.04f);
            g.Ring(cx, cy, 4f, Hi, 0.9f);
            float dx = d.RelX * DockMath.PxPerM * S, dy = -d.RelY * DockMath.PxPerM * S, len = (float)Math.Sqrt(dx * dx + dy * dy), lim = R - 3f;
            bool off = len > lim;
            if (off) { dx *= lim / len; dy *= lim / len; }
            ScopeTone dn = bad ? Amber : Hi;
            if (off) g.Ring(cx + dx, cy + dy, 3f, Amber); else g.Ring(cx + dx, cy + dy, 3f, dn, 1f, 1f, dn, 1f);
        }

        // ------------------------------------------------------------------------------------------------ CARGO (DROP)

        private static int CargoSig(in CargoScope d)
        {
            float t = CargoManifestRules.WindowSeconds(d);
            int window = float.IsNaN(t) ? -2 : float.IsPositiveInfinity(t) ? -1 : (int)Math.Ceiling(t);
            int flags = (d.Selected ? 1 : 0) | (d.Safe ? 2 : 0) | (d.Reloading ? 4 : 0) | (d.ZoneAvailable ? 8 : 0) | (d.UsesLandingZone ? 16 : 0) | (d.HasZone ? 32 : 0) | (d.HasReach ? 64 : 0);
            return unchecked(((((((flags * 31 + (int)d.Kind) * 31 + d.Ammo) * 31 + d.Max) * 31 + window) * 31 + (int)(d.DistanceM / 5f)) * 31 + (int)Math.Round(d.BearingDeg)) * 31 + (int)(d.ReachM / 10f));
        }

        /// <summary>DROP, full width (glyphs at 8, words at 22): the light with what holds the drop and the seconds until the zone comes into reach, the distance (the hero) with its steer, REACH while known, and the load.</summary>
        private void PaintCargo(ScopeCanvas g, in CargoScope d)
        {
            const float TextX = 22f, W = RouteLayout.W;
            AvUnits u = routeUnits;
            // Row 0: the jump light, the hint, and the seconds until the zone comes into reach (or that it is not closing).
            float y = RouteLayout.Row(0).Y + RowMid;
            CargoLight light = CargoManifestRules.Light(d);
            ScopeTone lt = light == CargoLight.Green ? Hi : light == CargoLight.Amber ? Amber : Red;
            g.Ring(8f, y, 6f, lt, 1f, 1.5f, lt, 0.9f);
            bool cleared = CargoManifestRules.LoadCheck(d) == CargoCheck.Pass && CargoManifestRules.SafetyCheck(d) == CargoCheck.Pass;
            float t = CargoManifestRules.WindowSeconds(d), rw = 0f;
            if (cleared && t > 0f && float.IsPositiveInfinity(t)) rw = g.Text("NOT CLOSING", W, y, Amber, HudType.Label, ScopeAlign.Right, true);
            else if (cleared && t > 0f) rw = g.Text(OpsScopeWords.Clock(t), W, y, Hi, HudType.Value, ScopeAlign.Right, true);
            g.FitText(CargoManifestRules.Hint(d), TextX, y, W - TextX - rw - (rw > 0f ? 8f : 0f), Ink, HudType.Body, HudType.Label);

            // Row 1: the distance and the steer to the zone.
            if (d.HasZone)
            {
                float ny = RouteLayout.Row(1).Y + RowMid;
                Hero(g, 0f, ny, ScopeStr.Distance("", d.DistanceM, u, 8), CargoManifestRules.InRange(d) ? Hi : Ink);
                int off = (int)Math.Round(Math.Abs(d.BearingDeg));
                ScopeTone st = off > 90 ? Amber : Ink;
                float dw = g.Text(ScopeStr.Cat("", off, "°"), W, ny, st, HudType.Body, ScopeAlign.Right, true);
                g.Chevron(W - dw - 10f, ny, d.BearingDeg, 5f, st, true);
            }

            // Row 2: how far a release from here reaches, while it is known.
            if (d.HasZone && d.HasReach) RouteDistance(g, Gutter(RouteLayout.Row(2), TextX), "REACH", d.ReachM, u, CargoManifestRules.InRange(d) ? Hi : Ink);

            // Row 3: the load.
            RouteRow(g, Gutter(RouteLayout.Row(3), TextX), CargoManifestRules.KindWord(d.Kind), ScopeStr.Cat("", d.Ammo, "/", d.Max, null), default, Ink);
        }

        private static BoardRect Gutter(in BoardRect r, float x) => new BoardRect(r.X + x, r.Y, r.W - x, r.H);

        // ------------------------------------------------------------------------------------------------ OBJECTIVE

        /// <summary>The nearest known air-defence ring of any kind to the pilot, -1 when none.</summary>
        private static int NearestRing(in ObjectiveScope d)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < d.RingCount && d.Rings != null && i < d.Rings.Length; i++)
            {
                float dist = ObjectiveMath.Distance(d.OwnX, d.OwnZ, d.Rings[i].X, d.Rings[i].Z);
                if (dist < bestD) { bestD = dist; best = i; }
            }
            return best;
        }

        private static int ObjectiveSig(in ObjectiveScope d)
        {
            int ring = NearestRing(d), threat = 0;
            if (ring >= 0)
            {
                SamExposure e = ObjectiveMath.Expose(d.Rings[ring], d.OwnX, d.OwnZ, d.OwnAgl);
                threat = ((int)d.Rings[ring].Kind * 2 + (e.Inside ? 1 : 0)) * 4096 + (int)(e.DistanceM / 10f) + 1;
            }
            float own = ObjectiveMath.Distance(d.OwnX, d.OwnZ, d.X, d.Z);
            return unchecked(((((((((d.Id * 31 + d.Current) * 31 + d.PhaseCount) * 31 + (int)ObjectiveMath.Line(d)) * 31 + (int)Math.Round(ObjectiveMath.Progress(d) * 100f)) * 31 + (int)(own / 5f)) * 31 +
                (int)Math.Round(ObjectiveMath.Relative(ObjectiveMath.Bearing(d.X - d.OwnX, d.Z - d.OwnZ), d.OwnHeading) / 3f)) * 31 + (int)Math.Ceiling(d.Left)) * 31 + (int)(d.Radius / 10f)) * 31 + threat);
        }

        /// <summary>
        /// OBJECTIVE: the contract's title, the distance (the hero) with the deadline and a small bearing dial (its ring is the in-zone home), the nearest known air-defence ring, the zone and the hold's progress (a bar on the
        /// bar line). What keeps the hold from counting is the shelf's status (the page's headline). The deadline stands in for ETA: the scope has no travel time.
        /// </summary>
        private void PaintObjective(ScopeCanvas g, in ObjectiveScope d)
        {
            const float W = RouteLayout.W, DialX = W - 20f;
            AvUnits u = routeUnits;
            g.FitText(d.Title, 0f, RouteLayout.Row(0).Y + RowMid, W - 44f, Ink, HudType.Body, HudType.Label);

            // Row 1: the distance to the zone's edge (its centre once inside), the deadline and the bearing dial.
            float ny = RouteLayout.Row(1).Y + RowMid, own = ObjectiveMath.Distance(d.OwnX, d.OwnZ, d.X, d.Z);
            bool inZone = own <= d.Radius;
            if (d.HasMarker)
            {
                Hero(g, 0f, ny, ScopeStr.Distance("", inZone ? own : own - d.Radius, u, 8), Hi);
                float rel = ObjectiveMath.Relative(ObjectiveMath.Bearing(d.X - d.OwnX, d.Z - d.OwnZ), d.OwnHeading);
                g.Ring(DialX, ny, 15f, Faint);
                g.Line(DialX, ny - 17f, DialX, ny - 13f, Dim);
                ScopeCanvas.Polar(rel, 13f, out float tx, out float ty);
                g.Line(DialX, ny, DialX + tx, ny + ty, Hi);
                g.Chevron(DialX + tx, ny + ty, rel, 5f, Hi, true);
                g.Ring(DialX, ny, 2f, Ink, 1f, 1f, Ink, 1f);
                if (inZone) g.Ring(DialX, ny, 19f, Hi, 0.7f);
            }
            if (d.Total > 0f && d.Left > 0f)
            {
                float rx = DialX - 24f;
                bool low = ObjectiveMath.DeadlineLow(d.Left);
                float cw = g.Text(OpsScopeWords.Clock(d.Left), rx, ny, low ? Amber : Hi, HudType.Value, ScopeAlign.Right, true);
                g.Text("DUE", rx - cw - 4f, ny, Dim, HudType.Label, ScopeAlign.Right);
            }

            // Row 2: the nearest known ring and how far it is (AMBER inside its reach).
            int ring = NearestRing(d);
            if (ring >= 0)
            {
                SamExposure e = ObjectiveMath.Expose(d.Rings[ring], d.OwnX, d.OwnZ, d.OwnAgl);
                RouteDistance(g, RouteLayout.Row(2), ObjectiveMath.Kind(d.Rings[ring].Kind), e.DistanceM, u, e.Inside ? Amber : Ink);
            }

            // Row 3: the zone's radius, and the hold's progress (its bar on the bar line).
            float y3 = RouteLayout.Row(3).Y + RowMid;
            float zw = g.Text("ZONE", 0f, y3, Dim, HudType.Label);
            g.Text(ScopeStr.Distance("", d.Radius, u, 8), zw + 6f, y3, Ink, HudType.Body);
            if (ObjectiveMath.Line(d) == ObjectiveLine.Progress)
            {
                float progress = ObjectiveMath.Progress(d);
                g.Text(ScopeStr.Pct((int)Math.Round(progress * 100f)), W, y3, Ink, HudType.Body, ScopeAlign.Right, true);
                g.Bar(0f, RouteLayout.BarY, W, 3f, progress, Hi);
            }
        }
    }
}
