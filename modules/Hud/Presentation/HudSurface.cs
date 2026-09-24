using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Hud.Presentation
{
    /// <summary>Original screen-space primitives; no native sprites, materials or layout inheritance.</summary>
    internal sealed class HudSurface
    {
        public readonly GameObject Root;
        public readonly Canvas Canvas;
        public readonly CanvasGroup Group;
        public Transform Transform => Root.transform;
        public float Scale => Canvas.scaleFactor;
        public HudSurface(string name, Transform parent)
        {
            Root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            Root.transform.SetParent(parent, false);
            Canvas = Root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            FlightHud native = SceneSingleton<FlightHud>.i;
            Canvas nativeCanvas = native != null ? native.GetComponentInParent<Canvas>() : null;
            Canvas.sortingOrder = nativeCanvas != null ? nativeCanvas.sortingOrder + 1 : 1;
            Group = Root.GetComponent<CanvasGroup>(); Group.blocksRaycasts = Group.interactable = false;
            Resize(1);
        }
        public void Resize(float scale)
        {
            Canvas.scaleFactor = Mathf.Max(.1f, Mathf.Min(Screen.width / 1920f, Screen.height / 1080f) * scale);
        }
        public Rect Safe
        {
            get
            {
                Rect safe = Screen.safeArea;
                if (safe.width < 1 || safe.height < 1) safe = new Rect(0, 0, Screen.width, Screen.height);
                return new Rect(safe.x / Scale, safe.y / Scale, safe.width / Scale, safe.height / Scale);
            }
        }
        public void Show(bool value) { if (Root.activeSelf != value) Root.SetActive(value); }
        public void Destroy() { Show(false); Object.Destroy(Root); }
        public static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); return rect;
        }
        public static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(w, h);
        }
        public static TMP_Text Text(string name, Transform parent, float size = 16)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            label.transform.SetParent(parent, false);
            label.font = AvFont.Font ?? TMP_Settings.defaultFontAsset;
            if (label.font != null) label.fontSharedMaterial = label.font.material;
            label.fontSize = size; label.color = AvTheme.TextPrimary;
            label.enableWordWrapping = false; label.richText = false;
            label.overflowMode = TextOverflowModes.Ellipsis; label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false; return label;
        }
        public static Image Line(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false); image.raycastTarget = false; image.color = color; return image;
        }
        public static Color Ink => Color.Lerp(VanillaHudStyle.Colours.AllClear, Color.white, .12f);
        public static Color Tone(HudTone tone) => tone == HudTone.Warning ? Color.Lerp(VanillaHudStyle.Colours.Alert, Color.white, .35f)
            : tone == HudTone.Caution ? Color.Lerp(VanillaHudStyle.Colours.Warning, Color.white, .25f) : Ink;
        public static void Write(TMP_Text label, string text, Color color)
        { if (label.text != text) label.text = text; if (label.color != color) label.color = color; }
    }

    // A mirrored chamfer echoes the map's corner silhouette without borrowing any game asset.
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class HudPanel : MaskableGraphic
    {
        private int contrast = 1;
        private bool mirrored, outlined = true;
        public void Style(int value, bool mirror = false, bool outline = true)
        { if (contrast == value && mirrored == mirror && outlined == outline) return; contrast = value; mirrored = mirror; outlined = outline; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); if (contrast == 0) return;
            Rect r = rectTransform.rect;
            Color fill = new Color(.015f, .025f, .035f); fill.a = contrast == 2 ? .9f : outlined ? .5f : .28f;
            float cut = outlined ? Mathf.Min(18, r.height * .2f) : 0;
            Vector2[] points = mirrored
                ? new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax - cut), new Vector2(r.xMax - cut, r.yMax), new Vector2(r.xMin, r.yMax) }
                : new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin + cut, r.yMax), new Vector2(r.xMin, r.yMax - cut) };
            for (int i = 0; i < points.Length; i++) mesh.AddVert(points[i], fill, Vector2.zero);
            for (int i = 1; i < points.Length - 1; i++) mesh.AddTriangle(0, i, i + 1);
            if (!outlined) return;
            Color edge = AvTheme.Frame; edge.a = .9f;
            for (int i = 0; i < points.Length; i++) Stroke(mesh, points[i], points[(i + 1) % points.Length], edge, 1);
        }
        internal static void Stroke(VertexHelper mesh, Vector2 from, Vector2 to, Color color, float thickness)
        {
            Vector2 d = (to - from).normalized, n = new Vector2(-d.y, d.x) * thickness * .5f;
            int start = mesh.currentVertCount;
            mesh.AddVert(from - n, color, Vector2.zero); mesh.AddVert(from + n, color, Vector2.zero);
            mesh.AddVert(to + n, color, Vector2.zero); mesh.AddVert(to - n, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
