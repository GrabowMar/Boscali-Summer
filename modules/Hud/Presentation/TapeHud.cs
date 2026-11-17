using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using HarmonyLib;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// THREAT MARKS ON THE GAME'S HEADING TAPE (2026-11-16, concept A): every bearing that matters now hangs under the native heading tape at its true bearing as a small triangle with a short word under it, most urgent first
    /// (<see cref="TapeMarkSet"/>): missiles inbound (WARNING, "M1 6S"), radars locking (CAUTION, an open amber triangle, LOCK) or painting us (CAUTION, SPK), the ace on the hunt (CAUTION, ACE), the objective (OBJ), the runway or pad (RWY, PAD), the tanker (TKR)
    /// and the landing zone (LZ). A bearing past the tape's end stands at that end with a chevron pointing out. The tape is the game's own: its <c>FlightHud.compass</c> RawImage, a window of <c>uvRect.width * 360</c> degrees whose middle is
    /// the nose; its place on the screen is measured from the RawImage's rectangle every frame (<see cref="Measure"/>), so the marks follow it wherever the game puts it. Where it cannot be found or measured, or is hidden (an external
    /// view, a suppressed HUD), nothing is drawn: it never guesses. Nothing native is touched. Our own holder under the HUD canvas, scaled by canvas height / 1080; per frame only the marks' holders move (the tape scrolls with the heading),
    /// and a mesh or a word is written only when what it shows changed. The visor's rim, side ticks and etched line are <see cref="VisorRim"/>'s.
    /// </summary>
    internal sealed class TapeHud
    {
        private static readonly AccessTools.FieldRef<FlightHud, RawImage> CompassRef = NativeRefs.Field<FlightHud, RawImage>("compass");
        /// <summary>How far above the RawImage's bottom edge the tape's tick line runs (reference px), by capture.</summary>
        private const float TickRise = 0f;
        private const float ChevronStroke = HudStroke.Standard;

        private sealed class Slot
        {
            public RectTransform Holder;
            public AvVector Shape;
            public ScopeText Text;
            public int Beyond = 2;
            public float X = float.NaN, Y = float.NaN;
        }

        private RectTransform canvasRoot, root;
        private Camera ownCam, tapeCam;
        private RawImage compass;
        private CanvasGroup group;
        private readonly Slot[] slots = new Slot[TapeLayout.MaxMarks + 1];
        private readonly TapeMarkSet marks = new TapeMarkSet();
        private readonly string[] heardLabels = new string[TapeLayout.MaxMarks];
        private readonly Vector3[] corners = new Vector3[4];
        private int heardCount, drawnSig = int.MinValue;
        private bool heardPrimed, on, dirty, tapeOk;
        private Transform own;
        private float lastK, jitterDeg, alpha = 1f;
        private Color drawnAlert, drawnCaution, drawnHi, drawnInk;
        private TapeFrame tape;

        /// <summary>How far the power-on has come: the marks wait until it is 1 (a self-test pip sweeps the tape meanwhile).</summary>
        public float Boot = 1f;

        /// <summary>The layer's holder (the visor glitch shader's material goes onto the graphics under it; null until built).</summary>
        internal Transform Root => root;
        internal bool Shown => root != null && root.gameObject.activeSelf;
        internal int MarkCount => marks.Count;
        internal bool TapeFound => tapeOk;

        /// <summary>What the sim probe reads: the measured tape and every mark (word, true bearing, x, side).</summary>
        internal string Describe()
        {
            var s = new System.Text.StringBuilder(96);
            s.Append("tape ").Append(tapeOk ? "ok cx=" + tape.CentreX.ToString("0.#") + " y=" + tape.TickY.ToString("0.#") + " ppd=" + tape.PxPerDeg.ToString("0.###") + " half=" + tape.HalfRangeDeg.ToString("0.#") : "none");
            float heading = own != null ? own.eulerAngles.y : 0f;
            for (int i = 0; i < marks.Count; i++)
            {
                TapeMark m = marks[i];
                float x = TapeBearings.X(m.Abs - heading + jitterDeg, tape, out int beyond);
                s.Append(" | ").Append(m.Label).Append(" abs=").Append(m.Abs.ToString("0")).Append(" x=").Append(x.ToString("0.#")).Append(" beyond=").Append(beyond).Append(m.Low ? " low" : "");
            }
            return s.ToString();
        }

        /// <summary>The reactive visor's look: the layer's brightness. The marks do not shift with the visor's jitter: the game's tape does not.</summary>
        public void ApplyFx(float a)
        {
            alpha = a;
            if (group != null && group.alpha != a) group.alpha = a;
        }

        /// <summary>The glitch's display jitter of the marks' bearings in degrees (a jammed visor: a degree or two).</summary>
        public void ApplyGlitch(float jitter) => jitterDeg = jitter;

        public bool Ensure()
        {
            if (root != null && canvasRoot != null) return true;
            root = null;
            if (!NativeRefs.HudCanvas(out _, out Canvas canvas)) return false;
            canvasRoot = (RectTransform)canvas.transform;
            ownCam = CameraOf(canvas);
            VanillaHudStyle.TryHudFont(out TMPro.TMP_FontAsset font, out Material fontMaterial);
            root = CombatAlerts.Holder(BoscaliSummer.Core.Ui.HudLayer.Ensure() ? BoscaliSummer.Core.Ui.HudLayer.Motion : canvasRoot, "BoscaliTape");
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
            group.alpha = alpha;
            for (int i = 0; i < slots.Length; i++)
            {
                var s = new Slot { Holder = CombatAlerts.Holder(root, i < TapeLayout.MaxMarks ? "Mark" : "Sweep") };
                s.Shape = AvVector.Create(s.Holder, "Shape", 8);
                s.Text = new ScopeText(s.Holder, font, fontMaterial);
                s.Holder.gameObject.SetActive(false);
                slots[i] = s;
            }
            root.gameObject.SetActive(false);
            compass = null;
            lastK = 0f;
            drawnSig = int.MinValue;
            return true;
        }

        private static Camera CameraOf(Canvas canvas)
        {
            Canvas top = canvas.rootCanvas;
            return top.renderMode == RenderMode.ScreenSpaceOverlay ? null : top.worldCamera;
        }

        /// <summary>
        /// The board's 10 Hz read: this tick's marks. <paramref name="show"/> false (the switch, an external view, a pause, the map) takes the layer down.
        /// </summary>
        public void Read(bool show, Aircraft aircraft)
        {
            on = show && aircraft != null;
            if (!on) { heardPrimed = false; marks.Clear(); return; }
            own = aircraft.transform;
            float heading = own.eulerAngles.y;
            marks.Clear();
            if (!tapeOk) { HearNew(); return; }      // no tape, no marks (and nothing heard)
            if (ScopeFeeds.TryRead(out ThreatScope threat))
            {
                for (int i = 0; i < threat.MissileCount; i++)
                {
                    ThreatMissile m = threat.Missiles[i];
                    if (!m.Live) continue;
                    marks.Add(heading + m.BearingDeg, TapeTone.Warning, TapeLabels.Missile(m.Number), float.IsNaN(m.TtiS) ? 90f : Mathf.Min(m.TtiS, 90f));
                }
                for (int i = 0; i < threat.EmitterCount; i++)
                {
                    ThreatEmitter e = threat.Emitters[i];
                    marks.Add(heading + e.BearingDeg, TapeTone.Caution, e.Lock ? "LOCK" : "SPK", e.Lock ? 100f : 300f + Mathf.Min(e.RangeM * 0.001f, 99f));
                }
            }
            if (ModuleServices.TryGet(out ISquadView squad) && !(squad is Object gone && gone == null) && squad.HuntActive && squad.TryAce(out Aircraft ace, out _) && ace != null)
            {
                Vector3 d = ace.transform.position - own.position;
                marks.Add(TapeBearings.CompassBearing(d.x, d.z), TapeTone.Caution, "ACE", 200f);
            }
            if (ScopeFeeds.TryRead(out DockScope dock)) marks.Add(heading + dock.TankerBearingDeg, TapeTone.Hi, "TKR", 400f);
            if (ScopeFeeds.TryRead(out ApproachScope approach) && approach.HasBearing) marks.Add(heading + approach.BearingDeg, TapeTone.Hi, approach.Pad ? "PAD" : "RWY", 410f);
            if (ScopeFeeds.TryRead(out ObjectiveScope objective) && objective.HasMarker)
                marks.Add(TapeBearings.CompassBearing(objective.X - objective.OwnX, objective.Z - objective.OwnZ), TapeTone.Hi, "OBJ", 500f);
            if (ModuleServices.TryGet(out ILandingZone zone) && !(zone is Object lost && lost == null) && zone.HasLocal && zone.TryGetLocal(out GlobalPosition at))
            {
                Vector3 d = at - aircraft.GlobalPosition();
                marks.Add(TapeBearings.CompassBearing(d.x, d.z), TapeTone.Hi, "LZ", 520f);
            }
            marks.Resolve(heading, tape);
            HearNew();
        }

        /// <summary>A tick (HMD-9) when a mark that was not on the tape at the last read appears: a new missile, emitter, ace or objective, never a refresh of one already there, and not while the power-on runs.</summary>
        private void HearNew()
        {
            bool fresh = false;
            int used = 0;
            for (int i = 0; i < marks.Count && !fresh; i++)
            {
                bool found = false;
                for (int j = 0; j < heardCount && !found; j++)
                    if ((used & (1 << j)) == 0 && ReferenceEquals(heardLabels[j], marks[i].Label)) { used |= 1 << j; found = true; }
                fresh = !found;
            }
            for (int i = 0; i < marks.Count; i++) heardLabels[i] = marks[i].Label;
            heardCount = marks.Count;
            if (fresh && heardPrimed && Boot >= 1f) BoscaliSummer.Core.Fx.VisorSound.Play(BoscaliSummer.Core.Fx.VisorClip.Caret);
            heardPrimed = true;
        }

        /// <summary>Every frame: measure the tape, show or hide, follow its scroll and repaint when what is drawn changed.</summary>
        public void Frame()
        {
            if (root == null) return;
            float k = canvasRoot.rect.height / 1080f;
            if (k > 0.01f && k != lastK) { root.localScale = new Vector3(k, k, 1f); lastK = k; }
            tapeOk = on && lastK > 0.01f && Measure(out tape);
            bool show = tapeOk;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!show) return;
            float heading = own != null ? own.eulerAngles.y : 0f;
            VanillaHudStyle.Palette palette = VanillaHudStyle.Colours;
            Color alert = palette.Alert, caution = palette.Warning, hi = VanillaHudStyle.InkHi, ink = palette.AllClear;
            bool booting = Boot < 1f;
            int shown = booting ? 0 : marks.Count;
            int sig = marks.Signature() * 31 + shown;
            bool repaint = dirty || sig != drawnSig || alert != drawnAlert || caution != drawnCaution || hi != drawnHi || ink != drawnInk;
            drawnSig = sig; drawnAlert = alert; drawnCaution = caution; drawnHi = hi; drawnInk = ink;
            dirty = false;
            for (int i = 0; i < TapeLayout.MaxMarks; i++)
            {
                Slot s = slots[i];
                if (i >= shown) { if (s.Holder.gameObject.activeSelf) s.Holder.gameObject.SetActive(false); s.Beyond = 2; continue; }
                TapeMark m = marks[i];
                float x = TapeBearings.X(m.Abs - heading + jitterDeg, tape, out int beyond);
                if (!s.Holder.gameObject.activeSelf) s.Holder.gameObject.SetActive(true);
                Place(s, x, tape.TickY);
                Color tone = m.Tone == TapeTone.Warning ? alert : m.Tone == TapeTone.Caution ? caution : hi;
                if (repaint || beyond != s.Beyond)
                {
                    s.Beyond = beyond;
                    Shape(s, beyond, m.Tone);
                    s.Shape.color = tone;
                    s.Text.Begin();
                    bool nav = m.Tone == TapeTone.Hi;      // a navigation mark's word is regular Ink; a threat's is bold in its tone
                    s.Text.Draw(System.MemoryExtensions.AsSpan(m.Label), m.LabelDx, m.Low ? TapeLayout.LowLabelDrop : TapeLayout.LabelDrop, HudType.Label, ScopeAlign.Center, nav ? ink : tone, !nav);
                    s.Text.End();
                }
            }
            // The power-on's self-test: one pip travels the tape's tick line once, where the marks will hang.
            Slot sweep = slots[TapeLayout.MaxMarks];
            bool sweeping = booting && Boot > 0f;
            if (sweep.Holder.gameObject.activeSelf != sweeping) sweep.Holder.gameObject.SetActive(sweeping);
            if (sweeping)
            {
                float x = tape.CentreX + (Boot * 2f - 1f) * tape.HalfRangeDeg * tape.PxPerDeg;
                Place(sweep, x, tape.TickY);
                if (sweep.Beyond != 0) { sweep.Beyond = 0; Shape(sweep, 0, TapeTone.Warning); sweep.Shape.color = hi; }
            }
        }

        private static void Place(Slot s, float x, float y)
        {
            if (s.X == x && s.Y == y) return;
            s.X = x; s.Y = y;
            s.Holder.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>The mark's solid triangle hanging under the tick line (apex up), and past an end of the tape a chevron beside it pointing out. White vertices: the slot's colour tints them.</summary>
        private static void Shape(Slot s, int beyond, TapeTone tone)
        {
            AvQuadBuffer b = s.Shape.Buffer;
            b.Clear();
            Rgba white = new Rgba(1f, 1f, 1f, 1f);
            float top = -TapeLayout.TriGap, bottom = top - TapeLayout.TriDepth, mid = (top + bottom) * 0.5f, h = TapeLayout.TriHalf;
            if (beyond != 0)
            {
                // Past the tape's end: one crisp chevron pointing out, in place of the symbol; the word stays under it.
                AvStrokes.Chevron(b, beyond * TapeLayout.ChevronOut * 0.5f, mid, TapeLayout.ChevronSize, beyond > 0 ? 0f : 180f, ChevronStroke, white);
            }
            else if (tone == TapeTone.Warning) b.Add(0f, top, -h, bottom, h, bottom, h, bottom, white, white, white, white);      // threat: solid triangle
            else if (tone == TapeTone.Caution)      // spike / ace: open triangle
            {
                AvStrokes.Line(b, 0f, top, -h, bottom, ChevronStroke, white);
                AvStrokes.Line(b, 0f, top, h, bottom, ChevronStroke, white);
                AvStrokes.Line(b, -h, bottom, h, bottom, ChevronStroke, white);
            }
            else AvStrokes.Diamond(b, 0f, mid, h * 0.85f, ChevronStroke, white);      // navigation: diamond
            s.Shape.Commit();
        }

        /// <summary>
        /// The native tape from the game's own RawImage: its rectangle's corners through the screen into this holder's units (the two canvases may differ), its width over the degrees its uvRect shows, the middle of it the nose.
        /// False when the game has no tape yet, it is not drawn, or what it reports cannot be a tape.
        /// </summary>
        private bool Measure(out TapeFrame f)
        {
            f = default;
            if (compass == null)
            {
                compass = NativeRefs.Get(CompassRef, SceneSingleton<FlightHud>.i);
                if (compass == null) return false;
                tapeCam = CameraOf(compass.canvas);
            }
            if (!compass.isActiveAndEnabled || compass.canvasRenderer.GetInheritedAlpha() < 0.05f) return false;
            compass.rectTransform.GetWorldCorners(corners);
            if (!Local(corners[0], out Vector2 bl) || !Local(corners[2], out Vector2 tr)) return false;
            float width = (tr.x - bl.x) / lastK, deg = compass.uvRect.width * 360f;
            if (!(width > 20f) || !(deg > 5f) || !(deg <= 360f)) return false;
            f.CentreX = (bl.x + tr.x) * 0.5f / lastK;
            f.PxPerDeg = width / deg;
            f.HalfRangeDeg = deg * 0.5f;
            f.TickY = bl.y / lastK + TickRise;
            return true;
        }

        private bool Local(Vector3 world, out Vector2 local)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(tapeCam, world);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, screen, ownCam, out local)) return false;
            local -= canvasRoot.rect.center;
            return true;
        }

        public void Hide()
        {
            on = false;
            if (root != null) root.gameObject.SetActive(false);
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            canvasRoot = null;
            group = null;
            compass = null;
            on = false;
        }
    }
}
