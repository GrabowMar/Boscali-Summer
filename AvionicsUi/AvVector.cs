using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>
    /// A passive batched mesh for one <see cref="AvQuadBuffer"/>: the caller fills the
    /// buffer with <see cref="AvStrokes"/> calls, then <see cref="Commit"/>s once. This
    /// component never allocates or computes geometry of its own; it only reads the
    /// buffer's arrays into a UI mesh.
    /// </summary>
    public sealed class AvVector : MaskableGraphic
    {
        /// <summary>The geometry this graphic draws. Filled by the caller every rebuild.</summary>
        public AvQuadBuffer Buffer { get; private set; }

        /// <summary>
        /// Builds a stretched, non-interactive vector surface parented under
        /// <paramref name="parent"/>, backed by a fresh buffer of <paramref name="capacity"/> quads.
        /// </summary>
        public static AvVector Create(RectTransform parent, string name, int capacity)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(AvVector));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;

            var vector = go.GetComponent<AvVector>();
            vector.raycastTarget = false;
            vector.Buffer = new AvQuadBuffer(capacity);
            return vector;
        }

        /// <summary>Marks the mesh dirty so the buffer's current contents are (re)drawn.</summary>
        public void Commit() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            AvQuadBuffer buffer = Buffer;
            if (buffer == null || buffer.Count <= 0) return;

            Color tint = color;
            float[] x = buffer.X, y = buffer.Y;
            Rgba[] c = buffer.C;

            var vert = UIVertex.simpleVert;
            for (int q = 0; q < buffer.Count; q++)
            {
                int i = q * 4;
                int baseIndex = vh.currentVertCount;

                for (int v = 0; v < 4; v++)
                {
                    Rgba source = c[i + v];
                    vert.position = new Vector3(x[i + v], y[i + v], 0f);
                    vert.color = new Color(source.R * tint.r, source.G * tint.g, source.B * tint.b, source.A * tint.a);
                    vh.AddVert(vert);
                }

                vh.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
                vh.AddTriangle(baseIndex + 2, baseIndex + 3, baseIndex);
            }
        }
    }
}
