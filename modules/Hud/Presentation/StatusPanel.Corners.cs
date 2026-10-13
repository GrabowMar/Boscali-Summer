using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The two other screen corners in the board's look (LAYOUT.md 7; the option Corners): the kill feed at the top-left ("DL EVT LOG", StatusPanel.EventLog.cs: the mod's own event log drawn in place of the game's feed text, HUD holo
    // pass D) and the minimised map at the bottom-left ("TAC MAP"). The feed's own text and its dark plate are hidden (NativeScreenCorners, through groups of ours) while the log and the board's shade stand in for them, and given
    // back the same frame the log is not in charge (the chat input is open, the capture is not seeing the game's lines, the option is off); the map is untouched. The mod's own objects are three roots under the HUD canvas: the
    // shade (the sun band and the cloud, first in the canvas so it lies behind every native mark; the feed's text is on the chat canvas, which draws above the HUD's), the furniture (the map's brackets and caption shelf on one surface,
    // over the map) and the log (its own root: it follows the pilot's Opacity). They are placed from the native rectangles (world corners into canvas units) whenever those change, and at every 10 Hz present; the log is also
    // drawn again between ticks whenever its signature moves (a row wiping in or fading). The shade and the map's furniture are not scaled and not dimmed by the pilot's options (no scale, no opacity), but everything sways with the board and follows the Shade and the declutter level (no furniture at Lean and Minimal). The minimised map's dressing is hidden whole while HudMapState.HoloActive is set (a map hologram has the corner), and its caption shelf stands inside the frame (HUD holo pass C); the NAV DATA block of the bottom-right corner has its own two roots and its own file (StatusPanel.Nav.cs), driven from CornersFrame, and so is the CUE stack at the left edge (StatusPanel.Cues.cs: the game's grey message boxes redrawn, its own two roots, also stood down with the Corners option).
    internal sealed partial class StatusPanel
    {
        private const int CornerQuads = 120;

        private readonly NativeScreenCorners screenCorners = new NativeScreenCorners();
        private RectTransform cornerShade, cornerFurniture, cornerSurface;
        private Image feedBand;
        private RawImage feedCloud;
        private Texture2D feedTexture;
        private byte[] feedBytes;
        private BoardGeometry feedFor;
        private Vector2 feedBandBase;
        private AvVector cornerVector;
        private ScopeText cornerText;
        private ScopeCanvas cornerCanvas;
        /// <summary>The map's furniture as last laid out (design units from the canvas's top-left; what the offline check reads).</summary>
        private CornerFurniture mapFurniture;
        /// <summary>The last paint put DL EVT LOG in charge of the feed corner (the game's plate and text are hidden while this holds).</summary>
        private bool feedLive;
        private bool seenFeedUp, seenMapUp;
        /// <summary>Where the minimised map's north is on its frame as last laid out (the offline check reads it).</summary>
        private CornerLayout.MapNorth mapNorth;
        private Vector2 seenFeedSize, seenMapSize;
        private Vector3 seenFeedAt, seenMapAt;
        /// <summary>The board's gate is shut (a pause, the maximised map, cinematic mode): set every frame by <see cref="TickNatives"/>.</summary>
        private bool cornersSuppressed;
        /// <summary>How many times the native objects were looked for again (<see cref="FindCorners"/> starts it at 0; a stand-in render never looks).</summary>
        private int findTries = int.MaxValue;
        private float findAt;

        /// <summary>Find the native feed and map (production), and look again a few times if they were not there yet.</summary>
        private void FindCorners()
        {
            findTries = 0;
            screenCorners.Find();
        }

        /// <summary>The native pieces the corners are made from are parameters, so an offline render can stand in for them: the feed's plate (its parent is the feed's box), the minimised map's plate, the map picture the game turns by the heading, the feed's text objects and the chat input's object.</summary>
        private void AdoptCorners(Image feedPlate, RectTransform mapPlate, Transform mapImage, TMP_Text[] feedTexts, GameObject chat)
        {
            findTries = int.MaxValue;
            screenCorners.Adopt(feedPlate, mapPlate, mapImage, feedTexts, chat);
        }

        /// <summary>
        /// Every frame (<paramref name="tick"/> false: the board's <see cref="TickNatives"/>) and at the end of every present (<paramref name="tick"/> true): lay the corners out again if a native rectangle moved, grew or
        /// came and went, always on a tick; stand them down, and give the feed's plate back, whenever the option is off or the board is stale (its update stopped) or gated; the board's own master switch does not stand them down. Fail open: the plate comes back the very frame the board's shade is not standing in for it.
        /// </summary>
        private void CornersFrame(bool alive, bool tick)
        {
            NavFrame(alive, tick);
            if (!(alive && Options.Corners && !cornersSuppressed && canvasRect != null)) { StandDownCorners(); return; }
            CueFrame(tick);
            if (!screenCorners.Complete && findTries < 6 && Time.unscaledTime >= findAt)
            {
                findAt = Time.unscaledTime + 2f;
                findTries++;
                screenCorners.Find();
            }
            // The map's dressing gives the corner up to a map hologram (HudMapState.HoloActive, set by whoever draws it): hidden whole while it is up.
            bool feedUp = screenCorners.FeedUp, mapUp = screenCorners.MapUp && !HudMapState.HoloActive;
            Vector2 feedSize = feedUp ? screenCorners.FeedBox.rect.size : default, mapSize = mapUp ? screenCorners.MapPlate.rect.size : default;
            Vector3 feedAt = feedUp ? screenCorners.FeedBox.position : default, mapAt = mapUp ? screenCorners.MapPlate.position : default;
            // The log's state is looked at every frame: a row wiping in or fading, the chat input opening, the capture going quiet are all noticed at once, not at the next tick.
            bool charge = LogInCharge(feedUp);
            int signature = charge ? eventLog.Signature(LogClock(), Options.Declutter) : 0;
            bool moved = feedUp != seenFeedUp || mapUp != seenMapUp || feedSize != seenFeedSize || mapSize != seenMapSize || feedAt != seenFeedAt || mapAt != seenMapAt || charge != logCharge || signature != logSig;
            if (tick || moved)
            {
                seenFeedUp = feedUp; seenMapUp = mapUp; seenFeedSize = feedSize; seenMapSize = mapSize; seenFeedAt = feedAt; seenMapAt = mapAt;
                logCharge = charge; logSig = signature;
                // A paint that throws leaves the game's feed alone (fail open): everything of ours stands down, so the plate and text come back this frame, and the failure is reported once.
                try { PaintCorners(feedUp, mapUp); }
                catch (System.Exception e) { StandDownCorners(); BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Hud.Corners", e); }
            }
            // The game's plate and text are hidden only while our own log is up in the HUD: a hidden HUD canvas takes it with it and gives them back.
            bool hide = feedLive && logRoot != null && logRoot.gameObject.activeInHierarchy;
            screenCorners.HidePlate(hide);
            screenCorners.HideText(hide);
        }

        /// <summary>Corners off for now: nothing of ours shows and the feed's plate and text are the game's again.</summary>
        private void StandDownCorners()
        {
            screenCorners.HidePlate(false);
            screenCorners.HideText(false);
            if (cornerShade != null) Show(cornerShade, false);
            if (cornerFurniture != null) Show(cornerFurniture, false);
            StandDownLog();
            StandDownCues();
            feedLive = seenFeedUp = seenMapUp = false;
        }

        private bool EnsureCornerTree()
        {
            if (cornerFurniture != null) return true;
            if (canvasRect == null) return false;
            cornerShade = CornerRoot("BoscaliCornerShade");
            cornerShade.SetAsFirstSibling();
            feedBand = ShadeChild<Image>(cornerShade, "FeedBand");
            feedBand.sprite = HudSprites.SunBand(0f);
            feedBand.type = Image.Type.Sliced;
            feedCloud = ShadeChild<RawImage>(cornerShade, "FeedCloud");
            cornerFurniture = CornerRoot("BoscaliCornerFurniture");
            cornerSurface = Pinned(cornerFurniture, "Corners", false);
            cornerVector = AvVector.Create(cornerSurface, "Strokes", CornerQuads);
            cornerText = new ScopeText(Holder(cornerSurface, "Words"), font, fontMaterial);
            cornerCanvas = new ScopeCanvas(cornerVector.Buffer, cornerText, "scope.corners");
            PlaceCornerRoots();
            return true;
        }

        /// <summary>A root as big as the HUD canvas (every child is placed from its top-left corner), off until the first paint.</summary>
        private RectTransform CornerRoot(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.SetActive(false);
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvasRect, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>The roots follow the board's sway (<see cref="swayShift"/>), and the band stays hung from the top of the screen.</summary>
        private void PlaceCornerRoots()
        {
            if (navFurniture != null) navShade.offsetMin = navShade.offsetMax = navFurniture.offsetMin = navFurniture.offsetMax = swayShift;
            if (logRoot != null) logRoot.offsetMin = logRoot.offsetMax = swayShift;
            if (cueRoot != null) cueShade.offsetMin = cueShade.offsetMax = cueRoot.offsetMin = cueRoot.offsetMax = swayShift;
            if (cornerFurniture == null) return;
            cornerShade.offsetMin = cornerShade.offsetMax = swayShift;
            cornerFurniture.offsetMin = cornerFurniture.offsetMax = swayShift;
            feedBand.rectTransform.anchoredPosition = feedBandBase - new Vector2(0f, swayShift.y);
        }

        /// <summary>A native rectangle in canvas units from the canvas's top-left corner (x right, y down), through its world corners.</summary>
        private bool CanvasBox(RectTransform native, out float l, out float t, out float r, out float b)
        {
            native.GetWorldCorners(corners);
            Vector3 bottomLeft = canvasRect.InverseTransformPoint(corners[0]), topRight = canvasRect.InverseTransformPoint(corners[2]);
            Rect c = canvasRect.rect;
            l = bottomLeft.x - c.xMin;
            r = topRight.x - c.xMin;
            t = c.yMax - topRight.y;
            b = c.yMax - bottomLeft.y;
            return r - l > 4f && b - t > 4f;
        }

        /// <summary>
        /// Lay out and draw: DL EVT LOG over the feed's box when it is in charge of the corner (<see cref="PaintEventLog"/>), its shade (the sun band over the top-left and one cloud behind the block, baked when its size changes)
        /// and the map's furniture, from the native rectangles. The feed's plate and text are hidden by the caller while the log is drawn here.
        /// </summary>
        private void PaintCorners(bool feedUp, bool mapUp)
        {
            if (!(feedUp || mapUp))
            {
                feedLive = false;
                if (cornerShade != null) { Show(cornerShade, false); Show(cornerFurniture, false); }
                if (logRoot != null) Show(logRoot, false);
                return;
            }
            if (!EnsureCornerTree()) return;
            float k = 1f / baseUnit, canvasW = canvasRect.rect.width, canvasH = canvasRect.rect.height, l = 0f, t = 0f, r = 0f, b = 0f, ml = 0f, mt = 0f, mr = 0f, mb = 0f;
            bool feed = feedUp && CanvasBox(screenCorners.FeedBox, out l, out t, out r, out b);
            bool map = mapUp && CanvasBox(screenCorners.MapPlate, out ml, out mt, out mr, out mb);
            mapFurniture = map ? CornerLayout.Map(ml, mt, mr, mb, canvasH, k) : default;
            mapNorth = map ? CornerLayout.North(mapFurniture, screenCorners.MapTurnDeg) : default;

            // The feed corner: the log, when it is in charge of it (else the game's feed is as the game made it and nothing of the log is up).
            feedLive = feed && LogInCharge(true) && PaintEventLog(l * k, t * k);
            if (!feedLive && logRoot != null) Show(logRoot, false);
            float strength = Mathf.Clamp01(Options.Shade);
            bool shade = feedLive && logPlan.Present && strength > 0.001f;
            Show(cornerShade, shade);
            if (shade) PaintFeedShade(strength * logLit, logPlan.Left, logPlan.Top, logPlan.Width, logPlan.Height);

            Show(cornerFurniture, map);
            PlaceOnGrid(cornerSurface, 0f, 0f, Whole(canvasW), Whole(canvasH));
            ScopeCanvas g = cornerCanvas;
            g.Begin(Palette(), baseUnit, Whole(canvasH), pixel);      // also with no map: the surface is cleared, not left holding the frame it last drew
            if (map && allow.Furniture) DrawMapFurniture(g, mapFurniture, mapNorth);
            g.End();
            cornerVector.Commit();
        }

        /// <summary>
        /// The feed's shade (LAYOUT.md 2): the band, 760 x 190 from the top-left of the screen with its right end ramping out over the last 280 (the left ramp is off the screen), and ONE cloud behind the log's block (the mock's
        /// 0, 8, 480 x 98 for the four lines of a box 475 x 90): the rectangle from the block's top + 8, as wide and 8 taller than the block, grown to 32 x 16 units so a new row does not bake the field again every time.
        /// <paramref name="strength"/> is the pilot's Shade times how lit the log is (its brightest row's fade), so the shade goes out with the last row.
        /// </summary>
        private void PaintFeedShade(float strength, float x, float y, float w, float h)
        {
            PlaceBoxAt(feedBand.rectTransform, new BoardRect(-ScopeLayout.BandRamp, 0f, 760f + ScopeLayout.BandRamp, ScopeLayout.BandHeight), baseUnit);
            feedBandBase = feedBand.rectTransform.anchoredPosition;
            feedBand.rectTransform.anchoredPosition = feedBandBase - new Vector2(0f, swayShift.y);
            float ramp = HudSprites.BandRamp / (ScopeLayout.BandRamp * baseUnit);
            if (feedBand.pixelsPerUnitMultiplier != ramp) feedBand.pixelsPerUnitMultiplier = ramp;
            var band = new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, ScopeLayout.BandAlpha * strength);
            if (feedBand.color != band) feedBand.color = band;
            Show(feedBand, true);

            var geo = new BoardGeometry { Hero = new BoardRect(x, y + 8f, Mathf.Ceil(w / 32f) * 32f, Mathf.Ceil((h + 8f) / 16f) * 16f) };
            BoardRect area = ShadeField.Area(geo);
            if (feedTexture == null || !ShadeField.SameBlocks(feedFor, geo))
            {
                BakeField(geo, area, ref feedTexture, feedCloud, ref feedBytes);
                feedFor = geo;
            }
            if (feedCloud.texture != feedTexture) feedCloud.texture = feedTexture;      // a rebuilt tree shows the texture that outlived the old one
            PlaceBoxAt(feedCloud.rectTransform, area, baseUnit);
            var cloud = new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, ScopeLayout.CloudAlpha * strength);
            if (feedCloud.color != cloud) feedCloud.color = cloud;
            Show(feedCloud, true);
        }

        /// <summary>TAC MAP: four DIM brackets round the plate, the caption shelf inside their lower left, and a small north tick with its "N" where the turning map's north meets the frame.</summary>
        private static void DrawMapFurniture(ScopeCanvas g, in CornerFurniture f, in CornerLayout.MapNorth north)
        {
            if (f.Present) g.Brackets(f.X0, f.Y0, f.X1 - f.X0, f.Y1 - f.Y0, f.Arm, Dim);
            g.CaptionShelf(f.ShelfX, f.ShelfY, f.ShelfW, "TAC MAP");
            if (!north.Present) return;
            g.Line(north.X, north.Y, north.InX, north.InY, Dim);
            g.Text("N", north.LabelX, north.LabelY, Ink, 12f, ScopeAlign.Center, true);
        }

        private void DestroyCorners()
        {
            DestroyNav();
            DestroyLog();
            DestroyCues();
            screenCorners.Dispose();
            if (cornerShade != null) UnityEngine.Object.Destroy(cornerShade.gameObject);
            if (cornerFurniture != null) UnityEngine.Object.Destroy(cornerFurniture.gameObject);
            if (feedTexture != null) UnityEngine.Object.Destroy(feedTexture);
            cornerShade = cornerFurniture = cornerSurface = null;
            feedBand = null;
            feedCloud = null;
            feedTexture = null;
            feedBytes = null;
            feedFor = default;
            cornerVector = null;
            cornerText = null;
            cornerCanvas = null;
            feedLive = seenFeedUp = seenMapUp = false;
            mapFurniture = default;
            mapNorth = default;
        }
    }
}
