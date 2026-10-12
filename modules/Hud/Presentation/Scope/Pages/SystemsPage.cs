using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// SYSTEMS (prototype: <c>pages/systems.js</c>, the style reference for every page): the cruise page. In the fixed AUTO scope it is the
    /// compact strip: the airframe with its damage zones and condition tags, the fuel bar with BINGO / JOKER marks and the way home
    /// and the wind (the countermeasure dispensers and the jammer are the stores header's). Its feed is <see cref="SystemsScope"/> (Hud's own provider
    /// over the watchers). It is "live" (earns a pip on the other pages, selectable as the full page) only while something is wrong: a
    /// fuel mark, a leak, a fire, a dead engine or lost structure. Events (a mark crossed, a fire, a flameout) go to the ticker once.
    /// </summary>
    internal sealed class SystemsPage : ScopePageBase
    {
        // Zones in the jet's unit space (x right of the centreline, y down the airframe; the right side is the mirror image).
        private static readonly float[] EngineBay = { -0.21f, 0.34f, -0.05f, 0.34f, -0.05f, 0.95f, -0.13f, 1.0f, -0.21f, 0.9f };
        private static readonly float[] EngineBayCentre = { -0.08f, 0.4f, 0.08f, 0.4f, 0.08f, 0.94f, 0f, 0.99f, -0.08f, 0.94f };
        private static readonly float[] TankCentre = { -0.09f, -0.32f, 0.09f, -0.32f, 0.09f, 0.26f, -0.09f, 0.26f };
        private static readonly float[] TankWing = { -0.16f, -0.16f, -0.5f, 0.06f, -0.5f, 0.37f, -0.26f, 0.355f, -0.16f, 0.28f };
        private static readonly float[] Wing = { -0.52f, 0.07f, -0.76f, 0.2f, -0.78f, 0.33f, -0.7f, 0.4f, -0.52f, 0.38f };
        private static readonly float[] Crack = { -0.6f, 0.12f, -0.65f, 0.19f, -0.6f, 0.25f, -0.68f, 0.32f };
        private static readonly float[] Canopy = { 0f, -0.68f, 0.055f, -0.5f, 0.045f, -0.3f, -0.045f, -0.3f, -0.055f, -0.5f };
        private static readonly float[] BaseArrow = { 0f, -6f, 5f, 5f, 0f, 2f, -5f, 5f };
        // The rotary airframe: fuselage with its tail boom and stabiliser, the engine decks either side of the mast, the tank under the cabin, the rotor disc.
        private static readonly float[] Fuselage =
        {
            0f, -0.62f, 0.2f, -0.45f, 0.26f, -0.1f, 0.24f, 0.3f, 0.07f, 0.42f, 0.045f, 0.95f, 0.17f, 1f,
            -0.17f, 1f, -0.045f, 0.95f, -0.07f, 0.42f, -0.24f, 0.3f, -0.26f, -0.1f, -0.2f, -0.45f,
        };
        private const int FuselagePoints = 13;
        private static readonly float[] RotorEngine = { -0.26f, -0.1f, -0.1f, -0.1f, -0.1f, 0.3f, -0.24f, 0.3f };
        private static readonly float[] RotorTank = { -0.1f, -0.35f, 0.1f, -0.35f, 0.1f, 0.05f, -0.1f, 0.05f };
        private static readonly float[] Flame = new float[10];

        private const float FlashSeconds = 2f, FireFlash = 3f, DripCount = 6f;

        private SystemsScope d;
        private bool ready, primed, abnormal, fast, critical;
        private float now;
        private readonly Lamp[] rows = new Lamp[HudDamage.MaxLamps];
        private int rowCount;
        private DamageState rowsFor;
        private SystemsHeadline headline = SystemsHeadline.Nominal;
        private string headlineText = "NOMINAL";
        private int headlineKey = -1;
        private float headlineAt = float.NegativeInfinity, hitAt = float.NegativeInfinity, engineOutAt = float.NegativeInfinity;
        private byte hitSide;
        private DamageState prev;
        private bool prevBingo, prevJoker;
        private int gaugeKey = -1;
        private string gaugeText, gaugeSub;
        /// <summary>The G gauge's live window (see <see cref="SystemsMath.GaugeLive"/>): the clock the load last reached it, and whether it is live this tick.</summary>
        private float gaugeAt = float.NegativeInfinity;
        private bool gaugeLive;

        public override ScopePage Id => ScopePage.Systems;

        public override string Title => "SYSTEMS";

        public override char Code => 'Y';

        public override bool Ready => ready;

        public override bool Fast => fast;

        public override bool Critical => critical;

        // ------------------------------------------------------------------------------------------------ poll

        public override bool Poll(in ScopeEnv env)
        {
            now = env.Now;
            ready = ScopeFeeds.TryRead(out d) && d.Fuel.Valid;
            if (!ready) { primed = false; fast = abnormal = critical = gaugeLive = false; gaugeAt = float.NegativeInfinity; return false; }
            gaugeLive = SystemsMath.GaugeLive(d.G, now, ref gaugeAt);

            if (!primed || !SystemsMath.SameDamage(rowsFor, d.Damage))
            {
                rowCount = HudDamage.Fill(d.Damage, rows);
                rowsFor = d.Damage;
            }
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
            critical = SystemsMath.HeadlineTone(h, d.Damage) == Red;      // fire, an engine out, BINGO, lost structure
            fast = (d.Damage.Flags & (DamageFlags.Fire | DamageFlags.FuelLeak)) != 0 || now < hitAt + 1f || h == SystemsHeadline.Fire || h == SystemsHeadline.Bingo ||
                   (abnormal && now < headlineAt + FlashSeconds);
            return abnormal;
        }

        /// <summary>Events once: a hit lands, a fire starts, an engine quits, a fuel mark is crossed.</summary>
        private void Edges(in ScopeEnv env)
        {
            DamageState c = d.Damage;
            byte gained = (byte)((c.Leak & ~prev.Leak) | (c.WingLoss & ~prev.WingLoss) | (c.EngineOut & ~prev.EngineOut));
            if (gained != 0) { hitAt = now; hitSide = gained; }
            if ((c.Flags & DamageFlags.Fire) != 0 && (prev.Flags & DamageFlags.Fire) == 0)
                env.Notice?.Invoke("fire", HudTone.Warning, "FIRE - " + SideWord(c.Fire) + " TANK", null);
            if (c.EnginesOut > prev.EnginesOut)
            {
                engineOutAt = now;
                env.Notice?.Invoke("dmg", HudTone.Warning, "ENGINE FLAMEOUT", c.Engines > 1 ? (c.Engines - c.EnginesOut) + " OF " + c.Engines + " RUNNING" : null);
            }
            if ((c.Flags & DamageFlags.FuelLeak) != 0 && (prev.Flags & DamageFlags.FuelLeak) == 0)
                env.Notice?.Invoke("dmg", HudTone.Caution, "FUEL LEAK - " + SideWord(c.Leak) + " TANK", null);
            if (d.Fuel.Bingo && !prevBingo)
                env.Notice?.Invoke("fuel", HudTone.Warning, "BINGO", d.Fuel.Field != null ? "RTB " + d.Fuel.Field.Trim().ToUpperInvariant() : null);
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

        public override bool Lamp(string legend, out LampState state, out bool flash, out string text)
        {
            state = LampState.Dark;
            flash = false;
            text = null;
            if (!ready || legend != "ENG" || d.Damage.EnginesOut <= 0) return false;
            state = LampState.Warning;
            flash = now < engineOutAt + FireFlash;
            return true;
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

        // ------------------------------------------------------------------------------------------------ paint (the compact strip)

        public override void Paint(ScopeCanvas g, in PagePaint p)
        {
            if (!ready) return;
            now = p.Now;
            PaintAirframe(g, p.Now, 26f, 63f, 24f);

            // The strip's three columns share one 16 unit row grid (LAYOUT.md 3.5, 12.1): R0..R4 = 31, 47, 63, 79, 95. Condition tags, most severe first (open tags 18 apart); the nominal words are data too (INK) and sit on
            // R1..R3 when nothing is wrong. No column dividers: the gaps between the three columns (conditions, fuel and base, weather) are the separators.
            if (rowCount > 0)
                for (int i = 0; i < rowCount; i++)
                    g.PlateTag(rows[i].Legend, 52f, 33f + i * 18f, SystemsMath.ToneOf(rows[i].State));
            else
            {
                if (d.Damage.Engines > 0) g.Text(ScopeStr.Cat("ENG ", d.Damage.Engines - d.Damage.EnginesOut, "/", d.Damage.Engines, null), 54f, 47f, Ink);
                g.Text("TANKS OK", 54f, 63f, Ink);
                g.Text(ScopeStr.Cat("STR ", 100 - SystemsMath.Percent(d.Damage.DetachedRatio), "%"), 54f, 79f, Ink);
            }

            FuelBlock(g, 142f, 31f, 156f);
            if (d.Fuel.Field != null && !float.IsNaN(d.Fuel.FieldM)) BaseBlock(g, 142f, 79f, 156f, p.Units);
            WeatherBlock(g, 310f, 35f);
        }

        private void PaintAirframe(ScopeCanvas g, float t, float cx, float cy, float s)
        {
            DamageState dm = d.Damage;
            if (d.Rotary)
            {
                int n = g.Load(Fuselage, FuselagePoints, cx, cy, s);
                g.FillPoly(n, Ink, 0.07f);
                g.Path(n, Ink, 0.95f);
                float hub = cy - 0.1f * s;
                g.Ring(cx, hub, s * 0.95f, Dim, 0.6f, 1f, dash: 3f, gap: 3f);
                g.Line(cx - s * 0.95f, hub, cx + s * 0.95f, hub, Dim, 0.9f);
                g.Line(cx, hub - s * 0.95f, cx, hub + s * 0.95f, Dim, 0.5f);
                g.Ring(cx, hub, 2f, Ink, 1f, 1f, Ink, 1f);
            }
            else
            {
                int n = g.Load(ScopeShapes.Jet, ScopeShapes.JetPoints, cx, cy, s);
                g.FillPoly(n, Ink, 0.07f);
                g.Path(n, Ink, 0.95f);
                n = g.Load(Canopy, 5, cx, cy, s);
                g.Path(n, Dim, 0.9f);

                // Exhaust of every engine still running, flickering from the hash, not from random.
                for (int side = 0; side < 3; side++)
                {
                    byte bit = (byte)(1 << side);
                    if ((dm.EngineSlots & bit) == 0 || (dm.EngineOut & bit) != 0) continue;
                    float sg = side == 0 ? -1f : side == 2 ? 1f : 0f;
                    for (int k = -1; k <= 1; k++)
                    {
                        float len = (0.1f + 0.12f * ScopeCanvas.Hash(t * 16f + k * 3f + side * 7f)) * s * (k == 0 ? 1.35f : 1f);
                        float x = cx + (sg * 0.1f + k * 0.035f) * s, y = cy + 1.03f * s;
                        g.Line(x, y, x, y + len, Ink, k == 0 ? 0.95f : 0.6f);
                    }
                }
            }

            // Damage zones by side: a dead engine bay, a leaking tank, a wing that lost parts.
            Zone(g, dm.EngineOut, d.Rotary ? RotorEngine : EngineBay, d.Rotary ? 4 : 5, EngineBayCentre, 5, cx, cy, s, Red, 0.24f);
            Zone(g, dm.Leak, d.Rotary ? RotorTank : TankWing, d.Rotary ? 4 : 5, TankCentre, 4, cx, cy, s, Amber, 0.18f + 0.08f * ScopeCanvas.Pulse(t, 1.2f));
            if (dm.DetachedRatio >= HudDamage.StructureCaution && !d.Rotary)
            {
                ScopeTone tone = dm.DetachedRatio >= HudDamage.StructureWarning ? Red : Amber;
                for (int side = 0; side < 3; side += 2)
                {
                    if ((dm.WingLoss & (byte)(1 << side)) == 0) continue;
                    bool mir = side == 2;
                    int n = g.Load(Wing, 5, cx, cy, s, 0f, mir);
                    g.Path(n, tone, 1f, 1f, true, 3f, 2f);
                    n = g.Load(Crack, 4, cx, cy, s, 0f, mir);
                    g.Path(n, tone, 1f, 1f, false);
                }
            }
            if ((dm.Flags & DamageFlags.Fire) != 0) Flames(g, t, dm.Fire != 0 ? dm.Fire : dm.EngineOut, cx, cy, s, d.Rotary ? 0.3f : 0.96f);
            if ((dm.Flags & DamageFlags.FuelLeak) != 0) Drips(g, t, dm.Leak, cx, cy, s);

            // The hit: an expanding ring and a cross where it landed.
            float hu = ScopeCanvas.Seg(t, hitAt, hitAt + 0.9f);
            if (hitSide != 0 && hu > 0f && hu < 1f)
            {
                float hx = (hitSide & (byte)ZoneSide.Left) != 0 ? -0.46f : (hitSide & (byte)ZoneSide.Right) != 0 ? 0.46f : 0f, hy = hx == 0f ? 0.4f : 0.2f;
                float x = cx + hx * s, y = cy + hy * s;
                g.Ring(x, y, 3f + 20f * ScopeCanvas.EaseOut(hu), Hi, 1f - hu);
                g.Cross(x, y, 8f * (1f - hu) + 3f, Red, 1f - hu);
            }
        }

        /// <summary>The zone of every side in <paramref name="mask"/> (left and right are mirrors of one outline), hatched and tinted.</summary>
        private static void Zone(ScopeCanvas g, byte mask, float[] side, int sideN, float[] centre, int centreN, float cx, float cy, float s, ScopeTone tone, float fill)
        {
            if (mask == 0) return;
            for (int bit = 0; bit < 3; bit++)
            {
                if ((mask & (1 << bit)) == 0) continue;
                int n = bit == 1 ? g.Load(centre, centreN, cx, cy, s) : g.Load(side, sideN, cx, cy, s, 0f, bit == 2);
                g.FillPoly(n, tone, fill);
                g.Hatch(n, 4f, tone, 0.8f);
                g.Path(n, tone);
            }
        }

        /// <summary>Three flame tongues off the burning bay, their jitter from the hash.</summary>
        private static void Flames(ScopeCanvas g, float t, byte mask, float cx, float cy, float s, float baseY)
        {
            if (mask == 0) mask = (byte)ZoneSide.Centre;
            for (int bit = 0; bit < 3; bit++)
            {
                if ((mask & (1 << bit)) == 0) continue;
                float sign = bit == 2 ? -1f : 1f, shift = bit == 1 ? 0.11f : 0f;     // the right bay is the left one mirrored; centre sits on the axis
                for (int k = 0; k < 3; k++)
                {
                    float x0 = (-0.16f + shift + k * 0.055f) * sign, len = 0.2f + 0.2f * ScopeCanvas.Hash(t * 13f + k * 5f), sway = (ScopeCanvas.Hash(t * 13f + k * 9f) - 0.5f) * 0.1f * sign;
                    Flame[0] = x0 - 0.03f * sign; Flame[1] = baseY;
                    Flame[2] = x0 + sway * 0.4f; Flame[3] = baseY + len * 0.55f;
                    Flame[4] = x0 + sway; Flame[5] = baseY + len;
                    Flame[6] = x0 + sway * 0.3f + 0.025f * sign; Flame[7] = baseY + len * 0.5f;
                    Flame[8] = x0 + 0.03f * sign; Flame[9] = baseY;
                    int n = g.Load(Flame, 5, cx, cy, s);
                    ScopeTone tone = k == 1 ? Amber : Red;
                    g.FillPoly(n, tone, 0.75f);
                    g.Path(n, tone);
                }
            }
        }

        /// <summary>Drops trailing aft from a leaking tank.</summary>
        private static void Drips(ScopeCanvas g, float t, byte mask, float cx, float cy, float s)
        {
            if (mask == 0) return;
            for (int bit = 0; bit < 3; bit += 2)
            {
                if ((mask & (1 << bit)) == 0) continue;
                float sign = bit == 2 ? 1f : -1f;
                for (int k = 0; k < (int)DripCount; k++)
                {
                    float u = (t * 1.5f + k / DripCount) % 1f;
                    g.Fill(Mathf.Round(cx + sign * (0.46f + u * 0.08f) * s), Mathf.Round(cy + (0.34f + u * 0.62f) * s), 2f, 2f, Amber, 1f - u);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ blocks

        private void FuelBlock(ScopeCanvas g, float x, float y, float w)
        {
            FuelState f = d.Fuel;
            ScopeTone tone = SystemsMath.ToneOf(f.Tone);
            g.Text("FUEL", x, y, Dim);
            g.Text(ScopeStr.Pct(SystemsMath.Percent(f.Level)), x + 36f, y, tone == Ink ? Hi : tone, 16f, bold: true);
            if (SystemsMath.EnduranceMinutes(f.EnduranceS, out int min))
            {
                ScopeTone et = SystemsMath.EndTone(f.EnduranceS);
                ReadOnlySpan<char> words = min < 1 ? "END <1 MIN" : min < 100 ? ScopeStr.Cat("END ", min, " MIN") : ScopeStr.Cat("END ", min / 60, (min % 60) < 10 ? "H0" : "H", min % 60, null);
                g.Text(words, x + w, y, et, align: ScopeAlign.Right, bold: et != Dim);
            }
            float by = y + 11f, bh = 10f;      // the bar is centred on R1 (y + 16)
            g.Bar(x, by, w, bh, f.Level, tone);
            if (f.HasNeed)
            {
                g.BarTick(x, by, w, bh, Math.Min(0.99f, f.Need), SystemsMath.BingoMarkTone(f));
                g.BarTick(x, by, w, bh, Math.Min(0.99f, f.Need + FuelEndurance.JokerMargin), SystemsMath.JokerMarkTone(f));
            }
            float burn = SystemsMath.BurnPointsPerMinute(f);
            if ((d.Damage.Flags & DamageFlags.FuelLeak) != 0 && burn > 0f)
                g.Text(ScopeStr.Cat("LEAK -", burn, 1, "%/M"), x + w, by + bh + 11f, Amber, align: ScopeAlign.Right, bold: true);
            else if (f.HasNeed)
                g.Text(ScopeStr.Cat("BINGO ", SystemsMath.Percent(f.Need), "%  JOKER ", SystemsMath.Percent(f.Need + FuelEndurance.JokerMargin), "%"), x, by + bh + 11f, Dim);
        }

        private void BaseBlock(ScopeCanvas g, float x, float y, float w, NOAvionics.AvUnits units)
        {
            FuelState f = d.Fuel;
            int n = g.Load(BaseArrow, 4, x + 7f, y, 1f, f.FieldRelDeg);
            g.FillPoly(n, Hi, 1f);
            g.Path(n, Hi);
            int margin = SystemsMath.MarginPoints(f);
            ScopeTone mt = SystemsMath.MarginTone(margin);
            // Two rows 16 apart (LAYOUT.md 3.5, 12.1): the name over the distance and the margin figure at the right edge, so a long name never meets the margin. The margin is shortened, then dropped, before
            // a blank is lost between it and the distance; the name has the first row to itself (the bearing arrow reaches 13, the words start at 22).
            int room = (int)((w - 22f) / (ScopeText.DesignAdvance * 12f)), form = 2;
            if (f.HasNeed) form = ScopeStr.FitMargin(null, f.FieldM, units, room, SystemsMath.MarginWords(margin, false).Length, SystemsMath.MarginWords(margin, true).Length, out _);
            g.Text(ScopeStr.Name(f.Field, room), x + 22f, y, Ink);
            g.Text(ScopeStr.Distance(null, f.FieldM, units, room), x + 22f, y + 16f, Ink);
            if (form < 2) g.Text(SystemsMath.MarginWords(margin, form == 1), x + w, y + 16f, mt, align: ScopeAlign.Right, bold: mt != Ink);
        }

        /// <summary>
        /// The wind (LAYOUT.md 3.5, 12.1): a dial turning with the nose (the arrow is where the air is going, relative to it) and, beside it, the DIM label WIND over the direction the air blows from and the speed with its unit
        /// ("180/0 KM/H"), on the strip's 16 unit rows, and IN CLOUD under that while the aircraft is in a deck. Weather is optional: without it nothing is drawn (no wind reading, no dial).
        /// </summary>
        private void WeatherBlock(ScopeCanvas g, float x, float y)
        {
            if (!d.HasWind && !(d.HasCloud && d.InCloud)) return;
            float row = y + 12f;
            if (d.HasWind)
            {
                float cx = x + 18f, cy = y + 20f, a = d.WindFromDeg + 180f - d.HeadingDeg;
                g.Ring(cx, cy, 17f, Faint);
                g.Line(cx, cy - 19f, cx, cy - 15f, Dim);
                ScopeCanvas.Polar(a, 13f, out float hx, out float hy);
                ScopeCanvas.Polar(a + 180f, 13f, out float tx, out float ty);
                g.Arrow(cx + tx, cy + ty, cx + hx, cy + hy, Ink, 1f, 1f, 5f);
                int dir = SystemsMath.WindFromDegrees(d.WindFromDeg);
                g.Text("WIND", x + 44f, y - 4f, Dim);
                g.Text(ScopeStr.Cat(dir < 10 ? "00" : dir < 100 ? "0" : "", dir, "/", (int)Math.Round(NOAvionics.AvUnitTable.Speed(d.WindMps, d.Units)), ScopeStr.SpeedUnitSp(d.Units)), x + 44f, row, Ink);
                row += 16f;
            }
            if (d.HasCloud && d.InCloud) g.Text("IN CLOUD", x + 44f, row, Hi, bold: true);
        }
    }
}
