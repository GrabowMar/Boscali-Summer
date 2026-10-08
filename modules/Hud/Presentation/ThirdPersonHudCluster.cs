using NOAvionics;
using System.Reflection;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The screen-fixed flight cluster and the relocated target-camera card
    /// (docs/superpowers/specs/2026-09-28-wingview-third-person-design.md, "Third-person HUD").
    /// Our own objects, parented directly under the native HUDCanvas root (found the same way
    /// <see cref="StatusPanel"/> finds its own dock), active only while the caller says third
    /// person is showing. All geometry comes from <see cref="ThirdPersonHudLayout"/>, all cue
    /// rules from <see cref="HudCues"/>; this class only builds/positions widgets and writes
    /// text on change, through a reused buffer -- a string is allocated only the tick a
    /// displayed value actually changes.
    /// Look (2026-10-07, base-game aligned; not configurable): bare HUD-ink numbers in the native
    /// weapon label font framed by thin corner brackets, thin tick scales for throttle and fuel,
    /// and a black slab with a sharp outline for the target card -- the same symbology language as
    /// the native flight HUD, no plates. Cues light brackets and text like annunciators: SPD on
    /// over-G, ALT on low ground (radar altitude replaces its caption under 300 m; PULL UP in alert
    /// colour), the fuel scale on low fuel. HDG adds the compass point; the card shows range and closure.
    /// </summary>
    internal sealed class ThirdPersonHudCluster
    {
        private static readonly FieldInfo WeaponStatusField = AccessTools.Field(typeof(CombatHUD), "weaponStatus");
        private static readonly FieldInfo NameTextField = AccessTools.Field(typeof(WeaponStatus), "nameText");

        /// <summary>|G| at or above this lights the SPD box (over-G caution).</summary>
        private const float OverG = 7f;
        /// <summary>Fuel fraction below this lights the fuel bar.</summary>
        private const float LowFuel = 0.2f;
        /// <summary>Text on a lit (ink-filled) tag.</summary>
        private static readonly Color LitText = new Color(0.02f, 0.05f, 0.05f, 1f);

        /// <summary>One tick of own-aircraft flight data.</summary>
        internal struct Flight
        {
            public float SpeedMps, AltitudeM, RadarAltM, ClimbMps, HeadingDeg, Mach, G, Fuel, Throttle, BankDeg, SlipDeg;
        }

        private readonly char[] buf = new char[48];

        private RectTransform canvasRoot;
        private RectTransform root;
        private TMP_FontAsset font;
        private Material fontMaterial;

        private Box spd, alt;
        private Box hdg;
        private TargetCardWidgets card;

        private bool builtActive;
        private AvVector symbols;
        private int lastSymbols;
        private const float Bracket = 9f, Stroke = 1.5f;

        private struct Box
        {
            public RectTransform Root;
            public Image Backing;
            public Image Frame;
            public TextMeshProUGUI Caption;
            public TextMeshProUGUI Value;
            public TextMeshProUGUI SubLine;
            public RectTransform Track;
            public Image TrackImage;
            public Image Bar;
            public TextMeshProUGUI BarLabel;
            public float LastValue;
            public float LastSub;
            public float LastCaption;
            public float LastBar;
            public bool Initialized;
        }

        private struct TargetCardWidgets
        {
            public RectTransform Root;
            public Image Backing;
            public Image Frame;
            public Image Tag;
            public TextMeshProUGUI TagText;
            public TextMeshProUGUI Header;
            public TextMeshProUGUI Range;
            public TextMeshProUGUI Closure;
            public RawImage Feed;
            public Image[] Reticle;
            public bool Active;
            public string LastHeader;
            public float LastRange;
            public float LastClosure;
        }

        public bool Available => root != null;

        public bool Ensure()
        {
            if (root != null && canvasRoot != null) return true;
            root = null;
            canvasRoot = null;

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || hud.iconLayer == null || !hud.iconLayer.gameObject.activeInHierarchy) return false;
            Canvas hudCanvas = hud.iconLayer.GetComponentInParent<Canvas>();
            if (hudCanvas == null) return false;

            Build(hud, hudCanvas);
            return root != null;
        }

        private void Build(CombatHUD hud, Canvas hudCanvas)
        {
            canvasRoot = hudCanvas.transform as RectTransform;
            if (canvasRoot == null) canvasRoot = hudCanvas.GetComponent<RectTransform>();

            WeaponStatus weaponStatus = WeaponStatusField?.GetValue(hud) as WeaponStatus;
            TextMeshProUGUI nameLabel = weaponStatus != null ? NameTextField?.GetValue(weaponStatus) as TextMeshProUGUI : null;
            font = nameLabel != null ? nameLabel.font : null;
            fontMaterial = nameLabel != null ? nameLabel.fontSharedMaterial : null;

            var rootObject = new GameObject("BoscaliWingviewCluster", typeof(RectTransform));
            root = (RectTransform)rootObject.transform;
            root.SetParent(canvasRoot, false);
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = Vector2.zero;

            spd = BuildBox("SPD", "SPD");
            alt = BuildBox("ALT", "ALT");
            hdg = BuildBox("HDG", null, withBar: false);
            card = BuildTargetCard();
            // Root is zero-size at screen centre, so this surface's origin is screen centre: RectF space.
            symbols = AvVector.Create(root, "Symbols", 900);
            lastSymbols = 0;

            rootObject.SetActive(false);
            builtActive = false;
        }

        private TextMeshProUGUI Label(RectTransform parent, string name, TextAlignmentOptions alignment, float size,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            TextMeshProUGUI label = HudText.Make(parent, name, font, fontMaterial, alignment, size);
            label.rectTransform.anchorMin = anchorMin;
            label.rectTransform.anchorMax = anchorMax;
            label.rectTransform.offsetMin = offsetMin;
            label.rectTransform.offsetMax = offsetMax;
            return label;
        }

        private Box BuildBox(string name, string caption, bool withBar = true)
        {
            var boxObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)boxObject.transform;
            rect.SetParent(root, false);
            rect.pivot = new Vector2(0.5f, 0.5f);
            Image backing = boxObject.GetComponent<Image>();
            backing.raycastTarget = false;
            backing.sprite = AvSprites.White;
            backing.color = Color.clear;
            Image frame = null;

            bool hasSub = caption != null;
            TextMeshProUGUI captionText = null;
            if (hasSub)
            {
                captionText = Label(rect, "Caption", TextAlignmentOptions.TopLeft, 11f,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(7f, -17f), new Vector2(-7f, -4f));
                captionText.characterSpacing = 8f;
                captionText.text = caption;
            }

            // Caption owns the top 14 px; the value sits between it and the sub-line (mono faces run wide).
            TextMeshProUGUI value = Label(rect, "Value", TextAlignmentOptions.Center, hasSub ? 21f : 20f,
                new Vector2(0f, hasSub ? 0.3f : 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, hasSub ? -15f : 0f));

            TextMeshProUGUI sub = Label(rect, "SubLine", TextAlignmentOptions.Center, 13f,
                Vector2.zero, new Vector2(1f, 0.34f), new Vector2(0f, 3f), Vector2.zero);
            sub.gameObject.SetActive(hasSub);

            RectTransform track = null;
            Image trackImage = null;
            Image bar = null;
            TextMeshProUGUI barLabel = null;
            if (withBar)
            {
                // A visible track so a low reading still reads as "a little of a bar", not a stray dot.
                var trackObject = new GameObject(name + "Track", typeof(RectTransform), typeof(Image));
                track = (RectTransform)trackObject.transform;
                track.SetParent(root, false);
                track.pivot = new Vector2(0.5f, 0.5f);
                trackImage = trackObject.GetComponent<Image>();
                trackImage.raycastTarget = false;
                trackImage.sprite = AvSprites.White;
                trackImage.color = Color.clear;

                // The fill is a solid 3 px line up the middle of the tick scale drawn in PaintSymbols.
                bar = HudSprites.Child(track, "Bar", AvSprites.White, sliced: false);
                ((RectTransform)bar.transform).offsetMin = new Vector2(1.5f, 0f);
                ((RectTransform)bar.transform).offsetMax = new Vector2(-1.5f, 0f);
                // Filled needs a sprite: without one Unity ignores fillAmount and draws the full rect.
                bar.type = Image.Type.Filled;
                bar.fillMethod = Image.FillMethod.Vertical;
                bar.fillOrigin = 0;
                bar.fillAmount = 0f;

                // Live percentage under the track, wider than the bar itself.
                barLabel = Label(track, "BarLabel", TextAlignmentOptions.Top, 10f,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-22f, -15f), new Vector2(22f, -2f));
            }

            return new Box
            {
                Root = rect, Backing = backing, Frame = frame, Caption = captionText, Value = value, SubLine = sub,
                Track = track, TrackImage = trackImage, Bar = bar, BarLabel = barLabel,
                LastValue = float.NaN, LastSub = float.NaN, LastCaption = float.NaN, LastBar = float.NaN
            };
        }

        private TargetCardWidgets BuildTargetCard()
        {
            const float header = ThirdPersonHudLayout.TargetCardHeaderHeight;
            var cardObject = new GameObject("TargetCard", typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)cardObject.transform;
            rect.SetParent(root, false);
            rect.pivot = new Vector2(0.5f, 0.5f);
            Image backing = cardObject.GetComponent<Image>();
            backing.raycastTarget = false;
            backing.sprite = AvSprites.White;

            var feedObject = new GameObject("Feed", typeof(RectTransform), typeof(RawImage));
            RectTransform feedRect = (RectTransform)feedObject.transform;
            feedRect.SetParent(rect, false);
            feedRect.anchorMin = new Vector2(0f, 0f);
            feedRect.anchorMax = new Vector2(1f, 1f);
            feedRect.offsetMin = new Vector2(4f, 4f);
            feedRect.offsetMax = new Vector2(-4f, -header);
            RawImage feed = feedObject.GetComponent<RawImage>();
            feed.raycastTarget = false;

            // Centre reticle: four short ticks around a gap, the RWR's plane-cross idiom.
            var reticle = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var tickObject = new GameObject("Tick" + i, typeof(RectTransform), typeof(Image));
                RectTransform tick = (RectTransform)tickObject.transform;
                tick.SetParent(feedRect, false);
                tick.anchorMin = tick.anchorMax = tick.pivot = new Vector2(0.5f, 0.5f);
                bool horizontal = i < 2;
                float sign = i % 2 == 0 ? -1f : 1f;
                tick.sizeDelta = horizontal ? new Vector2(10f, 1.5f) : new Vector2(1.5f, 10f);
                tick.anchoredPosition = horizontal ? new Vector2(sign * 11f, 0f) : new Vector2(0f, sign * 11f);
                reticle[i] = tickObject.GetComponent<Image>();
                reticle[i].raycastTarget = false;
            }

            // Closure reads in the feed's lower-right corner, over a small plate.
            TextMeshProUGUI closure = Label(feedRect, "Closure", TextAlignmentOptions.BottomRight, 12f,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(6f, 4f), new Vector2(-6f, 20f));

            Image frame = null;

            // Outlined "TGT" tag (outline drawn in PaintSymbols), then the target name, then its range right-aligned.
            var tagObject = new GameObject("Tag", typeof(RectTransform), typeof(Image));
            RectTransform tagRect = (RectTransform)tagObject.transform;
            tagRect.SetParent(rect, false);
            tagRect.anchorMin = tagRect.anchorMax = tagRect.pivot = new Vector2(0f, 1f);
            tagRect.anchoredPosition = new Vector2(5f, -4f);
            tagRect.sizeDelta = new Vector2(34f, header - 7f);
            Image tag = tagObject.GetComponent<Image>();
            tag.raycastTarget = false;
            tag.sprite = AvSprites.White;
            tag.color = Color.clear;
            TextMeshProUGUI tagText = Label(tagRect, "TagText", TextAlignmentOptions.Center, 11f,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            tagText.text = "TGT";

            TextMeshProUGUI title = HudText.Make(rect, "Header", font, fontMaterial, TextAlignmentOptions.MidlineLeft, 13f,
                TextOverflowModes.Ellipsis);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(45f, -1f);
            title.rectTransform.sizeDelta = new Vector2(-45f - 70f, header - 2f);

            TextMeshProUGUI range = HudText.Make(rect, "Range", font, fontMaterial, TextAlignmentOptions.MidlineRight, 13f);
            range.rectTransform.anchorMin = new Vector2(1f, 1f);
            range.rectTransform.anchorMax = new Vector2(1f, 1f);
            range.rectTransform.pivot = new Vector2(1f, 1f);
            range.rectTransform.anchoredPosition = new Vector2(-7f, -1f);
            range.rectTransform.sizeDelta = new Vector2(66f, header - 2f);

            cardObject.SetActive(false);
            return new TargetCardWidgets
            {
                Root = rect, Backing = backing, Frame = frame, Tag = tag, TagText = tagText, Header = title, Range = range,
                Closure = closure, Feed = feed, Reticle = reticle, Active = false, LastRange = float.NaN, LastClosure = float.NaN
            };
        }

        /// <summary>Refresh from this tick's flight data. <paramref name="active"/> is the
        /// caller's third-person-own-aircraft gate; false hides the whole cluster (one root
        /// toggle). Called at 10 Hz by the board. <paramref name="cameraTexture"/> null hides the
        /// target card; <paramref name="rangeM"/>/<paramref name="closureMps"/> NaN show "--".
        /// Colour writes are no-ops when unchanged (Graphic only dirties on a different value).</summary>
        public void Present(bool active, AvUnits units, in Flight f,
            Texture cameraTexture, string cameraCode, float rangeM, float closureMps)
        {
            if (root == null) return;
            if (active != builtActive)
            {
                root.gameObject.SetActive(active);
                builtActive = active;
            }
            if (!active) return;

            // Reference frame is the HUD canvas's own local rect, not raw Screen.width/height:
            // anchoredPosition under this canvas is in the canvas's own (possibly scaled) units.
            float screenW = canvasRoot.rect.width;
            float screenH = canvasRoot.rect.height;
            if (screenW <= 0f || screenH <= 0f) return;

            ColorTheme theme = ThemeManager.Active.ColorTheme;
            Color ink = theme.AllClear;
            Color caution = theme.Warning;
            Color alert = theme.Alert;

            RectF spdRect = ThirdPersonHudLayout.SpeedBox(screenW, screenH);
            RectF altRect = ThirdPersonHudLayout.AltitudeBox(screenW, screenH);
            PositionRect(spd.Root, spdRect);
            PositionRect(alt.Root, altRect);
            PositionRect(hdg.Root, ThirdPersonHudLayout.HeadingBox(screenW, screenH));
            PositionRect(spd.Track, ThirdPersonHudLayout.SideBar(spdRect, outwardIsLeft: true));
            PositionRect(alt.Track, ThirdPersonHudLayout.SideBar(altRect, outwardIsLeft: false));

            bool overG = float.IsFinite(f.G) && Mathf.Abs(f.G) >= OverG;
            TerrainCue terrain = HudCues.Terrain(f.RadarAltM, f.ClimbMps);
            bool lowFuel = f.Fuel < LowFuel;
            Color? spdLit = overG ? caution : (Color?)null;
            Color? altLit = terrain == TerrainCue.PullUp ? alert : terrain == TerrainCue.Low ? caution : (Color?)null;
            Style(spd, ink, spdLit);
            Style(alt, ink, altLit);
            Style(hdg, ink, null);
            Fill(ref spd, Mathf.Clamp01(f.Throttle), ink, "THR ");
            Fill(ref alt, Mathf.Clamp01(f.Fuel), lowFuel ? caution : ink, lowFuel ? "BINGO " : "FUEL ");
            bool pullUpFlash = terrain == TerrainCue.PullUp && (int)(Time.unscaledTime * 4f) % 2 == 0;
            PaintSymbols(screenW, screenH, ink, spdLit, altLit, lowFuel ? caution : ink, cameraTexture != null, pullUpFlash,
                f.BankDeg, f.SlipDeg);

            WriteSpeed(f.SpeedMps, units);
            WriteAltitude(f.AltitudeM, units);
            WriteAltCaption(f.RadarAltM, terrain, units);
            WriteHeading(f.HeadingDeg);
            WriteMachG(f.Mach, f.G);
            WriteClimb(f.ClimbMps, units);

            PresentTargetCard(cameraTexture, cameraCode, rangeM, closureMps, screenW, screenH, ink, units);
        }

        /// <summary>The RWR panel look: near-black plate tinted toward the theme ink.</summary>
        private static Color PlateTint(Color ink) => new Color(ink.r * 0.04f, ink.g * 0.07f, ink.b * 0.08f, 0.62f);

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>Normal: bare ink. Lit: value, caption and sub-line in <paramref name="lit"/>, over a
        /// faint wash of it so the cue survives a bright sky (brackets light in PaintSymbols).</summary>
        private static void Style(Box box, Color ink, Color? lit)
        {
            box.Backing.color = lit.HasValue ? WithAlpha(lit.Value, 0.14f) : Color.clear;
            box.Value.color = lit.HasValue ? lit.Value : ink;
            if (box.Caption != null) box.Caption.color = lit.HasValue ? lit.Value : WithAlpha(ink, 0.6f);
            box.SubLine.color = lit.HasValue ? lit.Value : WithAlpha(ink, 0.85f);
        }

        private void Fill(ref Box box, float amount, Color color, string prefix)
        {
            if (box.Bar.fillAmount != amount) box.Bar.fillAmount = amount;
            box.Bar.color = WithAlpha(color, 0.95f);
            box.BarLabel.color = WithAlpha(color, 0.85f);
            // Percent plus which prefix is showing (BINGO vs FUEL), as one change key.
            float key = Mathf.Round(amount * 100f) + (prefix.Length * 1000f);
            if (box.LastBar == key) return;
            int len = AvNumFormat.Append(buf, 0, prefix);
            len = AvNumFormat.Write(buf, len, Mathf.Round(amount * 100f), 0);
            box.BarLabel.text = new string(buf, 0, len);
            box.LastBar = key;
        }

        /// <summary>Brackets round each box, tick scales for the bars, the card's outline and its tag box.
        /// Rebuilt only when the layout or a lit state changes.</summary>
        private void PaintSymbols(float screenW, float screenH, Color ink, Color? spdLit, Color? altLit, Color fuel,
            bool cardShown, bool flash, float bankDeg, float slipDeg)
        {
            int key = (int)screenW * 7919 + (int)screenH * 31 + Code(spdLit) * 5 + Code(altLit) * 3 + Code(fuel) +
                (cardShown ? 11 : 0) + (flash ? 13 : 0) + Mathf.RoundToInt(bankDeg) * 104729 + Mathf.RoundToInt(slipDeg * 2f) * 7;
            if (key == lastSymbols) return;
            lastSymbols = key;

            AvQuadBuffer b = symbols.Buffer;
            b.Clear();
            RectF spdRect = ThirdPersonHudLayout.SpeedBox(screenW, screenH);
            RectF altRect = ThirdPersonHudLayout.AltitudeBox(screenW, screenH);
            Brackets(b, spdRect, (spdLit ?? ink).ToRgba());
            Brackets(b, altRect, (flash ? Color.clear : altLit ?? ink).ToRgba());
            Brackets(b, ThirdPersonHudLayout.HeadingBox(screenW, screenH), ink.ToRgba().WithAlpha(0.8f));
            Scale(b, ThirdPersonHudLayout.SideBar(spdRect, outwardIsLeft: true), ink.ToRgba());
            Scale(b, ThirdPersonHudLayout.SideBar(altRect, outwardIsLeft: false), fuel.ToRgba());
            BankAndSlip(b, ThirdPersonHudLayout.HeadingBox(screenW, screenH), bankDeg, slipDeg, ink.ToRgba());
            if (cardShown)
            {
                RectF card = ThirdPersonHudLayout.TargetCard(screenW, screenH, true);
                Rgba line = ink.ToRgba().WithAlpha(0.9f);
                Outline(b, card.Left, card.Bottom, card.Right, card.Top, Stroke, line);
                float tagTop = card.Top - 4f, tagLeft = card.Left + 5f;
                Outline(b, tagLeft, tagTop - (ThirdPersonHudLayout.TargetCardHeaderHeight - 7f), tagLeft + 34f, tagTop, 1f, line);
                AvStrokes.Line(b, card.Left, card.Top - ThirdPersonHudLayout.TargetCardHeaderHeight + 2f,
                    card.Right, card.Top - ThirdPersonHudLayout.TargetCardHeaderHeight + 2f, 1f, line.WithAlpha(0.5f));
            }
            symbols.Commit();
        }

        /// <summary>
        /// Bank &amp; slip under the HDG box (after NO_Tactitools): a ±45° tick arc with a pointer swung to the bank side
        /// (clamped at 50°), and a slip ball between cage marks that rolls away from the slip like a real inclinometer.
        /// </summary>
        private static void BankAndSlip(AvQuadBuffer b, RectF hdg, float bankDeg, float slipDeg, Rgba ink)
        {
            const float R = 46f, MaxBank = 45f, BallTravel = 16f, SlipFull = 10f;
            float cx = hdg.CenterX, top = hdg.Bottom - 12f, cy = top - R;
            Rgba dim = ink.WithAlpha(0.55f);
            AvStrokes.Arc(b, cx, cy, R, 90f - MaxBank, 90f + MaxBank, 18, 1f, dim);
            foreach (float tick in new[] { -45f, -30f, -20f, -10f, 0f, 10f, 20f, 30f, 45f })
            {
                float a = (90f - tick) * Mathf.Deg2Rad, len = tick == 0f || Mathf.Abs(tick) == 45f ? 7f : 4f;
                AvStrokes.Line(b, cx + Mathf.Cos(a) * R, cy + Mathf.Sin(a) * R, cx + Mathf.Cos(a) * (R + len), cy + Mathf.Sin(a) * (R + len), 1f, dim);
            }
            float p = (90f - Mathf.Clamp(bankDeg, -50f, 50f)) * Mathf.Deg2Rad;
            float px = cx + Mathf.Cos(p) * (R - 2f), py = cy + Mathf.Sin(p) * (R - 2f);
            AvStrokes.Chevron(b, px, py, 7f, p * Mathf.Rad2Deg, 1.75f, ink);

            float tubeY = top - 18f, ballX = cx - Mathf.Clamp(slipDeg / SlipFull, -1f, 1f) * BallTravel;
            AvStrokes.Line(b, cx - BallTravel - 4f, tubeY, cx + BallTravel + 4f, tubeY, 1f, dim);
            AvStrokes.Line(b, cx - 5f, tubeY - 5f, cx - 5f, tubeY + 5f, 1f, dim);
            AvStrokes.Line(b, cx + 5f, tubeY - 5f, cx + 5f, tubeY + 5f, 1f, dim);
            AvStrokes.Ring(b, ballX, tubeY, 1.6f, 10, 3.2f, ink);
        }

        private static int Code(Color? c) => c.HasValue ? Code(c.Value) : 0;
        private static int Code(Color c) => (int)(c.r * 50f) * 3 + (int)(c.g * 50f);

        /// <summary>The native HUD's corner-bracket frame: open sides, short arms top and bottom.</summary>
        private static void Brackets(AvQuadBuffer b, RectF r, Rgba c)
        {
            float l = r.Left, rt = r.Right, t = r.Top, bt = r.Bottom;
            AvStrokes.Line(b, l, bt, l, t, Stroke, c);
            AvStrokes.Line(b, l, t, l + Bracket, t, Stroke, c);
            AvStrokes.Line(b, l, bt, l + Bracket, bt, Stroke, c);
            AvStrokes.Line(b, rt, bt, rt, t, Stroke, c);
            AvStrokes.Line(b, rt, t, rt - Bracket, t, Stroke, c);
            AvStrokes.Line(b, rt, bt, rt - Bracket, bt, Stroke, c);
        }

        /// <summary>A thin vertical scale: spine plus five ticks; the live fill is the bar image over it.</summary>
        private static void Scale(AvQuadBuffer b, RectF r, Rgba c)
        {
            Rgba dim = c.WithAlpha(0.45f);
            AvStrokes.Line(b, r.CenterX, r.Bottom, r.CenterX, r.Top, 1f, dim);
            for (int i = 0; i <= 4; i++)
            {
                float y = r.Bottom + r.Height * i / 4f;
                float arm = i == 0 || i == 4 ? 5f : 3f;
                AvStrokes.Line(b, r.CenterX - arm, y, r.CenterX + arm, y, 1f, dim);
            }
        }

        private static void Outline(AvQuadBuffer b, float x0, float y0, float x1, float y1, float w, Rgba c)
        {
            AvStrokes.Line(b, x0, y0, x1, y0, w, c);
            AvStrokes.Line(b, x1, y0, x1, y1, w, c);
            AvStrokes.Line(b, x1, y1, x0, y1, w, c);
            AvStrokes.Line(b, x0, y1, x0, y0, w, c);
        }

        private static void PositionRect(RectTransform rt, RectF rect)
        {
            Vector2 pos = new Vector2(rect.CenterX, rect.CenterY);
            if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
            Vector2 size = new Vector2(rect.Width, rect.Height);
            if (rt.sizeDelta != size) rt.sizeDelta = size;
        }

        private void WriteSpeed(float speedMps, AvUnits units)
        {
            if (!float.IsFinite(speedMps)) return;
            float rounded = Mathf.Round(AvUnitTable.Speed(speedMps, units));
            if (spd.Initialized && spd.LastValue == rounded) return;
            int len = AvUnitTable.SpeedReading(buf, 0, speedMps, units);
            spd.Value.text = new string(buf, 0, len);
            spd.LastValue = rounded;
            spd.Initialized = true;
        }

        private void WriteAltitude(float altitudeM, AvUnits units)
        {
            if (!float.IsFinite(altitudeM)) return;
            float rounded = Mathf.Round(AvUnitTable.Altitude(altitudeM, units));
            if (alt.Initialized && alt.LastValue == rounded) return;
            int len = AvUnitTable.AltitudeReading(buf, 0, altitudeM, units);
            alt.Value.text = new string(buf, 0, len);
            alt.LastValue = rounded;
            alt.Initialized = true;
        }

        /// <summary>"ALT" normally; "R 120ft" under <see cref="HudCues.ShowRadarBelowM"/>;
        /// "PULL UP" when the ground is seconds away.</summary>
        private void WriteAltCaption(float radarAltM, TerrainCue terrain, AvUnits units)
        {
            bool showRadar = float.IsFinite(radarAltM) && radarAltM < HudCues.ShowRadarBelowM;
            float key = terrain == TerrainCue.PullUp ? -2f : showRadar ? Mathf.Round(AvUnitTable.Altitude(radarAltM, units) / 10f) : -1f;
            if (alt.LastCaption == key) return;
            int len;
            if (key == -2f) len = AvNumFormat.Append(buf, 0, "PULL UP");
            else if (key == -1f) len = AvNumFormat.Append(buf, 0, "ALT");
            else
            {
                len = AvNumFormat.Append(buf, 0, "R ");
                len = AvUnitTable.AltitudeReading(buf, len, radarAltM, units);
            }
            alt.Caption.text = new string(buf, 0, len);
            alt.LastCaption = key;
        }

        private void WriteHeading(float headingDeg)
        {
            float normalized = ((headingDeg % 360f) + 360f) % 360f;
            int rounded = Mathf.RoundToInt(normalized) % 360;
            if (hdg.Initialized && hdg.LastValue == rounded) return;
            buf[0] = (char)('0' + rounded / 100);
            buf[1] = (char)('0' + rounded / 10 % 10);
            buf[2] = (char)('0' + rounded % 10);
            buf[3] = '°';
            int len = AvNumFormat.Append(buf, 4, " <size=70%>");
            len = AvNumFormat.Append(buf, len, HudCues.Cardinal(rounded));
            hdg.Value.text = new string(buf, 0, len);
            hdg.LastValue = rounded;
            hdg.Initialized = true;
        }

        private void WriteMachG(float mach, float gForce)
        {
            if (!float.IsFinite(mach) || !float.IsFinite(gForce)) return;
            // Coarse change detection: round to the display's own precision so sub-pixel float
            // jitter every frame does not force a string allocation every frame.
            float key = Mathf.Round(mach * 100f) + Mathf.Round(gForce * 10f) * 10000f;
            if (spd.Initialized && spd.LastSub == key) return;

            int len = AvNumFormat.Append(buf, 0, "M ");
            len = AvNumFormat.Write(buf, len, mach, 2);
            len = AvNumFormat.Append(buf, len, "  G ");
            len = AvNumFormat.Write(buf, len, gForce, 1);
            spd.SubLine.text = new string(buf, 0, len);
            spd.LastSub = key;
        }

        private void WriteClimb(float climbMps, AvUnits units)
        {
            if (!float.IsFinite(climbMps)) return;
            float key = Mathf.Round(AvUnitTable.Climb(climbMps, units));
            if (alt.Initialized && alt.LastSub == key) return;

            int len = AvUnitTable.ClimbRateReading(buf, 0, climbMps, units);
            alt.SubLine.text = new string(buf, 0, len);
            alt.LastSub = key;
        }

        private void PresentTargetCard(Texture cameraTexture, string code, float rangeM, float closureMps,
            float screenW, float screenH, Color ink, AvUnits units)
        {
            bool show = cameraTexture != null;
            if (card.Active != show)
            {
                card.Root.gameObject.SetActive(show);
                card.Active = show;
            }
            if (!show) return;

            PositionRect(card.Root, ThirdPersonHudLayout.TargetCard(screenW, screenH, true));
            card.Backing.color = new Color(0.020f, 0.039f, 0.059f, 0.88f);
            card.TagText.color = ink;
            for (int i = 0; i < card.Reticle.Length; i++) card.Reticle[i].color = WithAlpha(ink, 0.7f);
            if (!ReferenceEquals(card.Feed.texture, cameraTexture)) card.Feed.texture = cameraTexture;

            string header = code ?? "--";
            if (card.LastHeader != header)
            {
                card.Header.text = header;
                card.LastHeader = header;
            }
            card.Header.color = ink;

            float rangeKey = float.IsFinite(rangeM) ? Mathf.Round(rangeM / 100f) : -1f;
            if (card.LastRange != rangeKey)
            {
                int len = rangeKey < 0f ? AvNumFormat.Unknown(buf, 0) : AvUnitTable.DistanceReading(buf, 0, rangeM, units);
                card.Range.text = new string(buf, 0, len);
                card.LastRange = rangeKey;
            }
            card.Range.color = WithAlpha(ink, 0.85f);

            // Closing reads in ink, opening dimmer; under 5 m/s either way is "steady".
            float closureKey = float.IsFinite(closureMps) ? Mathf.Round(AvUnitTable.Speed(closureMps, units) / 5f) : float.NaN;
            if (!(card.LastClosure == closureKey) && !(float.IsNaN(card.LastClosure) && float.IsNaN(closureKey)))
            {
                int len;
                if (float.IsNaN(closureKey)) len = 0;
                else if (Mathf.Abs(closureMps) < 5f) len = AvNumFormat.Append(buf, 0, "STEADY");
                else
                {
                    len = AvNumFormat.Append(buf, 0, closureMps > 0f ? "CLOSING " : "OPENING ");
                    len = AvUnitTable.SpeedReading(buf, len, Mathf.Abs(closureMps), units);
                }
                card.Closure.text = new string(buf, 0, len);
                card.LastClosure = closureKey;
            }
            card.Closure.color = float.IsFinite(closureMps) && closureMps > 5f ? ink : WithAlpha(ink, 0.6f);
        }

        /// <summary>Take the cluster out of view without destroying it. Idempotent.</summary>
        public void Hide()
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
            builtActive = false;
            if (card.Feed != null) card.Feed.texture = null;
        }

        /// <summary>Drop every object this cluster created. Safe when the native canvas already
        /// took the tree with it (root reads as Unity's fake-null and the destroy is a no-op).</summary>
        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            canvasRoot = null;
            spd = default;
            alt = default;
            hdg = default;
            card = default;
            symbols = null;
            lastSymbols = 0;
            builtActive = false;
        }
    }
}
