using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Visuals
{
    /// <summary>
    /// Generates lightweight procedural meshes for dynamic trenches and fortifications.
    /// Earthworks are raised berms with deep downward skirts so they conform across terrain
    /// slopes without touching terrain heightmaps. Every edge mesh shares one cross-section
    /// profile whose UVs are designed for the procedural earthwork material palette.
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
        /// Extrudes a continuous earthwork along a path polyline: rear parados, dry trench
        /// floor with a firing step, and a forward sandbag parapet over a wide berm.
        /// </summary>
        public static Mesh BuildEdgeMesh(Vector3[] path, float width, float parapetHeight, float skirtDepth, Vector3 threatDir)
        {
            if (path == null || path.Length < 2) return null;

            var mesh = new Mesh { name = "Trench_Edge_Mesh" };

            float halfW = width * 0.5f;
            float scale = Mathf.Clamp(width / 2.6f, 0.55f, 1f);
            float bermW = 1.5f * scale;
            float skirtW = 1.2f * scale;
            float stepW = Mathf.Min(0.9f * scale, halfW * 0.45f);
            float stepH = Mathf.Min(0.5f * scale, parapetHeight * 0.3f);
            float paradosH = parapetHeight * 0.78f;

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

                int baseIdx = r * ringSize;
                vertices[baseIdx + 0] = pt + left * (halfW + bermW + skirtW) + Vector3.down * skirtDepth;
                vertices[baseIdx + 1] = pt + left * (halfW + bermW + bermJitter * 0.4f) + Vector3.up * 0.10f;
                vertices[baseIdx + 2] = pt + left * (halfW + 0.85f + bermJitter * 0.2f) + Vector3.up * (paradosH + crestJitter * 0.5f);
                vertices[baseIdx + 3] = pt + left * halfW + Vector3.up * 0.14f;
                vertices[baseIdx + 4] = pt + right * (halfW - stepW) + Vector3.up * 0.14f;
                vertices[baseIdx + 5] = pt + right * (halfW - stepW) + Vector3.up * (0.14f + stepH);
                vertices[baseIdx + 6] = pt + right * (halfW + 0.45f) + Vector3.up * (parapetHeight + crestJitter);
                vertices[baseIdx + 7] = pt + right * (halfW + 1.05f + jitter * 0.4f) + Vector3.up * (parapetHeight - 0.3f + crestJitter * 0.6f);
                vertices[baseIdx + 8] = pt + right * (halfW + bermW) + Vector3.up * 0.10f;
                vertices[baseIdx + 9] = pt + right * (halfW + bermW + skirtW) + Vector3.down * skirtDepth;

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

        private static void Box(List<Vector3> vertices, List<int> triangles, Vector3 center, Vector3 size)
        {
            int start = vertices.Count;
            for (int corner = 0; corner < 8; corner++)
                vertices.Add(center + new Vector3((corner & 1) == 0 ? -size.x : size.x,
                    (corner & 2) == 0 ? -size.y : size.y, (corner & 4) == 0 ? -size.z : size.z) * 0.5f);
            int[] indices = { 0,2,1, 1,2,3, 4,5,6, 5,7,6, 0,4,2, 2,4,6,
                1,3,5, 3,7,5, 2,6,3, 3,6,7, 0,1,4, 1,5,4 };
            foreach (int index in indices) triangles.Add(start + index);
        }

        private static Mesh Finish(List<Vector3> vertices, List<int> triangles, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            var uv = new Vector2[vertices.Count];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(vertices[i].x, vertices[i].z);
            mesh.uv = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Rectangular revetted fighting bay with an open front and a raised parapet.</summary>
        public static Mesh BuildFightingBayMesh()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            // 1. Earthen foundation with downward skirts so the bay never floats on slopes
            Box(vertices, triangles, new Vector3(0, -0.6f, -0.3f), new Vector3(7.6f, 1.2f, 8.0f));

            // 2. Raised revetted courses in a U, with staggered ends and a dry floor
            for (int row = 0; row < 4; row++)
            {
                float y = 0.25f + row * 0.45f;
                float shrink = row * 0.3f;
                Box(vertices, triangles, new Vector3(-3.0f, y, -0.35f), new Vector3(1.15f, 0.46f, 6.9f - shrink));
                Box(vertices, triangles, new Vector3(3.0f, y, -0.35f), new Vector3(1.15f, 0.46f, 6.9f - shrink));
                Box(vertices, triangles, new Vector3(0, y, -3.05f), new Vector3(6.0f, 0.46f, 1.15f));
            }

            // 3. Sandbag firing parapet courses on the front and flanks
            for (int i = 0; i < 7; i++)
            {
                float z = -2.7f + i * 0.9f;
                Box(vertices, triangles, new Vector3(-3.0f, 2.05f, z), new Vector3(1.05f, 0.38f, 0.88f));
                Box(vertices, triangles, new Vector3(3.0f, 2.05f, z), new Vector3(1.05f, 0.38f, 0.88f));
            }
            for (int i = 0; i < 6; i++)
                Box(vertices, triangles, new Vector3(-2.5f + i * 1.0f, 2.05f, -3.05f), new Vector3(1.05f, 0.38f, 0.95f));

            // 4. Timber duckboard floor planks
            for (int plank = 0; plank < 12; plank++)
                Box(vertices, triangles, new Vector3(0, 0.10f, -2.4f + plank * 0.44f), new Vector3(4.6f, 0.14f, 0.38f));

            return Finish(vertices, triangles, "Trench_RevettedBay");
        }

        /// <summary>
        /// Generates a standardized modular straight trench mesh with timber revetments,
        /// firing step, parapet, and downward earth skirts.
        /// </summary>
        public static Mesh BuildModularStraightMesh(float length = 8.0f, float width = 2.6f, float parapetH = 2.4f, float paradosH = 1.7f, float skirtDepth = 1.4f)
        {
            Vector3[] path = new Vector3[]
            {
                new Vector3(0f, 0f, -length * 0.5f),
                new Vector3(0f, 0f, length * 0.5f)
            };

            Mesh mesh = BuildEdgeMesh(path, width, parapetH, skirtDepth, Vector3.right);
            if (mesh != null) mesh.name = "Trench_Modular_Straight";

            // Add timber floor planks along the center
            var verts = new List<Vector3>(mesh.vertices);
            var tris = new List<int>(mesh.triangles);
            int plankCount = Mathf.Max(2, Mathf.RoundToInt(length / 0.7f));
            for (int p = 0; p < plankCount; p++)
            {
                float z = -length * 0.5f + (p + 0.5f) * (length / plankCount);
                Box(verts, tris, new Vector3(-width * 0.1f, 0.18f, z), new Vector3(width * 0.8f, 0.1f, 0.34f));
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Generates a modular corner/bend connector mesh to bridge angles cleanly.</summary>
        public static Mesh BuildModularCornerMesh(float width = 2.6f, float parapetH = 2.4f, float skirtDepth = 1.4f)
        {
            float len = 3.4f;
            Vector3[] path = new Vector3[]
            {
                new Vector3(0f, 0f, -len),
                Vector3.zero,
                new Vector3(len * 0.707f, 0f, len * 0.707f)
            };

            Mesh mesh = BuildEdgeMesh(path, width, parapetH, skirtDepth, new Vector3(1f, 0f, -0.5f).normalized);
            if (mesh != null) mesh.name = "Trench_Modular_Corner";
            return mesh;
        }

        /// <summary>Generates a modular 3-way T-junction connector mesh.</summary>
        public static Mesh BuildModularJunctionMesh(float width = 2.6f, float parapetH = 2.4f, float skirtDepth = 1.4f)
        {
            float len = 4.0f;
            Vector3[] pathMain = new Vector3[]
            {
                new Vector3(0f, 0f, -len),
                Vector3.zero,
                new Vector3(0f, 0f, len)
            };
            Mesh mainMesh = BuildEdgeMesh(pathMain, width, parapetH, skirtDepth, Vector3.right);

            Vector3[] pathBranch = new Vector3[]
            {
                Vector3.zero,
                new Vector3(-len, 0f, 0f)
            };
            Mesh branchMesh = BuildEdgeMesh(pathBranch, width, parapetH * 0.8f, skirtDepth, Vector3.back);

            var verts = new List<Vector3>(mainMesh.vertices);
            var tris = new List<int>(mainMesh.triangles);
            int vOffset = verts.Count;
            verts.AddRange(branchMesh.vertices);
            foreach (int idx in branchMesh.triangles) tris.Add(vOffset + idx);

            var mesh = new Mesh { name = "Trench_Modular_Junction" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Generates a rounded earthen end cap to seal off dead-end trenches cleanly.</summary>
        public static Mesh BuildModularEndCapMesh(float width = 2.6f, float parapetH = 2.4f, float skirtDepth = 1.4f)
        {
            float len = 2.4f;
            Vector3[] path = new Vector3[]
            {
                new Vector3(0f, 0f, -len),
                Vector3.zero
            };
            Mesh mesh = BuildEdgeMesh(path, width, parapetH, skirtDepth, Vector3.right);
            var verts = new List<Vector3>(mesh.vertices);
            var tris = new List<int>(mesh.triangles);

            // Rounded end mound
            Box(verts, tris, new Vector3(0f, parapetH * 0.35f, 0.7f), new Vector3(width * 1.8f, parapetH * 0.7f, 1.4f));

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.name = "Trench_Modular_EndCap";
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Generates a low-poly earth/timber bunker or command dugout mesh.</summary>
        public static Mesh BuildBunkerMesh()
        {
            var mesh = new Mesh { name = "Trench_Bunker_Mesh" };

            float hw = 3.8f;
            float hl = 4.2f;
            float h = 3.0f;
            float skirt = 1.4f;

            Vector3[] vertices = new Vector3[]
            {
                // Top roof (y = h)
                new Vector3(-hw * 0.72f, h, -hl * 0.72f),
                new Vector3( hw * 0.72f, h, -hl * 0.72f),
                new Vector3( hw * 0.72f, h,  hl * 0.72f),
                new Vector3(-hw * 0.72f, h,  hl * 0.72f),

                // Base perimeter (y = -skirt)
                new Vector3(-hw * 1.25f, -skirt, -hl * 1.25f),
                new Vector3( hw * 1.25f, -skirt, -hl * 1.25f),
                new Vector3( hw * 1.25f, -skirt,  hl * 1.25f),
                new Vector3(-hw * 1.25f, -skirt,  hl * 1.25f),

                // Front firing embrasure visor (front is +Z)
                new Vector3(-1.2f, 1.7f, hl * 0.78f),
                new Vector3( 1.2f, 1.7f, hl * 0.78f),
                new Vector3( 1.2f, 1.1f, hl * 0.78f),
                new Vector3(-1.2f, 1.1f, hl * 0.78f)
            };

            int[] triangles = new int[]
            {
                0, 2, 1,  0, 3, 2,
                0, 1, 5,  0, 5, 4,
                1, 2, 6,  1, 6, 5,
                3, 7, 6,  3, 6, 2,
                0, 4, 7,  0, 7, 3,
                8, 10, 9, 8, 11, 10
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Generates a low-poly revetted heavy weapon pit mesh.</summary>
        public static Mesh BuildWeaponPitMesh()
        {
            var mesh = new Mesh { name = "Trench_WeaponPit_Mesh" };

            const int segments = 8;
            float innerRadius = 3.0f;
            float crestRadius = 4.6f;
            float skirtRadius = 6.4f;
            float depth = 1.1f;
            float crestH = 1.5f;
            float skirtDepth = 1.4f;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            verts.Add(new Vector3(0f, -depth, 0f));

            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Sin(angle) * innerRadius, -depth, Mathf.Cos(angle) * innerRadius));
            }

            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                float bump = 1f + 0.12f * Mathf.Sin(angle * 3.7f);
                verts.Add(new Vector3(Mathf.Sin(angle) * crestRadius, crestH * bump, Mathf.Cos(angle) * crestRadius));
            }

            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Sin(angle) * skirtRadius, -skirtDepth, Mathf.Cos(angle) * skirtRadius));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                tris.Add(0);
                tris.Add(1 + i);
                tris.Add(1 + next);
            }

            int crestBase = 1 + segments;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int f0 = 1 + i, f1 = 1 + next, c0 = crestBase + i, c1 = crestBase + next;
                tris.Add(f0); tris.Add(c0); tris.Add(c1);
                tris.Add(f0); tris.Add(c1); tris.Add(f1);
            }

            int skirtBase = crestBase + segments;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int c0 = crestBase + i, c1 = crestBase + next, s0 = skirtBase + i, s1 = skirtBase + next;
                tris.Add(c0); tris.Add(s0); tris.Add(s1);
                tris.Add(c0); tris.Add(s1); tris.Add(c1);
            }

            mesh.vertices = verts.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Generates an undercut drone shelter or foxhole mesh.</summary>
        public static Mesh BuildFoxholeMesh()
        {
            var mesh = new Mesh { name = "Trench_Foxhole_Mesh" };

            float r = 1.7f;
            float crestH = 0.85f;
            float floorY = -0.7f;
            float skirtY = -1.0f;
            int segs = 6;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            verts.Add(new Vector3(0f, floorY, 0f));

            for (int i = 0; i < segs; i++)
            {
                float angle = (i / (float)segs) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Sin(angle) * r, floorY, Mathf.Cos(angle) * r));
            }

            for (int i = 0; i < segs; i++)
            {
                float angle = (i / (float)segs) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Sin(angle) * (r * 1.5f), crestH, Mathf.Cos(angle) * (r * 1.5f)));
            }

            for (int i = 0; i < segs; i++)
            {
                float angle = (i / (float)segs) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Sin(angle) * (r * 2.3f), skirtY, Mathf.Cos(angle) * (r * 2.3f)));
            }

            for (int i = 0; i < segs; i++)
            {
                int next = (i + 1) % segs;
                tris.Add(0);
                tris.Add(1 + i);
                tris.Add(1 + next);

                int f0 = 1 + i, f1 = 1 + next;
                int c0 = 1 + segs + i, c1 = 1 + segs + next;
                int s0 = 1 + segs * 2 + i, s1 = 1 + segs * 2 + next;

                tris.Add(f0); tris.Add(c0); tris.Add(c1);
                tris.Add(f0); tris.Add(c1); tris.Add(f1);

                tris.Add(c0); tris.Add(s0); tris.Add(s1);
                tris.Add(c0); tris.Add(s1); tris.Add(c1);
            }

            mesh.vertices = verts.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
