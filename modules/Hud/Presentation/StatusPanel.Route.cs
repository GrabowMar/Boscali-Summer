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
    // The ROUTE slot (HUD zones, 2026-11-16): the left column's 281 x 133 face under the NOTICE line, showing the first live of DOCK, CARGO, OBJECTIVE (ScopePick.Route) as a compact caption shelf and five rows. It reads what the three
    // full pages read (ScopeFeeds, the pages' own number formats and rules) and draws none of their plots: DOCK keeps a small funnel dial, OBJECTIVE a small bearing dial, CARGO none. The pages stay polled every present, with the
    // board on or off (Present / PollRoutePages), so the slot sees them live in Auto and Manual alike. It lives in the cue tree's gate (HUD up, corners on, not suppressed), has its own root, follows the sway and redraws only when
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
        // The caption shelf's flag, rebuilt only when what it says changes.
        private string routeFlag, routeFlagTag;
        private int routeFlagKey = -1;
        private ScopePage routeFlagFace;

        /// <summary>The offline check reads these: the face drawn (when one is up), and where its top fell.</summary>
        internal bool RouteDrawn => routeDrawn;
        internal ScopePage RouteFaceDrawn => routeFaceDrawn;
        internal float RouteTop => routeTop;

        /// <summary>Which face is live, from what every page's last poll said.</summary>
        private void UpdateRoute()
        {
            int mask = 0;
            if (pageLive[(int)ScopePage.Dock]) mask |= CuePriority.Bit(ScopePage.Dock);
            if (pageLive[(int)ScopePage.Cargo]) mask |= CuePriority.Bit(ScopePage.Cargo);
            if (pageLive[(int)ScopePage.Objective]) mask |= CuePriority.Bit(ScopePage.Objective);
            routeLive = ScopePick.Route((ushort)mask, out routeFace);
        }

        /// <summary>With the board switched off nothing polls the pages: the three faces' pages still do, silently (the board's notices stay with the board).</summary>
        private void PollRoutePages(float clock)
        {
            if (pages == null) return;
            var silent = new ScopeEnv(clock, routeUnits, null);
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
        }

        private bool EnsureRouteTree()
        {
            if (routeRoot != null) return true;
            if (canvasRect == null) return false;
            routeRoot = CornerRoot("BoscaliRoute");
            var group = routeRoot.gameObject.AddComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
            group.interactable = group.blocksRaycasts = false;
            routeSurface = Pinned(routeRoot, "Route", false);
            routeVector = AvVector.Create(routeSurface, "Strokes", RouteQuads);
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
            bool ok, furniture = Options.Declutter == BoardDeclutter.Full;
            int sig;
            if (routeFace == ScopePage.Dock) { ok = ScopeFeeds.TryRead(out dock); sig = ok ? DockSig(dock) : 0; }
            else if (routeFace == ScopePage.Cargo) { ok = ScopeFeeds.TryRead(out cargo) && CargoManifestRules.Present(cargo); sig = ok ? CargoSig(cargo) : 0; }
            else { ok = ScopeFeeds.TryRead(out objective) && objective.Id != 0; sig = ok ? ObjectiveSig(objective) : 0; }
            if (!ok) { HideRoute(); return; }
            sig = unchecked(((sig * 31 + (int)routeFace) * 31 + (int)routeUnits) * 31 + (furniture ? 1 : 0));
            if (!moved && sig == routeSig) return;
            if (routeFaceDrawn != routeFace) routeFlagKey = -1;      // a face switch starts the flag over
            routeSig = sig;

            float canvasW = canvasRect.rect.width, canvasH = canvasRect.rect.height;
            Show(routeRoot, true);
            PlaceOnGrid(routeSurface, 0f, 0f, Whole(canvasW), Whole(canvasH));
            ScopeCanvas g = routeCanvas;
            g.Furniture = furniture;
            g.Begin(Palette(), baseUnit, Whole(canvasH), pixel);
            g.Origin(zone.X, top);      // everything below is in the slot's own units (RouteLayout)
            if (routeFace == ScopePage.Dock) PaintDock(g, dock);
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
            routeFlag = routeFlagTag = null;
            routeFlagKey = -1;
        }

        // ------------------------------------------------------------------------------------------------ shared parts

        private const float RowMid = RouteLayout.RowH * 0.5f;

        /// <summary>A row: the label in full ink at the left, the value (14 bold) and its unit (full ink) at the right edge of <paramref name="r"/>. The value is a scratch span: it is drawn by this very call.</summary>
        private static void RouteRow(ScopeCanvas g, in BoardRect r, string label, ReadOnlySpan<char> value, string unit, ScopeTone tone)
        {
            float y = r.Y + RowMid, x = r.Right;
            if (!string.IsNullOrEmpty(unit)) x -= g.Text(unit, r.Right, y, Ink, 12f, ScopeAlign.Right);
            g.Text(label, r.X, y, Ink);
            g.Text(value, x, y, tone, 14f, ScopeAlign.Right, true);
        }

        /// <summary>
        /// The caption shelf's flag of <paramref name="face"/>, built only when its inputs change (a face of its own key space): DOCK "TANKER · WORD" (<paramref name="a"/> cut to 14, <paramref name="b"/>), CARGO "WORD · n/m",
        /// OBJECTIVE "PH n/m" (<paramref name="n"/>, <paramref name="m"/>). Nothing is built before the key test.
        /// </summary>
        private string RouteFlagOf(ScopePage face, int key, string a, string b, int n, int m)
        {
            if (routeFlagFace != face || routeFlagKey != key || !string.Equals(routeFlagTag, a))
            {
                routeFlagFace = face;
                routeFlagKey = key;
                routeFlagTag = a;
                if (face == ScopePage.Objective) routeFlag = "PH " + n + "/" + m;
                else if (face == ScopePage.Cargo) routeFlag = a + " · " + n + "/" + m;
                else routeFlag = (a.Length > 14 ? a.Substring(0, 13) + "…" : a) + " · " + b;      // a long tanker name: the shelf has room for about 26 characters
            }
            return routeFlag;
        }

        // ------------------------------------------------------------------------------------------------ DOCK

        /// <summary>The numbers the face shows, a little finer than it shows them (feet are three to the metre; the probe moves a pixel per 0.6 m).</summary>
        private static int DockSig(in DockScope d)
        {
            unchecked
            {
                int h = (int)d.Phase;
                h = h * 31 + (int)Math.Round(d.RangeM * 3.3f);
                h = h * 31 + (int)Math.Round(DockPage.ConeOf(d) * 3.3f);
                h = h * 31 + (int)Math.Round(d.ClosureMps * 20f);
                h = h * 31 + (int)Math.Round(d.RelX * 4f);
                h = h * 31 + (int)Math.Round(d.RelY * 4f);
                h = h * 31 + (DockPage.OutOfFunnel(d) ? 1 : 0) + (DockPage.TooFastOf(d) ? 2 : 0);
                return h * 31 + (d.Tanker?.GetHashCode() ?? 0);
            }
        }

        /// <summary>DOCK: the funnel dial at the left, RANGE, FUNNEL, CLOSING and OFFSET beside it (FLOW has no source in <see cref="DockScope"/>).</summary>
        private void PaintDock(ScopeCanvas g, in DockScope d)
        {
            AvUnits u = routeUnits;
            string word = DockPage.Words[(int)d.Phase];
            string tanker = d.Tanker ?? "SKYWELL";
            g.CaptionShelf(0f, 0f, RouteLayout.W, "DOCK", RouteFlagOf(ScopePage.Dock, (int)d.Phase, tanker, word, 0, 0));
            bool bad = DockPage.OutOfFunnel(d), fast = DockPage.TooFastOf(d);
            DockDial(g, d, bad);
            float cone = DockPage.ConeOf(d), cv = DockPage.ClosureIn(d.ClosureMps, u);
            string len = DockPage.LengthUnitOf(u);
            RouteRow(g, RouteLayout.RowBesideDial(0), "RANGE", ScopeStr.Int(DockPage.LengthIn(d.RangeM, u)), len, Hi);
            RouteRow(g, RouteLayout.RowBesideDial(1), "FUNNEL", ScopeStr.Int(DockPage.LengthIn(cone, u)), len, bad ? Amber : Ink);
            RouteRow(g, RouteLayout.RowBesideDial(2), "CLOSING", ScopeStr.Cat("", cv, cv < 10f ? 1 : 0, null), DockPage.ClosureUnitOf(u), fast ? Amber : Hi);
            RouteRow(g, RouteLayout.RowBesideDial(3), "OFFSET", ScopeStr.Cat(d.RelX < 0f ? "L" : "R", Math.Abs(DockPage.LengthIn(d.RelX, u)), d.RelY < 0f ? " D" : " U", Math.Abs(DockPage.LengthIn(d.RelY, u)), null), len, bad ? Amber : Ink);
        }

        /// <summary>The dial: the metre rings' outer bound, the funnel ring at the page's scale, the contact point and the probe (the page's bullseye at half size, no silhouette, no directors).</summary>
        private static void DockDial(ScopeCanvas g, in DockScope d, bool bad)
        {
            const float R = 50f, S = R / DockMath.BullseyeR;
            float cx = RouteLayout.Dial * 0.5f, cy = RouteLayout.RowsTop + RouteLayout.Dial * 0.5f;
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

        // ------------------------------------------------------------------------------------------------ CARGO

        private static int CargoSig(in CargoScope d)
        {
            float t = CargoManifestRules.WindowSeconds(d);
            int window = float.IsNaN(t) ? -2 : float.IsPositiveInfinity(t) ? -1 : (int)Math.Ceiling(t);
            int flags = (d.Selected ? 1 : 0) | (d.Safe ? 2 : 0) | (d.Reloading ? 4 : 0) | (d.ZoneAvailable ? 8 : 0) | (d.UsesLandingZone ? 16 : 0) | (d.HasZone ? 32 : 0) | (d.HasReach ? 64 : 0);
            return unchecked(((((((flags * 31 + (int)d.Kind) * 31 + d.Ammo) * 31 + d.Max) * 31 + window) * 31 + (int)(d.DistanceM / 5f)) * 31 + (int)Math.Round(d.BearingDeg)) * 31 + (int)(d.ReachM / 10f));
        }

        /// <summary>CARGO: the light with its hold time (or why it holds), the distance numeral with its steer word, REACH (OBJ has no source in <see cref="CargoScope"/>).</summary>
        private void PaintCargo(ScopeCanvas g, in CargoScope d)
        {
            AvUnits u = routeUnits;
            g.CaptionShelf(0f, 0f, RouteLayout.W, "CARGO", RouteFlagOf(ScopePage.Cargo, ((int)d.Kind * 1000 + d.Ammo) * 1000 + d.Max, CargoManifestRules.KindWord(d.Kind), null, d.Ammo, d.Max));
            // Row 0: the jump light, and the seconds until the zone comes into reach, or what holds the drop.
            BoardRect r0 = RouteLayout.Row(0);
            float y = r0.Y + RowMid;
            CargoLight light = CargoManifestRules.Light(d);
            ScopeTone tone = light == CargoLight.Green ? Hi : light == CargoLight.Amber ? Amber : Red;
            g.Ring(8f, y, 6f, tone, 1f, 1.5f, tone, 0.9f);
            string lightWord = CargoManifestRules.LightWord(light);
            float wordW = g.Text(lightWord, 20f, y, tone, 14f, ScopeAlign.Left, true);
            bool cleared = CargoManifestRules.LoadCheck(d) == CargoCheck.Pass && CargoManifestRules.SafetyCheck(d) == CargoCheck.Pass;
            float t = CargoManifestRules.WindowSeconds(d);
            if (cleared && t > 0f && float.IsPositiveInfinity(t)) g.Text("NOT CLOSING", RouteLayout.W, y, Amber, 12f, ScopeAlign.Right);
            else if (cleared && t > 0f) g.Text(OpsScopeWords.Clock(t), RouteLayout.W, y, Hi, 16f, ScopeAlign.Right, true);
            else g.FitText(CargoManifestRules.Hint(d), RouteLayout.W, y, RouteLayout.W - 20f - wordW - 8f, light == CargoLight.Green ? Ink : tone, 12f, 12f, ScopeAlign.Right);

            // Rows 1 and 2: the distance numeral and the steer word.
            float ny = RouteLayout.Row(1).Y + RouteLayout.RowH;
            if (d.HasZone)
            {
                bool inside = CargoManifestRules.InRange(d);
                g.Text(ScopeStr.Distance("", d.DistanceM, u, 8), 0f, ny, inside ? Hi : Ink, 22f, ScopeAlign.Left, true);
                int off = (int)Math.Round(Math.Abs(d.BearingDeg));
                if (off <= 3) g.Text("ON THE NOSE", RouteLayout.W, ny, inside ? Hi : Ink, 14f, ScopeAlign.Right, true);
                else g.Text(ScopeStr.Cat(d.BearingDeg > 0f ? "STEER R " : "STEER L ", off, "°"), RouteLayout.W, ny, off > 90 ? Amber : Ink, 14f, ScopeAlign.Right, true);
            }
            else if (!d.UsesLandingZone || !d.ZoneAvailable) g.Text("NATIVE AIM", 0f, ny, Ink, 14f, ScopeAlign.Left, true);
            else g.Text("SET DROP ZONE", 0f, ny, Amber, 14f, ScopeAlign.Left, true);

            // Row 3: how far a release from here reaches.
            BoardRect r3 = RouteLayout.Row(3);
            if (d.HasZone && d.HasReach) RouteRow(g, r3, "REACH", ScopeStr.Distance("", d.ReachM, u, 8), null, CargoManifestRules.InRange(d) ? Hi : Ink);
            else RouteRow(g, r3, "REACH", "--", null, Dim);
        }

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

        /// <summary>OBJECTIVE: the contract's title, the distance numeral with the deadline and a small bearing dial, the nearest air-defence ring, the zone and what the hold needs (the deadline stands in for ETA: the scope has no travel time).</summary>
        private void PaintObjective(ScopeCanvas g, in ObjectiveScope d)
        {
            AvUnits u = routeUnits;
            g.CaptionShelf(0f, 0f, RouteLayout.W, "OBJECTIVE", d.PhaseCount > 0 ? RouteFlagOf(ScopePage.Objective, d.Current * 1024 + d.PhaseCount, null, null, d.Current + 1, d.PhaseCount) : null);
            g.FitText(d.Title, 0f, RouteLayout.Row(0).Y + RowMid, RouteLayout.W, Hi, 14f, 12f, ScopeAlign.Left, true);

            // Rows 1 and 2: the distance to the zone's edge (its centre once inside), the deadline and the bearing dial.
            float ny = RouteLayout.Row(1).Y + RouteLayout.RowH, own = ObjectiveMath.Distance(d.OwnX, d.OwnZ, d.X, d.Z);
            bool inZone = own <= d.Radius;
            const float DialX = RouteLayout.W - 20f;
            if (d.HasMarker)
            {
                g.Text(ScopeStr.Distance("", inZone ? own : own - d.Radius, u, 8), 0f, ny, Hi, 22f, ScopeAlign.Left, true);
                float rel = ObjectiveMath.Relative(ObjectiveMath.Bearing(d.X - d.OwnX, d.Z - d.OwnZ), d.OwnHeading);
                g.Ring(DialX, ny, 15f, Faint);
                g.Line(DialX, ny - 17f, DialX, ny - 13f, Dim);
                ScopeCanvas.Polar(rel, 13f, out float tx, out float ty);
                g.Line(DialX, ny, DialX + tx, ny + ty, Hi);
                g.Chevron(DialX + tx, ny + ty, rel, 5f, Hi, true);
                g.Ring(DialX, ny, 2f, Ink, 1f, 1f, Ink, 1f);
                if (inZone) g.Ring(DialX, ny, 19f, Hi, 0.7f);
            }
            else g.Text(ObjectiveMath.LineWord(ObjectiveLine.ContactLost), 0f, ny, Amber, 16f, ScopeAlign.Left, true);
            if (d.Total > 0f && d.Left > 0f)
            {
                float rx = DialX - 24f;
                bool low = ObjectiveMath.DeadlineLow(d.Left);
                float cw = g.Text(OpsScopeWords.Clock(d.Left), rx, ny, low ? Amber : Hi, 16f, ScopeAlign.Right, true);
                g.Text("DUE", rx - cw - 6f, ny, Ink, 12f, ScopeAlign.Right);
            }

            // Row 3: the nearest ring and whether the pilot stands in its reach.
            BoardRect r3 = RouteLayout.Row(3);
            float y3 = r3.Y + RowMid;
            int ring = NearestRing(d);
            if (ring < 0)
            {
                g.Text("AIR DEF", 0f, y3, Ink);
                g.Text("NONE KNOWN", RouteLayout.W, y3, Ink, 12f, ScopeAlign.Right);
            }
            else
            {
                SamExposure e = ObjectiveMath.Expose(d.Rings[ring], d.OwnX, d.OwnZ, d.OwnAgl);
                g.Text(ObjectiveMath.Kind(d.Rings[ring].Kind), 0f, y3, Ink);
                g.Text(e.Inside ? "EXPOSED" : "MASKED", 72f, y3, e.Inside ? Amber : Ink, 12f, ScopeAlign.Left, e.Inside);
                g.Text(ScopeStr.Distance("", e.DistanceM, u, 8), RouteLayout.W, y3, e.Inside ? Amber : Ink, 12f, ScopeAlign.Right);
            }

            // Row 4: the zone's radius, and what keeps the hold from counting (or the phase's progress).
            float y4 = RouteLayout.Row(4).Y + RowMid;
            float zw = g.Text(ScopeStr.Distance("ZONE R", d.Radius, u, 14), 0f, y4, Ink);
            ObjectiveLine line = ObjectiveMath.Line(d);
            string word = ObjectiveMath.LineWord(line);
            if (word != null && line != ObjectiveLine.ContactLost) g.FitText(word, RouteLayout.W, y4, RouteLayout.W - zw - 12f, Amber, 12f, 12f, ScopeAlign.Right, true);
            else if (line == ObjectiveLine.Progress) g.Text(ScopeStr.Pct((int)Math.Round(ObjectiveMath.Progress(d) * 100f)), RouteLayout.W, y4, Ink, 12f, ScopeAlign.Right);
            else if (inZone && d.HasMarker) g.Text("IN ZONE", RouteLayout.W, y4, Hi, 12f, ScopeAlign.Right, true);
        }
    }
}
