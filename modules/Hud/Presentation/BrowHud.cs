using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The visor's BROW RAIL and FRAME FURNITURE (2026-11-16, mockup <c>evolved-hud-mockup.html</c>): a curved rail at the top centre (reference px x 800..1116, y 12..104) with its tick scale, the
    /// boresight mark, one status caption above it (real state only) and bearing carets hung under it for what matters now, relative to the aircraft's nose, most urgent first (<see cref="BrowCarets"/>):
    /// missiles inbound (red M1, M2), radars locking or painting us (amber LOCK, SPK), the ace on the hunt (red ACE), the objective (white OBJ), the runway or pad (RWY, PAD), the tanker (TKR) and the
    /// landing zone (LZ). The faint side-edge tick ladders, the two side cross marks and the two bottom rules are the furniture: static, about 0.3 alpha, drawn clear of every zone.
    /// Built like <see cref="CombatAlerts"/>: a holder under the native HUD canvas, scaled by canvas height / 1080, centre origin, its own mesh layers and pooled labels (<see cref="ScopeText"/>, so the
    /// glow material applies). Data at the board's 10 Hz (<see cref="Read"/>); a mesh is rebuilt only when what it draws changed, so a steady frame does no work but a compare. Fails open: nothing of
    /// the game's own HUD is touched.
    /// </summary>
    internal sealed class BrowHud
    {
        private const int RailQuads = 96, FurnitureQuads = 48, CurveSegments = 40;
        // Mockup weights: rail and major ticks about 1.5 px at full ink (clear against bright sky and cloud), minor ticks thinner and dimmer.
        private const float Stroke = 1f, CurveStroke = 1.6f, MajorStroke = 1.6f, MinorStroke = 1.2f, FurnitureAlpha = 0.3f;
        private const float CaptionY = 11f, CaptionPt = BrowRail.LabelPt;
        private static readonly string[] MissileLabels = { "M0", "M1", "M2", "M3", "M4", "M5", "M6", "M7", "M8", "M9" };

        private RectTransform canvasRoot, root, words;
        private AvVector rail, furniture;
        private ScopeText text;
        private readonly BrowCarets carets = new BrowCarets();
        private readonly char[] caption = new char[64], seen = new char[64], scratch = new char[16];
        private int captionLen, seenLen = -1, drawnSig = int.MinValue;
        private bool on, dirty;
        private float lastK, drawnRefW, drawnLadder = -1f, ladderTop = 300f;
        private Color drawnInk;

        /// <summary>What the layer shows now (the sim probe reads it).</summary>
        internal bool Shown => root != null && root.gameObject.activeSelf;
        internal int CaretCount => carets.Count;
        internal string CaptionText => captionLen > 0 ? new string(caption, 0, captionLen) : "";

        public bool Ensure()
        {
            if (root != null && canvasRoot != null) return true;
            root = null;
            if (!NativeRefs.HudCanvas(out _, out Canvas canvas)) return false;
            canvasRoot = (RectTransform)canvas.transform;
            VanillaHudStyle.TryHudFont(out TMPro.TMP_FontAsset font, out Material fontMaterial);
            root = CombatAlerts.Holder(canvasRoot, "BoscaliBrow");
            furniture = AvVector.Create(root, "Furniture", FurnitureQuads);      // first: behind the rail
            rail = AvVector.Create(root, "Rail", RailQuads);
            words = CombatAlerts.Holder(root, "Words");
            text = new ScopeText(words, font, fontMaterial);
            root.gameObject.SetActive(false);
            lastK = 0f;
            drawnSig = int.MinValue;
            drawnRefW = 0f;
            seenLen = -1;
            return true;
        }

        /// <summary>
        /// The board's 10 Hz read: this tick's carets and caption. <paramref name="show"/> false (the switch, an external view, a pause, the map) takes the layer down.
        /// <paramref name="scopeMode"/> is the board's page mode (0 AUTO, 1 MANUAL, 2 OFF).
        /// </summary>
        public void Read(bool show, Aircraft aircraft, BoardFrame frame, int scopeMode, IHudBoard board)
        {
            on = show && aircraft != null;
            if (!on) return;
            Transform own = aircraft.transform;
            float heading = own.eulerAngles.y;
            carets.Clear();
            if (ScopeFeeds.TryRead(out ThreatScope threat))
            {
                for (int i = 0; i < threat.MissileCount; i++)
                {
                    ThreatMissile m = threat.Missiles[i];
                    if (!m.Live) continue;
                    carets.Add(m.BearingDeg, BrowTone.Red, MissileLabels[Mathf.Clamp(m.Number, 0, 9)], float.IsNaN(m.TtiS) ? 90f : Mathf.Min(m.TtiS, 90f));
                }
                for (int i = 0; i < threat.EmitterCount; i++)
                {
                    ThreatEmitter e = threat.Emitters[i];
                    carets.Add(e.BearingDeg, BrowTone.Amber, e.Lock ? "LOCK" : "SPK", e.Lock ? 100f : 300f + Mathf.Min(e.RangeM * 0.001f, 99f));
                }
            }
            if (ModuleServices.TryGet(out ISquadView squad) && !(squad is Object gone && gone == null) && squad.HuntActive && squad.TryAce(out Aircraft ace, out _) && ace != null)
            {
                Vector3 d = ace.transform.position - own.position;
                carets.Add(BrowRail.RelativeBearing(d.x, d.z, heading), BrowTone.Red, "ACE", 200f);
            }
            if (ScopeFeeds.TryRead(out DockScope dock)) carets.Add(dock.TankerBearingDeg, BrowTone.Bright, "TKR", 400f);
            if (ScopeFeeds.TryRead(out ApproachScope approach) && approach.HasBearing) carets.Add(approach.BearingDeg, BrowTone.Bright, approach.Pad ? "PAD" : "RWY", 410f);
            if (ScopeFeeds.TryRead(out ObjectiveScope objective) && objective.HasMarker)
                carets.Add(BrowRail.RelativeBearing(objective.X - objective.OwnX, objective.Z - objective.OwnZ, objective.OwnHeading), BrowTone.White, "OBJ", 500f);
            if (ModuleServices.TryGet(out ILandingZone zone) && !(zone is Object lost && lost == null) && zone.HasLocal && zone.TryGetLocal(out GlobalPosition at))
            {
                Vector3 d = at - aircraft.GlobalPosition();
                carets.Add(BrowRail.RelativeBearing(d.x, d.z, heading), BrowTone.Bright, "LZ", 520f);
            }
            carets.Resolve();

            CameraStateManager state = SceneSingleton<CameraStateManager>.i;
            Camera cam = state != null ? state.mainCamera : null;
            captionLen = BrowRail.Caption(caption, scopeMode, frame.Systems.NightVision, cam != null ? cam.fieldOfView : float.NaN);

            // The right edge's ladder starts under the board when its box reaches that edge.
            float bottom = 0f;
            if (board != null && board.TryGetScreenBox(out float l, out float t, out float r, out float b) && r > l && Screen.height > 0 && r >= Screen.width - 40f)
                bottom = b * 1080f / Screen.height;
            ladderTop = BrowRail.LadderTop(bottom);
        }

        /// <summary>Every frame: show or hide, follow the screen's size and repaint when what is drawn changed; nothing else.</summary>
        public void Frame()
        {
            if (root == null) return;
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            if (!on) return;
            float k = canvasRoot.rect.height / 1080f;
            if (!(k > 0.01f)) return;
            if (k != lastK)
            {
                root.localScale = new Vector3(k, k, 1f);
                lastK = k;
            }
            float refW = canvasRoot.rect.width / k;
            Color ink = VanillaHudStyle.Colours.AllClear;
            int sig = carets.Signature();
            bool captionChanged = seenLen != captionLen || !SameCaption();
            if (!dirty && !captionChanged && sig == drawnSig && refW == drawnRefW && ladderTop == drawnLadder && ink == drawnInk) return;
            Paint(refW, ink);
            dirty = false;
            drawnSig = sig;
            drawnRefW = refW;
            drawnLadder = ladderTop;
            drawnInk = ink;
            System.Array.Copy(caption, seen, captionLen);
            seenLen = captionLen;
        }

        private bool SameCaption()
        {
            for (int i = 0; i < captionLen; i++) if (caption[i] != seen[i]) return false;
            return true;
        }

        // Rail units (x from the centre line, y down from the top) to the holder's (y up from the centre).
        private static float Y(float railY) => 540f - railY;

        private void Paint(float refW, Color ink)
        {
            VanillaHudStyle.Palette palette = VanillaHudStyle.Colours;
            Rgba c = ink.ToRgba();
            AvQuadBuffer b = rail.Buffer;
            b.Clear();
            BrowRail.Point(0f, out float px, out float py);
            for (int i = 1; i <= CurveSegments; i++)
            {
                BrowRail.Point(i / (float)CurveSegments, out float x, out float y);
                AvStrokes.Line(b, px, Y(py), x, Y(y), CurveStroke, c);
                px = x; py = y;
            }
            for (int i = 0; i < BrowRail.TickCount; i++)
            {
                BrowRail.Tick(i, out float x, out float y, out float nx, out float ny, out bool major);
                float len = major ? 10f : 5f;
                AvStrokes.Line(b, x, Y(y), x + nx * len, Y(y + ny * len), major ? MajorStroke : MinorStroke, c.WithAlpha(major ? 1f : 0.6f));
            }
            Triangle(b, 0f, BrowRail.BoreTop, BrowRail.BoreHalf, BrowRail.BoreBase - BrowRail.BoreTop, c);
            for (int i = 0; i < carets.Count; i++)
            {
                BrowCaret k = carets[i];
                Triangle(b, k.X, k.Y + BrowRail.CaretTop, BrowRail.CaretHalf, BrowRail.CaretBase - BrowRail.CaretTop, Of(k.Tone, palette).ToRgba());
            }
            rail.Commit();

            text.Begin();
            text.Draw(new System.ReadOnlySpan<char>(caption, 0, captionLen), 0f, CaptionY - 540f, CaptionPt, ScopeAlign.Center, ink.WithAlpha(0.95f), true);
            for (int i = 0; i < carets.Count; i++)
            {
                BrowCaret k = carets[i];
                string label = k.Label;
                label.CopyTo(0, scratch, 0, label.Length);
                int n = label.Length;
                if (k.Beyond != 0) scratch[n++] = k.Beyond > 0 ? '>' : '<';
                text.Draw(new System.ReadOnlySpan<char>(scratch, 0, n), k.LabelX, k.Y + BrowRail.LabelDrop - 540f, BrowRail.LabelPt, ScopeAlign.Center, Of(k.Tone, palette), true);
            }
            text.End();

            PaintFurniture(refW, c.WithAlpha(FurnitureAlpha));
        }

        /// <summary>The glass: tick ladders down both sides (300..800 every 50, longer every 100), a small cross at the middle of each side, two short rules near the bottom. Everything on the left stays in the 12 px margin
        /// left of the column zones, the right ladder starts under the board when it reaches that edge, so nothing crosses a zone.</summary>
        private void PaintFurniture(float refW, Rgba c)
        {
            AvQuadBuffer b = furniture.Buffer;
            b.Clear();
            float left = -refW * 0.5f + 2f, right = refW * 0.5f - 2f;
            for (int y = 300; y <= 800; y += 50)
            {
                float len = y % 100 == 0 ? 10f : 6f;
                AvStrokes.Line(b, left, Y(y), left + len, Y(y), Stroke, c);
                if (y >= ladderTop) AvStrokes.Line(b, right, Y(y), right - len, Y(y), Stroke, c);
            }
            AvStrokes.Line(b, left, Y(540f), left + 10f, Y(540f), Stroke, c);
            AvStrokes.Line(b, left + 5f, Y(533f), left + 5f, Y(547f), Stroke, c);
            AvStrokes.Line(b, right, Y(540f), right - 10f, Y(540f), Stroke, c);
            AvStrokes.Line(b, right - 5f, Y(533f), right - 5f, Y(547f), Stroke, c);
            AvStrokes.Line(b, -370f, Y(1070f), -310f, Y(1070f), Stroke, c.WithAlpha(c.A * 0.6f));
            AvStrokes.Line(b, 310f, Y(1070f), 370f, Y(1070f), Stroke, c.WithAlpha(c.A * 0.6f));
            furniture.Commit();
        }

        private static Color Of(BrowTone tone, VanillaHudStyle.Palette palette) =>
            tone == BrowTone.Red ? palette.Alert : tone == BrowTone.Amber ? palette.Warning : tone == BrowTone.White ? Color.white : Color.Lerp(palette.AllClear, Color.white, ScopePalette.HiLift);

        /// <summary>A filled triangle (apex up on the screen) at rail units: the apex at (<paramref name="x"/>, <paramref name="topY"/>), its base <paramref name="depth"/> lower and <paramref name="half"/> either side.</summary>
        private static void Triangle(AvQuadBuffer b, float x, float topY, float half, float depth, Rgba c) =>
            b.Add(x, Y(topY), x - half, Y(topY + depth), x + half, Y(topY + depth), x + half, Y(topY + depth), c, c, c, c);

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
            on = false;
        }
    }
}
