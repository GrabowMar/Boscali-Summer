using System.Collections.Generic;
using BoscaliSummer.Features.HighCommand.Configuration;
using BoscaliSummer.Features.HighCommand.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.HighCommand.Presentation
{
    /// <summary>
    /// Draws every command post the local faction may see as one diamond on the vanilla map:
    /// the faction's own posts in the faction's colour, a confirmed enemy post in the alert
    /// colour, bigger for a higher tier, with a ring that pulses while the post is under
    /// fire. Client-local presentation only - the transform maths mirror the game's own
    /// objective markers and the module's theater effort marker, so a diamond tracks map
    /// zoom and pan and disappears with the map.
    ///
    /// <para>Nothing here selects, orders or reveals anything: a post is drawn only when the
    /// view already lists it as friendly or known, so the map cannot show a post the roster
    /// is hiding.</para>
    /// </summary>
    internal sealed class CommandPostMarkers : MonoBehaviour, ISceneService
    {
        private const int MaximumMarkers = CommandSnapshotRules.MaximumNodes;

        private sealed class Marker
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Diamond;
            public Image Core;
            public RectTransform Ring;
            public Image RingImage;
            public bool Alert;
        }

        private static readonly Color MarkerAlert = new Color(0.937f, 0.267f, 0.267f, 1f);

        private readonly List<Marker> pool = new List<Marker>(MaximumMarkers);

        private HighCommandSettings settings;
        private IHighCommandView view;
        private Transform layer;

        public void Configure(HighCommandSettings config, IHighCommandView source)
        {
            settings = config;
            view = source;
        }

        public void ResetForScene()
        {
            for (int i = 0; i < pool.Count; i++)
                if (pool[i].Root != null) Destroy(pool[i].Root);
            pool.Clear();
            layer = null;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (settings == null || !settings.MapMarkersEnabled.Value || view == null || !view.Available)
            {
                HideFrom(0);
                return;
            }
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.mapImage == null || map.iconLayer == null)
            {
                HideFrom(0);
                return;
            }

            IReadOnlyList<CommanderView> commanders = view.Commanders;
            if (commanders == null)
            {
                HideFrom(0);
                return;
            }

            float inverseScale = 1f / map.mapImage.transform.localScale.x;
            if (float.IsNaN(inverseScale) || float.IsInfinity(inverseScale) || inverseScale <= 0f)
            {
                HideFrom(0);
                return;
            }

            layer = map.iconLayer.transform;
            Color friendly = map.HQ != null && map.HQ.faction != null ? map.HQ.faction.color : Color.white;
            float pulse = CommandMarkerPolicy.Pulse(Time.unscaledTime * 2f);

            int drawn = 0;
            for (int i = 0; i < commanders.Count && drawn < MaximumMarkers; i++)
            {
                CommanderView commander = commanders[i];
                if (commander == null ||
                    !CommandMarkerPolicy.Show(commander.IsFriendly, commander.IsKnown, commander.IsKia))
                    continue;

                Marker marker = Take(drawn);
                float size = CommandMarkerPolicy.Size(commander.Tier);
                marker.Root.transform.SetParent(layer, false);
                marker.Rect.localScale = Vector3.one * inverseScale;
                marker.Rect.localPosition = new Vector3(commander.X, commander.Z, 0f);
                marker.Rect.sizeDelta = new Vector2(size, size);

                Color tone = commander.IsFriendly ? friendly : MarkerAlert;
                tone.a = commander.InTransit ? 0.65f : 0.92f;
                marker.Diamond.color = tone;
                marker.Core.color = new Color(0f, 0f, 0f, commander.IsFriendly ? 0.55f : 0f);

                marker.Alert = commander.Alert;
                if (marker.Alert)
                {
                    float ring = 1f + pulse * 0.8f;
                    marker.Ring.localScale = Vector3.one * ring;
                    marker.RingImage.color = new Color(tone.r, tone.g, tone.b,
                        Mathf.Lerp(0.85f, 0f, pulse));
                }
                marker.Ring.gameObject.SetActive(marker.Alert);

                if (!marker.Root.activeSelf) marker.Root.SetActive(true);
                drawn++;
            }

            HideFrom(drawn);
        }

        private void HideFrom(int first)
        {
            for (int i = first; i < pool.Count; i++)
                if (pool[i].Root != null && pool[i].Root.activeSelf) pool[i].Root.SetActive(false);
        }

        private Marker Take(int index)
        {
            while (pool.Count <= index)
            {
                var root = new GameObject("BoscaliPostMarker", typeof(RectTransform));
                var rect = (RectTransform)root.transform;
                root.SetActive(false);

                var diamondObject = new GameObject("Diamond", typeof(RectTransform), typeof(Image));
                var diamondRect = (RectTransform)diamondObject.transform;
                diamondRect.SetParent(rect, false);
                diamondRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                AvMarkerPlace(diamondRect);
                Image diamond = diamondObject.GetComponent<Image>();
                diamond.raycastTarget = false;

                var coreObject = new GameObject("Core", typeof(RectTransform), typeof(Image));
                var coreRect = (RectTransform)coreObject.transform;
                coreRect.SetParent(rect, false);
                coreRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                AvMarkerPlace(coreRect);
                coreRect.localScale = Vector3.one * 0.45f;
                Image core = coreObject.GetComponent<Image>();
                core.raycastTarget = false;

                var ringObject = new GameObject("AlertRing", typeof(RectTransform), typeof(Image));
                var ringRect = (RectTransform)ringObject.transform;
                ringRect.SetParent(rect, false);
                ringRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                AvMarkerPlace(ringRect);
                Image ring = ringObject.GetComponent<Image>();
                ring.raycastTarget = false;

                pool.Add(new Marker
                {
                    Root = root,
                    Rect = rect,
                    Diamond = diamond,
                    Core = core,
                    Ring = ringRect,
                    RingImage = ring,
                });
            }
            return pool[index];
        }

        private static void AvMarkerPlace(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }
}
