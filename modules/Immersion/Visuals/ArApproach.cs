using System.Collections.Generic;
using BoscaliSummer.Core.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// AR approach tunnel (2026-11-06): on final to a runway end, the helmet draws a "highway in the sky", eight HUD-green
    /// frames on the 3 degree glide path (15 m over the threshold), every 600 m out to 4.8 km. Only frames still ahead of the
    /// aircraft show; far frames fade. The frames are world-locked but drawn on the helmet layer (a Screen Space Overlay canvas
    /// just under the vanilla HUD), so the instrument panel never hides them: on a slow approach the nose is up about 8 degrees
    /// and the whole glide path sits behind the panel coaming. Cockpit view only; runway ends from
    /// <see cref="AirfieldLights.TryApproach"/>.
    /// </summary>
    internal sealed class ArApproach
    {
        private const int Frames = 8;
        private const float Spacing = 600f, HalfWidth = 30f, HalfHeight = 18f, Crossing = 15f, Stroke = 2.4f;
        private static readonly float Slope = Mathf.Tan(3f * Mathf.Deg2Rad);
        private static readonly Color Ink = new Color(.35f, 1f, .55f, 1f);

        private GameObject root;
        private Canvas canvas;
        private ArFrameGraphic graphic;
        private readonly Vector3[] corners = new Vector3[4];
        private bool inLayer, subscribed, active;
        private Vector3 threshold, outward, eyeGlobal;
        private float signature;

        internal bool Visible { get; private set; }

        /// <param name="eye">Camera position in global coordinates.</param>
        internal void Tick(Vector3 eye, bool enabled, AirfieldLights airfield, Camera camera)
        {
            // The approach geometry is read here (Update, global coordinates); the projection is painted at the layer's late point so it sits on the rendered pose.
            active = enabled && camera != null && Datum.origin != null &&
                     airfield.TryApproach(eye, out threshold, out outward) &&
                     Vector3.Dot(camera.transform.forward, -outward) > .5f && eye.y - threshold.y <= 1500f;
            eyeGlobal = eye;
            if (!subscribed) { subscribed = true; HudLayer.Paint += OnPaint; }
            if (!HudLayer.Enabled)
            {
                // A/B switch off: the old Update-phase path.
                Visible = active && Draw(camera, false, default);
                if (!Visible && graphic != null && graphic.enabled) graphic.enabled = false;
            }
        }

        private void OnPaint(HudView view)
        {
            if (!HudLayer.Enabled) return;
            Visible = active && view.Valid && Draw(view.Cam, true, view);
            if (!Visible && graphic != null && graphic.enabled) graphic.enabled = false;
        }

        private bool Draw(Camera camera, bool layer, HudView view)
        {
            if (!Ensure()) return false;
            Transform datum = Datum.origin;
            RectTransform rect = graphic.rectTransform;
            Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;
            float own = Vector3.Dot(eyeGlobal - threshold, outward);
            List<ArFrameGraphic.Quad> quads = graphic.Frames;
            quads.Clear();
            float sig = 0f;
            for (int i = 0; i < Frames; i++)
            {
                float d = (i + 1) * Spacing;
                if (d >= own - 150f) continue;   // behind the aircraft or right under it
                Vector3 c = threshold + outward * d + Vector3.up * (Crossing + d * Slope);
                corners[0] = c - side * HalfWidth - Vector3.up * HalfHeight;
                corners[1] = c + side * HalfWidth - Vector3.up * HalfHeight;
                corners[2] = c + side * HalfWidth + Vector3.up * HalfHeight;
                corners[3] = c - side * HalfWidth + Vector3.up * HalfHeight;
                var q = new ArFrameGraphic.Quad();
                bool inFront = true;
                for (int k = 0; k < 4 && inFront; k++)
                {
                    Vector3 world = datum.TransformPoint(corners[k]);
                    Vector2 local = default;
                    if (layer) inFront = view.Project(world, out local);
                    else
                    {
                        Vector3 screen = camera.WorldToScreenPoint(world);
                        inFront = screen.z > 1f && RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, null, out local);
                    }
                    if (inFront) { q[k] = local; sig = sig * 1.0001f + local.x + local.y * 1.7f; }
                }
                if (!inFront) continue;
                Color ink = Ink;
                ink.a = Mathf.Lerp(.9f, .3f, Mathf.InverseLerp(800f, 4500f, Vector3.Distance(eyeGlobal, c)));
                q.Ink = ink;
                sig += ink.a;
                quads.Add(q);
            }
            float scale = layer ? view.Scale : canvas.scaleFactor;
            graphic.Width = Stroke / Mathf.Max(.01f, scale);
            sig += quads.Count * 1000f;
            graphic.enabled = quads.Count > 0;
            if (sig != signature) { signature = sig; graphic.SetVerticesDirty(); }   // ponytail: float checksum, a missed change would only lag one frame
            return quads.Count > 0;
        }

        private bool Ensure()
        {
            bool layer = HudLayer.Enabled && HudLayer.Ensure();
            if (root != null && graphic != null && layer == inLayer) return true;
            if (root != null) Object.Destroy(root);
            inLayer = layer;
            root = new GameObject("Boscali.ArTunnel", typeof(RectTransform));
            if (layer)
            {
                canvas = null;
                root.transform.SetParent(HudLayer.Under, false);   // under the native symbols, inside the HUD canvas
            }
            else
            {
                int order = VisorOverlay.UnderHud(out int sortLayer, out _);
                canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = order + 1;   // the HUD's own order: over the visor glass, level with the symbology
                canvas.sortingLayerID = sortLayer;
            }
            var node = new GameObject("Frames", typeof(RectTransform));
            node.transform.SetParent(root.transform, false);
            var rect = (RectTransform)node.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            if (layer) { var r = (RectTransform)root.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
            graphic = node.AddComponent<ArFrameGraphic>();
            graphic.raycastTarget = false;
            graphic.enabled = false;
            signature = float.NaN;
            return true;
        }

        internal void Reset()
        {
            if (root != null) Object.Destroy(root);
            root = null; canvas = null; graphic = null;
            Visible = active = false;      // the Paint subscription outlives a reset: no stale approach redrawn (and its root rebuilt) until the next Tick says so
        }

        internal void Release()
        {
            if (subscribed) { subscribed = false; HudLayer.Paint -= OnPaint; }
            Reset();
        }
    }

    /// <summary>Thin outlines of four-corner frames in canvas space; one mesh for the whole tunnel.</summary>
    internal sealed class ArFrameGraphic : MaskableGraphic
    {
        internal struct Quad
        {
            public Vector2 A, B, C, D;
            public Color Ink;

            public Vector2 this[int i]
            {
                get => i == 0 ? A : i == 1 ? B : i == 2 ? C : D;
                set { if (i == 0) A = value; else if (i == 1) B = value; else if (i == 2) C = value; else D = value; }
            }
        }

        internal readonly List<Quad> Frames = new List<Quad>();
        internal float Width = 2f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            foreach (Quad q in Frames)
                for (int k = 0; k < 4; k++) Segment(vh, q[k], q[(k + 1) % 4], q.Ink);
        }

        private void Segment(VertexHelper vh, Vector2 a, Vector2 b, Color ink)
        {
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < .01f) return;
            // Extended by half a stroke at both ends so the corners close.
            dir.Normalize();
            Vector2 n = new Vector2(-dir.y, dir.x) * (Width * .5f), e = dir * (Width * .5f);
            int i = vh.currentVertCount;
            vh.AddVert(a - e - n, ink, Vector2.zero);
            vh.AddVert(a - e + n, ink, Vector2.zero);
            vh.AddVert(b + e + n, ink, Vector2.zero);
            vh.AddVert(b + e - n, ink, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
