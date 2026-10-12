using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The two other screen corners in the board's look (LAYOUT.md 7; the option Corners): the kill feed at the top-left ("DL EVT LOG") and the minimised map at the bottom-left ("TAC MAP"). Nothing native is replaced:
    // the feed's own text stays as the game draws it, and its dark plate is hidden (NativeScreenCorners, through a group of ours) while the board's shade stands in for it; the map is untouched. The mod's own objects
    // are two roots under the HUD canvas: the shade (the sun band and the cloud, first in the canvas so it lies behind every native mark; the feed's text is on the chat canvas, which draws above the HUD's) and the
    // furniture (brackets and caption shelves on one surface, last in the canvas so it lies over the map). They are placed from the native rectangles (world corners into canvas units) whenever those change, and at
    // every 10 Hz present. Not scaled and not dimmed by the pilot's options (no scale, no opacity), but they sway with the board and follow the Shade and the declutter level (no furniture at Lean and Minimal).
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
        /// <summary>The furniture as last laid out (design units from the canvas's top-left; what the offline check reads).</summary>
        private CornerFurniture feedFurniture, mapFurniture;
        /// <summary>The last paint drew the feed's shade and furniture (its plate is hidden while this holds).</summary>
        private bool feedLive;
        private bool seenFeedUp, seenMapUp;
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

        /// <summary>The native pieces the corners are made from are parameters, so an offline render can stand in for them: the feed's plate (its parent is the feed's box) and the minimised map's plate.</summary>
        private void AdoptCorners(Image feedPlate, RectTransform mapPlate)
        {
            findTries = int.MaxValue;
            screenCorners.Adopt(feedPlate, mapPlate);
        }

        /// <summary>
        /// Every frame (<paramref name="tick"/> false: the board's <see cref="TickNatives"/>) and at the end of every present (<paramref name="tick"/> true): lay the corners out again if a native rectangle moved, grew or
        /// came and went, always on a tick; stand them down, and give the feed's plate back, whenever the option is off or the board is stale (its update stopped) or gated; the board's own master switch does not stand them down. Fail open: the plate comes back the very frame the board's shade is not standing in for it.
        /// </summary>
        private void CornersFrame(bool alive, bool tick)
        {
            if (!(alive && Options.Corners && !cornersSuppressed && canvasRect != null)) { StandDownCorners(); return; }
            if (!screenCorners.Complete && findTries < 6 && Time.unscaledTime >= findAt)
            {
                findAt = Time.unscaledTime + 2f;
                findTries++;
                screenCorners.Find();
            }
            bool feedUp = screenCorners.FeedUp, mapUp = screenCorners.MapUp;
            Vector2 feedSize = feedUp ? screenCorners.FeedBox.rect.size : default, mapSize = mapUp ? screenCorners.MapPlate.rect.size : default;
            Vector3 feedAt = feedUp ? screenCorners.FeedBox.position : default, mapAt = mapUp ? screenCorners.MapPlate.position : default;
            bool moved = feedUp != seenFeedUp || mapUp != seenMapUp || feedSize != seenFeedSize || mapSize != seenMapSize || feedAt != seenFeedAt || mapAt != seenMapAt;
            if (tick || moved)
            {
                seenFeedUp = feedUp; seenMapUp = mapUp; seenFeedSize = feedSize; seenMapSize = mapSize; seenFeedAt = feedAt; seenMapAt = mapAt;
                PaintCorners(feedUp, mapUp);
            }
            // The plate is hidden only while our own objects are up in the HUD: a hidden HUD canvas takes them with it and gives the plate back.
            screenCorners.HidePlate(feedLive && cornerFurniture != null && cornerFurniture.gameObject.activeInHierarchy);
        }

        /// <summary>Corners off for now: nothing of ours shows and the feed's plate is the game's again.</summary>
        private void StandDownCorners()
        {
            screenCorners.HidePlate(false);
            if (cornerShade != null) Show(cornerShade, false);
            if (cornerFurniture != null) Show(cornerFurniture, false);
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
        /// Lay out and draw: the feed's shade (the sun band over the top-left and one cloud behind the box, baked when its size changes) and the furniture of whichever corner is up, from the native rectangles. The
        /// feed's plate is hidden by the caller while the feed is drawn here.
        /// </summary>
        private void PaintCorners(bool feedUp, bool mapUp)
        {
            if (!(feedUp || mapUp))
            {
                feedLive = false;
                if (cornerShade != null) { Show(cornerShade, false); Show(cornerFurniture, false); }
                return;
            }
            if (!EnsureCornerTree()) return;
            float k = 1f / baseUnit, canvasW = canvasRect.rect.width, canvasH = canvasRect.rect.height, l = 0f, t = 0f, r = 0f, b = 0f, ml = 0f, mt = 0f, mr = 0f, mb = 0f;
            bool feed = feedUp && CanvasBox(screenCorners.FeedBox, out l, out t, out r, out b);
            bool map = mapUp && CanvasBox(screenCorners.MapPlate, out ml, out mt, out mr, out mb);
            feedLive = feed;
            feedFurniture = feed ? CornerLayout.Feed(l, t, r, b, k) : default;
            mapFurniture = map ? CornerLayout.Map(ml, mt, mr, mb, canvasH, k) : default;

            float strength = Mathf.Clamp01(Options.Shade);
            bool shade = feed && strength > 0.001f;
            Show(cornerShade, shade);
            if (shade) PaintFeedShade(strength, l * k, t * k, (r - l) * k, (b - t) * k);

            Show(cornerFurniture, feed || map);
            if (!(feed || map)) return;
            PlaceOnGrid(cornerSurface, 0f, 0f, Whole(canvasW), Whole(canvasH));
            ScopeCanvas g = cornerCanvas;
            g.Begin(Palette(), baseUnit, Whole(canvasH), pixel);
            if (allow.Furniture)
            {
                if (feed) DrawFeedFurniture(g, feedFurniture);
                if (map) DrawMapFurniture(g, mapFurniture);
            }
            g.End();
            cornerVector.Commit();
        }

        /// <summary>
        /// The feed's shade (LAYOUT.md 2): the band, 760 x 190 from the top-left of the screen with its right end ramping out over the last 280 (the left ramp is off the screen), and ONE cloud behind the box (the mock's
        /// 0, 8, 480 x 98 for the four lines of a box 475 x 90): the rectangle from the box's top + 8, as wide and 8 taller than the box, grown to 32 x 16 units so a new line does not bake the field again every time.
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

        /// <summary>DL EVT LOG: two arm-10 brackets on the box's left edge and the caption shelf under its last line.</summary>
        private static void DrawFeedFurniture(ScopeCanvas g, in CornerFurniture f)
        {
            if (f.Present)
            {
                g.Line(f.X0, f.Y0, f.X0 + f.Arm, f.Y0, Dim);
                g.Line(f.X0, f.Y0, f.X0, f.Y0 + f.Arm, Dim);
                g.Line(f.X0, f.Y1, f.X0 + f.Arm, f.Y1, Dim);
                g.Line(f.X0, f.Y1, f.X0, f.Y1 - f.Arm, Dim);
            }
            g.CaptionShelf(f.ShelfX, f.ShelfY, f.ShelfW, "DL EVT LOG");
        }

        /// <summary>TAC MAP: four DIM brackets round the plate and the caption shelf above them.</summary>
        private static void DrawMapFurniture(ScopeCanvas g, in CornerFurniture f)
        {
            if (f.Present) g.Brackets(f.X0, f.Y0, f.X1 - f.X0, f.Y1 - f.Y0, f.Arm, Dim);
            g.CaptionShelf(f.ShelfX, f.ShelfY, f.ShelfW, "TAC MAP");
        }

        private void DestroyCorners()
        {
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
            feedFurniture = mapFurniture = default;
        }
    }
}
