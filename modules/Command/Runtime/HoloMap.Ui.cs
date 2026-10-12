using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// The hologram's furniture on the HUD canvas, in the helmet visor's language: ink over the world, no plate, four corner brackets round the picture and a captioned shelf above it ("HOLO MAP"
    /// at the left, the radius and which way is up at the right, a node and a 45 degree stub at the end). The picture is a square of 46 % of the canvas height, 24 units (a 1080p pixel) in from the
    /// left and bottom edges, growing out of the corner in <see cref="GrowSeconds"/>. While it is up the game's corner map is hidden through a CanvasGroup of ours on its root (alpha only, never
    /// SetActive on a native object), put back exactly the moment the holo goes. Everything is a child of one root under the HUD canvas, sent behind the HUD's own elements.
    /// </summary>
    internal sealed partial class HoloMap
    {
        // Labels: 0 the shelf caption, 1 the shelf flag, 2 north, 3 the outer ring's range, 4..9 the airbases, 10 the selected target.
        private const int LabelCaption = 0, LabelFlag = 1, LabelNorth = 2, LabelRing = 3, LabelBase = 4, LabelTarget = 10, LabelCount = 11;
        private const float SideShare = .46f, Margin = 24f;

        private GameObject root;
        private RectTransform rootRect, canvasRect;
        private CanvasGroup rootGroup;
        private Canvas hudCanvas;
        private RawImage picture;
        private AvVector furniture, marks;
        private readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[LabelCount];
        private float unit = 1f, side = 1f, pixel = 1f, laidHeight = -1f;
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
            for (int i = LabelRing; i < LabelCount; i++)
                MakeLabel(i, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, new Vector2(0f, .5f));
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

        /// <summary>Sizes everything from the canvas height: a unit is the canvas height over 1080 (a 1080p pixel), a hairline is one device pixel.</summary>
        private void Layout()
        {
            float height = canvasRect.rect.height;
            if (height < 100f || Mathf.Approximately(height, laidHeight)) return;
            laidHeight = height;
            unit = height / 1080f;
            side = Mathf.Round(SideShare * height);
            pixel = 1f / Mathf.Max(.01f, hudCanvas.rootCanvas.scaleFactor);
            rootRect.anchoredPosition = new Vector2(Mathf.Round(Margin * unit), Mathf.Round(Margin * unit));
            rootRect.sizeDelta = new Vector2(side, side);
            for (int i = 0; i < LabelCount; i++)
            {
                labels[i].fontSize = Mathf.Max(9f, Mathf.Round((i < LabelNorth ? 12f : 11f) * unit));
                labels[i].rectTransform.sizeDelta = new Vector2(Mathf.Round(190f * unit), Mathf.Round(16f * unit));
            }
            furnitureDirty = true;
        }

        /// <summary>The furniture, redrawn only when the layout, the ink or the flag's words change (the words set the shelf's gaps).</summary>
        private void DrawFurniture(string flag)
        {
            Layout();
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
            float u = unit, w = pixel;
            AvStrokes.Bracket(f, -4f * u, -4f * u, side + 8f * u, side + 8f * u, 10f * u, w, dim);

            // The shelf: a hairline interrupted by its two captions (4 unit gaps), ending in a node and a short 45 degree stub, as every block shelf of the visor.
            float y = side + 18f * u, end = side - 8f * u, gap = 4f * u;
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

        /// <summary>The growth: the picture comes out of the corner at 60 % to full size while the corner map fades away behind it.</summary>
        private void ApplyGrow()
        {
            float eased = Mathf.SmoothStep(0f, 1f, grow);
            rootRect.localScale = Vector3.one * Mathf.Lerp(StartScale, GrownScale, eased);
            rootGroup.alpha = eased;
            picture.color = new Color(1f, 1f, 1f, eased);
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
            laidHeight = -1f;
            furnitureDirty = false;
            inkAtFurniture = -1;
        }
    }
}
