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
    /// <para>The post whose file is open on the console wears a bracket reticle and the
    /// selected ink, the way the map lifts an icon it has selected; the layer only reads
    /// <see cref="IHighCommandView.HighlightedId"/> and never sets it.</para>
    ///
    /// <para>Nothing here selects, orders or reveals anything: a post is drawn only when the
    /// view already lists it as friendly or known, so the map cannot show a post the roster
    /// is hiding.</para>
    /// </summary>
    internal sealed class CommandPostMarkers : MonoBehaviour, ISceneService
    {
        private const int MaximumMarkers = CommandSnapshotRules.MaximumNodes;

        private const float SelectedInk = 0.4f;

        private sealed class Marker
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Diamond;
            public Image Core;
            public RectTransform Ring;
            public Image RingImage;
            public RectTransform Reticle;
            public Image[] ReticleRules;
            public float ReticleBuiltFor;
            public Color ReticleInk;
            public bool Alert;
            public bool Selected;
            public bool WasSelected;
        }

        private static readonly Color MarkerAlert = new Color(0.937f, 0.267f, 0.267f, 1f);

        private readonly List<Marker> pool = new List<Marker>(MaximumMarkers);

        private HighCommandSettings settings;
        private IHighCommandView view;
        private Transform layer;
        private RectTransform layerRoot;

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
            if (layerRoot != null) Destroy(layerRoot.gameObject);
            layerRoot = null;
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
            if (map == null || map.mapImage == null)
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
            float displayFactor = map.mapDisplayFactor;
            if (float.IsNaN(inverseScale) || float.IsInfinity(inverseScale) || inverseScale <= 0f)
            {
                HideFrom(0);
                return;
            }

            layer = EnsureLayer(map);
            if (layer == null)
            {
                HideFrom(0);
                return;
            }
            Color friendly = map.HQ != null && map.HQ.faction != null ? map.HQ.faction.color : Color.white;
            float pulse = CommandMarkerPolicy.Pulse(Time.unscaledTime * 2f);
            int highlighted = view.HighlightedId;

            int drawn = 0;
            for (int i = 0; i < commanders.Count && drawn < MaximumMarkers; i++)
            {
                CommanderView commander = commanders[i];
                if (commander == null ||
                    !CommandMarkerPolicy.Show(commander.IsFriendly, commander.IsKnown, commander.IsKia))
                    continue;

                Marker marker = Take(drawn);
                if (!CommandMarkerPolicy.MapPoint(commander.X, commander.Z, displayFactor,
                        out float mapX, out float mapZ))
                    continue;
                float size = CommandMarkerPolicy.Size(commander.Tier);
                // Only when the layer is not the parent already: re-parenting every frame
                // would rewrite the sibling order the selected marker is raised in.
                if (marker.Root.transform.parent != layer) marker.Root.transform.SetParent(layer, false);
                marker.Rect.localScale = Vector3.one * inverseScale;
                marker.Rect.localPosition = new Vector3(mapX, mapZ, 0f);
                marker.Rect.sizeDelta = new Vector2(size, size);

                // The selected post is drawn in the faction's ink lifted towards white - the
                // map's own way of saying "this icon" - and its bracket sits above the others.
                marker.Selected = commander.Id == highlighted;
                Color tone = Color.Lerp(commander.IsFriendly ? friendly : MarkerAlert, Color.white,
                                        marker.Selected ? SelectedInk : 0f);
                tone.a = marker.Selected ? 1f : commander.InTransit ? 0.65f : 0.92f;
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

                if (marker.Selected && !marker.WasSelected) marker.Root.transform.SetAsLastSibling();
                marker.WasSelected = marker.Selected;
                if (marker.Reticle.gameObject.activeSelf != marker.Selected)
                    marker.Reticle.gameObject.SetActive(marker.Selected);
                if (marker.Selected) PlaceReticle(marker, size, tone);

                if (!marker.Root.activeSelf) marker.Root.SetActive(true);
                drawn++;
            }

            HideFrom(drawn);
        }

        /// <summary>
        /// The markers' own container under the map image, kept as the last child so a post is
        /// never buried under the vanilla icon layer or the support overlay, which both live
        /// under the same image.
        /// </summary>
        private Transform EnsureLayer(DynamicMap map)
        {
            Transform parent = map.mapImage.transform;
            if (layerRoot == null)
            {
                var root = new GameObject("BoscaliCommandPostLayer", typeof(RectTransform));
                layerRoot = (RectTransform)root.transform;
                layerRoot.SetParent(parent, false);
                layerRoot.anchorMin = layerRoot.anchorMax = new Vector2(0.5f, 0.5f);
                layerRoot.pivot = new Vector2(0.5f, 0.5f);
                layerRoot.anchoredPosition = Vector2.zero;
                layerRoot.localScale = Vector3.one;
            }
            else if (layerRoot.parent != parent)
            {
                layerRoot.SetParent(parent, false);
            }
            if (layerRoot.GetSiblingIndex() != parent.childCount - 1) layerRoot.SetAsLastSibling();
            return layerRoot;
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

                // The selection bracket: a centred group, one rule per arm, hidden until the
                // post is the one the console has open. Anchored to the middle so the marker's
                // own size can change under it without moving it.
                var reticleObject = new GameObject("SelectedReticle", typeof(RectTransform));
                var reticle = (RectTransform)reticleObject.transform;
                reticle.SetParent(rect, false);
                reticle.anchorMin = reticle.anchorMax = new Vector2(0.5f, 0.5f);
                reticle.pivot = new Vector2(0.5f, 0.5f);
                reticle.anchoredPosition = Vector2.zero;
                var reticleRules = new Image[8];
                for (int i = 0; i < reticleRules.Length; i++)
                {
                    var ruleObject = new GameObject("Bracket", typeof(RectTransform), typeof(Image));
                    var ruleRect = (RectTransform)ruleObject.transform;
                    ruleRect.SetParent(reticle, false);
                    Image rule = ruleObject.GetComponent<Image>();
                    rule.raycastTarget = false;
                    reticleRules[i] = rule;
                }
                reticleObject.SetActive(false);

                pool.Add(new Marker
                {
                    Root = root,
                    Rect = rect,
                    Diamond = diamond,
                    Core = core,
                    Ring = ringRect,
                    RingImage = ring,
                    Reticle = reticle,
                    ReticleRules = reticleRules,
                    ReticleBuiltFor = float.NaN,
                });
            }
            return pool[index];
        }

        /// <summary>
        /// Lay the bracket's rules for the marker's own size, once per size or ink: the map
        /// runs at 60 Hz and the geometry only changes when the post or its tier does.
        /// </summary>
        private static void PlaceReticle(Marker marker, float size, Color ink)
        {
            if (marker.ReticleBuiltFor != size)
            {
                CommandMarkerPolicy.ReticleRule[] rules = CommandMarkerPolicy.Reticle(size);
                float bracket = CommandMarkerPolicy.ReticleBracket(size);
                marker.Reticle.sizeDelta = new Vector2(bracket, bracket);
                for (int i = 0; i < rules.Length && i < marker.ReticleRules.Length; i++)
                    PlaceRule((RectTransform)marker.ReticleRules[i].transform, rules[i]);
                marker.ReticleBuiltFor = size;
            }
            if (marker.ReticleInk != ink)
            {
                marker.ReticleInk = ink;
                for (int i = 0; i < marker.ReticleRules.Length; i++) marker.ReticleRules[i].color = ink;
            }
        }

        private static void PlaceRule(RectTransform rect, CommandMarkerPolicy.ReticleRule rule)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(rule.X, rule.Y);
            rect.sizeDelta = new Vector2(rule.Width, rule.Height);
            rect.localScale = Vector3.one;
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
