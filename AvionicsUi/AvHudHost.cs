using UnityEngine;

namespace NOAvionics
{
    /// <summary>
    /// The HUD's own screen-space overlay: one root canvas with no raycaster (the HUD never
    /// takes input), a <see cref="CanvasGroup"/> for whole-HUD fades, an <see cref="AvReveal"/>
    /// to drive them, and three stacked full-stretch layers a block can build into
    /// (<see cref="Static"/> for the frame, <see cref="Data"/> for readouts, <see cref="Motion"/>
    /// for anything that moves every frame).
    /// </summary>
    public sealed class AvHudHost
    {
        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;
        private Rect lastSafeArea;
        private float lastSizeStep = float.NaN;

        private AvHudHost() { }

        public GameObject Root { get; private set; }
        public Canvas Canvas { get; private set; }
        public CanvasGroup Group { get; private set; }
        public AvReveal Reveal { get; private set; }

        public RectTransform Static { get; private set; }
        public RectTransform Data { get; private set; }
        public RectTransform Motion { get; private set; }

        public float ScaleFactor { get; private set; }

        /// <summary>The safe area, converted from screen pixels into this canvas's own units.</summary>
        public Rect Safe { get; private set; }

        public static AvHudHost Create(string name, Transform parent, int sortingOrder)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            root.transform.SetParent(parent, worldPositionStays: false);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var group = root.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var reveal = root.AddComponent<AvReveal>();
            reveal.enabled = false;

            var rootRt = (RectTransform)root.transform;

            return new AvHudHost
            {
                Root = root,
                Canvas = canvas,
                Group = group,
                Reveal = reveal,
                Static = CreateLayer(rootRt, "Static"),
                Data = CreateLayer(rootRt, "Data"),
                Motion = CreateLayer(rootRt, "Motion"),
            };
        }

        private static RectTransform CreateLayer(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;

            var canvas = go.GetComponent<Canvas>();
            canvas.overrideSorting = false;
            return rt;
        }

        /// <summary>
        /// Recomputes the canvas scale from the current screen and safe area. A no-op, and
        /// returns false, unless the scale, the screen size or the safe area actually moved.
        /// </summary>
        public bool Refresh(float sizeStep)
        {
            int w = Screen.width;
            int h = Screen.height;
            Rect safeArea = Screen.safeArea;

            if (w == lastScreenWidth && h == lastScreenHeight && safeArea == lastSafeArea && sizeStep == lastSizeStep)
                return false;

            lastScreenWidth = w;
            lastScreenHeight = h;
            lastSafeArea = safeArea;
            lastSizeStep = sizeStep;

            float scale = Mathf.Max(0.1f, Mathf.Min(w / 1920f, h / 1080f) * sizeStep);
            ScaleFactor = scale;
            Canvas.scaleFactor = scale;
            Safe = new Rect(safeArea.x / scale, safeArea.y / scale, safeArea.width / scale, safeArea.height / scale);
            return true;
        }

        public void SetVisible(bool visible)
        {
            if (Canvas.enabled != visible) Canvas.enabled = visible;
        }

        public void SetAlpha(float alpha)
        {
            if (!Mathf.Approximately(Group.alpha, alpha)) Group.alpha = alpha;
        }

        /// <summary>Bottom-left anchored/pivoted placement, the HUD's own coordinate convention.</summary>
        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
        }
    }
}
