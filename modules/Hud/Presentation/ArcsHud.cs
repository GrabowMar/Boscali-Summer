using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The visor's two ARCS round the reticle (2026-11-16, mockup <c>arcs(d)</c>; 2026-11-17 one continuous stroke): fuel on the left, throttle on the right, 29 degrees each. A faint full-length
    /// track (1 px core at 0.35) and over it the lit fill (2.5 px core), both one polygon of 1-degree steps whose quads share their edges (no gaps, no stair dashes) with a 1 px soft edge each side
    /// (the UI has no MSAA: a hard 1-3 px quad on a slant aliases into dashes). Ticks cross the stroke radially, centred on it, with no letters (2026-11-17 refit: the colour is the word). Fuel carries a JOKER and a BINGO tick at the board's own numbers
    /// (<see cref="FuelState.Need"/>, plus <see cref="FuelEndurance.JokerMargin"/>): Dim until reached, then amber; the fill is Ink, amber once joker is reached, and the number (VALUE bold amber and a
    /// Dim %) shows only then. Throttle carries the game's own afterburner detent (the throttle past it lights the burner; read off the game's gauge, none when the aircraft has none): a Dim tick, Hi once
    /// passed, and the fill Ink up to it and Hi past it. Throttle's value (the game's throttle word 79% / AIRBRAKE / AFTERBURNER) is VALUE bold Hi with a Dim %, above its top end; each caption (regular, the arcs' lifted ink) sits below its bottom end.
    /// <para>They replace the game's two plain arcs (<see cref="NativeFlightArcs"/>) and stand exactly where those are: centre, radius and axis are measured off the game's gauges every frame.
    /// FAIL OPEN: the game's gauges are hidden (renderer alpha only) in the very frame this layer has painted and is showing, and shown again the same frame it is not wanted (<c>want</c> false:
    /// switch off, external view, pause, map, a board tick gone stale), cannot measure, or throws (<see cref="Fail"/>). Built like <see cref="TapeHud"/>: a holder under the native HUD canvas
    /// scaled by canvas height / 1080, one quad mesh and pooled <see cref="ScopeText"/> labels per arc, a mesh rebuilt only when an int signature of what it draws changed (a moving throttle
    /// repaints once per whole percent, not per frame), no per-frame allocation.</para>
    /// </summary>
    internal sealed class ArcsHud
    {
        // 29 one-degree steps x 3 quads (core + two soft edges), one extra step where the fill meets the track and where it crosses the detent, 3 ticks x 3: 96 at most.
        private const int Quads = 128;
        private const float PercentPt = HudType.Value, CaptionPt = HudType.Label, TickHalf = 6f, EndGap = 4f;

        private sealed class Side
        {
            public RectTransform Centre, Rot;
            public AvVector Mesh;
            public ScopeText Text;
            public int Signature = int.MinValue;
            public float X = float.NaN, Y, Axis = float.NaN;
        }

        private RectTransform canvasRoot, root;
        /// <summary>The layer's holder (the visor glitch shader's material goes onto the graphics under it; null until built).</summary>
        internal Transform Root => root;
        private readonly Side left = new Side(), right = new Side();
        private readonly NativeFlightArcs natives = new NativeFlightArcs();
        private readonly char[] scratch = new char[24];
        private float lastK;
        private bool drawing;

        /// <summary>How far the visor's power-on has filled the arcs, 0..1 (1 in flight): the fill is the real level times this, so they fill up from zero to their real values.</summary>
        public float Boot = 1f;

        // The 10 Hz read: the HUD aircraft's fuel and the board's BINGO need.
        private float level = float.NaN, need;
        private bool hasNeed, bingo;
        private bool bingoReached, jokerReached;      // the ticks' reached bits (they go into the signature)

        // What the last frame drew (the sim probe reads it).
        internal bool Shown => drawing;
        internal bool NativesHidden => natives.Hidden;
        internal string NativeTexts => natives.TextAlphas();
        internal int FuelLit { get; private set; }
        internal int ThrottleLit { get; private set; }
        internal float FuelRadius { get; private set; }
        internal float FuelAxis { get; private set; }
        internal string ThrottleWord { get; private set; } = "";

        /// <summary>The board's 10 Hz read: the fuel level and the BINGO need (the same numbers the SYSTEMS strip shows).</summary>
        public void Read(Aircraft aircraft, BoardFrame frame)
        {
            if (aircraft == null || !frame.Fuel.Valid) { level = float.NaN; return; }
            level = frame.Fuel.Level;
            hasNeed = frame.Fuel.HasNeed;
            need = frame.Fuel.Need;
            bingo = frame.Fuel.Bingo;
        }

        private bool Ensure()
        {
            if (root != null && canvasRoot != null) return true;
            root = null;
            if (!NativeRefs.HudCanvas(out _, out Canvas canvas)) return false;
            canvasRoot = (RectTransform)canvas.transform;
            VanillaHudStyle.TryHudFont(out TMPro.TMP_FontAsset font, out Material fontMaterial);
            root = CombatAlerts.Holder(BoscaliSummer.Core.Ui.HudLayer.Ensure() ? BoscaliSummer.Core.Ui.HudLayer.Data : canvasRoot, "BoscaliArcs");
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
            Build(left, "Fuel", font, fontMaterial);
            Build(right, "Throttle", font, fontMaterial);
            root.gameObject.SetActive(false);
            lastK = 0f;
            return true;
        }

        private void Build(Side s, string name, TMPro.TMP_FontAsset font, Material fontMaterial)
        {
            s.Centre = CombatAlerts.Holder(root, name);
            s.Rot = CombatAlerts.Holder(s.Centre, "Rot");
            s.Mesh = AvVector.Create(s.Rot, "Segments", Quads, BoscaliSummer.Core.Contracts.HudGlow.Stroke, BoscaliSummer.Core.Contracts.HudGlow.Alpha);
            s.Text = new ScopeText(CombatAlerts.Holder(s.Centre, "Words"), font, fontMaterial);
            s.Signature = int.MinValue;
            s.X = float.NaN;
            s.Axis = float.NaN;
        }

        /// <summary>
        /// Every frame. <paramref name="want"/>: the switch is on, the view is the cockpit, an overlay may show and the board's tick is fresh. Anything that is not a clean "draw" puts the game's gauges
        /// back first and takes this layer down; the game's are hidden only after this layer has been painted and switched on in this same call.
        /// </summary>
        public void Frame(bool want, Aircraft aircraft)
        {
            if (!want || aircraft == null || float.IsNaN(level) || !Ensure()) { Down(); return; }
            float k = canvasRoot.rect.height / 1080f;
            if (!(k > 0.01f)) { Down(); return; }
            if (!natives.Measure(canvasRoot, out NativeFlightArcs.Arc fuelArc, out NativeFlightArcs.Arc throttleArc, out float detent, out string word)) { Down(); return; }
            if (k != lastK) { root.localScale = new Vector3(k, k, 1f); lastK = k; }
            float canvasPerPixel = Screen.height > 0 ? canvasRoot.rect.height / Screen.height : 1f;
            float scale = canvasPerPixel > 0.01f ? k / canvasPerPixel : 1f;
            if (scale != devPerRef) { devPerRef = scale; left.Signature = right.Signature = int.MinValue; left.X = right.X = float.NaN; }      // a new grid repaints and replaces everything

            ControlInputs inputs = aircraft.GetInputs();
            float throttle = Mathf.Clamp01(inputs != null ? inputs.throttle : 0f);
            VanillaHudStyle.Palette palette = VanillaHudStyle.Colours;
            // The arcs stand among the game's own centre HUD (ladder, speed, altitude: additive, so the sky lifts them to a pale mint, measured 135-157,234-255,191-209 vs INK 96,255,153): INK lifted
            // FlightArcs.InkLift toward INK-HI matches them (2026-11-19, user: "fuel throttle still kinda different tone").
            Color ink = Color.Lerp(palette.AllClear, VanillaHudStyle.InkHi, FlightArcs.InkLift), amber = palette.Warning;
            float bingoAt = hasNeed ? need : float.NaN, jokerAt = hasNeed ? Mathf.Min(need + FuelEndurance.JokerMargin, 1f) : float.NaN;
            bingoReached = bingo || (hasNeed && level <= bingoAt);
            jokerReached = hasNeed && level <= jokerAt;
            bool low = bingoReached || jokerReached || level <= FuelEndurance.WarningLevel;      // caution: the fill and the number go amber

            Place(left, fuelArc, k);
            Place(right, throttleArc, k);
            float fill = Mathf.Clamp01(Boot);
            FuelLit = FlightArcs.Lit(level * fill);
            ThrottleLit = FlightArcs.Lit(throttle * fill);
            int fuelFill = FlightArcs.Percent(level * fill), throttleFill = FlightArcs.Percent(throttle * fill);      // the fill moves in whole percents (0.29 degree, 1.6 px)
            int fuelPct = FlightArcs.Percent(level);
            Color32 inkKey = ink, amberKey = amber;
            int colours = (inkKey.r << 16 | inkKey.g << 8 | inkKey.b) ^ (amberKey.r << 12 | amberKey.g << 4 | amberKey.b) * 31;
            float radius = fuelArc.Radius / k, thrRadius = throttleArc.Radius / k;

            int sigL = Mix(fuelFill, FlightArcs.Quant(bingoAt), FlightArcs.Quant(jokerAt), (low ? 1 : 0) | (bingoReached ? 2 : 0) | (jokerReached ? 4 : 0), fuelPct, (int)(radius * 2f), (int)(fuelArc.AxisDeg * 2f), colours);
            if (sigL != left.Signature)
            {
                PaintFuel(left, radius, fuelArc.AxisDeg, fuelFill * 0.01f, bingoAt, jokerAt, low, fuelPct, ink, amber);
                left.Signature = sigL;
            }
            int wordKey = word != null ? word.GetHashCode() : 0;
            int sigR = Mix(throttleFill, FlightArcs.Quant(detent), !float.IsNaN(detent) && throttleFill * 0.01f > detent ? 1 : 0, 0, wordKey, (int)(thrRadius * 2f), (int)(throttleArc.AxisDeg * 2f), colours);
            if (sigR != right.Signature)
            {
                PaintThrottle(right, thrRadius, throttleArc.AxisDeg, throttleFill * 0.01f, detent, word, ink, amber);
                right.Signature = sigR;
            }
            FuelRadius = fuelArc.Radius;
            FuelAxis = fuelArc.AxisDeg;
            ThrottleWord = word ?? "";

            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            drawing = true;
            natives.Hide();
        }

        private static int Mix(int a, int b, int c, int d, int e, int f, int g, int h)
        {
            unchecked
            {
                int x = a;
                x = x * 31 + b; x = x * 31 + c; x = x * 31 + d; x = x * 31 + e; x = x * 31 + f; x = x * 31 + g; x = x * 31 + h;
                return x;
            }
        }

        /// <summary>The arc's holder at the game's circle: canvas units to the holder's reference pixels, the axis as a rotation of the segments (the words stay upright).</summary>
        private static void Place(Side s, NativeFlightArcs.Arc arc, float k)
        {
            float x = Snap(arc.X / k), y = Snap(arc.Y / k);      // on the device pixel grid: a word at a fraction of a pixel is smeared
            if (s.X != x || s.Y != y)
            {
                s.Centre.localPosition = new Vector3(x, y, 0f);
                s.X = x;
                s.Y = y;
            }
            if (Mathf.Abs(s.Axis - arc.AxisDeg) > 0.01f || float.IsNaN(s.Axis))
            {
                s.Rot.localRotation = Quaternion.Euler(0f, 0f, arc.AxisDeg);
                s.Axis = arc.AxisDeg;
            }
        }

        private void PaintFuel(Side s, float radius, float axis, float fill, float bingoAt, float jokerAt, bool low, int percent, Color ink, Color amber)
        {
            Rgba on = (low ? amber : ink).ToRgba(), hot = amber.ToRgba(), dim = ink.ToRgba().WithAlpha(HudInk.LineMid);
            AvQuadBuffer b = s.Mesh.Buffer;
            b.Clear();
            Stroke(b, radius, true, fill, on, float.NaN, on, ink.ToRgba().WithAlpha(FlightArcs.UnlitAlpha));
            if (!float.IsNaN(bingoAt)) Tick(b, radius, FlightArcs.At(bingoAt, true), bingoReached ? hot : dim);
            if (!float.IsNaN(jokerAt)) Tick(b, radius, FlightArcs.At(jokerAt, true), jokerReached ? hot : dim);
            s.Mesh.Commit();

            ScopeText t = s.Text;
            t.Begin();
            int n = low ? FlightArcs.PercentText(scratch, 0, percent) - 1 : 0;      // the number shows at caution only, its % apart
            Ends(t, new System.ReadOnlySpan<char>(scratch, 0, n), true, "FUEL", axis, radius, true, amber, ink);
            t.End();
        }

        private void PaintThrottle(Side s, float radius, float axis, float fill, float detent, string word, Color ink, Color amber)
        {
            Rgba hi = Lift(ink).ToRgba(), dim = ink.ToRgba().WithAlpha(HudInk.LineMid);
            AvQuadBuffer b = s.Mesh.Buffer;
            b.Clear();
            Stroke(b, radius, false, fill, ink.ToRgba(), detent, hi, ink.ToRgba().WithAlpha(FlightArcs.UnlitAlpha));
            if (!float.IsNaN(detent)) Tick(b, radius, FlightArcs.At(detent, false), fill > detent ? hi : dim);
            s.Mesh.Commit();

            ScopeText t = s.Text;
            t.Begin();
            string w = word ?? "";
            bool pct = w.Length > 1 && w[w.Length - 1] == '%';
            Ends(t, System.MemoryExtensions.AsSpan(w, 0, pct ? w.Length - 1 : w.Length), pct, "THROTTLE", axis, radius, false, Lift(ink), ink);
            t.End();
        }

        /// <summary>The whole arc: the fill from the empty end to <paramref name="fill"/> in <paramref name="on"/> (past <paramref name="detent"/> in <paramref name="hot"/>), the faint track on to the full end.</summary>
        private static void Stroke(AvQuadBuffer b, float radius, bool reversed, float fill, Rgba on, float detent, Rgba hot, Rgba track)
        {
            float split = float.IsNaN(detent) ? fill : Mathf.Min(fill, detent);
            Band(b, radius, FlightArcs.At(0f, reversed), FlightArcs.At(split, reversed), FlightArcs.LitWidth, on);
            if (fill > split) Band(b, radius, FlightArcs.At(split, reversed), FlightArcs.At(fill, reversed), FlightArcs.LitWidth, hot);
            Band(b, radius, FlightArcs.At(fill, reversed), FlightArcs.At(1f, reversed), FlightArcs.UnlitWidth, track);
        }

        /// <summary>
        /// One stretch of the circle from angle <paramref name="from"/> to <paramref name="to"/> (the arc's own degrees): a core <paramref name="width"/> wide and a <see cref="FlightArcs.Feather"/>
        /// soft edge each side, in steps of at most <see cref="FlightArcs.StepDeg"/> whose quads share their radial edges exactly, so the curve is one unbroken stroke.
        /// </summary>
        private static void Band(AvQuadBuffer b, float radius, float from, float to, float width, Rgba c)
        {
            float span = to - from;
            if (Mathf.Abs(span) < 0.01f) return;
            int steps = Mathf.CeilToInt(Mathf.Abs(span) / FlightArcs.StepDeg);
            float hw = width * 0.5f, inner = radius - hw, outer = radius + hw, f = FlightArcs.Feather;
            Rgba clear = c.WithAlpha(0f), halo = c.WithAlpha(c.A * b.GlowAlpha);
            float g = width * b.Glow;
            float ca = Mathf.Cos(from * Mathf.Deg2Rad), sa = Mathf.Sin(from * Mathf.Deg2Rad);
            for (int i = 1; i <= steps; i++)
            {
                float a = (from + span * i / steps) * Mathf.Deg2Rad, cb = Mathf.Cos(a), sb = Mathf.Sin(a);
                Ring(b, ca, sa, cb, sb, inner, outer, c, c);
                if (g <= 0f)
                {
                    Ring(b, ca, sa, cb, sb, outer, outer + f, c, clear);
                    Ring(b, ca, sa, cb, sb, inner, inner - f, c, clear);
                }
                else      // the HudGlow halo stands in for the soft edge: both stacked read as a thicker, greener band than the board's strokes
                {
                    Ring(b, ca, sa, cb, sb, outer, outer + g, halo, clear);      // the HudGlow halo, its reach following the band's width
                    Ring(b, ca, sa, cb, sb, inner, Mathf.Max(0f, inner - g), halo, clear);
                }
                ca = cb;
                sa = sb;
            }
        }

        /// <summary>One quad of a ring between two directions (cos/sin a, cos/sin b) and two radii, <paramref name="c0"/> on the first radius and <paramref name="c1"/> on the second.</summary>
        private static void Ring(AvQuadBuffer b, float ca, float sa, float cb, float sb, float r0, float r1, Rgba c0, Rgba c1) =>
            b.Add(ca * r0, sa * r0, cb * r0, sb * r0, cb * r1, sb * r1, ca * r1, sa * r1, c0, c0, c1, c1);

        /// <summary>A tick across the arc at angle <paramref name="a"/>: radial (square to the stroke), centred on the circle, <see cref="TickHalf"/> each side, standard weight with a soft edge.</summary>
        private static void Tick(AvQuadBuffer b, float radius, float a, Rgba c)
        {
            float cos = Mathf.Cos(a * Mathf.Deg2Rad), sin = Mathf.Sin(a * Mathf.Deg2Rad);
            AvStrokes.Line(b, (radius - TickHalf) * cos, (radius - TickHalf) * sin, (radius + TickHalf) * cos, (radius + TickHalf) * sin, HudStroke.Standard, c, FlightArcs.Feather);
        }

        /// <summary>The word's anchor in the holder's upright frame, text coordinates (y down): the point at angle <paramref name="a"/> in the arc's frame and distance <paramref name="r"/> from the reticle.</summary>
        private static void Anchor(float axis, float r, float a, out float x, out float y)
        {
            float rad = (axis + a) * Mathf.Deg2Rad;
            x = Snap(r * Mathf.Cos(rad));
            y = Snap(-r * Mathf.Sin(rad));
        }

        /// <summary>Device pixels per reference pixel (the holder's scale over the canvas's own unit): set every frame; a reference length is rounded to whole device pixels so no word sits between two.</summary>
        private static float devPerRef = 1f;

        /// <summary>The words' ink: the HUD green lifted toward white, as the board's and the game's own small labels are. In the pure saturated green its halo (the glow material takes the word's own colour) fattens a 12 px word into a smear.</summary>
        private static Color Lift(Color ink) => VanillaHudStyle.InkHi;      // INK-HI, the one lift

        private static float Snap(float refPx) => Mathf.Round(refPx * devPerRef) / devPerRef;

        /// <summary>
        /// The same on both arcs: the value just above the full (top) end, the caption (LABEL regular, <paramref name="captionColour"/>) centred just below the empty (bottom) end. A value with a <paramref name="percent"/> sign is its digits
        /// (VALUE bold) right-aligned at the anchor + 6 and a Dim % (LABEL) left-aligned at the anchor + 10; any other word (AIRBRAKE) is VALUE bold, centred.
        /// </summary>
        private static void Ends(ScopeText t, System.ReadOnlySpan<char> value, bool percent, string caption, float axis, float radius, bool reversed, Color valueColour, Color captionColour)
        {
            Anchor(axis, radius, FlightArcs.At(1f, reversed), out float x, out float y);
            float vy = y - Snap(EndGap + PercentPt * 0.5f);
            if (value.Length > 0)
            {
                if (percent)
                {
                    t.Draw(value, x + 6f, vy, PercentPt, ScopeAlign.Right, valueColour, true);
                    t.Draw("%", x + 10f, vy + 2f, CaptionPt, ScopeAlign.Left, captionColour, false);
                }
                else t.Draw(value, x, vy, PercentPt, ScopeAlign.Center, valueColour, true);
            }
            Anchor(axis, radius, FlightArcs.At(0f, reversed), out x, out y);
            t.Draw(caption, x, y + Snap(EndGap + CaptionPt * 0.5f), CaptionPt, ScopeAlign.Center, captionColour, false);      // the arcs' lifted ink, as the native labels round them; regular, as the board draws LABEL captions: bold with the text edge turned chunky in game
        }

        /// <summary>The game's gauges back, this layer down: the same frame, every time. Safe to call every frame.</summary>
        /// <summary>The reactive visor's look (<see cref="VisorFx"/>): the arcs' brightness and the whole layer's shift (the G jitter, a glitch's jump), by group alpha and one position write. Nothing is repainted.</summary>
        public void ApplyFx(float alpha, float x, float y)
        {
            if (root == null) return;
            if (group != null && group.alpha != alpha) group.alpha = alpha;
            if (shiftX != x || shiftY != y || shiftK != lastK)
            {
                shiftX = x; shiftY = y; shiftK = lastK;
                root.anchoredPosition = new Vector2(x, y) * (lastK > 0.01f ? lastK : 1f);      // reference pixels to the canvas's units (the holder is scaled by lastK)
            }
        }

        private CanvasGroup group;
        private float shiftX, shiftY, shiftK;

        public void Down()
        {
            natives.Show();
            drawing = false;
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        /// <summary>A paint or measure threw: the game's gauges come back and stay back until the next clean frame.</summary>
        public void Fail() => Down();

        public void Destroy()
        {
            natives.Dispose();
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null;
            canvasRoot = null;
            group = null;
            drawing = false;
        }
    }
}
