using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// The hologram's furniture on the HUD canvas, in the helmet visor's language: ink over the world, no plate, four corner brackets round the picture and a captioned shelf. One frame in two sizes
    /// (<see cref="HoloPresentation.Frame"/>): the MINI is 340 x 300 units, 16 in from the left and bottom edges, with the shelf inside its lower-left ("TAC MAP" at the left, the range at the right end, a node and a 45 degree
    /// stub), the game's grid reference dim at its lower right and the scale bar to the left of that; the BIG one is 82 % of the canvas height and 1.3 times as wide, centred on the canvas, with the shelf above it ("HOLO MAP", the
    /// range and which way is up) and the scale bar at its lower right - the shelf glides between the two as the frame grows in <see cref="HoloPresentation.GrowSeconds"/>. While either is up the game's corner map is hidden
    /// through a CanvasGroup of ours on its root (alpha only, never SetActive on a native object), put back exactly the moment neither is. The game's dark plate behind its corner map (the Image of
    /// <c>DynamicMap.hudMapAnchor</c>'s parent, the <c>mapPanel</c> sprite) is hidden the same way (its renderer's alpha, put back with the map), and a soft feathered shade (<see cref="HudSprites.Feather"/>, the visor's
    /// rgb(2, 12, 8), 0.65 for the MINI and 0.6 for the BIG one, reaching 40 units past the frame) stands in for it, so the additive picture stays readable over bright sky with no hard edge. The scale bar is a round
    /// number of kilometres (<see cref="HoloMarks.ScaleBar"/>) measured at its own row of the picture, as long as that is of the frame's width. Everything is a child of one root under the HUD canvas, sent behind the
    /// HUD's own elements; the root is the frame, so every mark and label is placed in units from its lower-left corner.
    /// </summary>
    internal sealed partial class HoloMap
    {
        // Labels: 0 the shelf caption, 1 the shelf flag, 2 north, 3 the scale bar's length, 4..9 the airbases, 10 the selected target, 11 the game's grid reference (MINI only).
        private const int LabelCaption = 0, LabelFlag = 1, LabelNorth = 2, LabelScale = 3, LabelBase = 4, LabelTarget = 10, LabelGrid = 11, LabelCount = 12;

        private GameObject root;
        private RectTransform rootRect, canvasRect;
        private CanvasGroup rootGroup;
        private Canvas hudCanvas;
        private RawImage picture;
        private AvVector furniture, marks;
        private readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[LabelCount];
        // The frame's size in canvas units, a unit (the canvas height over 1080), a hairline (one device pixel in canvas units) and what the layout was last laid out for.
        private float unit = 1f, frameW = 1f, frameH = 1f, pixel = 1f, laidHeight = -1f, laidWidth = -1f, laidX = -1f, laidY = -1f;
        private bool furnitureDirty;
        private int inkAtFurniture = -1;
        // The scale bar as last drawn: its length in metres and units.
        private float barMetres, barLength;
        private CanvasGroup mapGroup;
        private Text nativeGrid;
        private float nativeGridAlpha = 1f;
        private Image nativePlate, shade;
        private float nativePlateAlpha = 1f;
        /// <summary>The shade's alpha behind the MINI and behind the BIG picture.</summary>
        private const float ShadeMini = .65f, ShadeBig = .6f;
        /// <summary>The MINI's shelf ends this many units in (the scale bar and the grid reference have the rest of the row), and the room kept for the grid reference at the frame's lower right.</summary>
        private const float MiniShelfEnd = 140f, GridRoom = 44f;
        /// <summary>The BIG map's shelf is this long (design units from the frame's left): the caption and the range at its end, short enough to stay clear of the HUD's stores and board rows at the top of the screen.</summary>
        private const float BigShelfEnd = 440f;

        /// <summary>Builds the root, the picture, the two vector layers and the labels once, under the HUD canvas: the canvas of the corner map, else the canvas of the HUD's icon layer.</summary>
        private bool PrepareUi()
        {
            if (root != null && picture != null && canvasRect != null) return true;
            ReleaseUi();
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            Canvas canvas = map != null && map.hudMapAnchor != null ? map.hudMapAnchor.GetComponentInParent<Canvas>() : null;
            if (canvas == null && hud != null && hud.iconLayer != null) canvas = hud.iconLayer.GetComponentInParent<Canvas>();
            if (canvas == null || !(canvas.transform is RectTransform parent)) return false;
            hudCanvas = canvas;
            canvasRect = parent;

            root = new GameObject("BoscaliHoloMap", typeof(RectTransform), typeof(CanvasGroup));
            rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            rootRect.SetAsFirstSibling();
            rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = Vector2.zero;
            rootGroup = root.GetComponent<CanvasGroup>();
            rootGroup.interactable = false;
            rootGroup.blocksRaycasts = false;

            // The soft shade is the first thing in the root, so it lies behind the picture; placed and tinted with the furniture.
            shade = HudSprites.Child(rootRect, "Shade", HudSprites.Feather, true);
            RectTransform shadeRect = shade.rectTransform;
            shadeRect.anchorMin = shadeRect.anchorMax = shadeRect.pivot = Vector2.zero;
            shade.color = new Color(HudSprites.ShadeTint.r, HudSprites.ShadeTint.g, HudSprites.ShadeTint.b, 0f);

            var image = new GameObject("Hologram", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var imageRect = (RectTransform)image.transform;
            imageRect.SetParent(rootRect, false);
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = imageRect.offsetMax = Vector2.zero;
            picture = image.GetComponent<RawImage>();
            picture.raycastTarget = false;
            picture.texture = rt;
            picture.material = additive;
            picture.uvRect = Upright(1f, 1f);

            furniture = AvVector.Create(rootRect, "Furniture", 160);
            marks = AvVector.Create(rootRect, "Marks", 4096);
            MakeLabel(LabelCaption, AvTextRole.Label, TextAlignmentOptions.MidlineLeft, new Vector2(0f, .5f));
            MakeLabel(LabelFlag, AvTextRole.Label, TextAlignmentOptions.MidlineRight, new Vector2(1f, .5f));
            MakeLabel(LabelNorth, AvTextRole.Micro, TextAlignmentOptions.Midline, new Vector2(.5f, .5f));
            MakeLabel(LabelScale, AvTextRole.Micro, TextAlignmentOptions.MidlineRight, new Vector2(1f, .5f));
            for (int i = LabelBase; i < LabelGrid; i++)
                MakeLabel(i, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, new Vector2(0f, .5f));
            MakeLabel(LabelGrid, AvTextRole.Micro, TextAlignmentOptions.MidlineRight, new Vector2(1f, .5f));
            AvText.Set(labels[LabelCaption], HoloMarks.Caption);
            root.SetActive(false);
            laidHeight = -1f;
            return true;
        }

        private void MakeLabel(int index, AvTextRole role, TextAlignmentOptions align, Vector2 pivot)
        {
            TextMeshProUGUI label = AvText.Make(rootRect, "Label " + index, role, "", align);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = pivot;
            label.enableWordWrapping = false;
            labels[index] = label;
        }

        private void ShowRoot()
        {
            if (root == null) return;
            picture.texture = rt;
            picture.material = additive;
            laidHeight = -1f;
            root.SetActive(true);
            Layout();
        }

        /// <summary>
        /// Sizes everything from the canvas and how far the picture has grown: a unit is the canvas height over 1080 (a 1080p pixel), a hairline is one device pixel, the frame's place and size follow
        /// <see cref="HoloPresentation.Frame"/>, and the part of the texture it is drawn into follows the pixels it is shown with. True when the frame's box changed (a picture is due: its marks are laid out for the new box).
        /// </summary>
        private bool Layout()
        {
            if (canvasRect == null) return false;
            float height = canvasRect.rect.height, width = canvasRect.rect.width;
            if (height < 100f || width < 100f) return false;
            HoloPresentation.Frame(size, width, height, out float x, out float y, out float w, out float h);
            bool resized = !Mathf.Approximately(height, laidHeight) || !Mathf.Approximately(width, laidWidth);
            if (!resized && w == frameW && h == frameH && x == laidX && y == laidY) return false;
            laidHeight = height;
            laidWidth = width;
            laidX = x;
            laidY = y;
            unit = height / 1080f;
            frameW = w;
            frameH = h;
            float scale = Mathf.Max(.01f, hudCanvas.rootCanvas.scaleFactor);
            pixel = 1f / scale;
            rootRect.anchoredPosition = new Vector2(x, y);
            rootRect.sizeDelta = new Vector2(w, h);
            if (resized)
                for (int i = 0; i < LabelCount; i++)
                {
                    labels[i].fontSize = Mathf.Max(9f, Mathf.Round((i < LabelNorth ? 12f : 11f) * unit));
                    labels[i].rectTransform.sizeDelta = new Vector2(Mathf.Round(190f * unit), Mathf.Round(16f * unit));
                }
            HoloPresentation.RenderPixels(w * scale, h * scale, out int drawnWide, out int drawnHigh);
            if (drawnWide != pixelsWide || drawnHigh != pixelsHigh)
            {
                pixelsWide = drawnWide;
                pixelsHigh = drawnHigh;
                picture.uvRect = Upright(drawnWide / (float)TextureWidth, drawnHigh / (float)TextureHeight);
            }
            furnitureDirty = true;
            return true;
        }

        /// <summary>
        /// The part of the texture the picture is drawn into, read from its top edge down: in the game the render texture comes out turned over against the marks (they are placed by <see cref="HoloView.Project"/>, the
        /// same numbers as the mesh's view), so the sample runs from the top of the part to its bottom.
        /// </summary>
        private static Rect Upright(float shareWide, float shareHigh) => new Rect(0f, shareHigh, shareWide, -shareHigh);

        /// <summary>The furniture, redrawn only when the layout, the ink, the shape, the scale or the flag's words change (the words set the shelf's gaps).</summary>
        private void DrawFurniture(string flag)
        {
            bool mini = shape == HoloShape.Mini;
            if (AvText.Set(labels[LabelCaption], mini ? HoloMarks.MiniCaption : HoloMarks.Caption)) furnitureDirty = true;
            if (AvText.Set(labels[LabelFlag], flag)) furnitureDirty = true;
            int ink = Mathf.RoundToInt(inkColour.r * 255f) * 65536 + Mathf.RoundToInt(inkColour.g * 255f) * 256 + Mathf.RoundToInt(inkColour.b * 255f);
            if (ink != inkAtFurniture) furnitureDirty = true;

            // The scale bar: a round number of kilometres, as long on the frame as that is of its width at the bar's own row (the picture is in perspective, a little wider farther up).
            float u = unit, w = pixel, glide = HoloPresentation.Ease(size), row = 14f * u;
            float across = HoloView.WidthAt(camera, row / frameH * 2f - 1f), metres = HoloMarks.ScaleBar(across), length = metres > 0f ? metres / across * frameW : 0f;
            if (metres != barMetres || Mathf.Abs(length - barLength) > .5f * u) furnitureDirty = true;
            if (AvText.Set(labels[LabelScale], metres > 0f ? HoloMarks.Range(metres) : "")) furnitureDirty = true;
            if (!furnitureDirty) return;
            furnitureDirty = false;
            inkAtFurniture = ink;
            barMetres = metres;
            barLength = length;
            labels[LabelCaption].color = Dim(.7f);
            labels[LabelFlag].color = inkColour;
            labels[LabelScale].color = Dim(.7f);

            AvQuadBuffer f = furniture.Buffer;
            f.Clear();
            Rgba dim = new Rgba(inkColour.r, inkColour.g, inkColour.b, .7f);

            // The frame's corner brackets: 4 units out on the MINI, 8 on the BIG one; the shade is the frame and 40 units on every side, its soft edge 40 units (design units, at any screen height) with no hard edge anywhere.
            float pad = Mathf.Lerp(4f * u, 8f * u, glide);
            AvStrokes.Bracket(f, -pad, -pad, frameW + 2f * pad, frameH + 2f * pad, 10f * u, w, dim);

            // The shelf: a hairline interrupted by its two captions (4 unit gaps), ending in a node and a short 45 degree stub, as every block shelf of the visor. Inside the lower-left of the MINI's frame (the game's own
            // screen-edge markers can sit above it, the scale bar and the grid reference have the rest of its row); the BIG one's stands above its brackets, <see cref="BigShelfEnd"/> long. It glides between the two as the frame grows.
            float start = Mathf.Lerp(0f, -pad, glide), end = Mathf.Lerp(MiniShelfEnd * u, Mathf.Min(frameW + pad, BigShelfEnd * u), glide), gap = 4f * u;
            float y = Mathf.Lerp(row, frameH + 18f * u, glide);
            float captionEnd = start + 12f * u + AvText.Width(labels[LabelCaption]) + gap;
            float flagStart = end - 12f * u - AvText.Width(labels[LabelFlag]) - gap;
            AvStrokes.Line(f, start, y, start + 12f * u - gap, y, w, dim);
            if (flagStart > captionEnd) AvStrokes.Line(f, captionEnd, y, flagStart, y, w, dim);
            AvStrokes.Line(f, end - 12f * u + gap, y, end, y, w, dim);
            AvStrokes.Fill(f, end - 2f * u, y - 2f * u, 4f * u, 4f * u, dim);
            AvStrokes.Line(f, end + 2f * u, y + 2f * u, end + 8f * u, y + 8f * u, w, dim);
            Place(LabelCaption, start + 12f * u, y);
            Place(LabelFlag, end - 12f * u, y);

            // The scale bar at the frame's lower right, to the left of the grid reference (the MINI's); its length written to the left of it.
            if (length > 0f)
            {
                float right = frameW - 10f * u - (mini ? GridRoom * u : 0f), left = right - length, tick = 3f * u;
                AvStrokes.Line(f, left, row, right, row, w, dim);
                AvStrokes.Line(f, left, row - tick, left, row + tick, w, dim);
                AvStrokes.Line(f, right, row - tick, right, row + tick, w, dim);
                Place(LabelScale, left - 5f * u, row);
            }
            furniture.Commit();

            float margin = 40f * u;
            RectTransform shadeRect = shade.rectTransform;
            shadeRect.anchoredPosition = new Vector2(-margin, -margin);
            shadeRect.sizeDelta = new Vector2(frameW + 2f * margin, frameH + 2f * margin);
            shade.pixelsPerUnitMultiplier = 1f / Mathf.Max(.01f, u);
            shade.color = new Color(HudSprites.ShadeTint.r, HudSprites.ShadeTint.g, HudSprites.ShadeTint.b, Mathf.Lerp(ShadeMini, ShadeBig, glide));
        }

        private Color Dim(float alpha) => new Color(inkColour.r, inkColour.g, inkColour.b, alpha);

        private void Place(int index, float x, float y)
        {
            RectTransform rect = labels[index].rectTransform;
            var at = new Vector2(Mathf.Round(x), Mathf.Round(y));
            if (rect.anchoredPosition != at) rect.anchoredPosition = at;
        }

        /// <summary>The fade-in: the picture comes up while the corner map fades away behind it (only when a presentation first shows; between MINI and BIG the corner map stays hidden).</summary>
        private void ApplyPresence()
        {
            float eased = Mathf.SmoothStep(0f, 1f, appear);
            if (rootGroup.alpha != eased) rootGroup.alpha = eased;
            if (picture.color.a != eased) picture.color = new Color(1f, 1f, 1f, eased);
            HideMinimap(1f - eased);
        }

        private void HideMinimap(float alpha)
        {
            if (mapGroup == null)
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map == null) return;
                mapGroup = map.gameObject.AddComponent<CanvasGroup>();
                if (mapGroup == null) return;
            }
            if (mapGroup.alpha != alpha) mapGroup.alpha = alpha;
            FadeNativeGrid(alpha);
            FadeNativePlate(alpha);
        }

        /// <summary>
        /// The game's dark plate behind its corner map is the Image on the map anchor's parent (<c>HMDCenter/LowerLeftPanel</c>, sprite <c>mapPanel</c>, found in game 2026-10-13; <c>DynamicMap.mapBackground</c> is the map's own
        /// background below it and is already hidden with the map). It is no part of the map root our group sits on, so it stayed behind the hologram. Its renderer's alpha follows the corner map's and is put back exactly as
        /// it was found: never <c>SetActive</c> (its children are the corner map), never the plate's own colour, never a group on it.
        /// </summary>
        private void FadeNativePlate(float alpha)
        {
            if (nativePlate == null)
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                Transform panel = map != null && map.hudMapAnchor != null ? map.hudMapAnchor.parent : null;
                nativePlate = panel != null ? panel.GetComponent<Image>() : null;
                if (nativePlate == null) return;
                nativePlateAlpha = nativePlate.canvasRenderer.GetAlpha();
            }
            if (nativePlate.canvasRenderer.GetAlpha() != alpha * nativePlateAlpha) nativePlate.canvasRenderer.SetAlpha(alpha * nativePlateAlpha);
        }

        /// <summary>
        /// The game's own grid reference (white, at the corner map's lower right) is parented under <c>DynamicMap.hudMapAnchor</c>, outside the root our group sits on, so it stayed up under the hologram's own dim copy of it.
        /// Its renderer's alpha follows the corner map's: the game only switches the text on and off (<c>enabled</c>) and writes its words, never this alpha.
        /// </summary>
        private void FadeNativeGrid(float alpha)
        {
            if (nativeGrid == null)
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                GridLabels labels = map != null ? map.gridLabels : null;
                nativeGrid = labels != null && GridAircraft != null ? GridAircraft(labels) : null;
                if (nativeGrid == null) return;
                nativeGridAlpha = nativeGrid.canvasRenderer.GetAlpha();
            }
            if (nativeGrid.canvasRenderer.GetAlpha() != alpha * nativeGridAlpha) nativeGrid.canvasRenderer.SetAlpha(alpha * nativeGridAlpha);
        }

        /// <summary>Takes our group off the corner map's root: the game has it exactly as it was (its own alpha is never written).</summary>
        private void RestoreMinimap()
        {
            if (nativeGrid != null) nativeGrid.canvasRenderer.SetAlpha(nativeGridAlpha);
            nativeGrid = null;
            if (nativePlate != null) nativePlate.canvasRenderer.SetAlpha(nativePlateAlpha);
            nativePlate = null;
            if (mapGroup == null) return;
            mapGroup.alpha = 1f;
            Destroy(mapGroup);
            mapGroup = null;
        }

        private void ReleaseUi()
        {
            if (root != null) Destroy(root);
            root = null;
            rootRect = canvasRect = null;
            rootGroup = null;
            hudCanvas = null;
            picture = null;
            shade = null;
            furniture = marks = null;
            for (int i = 0; i < labels.Length; i++) labels[i] = null;
            laidHeight = laidWidth = laidX = laidY = -1f;
            pixelsWide = TextureWidth;
            pixelsHigh = TextureHeight;
            furnitureDirty = false;
            inkAtFurniture = -1;
            barMetres = barLength = 0f;
        }
    }
}
