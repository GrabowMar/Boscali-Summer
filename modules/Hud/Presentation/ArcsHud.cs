using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The visor's two SEGMENTED ARCS round the reticle (2026-11-16, mockup <c>arcs(d)</c>): fuel on the left, throttle on the right, 22 segments each over 29 degrees, lit segments in full ink
    /// 8 px wide, unlit ones 5 px at 0.4. Fuel carries an amber BINGO tick ("B") and a white JOKER tick ("J") at the board's own numbers (<see cref="FuelState.Need"/>, plus
    /// <see cref="FuelEndurance.JokerMargin"/>) and its percentage at the top end; below BINGO the lit segments turn amber and the percentage says BINGO. Throttle carries the game's own afterburner
    /// detent tick (read off the game's gauge, none when the aircraft has none), its segments past it amber, and the game's own throttle word (79%, AIRBRAKE, AFTERBURNER) at the top end.
    /// <para>They replace the game's two plain arcs (<see cref="NativeFlightArcs"/>) and stand exactly where those are: centre, radius and axis are measured off the game's gauges every frame.
    /// FAIL OPEN: the game's gauges are hidden (renderer alpha only) in the very frame this layer has painted and is showing, and shown again the same frame it is not wanted (<c>want</c> false:
    /// switch off, external view, pause, map, a board tick gone stale), cannot measure, or throws (<see cref="Fail"/>). Built like <see cref="BrowHud"/>: a holder under the native HUD canvas
    /// scaled by canvas height / 1080, one quad mesh and pooled <see cref="ScopeText"/> labels per arc, a mesh rebuilt only when an int signature of what it draws changed (a moving throttle
    /// repaints once per segment, not per frame), no per-frame allocation.</para>
    /// </summary>
    internal sealed class ArcsHud
    {
        private const int Quads = 72;
        private const float BingoPrefixPt = 12f, PercentPt = 14f, CaptionPt = 12f, TickPt = 12f, TickLength = 18f, LabelInset = 28f, EndGap = 16f, CaptionDrop = 22f;
        private static readonly char[] BingoWord = { 'B', 'I', 'N', 'G', 'O', ' ' };

        private sealed class Side
        {
            public RectTransform Centre, Rot;
            public AvVector Mesh;
            public ScopeText Text;
            public int Signature = int.MinValue;
            public float X = float.NaN, Y, Axis = float.NaN;
        }

        private RectTransform canvasRoot, root;
        private readonly Side left = new Side(), right = new Side();
        private readonly NativeFlightArcs natives = new NativeFlightArcs();
        private readonly char[] scratch = new char[24];
        private float lastK;
        private bool drawing;

        // The 10 Hz read: the HUD aircraft's fuel and the board's BINGO need.
        private float level = float.NaN, need;
        private bool hasNeed, bingo;

        // What the last frame drew (the sim probe reads it).
        internal bool Shown => drawing;
        internal bool NativesHidden => natives.Hidden;
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
            root = CombatAlerts.Holder(canvasRoot, "BoscaliArcs");
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
            s.Mesh = AvVector.Create(s.Rot, "Segments", Quads);
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

            ControlInputs inputs = aircraft.GetInputs();
            float throttle = Mathf.Clamp01(inputs != null ? inputs.throttle : 0f);
            VanillaHudStyle.Palette palette = VanillaHudStyle.Colours;
            Color ink = palette.AllClear, amber = palette.Warning;
            bool low = bingo || level <= FuelEndurance.WarningLevel;

            Place(left, fuelArc, k);
            Place(right, throttleArc, k);
            FuelLit = FlightArcs.Lit(level);
            ThrottleLit = FlightArcs.Lit(throttle);
            int fuelPct = FlightArcs.Percent(level);
            float bingoAt = hasNeed ? need : float.NaN, jokerAt = hasNeed ? Mathf.Min(need + FuelEndurance.JokerMargin, 1f) : float.NaN;
            Color32 inkKey = ink, amberKey = amber;
            int colours = (inkKey.r << 16 | inkKey.g << 8 | inkKey.b) ^ (amberKey.r << 12 | amberKey.g << 4 | amberKey.b) * 31;
            float radius = fuelArc.Radius / k, thrRadius = throttleArc.Radius / k;

            int sigL = Mix(FuelLit, FlightArcs.Quant(bingoAt), FlightArcs.Quant(jokerAt), low ? 1 : 0, fuelPct, (int)(radius * 2f), (int)(fuelArc.AxisDeg * 2f), colours);
            if (sigL != left.Signature)
            {
                PaintFuel(left, radius, fuelArc.AxisDeg, FuelLit, bingoAt, jokerAt, low, fuelPct, ink, amber);
                left.Signature = sigL;
            }
            int wordKey = word != null ? word.GetHashCode() : 0;
            int sigR = Mix(ThrottleLit, FlightArcs.Quant(detent), 0, 0, wordKey, (int)(thrRadius * 2f), (int)(throttleArc.AxisDeg * 2f), colours);
            if (sigR != right.Signature)
            {
                PaintThrottle(right, thrRadius, throttleArc.AxisDeg, ThrottleLit, detent, word, ink, amber);
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
            float x = arc.X / k, y = arc.Y / k;
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

        private void PaintFuel(Side s, float radius, float axis, int lit, float bingoAt, float jokerAt, bool low, int percent, Color ink, Color amber)
        {
            Rgba on = (low ? amber : ink).ToRgba(), off = ink.ToRgba().WithAlpha(FlightArcs.UnlitAlpha);
            AvQuadBuffer b = s.Mesh.Buffer;
            b.Clear();
            Segments(b, radius, true, lit, on, off, float.NaN, amber.ToRgba());
            if (!float.IsNaN(bingoAt)) Tick(b, radius, FlightArcs.At(bingoAt, true), amber.ToRgba());
            if (!float.IsNaN(jokerAt)) Tick(b, radius, FlightArcs.At(jokerAt, true), new Rgba(1f, 1f, 1f));
            s.Mesh.Commit();

            ScopeText t = s.Text;
            t.Begin();
            Color word = low ? amber : ink;
            int n = 0;
            if (bingo)
            {
                for (; n < BingoWord.Length; n++) scratch[n] = BingoWord[n];
            }
            n = FlightArcs.PercentText(scratch, n, percent);
            Anchor(axis, radius + EndGap, FlightArcs.At(1f, true), out float x, out float y);
            t.Draw(new System.ReadOnlySpan<char>(scratch, 0, n), x, y, PercentPt, OutsideAlign(axis), word, true);
            Caption(t, "FUEL", axis, radius, FlightArcs.At(0f, true), ink);
            if (!float.IsNaN(bingoAt)) TickLabel(t, "B", axis, radius, FlightArcs.At(bingoAt, true), amber);
            if (!float.IsNaN(jokerAt)) TickLabel(t, "J", axis, radius, FlightArcs.At(jokerAt, true), Color.white);
            t.End();
        }

        private void PaintThrottle(Side s, float radius, float axis, int lit, float detent, string word, Color ink, Color amber)
        {
            Rgba on = ink.ToRgba(), off = ink.ToRgba().WithAlpha(FlightArcs.UnlitAlpha);
            AvQuadBuffer b = s.Mesh.Buffer;
            b.Clear();
            Segments(b, radius, false, lit, on, off, detent, amber.ToRgba());
            if (!float.IsNaN(detent)) Tick(b, radius, FlightArcs.At(detent, false), amber.ToRgba());
            s.Mesh.Commit();

            ScopeText t = s.Text;
            t.Begin();
            if (!string.IsNullOrEmpty(word))
            {
                Anchor(axis, radius + EndGap, FlightArcs.At(1f, false), out float x, out float y);
                t.Draw(word, x, y, PercentPt, OutsideAlign(axis), ink, true);
            }
            Caption(t, "THROTTLE", axis, radius, FlightArcs.At(0f, false), ink);
            t.End();
        }

        /// <summary>The 22 segments: <paramref name="lit"/> in <paramref name="on"/> 8 px wide (past <paramref name="detent"/> in <paramref name="hot"/>), the rest in <paramref name="off"/> 5 px.</summary>
        private static void Segments(AvQuadBuffer b, float radius, bool reversed, int lit, Rgba on, Rgba off, float detent, Rgba hot)
        {
            for (int i = 0; i < FlightArcs.Segments; i++)
            {
                FlightArcs.Segment(i, reversed, out float a0, out float a1);
                bool isLit = i < lit;
                Rgba c = isLit ? (!float.IsNaN(detent) && (i + 0.5f) / FlightArcs.Segments >= detent ? hot : on) : off;
                float r0 = a0 * Mathf.Deg2Rad, r1 = a1 * Mathf.Deg2Rad;
                AvStrokes.Line(b, radius * Mathf.Cos(r0), radius * Mathf.Sin(r0), radius * Mathf.Cos(r1), radius * Mathf.Sin(r1), isLit ? FlightArcs.LitWidth : FlightArcs.UnlitWidth, c);
            }
        }

        /// <summary>A tick across the arc at angle <paramref name="a"/>: from the circle toward the reticle (the mockup's 18 px, 2 px wide).</summary>
        private static void Tick(AvQuadBuffer b, float radius, float a, Rgba c)
        {
            float cos = Mathf.Cos(a * Mathf.Deg2Rad), sin = Mathf.Sin(a * Mathf.Deg2Rad);
            AvStrokes.Line(b, radius * cos, radius * sin, (radius - TickLength) * cos, (radius - TickLength) * sin, 2f, c);
        }

        /// <summary>The word's anchor in the holder's upright frame, text coordinates (y down): the point at angle <paramref name="a"/> in the arc's frame and distance <paramref name="r"/> from the reticle.</summary>
        private static void Anchor(float axis, float r, float a, out float x, out float y)
        {
            float rad = (axis + a) * Mathf.Deg2Rad;
            x = r * Mathf.Cos(rad);
            y = -r * Mathf.Sin(rad);
        }

        /// <summary>Words on the outside of the arc: right-aligned on the left arc, left-aligned on the right one.</summary>
        private static ScopeAlign OutsideAlign(float axis) => Mathf.Cos(axis * Mathf.Deg2Rad) < 0f ? ScopeAlign.Right : ScopeAlign.Left;

        private void Caption(ScopeText t, string word, float axis, float radius, float endAngle, Color ink)
        {
            Anchor(axis, radius, endAngle, out float x, out float y);
            t.Draw(word, x, y + CaptionDrop, CaptionPt, ScopeAlign.Center, ink.WithAlpha(0.95f), true);
        }

        private void TickLabel(ScopeText t, string word, float axis, float radius, float a, Color colour)
        {
            Anchor(axis, radius - LabelInset, a, out float x, out float y);
            t.Draw(word, x, y, TickPt, ScopeAlign.Center, colour, true);
        }

        /// <summary>The game's gauges back, this layer down: the same frame, every time. Safe to call every frame.</summary>
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
            drawing = false;
        }
    }
}
