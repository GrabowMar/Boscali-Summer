using BoscaliSummer.Features.TheaterOps.Configuration;
using BoscaliSummer.Features.TheaterOps.Domain;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.TheaterOps.Runtime
{
    /// <summary>
    /// Draws the local faction's main effort as one diamond on the vanilla map. Client-local
    /// presentation only: the transform maths mirror the game's own objective markers so the
    /// marker tracks map zoom and pan, and it disappears with the map like any other icon.
    /// </summary>
    internal sealed class TheaterEffortMarker : MonoBehaviour, ISceneService
    {
        private const float MarkerSize = 13f;

        private TheaterOpsSettings settings;
        private TheaterPriorityService priority;
        private GameObject root;
        private RectTransform rect;
        private Image image;
        private bool shown;

        public void Configure(TheaterOpsSettings config, TheaterPriorityService source)
        {
            settings = config;
            priority = source;
        }

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null;
            rect = null;
            image = null;
            shown = false;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (settings == null || !settings.MapMarkerEnabled.Value ||
                priority == null || !priority.TryGetLocalDirective(out PriorityDirective directive))
            {
                SetShown(false);
                return;
            }

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.mapImage == null || map.iconLayer == null)
            {
                SetShown(false);
                return;
            }

            float inverseScale = 1f / map.mapImage.transform.localScale.x;
            if (float.IsNaN(inverseScale) || float.IsInfinity(inverseScale) || inverseScale <= 0f)
            {
                SetShown(false);
                return;
            }

            EnsureMarker(map);
            rect.localScale = Vector3.one * inverseScale;

            Vector3 world = new GlobalPosition(directive.X, directive.Y, directive.Z).AsVector3();
            rect.localPosition = new Vector3(world.x, world.z, 0f);

            Color colour = map.HQ != null && map.HQ.faction != null ? map.HQ.faction.color : Color.white;
            colour.a = 0.9f;
            image.color = colour;
            SetShown(true);
        }

        private void EnsureMarker(DynamicMap map)
        {
            if (root == null)
            {
                root = new GameObject("BoscaliEffortMarker", typeof(RectTransform), typeof(Image));
                rect = (RectTransform)root.transform;
                image = root.GetComponent<Image>();
                image.raycastTarget = false;
                rect.sizeDelta = new Vector2(MarkerSize, MarkerSize);
                rect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }

            if (rect.parent != map.iconLayer.transform)
                rect.SetParent(map.iconLayer.transform, false);
        }

        private void SetShown(bool value)
        {
            if (root == null || shown == value) return;
            shown = value;
            root.SetActive(value);
        }
    }
}
