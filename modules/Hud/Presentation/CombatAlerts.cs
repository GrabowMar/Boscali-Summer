using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Patches;
using BoscaliSummer.Modules.Hud.Runtime;
using HarmonyLib;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The centre combat layer (2026-10-28, docs/superpowers/specs/2026-10-28-ace-alerts-design.md): what a pilot in a fight needs where they are looking.
    /// <list type="bullet">
    /// <item>WARNING LINE in the top-centre lane (<see cref="TopCentre"/>), above the game's heading tape: the most urgent alert (<see cref="AlertBoard"/>) as ONE row with a class bar, no wash (no frame): its
    /// word (VALUE, bold, in the alert's tone), the detail (BODY, Ink: the seeker, the radar's name, TERRAIN and seconds) and the call at the right (VALUE, bold, Hi: BEAM / CHAFF / FLARE, RTB), with the fuse
    /// (time left) as a line under the row. Warnings in the theme's alert colour, cautions (LOCKED, BINGO FUEL) in its caution colour. The jammed visor's EW JAM stands at the slot's left end while no alert is up.</item>
    /// <item>THREAT RING round the screen centre (<see cref="ThreatRing"/>): a double chevron toward every live inbound missile labelled "M1 6S", an amber diamond (no word) toward a radar locking us.</item>
    /// <item>SHOT CALLOUT under the reticle (<see cref="ShotCallouts"/>): HIT / DESTROYED / MISSED / DECOYED, no box; only DESTROYED names its target (and keeps its two side rules).</item>
    /// </list>
    /// Our own objects under the native HUDCanvas (vanilla owns visibility), in the native HUD face, laid out on a 1080-high reference screen and scaled to the real one, with the
    /// lane's one set of strokes, brackets, sizes and motion. While it is up it hides the game's own missile list ("Missile [ARH] 4.9km" above the map), which the banner and the
    /// ring now say better, through a group it owns (alpha back to 1 whenever the layer is down). Data at the board's 10 Hz (<see cref="Read"/>); blink, ring and pop every frame. The bearings of the threats
    /// hang on the heading tape itself (<see cref="TapeHud"/>).
    /// </summary>
    internal sealed class CombatAlerts
    {
        private const int RingMax = 6;
        /// <summary>The callout's centre under the screen centre, reference pixels.</summary>
        internal const float CalloutDown = 120f;
        /// <summary>The banner's class bar width; the share of the size gap its smaller word drops to sit on the big words' baseline.</summary>
        private const float Flank = 40f, BaselineDrop = 0.25f;      // Flank: how far each tone hairline runs out past the row
        /// <summary>Between an icon and its word; how far the screen-edge glow reaches in. Tracking of the callout's words (the ring labels and the callout's sub line track like LABEL).</summary>
        private const float IconGap = 8f, EdgeDepth = 90f, WordSpacing = 10f, LineSpacing = 2f;
        private static readonly AccessTools.FieldRef<CombatHUD, ThreatList> ThreatListRef = NativeRefs.Field<CombatHUD, ThreatList>("threatList");

        private RectTransform canvasRoot, root, line, callout;
        /// <summary>The layer's holder (the visor glitch shader's material goes onto the graphics under it; null until built).</summary>
        internal Transform Root => root;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private TextMeshProUGUI kindWord, middle, call, calloutWord, calloutSub;
        private TMP_Text calloutIcon;
        /// <summary>The line's words as last written and their widths (set when the text changes, not per frame), the widest the content has been while this alert stayed up (the box never shrinks and shifts back).</summary>
        private float kindW, middleW, callW, stickyContent;
        private string middleShown = "";
        private bool lineUp;
        private readonly TextMeshProUGUI[] ringLabels = new TextMeshProUGUI[RingMax];
        private readonly int[] ringKeys = new int[RingMax];
        private CanvasGroup calloutGroup, nativeList;
        private AvVector edgeLines, lineLines, ringLines, calloutLines;
        private float lastK;

        private readonly AlertBoard board = new AlertBoard();
        private AlertInputs inputs;
        private readonly char[] buf = new char[96];
        private bool on;
        private int kindKey = -1, detailKey, calloutVersion = -1, ledgerVersion = -1;
        private float lastCalled = float.PositiveInfinity;

        private TextMeshProUGUI jamWord;
        private bool jam;

        private readonly Missile[] missiles = new Missile[RingMax];
        private readonly int[] numbers = new int[RingMax];

        /// <summary>What the layer shows now (the sim probe in <c>HudAutomation</c> reads it).</summary>
        internal bool Shown => root != null && root.gameObject.activeSelf;
        internal bool TerrainAlert => on && board.IsUp(AlertKind.PullUp);
        /// <summary>A real warning is up right now (PULL UP, MISSILE, FIRE, ENGINE OUT, LOCKED): the visor's power-on is cut short for it. BINGO FUEL is a caution the self-test reports itself and does not count.</summary>
        internal bool WarningUp => on && (board.IsUp(AlertKind.PullUp) || board.IsUp(AlertKind.Missile) || board.IsUp(AlertKind.Fire) || board.IsUp(AlertKind.EngineOut) || board.IsUp(AlertKind.Locked));
        internal string LineWord => line != null && line.gameObject.activeSelf ? kindWord.text : "";
        internal string DetailText => line != null && line.gameObject.activeSelf ? middle.text : "";
        internal string CallText => line != null && line.gameObject.activeSelf ? call.text : "";
        internal bool JamShown => jamWord != null && jamWord.gameObject.activeSelf;

        /// <summary>The jammed visor's word (<see cref="VisorFx.JamUp"/>): EW JAM at the slot's left end while no alert is up.</summary>
        public void SetJam(bool up) => jam = up;
        internal int RingMarks { get; private set; }
        internal string CalloutText => callout != null && callout.gameObject.activeSelf ? calloutWord.text : "";
        internal bool NativeListHidden => nativeList != null && nativeList.alpha == 0f;

        public bool Ensure()
        {
            if (root != null && canvasRoot != null) return true;
            root = null;
            if (!NativeRefs.HudCanvas(out _, out Canvas canvas)) return false;
            canvasRoot = (RectTransform)canvas.transform;
            VanillaHudStyle.TryHudFont(out font, out fontMaterial);

            root = Holder(canvasRoot, "BoscaliCombatAlerts");
            edgeLines = AvVector.Create(root, "EdgeGlow", 32);      // first: behind every other piece
            ringLines = AvVector.Create(root, "Ring", 160);
            for (int i = 0; i < RingMax; i++)
            {
                ringLabels[i] = Text(root, "RingLabel", TopCentre.ValuePt, LineSpacing, bold: true);
                ringLabels[i].rectTransform.sizeDelta = new Vector2(112f, TopCentre.ValuePt * 1.5f);
                ringLabels[i].gameObject.SetActive(false);
                ringKeys[i] = int.MinValue;
            }

            // The warning line: the ticks and the fuse in one mesh, then the three words (kind at the left, detail after it, the call at the right).
            line = Holder(root, "Line");
            lineLines = AvVector.Create(line, "Lines", 16);
            kindWord = Word(line, "Kind", TopCentre.ValuePt, HudStep.Value, 0f);
            middle = Word(line, "Detail", TopCentre.DetailPt, HudStep.Body, 0f);
            middle.overflowMode = TextOverflowModes.Ellipsis;
            call = Word(line, "Call", TopCentre.ValuePt, HudStep.Value, 1f);

            callout = Holder(root, "Callout");
            calloutGroup = callout.gameObject.AddComponent<CanvasGroup>();
            calloutGroup.blocksRaycasts = false;
            calloutGroup.interactable = false;
            calloutLines = AvVector.Create(callout, "Lines", 16);
            calloutWord = Text(callout, "Word", TopCentre.NamePt, WordSpacing, bold: true);
            calloutIcon = Icon(callout, TopCentre.NamePt);
            calloutSub = Text(callout, "Sub", TopCentre.LabelPt, LineSpacing);
            calloutSub.rectTransform.anchoredPosition = new Vector2(0f, -TopCentre.NamePt * 0.7f - TopCentre.LabelPt);      // under the word

            jamWord = Word(root, "Jam", TopCentre.LabelPt, HudStep.Label, 0f);
            jamWord.text = JamText;
            jamWord.gameObject.SetActive(false);

            line.gameObject.SetActive(false);
            callout.gameObject.SetActive(false);
            root.gameObject.SetActive(false);
            lastK = 0f;
            kindKey = calloutVersion = -1;
            detailKey = 0;      // a rebuilt layer writes its words again
            middleShown = "";
            return true;
        }

        /// <summary>
        /// The board's 10 Hz read: this tick's alert conditions off the board's frame and the threat watch, the banner's detail line, and the shot tracker's new results.
        /// <paramref name="show"/> false (the switch, a pause, the map) drops every alert, so nothing replays when it comes back.
        /// </summary>
        public void Read(bool show, Aircraft aircraft, BoardFrame frame, ThreatWatch threats, float now)
        {
            if (!show || aircraft == null)
            {
                if (on) board.Reset();
                on = false;
                return;
            }
            if (!on)
            {
                on = true;
                lastCalled = now;      // results from before the layer was up are not called out
                ledgerVersion = -1;
            }
            inputs = default;
            inputs.Missiles = frame.Threat.Missiles;
            inputs.MissileTtiS = float.NaN;
            if (frame.ContactCount > 0 && frame.Contacts[0].Kind == ThreatKind.Missile)
            {
                ThreatContact c = frame.Contacts[0];
                inputs.Seeker = c.Seeker;
                inputs.MissileTtiS = c.TtiS;
            }
            float radarAlt = aircraft.radarAlt, climb = aircraft.rb != null ? aircraft.rb.velocity.y : 0f;
            inputs.PullUp = AlertBoard.PullUp(HudCues.Terrain(radarAlt, climb), climb, aircraft.gearState == LandingGear.GearState.LockedExtended);
            inputs.GroundS = climb < 0f ? radarAlt / -climb : float.NaN;
            inputs.Fire = (frame.Damage.Flags & DamageFlags.Fire) != 0;
            inputs.Engines = frame.Damage.Engines;
            inputs.EnginesOut = frame.Damage.EnginesOut;
            inputs.Locked = threats.TryLock(now, out _, out inputs.LockName);
            inputs.Bingo = frame.Fuel.Valid && frame.Fuel.Bingo;
            board.Update(inputs.Raised, now);
            if (root != null && board.Banner(now, out AlertKind kind)) WriteDetail(kind);
            WatchShots(now);
        }

        /// <summary>Every frame: place and paint the banner (its blink), the ring (missiles move every frame) and the callout's pop; hide or give back the game's missile list.</summary>
        public void Frame(Aircraft aircraft, ThreatWatch threats, IHudBoard boardBox, float now)
        {
            if (root == null) return;
            bool show = on && aircraft != null;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!show)
            {
                TopCentre.LineUp = lineUp = false;
                NativeList(false);
                return;
            }
            // A 1080-high reference screen scaled to the real one: every length below is the lane's reference pixels.
            float k = canvasRoot.rect.height / 1080f;
            if (!(k > 0.01f)) return;
            if (k != lastK)
            {
                root.localScale = new Vector3(k, k, 1f);
                lastK = k;
            }
            float w = canvasRoot.rect.width / k, h = 1080f;
            VanillaHudStyle.Palette palette = VanillaHudStyle.Colours;
            PaintLine(now, h, boardBox, palette);
            PaintRing(aircraft, threats, now, w, h, palette);
            PaintCallout(now, palette);
            NativeList(true);      // only once the layer has painted: a paint that throws leaves the game's own list up (fail open)
        }
        private const string JamText = "EW JAM";

        /// <summary>How far the slot's piece <paramref name="width"/> wide steps sideways to keep clear of the TAC BOARD's box (0 on the reticle's axis).</summary>
        private static float Shift(IHudBoard boardBox, float width, float height) =>
            boardBox != null && boardBox.TryGetScreenBox(out float l, out float t, out float r, out float bt)
                ? TopCentre.LaneShift(Screen.width, Screen.height, width, TopCentre.LineTop - TopCentre.Lift, height, l, t, r, bt) : 0f;

        /// <summary>
        /// The slot, every frame. An alert up: ONE row with a class bar, no wash, centred on the reticle's axis unless its own width would reach the TAC BOARD (then it steps aside, never up or down): the kind word at the left, the detail after
        /// it, the call at the right, the fuse under the row. The box never shrinks while the alert stays up, so the call word does not jump when a number loses a digit. No alert: EW JAM at the slot's left end
        /// (it yields to the ace card).
        /// </summary>
        private void PaintLine(float now, float h, IHudBoard boardBox, VanillaHudStyle.Palette palette)
        {
            bool up = board.Banner(now, out AlertKind kind);
            if (line.gameObject.activeSelf != up) line.gameObject.SetActive(up);
            TopCentre.LineUp = lineUp = up;
            float top = h * 0.5f - TopCentre.LineTop + TopCentre.Lift, rowY = top - TopCentre.LineHeight * 0.5f + 2f;      // the row sits a little high: the fuse is under it
            bool jamShown = !up && !TopCentre.CardUp && jam;
            if (jamWord.gameObject.activeSelf != jamShown) jamWord.gameObject.SetActive(jamShown);
            if (jamShown)
            {
                float sx = Shift(boardBox, TopCentre.Width, TopCentre.CardHeight);
                jamWord.rectTransform.anchoredPosition = new Vector2(sx - TopCentre.Width * 0.5f, rowY);
                Ink(jamWord, palette.Warning);
            }
            if (!up) { kindKey = -1; return; }

            if ((int)kind != kindKey)
            {
                kindKey = (int)kind;
                kindWord.text = AlertText.Word(kind);
                kindW = kindWord.GetPreferredValues(kindWord.text).x;
                stickyContent = 0f;
                detailKey = 0;
                WriteDetail(kind);
            }
            bool warning = AlertBoard.IsWarning(kind);
            Color tone = warning ? palette.Alert : palette.Warning;
            string wanted = detailText;
            if (middleShown != wanted)
            {
                middleShown = wanted;
                middle.text = wanted;
                middleW = wanted.Length > 0 ? middle.GetPreferredValues(wanted).x : 0f;
            }
            float content = kindW + (middleW > 0f ? TopCentre.WordGap + middleW : 0f) + (callW > 0f ? TopCentre.CallGap + callW : 0f);
            if (content > stickyContent) stickyContent = content;
            float boxW = TopCentre.BoxWidth(stickyContent), x = Shift(boardBox, boxW, TopCentre.LineHeight);
            float left = x - boxW * 0.5f, right = x + boxW * 0.5f, bottom = top - TopCentre.LineHeight, room = Mathf.Max(0f, TopCentre.MiddleRoom(boxW, kindW, callW));
            Place(kindWord.rectTransform, left + TopCentre.Pad, rowY, kindW + 8f, TopCentre.ValuePt * 1.5f);
            // The smaller word shares the big words' baseline (midline alignment alone floats it ~1 px high).
            Place(middle.rectTransform, left + TopCentre.Pad + kindW + TopCentre.WordGap, rowY - (TopCentre.ValuePt - TopCentre.DetailPt) * BaselineDrop, Mathf.Min(middleW + 8f, room), TopCentre.DetailPt * 1.5f);
            Place(call.rectTransform, right - TopCentre.Pad, rowY, callW + 8f, TopCentre.ValuePt * 1.5f);
            bool calm = AvFxDriver.ReducedMotion;      // the kit's REDUCED MOTION: nothing blinks
            Ink(kindWord, tone);
            kindWord.alpha = calm || AlertText.Lit(kind, now - board.Since(kind), inputs.MissileTtiS) ? 1f : 0.15f;
            Ink(middle, VanillaHudStyle.Ink);      // the detail in plain ink: AllClear washed out over cloud
            Ink(call, VanillaHudStyle.InkHi);

            AvQuadBuffer b = lineLines.Buffer;
            b.Clear();
            Rgba ink = tone.ToRgba();
            // No class bar, no wash (user 2026-11-17: "the alert sucks"): two hairlines in the alert's tone run out from the row's ends, the HUD's own section-head idiom.
            float wing = Flank, gap = 6f;
            AvStrokes.Fill(b, left - wing, rowY - 0.5f, wing + TopCentre.Pad - gap, 1f, ink.WithAlpha(HudInk.LineMid));
            AvStrokes.Fill(b, right - TopCentre.Pad + gap, rowY - 0.5f, wing + TopCentre.Pad - gap, 1f, ink.WithAlpha(HudInk.LineMid));
            // A timed alert burns a fuse under the row: full at 10 s to impact (or to the ground), gone at none.
            float share = AlertText.Countdown(kind, inputs);
            if (!float.IsNaN(share))
            {
                float run = boxW - TopCentre.Pad * 2f, fuseY = bottom + 4f;
                AvStrokes.Fill(b, left + TopCentre.Pad, fuseY - 0.5f, run, 1f, ink.WithAlpha(HudInk.LineLow));
                AvStrokes.Fill(b, left + TopCentre.Pad, fuseY - 0.5f, run * share, 1f, ink);
            }
            lineLines.Commit();
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            var p = new Vector2(x, y);
            if (rect.anchoredPosition != p) rect.anchoredPosition = p;
            var s = new Vector2(w, h);
            if (rect.sizeDelta != s) rect.sizeDelta = s;
        }

        /// <summary>A word's colour through the shared glow material (the halo is the word's own colour: one cached material per tone); written only when it changed.</summary>
        private void Ink(TextMeshProUGUI text, Color c)
        {
            Color opaque = new Color(c.r, c.g, c.b, 1f);
            if (text.color == opaque) return;
            text.color = opaque;
            Material glow = VanillaHudStyle.Glow(fontMaterial, opaque);
            if (glow != null && text.fontSharedMaterial != glow) text.fontSharedMaterial = glow;
        }

        private string detailText = "";

        /// <summary>The line's middle and right words: a second warning up at the same time leads the middle ("FIRE · ..."), so no warning is ever off the centre; written only when they changed.</summary>
        private void WriteDetail(AlertKind kind)
        {
            int also = -1;
            for (int k = 0; k < AlertBoard.Count && also < 0; k++)
                if (k != (int)kind && AlertBoard.IsWarning((AlertKind)k) && board.IsUp((AlertKind)k)) also = k;
            int n = AlertText.Detail(buf, kind, inputs);
            string order = kind == AlertKind.Missile ? AlertText.CallWord(AlertText.MissileCall(inputs.Seeker, inputs.MissileTtiS)) : kind == AlertKind.Bingo ? "RTB" : "";
            int key = (int)kind * 7919 + n * 131 + also + order.Length * 17;
            for (int i = 0; i < n; i++) key = key * 31 + buf[i];
            if (key == detailKey) return;
            detailKey = key;
            string text = new string(buf, 0, n);
            detailText = also < 0 ? text : AlertText.Word((AlertKind)also) + (n > 0 ? " · " + text : "");
            if (call.text != order)
            {
                call.text = order;
                callW = order.Length > 0 ? call.GetPreferredValues(order).x : 0f;
            }
        }


        private void PaintRing(Aircraft aircraft, ThreatWatch threats, float now, float w, float h, VanillaHudStyle.Palette palette)
        {
            AvQuadBuffer b = ringLines.Buffer, e = edgeLines.Buffer;
            b.Clear();
            e.Clear();
            int used = 0;
            bool calm = AvFxDriver.ReducedMotion;
            CameraStateManager state = SceneSingleton<CameraStateManager>.i;
            Camera cam = state != null ? state.mainCamera : null;
            if (cam != null)
            {
                float rx = ThreatRing.WidthShare * w, ry = ThreatRing.HeightShare * h;
                Transform eye = cam.transform;
                Rect px = cam.pixelRect;
                float kx = w / Mathf.Max(1f, px.width), ky = h / Mathf.Max(1f, px.height);
                Vector3 ownPos = aircraft.transform.position, ownVelocity = aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero;
                int n = threats.LiveMissiles(missiles, numbers);
                for (int i = 0; i < n && used < RingMax; i++)
                {
                    Vector3 pos = missiles[i].transform.position, rel = pos - ownPos;
                    Vector3 velocity = missiles[i].rb != null ? missiles[i].rb.velocity : Vector3.zero;
                    float range = rel.magnitude, closure = range > 1f ? -Vector3.Dot(velocity - ownVelocity, rel / range) : float.NaN;
                    float tti = ThreatWatch.Tti(range, closure, velocity.magnitude);
                    if (!Mark(eye, cam, pos, px, kx, ky, w, h, rx, ry, out float angle)) continue;
                    float size = ThreatRing.Size(tti);
                    Rgba c = palette.Alert.ToRgba().WithAlpha(calm || ThreatRing.Lit(tti, now) ? 1f : 0.25f);
                    float rad = angle * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad), r = ThreatRing.Reach(angle, rx, ry) + size * 0.5f;
                    // A double chevron pointing out at the missile, the nearer half dimmer: the way to look, at a glance.
                    AvStrokes.Chevron(b, cos * r, sin * r, size, angle, HudStroke.Standard, c);
                    AvStrokes.Chevron(b, cos * (r - size * 0.6f), sin * (r - size * 0.6f), size * 0.75f, angle, HudStroke.Hairline, c.WithAlpha(c.A * 0.55f));
                    float glow = ThreatRing.EdgeAlpha(tti);
                    if (glow > 0f) EdgeGlow(e, angle, w * 0.5f, h * 0.5f, palette.Alert.ToRgba().WithAlpha(glow));
                    int seconds = Mathf.Clamp(Mathf.CeilToInt(tti), 0, 999);
                    // Near twelve o'clock the label reads inward: outward it would sit on the banner's detail line; near six too: outward it would sit on the cockpit's rim.
                    bool inward = sin > 0.6f || sin < -0.6f;
                    RingLabel(used++, cos, sin, inward ? r - size * 1.1f - 4f : r + size * 0.5f + 6f, numbers[i] * 1000 + seconds, numbers[i], seconds, c, inward);
                }
                System.Array.Clear(missiles, 0, missiles.Length);
                if (used < RingMax && threats.TryLock(now, out Unit emitter, out _) && Mark(eye, cam, emitter.transform.position, px, kx, ky, w, h, rx, ry, out float lockAngle))
                {
                    Rgba c = palette.Warning.ToRgba();
                    float rad = lockAngle * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad), r = ThreatRing.Reach(lockAngle, rx, ry) + 8f;
                    AvStrokes.Diamond(b, cos * r, sin * r, 7f, HudStroke.Standard, c);      // a lock is the diamond alone: the banner and the tape say LOCK
                    if (ringLabels[used].gameObject.activeSelf) ringLabels[used].gameObject.SetActive(false);
                    used++;
                }
            }
            edgeLines.Commit();
            RingMarks = used;
            for (int i = used; i < RingMax; i++)
                if (ringLabels[i].gameObject.activeSelf) ringLabels[i].gameObject.SetActive(false);
            ringLines.Commit();
        }

        /// <summary>
        /// A red glow along the screen's edge where a missile closing on us comes from (<see cref="ThreatRing.EdgeAlpha"/>): strongest on the edge at the missile's direction, fading along the
        /// edge and inward over <see cref="EdgeDepth"/>. Two gradient quads per missile; the peripheral cue a pilot feels before reading anything.
        /// </summary>
        private static void EdgeGlow(AvQuadBuffer e, float angleDeg, float hw, float hh, Rgba c)
        {
            Rgba clear = c.WithAlpha(0f);
            if (ThreatRing.ExitsSide(angleDeg, hw, hh, out float ex, out float ey))
            {
                float sx = ex > 0f ? hw : -hw, ix = ex > 0f ? hw - EdgeDepth : -hw + EdgeDepth, reach = hh * 0.45f;
                float y0 = Mathf.Max(-hh, ey - reach), y1 = Mathf.Min(hh, ey + reach);
                e.Add(sx, ey, sx, y1, ix, y1, ix, ey, c, clear, clear, clear);
                e.Add(sx, y0, sx, ey, ix, ey, ix, y0, clear, c, clear, clear);
            }
            else
            {
                float sy = ey > 0f ? hh : -hh, iy = ey > 0f ? hh - EdgeDepth : -hh + EdgeDepth, reach = hw * 0.3f;
                float x0 = Mathf.Max(-hw, ex - reach), x1 = Mathf.Min(hw, ex + reach);
                e.Add(ex, sy, x1, sy, x1, iy, ex, iy, c, clear, clear, clear);
                e.Add(x0, sy, ex, sy, ex, iy, x0, iy, clear, c, clear, clear);
            }
        }

        /// <summary>A threat's place on the ring (<see cref="ThreatRing.Place"/>) from where the camera sees it: its own frame, and its screen offset from the centre in the layer's units while ahead.</summary>
        private static bool Mark(Transform eye, Camera cam, Vector3 world, Rect px, float kx, float ky, float w, float h, float rx, float ry, out float angle)
        {
            Vector3 local = eye.InverseTransformPoint(world);
            float sx = 0f, sy = 0f;
            if (local.z > 0f)
            {
                Vector3 s = cam.WorldToScreenPoint(world);
                sx = (s.x - px.center.x) * kx;
                sy = (s.y - px.center.y) * ky;
            }
            return ThreatRing.Place(local.x, local.y, local.z, sx, sy, rx, ry, w * 0.5f, h * 0.5f, out angle);
        }

        /// <summary>
        /// A ring label <paramref name="r"/> out along the mark's direction, its near edge at the point so it reads away from the mark (<paramref name="inward"/>: toward the centre) and
        /// never covers it: "M2 7S". The words are built only when <paramref name="key"/> changed (nothing allocated per frame).
        /// </summary>
        private void RingLabel(int slot, float cos, float sin, float r, int key, int number, int seconds, Rgba c, bool inward = false)
        {
            TextMeshProUGUI label = ringLabels[slot];
            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            if (ringKeys[slot] != key)
            {
                ringKeys[slot] = key;
                label.text = "M" + number + " " + seconds + "S";
            }
            RectTransform rect = label.rectTransform;
            float px = inward ? -cos : cos, py = inward ? -sin : sin;
            rect.pivot = new Vector2(px > 0.35f ? 0f : px < -0.35f ? 1f : 0.5f, 0.5f - 0.5f * py);
            label.alignment = px > 0.35f ? TextAlignmentOptions.MidlineLeft : px < -0.35f ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.Center;
            rect.anchoredPosition = new Vector2(cos * r, sin * r);
            label.color = new Color(c.R, c.G, c.B, c.A);
        }

        private void PaintCallout(float now, VanillaHudStyle.Palette palette)
        {
            ShotCallouts c = CueFeed.Callouts;
            bool live = c.Live(now);
            if (callout.gameObject.activeSelf != live) callout.gameObject.SetActive(live);
            if (!live) return;
            if (c.Version != calloutVersion)
            {
                calloutVersion = c.Version;
                string text = ShotCallouts.Word(c.Kind) + (c.Streak > 1 ? " x" + c.Streak : "");
                calloutWord.text = text;
                AvIcons.Set(calloutIcon, ShotCallouts.Icon(c.Kind), TopCentre.NamePt);
                bool sub = c.Kind == CalloutKind.Destroyed && !string.IsNullOrEmpty(c.Sub);
                calloutSub.gameObject.SetActive(sub);
                if (sub) calloutSub.text = c.Sub;
                Color ink = palette.AllClear;
                Color tone = c.Kind == CalloutKind.Destroyed || c.Kind == CalloutKind.Hit ? VanillaHudStyle.InkHi : c.Kind == CalloutKind.Decoyed ? palette.Warning
                    : c.Kind == CalloutKind.Missed ? VanillaHudStyle.InkDim : ink;
                calloutWord.color = tone;
                calloutIcon.color = tone;
                calloutSub.color = new Color(ink.r, ink.g, ink.b, 1f);
                float textWidth = calloutWord.GetPreferredValues(text).x, block = TopCentre.NamePt + IconGap + textWidth;
                calloutIcon.rectTransform.anchoredPosition = new Vector2(-block * 0.5f + TopCentre.NamePt * 0.5f, 0f);
                calloutWord.rectTransform.anchoredPosition = new Vector2(block * 0.5f - textWidth * 0.5f, 0f);
                float half = block * 0.5f + TopCentre.Arm * 2f;
                AvQuadBuffer b = calloutLines.Buffer;
                b.Clear();
                Rgba line = tone.ToRgba();
                // No box: the kill call's side rules reach out, it is the one to celebrate.
                if (c.Kind == CalloutKind.Destroyed)
                {
                    AvStrokes.Line(b, -half - 34f, 0f, -half - 6f, 0f, HudStroke.Hairline, line);
                    AvStrokes.Line(b, half + 6f, 0f, half + 34f, 0f, HudStroke.Hairline, line);
                }
                calloutLines.Commit();
            }
            float age = now - c.At, s = AvFxDriver.ReducedMotion ? 1f : ShotCallouts.Scale(age);
            callout.anchoredPosition = new Vector2(0f, -CalloutDown);
            callout.localScale = new Vector3(s, s, 1f);
            calloutGroup.alpha = ShotCallouts.Alpha(age);
        }

        /// <summary>New results in the shot tracker's ledger since the last call (by their resolution time), handed to the callout; a heavier call keeps the stage (<see cref="ShotCallouts.Push"/>).</summary>
        private void WatchShots(float now)
        {
            ShotLedger ledger = ShotFeed.Ledger;
            if (ledger.Version == ledgerVersion) return;
            ledgerVersion = ledger.Version;
            float newest = lastCalled;
            for (int i = 0; i < ShotLedger.Capacity; i++)
            {
                CalloutKind kind = ShotCallouts.Of(ledger.State(i));
                float at = ledger.Time(i);
                if (kind == CalloutKind.None || !(at > lastCalled) || at > now + 1f) continue;
                CueFeed.Callouts.Push(kind, ledger.Name(i), at);
                if (at > newest) newest = at;
            }
            lastCalled = newest;
        }

        /// <summary>The game's own missile list (its <c>ThreatList</c>: one "Missile [ARH] 4.9km" line per missile above the map) hidden while the layer is up, through a group we own; fails open.</summary>
        private void NativeList(bool hide)
        {
            if (nativeList == null)
            {
                if (!hide) return;
                ThreatList list = NativeRefs.Get(ThreatListRef, SceneSingleton<CombatHUD>.i);
                if (list == null || (nativeList = list.gameObject.AddComponent<CanvasGroup>()) == null) return;
                nativeList.blocksRaycasts = false;
            }
            float alpha = hide ? 0f : 1f;
            if (nativeList.alpha != alpha) nativeList.alpha = alpha;
        }

        internal static RectTransform Holder(RectTransform parent, string name)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            return rect;
        }

        /// <summary>An icon of the kit's icon font, centre-anchored like the words (its glyph is set with the word it goes with).</summary>
        private static TMP_Text Icon(RectTransform parent, float size)
        {
            TMP_Text icon = AvIcons.Make(parent, AvIcon.None, size, Color.white);
            RectTransform rect = icon.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size * 1.4f, size * 1.4f);
            return icon;
        }

        private TextMeshProUGUI Text(RectTransform parent, string name, float size, float spacing, bool bold = false)
        {
            TextMeshProUGUI text = HudText.Make(parent, name, font, fontMaterial, TextAlignmentOptions.Center, size);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(640f, size * 1.5f);
            text.characterSpacing = spacing;
            text.richText = false;
            if (bold) text.fontStyle |= FontStyles.Bold;
            return text;
        }

        /// <summary>A word of the warning slot: its type step's size and tracking, bold from VALUE up, anchored at its left (<paramref name="pivotX"/> 0) or right (1) edge; the caller places it.</summary>
        private TextMeshProUGUI Word(RectTransform parent, string name, float size, HudStep step, float pivotX)
        {
            TextMeshProUGUI text = Text(parent, name, size, HudType.Tracking(step), HudType.Bold(step, warning: name == "Jam"));
            text.alignment = pivotX > 0.5f ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            text.rectTransform.pivot = new Vector2(pivotX, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(size * 12f, size * 1.5f);
            return text;
        }

        public void Hide()
        {
            on = false;
            TopCentre.LineUp = lineUp = false;
            board.Reset();
            NativeList(false);
            if (root != null) root.gameObject.SetActive(false);
        }

        public void Destroy()
        {
            if (nativeList != null) Object.Destroy(nativeList);
            nativeList = null;
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            canvasRoot = null;
            on = false;
            board.Reset();
            CueFeed.Callouts.Clear();
        }
    }
}
