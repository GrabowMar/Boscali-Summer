using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>CPU-only Editor check for the actual generated meshes, including their shaded winding.</summary>
    public static class WorldDetailMeshCheck
    {
        [Serializable]
        private sealed class MeshResult
        {
            public string name;
            public int vertices, triangles, submeshes;
            public Vector3 minimum, maximum, minimumBoundsPadding;
            public float smallestTriangleArea, smallestUvTriangleArea, smallestWindingAlignment;
        }

        [Serializable]
        private sealed class Report
        {
            public string status = "PASS";
            public MeshResult[] meshes;
        }

        public static string Run()
        {
            Mesh[] meshes = WorldDetailMeshes.Build();
            try
            {
                Require(meshes != null && meshes.Length == 5, "Expected five authored meshes.");
                int[] expectedTriangles = { 164, 142, 186, 96, 104 };
                int[] expectedVertices = { 328, 284, 372, 70, 135 };
                var results = new MeshResult[meshes.Length];
                var summary = new StringBuilder("PASS world-detail meshes:");
                for (int i = 0; i < meshes.Length; i++)
                {
                    Mesh mesh = meshes[i];
                    Require(mesh != null && mesh.isReadable, "Mesh " + i + " must be present and readable for validation.");
                    Vector3[] vertices = mesh.vertices, normals = mesh.normals;
                    Vector2[] uvs = mesh.uv;
                    Color[] colours = mesh.colors;
                    Require(mesh.subMeshCount == 1 && mesh.GetTopology(0) == MeshTopology.Triangles,
                        mesh.name + ": expected one triangle submesh.");
                    int[] indices = mesh.GetTriangles(0);
                    Require(vertices.Length == expectedVertices[i] && indices.Length == expectedTriangles[i] * 3,
                        mesh.name + ": unexpected vertex or triangle budget: " + vertices.Length + "/" + indices.Length / 3 + ".");
                    Require(normals.Length == vertices.Length && uvs.Length == vertices.Length && colours.Length == vertices.Length,
                        mesh.name + ": every vertex requires a normal, UV and colour.");
                    Bounds bounds = mesh.bounds;
                    Require(Finite(bounds.min) && Finite(bounds.max), mesh.name + ": non-finite bounds.");
                    Vector3 minimum = vertices[0], maximum = vertices[0];
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        Require(Finite(vertices[v]) && Finite(normals[v]) && Finite(uvs[v].x) && Finite(uvs[v].y) &&
                            Finite(colours[v].r) && Finite(colours[v].g) && Finite(colours[v].b) && Finite(colours[v].a),
                            mesh.name + ": non-finite vertex channel at " + v + ".");
                        Require(normals[v].sqrMagnitude > .000001f,
                            mesh.name + ": zero-length normal at " + v + ".");
                        Require(bounds.Contains(vertices[v]), mesh.name + ": vertex outside bounds at " + v + ".");
                        Require(uvs[v].x >= 0f && uvs[v].x <= 1f && uvs[v].y >= 0f && uvs[v].y <= 1f,
                            mesh.name + ": UV outside the shared atlas at " + v + ".");
                        minimum = Vector3.Min(minimum, vertices[v]); maximum = Vector3.Max(maximum, vertices[v]);
                    }
                    float smallestArea = float.PositiveInfinity, smallestUvArea = float.PositiveInfinity,
                        smallestAlignment = float.PositiveInfinity;
                    for (int t = 0; t < indices.Length; t += 3)
                    {
                        int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                        Require(a >= 0 && a < vertices.Length && b >= 0 && b < vertices.Length && c >= 0 && c < vertices.Length,
                            mesh.name + ": triangle index outside vertex array at " + t / 3 + ".");
                        Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                        Require(Finite(cross) && cross.sqrMagnitude > .000000000001f,
                            mesh.name + ": degenerate triangle at " + t / 3 + ".");
                        Vector2 uvAB = uvs[b] - uvs[a], uvAC = uvs[c] - uvs[a];
                        float uvArea = Mathf.Abs(uvAB.x * uvAC.y - uvAB.y * uvAC.x) * .5f;
                        Require(Finite(uvArea) && uvArea > .0000000001f,
                            mesh.name + ": collapsed texture UV triangle at " + t / 3 + ".");
                        Vector3 shading = normals[a] + normals[b] + normals[c];
                        float alignment = Vector3.Dot(cross.normalized, shading.normalized);
                        Require(Finite(alignment) && alignment > 0f,
                            mesh.name + ": triangle winding opposes authored normals at " + t / 3 + ".");
                        smallestArea = Mathf.Min(smallestArea, cross.magnitude * .5f);
                        smallestUvArea = Mathf.Min(smallestUvArea, uvArea);
                        smallestAlignment = Mathf.Min(smallestAlignment, alignment);
                    }
                    Vector3 padding = Vector3.Min(minimum - bounds.min, bounds.max - maximum);
                    Require(padding.x >= .049f && padding.y >= .049f && padding.z >= .049f,
                        mesh.name + ": bounds must retain the authored 5 cm wind margin.");
                    results[i] = new MeshResult
                    {
                        name = mesh.name, vertices = vertices.Length, triangles = indices.Length / 3, submeshes = mesh.subMeshCount,
                        minimum = minimum, maximum = maximum, minimumBoundsPadding = padding,
                        smallestTriangleArea = smallestArea, smallestUvTriangleArea = smallestUvArea,
                        smallestWindingAlignment = smallestAlignment
                    };
                    summary.Append(' ').Append(mesh.name).Append('=').Append(indices.Length / 3).Append("tris/")
                        .Append(vertices.Length).Append("verts;");
                }
                string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "world-detail-meshes.json");
                File.WriteAllText(reportPath, JsonUtility.ToJson(new Report { meshes = results }, true));
                return summary.Append(" report=").Append(reportPath).ToString();
            }
            finally
            {
                if (meshes != null)
                    foreach (Mesh mesh in meshes)
                        if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("World detail geometry check: " + message);
        }
    }
}
