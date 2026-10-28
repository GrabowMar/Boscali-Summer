using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// CARGO / AIRDROP, the drop computer for a carried load (a HALO stick, troops, a glider, cargo). Left, the drop plot, heading up with the aircraft in the middle: the zone at its real bearing and range,
    /// the reach of a release from here as a ring (solid once the zone is inside it), the steer line and a steer word; a zone past the rim rides it. Right: the jump light (RED holds the drop, AMBER: no zone,
    /// the load follows its designation, GREEN: clear to drop) with its reason or the seconds until the zone comes into reach, the focused load with its stick drawn (fireteams of four jumpers, gliders,
    /// crates), the release checklist that says why the light is not green, the height above the zone and the ground speed, and the other loads aboard. It reports and never commands: the load is chosen
    /// in WEAPONS and released with the native fire control. Fed by <see cref="CargoScope"/> (<c>CargoFeed</c>).
    /// </summary>
    internal sealed class CargoPage : ScopePageBase
    {
        // The plot (its middle and rim) and the right-hand column.
        private const float CX = 104f, CY = 166f, R = 96f, X0 = 222f, Right = 432f;
        private const float LightY = 46f, ReasonY = 64f, LoadY = 94f, StickY = 116f, CheckY = 146f, CheckPitch = 18f, FiguresY = 228f, RowPitch = 18f;
        private const float RuleLoad = 78f, RuleChecks = 130f, RuleFigures = 212f, Bottom = 290f;

        private CargoScope d;
        private bool live;
        private string status = "";
        private ScopeTone statusTone = Hi;

        public override ScopePage Id => ScopePage.Cargo;

        public override string Title => "CARGO";

        public override string IdleWord => "NO CARGO";

        public override string IdleHint => "NO TROOPS, CARGO OR GLIDERS ABOARD";

        public override bool Poll(in ScopeEnv env)
        {
            live = ScopeFeeds.TryRead(out d) && CargoManifestRules.Present(d);
            if (!live) return false;
            status = CargoManifestRules.Status(d);
            statusTone = CargoManifestRules.Light(d) == CargoLight.Green ? Hi : Amber;
            return true;
        }

        public override ScopeHeadline Headline() => live ? new ScopeHeadline { Text = status, Tone = statusTone } : default;

        public override void Paint(ScopeCanvas g, in PagePaint p)
        {
            if (!live) return;
            Plot(g, p.Units);
            JumpLight(g);
            Load(g);
            Checklist(g, p.Units);
            Figures(g, p.Units);
        }

        private static void At(float bearing, float r, out float x, out float y)
        {
            ScopeCanvas.Polar(bearing, r, out float dx, out float dy);
            x = CX + dx;
            y = CY + dy;
        }

        // ------------------------------------------------------------------------------------------------ the drop plot

        private void Plot(ScopeCanvas g, AvUnits units)
        {
            g.Ring(CX, CY, R, Dim, 0.85f);
            if (!d.UsesLandingZone || !d.ZoneAvailable && !d.HasZone)
            {
                // A load that keeps the game's own aim: nothing to plot.
                g.Text("NO DROP PLOT", CX, CY - 8f, Ink, 14f, ScopeAlign.Center, true);
                g.Text("THE GAME AIMS", CX, CY + 12f, Dim, 12f, ScopeAlign.Center);
                g.Text("THIS LOAD", CX, CY + 28f, Dim, 12f, ScopeAlign.Center);
                return;
            }
            if (d.HasZone) g.Ring(CX, CY, R / 2f, Faint, 1f, 1f, Ink, 0f, 4f, 3f);
            for (int b = 0; b < 360; b += 30)
            {
                At(b, R, out float ax, out float ay);
                At(b, R - (b % 90 == 0 ? 6f : 3f), out float bx, out float by);
                g.Line(ax, ay, bx, by, Dim, 0.9f);
            }
            g.Dashed(CX, CY - 12f, CX, CY - R, Faint, 0.9f);      // the track
            g.Text("HDG UP", 6f, 264f, Faint);
            g.Jet(CX, CY, 0f, 8f, Ink, true);
            if (!d.HasZone)
            {
                g.Text("NO DROP ZONE", CX, CY + 28f, Amber, 14f, ScopeAlign.Center, true);
                g.Text("RIGHT-CLICK THE MAP", CX, CY + 46f, Dim, 12f, ScopeAlign.Center);
                g.Text("OR WEAPONS > CARGO", CX, CY + 62f, Dim, 12f, ScopeAlign.Center);
                return;
            }

            float range = CargoManifestRules.PlotRangeM(d.DistanceM, d.HasReach ? d.ReachM : float.NaN), k = R / range;
            g.Text(ScopeStr.Distance("", range, units, 8), CX, CY + R + 10f, Dim, 12f, ScopeAlign.Center);      // the rim's range, under it: the zone is seldom dead astern
            bool inside = CargoManifestRules.InRange(d);
            if (d.HasReach)
            {
                float rr = Math.Min(d.ReachM * k, R);
                if (inside) g.Ring(CX, CY, rr, Hi, 1f, 1.5f);
                else g.Ring(CX, CY, rr, Ink, 0.9f, 1f, Ink, 0f, 5f, 3f);
                if (rr > 18f && rr < R - 10f) g.Text("REACH", CX, CY + rr + 8f, inside ? Hi : Dim, 12f, ScopeAlign.Center);      // under the ring: the zone is mostly ahead
            }
            float dz = d.DistanceM * k;
            if (dz <= R - 8f)
            {
                At(d.BearingDeg, dz, out float x, out float y);
                ScopeTone tone = inside ? Hi : Ink;
                g.Dashed(CX, CY, x, y, tone, 0.7f);
                g.Diamond(x, y, 6f, tone, inside, 0.9f);
                g.Ring(x, y, 11f, tone, 0.6f);
                g.Text("DZ", x + 14f, y - 7f, tone, 12f, ScopeAlign.Left, true);
            }
            else
            {
                // Past the rim: a pointer on it at the zone's bearing, and its range inside.
                At(d.BearingDeg, R, out float x, out float y);
                g.Tri(x, y, d.BearingDeg, 6f, Amber);
                At(d.BearingDeg, R - 20f, out float lx, out float ly);
                g.Text(ScopeStr.Distance("DZ", d.DistanceM, units, 10), lx, ly, Amber, 12f, ScopeAlign.Center, true);
            }
            int off = (int)Math.Round(Math.Abs(d.BearingDeg));
            if (off <= 3) g.Text("DZ ON THE NOSE", CX, 46f, inside ? Hi : Ink, 12f, ScopeAlign.Center, true);
            else g.Text(ScopeStr.Cat(d.BearingDeg > 0f ? "DZ RIGHT " : "DZ LEFT ", off, "°"), CX, 46f, off > 90 ? Amber : Ink, 12f, ScopeAlign.Center, true);
        }

        // ------------------------------------------------------------------------------------------------ the right-hand column

        /// <summary>The jump light and, under it, why it is not green, or how long until the zone comes into reach.</summary>
        private void JumpLight(ScopeCanvas g)
        {
            CargoLight light = CargoManifestRules.Light(d);
            ScopeTone tone = light == CargoLight.Green ? Hi : light == CargoLight.Amber ? Amber : Red;
            g.Ring(X0 + 9f, LightY, 8f, tone, 1f, 1.5f, tone, 0.9f);
            g.Text(CargoManifestRules.LightWord(light), X0 + 24f, LightY, tone, 14f, ScopeAlign.Left, true);

            // Under it the next action; short of the zone with nothing else holding the drop, how long until it is in reach.
            bool cleared = CargoManifestRules.LoadCheck(d) == CargoCheck.Pass && CargoManifestRules.SafetyCheck(d) == CargoCheck.Pass;
            float t = CargoManifestRules.WindowSeconds(d);
            if (cleared && t > 0f && float.IsPositiveInfinity(t)) g.Text("NOT CLOSING - TURN TO DZ", X0, ReasonY, Amber, 12f);
            else if (cleared && t > 0f)
            {
                int s = (int)Math.Ceiling(t);
                g.Text(ScopeStr.Cat("IN REACH IN ", s / 60, s % 60 < 10 ? ":0" : ":", s % 60, null), X0, ReasonY, Ink, 12f);
            }
            else g.FitText(CargoManifestRules.Hint(d), X0, ReasonY, Right - X0, light == CargoLight.Green ? Ink : tone, 12f, 12f, ScopeAlign.Left);
        }

        /// <summary>The focused load: its station, name and rounds, then the stick drawn.</summary>
        private void Load(ScopeCanvas g)
        {
            g.Line(X0, RuleLoad, Right, RuleLoad, Dim, 0.8f);
            float roundsW = ScopeCanvas.TextW(ScopeStr.Cat("", d.Ammo, "/", d.Max, null), 16f);
            g.Text(ScopeStr.Cat("", d.Ammo, "/", d.Max, null), Right, LoadY, d.Ammo > 0 ? Hi : Red, 16f, ScopeAlign.Right, true);
            float staW = g.Text(ScopeStr.Cat("STA ", d.Station), X0, LoadY, Dim, 12f);
            float nameX = X0 + staW + 6f;
            g.FitText(d.Name, nameX, LoadY, Right - roundsW - 8f - nameX, Hi, 14f, 12f, ScopeAlign.Left, true);
            Stick(g);
        }

        /// <summary>The load drawn: jumpers in fireteams of four, gliders, crates; filled while aboard, outlined once gone; past the room a count.</summary>
        private void Stick(ScopeCanvas g)
        {
            bool people = d.Kind == CargoKind.Paratroopers || d.Kind == CargoKind.Troops, glider = d.Kind == CargoKind.Glider;
            int room = people ? 16 : glider ? 8 : 10, total = Math.Max(d.Max, d.Ammo), shown = Math.Min(total, room);
            float x = X0, cy = StickY;
            for (int i = 0; i < shown; i++)
            {
                bool aboard = i < d.Ammo;
                ScopeTone tone = aboard ? Ink : Dim;
                if (people)
                {
                    if (aboard) { g.Fill(x + 1f, cy - 7f, 3f, 3f, tone); g.Fill(x, cy - 3f, 5f, 8f, tone); }
                    else { g.Box(x + 1f, cy - 7f, 3f, 3f, tone, 0.8f); g.Box(x, cy - 3f, 5f, 8f, tone, 0.8f); }
                    x += 7f;
                    if ((i + 1) % CargoManifestRules.TeamSize == 0) x += 6f;
                }
                else if (glider) { g.Chevron(x + 6f, cy, 0f, 6f, tone, aboard, 0.9f); x += 16f; }
                else
                {
                    if (aboard) g.Fill(x, cy - 4f, 9f, 9f, tone, 0.9f);
                    else g.Box(x, cy - 4f, 9f, 9f, tone, 0.8f);
                    x += 13f;
                }
            }
            if (total > room) g.Text(ScopeStr.Cat("+", total - room), x + 2f, cy, Dim, 12f);
            g.Text(CargoManifestRules.KindWord(d.Kind), Right, cy, Dim, 12f, ScopeAlign.Right);
        }

        /// <summary>The release checklist: the load, its safety, the zone and the reach, each passed (a tick), failed (a cross, amber) or not this load's business (a dash).</summary>
        private void Checklist(ScopeCanvas g, AvUnits units)
        {
            g.Line(X0, RuleChecks, Right, RuleChecks, Dim, 0.8f);
            float y = CheckY;
            CargoCheck load = CargoManifestRules.LoadCheck(d);
            Mark(g, y, "LOAD", load);
            g.Text(d.Reloading ? "RELOADING" : d.Ammo <= 0 ? "EXPENDED" : d.Selected ? "SELECTED" : "SELECT IN WEAPONS", Right, y, Tone(load), 12f, ScopeAlign.Right);

            y += CheckPitch;
            CargoCheck safety = CargoManifestRules.SafetyCheck(d);
            Mark(g, y, "SAFETY", safety);
            g.Text(d.Safe ? "ON" : "OFF", Right, y, Tone(safety), 12f, ScopeAlign.Right, d.Safe);

            y += CheckPitch;
            CargoCheck zone = CargoManifestRules.ZoneCheck(d);
            Mark(g, y, "DROP ZONE", zone);
            if (zone == CargoCheck.Pass) g.Text(ScopeStr.Distance("", d.DistanceM, units, 8), Right, y, Tone(zone), 12f, ScopeAlign.Right);
            else g.Text(zone == CargoCheck.Fail ? "NOT SET" : "NATIVE AIM", Right, y, Tone(zone), 12f, ScopeAlign.Right);

            y += CheckPitch;
            CargoCheck reach = CargoManifestRules.ReachCheck(d);
            Mark(g, y, "REACH", reach);
            if (reach != CargoCheck.NotApplicable) g.Text(ScopeStr.Distance("", d.ReachM, units, 8), Right, y, Tone(reach), 12f, ScopeAlign.Right);
            else g.Text(d.HasZone ? "NOT RATED" : "--", Right, y, Dim, 12f, ScopeAlign.Right);
        }

        private static void Mark(ScopeCanvas g, float y, string label, CargoCheck check)
        {
            float x = X0 + 5f;
            if (check == CargoCheck.Pass) g.Check(x, y, 9f, Hi, 1f, 1.5f);
            else if (check == CargoCheck.Fail) g.Cross(x, y, 7f, Amber, 1f, 1.5f);
            else g.Line(x - 4f, y, x + 4f, y, Dim);
            g.Text(label, X0 + 18f, y, check == CargoCheck.NotApplicable ? Dim : Ink, 12f);
        }

        private static ScopeTone Tone(CargoCheck check) => check == CargoCheck.Pass ? Hi : check == CargoCheck.Fail ? Amber : Dim;

        /// <summary>The height above the zone and the ground speed, then the other loads aboard (their station, name and rounds; the selected one BRIGHT).</summary>
        private void Figures(ScopeCanvas g, AvUnits units)
        {
            g.Line(X0, RuleFigures, Right, RuleFigures, Dim, 0.8f);
            float y = FiguresY;
            bool imperial = units == AvUnits.Imperial;
            if (d.HasZone)
            {
                g.Text(ScopeStr.Cat("HAZ ", (int)Math.Round(d.HeightM * (imperial ? 3.28084f : 1f)), imperial ? " FT" : " M"), X0, y, Ink, 12f);
                g.Text(ScopeStr.Cat("GS ", (int)Math.Round(d.GroundMps * (imperial ? 1.943844f : 3.6f)), ScopeStr.SpeedUnitSp(units)), Right, y, Ink, 12f, ScopeAlign.Right);
                y += RowPitch;
            }
            for (int i = 0; i < d.LoadCount && d.Loads != null && y <= Bottom; i++)
            {
                CargoLoad l = d.Loads[i];
                if (l.Station == d.Station) continue;
                ScopeTone tone = l.Selected ? Hi : l.Ammo > 0 ? Ink : Dim;
                float roundsW = ScopeCanvas.TextW(ScopeStr.Cat("", l.Ammo, "/", l.Max, null));
                g.Text(ScopeStr.Cat("", l.Ammo, "/", l.Max, null), Right, y, tone, 12f, ScopeAlign.Right);
                float staW = g.Text(ScopeStr.Cat("STA ", l.Station), X0, y, Dim, 12f);
                g.FitText(l.Name, X0 + staW + 6f, y, Right - roundsW - 8f - (X0 + staW + 6f), tone, 12f, 12f, ScopeAlign.Left);
                y += RowPitch;
            }
        }
    }
}
