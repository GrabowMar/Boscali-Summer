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
            int capHubStart = ringCount * ringSize;
            var vertices = new Vector3[capHubStart + 2];   // two end-cap hubs
            var uvs = new Vector2[capHubStart + 2];
            var triangles = new List<int>((ringCount - 1) * (ringSize - 1) * 6 + (ringSize - 1) * 6);

            float accumulatedLength = 0f;
            Vector3 prevRight = Vector3.zero;
            bool flipProfile = Vector3.Dot(Vector3.Cross(Vector3.up, path[1] - path[0]), threatDir) < 0f;

            for (int r = 0; r < ringCount; r++)
            {
                Vector3 pt = path[r];
                if (r > 0) accumulatedLength += Vector3.Distance(pt, path[r - 1]);

                // Cross-section frame. At a traverse corner the two wall planes of one side
                // meet on their bisector; extending the profile to that intersection keeps
                // consecutive rings in contact instead of folding over one another.
                Vector3 right;
                float miter = 1f;
                if (r == 0)
                {
                    right = Lateral(path[1] - path[0], Vector3.right);
                    if (flipProfile) right = -right;
                }
                else if (r == ringCount - 1)
                {
                    right = Lateral(pt - path[r - 1], prevRight);
                    if (Vector3.Dot(right, prevRight) < 0f) right = -right;
                }
                else
                {
                    Vector3 inRight = Lateral(pt - path[r - 1], prevRight);
                    Vector3 outRight = Lateral(path[r + 1] - pt, prevRight);
                    if (Vector3.Dot(inRight, prevRight) < 0f) inRight = -inRight;
                    if (Vector3.Dot(outRight, prevRight) < 0f) outRight = -outRight;
                    Vector3 sum = inRight + outRight;
                    if (sum.sqrMagnitude > 0.0001f)
                    {
                        right = sum.normalized;
                        miter = Mathf.Clamp(1f / Mathf.Max(0.35f, Vector3.Dot(right, inRight)), 1f, 2.5f);
                    }
                    else
                    {
                        right = inRight;
                    }
                }
                prevRight = right;
                Vector3 left = -right;

                // Hand-dug irregularity: spoil heaps and a slightly wavy crest, plus a slow
                // width drift so the earthwork never reads as one uniform extrusion.
                float spoilJitter = Noise(accumulatedLength * 0.16f, 5.3f);
                float crestJitter = (Noise(accumulatedLength * 0.22f, 11.7f) - 0.5f) * 0.34f +
                    (Noise(accumulatedLength * 0.05f, 71.3f) - 0.5f) * 0.30f;
                float bermJitter = (Noise(accumulatedLength * 0.13f, 29.1f) - 0.5f) * 0.55f;
                miter *= 1f + (Noise(accumulatedLength * 0.025f, 43.9f) - 0.5f) * 0.16f;

                // Conform the outer sides to the terrain: a cross-slope must meet the
                // berms, not run under or above them.
                float outer = (halfW + bermW + skirtW) * miter;
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
                    (pt + left * ((halfW + bermW) * miter + bermJitter * 0.4f)).x, leftGround + 0.10f,
                    (pt + left * ((halfW + bermW) * miter + bermJitter * 0.4f)).z);
                vertices[baseIdx + 2] = pt + left * ((halfW + 0.85f) * miter + bermJitter * 0.2f) +
                    Vector3.up * (Mathf.Max(pt.y, leftGround) - pt.y + paradosH + crestJitter * 0.5f);
                vertices[baseIdx + 3] = pt + left * (halfW * miter) + Vector3.up * 0.14f;
                vertices[baseIdx + 4] = pt + right * ((halfW - stepW) * miter) + Vector3.up * 0.14f;
                vertices[baseIdx + 5] = pt + right * ((halfW - stepW) * miter) + Vector3.up * (0.14f + stepH);
                vertices[baseIdx + 6] = pt + right * ((halfW + 0.45f) * miter) + Vector3.up * (parapetHeight + crestJitter);
                vertices[baseIdx + 7] = pt + right * ((halfW + 1.05f) * miter + spoilJitter * 0.4f) +
                    Vector3.up * (parapetHeight - 0.3f + crestJitter * 0.6f);
                vertices[baseIdx + 8] = new Vector3(
                    (pt + right * ((halfW + bermW) * miter)).x, rightGround + 0.10f,
                    (pt + right * ((halfW + bermW) * miter)).z);
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

            // Close both ends over the cross-section, so a trench end reads as a head-cover
            // bank instead of an open pipe mouth.
            for (int end = 0; end < 2; end++)
            {
                int baseIdx = (end == 0 ? 0 : ringCount - 1) * ringSize;
                Vector3 hub = Vector3.zero;
                for (int p = 0; p < ringSize; p++) hub += vertices[baseIdx + p];
                vertices[capHubStart + end] = hub / ringSize;
                uvs[capHubStart + end] = new Vector2(0.5f, 0.5f);
            }
            AddCap(triangles, capHubStart, 0, ringSize, path[0] - path[1], vertices);
            AddCap(triangles, capHubStart + 1, (ringCount - 1) * ringSize, ringSize,
                path[ringCount - 1] - path[ringCount - 2], vertices);

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Lateral(Vector3 direction, Vector3 fallback)
        {
            Vector3 lateral = Vector3.Cross(Vector3.up, direction);
            if (lateral.sqrMagnitude < 0.0001f)
                return fallback.sqrMagnitude > 0.0001f ? fallback : Vector3.right;
            return lateral.normalized;
        }

        /// <summary>Fans the profile closed at one end; auto-fixes the winding to face outward.</summary>
        private static void AddCap(List<int> triangles, int hub, int baseIndex, int ringSize, Vector3 outward, Vector3[] vertices)
        {
            if (outward.sqrMagnitude < 0.000001f) return;
            Vector3 wanted = outward.normalized;
            Vector3 normal = Vector3.Cross(vertices[baseIndex] - vertices[hub], vertices[baseIndex + 1] - vertices[hub]);
            bool flip = Vector3.Dot(normal, wanted) < 0f;
            for (int p = 0; p < ringSize - 1; p++)
            {
                triangles.Add(hub);
                triangles.Add(flip ? baseIndex + p + 1 : baseIndex + p);
                triangles.Add(flip ? baseIndex + p : baseIndex + p + 1);
            }
        }

        private static float Noise(float distance, float seed) => Mathf.PerlinNoise(distance, seed);
    }
}
