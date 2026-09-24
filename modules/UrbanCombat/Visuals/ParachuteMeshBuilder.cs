using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Engine-generated parachute: a gored dome canopy above crossed shroud lines.
    /// Pure geometry, no game types, so it can be previewed without the game assembly.
    /// </summary>
    internal static class ParachuteMeshBuilder
    {
        public const float RimRadius = 3.9f;
        public const float RimHeight = 7f;
        public const float CanopyDepth = 2.6f;
        public const float HarnessHeight = 0.15f;
        public const int Segments = 20;
        public const int Rings = 7;

        public static Mesh Build()
        {
            var verts = new List<Vector3>(Segments * (Rings + 1) * 2 + Segments * 16);
            var uvs = new List<Vector2>(verts.Capacity);
            var canopyTris = new List<int>(Segments * Rings * 12);
            var lineTris = new List<int>(Segments * 24);

            BuildCanopy(verts, uvs, canopyTris);
            AddInnerCanopy(verts, uvs, canopyTris);
            BuildShrouds(verts, uvs, lineTris);

            Mesh mesh = new Mesh();
            mesh.name = "BoscaliSummer.Parachute";
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(canopyTris, 0);
            mesh.SetTriangles(lineTris, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void BuildCanopy(List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            int apex = verts.Count;
            verts.Add(new Vector3(0f, RimHeight + CanopyDepth, 0f));
            uvs.Add(new Vector2(0.5f, 0f));

            for (int r = 1; r <= Rings; r++)
            {
                float t = r / (float)Rings;
                float radius = RimRadius * Mathf.Sin(t * Mathf.PI * 0.5f);
                float y = Mathf.Lerp(RimHeight + CanopyDepth, RimHeight, t);
                for (int s = 0; s < Segments; s++)
                {
                    float a = s / (float)Segments * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                    uvs.Add(new Vector2(s / (float)Segments, t));
                }
            }

            // Outer skin: every triangle faces out, so the apex fan and the rings share smooth
            // normals; AddInnerCanopy mirrors it for the inside.
            int firstRing = apex + 1;
            for (int s = 0; s < Segments; s++)
                AddTriangle(tris, apex, firstRing + (s + 1) % Segments, firstRing + s);

            for (int r = 0; r < Rings - 1; r++)
            {
                int ring = firstRing + r * Segments;
                int nextRing = ring + Segments;
                for (int s = 0; s < Segments; s++)
                {
                    int next = (s + 1) % Segments;
                    AddQuad(tris, ring + s, ring + next, nextRing + next, nextRing + s);
                }
            }
        }

        private static void AddInnerCanopy(List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            int canopyVertexCount = verts.Count;
            int canopyIndexCount = tris.Count;

            for (int i = 0; i < canopyVertexCount; i++)
            {
                verts.Add(verts[i]);
                uvs.Add(uvs[i]);
            }

            for (int i = 0; i < canopyIndexCount; i += 3)
            {
                tris.Add(tris[i] + canopyVertexCount);
                tris.Add(tris[i + 2] + canopyVertexCount);
                tris.Add(tris[i + 1] + canopyVertexCount);
            }
        }

        private static void BuildShrouds(List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            for (int s = 0; s < Segments; s++)
            {
                float a = s / (float)Segments * Mathf.PI * 2f;
                Vector3 rim = new Vector3(Mathf.Cos(a) * RimRadius * 0.97f, RimHeight, Mathf.Sin(a) * RimRadius * 0.97f);
                Vector3 harness = new Vector3(0f, HarnessHeight, 0f);
                AddRibbon(verts, uvs, tris, harness, rim, 0.04f, a);
                AddRibbon(verts, uvs, tris, harness, rim, 0.04f, a + Mathf.PI * 0.5f);
            }
        }

        private static void AddTriangle(List<int> tris, int a, int b, int c)
        {
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
        }

        private static void AddQuad(List<int> tris, int a, int b, int c, int d)
        {
            AddTriangle(tris, a, b, c);
            AddTriangle(tris, a, c, d);
        }

        private static void AddRibbon(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3 from, Vector3 to, float halfWidth, float roll)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.001f) return;

            Vector3 dir = delta / length;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
            side = Quaternion.AngleAxis(roll * Mathf.Rad2Deg, dir) * side.normalized * halfWidth;

            // Front and back each get their own four vertices, so neither face's normal
            // cancels the other's, and the back is the exact reverse of the front.
            int baseIndex = verts.Count;
            for (int face = 0; face < 2; face++)
            {
                verts.Add(from - side);
                verts.Add(from + side);
                verts.Add(to + side);
                verts.Add(to - side);
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, 0f));
            }

            AddQuad(tris, baseIndex, baseIndex + 1, baseIndex + 2, baseIndex + 3);
            AddQuad(tris, baseIndex + 4, baseIndex + 7, baseIndex + 6, baseIndex + 5);
        }
    }
}
