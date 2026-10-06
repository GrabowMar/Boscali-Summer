using NOAvionics;
using System.Collections.Generic;
using BoscaliSummer.Modules.Comms.Configuration;
using BoscaliSummer.Modules.Comms.Domain;
using BoscaliSummer.Modules.Comms.Runtime;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Comms.Presentation
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
        private const float CaptionWidth = 260f, CaptionHeight = 44f, CaptionGap = 6f;

        private CommsSettings settings;
        private CommsManager manager;
        private GameObject root;
        private RectTransform canvasRect;
        private Marker[] markers;
        private readonly List<CommsItem> pings = new List<CommsItem>(16);
        private readonly Rect[] captions = new Rect[MaxMarkers];
        private readonly Vector2[] positions = new Vector2[MaxMarkers];
        private readonly float[] bearings = new float[MaxMarkers];
        private readonly bool[] projected = new bool[MaxMarkers], pinned = new bool[MaxMarkers];
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
                if (float.IsNaN(item.Height)) item.Height = GroundHeight(item.X, item.Z);
                pings.Add(item);
            }
        }

        /// <summary>
        /// The ground (or deck) under a map point, in global metres: looked up once per ping,
        /// the way the OPS call-ins find theirs. A miss on terrain that is not loaded yet
        /// answers sea level for now and leaves the ping to be asked again.
        /// </summary>
        private static float GroundHeight(float x, float z)
        {
            Vector3 local = new GlobalPosition(x, 0f, z).ToLocalPosition();
            Vector3 origin = new Vector3(local.x, Mathf.Max(local.y, Datum.LocalSeaY) + 8000f, local.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 16000f,
                (int)PhysicsLayers.StaticsMask | (int)PhysicsLayers.ShipsMask))
                return (float)hit.point.ToGlobalPosition().y;
            return float.NaN;
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
            float minimumFontSize = AvTypeScale.Floor / Mathf.Max(.1f, canvasRect.GetComponent<Canvas>().scaleFactor);
            int captionCount = 0;
            // Reserve every fixed glyph before placing any caption, including older calls.
            for (int i = 0; i < markers.Length; i++)
            {
                projected[i] = false;
                if (i >= pings.Count) continue;
                CommsItem ping = pings[i];
                Vector3 target = new GlobalPosition(ping.X, float.IsNaN(ping.Height) ? 0f : ping.Height, ping.Z).ToLocalPosition();
                projected[i] = Project(camera, target, halfWidth, halfHeight, out positions[i], out bearings[i], out pinned[i]);
            }
            for (int i = 0; i < markers.Length; i++)
            {
                if (!projected[i])
                {
                    markers[i].SetVisible(false);
                    continue;
                }
                CommsItem ping = pings[i];
                PingKind kind = CommsCatalog.Pings[ping.Style];
                float range = Scalar.Distance2D(ping.X, ping.Z, self.x, self.z);
                string who = ping.Author == manager.LocalId ? "YOU" : ping.AuthorName;
                float fade = Mathf.Clamp01((ping.Expires - now) / 5f);
                Color colour = CommsMesh.Tone(ping.Faction == manager.LocalFaction ? kind.Tone : CommsTone.Caution);
                colour.a *= fade;
                string code = ping.IsCall && !string.IsNullOrEmpty(ping.Text) ? ping.Text : kind.Code;
                Vector2 at = positions[i];
                Vector2 captionAt = PlaceCaption(at, captionCount);
                captions[captionCount++] = CaptionBounds(captionAt);
                markers[i].Show(at, captionAt - at, kind.Glyph, code + " · " + CommsText.Distance(range, metric) + "\n" + who,
                    colour, pinned[i], bearings[i], minimumFontSize);
            }
        }

        // Keep glyphs on their exact projected positions. Only captions move: newest first,
        // within the screen, with a short leader when older calls share the same sightline.
        private Vector2 PlaceCaption(Vector2 at, int count)
        {
            Rect screen = canvasRect.rect;
            Vector2 desired = new Vector2(Mathf.Clamp(at.x, screen.xMin + CaptionWidth * .5f + 8f,
                screen.xMax - CaptionWidth * .5f - 8f), at.y - 24f);
            Vector2 candidate = desired;
            for (int step = 0; step <= MaxMarkers * 4; step++)
            {
                int row = step <= MaxMarkers * 2 ? step : -(step - MaxMarkers * 2);
                candidate.y = Mathf.Clamp(desired.y - row * (CaptionHeight + CaptionGap),
                    screen.yMin + CaptionHeight + 8f, screen.yMax - 8f);
                Rect box = CaptionBounds(candidate);
                box.yMin -= CaptionGap; box.yMax += CaptionGap;
                bool clear = true;
                for (int i = 0; i < projected.Length; i++)
                {
                    if (!projected[i]) continue;
                    if (box.Overlaps(new Rect(positions[i].x - 18f, positions[i].y - 18f, 36f, 36f)))
                    { clear = false; break; }
                    if (!pinned[i]) continue;
                    Vector2 arrowAt = positions[i] + new Vector2(Mathf.Cos(bearings[i] * Mathf.Deg2Rad), Mathf.Sin(bearings[i] * Mathf.Deg2Rad)) * 26f;
                    if (box.Overlaps(new Rect(arrowAt.x - 14f, arrowAt.y - 14f, 28f, 28f)))
                    { clear = false; break; }
                }
                for (int i = 0; i < count; i++)
                    if (box.Overlaps(captions[i])) { clear = false; break; }
                if (clear) break;
            }
            return candidate;
        }

        private static Rect CaptionBounds(Vector2 top) =>
            new Rect(top.x - CaptionWidth * .5f, top.y - CaptionHeight, CaptionWidth, CaptionHeight);

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
            bool nativeStyle = VanillaHudStyle.TryCockpit(out VanillaHudStyle.CockpitStyle style);
            TMP_FontAsset font = nativeStyle ? style.Font : AvType.VanillaFallback;
            markers = new Marker[MaxMarkers];
            for (int i = 0; i < markers.Length; i++) markers[i] = new Marker(canvasRect, font, nativeStyle ? style.FontMaterial : null);
        }

        /// <summary>One projected ping: its glyph, a caption, and an edge arrow when pinned.</summary>
        private sealed class Marker
        {
            private readonly RectTransform root;
            private readonly CommsGlyphGraphic glyph;
            private readonly CommsGlyphGraphic arrow;
            private readonly Image leader, captionBack;
            private readonly TMP_Text label;
            private readonly float fontSize;
            private string text;

            public Marker(RectTransform parent, TMP_FontAsset font, Material fontMaterial)
            {
                var go = new GameObject("CommsMarker", typeof(RectTransform));
                root = (RectTransform)go.transform;
                root.SetParent(parent, false);
                root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
                root.pivot = new Vector2(0.5f, 0.5f);
                root.sizeDelta = new Vector2(24f, 24f);

                leader = AvLay.Solid(parent, "Caption leader", Color.clear);
                leader.raycastTarget = false;
                leader.rectTransform.anchorMin = leader.rectTransform.anchorMax = leader.rectTransform.pivot = new Vector2(.5f, .5f);
                leader.rectTransform.SetAsFirstSibling(); // every caption paints over every leader
                leader.enabled = false;
                glyph = Glyph(root, "Glyph", 24f);
                arrow = Glyph(root, "Arrow", 18f);

                captionBack = AvLay.Solid(root, "Caption backing", Color.clear);
                captionBack.raycastTarget = false;
                captionBack.rectTransform.anchorMin = captionBack.rectTransform.anchorMax = new Vector2(.5f, .5f);
                captionBack.rectTransform.pivot = new Vector2(.5f, 1f);
                captionBack.rectTransform.sizeDelta = new Vector2(CaptionWidth, CaptionHeight);
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                var labelRect = (RectTransform)labelGo.transform;
                labelRect.SetParent(root, false);
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
                labelRect.pivot = new Vector2(0.5f, 1f);
                labelRect.sizeDelta = new Vector2(CaptionWidth - 12f, CaptionHeight - 4f);
                labelRect.anchoredPosition = new Vector2(0f, -15f);
                label = labelGo.GetComponent<TextMeshProUGUI>();
                AvType.Apply(label, AvTextRole.Data);
                fontSize = label.fontSize;
                if (font != null) label.font = font;
                if (fontMaterial != null) label.fontSharedMaterial = fontMaterial;
                label.alignment = TextAlignmentOptions.Top;
                label.enableWordWrapping = false;
                label.richText = false;
                label.raycastTarget = false;
                go.SetActive(false);
            }

            public void Show(Vector2 at, Vector2 captionOffset, string kind, string caption, Color colour, bool clamped, float bearing, float minimumFontSize)
            {
                if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
                root.anchoredPosition = at;
                captionBack.rectTransform.anchoredPosition = captionOffset;
                Color back = CommsMesh.Under; back.a = colour.a * (230f / 255f); captionBack.color = back;
                label.rectTransform.anchoredPosition = captionOffset + new Vector2(0f, -2f);
                bool moved = (captionOffset - new Vector2(0f, -24f)).sqrMagnitude > 4f;
                leader.enabled = moved;
                if (moved)
                {
                    Vector2 start = new Vector2(0f, captionOffset.y > 0f ? 15f : -15f);
                    Vector2 end = captionOffset + (captionOffset.y > 0f ? new Vector2(0f, -CaptionHeight) : Vector2.zero);
                    Vector2 line = end - start;
                    leader.rectTransform.anchoredPosition = at + (start + end) * .5f;
                    leader.rectTransform.sizeDelta = new Vector2(line.magnitude, 1f);
                    leader.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(line.y, line.x) * Mathf.Rad2Deg);
                    Color ink = colour; ink.a *= .65f; leader.color = ink;
                }
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
                label.fontSize = Mathf.Max(fontSize, minimumFontSize);
            }

            public void SetVisible(bool visible)
            {
                if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
                if (!visible) leader.enabled = false;
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
