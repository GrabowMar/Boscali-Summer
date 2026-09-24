using System.Collections.Generic;
using BepInEx.Logging;
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
    /// The shared comms on the tactical map. Everything is parented to the map image, so it
    /// pans and zooms with the terrain, and drawn in map-local units (world metres times the
    /// map's display factor), so a circle drawn around a SAM site stays around it at any zoom.
    /// Strokes and glyphs are counter-scaled to a constant screen width, like the trench and
    /// front-line layers beside them.
    ///
    /// <para>Two meshes: <see cref="CommsInkGraphic"/> holds what rarely changes (drawings,
    /// stickers, labels, hunt reveals) and rebuilds on a board revision or a zoom step;
    /// <see cref="CommsPulseGraphic"/> holds what moves (fresh pings breathing, the stroke
    /// under the pen, the ruler) and rebuilds every frame only while something is moving.
    /// Neither takes raycasts: the map's own icons stay clickable through the ink.</para>
    /// </summary>
    internal sealed class CommsMapLayer : MonoBehaviour, ISceneService
    {
        private const int MaxTags = 48;
        private const float TagRefreshSeconds = 0.25f;

        private CommsSettings settings;
        private CommsManager manager;
        private ManualLogSource logger;

        private DynamicMap map;
        private GameObject root;
        private CommsInkGraphic ink;
        private CommsPulseGraphic pulse;
        private readonly List<CommsMapTag> tags = new List<CommsMapTag>(MaxTags);
        private int usedTags;

        private int lastRevision = -1;
        private int lastBoardRevision = -1;
        private float lastZoom = -1f;
        private float nextTags;

        public void Configure(CommsSettings config, CommsManager owner, ManualLogSource log)
        {
            settings = config;
            manager = owner;
            logger = log;
        }

        public void ResetForScene()
        {
            if (root != null) Destroy(root);
            root = null;
            ink = null;
            pulse = null;
            map = null;
            tags.Clear();
            usedTags = 0;
            lastRevision = lastBoardRevision = -1;
            lastZoom = -1f;
            nextTags = 0f;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (settings == null || manager == null) return;
            bool wanted = settings.Enabled.Value && settings.ShowOnMap.Value && !Application.isBatchMode;
            if (!wanted)
            {
                if (root != null && root.activeSelf) root.SetActive(false);
                return;
            }

            if (root == null && !TryBuild()) return;
            bool open = DynamicMap.mapMaximized && map != null && map.mapImage != null;
            if (root.activeSelf != open) root.SetActive(open);
            if (!open) return;

            float zoom = Mathf.Abs(map.mapImage.transform.localScale.x);
            float factor = map.mapDisplayFactor;
            if (!(zoom > 1e-4f) || !(factor > 1e-6f)) return;
            bool zoomed = Mathf.Abs(zoom - lastZoom) > Mathf.Max(0.0005f, lastZoom * 0.01f);

            CommsClientState state = manager.State;
            if (zoomed || state.Revision != lastRevision || state.Board.Revision != lastBoardRevision)
            {
                lastRevision = state.Revision;
                lastBoardRevision = state.Board.Revision;
                lastZoom = zoom;
                ink.SetVerticesDirty();
                nextTags = 0f;
            }
            if (pulse.Animating(Time.unscaledTime)) pulse.SetVerticesDirty();

            if (Time.unscaledTime >= nextTags)
            {
                nextTags = Time.unscaledTime + TagRefreshSeconds;
                LayoutTags(state, factor, zoom);
            }
        }

        private bool TryBuild()
        {
            map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.mapImage == null) return false;

            root = new GameObject("BoscaliComms.MapLayer", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(map.mapImage.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;

            ink = Layer<CommsInkGraphic>(rect, "Ink");
            ink.Bind(manager, map);
            pulse = Layer<CommsPulseGraphic>(rect, "Pulse");
            pulse.Bind(manager, map);

            // Comms read above the terrain overlays laid down before them. Command's front
            // line re-asserts itself as the last sibling, and that is fine: it is thin.
            root.transform.SetAsLastSibling();
            root.SetActive(DynamicMap.mapMaximized);
            logger?.LogInfo("[COMMS] Tactical map layer initialized.");
            return true;
        }

        private static T Layer<T>(RectTransform parent, string name) where T : MaskableGraphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            T graphic = go.GetComponent<T>();
            graphic.raycastTarget = false;
            return graphic;
        }

        // ---- Tags --------------------------------------------------------------------------

        /// <summary>Names under pings, the words of text labels, the ruler and the hunt reveal.</summary>
        private void LayoutTags(CommsClientState state, float factor, float zoom)
        {
            usedTags = 0;
            float inverse = 1f / zoom;
            float now = Time.unscaledTime;
            ulong self = manager.LocalId;
            int side = manager.LocalFaction;

            IReadOnlyList<CommsItem> items = state.Board.Items;
            for (int i = 0; i < items.Count && usedTags < MaxTags; i++)
            {
                CommsItem item = items[i];
                if (state.IsMuted(item.Author)) continue;
                Vector2 at = new Vector2(item.X * factor, item.Z * factor);
                bool enemy = item.Faction != side;
                if (item.Kind == CommsItemKind.Label)
                {
                    Tag(at, inverse, item.Text, CommsMesh.Tone(CommsTone.Info), new Vector2(0f, 12f), 13f, bold: true);
                }
                else if (item.Kind == CommsItemKind.Ping && CommsCatalog.ValidPing(item.Style))
                {
                    float fade = Mathf.Clamp01((item.Expires - now) / 5f);
                    string who = item.Author == self ? "YOU" : item.AuthorName;
                    string text = CommsCatalog.Pings[item.Style].Code + " · " + who + (enemy ? " (OTHER SIDE)" : "");
                    Color32 colour = enemy ? CommsMesh.Tone(CommsTone.Caution) : CommsMesh.Tone(CommsCatalog.Pings[item.Style].Tone);
                    Tag(at, inverse, text, CommsMesh.Fade(colour, fade), new Vector2(0f, -22f), 11f, bold: false);
                }
            }

            IReadOnlyList<HuntView> hunts = state.Hunts;
            for (int i = 0; i < hunts.Count && usedTags < MaxTags; i++)
            {
                HuntView hunt = hunts[i];
                if (hunt.Revealed)
                {
                    Tag(new Vector2(hunt.HiddenX * factor, hunt.HiddenZ * factor), inverse,
                        "HIDDEN BY " + hunt.AuthorName, CommsMesh.Tone(CommsTone.Fun), new Vector2(0f, 22f), 12f, bold: true);
                    for (int g = 0; g < hunt.Placings.Count && usedTags < MaxTags; g++)
                    {
                        HuntPlacingView placing = hunt.Placings[g];
                        Tag(new Vector2(placing.X * factor, placing.Z * factor), inverse,
                            "#" + (g + 1) + " " + placing.Name + " · " + CommsText.Distance(placing.Metres, VanillaHudStyle.Metric),
                            g == 0 ? CommsMesh.Tone(CommsTone.Fun) : CommsMesh.Tone(CommsTone.Info), new Vector2(0f, -16f), 11f, bold: g == 0);
                    }
                }
                else if (hunt.HasLocalGuess)
                {
                    Tag(new Vector2(hunt.LocalGuessX * factor, hunt.LocalGuessZ * factor), inverse,
                        hunt.Author == self ? "YOUR HIDDEN TARGET" : "YOUR GUESS", CommsMesh.Tone(CommsTone.Fun),
                        new Vector2(0f, -16f), 11f, bold: false);
                }
            }

            if (manager.HasMeasure && usedTags < MaxTags)
            {
                float dx = manager.MeasureBX - manager.MeasureAX, dz = manager.MeasureBZ - manager.MeasureAZ;
                float metres = Mathf.Sqrt(dx * dx + dz * dz);
                int bearing = CommsText.Bearing(manager.MeasureAX, manager.MeasureAZ, manager.MeasureBX, manager.MeasureBZ);
                Vector2 mid = new Vector2((manager.MeasureAX + manager.MeasureBX) * 0.5f * factor,
                                          (manager.MeasureAZ + manager.MeasureBZ) * 0.5f * factor);
                Tag(mid, inverse, CommsText.Distance(metres, VanillaHudStyle.Metric) + " · " + bearing.ToString("000") + "°",
                    CommsMesh.Tone(CommsTone.Caution), new Vector2(0f, 14f), 13f, bold: true);
            }

            for (int i = usedTags; i < tags.Count; i++) tags[i].SetVisible(false);
        }

        private void Tag(Vector2 at, float inverse, string text, Color colour, Vector2 offset, float size, bool bold)
        {
            if (string.IsNullOrEmpty(text) || root == null) return;
            if (usedTags >= tags.Count) tags.Add(new CommsMapTag((RectTransform)root.transform));
            CommsMapTag tag = tags[usedTags++];
            tag.Show(at, inverse, text, colour, offset, size, bold);
        }
    }

    /// <summary>One counter-scaled caption on the map, on a dark plate so it reads over snow and sea alike.</summary>
    internal sealed class CommsMapTag
    {
        private const float Width = 260f;
        private const float Height = 18f;

        private readonly RectTransform root;
        private readonly Image plate;
        private readonly TMP_Text label;
        private string text;

        public CommsMapTag(RectTransform parent)
        {
            var go = new GameObject("CommsTag", typeof(RectTransform));
            root = (RectTransform)go.transform;
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(Width, Height);

            plate = AvKit.Panel(root, new Rect(0f, 0f, Width, Height), new Color(0.02f, 0.03f, 0.04f, 0.62f));
            plate.rectTransform.anchorMin = plate.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            plate.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            plate.rectTransform.anchoredPosition = Vector2.zero;

            label = AvKit.Label(root, "", new Rect(0f, 0f, Width, Height), Color.white, 11f,
                FontStyles.Normal, TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.anchoredPosition = Vector2.zero;
            label.richText = false;
            label.raycastTarget = false;
            plate.raycastTarget = false;
            go.SetActive(false);
        }

        public void Show(Vector2 at, float inverse, string value, Color colour, Vector2 offset, float size, bool bold)
        {
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            root.localScale = new Vector3(inverse, inverse, 1f);
            root.anchoredPosition = at + offset * inverse;
            if (text != value)
            {
                text = value;
                label.text = value;
                label.fontSize = size;
                label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
                float width = Mathf.Min(Width, Mathf.Ceil(label.GetPreferredValues(value).x) + 10f);
                plate.rectTransform.sizeDelta = new Vector2(width, size + 5f);
            }
            label.color = colour;
        }

        public void SetVisible(bool visible)
        {
            if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        }
    }

    /// <summary>
    /// The settled ink: strokes, stickers, text-label anchors and hunt reveals. Rebuilt on a
    /// board or panel revision and on zoom steps, never per frame.
    /// </summary>
    internal sealed class CommsInkGraphic : MaskableGraphic
    {
        private const int MaxVertices = 60000;
        private const float UnderExtra = 1.1f;

        private CommsManager manager;
        private DynamicMap map;

        public void Bind(CommsManager owner, DynamicMap dynamicMap)
        {
            manager = owner;
            map = dynamicMap;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (manager == null || map == null || map.mapImage == null) return;
            float factor = map.mapDisplayFactor;
            float zoom = Mathf.Abs(map.mapImage.transform.localScale.x);
            if (!(factor > 1e-6f) || !(zoom > 1e-4f)) return;
            float px = 1f / zoom;

            CommsClientState state = manager.State;
            IReadOnlyList<CommsItem> items = state.Board.Items;

            // Strokes first so every glyph sits on top of the drawings.
            for (int i = 0; i < items.Count; i++)
            {
                CommsItem item = items[i];
                if (item.Kind != CommsItemKind.Stroke || state.IsMuted(item.Author)) continue;
                float half = CommsCatalog.PenWidths[item.Size < CommsCatalog.PenWidths.Length ? item.Size : 0] * px;
                Stroke(vh, item.Points, factor, half + UnderExtra * px, CommsMesh.Under);
                Stroke(vh, item.Points, factor, half, CommsMesh.Ink(item.Style, 235));
                if (vh.currentVertCount > MaxVertices) return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                CommsItem item = items[i];
                if (state.IsMuted(item.Author)) continue;
                Vector2 at = new Vector2(item.X * factor, item.Z * factor);
                if (item.Kind == CommsItemKind.Sticker && CommsCatalog.ValidSticker(item.Style))
                {
                    StickerKind sticker = CommsCatalog.Stickers[item.Style];
                    CommsMesh.Glyph(vh, sticker.Glyph, at, 13f * px, 1.35f * px, CommsMesh.Tone(sticker.Tone), under: true);
                }
                else if (item.Kind == CommsItemKind.Label)
                {
                    CommsMesh.Disc(vh, at, 3.2f * px, CommsMesh.Under);
                    CommsMesh.Disc(vh, at, 2.1f * px, CommsMesh.Tone(CommsTone.Info));
                }
            }

            IReadOnlyList<HuntView> hunts = state.Hunts;
            for (int i = 0; i < hunts.Count; i++)
            {
                HuntView hunt = hunts[i];
                if (!hunt.Revealed) continue;
                Vector2 hidden = new Vector2(hunt.HiddenX * factor, hunt.HiddenZ * factor);
                Color32 fun = CommsMesh.Tone(CommsTone.Fun);
                for (int g = 0; g < hunt.Placings.Count; g++)
                {
                    HuntPlacingView placing = hunt.Placings[g];
                    Vector2 guess = new Vector2(placing.X * factor, placing.Z * factor);
                    Color32 tone = g == 0 ? fun : CommsMesh.Tone(CommsTone.Info, 200);
                    CommsMesh.Dashed(vh, guess, hidden, 0.8f * px, 6f * px, 4f * px, CommsMesh.Fade(tone, 0.7f));
                    CommsMesh.Glyph(vh, "guess", guess, 7f * px, 1.1f * px, tone, under: true);
                }
                CommsMesh.Glyph(vh, "hunt", hidden, 14f * px, 1.4f * px, fun, under: true);
            }
        }

        private static void Stroke(VertexHelper vh, int[] points, float factor, float halfWidth, Color32 ink)
        {
            if (points == null || points.Length < 4) return;
            Vector2 previous = Local(points, 0, factor);
            for (int i = 2; i + 1 < points.Length; i += 2)
            {
                Vector2 next = Local(points, i, factor);
                CommsMesh.Segment(vh, previous, next, halfWidth, ink);
                previous = next;
            }
        }

        private static Vector2 Local(int[] points, int index, float factor) =>
            new Vector2(StrokeCodec.Restore(points[index]) * factor, StrokeCodec.Restore(points[index + 1]) * factor);
    }

    /// <summary>
    /// What moves: pings breathing for their first seconds and fading at the end, the stroke
    /// or shape under the pen, the ruler, a highlighted log position and this player's own
    /// hunt mark. Rebuilt each frame only while one of those is on the map.
    /// </summary>
    internal sealed class CommsPulseGraphic : MaskableGraphic
    {
        private const float PulseSeconds = 8f;
        private const float PulsePeriod = 1.4f;
        private const float FadeSeconds = 5f;

        private CommsManager manager;
        private DynamicMap map;
        private bool drewLastFrame;

        public void Bind(CommsManager owner, DynamicMap dynamicMap)
        {
            manager = owner;
            map = dynamicMap;
            SetVerticesDirty();
        }

        /// <summary>Whether this frame needs a rebuild: something animates, or last frame drew and may need clearing.</summary>
        public bool Animating(float now)
        {
            if (manager == null) return false;
            bool active = manager.PreviewActive || manager.HasMeasure || manager.HighlightUntil > now ||
                          manager.State.Board.CountOf(CommsItemKind.Ping) > 0 || AnyLocalHunt();
            bool rebuild = active || drewLastFrame;
            drewLastFrame = active;
            return rebuild;
        }

        private bool AnyLocalHunt()
        {
            IReadOnlyList<HuntView> hunts = manager.State.Hunts;
            for (int i = 0; i < hunts.Count; i++)
                if (!hunts[i].Revealed && hunts[i].HasLocalGuess) return true;
            return false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (manager == null || map == null || map.mapImage == null) return;
            float factor = map.mapDisplayFactor;
            float zoom = Mathf.Abs(map.mapImage.transform.localScale.x);
            if (!(factor > 1e-6f) || !(zoom > 1e-4f)) return;
            float px = 1f / zoom;
            float now = Time.unscaledTime;
            CommsClientState state = manager.State;

            IReadOnlyList<CommsItem> items = state.Board.Items;
            for (int i = 0; i < items.Count; i++)
            {
                CommsItem item = items[i];
                if (item.Kind != CommsItemKind.Ping || !CommsCatalog.ValidPing(item.Style) || state.IsMuted(item.Author)) continue;
                PingKind kind = CommsCatalog.Pings[item.Style];
                Vector2 at = new Vector2(item.X * factor, item.Z * factor);
                float fade = Mathf.Clamp01((item.Expires - now) / FadeSeconds);
                Color32 tone = CommsMesh.Fade(CommsMesh.Tone(kind.Tone), fade);
                float age = now - item.Created;
                if (age < PulseSeconds)
                {
                    float phase = (age % PulsePeriod) / PulsePeriod;
                    CommsMesh.Ring(vh, at, Mathf.Lerp(10f, 38f, phase) * px, 1.2f * px,
                        CommsMesh.Fade(tone, (1f - phase) * 0.9f), 28);
                }
                if (kind.Ring) CommsMesh.Ring(vh, at, 22f * px, 0.9f * px, CommsMesh.Fade(tone, 0.55f), 28);
                CommsMesh.Glyph(vh, kind.Glyph, at, 11f * px, 1.35f * px, tone, under: true);
            }

            IReadOnlyList<HuntView> hunts = state.Hunts;
            for (int i = 0; i < hunts.Count; i++)
            {
                HuntView hunt = hunts[i];
                if (hunt.Revealed || !hunt.HasLocalGuess) continue;
                Vector2 at = new Vector2(hunt.LocalGuessX * factor, hunt.LocalGuessZ * factor);
                float breathe = 0.65f + 0.35f * Mathf.Sin(now * 4f);
                CommsMesh.Glyph(vh, hunt.Author == manager.LocalId ? "hunt" : "guess", at, 10f * px, 1.2f * px,
                    CommsMesh.Fade(CommsMesh.Tone(CommsTone.Fun), breathe), under: true);
            }

            if (manager.PreviewActive) Preview(vh, factor, px);

            if (manager.HasMeasure)
            {
                Vector2 a = new Vector2(manager.MeasureAX * factor, manager.MeasureAZ * factor);
                Vector2 b = new Vector2(manager.MeasureBX * factor, manager.MeasureBZ * factor);
                Color32 caution = CommsMesh.Tone(CommsTone.Caution);
                CommsMesh.Segment(vh, a, b, 2.2f * px, CommsMesh.Under);
                CommsMesh.Dashed(vh, a, b, 1.1f * px, 8f * px, 4f * px, caution);
                CommsMesh.Disc(vh, a, 3f * px, caution);
                CommsMesh.Disc(vh, b, 3f * px, caution);
            }

            if (manager.HighlightUntil > now)
            {
                float left = manager.HighlightUntil - now;
                float phase = (left % 0.9f) / 0.9f;
                Vector2 at = new Vector2(manager.HighlightX * factor, manager.HighlightZ * factor);
                CommsMesh.Ring(vh, at, Mathf.Lerp(46f, 12f, phase) * px, 1.6f * px,
                    CommsMesh.Tone(CommsTone.Caution, (byte)(255 * phase)), 32);
            }
        }

        private void Preview(VertexHelper vh, float factor, float px)
        {
            Color32 ink = CommsMesh.Ink(manager.PenInk, 210);
            float half = CommsCatalog.PenWidths[Mathf.Clamp(manager.PenWidth, 0, CommsCatalog.PenWidths.Length - 1)] * px;
            Vector2 a = new Vector2(manager.PreviewAX * factor, manager.PreviewAZ * factor);
            Vector2 b = new Vector2(manager.PreviewBX * factor, manager.PreviewBZ * factor);
            switch (manager.PreviewTool)
            {
                case CommsTool.Pen:
                {
                    IReadOnlyList<float> pen = manager.PreviewPen;
                    for (int i = 2; i + 1 < pen.Count; i += 2)
                        CommsMesh.Segment(vh, new Vector2(pen[i - 2] * factor, pen[i - 1] * factor),
                            new Vector2(pen[i] * factor, pen[i + 1] * factor), half, ink);
                    break;
                }
                case CommsTool.Measure:
                    CommsMesh.Dashed(vh, a, b, 1.1f * px, 8f * px, 4f * px, CommsMesh.Tone(CommsTone.Caution));
                    break;
                default:
                {
                    CommsShape shape = manager.PreviewTool == CommsTool.Arrow ? CommsShape.Arrow
                        : manager.PreviewTool == CommsTool.Circle ? CommsShape.Circle
                        : manager.PreviewTool == CommsTool.Box ? CommsShape.Box
                        : CommsShape.Line;
                    float[][] strokes = CommsShapes.Build(shape, manager.PreviewAX, manager.PreviewAZ,
                        manager.PreviewBX, manager.PreviewBZ);
                    for (int s = 0; s < strokes.Length; s++)
                    {
                        float[] p = strokes[s];
                        for (int i = 2; i + 1 < p.Length; i += 2)
                            CommsMesh.Segment(vh, new Vector2(p[i - 2] * factor, p[i - 1] * factor),
                                new Vector2(p[i] * factor, p[i + 1] * factor), half, ink);
                    }
                    break;
                }
            }
        }
    }
}
