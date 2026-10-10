using System.Collections.Generic;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.FireAndDestruction.Domain;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    internal static class FractureGeometry
    {
        internal static Mesh Section(uint seed)
        {
            var vertices = new List<Vector3>(96);
            var triangles = new List<int>(96);
            const int sides = 7;
            var upper = new Vector3[sides];
            var lower = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                float radius = 0.42f + Rand(seed, i) * 0.13f;
                upper[i] = new Vector3(Mathf.Cos(angle) * radius, 0.36f + Rand(seed, i + 17) * 0.14f,
                    Mathf.Sin(angle) * radius);
                lower[i] = new Vector3(upper[i].x * 0.88f, -0.42f + Rand(seed, i + 31) * 0.08f, upper[i].z * 0.92f);
            }
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                Triangle(vertices, triangles, Vector3.up * 0.45f, upper[next], upper[i]);
                Triangle(vertices, triangles, Vector3.down * 0.39f, lower[i], lower[next]);
                Triangle(vertices, triangles, upper[i], upper[next], lower[next]);
                Triangle(vertices, triangles, upper[i], lower[next], lower[i]);
            }
            return Mesh(vertices, triangles, null, "Fractured concrete section");
        }

        private static void Triangle(List<Vector3> vertices, List<int> indices, Vector3 a, Vector3 b, Vector3 c)
        {
            int index = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            indices.Add(index); indices.Add(index + 1); indices.Add(index + 2);
        }

        private static Mesh Mesh(List<Vector3> vertices, List<int> concrete, List<int> steel, string name)
        {
            var uv = new Vector2[vertices.Count];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(vertices[i].x + vertices[i].z * 0.17f, vertices[i].y + vertices[i].z * 0.61f);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices); mesh.uv = uv;
            mesh.subMeshCount = steel == null ? 1 : 2;
            mesh.SetTriangles(concrete, 0);
            if (steel != null) mesh.SetTriangles(steel, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        internal static float Rand(uint seed, int channel) =>
            Deterministic.UnitFloat(Deterministic.Hash(unchecked((int)seed), channel, 137));
    }
}
