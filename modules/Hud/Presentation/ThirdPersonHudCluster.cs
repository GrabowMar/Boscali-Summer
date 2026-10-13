using NOAvionics;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
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
    /// and for the target card corner brackets over the feed with a soft shade behind its header (the visor look: no slab, no closed outline,
    /// the "TGT" tag an open tag) -- the same symbology language as the native flight HUD, no plates. Cues light brackets and text like annunciators: SPD on
    /// over-G, ALT on low ground (radar altitude replaces its caption under 300 m; PULL UP in alert
    /// colour), the fuel scale on low fuel. HDG adds the compass point; the card shows range and closure.
    /// Legibility (2026-10-10): a dark scrim behind every box and bar label so thin ink survives bright haze, captions at
    /// 12-13 pt, the whole cluster scaled by the player's own HUD text size (the layout runs in a virtual screen of
    /// screen / scale and the root is scaled back up), AoA in the SPD caption row, the real load factor against the airframe's
    /// limit, and the fuel scale on the same 25 % / 10 % thresholds as the board's fuel row.
    /// </summary>
    internal sealed class ThirdPersonHudCluster
    {
        private static readonly FieldInfo WeaponStatusField = AccessTools.Field(typeof(CombatHUD), "weaponStatus");
        private static readonly FieldInfo NameTextField = AccessTools.Field(typeof(WeaponStatus), "nameText");

        /// <summary>One tick of own-aircraft flight data.</summary>
        internal struct Flight
        {
            public float SpeedMps, AltitudeM, RadarAltM, ClimbMps, HeadingDeg, Mach, G, GLimit, Fuel, Throttle, BankDeg, SlipDeg;
            /// <summary>Angle of attack in degrees; NaN below the native readout's speed gate.</summary>
            public float AoaDeg;
            /// <summary>The fuel row's BINGO / JOKER flags, so the fuel scale says the same thing as the board.</summary>
            public bool FuelBingo, FuelJoker;
        }

        private readonly char[] buf = new char[48];

        private RectTransform canvasRoot;
        private RectTransform root;
        private TMP_FontAsset font;
        private Material fontMaterial;

        private Box spd, alt;
        private Box hdg;
        private TargetCardWidgets card, missile;
        // The missile feed for the next Present (SetMissileFeed): its card shows in the cockpit too, where the flight boxes do not.
        private Texture missileFeed;
        private string missileName;
        private float missileRangeM;
        private bool missileLost, missileRaised;

        private bool builtActive, flightShown;
        private AvVector symbols;
        private int lastSymbols;
        private const float Bracket = 9f, Stroke = 1.5f;
        /// <summary>The target card's corner brackets: arm 10, hung on the card's own corners.</summary>
        private const float CardArm = 10f;
        /// <summary>The soft shade behind the card stands this far outside it (its feathered edge is HudSprites.FeatherEdge wide, so the card's edge is under the full part) at this alpha.</summary>
        private const float CardShadeMargin = 44f, CardShadeAlpha = 0.45f;
        private float lastScale = 1f;

        private struct Box
        {
            public RectTransform Root;
            public Image Backing;
            public TextMeshProUGUI Caption;
            /// <summary>Right end of the caption row (the SPD box's AoA).</summary>
            public TextMeshProUGUI CaptionRight;
            public TextMeshProUGUI Value;
            public TextMeshProUGUI SubLine;
            public RectTransform Track;
            public Image TrackImage;
            public Image Bar;
            public TextMeshProUGUI BarLabel;
            public float LastValue;
            public float LastSub;
            public float LastCaption;
            public float LastRight;
            public float LastBar;
            public bool Initialized;
        }

        private struct TargetCardWidgets
        {
            public RectTransform Root;
            public Image Backing;
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

        /// <summary>The flight boxes are on screen (the board asks so that it can take them down the frame third person ends while the missile card stays).</summary>
        public bool FlightShown => builtActive && flightShown;

        /// <summary>The missile feed card for the next <see cref="Present"/>: null <paramref name="feed"/> is no card. <paramref name="raised"/> (the cockpit: NAV DATA has the corner)
        /// puts it one slot up even with no target card under it.</summary>
        public void SetMissileFeed(Texture feed, string name, float rangeM, bool lost, bool raised)
        {
            missileFeed = feed;
            missileName = name;
            missileRangeM = rangeM;
            missileLost = lost;
            missileRaised = raised;
        }

        /// <summary>The TAC BOARD's box on the screen (centre-origin, y up, canvas units; no size: no board): the boxes of the cluster keep clear of it (<see cref="ThirdPersonHudLayout.KeepClearOf"/>). Set by the board before <see cref="Present"/>.</summary>
        public RectF Board;
        /// <summary><see cref="Board"/> in the layout's units (the canvas over the text scale).</summary>
        private RectF avoid;

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
            lastScale = 1f;

            spd = BuildBox("SPD", "SPD");
            alt = BuildBox("ALT", "ALT");
            hdg = BuildBox("HDG", null, withBar: false);
            card = BuildTargetCard("TargetCard", "TGT");
            missile = BuildTargetCard("MissileCard", "MSL");
            // Root is zero-size at screen centre, so this surface's origin is screen centre: RectF space.
            symbols = AvVector.Create(root, "Symbols", 900);
            lastSymbols = 0;

            rootObject.SetActive(false);
            builtActive = false;
            flightShown = true;
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

            bool hasSub = caption != null;
            TextMeshProUGUI captionText = null, captionRight = null;
            if (hasSub)
            {
                captionText = Label(rect, "Caption", TextAlignmentOptions.TopLeft, 13f,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(7f, -19f), new Vector2(-7f, -4f));
                captionText.characterSpacing = 6f;
                captionText.text = caption;
                captionRight = Label(rect, "CaptionRight", TextAlignmentOptions.TopRight, 13f,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(7f, -19f), new Vector2(-7f, -4f));
            }

            // Caption owns the top 16 px; the value sits between it and the sub-line (mono faces run wide).
            TextMeshProUGUI value = Label(rect, "Value", TextAlignmentOptions.Center, hasSub ? 22f : 21f,
                new Vector2(0f, hasSub ? 0.3f : 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, hasSub ? -17f : 0f));

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
                trackImage.color = Scrim(null);   // the scale sits on a plate, not straight on the sky

                // The fill is a solid 3 px line up the middle of the tick scale drawn in PaintSymbols.
                bar = HudSprites.Child(track, "Bar", AvSprites.White, sliced: false);
                ((RectTransform)bar.transform).offsetMin = new Vector2(1.5f, 0f);
                ((RectTransform)bar.transform).offsetMax = new Vector2(-1.5f, 0f);
                // Filled needs a sprite: without one Unity ignores fillAmount and draws the full rect.
                bar.type = Image.Type.Filled;
                bar.fillMethod = Image.FillMethod.Vertical;
                bar.fillOrigin = 0;
                bar.fillAmount = 0f;

                // Live percentage under the track, wider than the bar itself, on its own scrim (it sits outside the box's).
                var scrimObject = new GameObject("LabelScrim", typeof(RectTransform), typeof(Image));
                var scrim = (RectTransform)scrimObject.transform;
                scrim.SetParent(track, false);
                scrim.anchorMin = scrim.anchorMax = new Vector2(0.5f, 0f);
                scrim.offsetMin = new Vector2(-36f, -19f);
                scrim.offsetMax = new Vector2(36f, -1f);
                Image scrimImage = scrimObject.GetComponent<Image>();
                scrimImage.raycastTarget = false;
                scrimImage.sprite = AvSprites.White;
                scrimImage.color = Scrim(null);
                barLabel = Label(track, "BarLabel", TextAlignmentOptions.Top, 12f,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-35f, -18f), new Vector2(35f, -2f));
            }

            return new Box
            {
                Root = rect, Backing = backing, Caption = captionText, CaptionRight = captionRight, Value = value, SubLine = sub,
                Track = track, TrackImage = trackImage, Bar = bar, BarLabel = barLabel,
                LastValue = float.NaN, LastSub = float.NaN, LastCaption = float.NaN, LastRight = float.NaN, LastBar = float.NaN
            };
        }

        private TargetCardWidgets BuildTargetCard(string name, string tagWord)
        {
            const float header = ThirdPersonHudLayout.TargetCardHeaderHeight;
            var cardObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)cardObject.transform;
            rect.SetParent(root, false);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // No slab: a feathered patch of shade a little larger than the card, under everything of it (first child), in the board's shade colour.
            Image backing = HudSprites.Child(rect, "Shade", HudSprites.Feather, sliced: true);
            backing.rectTransform.offsetMin = new Vector2(-CardShadeMargin, -CardShadeMargin);
            backing.rectTransform.offsetMax = new Vector2(CardShadeMargin, CardShadeMargin);
            backing.color = new Color(HudSprites.ShadeTint.r, HudSprites.ShadeTint.g, HudSprites.ShadeTint.b, CardShadeAlpha);

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
            tagText.text = tagWord;

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
                Root = rect, Backing = backing, Tag = tag, TagText = tagText, Header = title, Range = range,
                Closure = closure, Feed = feed, Reticle = reticle, Active = false, LastRange = float.NaN, LastClosure = float.NaN
            };
        }

        /// <summary>Refresh from this tick's flight data. <paramref name="active"/> is the
        /// caller's third-person-own-aircraft gate; false hides the whole cluster (one root
        /// toggle). Called at 10 Hz by the board. <paramref name="cameraTexture"/> null hides the
        /// target card; <paramref name="rangeM"/>/<paramref name="closureMps"/> NaN show "--".
        /// Colour writes are no-ops when unchanged (Graphic only dirties on a different value).</summary>
        public void Present(bool active, AvUnits units, in Flight f,
            Texture cameraTexture, string cameraCode, float rangeM, float closureMps, float scale)
        {
            if (root == null) return;
            // The missile card shows without the flight cluster too (the cockpit): the root is up for either, the boxes and their bars only for the cluster.
            bool shown = active || missileFeed != null;
            if (shown != builtActive)
            {
                root.gameObject.SetActive(shown);
                builtActive = shown;
            }
            if (!shown) return;
            if (active != flightShown)
            {
                flightShown = active;
                spd.Root.gameObject.SetActive(active);
                alt.Root.gameObject.SetActive(active);
                hdg.Root.gameObject.SetActive(active);
                spd.Track.gameObject.SetActive(active);
                alt.Track.gameObject.SetActive(active);
            }
            if (!active) cameraTexture = null;

            // Reference frame is the HUD canvas's own local rect, not raw Screen.width/height:
            // anchoredPosition under this canvas is in the canvas's own (possibly scaled) units.
            // The layout runs in a virtual screen of screen / scale and the root is scaled back up, so every box, text and stroke
            // follows the player's own HUD text size and the cluster still lands where the screen edges are.
            scale = float.IsFinite(scale) && scale > 0.1f ? scale : 1f;
            if (scale != lastScale)
            {
                root.localScale = new Vector3(scale, scale, 1f);
                lastScale = scale;
            }
            float screenW = canvasRoot.rect.width / scale;
            float screenH = canvasRoot.rect.height / scale;
            if (screenW <= 0f || screenH <= 0f) return;
            avoid = new RectF(Board.CenterX / scale, Board.CenterY / scale, Board.Width / scale, Board.Height / scale);

            ColorTheme theme = ThemeManager.Active.ColorTheme;
            Color ink = theme.AllClear;
            Color caution = theme.Warning;
            Color alert = theme.Alert;

            Color? spdLit = null, altLit = null;
            Color fuelColor = ink;
            bool pullUpFlash = false;
            if (active)
            {
                RectF spdRect = ThirdPersonHudLayout.SpeedBox(screenW, screenH, avoid);
                RectF altRect = ThirdPersonHudLayout.AltitudeBox(screenW, screenH, avoid);
                PositionRect(spd.Root, spdRect);
                PositionRect(alt.Root, altRect);
                PositionRect(hdg.Root, ThirdPersonHudLayout.HeadingBox(screenW, screenH, avoid));
                PositionRect(spd.Track, ThirdPersonHudLayout.SideBar(spdRect, outwardIsLeft: true));
                PositionRect(alt.Track, ThirdPersonHudLayout.SideBar(altRect, outwardIsLeft: false));

                bool overG = float.IsFinite(f.G) && Mathf.Abs(f.G) >= BoscaliSummer.Core.Math.FlightLoad.CautionAt(f.GLimit);
                TerrainCue terrain = HudCues.Terrain(f.RadarAltM, f.ClimbMps);
                HudTone fuelTone = FuelEndurance.Tone(f.Fuel, f.FuelBingo, f.FuelJoker);
                fuelColor = fuelTone == HudTone.Warning ? alert : fuelTone == HudTone.Caution ? caution : ink;
                spdLit = overG ? caution : (Color?)null;
                altLit = terrain == TerrainCue.PullUp ? alert : terrain == TerrainCue.Low ? caution : (Color?)null;
                Style(spd, ink, spdLit);
                Style(alt, ink, altLit);
                Style(hdg, ink, null);
                Fill(ref spd, Mathf.Clamp01(f.Throttle), ink, "THR ");
                Fill(ref alt, Mathf.Clamp01(f.Fuel), fuelColor, FuelText.Legend(f.FuelBingo, f.FuelJoker, fuelTone) + " ");
                pullUpFlash = terrain == TerrainCue.PullUp && (int)(Time.unscaledTime * 4f) % 2 == 0;

                WriteSpeedCaption(overG);
                WriteSpeed(f.SpeedMps, units);
                WriteAltitude(f.AltitudeM, units);
                WriteAltCaption(f.RadarAltM, terrain, units);
                WriteHeading(f.HeadingDeg);
                WriteMachG(f.Mach, f.G);
                WriteAoa(overG ? float.NaN : f.AoaDeg);   // the 106 px caption row holds OVER-G or the AoA beside SPD, not both
                WriteClimb(f.ClimbMps, units);
            }
            // The missile card sits on the target card when there is one, in its place when there is not (unless told to stay up).
            bool raised = missileRaised || cameraTexture != null;
            PaintSymbols(screenW, screenH, ink, spdLit, altLit, fuelColor, cameraTexture != null, pullUpFlash,
                f.BankDeg, f.SlipDeg, active, missileFeed == null ? 0 : raised ? 2 : 1);

            PresentTargetCard(cameraTexture, cameraCode, rangeM, closureMps, screenW, screenH, ink, units);
            PresentMissileCard(ThirdPersonHudLayout.MissileCard(screenW, screenH, raised), ink, units);
        }

        /// <summary>The RWR panel look: near-black plate tinted toward the theme ink.</summary>
        private static Color PlateTint(Color ink) => new Color(ink.r * 0.04f, ink.g * 0.07f, ink.b * 0.08f, 0.62f);

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>A dark scrim so thin ink survives bright haze (a lit cue tints it toward its colour).</summary>
        private static Color Scrim(Color? lit) => lit.HasValue
            ? new Color(0.01f + lit.Value.r * 0.10f, 0.02f + lit.Value.g * 0.10f, 0.03f + lit.Value.b * 0.10f, 0.88f)
            : new Color(0.01f, 0.02f, 0.03f, 0.72f);

        /// <summary>Normal: ink on the scrim. Lit: value, caption and sub-line in <paramref name="lit"/>, on a scrim tinted with
        /// it so the cue survives a bright sky (brackets light in PaintSymbols).</summary>
        private static void Style(Box box, Color ink, Color? lit)
        {
            box.Backing.color = Scrim(lit);
            box.Value.color = lit.HasValue ? lit.Value : ink;
            if (box.Caption != null) box.Caption.color = lit.HasValue ? lit.Value : WithAlpha(ink, 0.75f);
            if (box.CaptionRight != null) box.CaptionRight.color = lit.HasValue ? lit.Value : ink;
            box.SubLine.color = lit.HasValue ? lit.Value : WithAlpha(ink, 0.9f);
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
            bool cardShown, bool flash, float bankDeg, float slipDeg, bool flight, int missileSlot)
        {
            // Bank in 2 degree steps and slip in whole degrees: finer motion is not visible and would rebuild the mesh every tick in a turn.
            int key = (int)screenW * 7919 + (int)screenH * 31 + (int)(lastScale * 100f) * 977 + Code(spdLit) * 5 + Code(altLit) * 3 + Code(fuel) + Code(ink) * 17 +
                (cardShown ? 11 : 0) + (flash ? 13 : 0) + (flight ? 19 : 0) + missileSlot * 23 + Mathf.RoundToInt(bankDeg / 2f) * 104729 + Mathf.RoundToInt(slipDeg) * 7 +
                Mathf.RoundToInt(avoid.CenterX) * 15485863 + Mathf.RoundToInt(avoid.CenterY) * 32452843 + Mathf.RoundToInt(avoid.Width) * 49979687 + Mathf.RoundToInt(avoid.Height) * 67867967;
            if (key == lastSymbols) return;
            lastSymbols = key;

            AvQuadBuffer b = symbols.Buffer;
            b.Clear();
            if (flight)
            {
                RectF spdRect = ThirdPersonHudLayout.SpeedBox(screenW, screenH, avoid);
                RectF altRect = ThirdPersonHudLayout.AltitudeBox(screenW, screenH, avoid);
                Brackets(b, spdRect, (spdLit ?? ink).ToRgba());
                Brackets(b, altRect, (flash ? Color.clear : altLit ?? ink).ToRgba());
                Brackets(b, ThirdPersonHudLayout.HeadingBox(screenW, screenH, avoid), ink.ToRgba().WithAlpha(0.8f));
                Scale(b, ThirdPersonHudLayout.SideBar(spdRect, outwardIsLeft: true), ink.ToRgba());
                Scale(b, ThirdPersonHudLayout.SideBar(altRect, outwardIsLeft: false), fuel.ToRgba());
                BankAndSlip(b, ThirdPersonHudLayout.HeadingBox(screenW, screenH, avoid), bankDeg, slipDeg, ink.ToRgba());
            }
            Rgba line = ink.ToRgba().WithAlpha(0.9f);
            if (cardShown) CardFrame(b, ThirdPersonHudLayout.TargetCard(screenW, screenH, true), line);
            if (missileSlot > 0) CardFrame(b, ThirdPersonHudLayout.MissileCard(screenW, screenH, missileSlot == 2), line);
            BufferGuard.Check(b, "third-person cluster");
            symbols.Commit();
        }

        /// <summary>A feed card's ink: corner brackets, the open tag (a tick on its left edge and a hairline under it, not a box) and the hairline under the header.</summary>
        private static void CardFrame(AvQuadBuffer b, RectF card, Rgba line)
        {
            CornerBrackets(b, card.Left, card.Bottom, card.Right, card.Top, CardArm, Stroke, line);
            float tagTop = card.Top - 4f, tagLeft = card.Left + 5f, tagBottom = tagTop - (ThirdPersonHudLayout.TargetCardHeaderHeight - 7f);
            AvStrokes.Line(b, tagLeft, tagBottom, tagLeft, tagTop, 2f, line);
            AvStrokes.Line(b, tagLeft, tagBottom, tagLeft + 34f, tagBottom, 1f, line.WithAlpha(0.55f));
            AvStrokes.Line(b, card.Left, card.Top - ThirdPersonHudLayout.TargetCardHeaderHeight + 2f,
                card.Right, card.Top - ThirdPersonHudLayout.TargetCardHeaderHeight + 2f, 1f, line.WithAlpha(0.5f));
        }

        private static readonly float[] BankTicks = { -45f, -30f, -20f, -10f, 0f, 10f, 20f, 30f, 45f };

        /// <summary>
        /// Bank &amp; slip under the HDG box (after NO_Tactitools): a ±45° tick arc with a pointer swung to the bank side
        /// (clamped at 50°), and a slip ball between cage marks that rolls away from the slip like a real inclinometer.
        /// </summary>
        private static void BankAndSlip(AvQuadBuffer b, RectF hdg, float bankDeg, float slipDeg, Rgba ink)
        {
            const float R = 64f, MaxBank = 45f, BallTravel = 22f, SlipFull = 10f;
            float cx = hdg.CenterX, top = hdg.Bottom - 12f, cy = top - R;
            Rgba dim = ink.WithAlpha(0.7f);
            // A dark plate behind the arc, ticks and tube so the thin ink survives a bright haze.
            float plateBottom = top - R * (1f - Mathf.Cos(MaxBank * Mathf.Deg2Rad)) - 24f;
            AvStrokes.Fill(b, cx - R * 0.72f - 12f, plateBottom, R * 1.44f + 24f, top + 12f - plateBottom, new Rgba(0.01f, 0.02f, 0.03f, 0.72f));
            AvStrokes.Arc(b, cx, cy, R, 90f - MaxBank, 90f + MaxBank, 24, 1.5f, dim);
            foreach (float tick in BankTicks)
            {
                float a = (90f - tick) * Mathf.Deg2Rad, len = tick == 0f || Mathf.Abs(tick) == 45f ? 10f : 6f;
                AvStrokes.Line(b, cx + Mathf.Cos(a) * R, cy + Mathf.Sin(a) * R, cx + Mathf.Cos(a) * (R + len), cy + Mathf.Sin(a) * (R + len), 1.5f, dim);
            }
            float p = (90f - Mathf.Clamp(bankDeg, -50f, 50f)) * Mathf.Deg2Rad;
            float px = cx + Mathf.Cos(p) * (R - 2f), py = cy + Mathf.Sin(p) * (R - 2f);
            AvStrokes.Chevron(b, px, py, 9f, p * Mathf.Rad2Deg, 2f, ink);

            float tubeY = top - R * (1f - Mathf.Cos(MaxBank * Mathf.Deg2Rad)) - 12f, ballX = cx - Mathf.Clamp(slipDeg / SlipFull, -1f, 1f) * BallTravel;
            AvStrokes.Line(b, cx - BallTravel - 6f, tubeY, cx + BallTravel + 6f, tubeY, 1.5f, dim);
            AvStrokes.Line(b, cx - 7f, tubeY - 6f, cx - 7f, tubeY + 6f, 1.5f, dim);
            AvStrokes.Line(b, cx + 7f, tubeY - 6f, cx + 7f, tubeY + 6f, 1.5f, dim);
            AvStrokes.Ring(b, ballX, tubeY, 2.2f, 10, 4.4f, ink);
        }

        private static int Code(Color? c) => c.HasValue ? Code(c.Value) : 0;
        private static int Code(Color c)
        {
            Color32 p = c;
            return (p.r << 24) | (p.g << 16) | (p.b << 8) | p.a;
        }

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

        /// <summary>Four corner brackets round a rectangle (y up): an arm of <paramref name="arm"/> each way from every corner, nothing between them.</summary>
        private static void CornerBrackets(AvQuadBuffer b, float l, float bt, float r, float t, float arm, float w, Rgba c)
        {
            AvStrokes.Line(b, l, t, l + arm, t, w, c);
            AvStrokes.Line(b, l, t, l, t - arm, w, c);
            AvStrokes.Line(b, r, t, r - arm, t, w, c);
            AvStrokes.Line(b, r, t, r, t - arm, w, c);
            AvStrokes.Line(b, l, bt, l + arm, bt, w, c);
            AvStrokes.Line(b, l, bt, l, bt + arm, w, c);
            AvStrokes.Line(b, r, bt, r - arm, bt, w, c);
            AvStrokes.Line(b, r, bt, r, bt + arm, w, c);
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

        /// <summary>"SPD" normally, "OVER-G" while the load factor cautions (the amber box says why).</summary>
        private void WriteSpeedCaption(bool overG)
        {
            float key = overG ? -2f : -1f;
            if (spd.LastCaption == key) return;
            spd.Caption.text = overG ? "OVER-G" : "SPD";
            spd.LastCaption = key;
        }

        /// <summary>"ALT" normally; "R 120ft" under <see cref="HudCues.ShowRadarBelowM"/>, "LOW R 120ft" when sinking low;
        /// "PULL UP" when the ground is seconds away.</summary>
        private void WriteAltCaption(float radarAltM, TerrainCue terrain, AvUnits units)
        {
            bool showRadar = float.IsFinite(radarAltM) && radarAltM < HudCues.ShowRadarBelowM, low = terrain == TerrainCue.Low;
            float key = terrain == TerrainCue.PullUp ? -2f : showRadar ? Mathf.Round(AvUnitTable.Altitude(radarAltM, units) / 10f) + (low ? 1e6f : 0f) : -1f;
            if (alt.LastCaption == key) return;
            int len;
            if (key == -2f) len = AvNumFormat.Append(buf, 0, "PULL UP");
            else if (key == -1f) len = AvNumFormat.Append(buf, 0, "ALT");
            else
            {
                len = AvNumFormat.Append(buf, 0, low ? "LOW R " : "R ");
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

        /// <summary>"AOA 4.2°" at the right end of the SPD caption row; blank while the native readout is off (slow or no data) and while OVER-G fills the row.</summary>
        private void WriteAoa(float aoaDeg)
        {
            if (spd.CaptionRight == null) return;
            float key = float.IsFinite(aoaDeg) ? Mathf.Round(aoaDeg * 10f) : float.NaN;
            if (spd.LastRight == key || (float.IsNaN(spd.LastRight) && float.IsNaN(key))) return;
            if (float.IsNaN(key)) spd.CaptionRight.text = "";
            else
            {
                int len = AvNumFormat.Append(buf, 0, "AOA ");
                len = AvNumFormat.Write(buf, len, aoaDeg, 1);
                buf[len++] = '°';
                spd.CaptionRight.text = new string(buf, 0, len);
            }
            spd.LastRight = key;
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
            if (!PresentCard(ref card, ThirdPersonHudLayout.TargetCard(screenW, screenH, true), cameraTexture, code, rangeM, ink, units)) return;

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

        /// <summary>The missile feed card: the weapon's name, the range to what it guides on, SIGNAL LOST over the held last picture.</summary>
        private void PresentMissileCard(RectF rect, Color ink, AvUnits units)
        {
            if (!PresentCard(ref missile, rect, missileFeed, missileName, missileRangeM, ink, units)) return;
            float key = missileLost ? 1f : 0f;
            if (missile.LastClosure != key)
            {
                missile.Closure.text = missileLost ? "SIGNAL LOST" : "";
                missile.LastClosure = key;
            }
            missile.Closure.color = ink;
        }

        /// <summary>What both feed cards share: shown while there is a picture, placed, its feed, header and range written on change. False while hidden.</summary>
        private bool PresentCard(ref TargetCardWidgets c, RectF rect, Texture feed, string header, float rangeM, Color ink, AvUnits units)
        {
            bool show = feed != null;
            if (c.Active != show)
            {
                c.Root.gameObject.SetActive(show);
                c.Active = show;
            }
            if (!show) return false;

            PositionRect(c.Root, rect);
            c.TagText.color = ink;
            for (int i = 0; i < c.Reticle.Length; i++) c.Reticle[i].color = WithAlpha(ink, 0.7f);
            if (!ReferenceEquals(c.Feed.texture, feed)) c.Feed.texture = feed;

            header ??= "--";
            if (c.LastHeader != header)
            {
                c.Header.text = header;
                c.LastHeader = header;
            }
            c.Header.color = ink;

            float rangeKey = float.IsFinite(rangeM) ? Mathf.Round(rangeM / 100f) : -1f;
            if (c.LastRange != rangeKey)
            {
                int len = rangeKey < 0f ? AvNumFormat.Unknown(buf, 0) : AvUnitTable.DistanceReading(buf, 0, rangeM, units);
                c.Range.text = new string(buf, 0, len);
                c.LastRange = rangeKey;
            }
            c.Range.color = WithAlpha(ink, 0.85f);
            return true;
        }

        /// <summary>Take the cluster out of view without destroying it. Idempotent.</summary>
        public void Hide()
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
            builtActive = false;
            if (card.Feed != null) card.Feed.texture = null;
            if (missile.Feed != null) missile.Feed.texture = null;
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
            missile = default;
            symbols = null;
            lastSymbols = 0;
            builtActive = false;
        }
    }
}
