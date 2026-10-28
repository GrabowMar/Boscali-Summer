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
    /// <item>WARNING banner in the top-centre lane's slot C (<see cref="TopCentre"/>): the most urgent alert (<see cref="AlertBoard"/>), its word between the lane's hazard
    /// chevrons in corner brackets, and one detail line. Warnings in the theme's alert colour, cautions in its caution colour, always with the word.</item>
    /// <item>THREAT RING round the screen centre (<see cref="ThreatRing"/>): a double chevron toward every live inbound missile labelled "M1 6S", an amber diamond toward a radar locking us.</item>
    /// <item>SHOT CALLOUT under the reticle (<see cref="ShotCallouts"/>): HIT / DESTROYED / MISSED / DECOYED; only DESTROYED names its target.</item>
    /// </list>
    /// Our own objects under the native HUDCanvas (vanilla owns visibility), in the native HUD face, laid out on a 1080-high reference screen and scaled to the real one, with the
    /// lane's one set of strokes, brackets, sizes and motion. While it is up it hides the game's own missile list ("Missile [ARH] 4.9km" above the map), which the banner and the
    /// ring now say better, through a group it owns (alpha back to 1 whenever the layer is down). Data at the board's 10 Hz (<see cref="Read"/>); blink, ring and pop every frame.
    /// </summary>
    internal sealed class CombatAlerts
    {
        private const int RingMax = 6;
        /// <summary>The callout's centre under the screen centre, reference pixels.</summary>
        private const float CalloutDown = 120f;
        /// <summary>Between an icon and its word; how far the screen-edge glow reaches in.</summary>
        private const float IconGap = 8f, EdgeDepth = 90f;
        private static readonly AccessTools.FieldRef<CombatHUD, ThreatList> ThreatListRef = NativeRefs.Field<CombatHUD, ThreatList>("threatList");

        private RectTransform canvasRoot, root, banner, callout;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private TextMeshProUGUI word, detail, calloutWord, calloutSub;
        private TMP_Text bannerIcon, calloutIcon;
        private readonly TextMeshProUGUI[] ringLabels = new TextMeshProUGUI[RingMax];
        private readonly int[] ringKeys = new int[RingMax];
        private Image shade;
        private CanvasGroup calloutGroup, nativeList;
        private AvVector edgeLines, bannerLines, ringLines, calloutLines;
        private float lastK;

        private readonly AlertBoard board = new AlertBoard();
        private AlertInputs inputs;
        private AvUnits units = AvUnits.Metric;
        private readonly char[] buf = new char[96];
        private bool on;
        private float wordHalf, wordWidth;
        private int wordKey = -1, detailKey, calloutVersion = -1, ledgerVersion = -1;
        private float lastCalled = float.PositiveInfinity;

        private readonly Missile[] missiles = new Missile[RingMax];
        private readonly int[] numbers = new int[RingMax];

        /// <summary>What the layer shows now (the sim probe in <c>HudAutomation</c> reads it).</summary>
        internal bool Shown => root != null && root.gameObject.activeSelf;
        internal string BannerWord => banner != null && banner.gameObject.activeSelf ? word.text : "";
        internal string DetailText => banner != null && banner.gameObject.activeSelf ? detail.text : "";
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
                ringLabels[i] = Text(root, "RingLabel", TopCentre.LabelPt, TopCentre.LineSpacing, bold: true);
                ringLabels[i].rectTransform.sizeDelta = new Vector2(96f, TopCentre.LabelPt * 1.5f);
                ringLabels[i].gameObject.SetActive(false);
                ringKeys[i] = int.MinValue;
            }

            banner = Holder(root, "Banner");
            shade = HudSprites.Child(banner, "Shade", HudSprites.Feather, sliced: true);
            shade.raycastTarget = false;
            shade.rectTransform.anchorMin = shade.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            bannerLines = AvVector.Create(banner, "Lines", 48);
            word = Text(banner, "Word", TopCentre.WordPt, TopCentre.WordSpacing, bold: true);
            bannerIcon = Icon(banner, TopCentre.WordPt);
            detail = Text(banner, "Detail", TopCentre.DetailPt, TopCentre.LineSpacing);
            detail.overflowMode = TextOverflowModes.Ellipsis;

            callout = Holder(root, "Callout");
            calloutGroup = callout.gameObject.AddComponent<CanvasGroup>();
            calloutGroup.blocksRaycasts = false;
            calloutGroup.interactable = false;
            calloutLines = AvVector.Create(callout, "Lines", 16);
            calloutWord = Text(callout, "Word", TopCentre.NamePt, TopCentre.WordSpacing, bold: true);
            calloutIcon = Icon(callout, TopCentre.NamePt);
            calloutSub = Text(callout, "Sub", TopCentre.LabelPt, TopCentre.LineSpacing);
            calloutSub.rectTransform.anchoredPosition = new Vector2(0f, -TopCentre.NamePt * 0.7f - TopCentre.LabelPt);      // under the brackets, not in them

            banner.gameObject.SetActive(false);
            callout.gameObject.SetActive(false);
            root.gameObject.SetActive(false);
            lastK = 0f;
            wordKey = calloutVersion = -1;
            detailKey = 0;      // a rebuilt layer writes its detail line again
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
            units = frame.Units;
            inputs = default;
            inputs.Missiles = frame.Threat.Missiles;
            inputs.MissileRangeM = inputs.MissileTtiS = float.NaN;
            if (frame.ContactCount > 0 && frame.Contacts[0].Kind == ThreatKind.Missile)
            {
                ThreatContact c = frame.Contacts[0];
                inputs.Seeker = c.Seeker;
                inputs.MissileRangeM = c.RangeM;
                inputs.MissileTtiS = c.TtiS;
            }
            float radarAlt = aircraft.radarAlt, climb = aircraft.rb != null ? aircraft.rb.velocity.y : 0f;
            inputs.PullUp = AlertBoard.PullUp(HudCues.Terrain(radarAlt, climb), climb, aircraft.gearState == LandingGear.GearState.LockedExtended);
            inputs.GroundS = climb < 0f ? radarAlt / -climb : float.NaN;
            inputs.Fire = (frame.Damage.Flags & DamageFlags.Fire) != 0;
            inputs.Engines = frame.Damage.Engines;
            inputs.EnginesOut = frame.Damage.EnginesOut;
            inputs.Locked = threats.TryLock(now, out Unit emitter, out inputs.LockName);
            inputs.LockRangeM = emitter != null ? Vector3.Distance(emitter.transform.position, aircraft.transform.position) : float.NaN;
            inputs.Bingo = frame.Fuel.Valid && frame.Fuel.Bingo;
            inputs.Field = frame.Fuel.Field;
            inputs.FieldM = frame.Fuel.FieldM;
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
            PaintBanner(now, h, boardBox, palette);
            PaintRing(aircraft, threats, now, w, h, palette);
            PaintCallout(now, palette);
            NativeList(true);      // only once the layer has painted: a paint that throws leaves the game's own list up (fail open)
        }

        private void PaintBanner(float now, float h, IHudBoard boardBox, VanillaHudStyle.Palette palette)
        {
            bool up = board.Banner(now, out AlertKind kind);
            if (banner.gameObject.activeSelf != up) banner.gameObject.SetActive(up);
            if (!up) return;
            if ((int)kind != wordKey)
            {
                wordKey = (int)kind;
                word.text = AlertText.Word(kind);
                AvIcons.Set(bannerIcon, AlertText.Icon(kind), TopCentre.WordPt);
                wordWidth = word.GetPreferredValues(word.text).x;
                wordHalf = (wordWidth + TopCentre.WordPt + IconGap) * 0.5f;      // the word block: icon, gap, word
                WriteDetail(kind);
            }
            bool warning = AlertBoard.IsWarning(kind);
            Color tone = warning ? palette.Alert : palette.Warning;
            float top = h * 0.5f - TopCentre.BannerTop, bottom = top - TopCentre.BannerHeight;
            float wordY = top - TopCentre.WordPt * 0.75f, detailY = bottom + TopCentre.DetailPt;
            // The banner's width follows its word only (never the detail digits, which change ten times a second): at least the lane's, so it never slides sideways.
            float half = Mathf.Max(TopCentre.HazardReach(wordHalf) + TopCentre.Arm * 2f, TopCentre.Width * 0.5f);
            // On the reticle's axis unless this banner's own width would reach the TAC BOARD.
            float x = boardBox != null && boardBox.TryGetScreenBox(out float l, out float t, out float r, out float bt)
                ? TopCentre.LaneShift(Screen.width, Screen.height, half * 2f, TopCentre.BannerTop, TopCentre.BannerHeight, l, t, r, bt) : 0f;
            word.rectTransform.anchoredPosition = new Vector2(x + wordHalf - wordWidth * 0.5f, wordY);
            bannerIcon.rectTransform.anchoredPosition = new Vector2(x - wordHalf + TopCentre.WordPt * 0.5f, wordY);
            detail.rectTransform.anchoredPosition = new Vector2(x, detailY);
            float detailWidth = (half - TopCentre.Arm * 2f) * 2f;      // the detail stays between the brackets (an ellipsis past them)
            if (detail.rectTransform.sizeDelta.x != detailWidth) detail.rectTransform.sizeDelta = new Vector2(detailWidth, TopCentre.DetailPt * 1.5f);
            shade.rectTransform.anchoredPosition = new Vector2(x, (top + bottom) * 0.5f);
            shade.rectTransform.sizeDelta = new Vector2((half + TopCentre.ShadeMargin) * 2f, TopCentre.BannerHeight + TopCentre.ShadeMargin * 2f);
            shade.color = HudSprites.BackdropColor;
            bool calm = AvFxDriver.ReducedMotion;      // the kit's REDUCED MOTION: nothing blinks, marches, pops or types on
            word.color = tone.WithAlpha(calm || AlertText.Lit(kind, now - board.Since(kind), inputs.MissileTtiS) ? 1f : 0.15f);
            bannerIcon.color = word.color;
            detail.color = tone.WithAlpha(0.9f);

            // The brackets close in from wide when the alert comes up (the lane's enter time); a warning's chevrons run in from the outside, a caution's stand still.
            float open = calm ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((now - board.Since(kind)) / TopCentre.EnterS)), wide = half + (1f - open) * 40f;
            AvQuadBuffer b = bannerLines.Buffer;
            b.Clear();
            Rgba ink = tone.ToRgba();
            AvStrokes.Bracket(b, x - wide, bottom, wide * 2f, top - bottom, TopCentre.Arm, TopCentre.Stroke, ink);
            TopCentre.Hazard(b, x, wordY, wordHalf, ink, warning && !calm ? (int)(now * 6f) % 3 : -1);
            // A timed alert burns a fuse along the banner's foot, between the brackets' arms: full at 10 s to impact (or to the ground), gone at none.
            float share = AlertText.Countdown(kind, inputs);
            if (!float.IsNaN(share))
            {
                float run = (wide - TopCentre.Arm - 6f) * 2f;
                AvStrokes.Fill(b, x - run * 0.5f, bottom - 1.5f, run, 3f, ink.WithAlpha(0.2f));
                AvStrokes.Fill(b, x - run * share * 0.5f, bottom - 1.5f, run * share, 3f, ink);
            }
            bannerLines.Commit();
        }

        /// <summary>The detail line: a second warning up at the same time leads it ("+FIRE · ..."), so no warning is ever off the centre; written only when it changed.</summary>
        private void WriteDetail(AlertKind kind)
        {
            int also = -1;
            for (int k = 0; k < AlertBoard.Count && also < 0; k++)
                if (k != (int)kind && AlertBoard.IsWarning((AlertKind)k) && board.IsUp((AlertKind)k)) also = k;
            int n = AlertText.Detail(buf, kind, inputs, units), key = (int)kind * 7919 + n * 131 + also;
            for (int i = 0; i < n; i++) key = key * 31 + buf[i];
            if (key == detailKey) return;
            detailKey = key;
            string line = new string(buf, 0, n);
            detail.text = also < 0 ? line : "+" + AlertText.Word((AlertKind)also) + (n > 0 ? " · " + line : "");
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
                    AvStrokes.Chevron(b, cos * r, sin * r, size, angle, TopCentre.ChevronStroke, c);
                    AvStrokes.Chevron(b, cos * (r - size * 0.6f), sin * (r - size * 0.6f), size * 0.75f, angle, TopCentre.ChevronStroke, c.WithAlpha(c.A * 0.55f));
                    float glow = ThreatRing.EdgeAlpha(tti);
                    if (glow > 0f) EdgeGlow(e, angle, w * 0.5f, h * 0.5f, palette.Alert.ToRgba().WithAlpha(glow));
                    int seconds = Mathf.Clamp(Mathf.RoundToInt(tti), 0, 999);
                    // Near twelve o'clock the label reads inward: outward it would sit on the banner's detail line.
                    bool inward = sin > 0.6f;
                    RingLabel(used++, cos, sin, inward ? r - size * 1.1f - 4f : r + size * 0.5f + 6f, numbers[i] * 1000 + seconds, numbers[i], seconds, c, inward);
                }
                System.Array.Clear(missiles, 0, missiles.Length);
                if (used < RingMax && threats.TryLock(now, out Unit emitter, out _) && Mark(eye, cam, emitter.transform.position, px, kx, ky, w, h, rx, ry, out float lockAngle))
                {
                    Rgba c = palette.Warning.ToRgba();
                    float rad = lockAngle * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad), r = ThreatRing.Reach(lockAngle, rx, ry) + 8f;
                    AvStrokes.Diamond(b, cos * r, sin * r, 8f, TopCentre.ChevronStroke, c);
                    AvStrokes.Diamond(b, cos * r, sin * r, 3f, 3f, c);
                    bool inward = sin > 0.6f;
                    RingLabel(used++, cos, sin, inward ? r - 22f : r + 14f, -1, -1, 0, c, inward);
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
        /// never covers it: "M2 7S", or LOCK for <paramref name="number"/> -1. The words are built only when <paramref name="key"/> changed (nothing allocated per frame).
        /// </summary>
        private void RingLabel(int slot, float cos, float sin, float r, int key, int number, int seconds, Rgba c, bool inward = false)
        {
            TextMeshProUGUI label = ringLabels[slot];
            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            if (ringKeys[slot] != key)
            {
                ringKeys[slot] = key;
                label.text = number < 0 ? "LOCK" : "M" + number + " " + seconds + "S";
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
                Color tone = c.Kind == CalloutKind.Destroyed ? Color.Lerp(ink, Color.white, 0.55f) : c.Kind == CalloutKind.Decoyed ? palette.Warning
                    : c.Kind == CalloutKind.Missed ? ink.WithAlpha(0.65f) : ink;
                calloutWord.color = tone;
                calloutIcon.color = tone;
                calloutSub.color = tone.WithAlpha(tone.a * 0.85f);
                float textWidth = calloutWord.GetPreferredValues(text).x, block = TopCentre.NamePt + IconGap + textWidth;
                calloutIcon.rectTransform.anchoredPosition = new Vector2(-block * 0.5f + TopCentre.NamePt * 0.5f, 0f);
                calloutWord.rectTransform.anchoredPosition = new Vector2(block * 0.5f - textWidth * 0.5f, 0f);
                float half = block * 0.5f + TopCentre.Arm * 2f, height = TopCentre.NamePt * 1.4f;
                AvQuadBuffer b = calloutLines.Buffer;
                b.Clear();
                Rgba line = tone.ToRgba();
                AvStrokes.Bracket(b, -half, -height * 0.5f, half * 2f, height, TopCentre.Arm, TopCentre.Stroke, line);
                // The kill call's side rules reach out further: it is the one to celebrate.
                float reach = c.Kind == CalloutKind.Destroyed ? 48f : 24f;
                AvStrokes.Line(b, -half - TopCentre.Arm - reach, 0f, -half - TopCentre.Arm, 0f, TopCentre.Stroke, line);
                AvStrokes.Line(b, half + TopCentre.Arm, 0f, half + TopCentre.Arm + reach, 0f, TopCentre.Stroke, line);
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

        private static RectTransform Holder(RectTransform parent, string name)
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

        public void Hide()
        {
            on = false;
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
