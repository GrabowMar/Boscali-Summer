using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// The hologram's furniture on the HUD canvas, in the helmet visor's language: ink over the world, no plate, four corner brackets round the picture and a captioned shelf. One picture in two sizes: the
    /// MINI is a square of 300 units, 16 in from the left and bottom edges, with the shelf inside its lower-left ("TAC MAP" at the left, the radius at the right end, a node and a 45 degree stub) and the game's
    /// grid reference dim at its lower right; the BIG one is 46 % of the canvas height, 24 units in, with the shelf above it ("HOLO MAP", the radius and which way is up) - the shelf glides between the two
    /// as the frame grows in <see cref="HoloPresentation.GrowSeconds"/>. While either is up the game's corner map is hidden through a CanvasGroup of ours on its root (alpha only, never SetActive on a native
    /// object), put back exactly the moment neither is. The game's dark plate behind its corner map (the Image of <c>DynamicMap.hudMapAnchor</c>'s parent, the <c>mapPanel</c> sprite) is hidden the same way (its renderer's alpha, put back with the map), and a soft feathered
    /// shade (<see cref="HudSprites.Feather"/>, the visor's rgb(2, 12, 8), 0.5 for the MINI and 0.4 for the BIG one, 30 units past the disc's box) stands in for it, so the additive picture stays readable over bright sky with no
    /// hard edge. The frame hugs the disc (<see cref="HoloView.DiscBox"/>): the BIG one's brackets are the disc's box 8 units out and its shelf stands 10 units above them; the MINI keeps its square and its shelf inside the lower-left.
    /// Everything is a child of one root under the HUD canvas, sent behind the HUD's own elements.
    /// </summary>
    internal sealed partial class HoloMap
    {
        // Labels: 0 the shelf caption, 1 the shelf flag, 2 north, 3 the outer ring's range, 4..9 the airbases, 10 the selected target, 11 the game's grid reference (MINI only).
        private const int LabelCaption = 0, LabelFlag = 1, LabelNorth = 2, LabelRing = 3, LabelBase = 4, LabelTarget = 10, LabelGrid = 11, LabelCount = 12;

        private GameObject root;
        private RectTransform rootRect, canvasRect;
        private CanvasGroup rootGroup;
        private Canvas hudCanvas;
        private RawImage picture;
        private AvVector furniture, marks;
        private readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[LabelCount];
        private float unit = 1f, side = 1f, pixel = 1f, laidHeight = -1f, laidMargin = -1f;
        private bool furnitureDirty;
        private int inkAtFurniture = -1;
        private CanvasGroup mapGroup;
        private Text nativeGrid;
        private float nativeGridAlpha = 1f;
        private Image nativePlate, shade;
        private float nativePlateAlpha = 1f;
        /// <summary>The shade's alpha behind the MINI and behind the BIG picture.</summary>
        private const float ShadeMini = .5f, ShadeBig = .4f;

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
            picture.uvRect = Upright(1f);

            furniture = AvVector.Create(rootRect, "Furniture", 160);
            marks = AvVector.Create(rootRect, "Marks", 4096);
            MakeLabel(LabelCaption, AvTextRole.Label, TextAlignmentOptions.MidlineLeft, new Vector2(0f, .5f));
            MakeLabel(LabelFlag, AvTextRole.Label, TextAlignmentOptions.MidlineRight, new Vector2(1f, .5f));
            MakeLabel(LabelNorth, AvTextRole.Micro, TextAlignmentOptions.Midline, new Vector2(.5f, .5f));
            for (int i = LabelRing; i < LabelGrid; i++)
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
        /// Sizes everything from the canvas height and how far the picture has grown: a unit is the canvas height over 1080 (a 1080p pixel), a hairline is one device pixel, the picture's side and its
        /// distance from the corner follow <see cref="HoloPresentation.Side"/> and <see cref="HoloPresentation.Margin"/>, and the part of the texture it is drawn into follows the pixels it is shown with.
        /// True when the picture's box changed (a picture is due: its marks are laid out for the new box).
        /// </summary>
        private bool Layout()
        {
            if (canvasRect == null) return false;
            float height = canvasRect.rect.height;
            if (height < 100f) return false;
            float wantSide = HoloPresentation.Side(size, height), wantMargin = HoloPresentation.Margin(size, height);
            bool resized = !Mathf.Approximately(height, laidHeight);
            if (!resized && wantSide == side && wantMargin == laidMargin) return false;
            laidHeight = height;
            laidMargin = wantMargin;
            unit = height / 1080f;
            side = wantSide;
            float scale = Mathf.Max(.01f, hudCanvas.rootCanvas.scaleFactor);
            pixel = 1f / scale;
            rootRect.anchoredPosition = new Vector2(wantMargin, wantMargin);
            rootRect.sizeDelta = new Vector2(side, side);
            if (resized)
                for (int i = 0; i < LabelCount; i++)
                {
                    labels[i].fontSize = Mathf.Max(9f, Mathf.Round((i < LabelNorth ? 12f : 11f) * unit));
                    labels[i].rectTransform.sizeDelta = new Vector2(Mathf.Round(190f * unit), Mathf.Round(16f * unit));
                }
            int drawn = HoloPresentation.RenderPixels(side * scale);
            if (drawn != pixels)
            {
                pixels = drawn;
                float share = drawn / (float)TextureSize;
                picture.uvRect = Upright(share);
            }
            furnitureDirty = true;
            return true;
        }

        /// <summary>
        /// The part of the texture the picture is drawn into, read from its top edge down: in the game the render texture comes out turned over against the marks (they are placed by <see cref="HoloView.Project"/>, the
        /// same numbers as the mesh's view), so the sample runs from the top of the part to its bottom.
        /// </summary>
        private static Rect Upright(float share) => new Rect(0f, share, share, -share);

        /// <summary>The furniture, redrawn only when the layout, the ink, the shape or the flag's words change (the words set the shelf's gaps).</summary>
        private void DrawFurniture(string flag)
        {
            bool mini = shape == HoloShape.Mini;
            if (AvText.Set(labels[LabelCaption], mini ? HoloMarks.MiniCaption : HoloMarks.Caption)) furnitureDirty = true;
            if (AvText.Set(labels[LabelFlag], flag)) furnitureDirty = true;
            int ink = Mathf.RoundToInt(inkColour.r * 255f) * 65536 + Mathf.RoundToInt(inkColour.g * 255f) * 256 + Mathf.RoundToInt(inkColour.b * 255f);
            if (ink != inkAtFurniture) furnitureDirty = true;
            if (!furnitureDirty) return;
            furnitureDirty = false;
            inkAtFurniture = ink;
            labels[LabelCaption].color = Dim(.7f);
            labels[LabelFlag].color = inkColour;

            AvQuadBuffer f = furniture.Buffer;
            f.Clear();
            Rgba dim = new Rgba(inkColour.r, inkColour.g, inkColour.b, .7f);
            float u = unit, w = pixel, glide = HoloPresentation.Ease(size);

            // The disc's box on the picture (from the projected outer ring and the north tick), in canvas units from the picture's lower-left corner.
            HoloView.DiscBox(out float bx0, out float bx1, out float by0, out float by1);
            float left = (bx0 * .5f + .5f) * side, right = (bx1 * .5f + .5f) * side, bottom = (by0 * .5f + .5f) * side, top = (by1 * .5f + .5f) * side;

            // The frame: the MINI's is the square picture, 4 units out; the BIG one's hugs the disc's box, 8 units out; it glides between the two as the picture grows.
            float frameLeft = Mathf.Lerp(-4f * u, left - 8f * u, glide), frameBottom = Mathf.Lerp(-4f * u, bottom - 8f * u, glide);
            float frameRight = Mathf.Lerp(side + 4f * u, right + 8f * u, glide), frameTop = Mathf.Lerp(side + 4f * u, top + 8f * u, glide);
            AvStrokes.Bracket(f, frameLeft, frameBottom, frameRight - frameLeft, frameTop - frameBottom, 10f * u, w, dim);

            // The shelf: a hairline interrupted by its two captions (4 unit gaps), ending in a node and a short 45 degree stub, as every block shelf of the visor. Inside the lower-left of the
            // MINI's frame (the game's own screen-edge markers can sit above it); the BIG one's stands 10 units above its brackets, as wide as they are. It glides between the two as the frame grows.
            float start = Mathf.Lerp(0f, left - 8f * u, glide), end = Mathf.Lerp(side * .62f, right + 8f * u, glide), gap = 4f * u;
            float y = Mathf.Lerp(14f * u, top + 18f * u, glide);
            float captionEnd = start + 12f * u + AvText.Width(labels[LabelCaption]) + gap;
            float flagStart = end - 12f * u - AvText.Width(labels[LabelFlag]) - gap;
            AvStrokes.Line(f, start, y, start + 12f * u - gap, y, w, dim);
            if (flagStart > captionEnd) AvStrokes.Line(f, captionEnd, y, flagStart, y, w, dim);
            AvStrokes.Line(f, end - 12f * u + gap, y, end, y, w, dim);
            AvStrokes.Fill(f, end - 2f * u, y - 2f * u, 4f * u, 4f * u, dim);
            AvStrokes.Line(f, end + 2f * u, y + 2f * u, end + 8f * u, y + 8f * u, w, dim);
            Place(LabelCaption, start + 12f * u, y);
            Place(LabelFlag, end - 12f * u, y);
            furniture.Commit();

            // The shade: the disc's box and 30 units on every side, its soft edge 40 units (design units, at any screen height) with no hard edge anywhere; 0.5 behind the MINI, 0.4 behind the BIG one.
            float margin = 30f * u;
            RectTransform shadeRect = shade.rectTransform;
            shadeRect.anchoredPosition = new Vector2(left - margin, bottom - margin);
            shadeRect.sizeDelta = new Vector2(right - left + 2f * margin, top - bottom + 2f * margin);
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
            laidHeight = laidMargin = -1f;
            pixels = TextureSize;
            furnitureDirty = false;
            inkAtFurniture = -1;
        }
    }
}
