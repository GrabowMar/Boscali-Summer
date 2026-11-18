using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>The 1080-unit grid used by cockpit markers, inside the native HUD or on a standalone overlay.</summary>
    internal sealed class HudMarkerCanvas
    {
        private const float Height = 1080f;
        private Canvas overlay;
        private float layerScale;

        public RectTransform Root { get; private set; }
        public bool InLayer { get; private set; }
        public float Scale => InLayer ? layerScale : overlay.scaleFactor;

        public void Create(string name, Transform owner, bool inLayer)
        {
            InLayer = inLayer;
            if (inLayer)
            {
                Root = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
                Root.SetParent(HudLayer.Motion, false);
                Root.anchorMin = Root.anchorMax = Root.pivot = new Vector2(0.5f, 0.5f);
                Root.anchoredPosition = Vector2.zero;
                return;
            }

            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Root = (RectTransform)go.transform;
            Root.SetParent(owner, false);
            overlay = go.GetComponent<Canvas>();
            overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            overlay.sortingOrder = 1;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, Height);
            scaler.matchWidthOrHeight = 1f;
            var group = go.AddComponent<CanvasGroup>();
            group.blocksRaycasts = group.interactable = false;
        }

        public void FitToLayer(HudView view)
        {
            layerScale = Mathf.Max(0.1f, Screen.height / Height);
            Root.localScale = Vector3.one * (layerScale / Mathf.Max(0.01f, view.Scale));
            Root.sizeDelta = new Vector2(Screen.width / layerScale, Height);
        }

        public bool Project(Camera camera, HudView view, Vector3 world, out Vector2 local)
        {
            if (InLayer)
            {
                bool projected = view.Project(world, out local);
                local *= view.Scale / layerScale;
                return projected;
            }

            Vector3 screen = camera.WorldToScreenPoint(world);
            local = default;
            return screen.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(Root, screen, null, out local);
        }

        public void Clear()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
            Root = null;
            overlay = null;
        }
    }
}
