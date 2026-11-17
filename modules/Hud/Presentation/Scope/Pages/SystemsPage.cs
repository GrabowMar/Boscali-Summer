using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// SYSTEMS (prototype: <c>pages/systems.js</c>, the style reference for every page): the cruise page. On the board it is the INFO cell of the ONE BAR (local time and the mission clock, the wind, the sky and the temperature);
    /// the airframe and fuel pictures are the native MFD's and the mod's FUEL arc, their warnings the FUEL and DMG lamps. Its feed is <see cref="SystemsScope"/> (Hud's own provider
    /// over the watchers). It is "live" (earns a pip on the other pages, selectable as the full page) only while something is wrong: a
    /// fuel mark, a leak, a fire, a dead engine or lost structure. Events (a mark crossed, a fire, a flameout) go to the ticker once.
    /// </summary>
    internal sealed class SystemsPage : ScopePageBase
    {
        private const float FlashSeconds = 2f;

        private SystemsScope d;
        private bool ready, primed, abnormal;
        private float now;
        private SystemsHeadline headline = SystemsHeadline.Nominal;
        private string headlineText = "NOMINAL";
        private int headlineKey = -1;
        private float headlineAt = float.NegativeInfinity;
        private DamageState prev;
        private bool prevBingo, prevJoker;
        private int gaugeKey = -1;
        private string gaugeText, gaugeSub;
        /// <summary>The G gauge's live window (see <see cref="SystemsMath.GaugeLive"/>): the clock the load last reached it, and whether it is live this tick.</summary>
        private float gaugeAt = float.NegativeInfinity;
        private bool gaugeLive;
        private int clockShown = -1;
        private string clockWords = "";

        public override ScopePage Id => ScopePage.Systems;

        public override string Title => "SYSTEMS";

        public override bool Ready => ready;

        public override bool Fast => false;

        // ------------------------------------------------------------------------------------------------ poll

        public override bool Poll(in ScopeEnv env)
        {
            now = env.Now;
            ready = ScopeFeeds.TryRead(out d) && d.Fuel.Valid;
            if (!ready) { primed = false; abnormal = gaugeLive = false; gaugeAt = float.NegativeInfinity; return false; }
            gaugeLive = SystemsMath.GaugeLive(d.G, now, ref gaugeAt);

            SystemsHeadline h = SystemsMath.Headline(d.Damage, d.Fuel);
            int key = (int)h * 1000 + (h == SystemsHeadline.Structure ? SystemsMath.Percent(d.Damage.DetachedRatio) : 0);
            if (key != headlineKey)
            {
                headlineKey = key;
                headline = h;
                headlineText = SystemsMath.HeadlineText(h, d.Damage);
                if (primed) headlineAt = now;
            }

            if (primed) Edges(env);
            prev = d.Damage;
            prevBingo = d.Fuel.Bingo;
            prevJoker = d.Fuel.Joker;
            primed = true;

            abnormal = h != SystemsHeadline.Nominal;
            return abnormal;
        }

        /// <summary>Events once: a fire starts, an engine quits, a fuel mark is crossed.</summary>
        private void Edges(in ScopeEnv env)
        {
            DamageState c = d.Damage;
            if ((c.Flags & DamageFlags.Fire) != 0 && (prev.Flags & DamageFlags.Fire) == 0)
                env.Notice?.Invoke("fire", HudTone.Warning, "FIRE - " + SideWord(c.Fire) + " TANK", null);
            if (c.EnginesOut > prev.EnginesOut)
            {
                env.Notice?.Invoke("dmg", HudTone.Warning, "ENGINE FLAMEOUT", c.Engines > 1 ? (c.Engines - c.EnginesOut) + " OF " + c.Engines + " RUNNING" : null);
            }
            if ((c.Flags & DamageFlags.FuelLeak) != 0 && (prev.Flags & DamageFlags.FuelLeak) == 0)
                env.Notice?.Invoke("dmg", HudTone.Caution, "FUEL LEAK - " + SideWord(c.Leak) + " TANK", null);
            if (d.Fuel.Bingo && !prevBingo)
                env.Notice?.Invoke("fuel", HudTone.Caution, "BINGO", d.Fuel.Field != null ? "RTB " + d.Fuel.Field.Trim().ToUpperInvariant() : null);
            else if (d.Fuel.Joker && !prevJoker && !d.Fuel.Bingo)
                env.Notice?.Invoke("fuel", HudTone.Caution, "JOKER - " + SystemsMath.Percent(d.Fuel.Level) + "%", null);
        }

        private static string SideWord(byte mask) => (mask & (byte)ZoneSide.Left) != 0 ? "LEFT" : (mask & (byte)ZoneSide.Right) != 0 ? "RIGHT" : "CENTRE";

        public override ScopeHeadline Headline()
        {
            if (!ready) return default;
            ScopeTone tone = SystemsMath.HeadlineTone(headline, d.Damage);
            bool fresh = now < headlineAt + FlashSeconds;
            return new ScopeHeadline { Text = headlineText, Tone = tone, Flash = tone == Red && (headline == SystemsHeadline.Fire || headline == SystemsHeadline.Bingo) || tone != Dim && fresh };
        }

        public override int Gauges(GaugeSpec[] into)
        {
            if (!ready || !gaugeLive || d.GLimit <= 1f) return 0;      // the G gauge only while the airframe is pulling (a hard turn and a few seconds after), never in level cruise
            int key = (int)Math.Round(d.G * 10f) * 1000 + (int)d.GLimit;
            if (key != gaugeKey)
            {
                gaugeKey = key;
                gaugeText = ScopeStr.Cat("", d.G, 1, null).ToString();
                gaugeSub = "LIM " + (int)d.GLimit;
            }
            float share = Math.Max(0f, d.G) / d.GLimit;
            into[0] = new GaugeSpec { Id = "g", Label = "G", Kind = GaugeKind.Arc, Value = share, Text = gaugeText, Sub = gaugeSub, Tone = share >= 0.8f ? Amber : Ink };
            return 1;
        }
        // ------------------------------------------------------------------------------------------------ paint (the INFO cell of the ONE BAR)

        /// <summary>The full page: the INFO cell alone (the board paints it into its bar through <see cref="PaintInfo"/>).</summary>
        public override void Paint(ScopeCanvas g, in PagePaint p) => PaintInfo(g, new BoardRect(0f, 0f, ScopeLayout.InfoW, ScopeLayout.BarH), p.Now, p.Units);

        /// <summary>
        /// The INFO cell. R1: the local time (a clock glyph and HH:MM) and the mission clock at the right. R2: the wind dial, DDD/SS and its unit, the gust at the right. R3: the sky (IMC, or cover word, base and rain)
        /// and the temperature (or TURB) at the right. Every slot is fixed: no reading draws a Dim "--".
        /// </summary>
        public void PaintInfo(ScopeCanvas g, in BoardRect r, float t, NOAvionics.AvUnits units)
        {
            if (!ready) return;
            now = t;
            float x = r.X, y = r.Y, right = x + r.W, r1 = y + ScopeLayout.R1, r2 = y + ScopeLayout.R2, r3 = y + ScopeLayout.R3, tx = x + 22f;
            if (g.Furniture) g.Text("INFO", x, y + ScopeLayout.ShelfY, Dim, HudType.Label, bold: true);

            // R1: the local time, then the mission clock right-aligned as one group ("T+" DIM, the time INK).
            float cx = x + 8f;
            float tod = d.TodH;
            float timeEnd = tx;
            if (float.IsNaN(tod)) timeEnd += g.Text("--:--", tx, r1, Dim, HudType.Value);
            else
            {
                int h = (int)tod, m = (int)((tod - h) * 60f);
                g.Ring(cx, r1, 6f, Faint);      // the dial only round a time: no empty ring
                ScopeCanvas.Polar((h % 12 + m / 60f) * 30f, 3.5f, out float hx, out float hy);
                ScopeCanvas.Polar(m * 6f, 5f, out float mx, out float my);
                g.Line(cx, r1, cx + hx, r1 + hy, Ink);
                g.Line(cx, r1, cx + mx, r1 + my, Ink);
                timeEnd += g.Text(ScopeStr.Cat(h < 10 ? "0" : "", h, m < 10 ? ":0" : ":", m, null), tx, r1, Ink, HudType.Value);
            }
            if (d.ClockS >= 0f)
            {
                int s = (int)d.ClockS;
                if (s != clockShown) { clockShown = s; clockWords = (s / 3600) + ":" + (s / 60 % 60).ToString("00") + ":" + (s % 60).ToString("00"); }
                float tw = ScopeCanvas.TextW("T+", HudType.Label), left = right - tw - ScopeCanvas.TextW(clockWords, HudType.Label);
                if (left >= timeEnd + 6f)
                {
                    g.Text("T+", left, r1, Dim, HudType.Label);
                    g.Text(clockWords, left + tw, r1, Ink, HudType.Label);
                }
            }

            // R2: the wind. A dial turning with the nose (the arrow is where the air is going), where it blows from over its speed, the gust at the right.
            if (!d.HasWind) g.Text("--", tx, r2, Dim, HudType.Label);
            else if (d.WindMps < 3f) g.Text("CALM", tx, r2, Dim, HudType.Label);
            else
            {
                float a = d.WindFromDeg + 180f - d.HeadingDeg;
                g.Ring(cx, r2, 8f, Faint);      // the dial only round an arrow: a ring beside CALM / -- read as a radio button
                g.Line(cx, r2 - 10f, cx, r2 - 7f, Dim);
                ScopeCanvas.Polar(a, 6f, out float hx, out float hy);
                ScopeCanvas.Polar(a + 180f, 6f, out float bx, out float by);
                g.Arrow(cx + bx, r2 + by, cx + hx, r2 + hy, Ink, 1f, 1f, 4f);
                int dir = SystemsMath.WindFromDegrees(d.WindFromDeg);
                float end = tx + g.Text(ScopeStr.Cat(dir < 10 ? "00" : dir < 100 ? "0" : "", dir, "/", (int)Math.Round(NOAvionics.AvUnitTable.Speed(d.WindMps, units)), null), tx, r2, Ink, HudType.Body);
                end += 4f + g.Text(ScopeStr.SpeedUnit(units), end + 4f, r2, Dim, HudType.Label);
                if (d.GustMps >= 5f)
                {
                    ReadOnlySpan<char> gust = ScopeStr.Cat("G", (int)Math.Round(NOAvionics.AvUnitTable.Speed(d.WindMps + d.GustMps, units)));
                    if (right - ScopeCanvas.TextW(gust, HudType.Label) >= end + 6f) g.Text(gust, right, r2, Ink, HudType.Label, ScopeAlign.Right);
                }
            }

            // R3: the sky at the left (IMC is its one home), the temperature or TURB at the right (dropped first when it would touch the sky words).
            float skyEnd = tx;
            if (!d.HasCloud) skyEnd += g.Text("--", tx, r3, Dim, HudType.Label);
            else if (d.InCloud) skyEnd += g.Text("IMC", tx, r3, Hi, HudType.Label, ScopeAlign.Left, true);
            else
            {
                string sky = SystemsMath.SkyWord(d.Cover);
                skyEnd += g.Text(sky, skyEnd, r3, Ink, HudType.Label);
                if (d.Cover >= 0.1f)
                {
                    bool ft = units == NOAvionics.AvUnits.Imperial;
                    int baseH = (int)Math.Round(NOAvionics.AvUnitTable.Altitude(d.CloudBaseM, units) / 100f) * 100;
                    skyEnd += g.Text(ScopeStr.Cat(" ", baseH, ft ? "FT" : "M"), skyEnd, r3, Ink, HudType.Label);
                }
                if (d.Rain01 >= 0.15f) skyEnd += g.Text(d.Rain01 >= 0.6f ? " +RA" : " RA", skyEnd, r3, Ink, HudType.Label);
            }
            if (d.HasWind)
            {
                if (d.Turb01 >= 0.5f)
                {
                    if (right - ScopeCanvas.TextW("TURB", HudType.Label) >= skyEnd + 6f) g.Text("TURB", right, r3, Amber, HudType.Label, ScopeAlign.Right, true);
                }
                else
                {
                    bool ft = units == NOAvionics.AvUnits.Imperial;
                    int tc = (int)Math.Round(ft ? d.TempC * 1.8f + 32f : d.TempC);
                    ReadOnlySpan<char> temp = ScopeStr.Cat(tc >= 0 ? "+" : "", tc, ft ? "°F" : "°C");
                    if (right - ScopeCanvas.TextW(temp, HudType.Label) >= skyEnd + 6f) g.Text(temp, right, r3, Ink, HudType.Label, ScopeAlign.Right);
                }
            }
        }
    }
}
