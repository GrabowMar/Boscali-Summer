using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Visuals
{
    /// <summary>
    /// Generates the carved trench ditch: a raised earthwork with a dry floor, a firing
    /// step and a forward parapet over a downward skirt, so it conforms across terrain
    /// slopes without touching terrain heightmaps. Strongpoints are real game scenery
    /// placed by <see cref="Runtime.TrenchWorks"/>; this only shapes the ditch between them.
    /// Every edge mesh shares one cross-section profile whose UVs are designed for the
    /// procedural earthwork material palette.
    /// </summary>
    internal static class TrenchMeshBuilder
    {
        /// <summary>Ring vertices per cross-section. Index order is documented in BuildEdgeMesh.</summary>
        public const int ProfilePointCount = 10;

        /// <summary>Cross-section strip between profile points 3 and 4 is the dry trench floor.</summary>
        public const int FloorStrip = 3;

        // 0: left outer skirt tip (ground penetration)
        // 1: left berm toe
        // 2: left parados crest (rear earth mound)
        // 3: left floor shoulder
        // 4: firing step base
        // 5: firing step top
        // 6: parapet inner crest (toward threat)
        // 7: parapet outer shoulder
        // 8: right berm toe
        // 9: right outer skirt tip (ground penetration)
        private static readonly float[] ProfileU = { 0.00f, 0.07f, 0.21f, 0.34f, 0.41f, 0.47f, 0.63f, 0.73f, 0.94f, 1.00f };

        /// <summary>
        /// Extrudes a continuous ditch along a path polyline: rear parados, dry trench
        /// floor with a firing step, and a forward parapet over a narrow spoil berm.
        /// When <paramref name="ground"/> is supplied, the outer berm toes and skirt tips
        /// are lowered onto the terrain on each side, so the earthwork hugs cross-slopes
        /// instead of bridging them.
        /// </summary>
        public static Mesh BuildEdgeMesh(Vector3[] path, float width, float parapetHeight, float skirtDepth, Vector3 threatDir,
            Func<Vector3, Vector3> ground = null)
        {
            if (path == null || path.Length < 2) return null;

            var mesh = new Mesh { name = "Trench_Edge_Mesh" };

            float halfW = width * 0.5f;
            float scale = Mathf.Clamp(width / 2.6f, 0.55f, 1f);
            float bermW = 1.0f * scale;
            float skirtW = 0.9f * scale;
            float stepW = Mathf.Min(0.9f * scale, halfW * 0.45f);
            float stepH = Mathf.Min(0.5f * scale, parapetHeight * 0.3f);
            float paradosH = parapetHeight * 0.82f;

            int ringSize = ProfilePointCount;
            int ringCount = path.Length;
            var vertices = new Vector3[ringCount * ringSize];
            var uvs = new Vector2[ringCount * ringSize];
            var triangles = new List<int>((ringCount - 1) * (ringSize - 1) * 6);

            float accumulatedLength = 0f;
            Vector3 prevRight = Vector3.zero;
            bool flipProfile = Vector3.Dot(Vector3.Cross(Vector3.up, path[1] - path[0]), threatDir) < 0f;

            for (int r = 0; r < ringCount; r++)
            {
                Vector3 pt = path[r];
                if (r > 0) accumulatedLength += Vector3.Distance(pt, path[r - 1]);

                Vector3 forward;
                if (r == 0) forward = (path[1] - path[0]).normalized;
                else if (r == ringCount - 1) forward = (path[ringCount - 1] - path[ringCount - 2]).normalized;
                else
                {
                    Vector3 d0 = (pt - path[r - 1]).normalized;
                    Vector3 d1 = (path[r + 1] - pt).normalized;
                    Vector3 sum = d0 + d1;
                    forward = sum.sqrMagnitude > 0.01f ? sum.normalized : d1;
                }

                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                if (right.sqrMagnitude < 0.001f)
                    right = prevRight.sqrMagnitude > 0.001f ? prevRight : Vector3.right;

                // Maintain continuous lateral orientation so walls never twist 180 degrees.
                if (r > 0 && prevRight.sqrMagnitude > 0.001f)
                {
                    if (Vector3.Dot(right, prevRight) < 0f) right = -right;
                }
                else if (flipProfile)
                {
                    right = -right;
                }
                prevRight = right;
                Vector3 left = -right;

                // Sandbag/traverse jitter breaks the perfectly smooth silhouette.
                float jitter = Hash(accumulatedLength);
                float crestJitter = (Hash(accumulatedLength + 17.3f) - 0.5f) * 0.30f;
                float bermJitter = (Hash(accumulatedLength + 41.7f) - 0.5f) * 0.45f;

                // Conform the outer sides to the terrain: a cross-slope must meet the
                // berms, not run under or above them.
                float outer = halfW + bermW + skirtW;
                float leftGround = pt.y, rightGround = pt.y;
                if (ground != null)
                {
                    leftGround = ground(pt - right * outer).y;
                    rightGround = ground(pt + right * outer).y;
                }

                int baseIdx = r * ringSize;
                vertices[baseIdx + 0] = new Vector3(
                    (pt + left * outer).x, leftGround - skirtDepth, (pt + left * outer).z);
                vertices[baseIdx + 1] = new Vector3(
                    (pt + left * (halfW + bermW + bermJitter * 0.4f)).x, leftGround + 0.10f,
                    (pt + left * (halfW + bermW + bermJitter * 0.4f)).z);
                vertices[baseIdx + 2] = pt + left * (halfW + 0.85f + bermJitter * 0.2f) +
                    Vector3.up * (Mathf.Max(pt.y, leftGround) - pt.y + paradosH + crestJitter * 0.5f);
                vertices[baseIdx + 3] = pt + left * halfW + Vector3.up * 0.14f;
                vertices[baseIdx + 4] = pt + right * (halfW - stepW) + Vector3.up * 0.14f;
                vertices[baseIdx + 5] = pt + right * (halfW - stepW) + Vector3.up * (0.14f + stepH);
                vertices[baseIdx + 6] = pt + right * (halfW + 0.45f) + Vector3.up * (parapetHeight + crestJitter);
                vertices[baseIdx + 7] = pt + right * (halfW + 1.05f + jitter * 0.4f) + Vector3.up * (parapetHeight - 0.3f + crestJitter * 0.6f);
                vertices[baseIdx + 8] = new Vector3(
                    (pt + right * (halfW + bermW)).x, rightGround + 0.10f,
                    (pt + right * (halfW + bermW)).z);
                vertices[baseIdx + 9] = new Vector3(
                    (pt + right * outer).x, rightGround - skirtDepth, (pt + right * outer).z);

                float u = accumulatedLength * 0.34f;
                for (int p = 0; p < ringSize; p++) uvs[baseIdx + p] = new Vector2(ProfileU[p], u);
            }

            for (int r = 0; r < ringCount - 1; r++)
            {
                int r0 = r * ringSize;
                int r1 = (r + 1) * ringSize;
                // Facing the threat can mirror the cross-section. Mirror triangle
                // winding too, otherwise the entire berm is back-face culled from above.
                bool mirrored = Vector3.Cross(path[r + 1] - path[r], vertices[r0 + 6] - vertices[r0]).y < 0f;

                for (int s = 0; s < ringSize - 1; s++)
                {
                    int a = r0 + s;
                    int b = r1 + s;
                    int c = r1 + s + 1;
                    int d = r0 + s + 1;

                    triangles.Add(a);
                    triangles.Add(mirrored ? c : b);
                    triangles.Add(mirrored ? b : c);

                    triangles.Add(a);
                    triangles.Add(mirrored ? d : c);
                    triangles.Add(mirrored ? c : d);
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float Hash(float value)
        {
            float v = Mathf.Sin(value * 12.9898f + 78.233f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }
    }
}
