using BoscaliSummer.Core.Game;
using NOAvionics;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
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
    /// Look (2026-11-17 refit, the board's ONE BAR grammar): a Dim bold caption over a Dim hairline rule (none on HDG) and the value as a bare INK-HI EMPHASIS figure, a 3 px bar for throttle and fuel on a Faint
    /// track, and for the feed cards the shared FeedCorner chrome (a rule over the picture, 6-long brackets) -- no plates, no corner brackets, no flashing. A cue only turns the CAPTION amber and steady: SPD on
    /// over-G, ALT on low ground (radar altitude replaces its caption under 300 m; PULL UP), the fuel bar on low fuel (never red). HDG adds the compass point; the card shows range and closure.
    /// Legibility (2026-10-10; 2026-10-13 nothing behind the boxes, by the user's ruling: bare ink): captions at
    /// 12-13 pt, the whole cluster scaled by the player's own HUD text size (the layout runs in a virtual screen of
    /// screen / scale and the root is scaled back up), AoA in the SPD caption row, the real load factor against the airframe's
    /// limit, and the fuel scale on the same 25 % / 10 % thresholds as the board's fuel row.
    /// </summary>
    internal sealed class ThirdPersonHudCluster
    {
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
        private bool missileLost;
        private float missileClosureMps = float.NaN, missileTtiS = float.NaN;

        /// <summary>The target's aspect angle (<see cref="TargetReadout.Aspect"/>; NaN = none) for the TARGET CAM's data strip. Set by the board before <see cref="Present"/>.</summary>
        public float CardAspectDeg = float.NaN;
        /// <summary>The picture already carries the game's own tac-screen text (the cockpit). Kept for the board's write; the card draws no name of its own on the picture any more (the name is on the shelf).</summary>
        public bool CardNativeText = true;

        private bool builtActive, flightShown;
        private AvVector symbols;
        private int lastSymbols;
        private float lastScale = 1f;

        private struct Box
        {
            public RectTransform Root;
            public TextMeshProUGUI Caption;
            /// <summary>Right end of the caption row (the SPD box's AoA).</summary>
            public TextMeshProUGUI CaptionRight;
            public TextMeshProUGUI Value;
            public TextMeshProUGUI SubLine;
            public RectTransform Track;
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
            public TextMeshProUGUI Name;         // the shelf's word at the left (TARGET CAM / MISSILE CAM)
            public TextMeshProUGUI State;         // the shelf's word at the right (what it looks at, its state)
            public TextMeshProUGUI Count;         // on the picture, bottom left (the missile's countdown)
            public TextMeshProUGUI Note;          // on the picture, bottom right (SIGNAL LOST)
            public TextMeshProUGUI[] Cells;       // the data strip's three cells: left, centre, right
            public RawImage Feed;
            public bool Active;
            public string LastState, LastNote, LastCode, StateText;
            public float StripPt, LastPicW, LastPicH, LastTexAspect;      // LastPicW / H start at -1 (set in BuildTargetCard)
            public int LastCount;
            public int[] CellKey;
            /// <summary>What the mesh draws over the picture (a lock bracket): part of the chrome signature.</summary>
            public bool Locked;
        }

        /// <summary>The flight boxes are on screen (the board asks so that it can take them down the frame third person ends while the missile card stays).</summary>
        public bool FlightShown => builtActive && flightShown;

        /// <summary>The target card is on screen (the board asks so that it does not take the root down between two reads while the cockpit card is up).</summary>
        public bool TargetCardShown => builtActive && card.Active;

        /// <summary>The missile feed card for the next <see cref="Present"/>: null <paramref name="feed"/> is no card.</summary>
        public void SetMissileFeed(Texture feed, string name, float rangeM, bool lost, float closureMps = float.NaN, float ttiS = float.NaN)
        {
            missileFeed = feed;
            missileName = name;
            missileRangeM = rangeM;
            missileLost = lost;
            missileClosureMps = closureMps;
            missileTtiS = ttiS;
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

            if (!NativeRefs.HudCanvas(out CombatHUD hud, out Canvas hudCanvas)) return false;

            Build(hud, hudCanvas);
            return root != null;
        }

        private void Build(CombatHUD hud, Canvas hudCanvas)
        {
            canvasRoot = hudCanvas.transform as RectTransform;
            if (canvasRoot == null) canvasRoot = hudCanvas.GetComponent<RectTransform>();

            WeaponStatus weaponStatus = StatusPanel.WeaponStatusField?.GetValue(hud) as WeaponStatus;
            TextMeshProUGUI nameLabel = weaponStatus != null ? StatusPanel.NameTextField?.GetValue(weaponStatus) as TextMeshProUGUI : null;
            font = nameLabel != null ? nameLabel.font : null;
            fontMaterial = BoscaliSummer.Core.Game.VanillaHudStyle.Glow(nameLabel != null ? nameLabel.fontSharedMaterial : null);

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
            card = BuildTargetCard("TargetCard", "TARGET CAM");
            missile = BuildTargetCard("MissileCard", "MISSILE CAM");
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
            var boxObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)boxObject.transform;
            rect.SetParent(root, false);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // No plate and no shade (user ruling 2026-10-13): bare ink on the sky, like the board's own blocks.

            bool hasSub = caption != null;
            TextMeshProUGUI captionText = null, captionRight = null;
            if (hasSub)
            {
                captionText = Label(rect, "Caption", TextAlignmentOptions.TopLeft, HudType.Label,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(7f, -19f), new Vector2(-7f, -4f));
                captionText.characterSpacing = HudType.Tracking(HudStep.Label);
                captionText.text = caption;
                captionText.fontStyle = FontStyles.Bold;      // LABEL 12 bold Dim: the shelf caption
                captionRight = Label(rect, "CaptionRight", TextAlignmentOptions.TopRight, HudType.Label,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(7f, -19f), new Vector2(-7f, -4f));
            }

            // Caption owns the top 16 px; the value sits between it and the sub-line (mono faces run wide).
            TextMeshProUGUI value = Label(rect, "Value", TextAlignmentOptions.Center, HudType.Emphasis,
                new Vector2(0f, hasSub ? 0.3f : 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, hasSub ? -17f : 0f));
            value.fontStyle = FontStyles.Bold;      // EMPHASIS bold, always INK-HI

            TextMeshProUGUI sub = Label(rect, "SubLine", TextAlignmentOptions.Center, HudType.Label,
                Vector2.zero, new Vector2(1f, 0.34f), new Vector2(0f, 3f), Vector2.zero);
            sub.gameObject.SetActive(hasSub);      // LABEL regular Ink

            RectTransform track = null;
            Image bar = null;
            TextMeshProUGUI barLabel = null;
            if (withBar)
            {
                // The tick scale (PaintSymbols) is the track: a low reading still reads as "a little of a bar", not a stray dot.
                var trackObject = new GameObject(name + "Track", typeof(RectTransform));
                track = (RectTransform)trackObject.transform;
                track.SetParent(root, false);
                track.pivot = new Vector2(0.5f, 0.5f);

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
                barLabel = Label(track, "BarLabel", TextAlignmentOptions.Top, HudType.Label,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(-ThirdPersonHudLayout.BarLabelHalfWidth, -ThirdPersonHudLayout.BarLabelDrop), new Vector2(ThirdPersonHudLayout.BarLabelHalfWidth, -2f));
            }

            return new Box
            {
                Root = rect, Caption = captionText, CaptionRight = captionRight, Value = value, SubLine = sub,
                Track = track, Bar = bar, BarLabel = barLabel,
                LastValue = float.NaN, LastSub = float.NaN, LastCaption = float.NaN, LastRight = float.NaN, LastBar = float.NaN
            };
        }

        private TargetCardWidgets BuildTargetCard(string name, string shelfName)
        {
            const float header = ThirdPersonHudLayout.TargetCardHeaderHeight, strip = FeedCorner.Strip;
            var cardObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)cardObject.transform;
            rect.SetParent(root, false);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // Feed stack (2026-11-17 refit): no plate. The MSL, TGT and UAV cards share one chrome in the HUD's own ink (FeedCorner.Frame, drawn in PaintSymbols): a Dim caption shelf
            // over one rule across the picture's top, brackets round it, and a one-line data strip under it. The line art over the picture is drawn there too.

            var feedObject = new GameObject("Feed", typeof(RectTransform), typeof(RawImage));
            RectTransform feedRect = (RectTransform)feedObject.transform;
            feedRect.SetParent(rect, false);
            feedRect.anchorMin = new Vector2(0f, 0f);
            feedRect.anchorMax = new Vector2(1f, 1f);
            feedRect.offsetMin = new Vector2(0f, strip);
            feedRect.offsetMax = new Vector2(0f, -header);
            RawImage feed = feedObject.GetComponent<RawImage>();
            feed.raycastTarget = false;

            // On the picture: the missile's countdown (bottom left), SIGNAL LOST (bottom right). The target's name lives on the shelf.
            TextMeshProUGUI count = Label(feedRect, "Count", TextAlignmentOptions.BottomLeft, HudType.Emphasis,
                new Vector2(0f, 0f), new Vector2(0.6f, 0f), new Vector2(8f, 5f), new Vector2(0f, 32f));
            count.fontStyle = FontStyles.Bold;
            TextMeshProUGUI note = Label(feedRect, "Note", TextAlignmentOptions.BottomRight, HudType.Label,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(6f, 5f), new Vector2(-8f, 21f));

            // The shelf: the Dim caption at the left, the state word at the right end (the rule under them is FeedCorner.Frame's).
            TextMeshProUGUI title = HudText.Make(rect, "Name", font, fontMaterial, TextAlignmentOptions.MidlineLeft, 12f);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = title.rectTransform.pivot = new Vector2(0f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(FeedCorner.ShelfText, 0f);
            title.rectTransform.sizeDelta = new Vector2(150f, header - 3f);
            title.text = shelfName;
            title.fontStyle = FontStyles.Bold;      // the shelf words hold up over a bright cloud deck

            TextMeshProUGUI state = HudText.Make(rect, "State", font, fontMaterial, TextAlignmentOptions.MidlineRight, 12f);
            state.rectTransform.anchorMin = state.rectTransform.anchorMax = state.rectTransform.pivot = new Vector2(1f, 1f);
            state.rectTransform.anchoredPosition = new Vector2(-FeedCorner.StateInset, 0f);
            state.rectTransform.sizeDelta = new Vector2(170f, header - 3f);
            state.fontStyle = FontStyles.Bold;

            // The data strip: three cells, left / centre / right, label dim and value full (FeedCorner.CellHead).
            var cells = new TextMeshProUGUI[3];
            var keys = new int[3];
            for (int i = 0; i < 3; i++)
            {
                cells[i] = Label(rect, "Cell" + i, i == 0 ? TextAlignmentOptions.MidlineLeft : i == 1 ? TextAlignmentOptions.Midline : TextAlignmentOptions.MidlineRight, 12f,
                    new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(3f, 0f), new Vector2(-3f, strip));
                keys[i] = int.MinValue + 1;
            }

            cardObject.SetActive(false);
            return new TargetCardWidgets
            {
                Root = rect, Name = title, State = state, Count = count, Note = note, Cells = cells, CellKey = keys,
                Feed = feed, Active = false, StripPt = HudType.Label, LastCount = -2, LastPicW = -1f, LastPicH = -1f
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
            // The missile card and the target card show without the flight cluster too (the cockpit): the root is up for any of them, the boxes and their bars only for the cluster.
            bool shown = active || missileFeed != null || cameraTexture != null;
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
            Color ink = VanillaHudStyle.Ink;
            Color caution = theme.Warning;

            Color? spdLit = null, altLit = null;
            Color fuelColor = ink;
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
                fuelColor = fuelTone == HudTone.Info ? ink : caution;      // amber from caution up: the third-person cluster never turns red
                spdLit = overG ? caution : (Color?)null;
                altLit = terrain == TerrainCue.PullUp || terrain == TerrainCue.Low ? caution : (Color?)null;
                Style(spd, ink, spdLit);
                Style(alt, ink, altLit);
                Style(hdg, ink, null);
                Fill(ref spd, Mathf.Clamp01(f.Throttle), ink, "THR ");
                Fill(ref alt, Mathf.Clamp01(f.Fuel), fuelColor, FuelText.Legend(f.FuelBingo, f.FuelJoker, fuelTone) + " ");

                WriteSpeedCaption(overG);
                WriteSpeed(f.SpeedMps, units);
                WriteAltitude(f.AltitudeM, units);
                WriteAltCaption(f.RadarAltM, terrain, units);
                WriteHeading(f.HeadingDeg);
                WriteMachG(f.Mach, f.G);
                WriteAoa(overG ? float.NaN : f.AoaDeg);   // the 106 px caption row holds OVER-G or the AoA beside SPD, not both
                WriteClimb(f.ClimbMps, units);
            }
            // The target card stands in the left column (TARGET CAM zone); the missile card in the bottom-right feed column, on the UAV card (VANGUARD's, FeedCorner) when one is up.
            RectF uav = UavCard(screenW, screenH);
            RectF targetRect = ThirdPersonHudLayout.TargetCard(screenW, screenH, true);
            RectF missileRect = ThirdPersonHudLayout.MissileCard(screenW, screenH, uav, avoid,
                active ? ThirdPersonHudLayout.AltitudeBlock(screenW, screenH, avoid) : default);
            // The cards' words first (their widths set where the shelf rules stop), then the mesh over them.
            PresentTargetCard(cameraTexture, cameraCode, rangeM, closureMps, targetRect, ink, units);
            PresentMissileCard(missileRect, ink, units);
            PaintSymbols(screenW, screenH, ink, cameraTexture != null ? targetRect : default, f.BankDeg, f.SlipDeg, active, missileFeed != null ? missileRect : default);
        }

        /// <summary>The UAV card's box (<see cref="FeedCorner.Uav"/>, screen pixels on its own canvas) in the layout's units; no size while it is down.</summary>
        private static RectF UavCard(float screenW, float screenH)
        {
            Rect px = FeedCorner.Uav;
            if (px.width <= 0f || px.height <= 0f || Screen.width <= 0 || Screen.height <= 0) return default;
            float kx = screenW / Screen.width, ky = screenH / Screen.height;
            return new RectF((px.center.x - Screen.width * 0.5f) * kx, (px.center.y - Screen.height * 0.5f) * ky, px.width * kx, px.height * ky);
        }


        /// <summary>The value is always INK-HI and the sub-line and the caption's right end Ink; the caption is Dim, or <paramref name="lit"/> (amber, steady) while a cue holds: state goes on the caption only.</summary>
        private static void Style(Box box, Color ink, Color? lit)
        {
            box.Value.color = VanillaHudStyle.InkHi;
            if (box.Caption != null) box.Caption.color = lit ?? VanillaHudStyle.InkDim;
            if (box.CaptionRight != null) box.CaptionRight.color = ink;
            box.SubLine.color = ink;
        }

        private void Fill(ref Box box, float amount, Color color, string prefix)
        {
            if (box.Bar.fillAmount != amount) box.Bar.fillAmount = amount;
            box.Bar.color = color;
            box.BarLabel.color = color;
            // Percent plus which prefix is showing (BINGO vs FUEL), as one change key.
            float key = Mathf.Round(amount * 100f) + (prefix.Length * 1000f);
            if (box.LastBar == key) return;
            int len = AvNumFormat.Append(buf, 0, prefix);
            len = AvNumFormat.Write(buf, len, Mathf.Round(amount * 100f), 0);
            box.BarLabel.text = new string(buf, 0, len);
            box.LastBar = key;
        }

        /// <summary>A Dim rule under the SPD and ALT captions, Faint tracks for the bars, the cards' chrome and the line art over their pictures.
        /// Rebuilt only when the layout changes.</summary>
        /// <param name="targetCard">The target card's box, no size while it is down; <paramref name="missileCard"/> the same for the missile card.</param>
        private void PaintSymbols(float screenW, float screenH, Color ink, RectF targetCard, float bankDeg, float slipDeg, bool flight, RectF missileCard)
        {
            // Bank in 2 degree steps and slip in whole degrees: finer motion is not visible and would rebuild the mesh every tick in a turn.
            int key = (int)screenW * 7919 + (int)screenH * 31 + (int)(lastScale * 100f) * 977 + Code(ink) * 17 +
                Mathf.RoundToInt(targetCard.CenterY + targetCard.Height) * 11 + (flight ? 19 : 0) +
                Mathf.RoundToInt(missileCard.CenterX * 3f + missileCard.CenterY + missileCard.Height) * 23 + Mathf.RoundToInt(bankDeg / 2f) * 104729 + Mathf.RoundToInt(slipDeg) * 7 +
                Mathf.RoundToInt(avoid.CenterX) * 15485863 + Mathf.RoundToInt(avoid.CenterY) * 32452843 + Mathf.RoundToInt(avoid.Width) * 49979687 + Mathf.RoundToInt(avoid.Height) * 67867967;
            key += (card.Locked ? 7 : 0) * 104723;      // whether the lock bracket is drawn
            if (key == lastSymbols) return;
            lastSymbols = key;

            AvQuadBuffer b = symbols.Buffer;
            b.Clear();
            if (flight)
            {
                RectF spdRect = ThirdPersonHudLayout.SpeedBox(screenW, screenH, avoid);
                RectF altRect = ThirdPersonHudLayout.AltitudeBox(screenW, screenH, avoid);
                CaptionRule(b, spdRect, ink.ToRgba());
                CaptionRule(b, altRect, ink.ToRgba());
                Scale(b, ThirdPersonHudLayout.SideBar(spdRect, outwardIsLeft: true), ink.ToRgba());
                Scale(b, ThirdPersonHudLayout.SideBar(altRect, outwardIsLeft: false), ink.ToRgba());
                BankAndSlip(b, ThirdPersonHudLayout.HeadingBox(screenW, screenH, avoid), bankDeg, slipDeg, ink.ToRgba());
            }
            if (targetCard.Height > 0f)
            {
                FeedCorner.Frame(b, targetCard.Left, targetCard.Bottom, targetCard.Right, targetCard.Top, ink.ToRgba());
                TargetArt(b, targetCard, card.Locked, ink.ToRgba(), VanillaHudStyle.InkHi.ToRgba());
            }
            if (missileCard.Height > 0f)
            {
                FeedCorner.Frame(b, missileCard.Left, missileCard.Bottom, missileCard.Right, missileCard.Top, ink.ToRgba());
                SeekerArt(b, missileCard, ink.ToRgba());
            }
            BufferGuard.Check(b, "third-person cluster");
            symbols.Commit();
        }

        /// <summary>What the TARGET CAM draws over its picture: range-crosshair ticks, and while a target is locked a Hi lock bracket round it (a selection: red means they lock us; the pod keeps the target centred). The text on the picture is the game's own in the cockpit.</summary>
        private static void TargetArt(AvQuadBuffer b, RectF r, bool locked, Rgba ink, Rgba hi)
        {
            float pb = FeedCorner.PictureBottom(r.Bottom), pt = r.Top - ThirdPersonHudLayout.TargetCardHeaderHeight, h = pt - pb, cx = r.CenterX, cy = (pb + pt) * 0.5f;
            Rgba tick = ink.WithAlpha(HudInk.LineMid);
            AvStrokes.Line(b, cx, pt - 6f, cx, pt - 16f, HudStroke.Hairline, tick);
            AvStrokes.Line(b, cx, pb + 6f, cx, pb + 16f, HudStroke.Hairline, tick);
            float side = r.Width * 0.2f;
            AvStrokes.Line(b, r.Left + side, cy, r.Left + side + 10f, cy, HudStroke.Hairline, tick);
            AvStrokes.Line(b, r.Right - side, cy, r.Right - side - 10f, cy, HudStroke.Hairline, tick);
            if (!locked) return;
            float bw = h * 0.8f, bh = h * 0.6f;
            AvStrokes.Bracket(b, cx - bw * 0.5f, cy - bh * 0.5f, bw, bh, 10f, HudStroke.Standard, hi);
        }

        /// <summary>What the MISSILE CAM draws over its picture: the seeker reticle, a dashed circle with a cross of ticks and a centre dot (the countdown is text, bottom left).</summary>
        private static void SeekerArt(AvQuadBuffer b, RectF r, Rgba ink)
        {
            float pb = FeedCorner.PictureBottom(r.Bottom), pt = r.Top - ThirdPersonHudLayout.TargetCardHeaderHeight, rad = (pt - pb) * 0.34f, cx = r.CenterX, cy = (pb + pt) * 0.5f;
            AvStrokes.DashedRing(b, cx, cy, rad, 28, 0.55f, 2f, ink);
            Rgba tick = ink;
            AvStrokes.Line(b, cx, cy + rad - 10f, cx, cy + rad + 6f, HudStroke.Hairline, tick);
            AvStrokes.Line(b, cx, cy - rad + 10f, cx, cy - rad - 6f, HudStroke.Hairline, tick);
            AvStrokes.Line(b, cx - rad + 10f, cy, cx - rad - 6f, cy, HudStroke.Hairline, tick);
            AvStrokes.Line(b, cx + rad - 10f, cy, cx + rad + 6f, cy, HudStroke.Hairline, tick);
            AvStrokes.Fill(b, cx - 1.5f, cy - 1.5f, 3f, 3f, ink);
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

        /// <summary>The one Dim hairline under a box's caption, from the box's left + 7 to its right - 7: it replaces the corner brackets.</summary>
        private static void CaptionRule(AvQuadBuffer b, RectF r, Rgba c) =>
            AvStrokes.Line(b, r.Left + 7f, r.Top - 19.5f, r.Right - 7f, r.Top - 19.5f, HudStroke.Hairline, c.WithAlpha(HudInk.LineMid));

        /// <summary>A vertical bar: a 3 wide Faint track plus five ticks; the live fill is the bar image over it (3 wide, in its tone).</summary>
        private static void Scale(AvQuadBuffer b, RectF r, Rgba c)
        {
            Rgba dim = c.WithAlpha(HudInk.LineLow);
            AvStrokes.Fill(b, r.CenterX - 1.5f, r.Bottom, 3f, r.Height, dim);
            for (int i = 0; i <= 4; i++)
            {
                float y = r.Bottom + r.Height * i / 4f;
                float arm = i == 0 || i == 4 ? 5f : 3f;
                AvStrokes.Line(b, r.CenterX - arm, y, r.CenterX + arm, y, 1f, dim);
            }
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
            RectF rect, Color ink, AvUnits units)
        {
            // Locked = the board read a target (a range): its name and "· LOCK" on the shelf (the one place the name appears), the lock bracket on the picture. The state word is rebuilt only when the code or the lock changed.
            bool locked = float.IsFinite(rangeM);
            if (card.StateText == null || !ReferenceEquals(card.LastCode, code) || card.Locked != locked)
            {
                card.StateText = code == null ? "--" : locked ? code + " · LOCK" : code;
                card.LastCode = code;
                card.Locked = locked;
            }
            if (!PresentCard(ref card, rect, cameraTexture, card.StateText, ink, locked ? VanillaHudStyle.InkHi : VanillaHudStyle.InkDim)) return;

            // RNG, CLS (closing positive), ASP (the target's aspect: 0 tail-on, 180 head-on, R or L of its tail): a cell with no truthful value is blank.
            if (Changed(ref card, 0, locked ? Mathf.RoundToInt(rangeM / 100f) : int.MinValue))
                Cell(ref card, 0, locked ? AvUnitTable.DistanceReading(buf, CellStart("RNG"), rangeM, units) : 0);
            bool closing = float.IsFinite(closureMps);
            if (Changed(ref card, 1, closing ? Mathf.RoundToInt(AvUnitTable.Speed(closureMps, units) / 5f) : int.MinValue))
            {
                int len = 0;
                if (closing)
                {
                    len = CellStart("CLS");
                    buf[len++] = closureMps >= 0f ? '+' : '-';
                    len = AvUnitTable.SpeedReading(buf, len, Mathf.Abs(closureMps), units);
                }
                Cell(ref card, 1, len);
            }
            bool aspect = locked && float.IsFinite(CardAspectDeg);
            if (Changed(ref card, 2, aspect ? Mathf.RoundToInt(CardAspectDeg) : int.MinValue))
            {
                int len = 0;
                if (aspect)
                {
                    int a = Mathf.RoundToInt(CardAspectDeg), mag = Mathf.Abs(a);
                    len = CellStart("ASP");
                    len = AvNumFormat.Write(buf, len, mag, 0);
                    if (mag != 0 && mag != 180) buf[len++] = a > 0 ? 'R' : 'L';
                }
                Cell(ref card, 2, len);
            }
        }

        /// <summary>The missile feed card: the weapon's name on the shelf (ink, lost or not), the countdown on the picture, RNG at the left and CLS at the right under it, SIGNAL LOST (the one alert word) over the held last picture.</summary>
        private void PresentMissileCard(RectF rect, Color ink, AvUnits units)
        {
            Color alert = ThemeManager.Active.ColorTheme.Alert;
            if (!PresentCard(ref missile, rect, missileFeed, missileName, ink, ink)) return;
            string note = missileLost ? "SIGNAL LOST" : "";
            if (missile.LastNote != note)
            {
                missile.Note.text = note;
                missile.LastNote = note;
            }
            missile.Note.color = alert;

            // The countdown: time to impact, big on the picture, "T-06S"; nothing while the weapon is not closing (or lost).
            bool tti = !missileLost && float.IsFinite(missileTtiS);
            int seconds = tti ? Mathf.Clamp(Mathf.CeilToInt(missileTtiS), 0, 999) : -1;
            if (missile.LastCount != seconds)
            {
                int len = 0;
                if (tti)
                {
                    len = AvNumFormat.Append(buf, 0, "T-");
                    if (seconds < 10) buf[len++] = '0';
                    len = AvNumFormat.Write(buf, len, seconds, 0);
                    buf[len++] = 'S';
                }
                missile.Count.text = len > 0 ? new string(buf, 0, len) : "";
                missile.LastCount = seconds;
            }
            missile.Count.color = ink;

            bool ranged = float.IsFinite(missileRangeM);
            if (Changed(ref missile, 0, ranged ? Mathf.RoundToInt(missileRangeM / 100f) : int.MinValue))
                Cell(ref missile, 0, ranged ? AvUnitTable.DistanceReading(buf, CellStart("RNG"), missileRangeM, units) : 0);
            bool closing = float.IsFinite(missileClosureMps);
            if (Changed(ref missile, 2, closing ? Mathf.RoundToInt(AvUnitTable.Speed(missileClosureMps, units) / 5f) : int.MinValue))
            {
                int len = 0;
                if (closing)
                {
                    len = CellStart("CLS");
                    buf[len++] = missileClosureMps >= 0f ? '+' : '-';
                    len = AvUnitTable.SpeedReading(buf, len, Mathf.Abs(missileClosureMps), units);
                }
                Cell(ref missile, 2, len);
            }
        }

        /// <summary>True (and remembers it) when strip cell <paramref name="i"/>'s change key moved: the cell's text is then rewritten, not before.</summary>
        private static bool Changed(ref TargetCardWidgets c, int i, int key)
        {
            if (c.CellKey[i] == key) return false;
            c.CellKey[i] = key;
            return true;
        }

        /// <summary>Starts a strip cell's text in <see cref="buf"/>: the dim label and a gap. Returns the length so far.</summary>
        private int CellStart(string label)
        {
            int len = AvNumFormat.Append(buf, 0, FeedCorner.CellHead);
            len = AvNumFormat.Append(buf, len, label);
            return AvNumFormat.Append(buf, len, FeedCorner.CellTail);
        }

        /// <summary>Writes cell <paramref name="i"/> from the first <paramref name="len"/> characters of <see cref="buf"/> (0 = blank). The label alone is no value: a cell is blank, never a label over nothing.</summary>
        private void Cell(ref TargetCardWidgets c, int i, int len) =>
            c.Cells[i].text = len > 0 ? new string(buf, 0, len) : "";

        /// <summary>What every feed card shares: shown while there is a picture, placed, its feed fitted, the shelf's words written on change (the caption InkDim, the state in <paramref name="tone"/>), the strip's size and colour kept. False while hidden.</summary>
        private bool PresentCard(ref TargetCardWidgets c, RectF rect, Texture feed, string state, Color ink, Color tone)
        {
            bool show = feed != null;
            if (c.Active != show)
            {
                c.Root.gameObject.SetActive(show);
                c.Active = show;
            }
            if (!show) return false;

            PositionRect(c.Root, rect);
            if (!ReferenceEquals(c.Feed.texture, feed)) c.Feed.texture = feed;
            FitPicture(ref c, rect, feed);

            c.Name.color = VanillaHudStyle.InkDim;
            state ??= "--";
            if (c.LastState != state)
            {
                c.State.text = state;
                c.LastState = state;
            }
            c.State.color = tone;

            float pt = FeedCorner.StripPt(rect.Width);
            if (pt != c.StripPt || c.Cells[0].fontSize != pt)
            {
                c.StripPt = pt;
                for (int i = 0; i < c.Cells.Length; i++) c.Cells[i].fontSize = pt;
            }
            for (int i = 0; i < c.Cells.Length; i++) c.Cells[i].color = ink;
            return true;
        }

        /// <summary>The picture keeps its texture's aspect: wider than the texture crops the top and bottom, narrower crops the sides (centred). Recomputed only when the size or the texture changed.</summary>
        private static void FitPicture(ref TargetCardWidgets c, RectF rect, Texture feed)
        {
            float w = rect.Width, h = rect.Height - ThirdPersonHudLayout.TargetCardHeaderHeight - FeedCorner.Strip;
            float ta = feed.height > 0 ? (float)feed.width / feed.height : 16f / 9f;
            if (w == c.LastPicW && h == c.LastPicH && ta == c.LastTexAspect) return;
            c.LastPicW = w;
            c.LastPicH = h;
            c.LastTexAspect = ta;
            float pa = h > 1f ? w / h : ta;
            c.Feed.uvRect = pa > ta ? new Rect(0f, (1f - ta / pa) * 0.5f, 1f, ta / pa) : new Rect((1f - pa / ta) * 0.5f, 0f, pa / ta, 1f);
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
