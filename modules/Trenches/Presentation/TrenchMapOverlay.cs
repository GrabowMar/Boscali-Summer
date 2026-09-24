using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Trenches.Configuration;
using BoscaliSummer.Features.Trenches.Domain;
using BoscaliSummer.Features.Trenches.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Trenches.Presentation
{
    /// <summary>
    /// Hosts the trench belt's tactical map layer. The layer is a mesh pass over the drawn
    /// curves (<see cref="TrenchMapGraphic"/>), not a baked texture, and it is active only
    /// while the map is maximized: the cockpit HUD and MFD surfaces never see it.
    /// </summary>
    internal sealed class TrenchMapOverlay : MonoBehaviour, ISceneService
    {
        private TrenchesSettings settings;
        private TrenchManager trenchManager;
        private ManualLogSource logger;

        private DynamicMap dynamicMap;
        private GameObject layerObject;
        private TrenchMapGraphic layer;

        private bool initialized;
        private bool isMapMaximized;
        private float lastZoom = -1f;
        private FactionHQ lastHq;

        public void Configure(TrenchesSettings config, TrenchManager manager, ManualLogSource log)
        {
            settings = config;
            trenchManager = manager;
            logger = log;

            if (trenchManager != null)
            {
                trenchManager.OnLinesChanged += HandleLinesChanged;
            }
        }

        public void ResetForScene()
        {
            if (layerObject != null)
            {
                Destroy(layerObject);
                layerObject = null;
            }
            layer = null;
            dynamicMap = null;
            initialized = false;
            isMapMaximized = false;
            lastZoom = -1f;
            lastHq = null;
        }

        private void OnDestroy()
        {
            if (trenchManager != null)
            {
                trenchManager.OnLinesChanged -= HandleLinesChanged;
            }
            ResetForScene();
        }

        private void HandleLinesChanged()
        {
            if (isMapMaximized && layer != null)
            {
                layer.SetVerticesDirty();
            }
        }

        private void Update()
        {
            if (settings == null || !settings.Enabled.Value || !settings.ShowOnTacticalMap.Value)
            {
                if (layerObject != null) layerObject.SetActive(false);
                return;
            }

            if (!initialized)
            {
                TryInitialize();
                return;
            }

            if (dynamicMap == null) return;

            bool currentMaximized = DynamicMap.mapMaximized;
            if (layerObject != null && layerObject.activeSelf != currentMaximized)
            {
                layerObject.SetActive(currentMaximized);
                if (currentMaximized && layer != null) layer.SetVerticesDirty();
            }
            isMapMaximized = currentMaximized;
            if (!currentMaximized) return;

            // The mesh is cut in map-local units at a constant screen width, so a zoom step
            // and a faction change are the two things that must re-ink it.
            if (dynamicMap.mapImage != null)
            {
                float zoom = Mathf.Abs(dynamicMap.mapImage.transform.localScale.x);
                if (Mathf.Abs(zoom - lastZoom) > Mathf.Max(0.0005f, Mathf.Abs(lastZoom) * 0.01f))
                {
                    lastZoom = zoom;
                    if (layer != null) layer.SetVerticesDirty();
                }
            }
            if (dynamicMap.HQ != lastHq)
            {
                lastHq = dynamicMap.HQ;
                if (layer != null) layer.SetVerticesDirty();
            }
        }

        private void TryInitialize()
        {
            dynamicMap = SceneSingleton<DynamicMap>.i;
            if (dynamicMap == null || dynamicMap.mapImage == null) return;

            RectTransform mapImageRect = dynamicMap.mapImage.GetComponent<RectTransform>();
            if (mapImageRect == null) return;

            layerObject = new GameObject("TrenchMapLayer", typeof(RectTransform));
            layerObject.transform.SetParent(dynamicMap.mapImage.transform, false);

            RectTransform layerRect = layerObject.GetComponent<RectTransform>();
            layerRect.anchorMin = Vector2.zero;
            layerRect.anchorMax = Vector2.one;
            layerRect.offsetMin = Vector2.zero;
            layerRect.offsetMax = Vector2.zero;
            layerRect.pivot = new Vector2(0.5f, 0.5f);
            layerRect.localScale = Vector3.one;
            layerRect.localPosition = Vector3.zero;

            layer = layerObject.AddComponent<TrenchMapGraphic>();
            layer.raycastTarget = false;
            layer.SetSource(trenchManager, dynamicMap);

            // Command's own front line re-asserts itself as the last sibling; the trench
            // trace reads under it.
            layerObject.transform.SetAsLastSibling();

            lastZoom = Mathf.Abs(dynamicMap.mapImage.transform.localScale.x);
            lastHq = dynamicMap.HQ;
            initialized = true;
            isMapMaximized = DynamicMap.mapMaximized;
            layerObject.SetActive(isMapMaximized);
            logger?.LogInfo("[TRENCHES] Tactical map overlay initialized.");
        }
    }

    /// <summary>
    /// The trench belt as curves: one mesh pass over the fire line (solid, crenellated
    /// toward the enemy), the support and redoubt traces, the communication links and saps,
    /// a mark per strongpoint and a tick per growth stage. Everything is drawn in map-local
    /// units — world metres times DynamicMap's own display factor — and every stroke is
    /// counter-scaled to a constant screen width, so zooming magnifies the curves instead of
    /// pixelating a texture. Rebuilt on demand, never per frame while the map is closed.
    /// </summary>
    internal sealed class TrenchMapGraphic : MaskableGraphic
    {
        private const float FireHalfWidth = 1.7f;
        private const float RearHalfWidth = 1.0f;
        private const float LinkHalfWidth = 0.8f;
        private const float UnderExtra = 1.0f;
        private const float ToothSpacingPixels = 13f;
        private const float ToothLengthPixels = 6.5f;
        private const float ToothHalfWidthPixels = 1.1f;
        private const float MarkHalfSizePixels = 1.7f;
        private const float StepPixels = 2f;
        private const int MaximumSegments = 640;
        private const int MaximumSegmentsPerStroke = 200;
        private const int MaximumVertices = 12000;

        private static readonly Color32 Under = new Color32(10, 12, 16, 150);
        private static readonly Color32 FriendlyInk = new Color32(65, 210, 255, 235);
        private static readonly Color32 HostileInk = new Color32(255, 100, 80, 235);
        private static readonly Color32 SuppressedInk = new Color32(255, 195, 65, 235);
        private static readonly Color32 NeutralizedInk = new Color32(150, 150, 150, 190);

        private TrenchManager manager;
        private DynamicMap map;

        public void SetSource(TrenchManager source, DynamicMap dynamicMap)
        {
            manager = source;
            map = dynamicMap;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (manager == null || map == null || !isActiveAndEnabled) return;

            float factor = map.mapDisplayFactor;
            if (!(factor > 0.0001f)) return;

            float scale = Mathf.Abs(transform.lossyScale.x);
            if (!(scale > 1e-4f)) scale = 1f;
            float pixel = 1f / scale;

            IReadOnlyList<TrenchLine> lines = manager.Lines;

            // Zoomed in, the belt is far longer on screen than the segment budget. Widening
            // every step keeps all of it drawn, a little coarser, instead of spending the
            // budget on the first lines and dropping the rest.
            float step = Mathf.Max(pixel * StepPixels, Measure(lines, factor) / MaximumSegments);
            for (int i = 0; i < lines.Count; i++)
            {
                TrenchLine line = lines[i];
                if (line == null || line.Curve == null) continue;

                Color32 ink = InkFor(line);
                Color32 rear = ink;
                rear.a = 150;

                Stroke(vh, line.Curve, factor, step, pixel, FireHalfWidth, ink);
                Teeth(vh, line.Curve, line.Threat, factor, pixel, ink);
                Stroke(vh, line.Support, factor, step, pixel, RearHalfWidth, rear);
                Stroke(vh, line.Redoubt, factor, step, pixel, RearHalfWidth, rear);
                StrokeAll(vh, line.Links, factor, step, pixel, LinkHalfWidth, rear);
                StrokeAll(vh, line.Spurs, factor, step, pixel, LinkHalfWidth, rear);
                Marks(vh, line, factor, pixel, ink);
                States(vh, line, factor, pixel, ink);

                if (vh.currentVertCount > MaximumVertices) return;
            }
        }

        /// <summary>Total drawn length of every stroke, in map-local units.</summary>
        private static float Measure(IReadOnlyList<TrenchLine> lines, float factor)
        {
            float total = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                TrenchLine line = lines[i];
                if (line == null) continue;
                total += Length(line.Curve, factor);
                total += Length(line.Support, factor);
                total += Length(line.Redoubt, factor);
                total += LengthAll(line.Links, factor);
                total += LengthAll(line.Spurs, factor);
            }
            return total;
        }

        private static float Length(Vector3[] path, float factor)
        {
            if (path == null || path.Length < 2) return 0f;
            float total = 0f;
            for (int i = 1; i < path.Length; i++)
                total += (ToLocal(path[i], factor) - ToLocal(path[i - 1], factor)).magnitude;
            return total;
        }

        private static float LengthAll(Vector3[][] traces, float factor)
        {
            if (traces == null) return 0f;
            float total = 0f;
            for (int t = 0; t < traces.Length; t++) total += Length(traces[t], factor);
            return total;
        }

        private Color32 InkFor(TrenchLine line)
        {
            if (line.Overrun) return NeutralizedInk;
            if (line.Suppressed) return SuppressedInk;
            return line.OwnerHq != null && line.OwnerHq == map.HQ ? FriendlyInk : HostileInk;
        }

        /// <summary>Strongpoints: one per living defender, spread over the bays, plus the
        /// support centre, so a shot-up position visibly thins on the map.</summary>
        private static void Marks(VertexHelper vh, TrenchLine line, float factor, float pixel, Color32 ink)
        {
            int budget = Mathf.Clamp(line.DefenderCount, 0, TrenchTraceMath.DefenderBudget(line.Stage));
            Vector3[] nodes = line.Nodes;
            if (budget > 0 && nodes != null && nodes.Length > 0)
            {
                for (int slot = 0; slot < budget; slot++)
                    Mark(vh, nodes, TrenchTraceMath.NodeFraction(slot, budget), factor, pixel, ink);
            }
            else if (budget > 0)
            {
                Mark(vh, line.Anchors, 0.15f, factor, pixel, ink);
                Mark(vh, line.Anchors, 0.5f, factor, pixel, ink);
                Mark(vh, line.Anchors, 0.85f, factor, pixel, ink);
            }
            Mark(vh, line.SupportAnchors, 0.5f, factor, pixel, ink);
        }

        private static void Mark(VertexHelper vh, Vector3[] anchors, float fraction, float factor,
            float pixel, Color32 ink)
        {
            if (anchors == null || anchors.Length == 0) return;
            Vector3 anchor = anchors[Mathf.Clamp(Mathf.RoundToInt(fraction * (anchors.Length - 1)), 0, anchors.Length - 1)];
            AddRect(vh, ToLocal(anchor, factor), MarkHalfSizePixels * pixel, ink);
        }

        /// <summary>Stage ticks, or a crossed-out centre once the position has no defenders
        /// left; a cut-off line that still fights keeps its ticks under the neutralized ink.</summary>
        private static void States(VertexHelper vh, TrenchLine line, float factor, float pixel, Color32 ink)
        {
            Vector2 at = ToLocal(line.Center, factor);
            if (line.Overrun && line.DefenderCount <= 0)
            {
                float arm = 4f * pixel;
                AddStroke(vh, at + new Vector2(-arm, -arm), at + new Vector2(arm, arm), pixel * 1.2f, ink);
                AddStroke(vh, at + new Vector2(-arm, arm), at + new Vector2(arm, -arm), pixel * 1.2f, ink);
                return;
            }
            for (int tick = 0; tick < (int)line.Stage; tick++)
            {
                float x = at.x + (tick - 2) * 3f * pixel;
                AddStroke(vh, new Vector2(x, at.y + 6f * pixel), new Vector2(x, at.y + 9f * pixel), pixel, ink);
            }
        }

        private static void StrokeAll(VertexHelper vh, Vector3[][] traces, float factor, float step,
            float pixel, float halfWidthPixels, Color32 ink)
        {
            if (traces == null) return;
            for (int t = 0; t < traces.Length; t++)
                Stroke(vh, traces[t], factor, step, pixel, halfWidthPixels, ink);
        }

        /// <summary>
        /// One polyline as anti-aliased strokes: a dark under-stroke for legibility over
        /// terrain, then the ink. Stations closer than a couple of screen pixels are thinned
        /// out, so a densely resampled curve costs no more than it reads.
        /// </summary>
        private static void Stroke(VertexHelper vh, Vector3[] path, float factor, float step,
            float pixel, float halfWidthPixels, Color32 ink)
        {
            if (path == null || path.Length < 2) return;
            Vector2 anchor = ToLocal(path[0], factor);
            int segments = 0;
            for (int i = 1; i < path.Length; i++)
            {
                Vector2 here = ToLocal(path[i], factor);
                if (i < path.Length - 1 && (here - anchor).sqrMagnitude < step * step) continue;

                float half = halfWidthPixels * pixel;
                AddStroke(vh, anchor, here, half + UnderExtra * pixel, Under);
                AddStroke(vh, anchor, here, half, ink);
                anchor = here;
                if (++segments >= MaximumSegmentsPerStroke) return;
            }
        }

        /// <summary>NATO crenellation teeth, one per screen spacing, facing the enemy.</summary>
        private static void Teeth(VertexHelper vh, Vector3[] path, Vector3[] threat, float factor,
            float pixel, Color32 ink)
        {
            if (path == null || path.Length < 2) return;
            float spacing = ToothSpacingPixels * pixel;
            float sinceTooth = spacing;
            int teeth = 0;
            for (int i = 0; i + 1 < path.Length && teeth < MaximumSegmentsPerStroke; i++)
            {
                Vector2 a = ToLocal(path[i], factor);
                Vector2 b = ToLocal(path[i + 1], factor);
                Vector2 delta = b - a;
                float length = delta.magnitude;
                if (length < 1e-4f) continue;

                sinceTooth += length;
                if (sinceTooth < spacing) continue;
                sinceTooth = 0f;

                Vector2 side = new Vector2(-delta.y, delta.x) / length;
                Vector2 enemy = ThreatAt(threat, i);
                if (enemy.sqrMagnitude > 1e-6f && Vector2.Dot(side, enemy) < 0f) side = -side;
                AddStroke(vh, b, b + side * (ToothLengthPixels * pixel),
                    ToothHalfWidthPixels * pixel, ink);
                teeth++;
            }
        }

        private static Vector2 ThreatAt(Vector3[] threat, int index)
        {
            if (threat == null || threat.Length == 0) return Vector2.zero;
            Vector3 direction = threat[Mathf.Min(index, threat.Length - 1)];
            return new Vector2(direction.x, direction.z);
        }

        private static Vector2 ToLocal(Vector3 world, float factor)
            => new Vector2(world.x * factor, world.z * factor);

        private static void AddStroke(VertexHelper vh, Vector2 a, Vector2 b, float halfWidth, Color32 ink)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 1e-4f) return;
            Vector2 side = new Vector2(-delta.y, delta.x) / length * halfWidth;
            AddQuad(vh, a - side, a + side, b + side, b - side, ink);
        }

        private static void AddRect(VertexHelper vh, Vector2 center, float half, Color32 ink)
            => AddQuad(vh,
                new Vector2(center.x - half, center.y - half),
                new Vector2(center.x - half, center.y + half),
                new Vector2(center.x + half, center.y + half),
                new Vector2(center.x + half, center.y - half), ink);

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 ink)
        {
            var vert = UIVertex.simpleVert;
            vert.color = ink;
            int index = vh.currentVertCount;
            vert.position = a;
            vh.AddVert(vert);
            vert.position = b;
            vh.AddVert(vert);
            vert.position = c;
            vh.AddVert(vert);
            vert.position = d;
            vh.AddVert(vert);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
