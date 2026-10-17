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
    // Native corner presentation: DL EVT LOG replaces the captured feed, TAC MAP dresses the minimised map.
    // Feed text and plate are restored immediately when capture is unhealthy, chat opens or the presentation gate closes.
    // One faint feathered shadow spans the occupied log, COMMS and status column. It shares their sway and follows Shade/Opacity;
    // the sections add no shadows of their own. NAV DATA retains its separate bottom-right presentation.
    internal sealed partial class StatusPanel
    {
        private const int CornerQuads = 120;

        private readonly NativeScreenCorners screenCorners = new NativeScreenCorners();
        private RectTransform cornerShade, cornerFurniture, cornerSurface;
        private Image leftShadow;
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
            CueFrame(tick);
            PaintLeftShadow();
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
            leftShadow = ShadeChild<Image>(cornerShade, "LeftStackShadow");
            HudSprites.Slice(leftShadow, HudSprites.Feather);
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
            if (cueRoot != null) cueRoot.offsetMin = cueRoot.offsetMax = swayShift;
            if (cornerFurniture == null) return;
            cornerShade.offsetMin = cornerShade.offsetMax = swayShift;
            cornerFurniture.offsetMin = cornerFurniture.offsetMax = swayShift;
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
                BoscaliSummer.Core.Ui.LeftHudStack.LogBottom = 0f;
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
            if (!feedLive) BoscaliSummer.Core.Ui.LeftHudStack.LogBottom = 0f;
            if (!feedLive && logRoot != null) Show(logRoot, false);

            Show(cornerFurniture, map);
            PlaceOnGrid(cornerSurface, 0f, 0f, Whole(canvasW), Whole(canvasH));
            ScopeCanvas g = cornerCanvas;
            g.Begin(Palette(), baseUnit, Whole(canvasH), pixel);      // also with no map: the surface is cleared, not left holding the frame it last drew
            if (map && allow.Furniture) DrawMapFurniture(g, mapFurniture, mapNorth);
            g.End();
            cornerVector.Commit();
        }

        /// <summary>
        /// One faint feathered shadow behind the occupied left column, with no overlapping per-section shadows.
        /// </summary>
        private void PaintLeftShadow()
        {
            float top = float.PositiveInfinity, bottom = 0f, lit = 0f;
            if (feedLive && logPlan.Present)
            {
                top = logPlan.Top; bottom = logPlan.Top + logPlan.Height; lit = logLit;
            }
            if (LeftHudStack.CommsBottom > 0f)
            {
                top = Mathf.Min(top, LeftHudStack.CommsTop);
                bottom = Mathf.Max(bottom, LeftHudStack.CommsBottom); lit = 1f;
            }
            if (cueCount > 0 && cueLit > 0f)
            {
                top = Mathf.Min(top, LeftHudStack.CueTop(canvasRect.rect.height / baseUnit));
                bottom = Mathf.Max(bottom, cueBottom); lit = Mathf.Max(lit, cueLit);
            }
            float alpha = LeftHudStack.ShadowAlpha * Mathf.Clamp01(Options.Shade) * Mathf.Clamp01(Options.Opacity) * lit;
            if (bottom <= 0f || alpha <= 0.001f)
            {
                if (cornerShade != null) Show(cornerShade, false);
                return;
            }
            if (!EnsureCornerTree()) return;
            const float feather = 12f;
            PlaceBoxAt(leftShadow.rectTransform, new BoardRect(LeftHudStack.Left - feather, top - feather,
                LeftHudStack.Width + feather * 2f, bottom - top + feather * 2f), baseUnit);
            leftShadow.color = new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, alpha);
            Show(leftShadow, true);
            Show(cornerShade, true);
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
            cornerShade = cornerFurniture = cornerSurface = null;
            leftShadow = null;
            cornerVector = null;
            cornerText = null;
            cornerCanvas = null;
            feedLive = seenFeedUp = seenMapUp = false;
            mapFurniture = default;
            mapNorth = default;
        }
    }
}
