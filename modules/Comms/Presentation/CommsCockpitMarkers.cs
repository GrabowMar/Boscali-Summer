using System.Collections.Generic;
using BoscaliSummer.Features.Comms.Configuration;
using BoscaliSummer.Features.Comms.Domain;
using BoscaliSummer.Features.Comms.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Comms.Presentation
{
    /// <summary>
    /// Pings, projected into the cockpit. A teammate's "SAM THREAT" is worth most when it can
    /// be found without opening the map, so each live ping gets a small marker on its ground
    /// position with its kind, who called it and the range; a ping behind or off screen is
    /// pinned to the frame edge with an arrow pointing the way. Drawn on its own overlay
    /// canvas in the vanilla HUD font; nothing vanilla is touched.
    /// </summary>
    internal sealed class CommsCockpitMarkers : MonoBehaviour, ISceneService
    {
        private const int MaxMarkers = 6;
        private const float EdgeMarginX = 150f;
        private const float EdgeMarginY = 120f;
        private const float ContentSeconds = 0.25f;

        private CommsSettings settings;
        private CommsManager manager;
        private GameObject root;
        private RectTransform canvasRect;
        private Marker[] markers;
        private readonly List<CommsItem> pings = new List<CommsItem>(16);
        private float nextContent;

        public void Configure(CommsSettings config, CommsManager owner)
        {
            settings = config;
            manager = owner;
        }

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null;
            canvasRect = null;
            markers = null;
            pings.Clear();
            nextContent = 0f;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (settings == null || manager == null || !settings.Enabled.Value || !settings.CockpitPingMarkers.Value ||
                Application.isBatchMode || DynamicMap.mapMaximized ||
                !GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null || aircraft.disabled)
            {
                Hide();
                return;
            }

            if (Time.unscaledTime >= nextContent)
            {
                nextContent = Time.unscaledTime + ContentSeconds;
                Collect();
            }
            if (pings.Count == 0)
            {
                Hide();
                return;
            }

            Camera camera = SceneSingleton<CameraStateManager>.i != null
                ? SceneSingleton<CameraStateManager>.i.mainCamera
                : Camera.main;
            if (camera == null)
            {
                Hide();
                return;
            }
            if (root == null) Build();
            if (root == null) return;

            Vector3 self = aircraft.transform.position.ToGlobalPosition().AsVector3();
            Render(camera, self);
        }

        /// <summary>The newest live pings from players who are not muted, newest first.</summary>
        private void Collect()
        {
            pings.Clear();
            CommsClientState state = manager.State;
            IReadOnlyList<CommsItem> items = state.Board.Items;
            float now = Time.unscaledTime;
            for (int i = items.Count - 1; i >= 0 && pings.Count < MaxMarkers; i--)
            {
                CommsItem item = items[i];
                if (item.Kind != CommsItemKind.Ping || item.Expires <= now || state.IsMuted(item.Author) ||
                    !CommsCatalog.ValidPing(item.Style)) continue;
                pings.Add(item);
            }
        }

        private void Render(Camera camera, Vector3 self)
        {
            float halfWidth = canvasRect.rect.width * 0.5f - EdgeMarginX;
            float halfHeight = canvasRect.rect.height * 0.5f - EdgeMarginY;
            if (halfWidth < 1f || halfHeight < 1f)
            {
                Hide();
                return;
            }

            float now = Time.unscaledTime;
            bool metric = VanillaHudStyle.Metric;
            for (int i = 0; i < markers.Length; i++)
            {
                if (i >= pings.Count)
                {
                    markers[i].SetVisible(false);
                    continue;
                }
                CommsItem ping = pings[i];
                var target = new GlobalPosition(ping.X, 0f, ping.Z);
                if (!Project(camera, target.ToLocalPosition(), halfWidth, halfHeight, out Vector2 at, out float bearing, out bool clamped))
                {
                    markers[i].SetVisible(false);
                    continue;
                }

                PingKind kind = CommsCatalog.Pings[ping.Style];
                float dx = ping.X - self.x, dz = ping.Z - self.z;
                float range = Mathf.Sqrt(dx * dx + dz * dz);
                string who = ping.Author == manager.LocalId ? "YOU" : ping.AuthorName;
                float fade = Mathf.Clamp01((ping.Expires - now) / 5f);
                Color colour = CommsMesh.Tone(ping.Faction == manager.LocalFaction ? kind.Tone : CommsTone.Caution);
                colour.a *= fade;
                markers[i].Show(at, kind.Glyph, kind.Code + " · " + who + " · " + CommsText.Distance(range, metric),
                    colour, clamped, bearing);
            }
        }

        /// <summary>Screen position in canvas units, clamped to the frame when off screen or behind.</summary>
        private bool Project(Camera camera, Vector3 world, float halfWidth, float halfHeight,
            out Vector2 at, out float bearing, out bool clamped)
        {
            at = Vector2.zero;
            bearing = 0f;
            clamped = false;
            Vector3 view = camera.WorldToScreenPoint(world);
            Vector2 local;
            if (view.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, view, null, out local) &&
                Mathf.Abs(local.x) <= halfWidth && Mathf.Abs(local.y) <= halfHeight)
            {
                at = local;
                return true;
            }

            Vector3 direction = camera.transform.InverseTransformPoint(world);
            Vector2 flat = new Vector2(direction.x, direction.y);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector2.down;
            flat.Normalize();
            float scale = Mathf.Min(
                Mathf.Abs(flat.x) > 1e-5f ? halfWidth / Mathf.Abs(flat.x) : float.MaxValue,
                Mathf.Abs(flat.y) > 1e-5f ? halfHeight / Mathf.Abs(flat.y) : float.MaxValue);
            if (float.IsInfinity(scale) || scale > 1e6f) return false;
            at = flat * scale;
            bearing = Mathf.Atan2(flat.y, flat.x) * Mathf.Rad2Deg;
            clamped = true;
            return true;
        }

        private void Hide()
        {
            if (markers == null) return;
            for (int i = 0; i < markers.Length; i++) markers[i].SetVisible(false);
        }

        private void Build()
        {
            root = new GameObject("Boscali Comms Markers", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1;
            canvas.pixelPerfect = false;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            canvasRect = (RectTransform)root.transform;
            TMP_FontAsset font = VanillaHudStyle.TryCockpit(out VanillaHudStyle.CockpitStyle style) ? style.Font : AvFont.Font;
            markers = new Marker[MaxMarkers];
            for (int i = 0; i < markers.Length; i++) markers[i] = new Marker(canvasRect, font);
        }

        /// <summary>One projected ping: its glyph, a caption, and an edge arrow when pinned.</summary>
        private sealed class Marker
        {
            private readonly RectTransform root;
            private readonly CommsGlyphGraphic glyph;
            private readonly CommsGlyphGraphic arrow;
            private readonly TMP_Text label;
            private string text;

            public Marker(RectTransform parent, TMP_FontAsset font)
            {
                var go = new GameObject("CommsMarker", typeof(RectTransform));
                root = (RectTransform)go.transform;
                root.SetParent(parent, false);
                root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
                root.pivot = new Vector2(0.5f, 0.5f);
                root.sizeDelta = new Vector2(24f, 24f);

                glyph = Glyph(root, "Glyph", 24f);
                arrow = Glyph(root, "Arrow", 18f);

                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                var labelRect = (RectTransform)labelGo.transform;
                labelRect.SetParent(root, false);
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
                labelRect.pivot = new Vector2(0.5f, 1f);
                labelRect.sizeDelta = new Vector2(320f, 20f);
                labelRect.anchoredPosition = new Vector2(0f, -15f);
                label = labelGo.GetComponent<TextMeshProUGUI>();
                if (font != null) label.font = font;
                label.fontSize = 14f;
                label.alignment = TextAlignmentOptions.Top;
                label.enableWordWrapping = false;
                label.richText = false;
                label.raycastTarget = false;
                go.SetActive(false);
            }

            public void Show(Vector2 at, string kind, string caption, Color colour, bool clamped, float bearing)
            {
                if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
                root.anchoredPosition = at;
                glyph.Set(kind, colour, 1.4f, underStroke: true);
                if (arrow.gameObject.activeSelf != clamped) arrow.gameObject.SetActive(clamped);
                if (clamped)
                {
                    // The arrow glyph points up-right at rest; turn it onto the bearing.
                    arrow.Set("arrow", colour, 1.4f, underStroke: true);
                    arrow.SetRotation(bearing - 45f);
                    Vector2 outward = new Vector2(Mathf.Cos(bearing * Mathf.Deg2Rad), Mathf.Sin(bearing * Mathf.Deg2Rad));
                    arrow.rectTransform.anchoredPosition = outward * 26f;
                }
                if (text != caption)
                {
                    text = caption;
                    label.text = caption;
                }
                label.color = colour;
            }

            public void SetVisible(bool visible)
            {
                if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
            }

            private static CommsGlyphGraphic Glyph(RectTransform parent, string name, float size)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(CommsGlyphGraphic));
                var rect = (RectTransform)go.transform;
                rect.SetParent(parent, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(size, size);
                CommsGlyphGraphic graphic = go.GetComponent<CommsGlyphGraphic>();
                graphic.raycastTarget = false;
                return graphic;
            }
        }
    }
}
