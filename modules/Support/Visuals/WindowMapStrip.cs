using BoscaliSummer.Modules.Support.Domain.Orbital;
using BoscaliSummer.Modules.Support.Presentation;
using BoscaliSummer.Modules.Support.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// The station's coverage ring on the maximized tactical map: the sub-point and radius of
    /// the same <c>CheckTarget</c> rule that gates the sweeps and the heaviest fires, read live
    /// off the mirrored platform. Solid while the tasking window is open, dotted while it holds.
    /// Client-local presentation; words live on the MFD, the strip is geometry only.
    /// </summary>
    internal sealed class WindowMapStrip : MonoBehaviour, ISceneService
    {
        private const float MinimumRingPixels = 24f;
        private const float IconPixels = 28f;

        private static readonly Color OpenTint = new Color(0.45f, 0.9f, 1f, 0.55f);
        private static readonly Color ClosedTint = new Color(0.6f, 0.65f, 0.7f, 0.3f);

        private SupportManager support;
        private GameObject root;
        private RectTransform dot;
        private Image ring;
        private Image icon;
        private Sprite shownRing;

        internal void Configure(SupportManager manager) => support = manager;

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null;
            dot = null;
            ring = null;
            icon = null;
            shownRing = null;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (support == null || map == null || map.mapImage == null || !DynamicMap.mapMaximized ||
                Application.isBatchMode)
            {
                Hide();
                return;
            }
            OrbitalPlatform platform = support.LocalPlatform;
            if (platform == null || !platform.Exists)
            {
                Hide();
                return;
            }
            float factor = map.mapDisplayFactor;
            float zoom = map.mapImage.transform.localScale.x;
            if (!Finite(factor) || factor <= 0f || !Finite(zoom) || zoom <= 0f)
            {
                Hide();
                return;
            }
            double now = support.OrbitNow;
            OrbitState state = platform.State(now);
            float radius = platform.CoverageRadius(now);
            float diameter = 2f * radius * factor;
            if (radius <= 0f || diameter * zoom < MinimumRingPixels)
            {
                Hide();
                return;
            }
            if (root == null) Build();
            if (dot.parent != map.mapImage.transform)
            {
                dot.SetParent(map.mapImage.transform, false);
                dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f);
                dot.pivot = new Vector2(0.5f, 0.5f);
                dot.SetAsLastSibling();
            }
            float x;
            float y;
            if (!(ModuleServices.TryGet(out IMapProjection projection) &&
                projection.TryProject((float)state.SubX, (float)state.SubZ, out x, out y)))
            {
                x = (float)state.SubX * factor;
                y = (float)state.SubZ * factor;
            }
            root.SetActive(true);
            dot.anchoredPosition = new Vector2(x, y);
            float inverse = 1f / zoom;
            dot.localScale = new Vector3(inverse, inverse, 1f);
            ring.rectTransform.sizeDelta = new Vector2(diameter, diameter);
            ring.rectTransform.localScale = new Vector3(zoom, zoom, 1f);
            icon.rectTransform.sizeDelta = new Vector2(IconPixels, IconPixels);
            bool open = support.LocalWindowOpen;
            Sprite want = open ? SupportTacticalIcons.RingSprite : SupportTacticalIcons.DottedRingSprite;
            if (want != null && want != shownRing)
            {
                shownRing = want;
                ring.sprite = want;
            }
            ring.color = open ? OpenTint : ClosedTint;
        }

        private void Build()
        {
            SupportTacticalIcons.EnsureInitialized();
            root = new GameObject("Boscali Window Strip", typeof(RectTransform));
            RectTransform layer = (RectTransform)root.transform;
            layer.SetParent(transform, false);
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            dot = Picture(layer, "Dot").rectTransform;
            ring = Picture(dot, "Coverage Ring");
            ring.sprite = SupportTacticalIcons.RingSprite;
            shownRing = ring.sprite;
            icon = Picture(dot, "Satellite");
            icon.sprite = SupportTacticalIcons.SatIcon;
            icon.color = Color.white;
        }

        private void Hide()
        {
            if (root != null && root.activeSelf) root.SetActive(false);
        }

        private static Image Picture(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
