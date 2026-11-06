using System.Collections.Generic;
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

        internal bool Visible { get; private set; }

        /// <param name="eye">Camera position in global coordinates.</param>
        internal void Tick(Vector3 eye, bool enabled, AirfieldLights airfield, Camera camera)
        {
            Visible = enabled && camera != null && Datum.origin != null &&
                      airfield.TryApproach(eye, out Vector3 threshold, out Vector3 outward) &&
                      Vector3.Dot(camera.transform.forward, -outward) > .5f &&
                      Draw(eye, threshold, outward, camera);
            if (!Visible && graphic != null && graphic.enabled) graphic.enabled = false;
        }

        private bool Draw(Vector3 eye, Vector3 threshold, Vector3 outward, Camera camera)
        {
            if (eye.y - threshold.y > 1500f || !Ensure()) return false;
            Transform datum = Datum.origin;
            RectTransform rect = graphic.rectTransform;
            Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;
            float own = Vector3.Dot(eye - threshold, outward);
            List<ArFrameGraphic.Quad> quads = graphic.Frames;
            quads.Clear();
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
                    Vector3 screen = camera.WorldToScreenPoint(datum.TransformPoint(corners[k]));
                    Vector2 local = default;
                    inFront = screen.z > 1f && RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, null, out local);
                    if (inFront) q[k] = local;
                }
                if (!inFront) continue;
                Color ink = Ink;
                ink.a = Mathf.Lerp(.9f, .3f, Mathf.InverseLerp(800f, 4500f, Vector3.Distance(eye, c)));
                q.Ink = ink;
                quads.Add(q);
            }
            graphic.Width = Stroke / Mathf.Max(.01f, canvas.scaleFactor);
            graphic.enabled = quads.Count > 0;
            graphic.SetVerticesDirty();
            return quads.Count > 0;
        }

        private bool Ensure()
        {
            if (root != null && graphic != null) return true;
            int order = VisorOverlay.UnderHud(out int layer, out _);
            root = new GameObject("Boscali.ArTunnel");
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order + 1;   // the HUD's own order: over the visor glass, level with the symbology
            canvas.sortingLayerID = layer;
            var node = new GameObject("Frames", typeof(RectTransform));
            node.transform.SetParent(root.transform, false);
            var rect = (RectTransform)node.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            graphic = node.AddComponent<ArFrameGraphic>();
            graphic.raycastTarget = false;
            graphic.enabled = false;
            return true;
        }

        internal void Reset()
        {
            if (root != null) Object.Destroy(root);
            root = null; canvas = null; graphic = null;
            Visible = false;
        }

        internal void Release() => Reset();
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
