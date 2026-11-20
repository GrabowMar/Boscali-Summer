using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
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
        /// <summary><paramref name="glow"/> &gt; 0 makes every line and arc glow (<see cref="AvQuadBuffer.Glow"/>; the buffer gets three times the quads).</summary>
        public static AvVector Create(RectTransform parent, string name, int capacity, float glow, float glowAlpha)
        {
            AvVector v = Create(parent, name, glow > 0f ? capacity * 3 : capacity);
            v.Buffer.Glow = glow;
            v.Buffer.GlowAlpha = glowAlpha;
            return v;
        }

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

        private ulong committedHash;
        private int committedCount = -1;

        /// <summary>
        /// Marks the mesh dirty so the buffer's current contents are (re)drawn; a no-op when they match the last commit. <paramref name="force"/> is for a buffer
        /// redrawn every frame anyway (Motion): it always redraws, and from then on the buffer stops hashing what it is fed.
        /// </summary>
        public void Commit(bool force = false)
        {
            AvQuadBuffer b = Buffer;
            if (force)
            {
                if (b != null) b.Hashing = false;
                committedCount = -1;
                SetVerticesDirty();
                return;
            }
            if (b != null && b.Count == committedCount && b.ContentHash == committedHash) return;
            committedCount = b != null ? b.Count : -1;
            committedHash = b != null ? b.ContentHash : 0;
            SetVerticesDirty();
        }

        // Own mesh and capacity arrays: no VertexHelper list appends or second copy on a rebuild; the index buffer is written once.
        private Mesh mesh;
        private Vector3[] verts;
        private Color[] cols;
        private Vector2[] uvs;
        private int[] tris;
        private int meshQuads = -1;

        protected override void UpdateGeometry()
        {
            AvQuadBuffer b = Buffer;
            int n = b != null ? b.Count : 0;
            if (mesh == null)
            {
                mesh = new Mesh { name = "AvVector", hideFlags = HideFlags.HideAndDontSave };
                mesh.MarkDynamic();
            }
            if (n <= 0)
            {
                if (meshQuads != 0) mesh.Clear();
                meshQuads = 0;
                canvasRenderer.SetMesh(mesh);
                return;
            }

            int cap = 4 * b.Capacity;
            if (verts == null || verts.Length < cap)
            {
                verts = new Vector3[cap];
                cols = new Color[cap];
                uvs = new Vector2[cap];
                tris = new int[6 * b.Capacity];
                for (int q = 0, t = 0; q < b.Capacity; q++, t += 6)
                {
                    int i = q * 4;
                    tris[t] = i; tris[t + 1] = i + 1; tris[t + 2] = i + 2;
                    tris[t + 3] = i + 2; tris[t + 4] = i + 3; tris[t + 5] = i;
                }
                meshQuads = -1;
            }

            Color tint = color;
            bool plain = tint == Color.white;
            float[] x = b.X, y = b.Y;
            Rgba[] c = b.C;
            int nv = n * 4;
            for (int i = 0; i < nv; i++)
            {
                verts[i] = new Vector3(x[i], y[i], 0f);
                Rgba s = c[i];
                cols[i] = plain ? new Color(s.R, s.G, s.B, s.A) : new Color(s.R * tint.r, s.G * tint.g, s.B * tint.b, s.A * tint.a);
            }

            if (n != meshQuads)
            {
                mesh.Clear();
                mesh.SetVertices(verts, 0, nv);
                mesh.SetColors(cols, 0, nv);
                mesh.SetUVs(0, uvs, 0, nv);
                mesh.SetTriangles(tris, 0, n * 6, 0);
                meshQuads = n;
            }
            else
            {
                mesh.SetVertices(verts, 0, nv);
                mesh.SetColors(cols, 0, nv);
            }
            canvasRenderer.SetMesh(mesh);
        }

        protected override void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            mesh = null;
            base.OnDestroy();
        }

        /// <summary>The stock path (unused while <see cref="UpdateGeometry"/> is overridden; kept for anything that asks a graphic for its mesh).</summary>
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
