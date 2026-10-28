using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Space;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// The three birds as built in code (2026-10-28; the old one was a 12 m box with two flat plates, unlit): OPTICAL a telescope tube under a foil bus,
    /// aperture and sunshade to nadir; RADAR a foil bus over a long side-looking array tilted 25 degrees; KINETIC a hexagonal bus over a magazine of seven
    /// rods, tips down. Metres, Y up (the sensor face looks down), wings along X. Each bird is a body mesh and a wing mesh that turns about X to face the
    /// sun, both with four submeshes: gold foil, solar cells (cell texture, white backs), white frame, dark metal. Lit by the scene's sun.
    /// </summary>
    internal static class SatelliteModels
    {
        internal const int Foil = 0, Cells = 1, Frame = 2, Dark = 3;

        internal static Mesh Body(BirdKind kind)
        {
            var k = new Kit();
            switch (kind)
            {
                case BirdKind.Optical:
                    k.Box(Foil, new Vector3(0f, 2.2f, 0f), new Vector3(2.8f, 2.4f, 2.8f));
                    k.Tube(Frame, new Vector3(0f, 1f, 0f), new Vector3(0f, -5.5f, 0f), 1.45f, 20);
                    k.Tube(Dark, new Vector3(0f, -5.5f, 0f), new Vector3(0f, -5.56f, 0f), 1.38f, 20);           // the aperture, just proud of the tube's end
                    k.Tube(Frame, new Vector3(0f, -5.5f, 0f), new Vector3(0f, -7.2f, 0.4f), 1.6f, 20, false);   // sunshade lip, cut on a slant
                    k.Box(Frame, new Vector3(0f, 3.6f, 0f), new Vector3(1.6f, 0.4f, 1.6f));                     // radiator plate
                    Dish(k, new Vector3(0.9f, 3.4f, 1.6f), new Vector3(0.5f, 0.3f, 1f), 1.1f);
                    break;
                case BirdKind.Radar:
                    k.Box(Foil, new Vector3(0f, 1.2f, 0f), new Vector3(3f, 2.6f, 3.4f));
                    k.Box(Frame, new Vector3(0f, -0.3f, 0f), new Vector3(0.5f, 0.6f, 0.5f));                    // mast to the array
                    Quaternion tilt = Quaternion.AngleAxis(25f, Vector3.forward);
                    k.Box(Dark, new Vector3(0.4f, -0.8f, 0f), new Vector3(2.6f, 0.22f, 15f), tilt);              // the array
                    for (int i = -3; i <= 3; i++)                                                                 // its rib frame
                        k.Box(Frame, new Vector3(0.4f, -0.66f, i * 2.1f), new Vector3(2.7f, 0.08f, 0.12f), tilt);
                    k.Box(Frame, new Vector3(0f, 2.65f, 0f), new Vector3(2.2f, 0.3f, 2.6f));
                    Dish(k, new Vector3(-0.8f, 2.9f, -1.4f), new Vector3(-0.3f, 1f, -0.4f), 0.8f);
                    break;
                default:
                    k.Tube(Foil, new Vector3(0f, 3.2f, 0f), new Vector3(0f, -0.6f, 0f), 1.7f, 6);              // hexagonal bus
                    k.Tube(Frame, new Vector3(0f, -0.6f, 0f), new Vector3(0f, -1.0f, 0f), 1.5f, 6);             // magazine plate
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        var p = i == 6 ? Vector3.zero : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.82f;
                        k.Tube(Dark, p + new Vector3(0f, -1f, 0f), p + new Vector3(0f, -6.4f, 0f), 0.34f, 8);
                        k.Cone(Dark, p + new Vector3(0f, -6.4f, 0f), p + new Vector3(0f, -7.4f, 0f), 0.34f, 8);
                    }
                    k.Tube(Frame, new Vector3(0f, -3.6f, 0f), new Vector3(0f, -3.9f, 0f), 1.4f, 12);            // the band round the rods
                    k.Cone(Dark, new Vector3(0f, 3.2f, 0f), new Vector3(0f, 4.6f, 0f), 0.75f, 12);             // thruster housing
                    break;
            }
            return k.Build("BoscaliBird:" + kind);
        }

        /// <summary>The two wings and their yokes, pivoting about X at the origin.</summary>
        internal static Mesh Wings(BirdKind kind)
        {
            var k = new Kit();
            int panels = kind == BirdKind.Radar ? 4 : kind == BirdKind.Optical ? 3 : 2;
            float root = kind == BirdKind.Kinetic ? 1.75f : 1.5f, width = kind == BirdKind.Radar ? 2.6f : 2.2f, length = 2.5f, gap = 0.12f;
            for (int side = -1; side <= 1; side += 2)
            {
                k.Box(Frame, new Vector3(side * (root + 0.6f), 0f, 0f), new Vector3(1.2f, 0.09f, 0.09f));     // yoke
                for (int i = 0; i < panels; i++)
                {
                    float x = side * (root + 1.2f + gap + i * (length + gap) + length * 0.5f);
                    k.Panel(new Vector3(x, 0f, 0f), new Vector2(length, width), 0.05f);
                }
            }
            return k.Build("BoscaliWings:" + kind);
        }

        /// <summary>One material per submesh, in submesh order; the caller owns (and destroys) them and their textures.</summary>
        internal static Material[] Materials()
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            return new[]
            {
                // Half metallic: fully metallic foil mirrors black space and goes brown; this keeps a gold diffuse under the sun and a sheen.
                Make(lit, unlit, "BoscaliSatFoil", new Color(1f, 0.76f, 0.34f), 0.55f, 0.55f, FoilTexture()),
                Make(lit, unlit, "BoscaliSatCells", new Color(0.6f, 0.68f, 0.95f), 0.25f, 0.78f, CellTexture()),
                Make(lit, unlit, "BoscaliSatFrame", new Color(0.86f, 0.87f, 0.88f), 0.05f, 0.42f, null),
                Make(lit, unlit, "BoscaliSatDark", new Color(0.13f, 0.135f, 0.15f), 0.4f, 0.6f, null),
            };
        }

        private static Material Make(Shader lit, Shader unlit, string name, Color colour, float metallic, float smoothness, Texture2D map)
        {
            var m = new Material(lit != null ? lit : unlit) { name = name };
            m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Color")) m.SetColor("_Color", colour);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (map != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", map);
            return m;
        }

        private static void Dish(Kit k, Vector3 at, Vector3 facing, float radius)
        {
            Vector3 n = facing.normalized;
            k.Cone(Frame, at + n * 0.35f, at, radius, 16);             // the shallow bowl, rim forward
            k.Tube(Frame, at, at - n * 0.5f, 0.08f, 6);                // its boom
            k.Tube(Dark, at + n * 0.35f, at + n * 0.75f, 0.06f, 6);    // the feed
        }

        // Dark blue cells, 4 x 4 to a tile (one tile per 1.6 m), thin silver gaps and a busbar across each cell.
        private static Texture2D CellTexture()
        {
            const int res = 128, cell = 32;
            var t = new Texture2D(res, res, TextureFormat.RGBA32, true) { name = "BoscaliSatCellsTex", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[res * res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    int cx = x % cell, cy = y % cell;
                    bool gap = cx < 2 || cy < 2;
                    bool bar = cy == cell / 2 || cy == cell / 2 + 1;
                    float shade = 0.85f + 0.15f * Mathf.PerlinNoise(x / 9f + (x / cell) * 3.1f, y / 9f);
                    px[y * res + x] = gap ? new Color(0.62f, 0.64f, 0.68f) : bar ? new Color(0.26f, 0.3f, 0.42f) : new Color(0.07f * shade, 0.11f * shade, 0.27f * shade);
                }
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }

        // Crinkled multi-layer insulation: soft folds and sharp creases in brightness, tinted gold by the material.
        private static Texture2D FoilTexture()
        {
            const int res = 128;
            var t = new Texture2D(res, res, TextureFormat.RGBA32, true) { name = "BoscaliSatFoilTex", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[res * res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float u = x / (float)res * 6f, v = y / (float)res * 6f;
                    float fold = Mathf.PerlinNoise(u, v) * 0.6f + Mathf.PerlinNoise(u * 2.7f + 5f, v * 2.7f) * 0.4f;
                    float crease = 1f - Mathf.Abs(Mathf.PerlinNoise(u * 1.9f + 11f, v * 1.9f + 3f) * 2f - 1f);
                    float b = 0.62f + 0.38f * fold + 0.25f * Mathf.Pow(crease, 8f);
                    px[y * res + x] = new Color(b, b, b, 1f);
                }
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }

        /// <summary>Flat-shaded mesh builder: every face has its own vertices and normal; UVs are face-planar in metres.</summary>
        private sealed class Kit
        {
            private readonly List<Vector3> verts = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int>[] tris = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };

            /// <summary>A quad a b c d (in order round its edge) facing <paramref name="outward"/>; UVs are metres along a-b and a-d times <paramref name="uvPerMetre"/>.</summary>
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, float uvPerMetre = 0.6f)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { (b, d) = (d, b); }
                Vector3 u = (b - a).normalized, w = (d - a).normalized, n = outward.normalized;
                int i = verts.Count;
                foreach (Vector3 p in new[] { a, b, c, d })
                {
                    verts.Add(p); normals.Add(n);
                    uvs.Add(new Vector2(Vector3.Dot(p - a, u), Vector3.Dot(p - a, w)) * uvPerMetre);
                }
                tris[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { (b, c) = (c, b); }
                int i = verts.Count;
                Vector3 n = outward.normalized;
                foreach (Vector3 p in new[] { a, b, c }) { verts.Add(p); normals.Add(n); uvs.Add(new Vector2(p.x + p.z, p.y) * 0.6f); }
                tris[sub].AddRange(new[] { i, i + 1, i + 2 });
            }

            public void Box(int sub, Vector3 centre, Vector3 size) => Box(sub, centre, size, Quaternion.identity);

            public void Box(int sub, Vector3 centre, Vector3 size, Quaternion rotation)
            {
                Vector3 h = size * 0.5f;
                Vector3 P(float x, float y, float z) => centre + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
                Vector3 R(Vector3 v) => rotation * v;
                Quad(sub, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), R(Vector3.up));
                Quad(sub, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), R(Vector3.down));
                Quad(sub, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), R(Vector3.right));
                Quad(sub, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), R(Vector3.left));
                Quad(sub, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), R(Vector3.forward));
                Quad(sub, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), R(Vector3.back));
            }

            /// <summary>A solar panel in the XZ plane: cells on top (+Y), the white back below, frame edges.</summary>
            public void Panel(Vector3 centre, Vector2 size, float thick)
            {
                float hx = size.x * 0.5f, hz = size.y * 0.5f, hy = thick * 0.5f;
                Vector3 P(float x, float y, float z) => centre + new Vector3(x * hx, y * hy, z * hz);
                Quad(Cells, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Vector3.up, 1f / 1.6f);
                Quad(Frame, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), Vector3.down);
                Quad(Frame, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Vector3.right);
                Quad(Frame, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), Vector3.left);
                Quad(Frame, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Vector3.forward);
                Quad(Frame, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), Vector3.back);
            }

            /// <summary>A prism from <paramref name="a"/> to <paramref name="b"/> (sides = 6 gives a hexagon); end caps unless <paramref name="caps"/> is false.</summary>
            public void Tube(int sub, Vector3 a, Vector3 b, float radius, int sides, bool caps = true)
            {
                Vector3 axis = (b - a).normalized;
                Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
                Vector3 up = Vector3.Cross(side, axis);
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * 2f * Mathf.PI / sides, a1 = (i + 1) * 2f * Mathf.PI / sides;
                    Vector3 r0 = (side * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * radius, r1 = (side * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * radius;
                    Quad(sub, a + r0, a + r1, b + r1, b + r0, (r0 + r1).normalized);
                    if (!caps) continue;
                    Tri(sub, a, a + r0, a + r1, -axis);
                    Tri(sub, b, b + r0, b + r1, axis);
                }
            }

            /// <summary>A cone from a base disc at <paramref name="baseCentre"/> to <paramref name="tip"/>, with its base capped.</summary>
            public void Cone(int sub, Vector3 baseCentre, Vector3 tip, float radius, int sides)
            {
                Vector3 axis = (tip - baseCentre).normalized;
                Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
                Vector3 up = Vector3.Cross(side, axis);
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * 2f * Mathf.PI / sides, a1 = (i + 1) * 2f * Mathf.PI / sides;
                    Vector3 r0 = (side * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * radius, r1 = (side * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * radius;
                    Vector3 outward = Vector3.Cross(r1 - r0, tip - (baseCentre + r0));
                    if (Vector3.Dot(outward, r0 + r1) < 0f) outward = -outward;
                    Tri(sub, baseCentre + r0, baseCentre + r1, tip, outward);
                    Tri(sub, baseCentre, baseCentre + r0, baseCentre + r1, -axis);
                }
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(verts);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = tris.Length;
                for (int i = 0; i < tris.Length; i++) mesh.SetTriangles(tris[i], i);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
