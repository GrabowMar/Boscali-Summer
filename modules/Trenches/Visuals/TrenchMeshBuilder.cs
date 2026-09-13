using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Visuals
{
    /// <summary>
    /// Generates lightweight, procedural low-poly meshes for dynamic trenches and fortifications.
    /// Uses raised berms and deep downward skirts to conform seamlessly across terrain slopes
    /// without modifying terrain heightmaps.
    /// </summary>
    internal static class TrenchMeshBuilder
    {
        /// <summary>Rectangular revetted fighting bay with an open front, not a buried ring.</summary>
        public static Mesh BuildFightingBayMesh()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            // Raised timber-sized courses in a U, with staggered ends and a dry floor.
            for (int row = 0; row < 3; row++)
            {
                float y = 0.2f + row * 0.35f;
                Box(vertices, triangles, new Vector3(-2.1f, y, 0), new Vector3(0.85f, 0.36f, 5.2f - row * 0.25f));
                Box(vertices, triangles, new Vector3(2.1f, y, 0), new Vector3(0.85f, 0.36f, 5.2f - row * 0.25f));
                Box(vertices, triangles, new Vector3(0, y, -2.2f), new Vector3(4.2f, 0.36f, 0.85f));
            }
            for (int plank = 0; plank < 9; plank++)
                Box(vertices, triangles, new Vector3(0, 0.09f, -1.6f + plank * 0.4f), new Vector3(3.3f, 0.12f, 0.34f));
            var mesh = new Mesh { name = "Trench_RevettedBay" };
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
        /// <summary>
        /// Extrudes a continuous raised berm and trench trough along a path polyline.
        /// </summary>
        public static Mesh BuildEdgeMesh(Vector3[] path, float width, float parapetHeight, float skirtDepth, Vector3 threatDir)
        {
            if (path == null || path.Length < 2) return null;

            var mesh = new Mesh { name = "Trench_Edge_Mesh" };

            float halfW = width * 0.5f;
            float stepW = width * 0.28f;
            float bermW = 1.6f;
            float skirtW = 1.8f;
            float paradosH = parapetHeight * 0.75f;

            // 7 profile points per cross-section ring:
            // 0: Left skirt tip (ground penetration)
            // 1: Left parados crest
            // 2: Left trench floor
            // 3: Firing step base
            // 4: Firing step top
            // 5: Right parapet crest (forward toward threat)
            // 6: Right skirt tip (ground penetration)
            int ringSize = 7;
            int ringCount = path.Length;
            var vertices = new Vector3[ringCount * ringSize];
            var uvs = new Vector2[ringCount * ringSize];
            var triangles = new List<int>((ringCount - 1) * (ringSize - 1) * 6);

            float accumulatedLength = 0f;
            // Choose the parapet side once for the whole strip. Flipping individual
            // rings on an S-curve twists the walls through the corridor.
            bool flipProfile = Vector3.Dot(Vector3.Cross(Vector3.up, path[1] - path[0]), threatDir) < 0f;

            for (int r = 0; r < ringCount; r++)
            {
                Vector3 pt = path[r];
                if (r > 0)
                {
                    accumulatedLength += Vector3.Distance(pt, path[r - 1]);
                }

                // Compute tangent vector
                Vector3 forward;
                if (r == 0) forward = (path[1] - path[0]).normalized;
                else if (r == ringCount - 1) forward = (path[ringCount - 1] - path[ringCount - 2]).normalized;
                else forward = ((path[r + 1] - pt).normalized + (pt - path[r - 1]).normalized).normalized;

                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                if (right.sqrMagnitude < 0.001f) right = Vector3.right;

                // Ensure right points toward threat direction for the parapet
                if (flipProfile)
                {
                    right = -right;
                }
                Vector3 left = -right;

                int baseIdx = r * ringSize;

                // Relative ring points (local Y relative to pt.y)
                vertices[baseIdx + 0] = pt + left * (halfW + bermW + skirtW) + Vector3.down * skirtDepth;
                vertices[baseIdx + 1] = pt + left * (halfW + 0.3f) + Vector3.up * paradosH;
                vertices[baseIdx + 2] = pt + left * halfW + Vector3.up * 0.05f;
                vertices[baseIdx + 3] = pt + (right * (halfW - stepW)) + Vector3.up * 0.05f;
                vertices[baseIdx + 4] = pt + (right * (halfW - stepW)) + Vector3.up * 0.45f;
                vertices[baseIdx + 5] = pt + right * (halfW + 0.3f) + Vector3.up * parapetHeight;
                vertices[baseIdx + 6] = pt + right * (halfW + bermW + skirtW) + Vector3.down * skirtDepth;

                float u = accumulatedLength * 0.5f;
                uvs[baseIdx + 0] = new Vector2(0.0f, u);
                uvs[baseIdx + 1] = new Vector2(0.2f, u);
                uvs[baseIdx + 2] = new Vector2(0.35f, u);
                uvs[baseIdx + 3] = new Vector2(0.5f, u);
                uvs[baseIdx + 4] = new Vector2(0.65f, u);
                uvs[baseIdx + 5] = new Vector2(0.85f, u);
                uvs[baseIdx + 6] = new Vector2(1.0f, u);
            }

            // Connect rings with quads
            for (int r = 0; r < ringCount - 1; r++)
            {
                int r0 = r * ringSize;
                int r1 = (r + 1) * ringSize;
                // Facing the threat can mirror the cross-section. Mirror triangle
                // winding too, otherwise the entire berm is back-face culled from above.
                bool mirrored = Vector3.Cross(path[r + 1] - path[r],
                    vertices[r0 + 6] - vertices[r0]).y < 0f;

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

        /// <summary>
        /// Generates a low-poly earth/timber bunker or command dugout mesh (Blindage).
        /// </summary>
        public static Mesh BuildBunkerMesh()
        {
            var mesh = new Mesh { name = "Trench_Bunker_Mesh" };

            float hw = 2.8f; // half width
            float hl = 3.2f; // half length
            float h = 2.4f;  // height
            float skirt = 1.0f;

            // Octagonal bunker with sloped earth ramparts
            Vector3[] vertices = new Vector3[]
            {
                // Top roof (y = h)
                new Vector3(-hw * 0.7f, h, -hl * 0.7f),
                new Vector3( hw * 0.7f, h, -hl * 0.7f),
                new Vector3( hw * 0.7f, h,  hl * 0.7f),
                new Vector3(-hw * 0.7f, h,  hl * 0.7f),

                // Base perimeter (y = -skirt)
                new Vector3(-hw * 1.3f, -skirt, -hl * 1.3f),
                new Vector3( hw * 1.3f, -skirt, -hl * 1.3f),
                new Vector3( hw * 1.3f, -skirt,  hl * 1.3f),
                new Vector3(-hw * 1.3f, -skirt,  hl * 1.3f),

                // Front firing embrasure visor (front is +Z)
                new Vector3(-0.9f, 1.3f, hl * 0.75f),
                new Vector3( 0.9f, 1.3f, hl * 0.75f),
                new Vector3( 0.9f, 0.9f, hl * 0.75f),
                new Vector3(-0.9f, 0.9f, hl * 0.75f)
            };

            int[] triangles = new int[]
            {
                // Roof
                0, 2, 1,  0, 3, 2,

                // Sides
                0, 1, 5,  0, 5, 4, // Rear wall
                1, 2, 6,  1, 6, 5, // Right wall
                3, 7, 6,  3, 6, 2, // Front wall
                0, 4, 7,  0, 7, 3, // Left wall

                // Firing slit cutout front
                8, 10, 9, 8, 11, 10
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// Generates a low-poly revetted circular/octagonal heavy weapon pit mesh.
        /// </summary>
        public static Mesh BuildWeaponPitMesh()
        {
            var mesh = new Mesh { name = "Trench_WeaponPit_Mesh" };

            const int segments = 8;
            float innerRadius = 2.4f;
            float crestRadius = 3.6f;
            float skirtRadius = 5.0f;
            float depth = 0.9f;
            float crestH = 1.3f;
            float skirtDepth = 1.0f;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            // Center floor point
            verts.Add(new Vector3(0f, -depth, 0f)); // Index 0

            // Inner floor ring
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Sin(angle) * innerRadius, -depth, Mathf.Cos(angle) * innerRadius));
            }

            // Crest ring
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Sin(angle) * crestRadius, crestH, Mathf.Cos(angle) * crestRadius));
            }

            // Outer skirt ring
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Sin(angle) * skirtRadius, -skirtDepth, Mathf.Cos(angle) * skirtRadius));
            }

            // Floor triangles
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                tris.Add(0);
                tris.Add(1 + i);
                tris.Add(1 + next);
            }

            // Pit wall (floor to crest)
            int crestBase = 1 + segments;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int f0 = 1 + i;
                int f1 = 1 + next;
                int c0 = crestBase + i;
                int c1 = crestBase + next;

                tris.Add(f0);
                tris.Add(c0);
                tris.Add(c1);

                tris.Add(f0);
                tris.Add(c1);
                tris.Add(f1);
            }

            // Crest to outer skirt
            int skirtBase = crestBase + segments;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int c0 = crestBase + i;
                int c1 = crestBase + next;
                int s0 = skirtBase + i;
                int s1 = skirtBase + next;

                tris.Add(c0);
                tris.Add(s0);
                tris.Add(s1);

                tris.Add(c0);
                tris.Add(s1);
                tris.Add(c1);
            }

            mesh.vertices = verts.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// Generates an undercut drone shelter or foxhole mesh.
        /// </summary>
        public static Mesh BuildFoxholeMesh()
        {
            var mesh = new Mesh { name = "Trench_Foxhole_Mesh" };

            float r = 1.4f;
            float crestH = 0.65f;
            float floorY = -0.6f;
            float skirtY = -0.8f;
            int segs = 6;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            verts.Add(new Vector3(0f, floorY, 0f)); // 0: floor center

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

                int f0 = 1 + i;
                int f1 = 1 + next;
                int c0 = 1 + segs + i;
                int c1 = 1 + segs + next;
                int s0 = 1 + segs * 2 + i;
                int s1 = 1 + segs * 2 + next;

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
