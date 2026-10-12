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
    /// object), put back exactly the moment neither is. Everything is a child of one root under the HUD canvas, sent behind the HUD's own elements.
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
                picture.uvRect = new Rect(0f, 0f, share, share);
            }
            furnitureDirty = true;
            return true;
        }

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
            AvStrokes.Bracket(f, -4f * u, -4f * u, side + 8f * u, side + 8f * u, 10f * u, w, dim);

            // The shelf: a hairline interrupted by its two captions (4 unit gaps), ending in a node and a short 45 degree stub, as every block shelf of the visor. Inside the lower-left of the
            // MINI's frame (the game's own screen-edge markers can sit above it), above the BIG one's; it glides between the two as the frame grows.
            float y = Mathf.Lerp(14f * u, side + 18f * u, glide), end = Mathf.Lerp(side * .62f, side - 8f * u, glide), gap = 4f * u;
            float captionEnd = 12f * u + AvText.Width(labels[LabelCaption]) + gap;
            float flagStart = end - 12f * u - AvText.Width(labels[LabelFlag]) - gap;
            AvStrokes.Line(f, 0f, y, 12f * u - gap, y, w, dim);
            if (flagStart > captionEnd) AvStrokes.Line(f, captionEnd, y, flagStart, y, w, dim);
            AvStrokes.Line(f, end - 12f * u + gap, y, end, y, w, dim);
            AvStrokes.Fill(f, end - 2f * u, y - 2f * u, 4f * u, 4f * u, dim);
            AvStrokes.Line(f, end + 2f * u, y + 2f * u, end + 8f * u, y + 8f * u, w, dim);
            Place(LabelCaption, 12f * u, y);
            Place(LabelFlag, end - 12f * u, y);
            furniture.Commit();
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
        }

        /// <summary>Takes our group off the corner map's root: the game has it exactly as it was (its own alpha is never written).</summary>
        private void RestoreMinimap()
        {
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
            furniture = marks = null;
            for (int i = 0; i < labels.Length; i++) labels[i] = null;
            laidHeight = laidMargin = -1f;
            pixels = TextureSize;
            furnitureDirty = false;
            inkAtFurniture = -1;
        }
    }
}
