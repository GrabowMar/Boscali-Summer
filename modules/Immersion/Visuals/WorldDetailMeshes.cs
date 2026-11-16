using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>Small authored meshes; build once, then share between instanced draws.</summary>
    internal static class WorldDetailMeshes
    {
        internal const int ShrubWide = 0, ShrubLoose = 1, ShrubTall = 2, Rock = 3, Log = 4, ShrubCount = 3;

        internal static Mesh[] Build() => new[]
        {
            Bush(1701, 7, .73f, .83f, .47f, false, "Broadleaf"),
            Bush(3109, 6, .58f, 1.12f, .43f, true, "Open Brush"),
            Bush(7919, 8, .46f, 1.32f, .39f, true, "Upright Brush"),
            Stone(), FallenWood()
        };

        private static Mesh Bush(uint seed, int branches, float radius, float height, float leafSize,
            bool narrowLeaves, string name)
        {
            var mesh = new Builder();
            var random = new Seed(seed);
            Vector3 root = new Vector3(0f, .02f, 0f);
            Vector3 fork = new Vector3(.02f, height * .32f, -.025f);
            Branch(mesh, root, fork, .034f, .021f, 5, new Color(.68f, .62f, .49f));
            for (int branch = 0; branch < branches; branch++)
            {
                float angle = branch * 2.399963f + random.Range(-.2f, .2f);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 start = Vector3.Lerp(root, fork, random.Range(.4f, 1f));
                Vector3 end = outward * radius * random.Range(.65f, 1f) +
                    Vector3.up * height * random.Range(.57f, 1f);
                Branch(mesh, start, end, .018f, .004f, 4, new Color(.72f, .68f, .52f));
                for (int leaf = 0; leaf < 7; leaf++)
                {
                    float along = .48f + leaf * .082f;
                    Vector3 centre = Vector3.Lerp(start, end, along) +
                        new Vector3(random.Range(-.15f, .15f), random.Range(-.07f, .1f), random.Range(-.15f, .15f));
                    Vector3 normal = (outward + Vector3.up * random.Range(.65f, 1.6f) +
                        new Vector3(random.Range(-.6f, .6f), 0f, random.Range(-.6f, .6f))).normalized;
                    float size = leafSize * random.Range(.72f, 1.15f);
                    Color tint = Color.Lerp(new Color(.56f, .65f, .48f), new Color(.94f, .98f, .82f),
                        Mathf.Clamp01(centre.y / height) * .7f + random.Range(0f, .25f));
                    Leaf(mesh, centre, normal, size, random.Range(-1.1f, 1.1f), narrowLeaves, tint);
                }
            }
            return mesh.Finish("Boscali " + name);
        }

        private static void Leaf(Builder mesh, Vector3 centre, Vector3 normal, float size, float twist,
            bool narrow, Color tint)
        {
            Vector3 right = Vector3.Cross(Vector3.up, normal).normalized;
            if (right.sqrMagnitude < .01f) right = Vector3.right;
            Vector3 up = Vector3.Cross(normal, right).normalized;
            Vector3 across = (right * Mathf.Cos(twist) + up * Mathf.Sin(twist)) * size * .5f;
            Vector3 along = (-right * Mathf.Sin(twist) + up * Mathf.Cos(twist)) * size * .5f;
            // Two atlas tiles contain many close, irregular leaves on one small branching spray.
            float v0 = narrow ? .015625f : .515625f, v1 = narrow ? .484375f : .984375f;
            Vector3 lightingNormal = (normal + Vector3.up * .8f).normalized;
            mesh.Quad(centre - across - along, centre + across - along, centre + across + along,
                centre - across + along, lightingNormal, tint,
                new Vector2(.015625f, v0), new Vector2(.484375f, v0),
                new Vector2(.484375f, v1), new Vector2(.015625f, v1));
        }

        private static void Branch(Builder mesh, Vector3 start, Vector3 end, float radius0, float radius1,
            int sides, Color tint, bool cap = false)
        {
            Vector3 axis = (end - start).normalized;
            Vector3 right = Vector3.Cross(axis, Vector3.forward).normalized;
            if (right.sqrMagnitude < .01f) right = Vector3.Cross(axis, Vector3.up).normalized;
            Vector3 up = Vector3.Cross(axis, right).normalized;
            for (int side = 0; side < sides; side++)
            {
                float a = side * Mathf.PI * 2f / sides, b = (side + 1) * Mathf.PI * 2f / sides;
                Vector3 r0 = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                Vector3 r1 = right * Mathf.Cos(b) + up * Mathf.Sin(b);
                mesh.Quad(start + r0 * radius0, start + r1 * radius0, end + r1 * radius1,
                    end + r0 * radius1, (r0 + r1).normalized, tint,
                    new Vector2(.515625f, .28f), new Vector2(.734375f, .28f),
                    new Vector2(.734375f, .984375f), new Vector2(.515625f, .984375f));
                if (cap)
                {
                    int middle = mesh.Vertex(end, axis, tint, new Vector2(.625f, .125f));
                    int first = mesh.Vertex(end + r0 * radius1, axis, tint,
                        new Vector2(.625f + Mathf.Cos(a) * .105f, .125f + Mathf.Sin(a) * .105f));
                    int second = mesh.Vertex(end + r1 * radius1, axis, tint,
                        new Vector2(.625f + Mathf.Cos(b) * .105f, .125f + Mathf.Sin(b) * .105f));
                    mesh.Triangle(middle, first, second, axis);
                }
            }
        }

        private static Mesh Stone()
        {
            const int sides = 10, rings = 5;
            var mesh = new Builder();
            var random = new Seed(4591);
            float[] widths = { .69f, .86f, .82f, .74f, .61f };
            float[] heights = { .015f, .13f, .39f, .53f, .63f };
            float[] offsetsX = { -.16f, -.1f, -.03f, .12f, .19f };
            float[] offsetsZ = { 0f, -.04f, -.04f, .02f, .08f };
            for (int ring = 0; ring < rings; ring++)
                for (int side = 0; side < sides; side++)
                {
                    float angle = side * Mathf.PI * 2f / sides + .075f * Mathf.Sin(ring * 1.9f + side);
                    float notch = side == 2 || side == 3 ? .82f : side == 7 ? 1.1f : 1f;
                    float radius = widths[ring] * random.Range(.92f, 1.06f) * notch;
                    float cosine = Mathf.Cos(angle), sine = Mathf.Sin(angle);
                    // Offset shelves and a chipped shoulder break the previous rounded egg silhouette.
                    Vector3 position = new Vector3(Mathf.Sign(cosine) * Mathf.Pow(Mathf.Abs(cosine), .72f) * radius * 1.15f + offsetsX[ring],
                        heights[ring] + (ring == 0 || ring == rings - 1 ? 0f : random.Range(-.025f, .025f)),
                        Mathf.Sign(sine) * Mathf.Pow(Mathf.Abs(sine), .72f) * radius * .68f + offsetsZ[ring]);
                    // The capped rings need end normals: radial normals can face away from
                    // a sloping cap triangle even when its geometric winding is outward.
                    Vector3 normal = ring == 0 ? Vector3.down : ring == rings - 1 ? Vector3.up :
                        new Vector3(Mathf.Cos(angle), (ring - 1) * .35f, Mathf.Sin(angle)).normalized;
                    float shade = random.Range(.78f, 1f) * Mathf.Lerp(.65f, 1f, ring / 4f);
                    mesh.Vertex(position, normal, new Color(shade, shade * .98f, shade * .91f),
                        new Vector2(.765625f + side / 10f * .21875f, .015625f + ring / 4f * .96875f));
                }
            for (int ring = 0; ring < rings - 1; ring++)
                for (int side = 0; side < sides; side++)
                {
                    int a = ring * sides + side, b = ring * sides + (side + 1) % sides;
                    Vector3 outward = new Vector3(Mathf.Cos((side + .5f) * Mathf.PI * 2f / sides),
                        (ring - 1) * .4f, Mathf.Sin((side + .5f) * Mathf.PI * 2f / sides));
                    mesh.Triangle(a, b, a + sides, outward);
                    mesh.Triangle(b, b + sides, a + sides, outward);
                }
            Vector3 minimum = mesh.Positions[0], maximum = minimum;
            for (int vertex = 1; vertex < sides * rings; vertex++)
            {
                minimum = Vector3.Min(minimum, mesh.Positions[vertex]);
                maximum = Vector3.Max(maximum, mesh.Positions[vertex]);
            }
            float planarScale = .21875f / (maximum.x - minimum.x);
            float centreZ = (minimum.z + maximum.z) * .5f;
            // Caps cannot share the side strip's constant-V ring UVs. Duplicate only
            // these rims and project in X/Z at uniform texel density within the stone tile.
            for (int cap = 0; cap < 2; cap++)
                for (int side = 0; side < sides; side++)
                {
                    int source = cap * (rings - 1) * sides + side;
                    Vector3 position = mesh.Positions[source];
                    mesh.CopyVertex(source, cap == 0 ? Vector3.down : Vector3.up,
                        new Vector2(.765625f + (position.x - minimum.x) * planarScale,
                            .5f + (position.z - centreZ) * planarScale));
                }
            for (int side = 1; side < sides - 1; side++)
            {
                mesh.Triangle(50, 50 + side, 51 + side, Vector3.down);
                mesh.Triangle(60, 60 + side, 61 + side, Vector3.up);
            }
            return mesh.Finish("Boscali Weathered Rock");
        }

        private static Mesh FallenWood()
        {
            const int sides = 8, rings = 5;
            var mesh = new Builder();
            var random = new Seed(7121);
            for (int ring = 0; ring < rings; ring++)
                for (int side = 0; side <= sides; side++)
                {
                    int radialSide = side % sides;
                    float angle = radialSide * Mathf.PI * 2f / sides;
                    float radius = Mathf.Lerp(.18f, .12f, ring / 4f) * (1f + .07f * Mathf.Sin(radialSide * 2.3f));
                    float brokenEnd = ring == 0 || ring == rings - 1 ? .055f * Mathf.Sin(radialSide * 2.7f) : 0f;
                    Vector3 normal = new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector3 position = new Vector3(-1.1f + ring * .55f + brokenEnd,
                        .19f + normal.y * radius + .035f * Mathf.Sin(ring), normal.z * radius);
                    float shade = random.Range(.75f, .98f) * (normal.y < -.1f ? .65f : 1f);
                    mesh.Vertex(position, normal, new Color(shade, shade, shade),
                        new Vector2(.515625f + side / 8f * .21875f, .28f + ring / 4f * .704375f));
                }
            for (int ring = 0; ring < rings - 1; ring++)
                for (int side = 0; side < sides; side++)
                {
                    int a = ring * (sides + 1) + side, b = a + 1;
                    Vector3 outward = new Vector3(0f, Mathf.Cos((side + .5f) * Mathf.PI * 2f / sides),
                        Mathf.Sin((side + .5f) * Mathf.PI * 2f / sides));
                    mesh.Triangle(a, b, a + sides + 1, outward);
                    mesh.Triangle(b, b + sides + 1, a + sides + 1, outward);
                }
            EndGrain(mesh, new Vector3(-1.1f, .19f, 0f), Vector3.left, 0, sides);
            EndGrain(mesh, new Vector3(1.1f, .19f + .035f * Mathf.Sin(4f), 0f), Vector3.right, 36, sides);
            Branch(mesh, new Vector3(-.35f, .25f, .06f), new Vector3(-.48f, .57f, .19f), .052f, .027f,
                4, new Color(.85f, .85f, .82f), true);
            Branch(mesh, new Vector3(.45f, .25f, -.025f), new Vector3(.56f, .44f, -.2f), .038f, .012f,
                4, new Color(.78f, .78f, .75f), true);
            return mesh.Finish("Boscali Fallen Wood");
        }

        private static void EndGrain(Builder mesh, Vector3 centre, Vector3 normal,
            int rimStart, int sides)
        {
            int middle = mesh.Vertex(centre, normal, new Color(.87f, .83f, .72f), new Vector2(.625f, .125f));
            for (int side = 0; side < sides; side++)
            {
                float a = side * Mathf.PI * 2f / sides, b = (side + 1) * Mathf.PI * 2f / sides;
                int first = mesh.Vertex(mesh.Positions[rimStart + side], normal, Color.white,
                    new Vector2(.625f + Mathf.Sin(a) * .105f, .125f + Mathf.Cos(a) * .105f));
                int next = mesh.Vertex(mesh.Positions[rimStart + side + 1], normal, Color.white,
                    new Vector2(.625f + Mathf.Sin(b) * .105f, .125f + Mathf.Cos(b) * .105f));
                mesh.Triangle(middle, first, next, normal);
            }
        }

        private struct Seed
        {
            private uint state;
            internal Seed(uint value) { state = value; }
            internal float Range(float min, float max)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                return min + (max - min) * ((state & 0xFFFFFF) / 16777216f);
            }
        }

        private sealed class Builder
        {
            internal readonly List<Vector3> Positions = new List<Vector3>(400);
            private readonly List<Vector3> normals = new List<Vector3>(400);
            private readonly List<Color32> colours = new List<Color32>(400);
            private readonly List<Vector2> uvs = new List<Vector2>(400);
            private readonly List<int> triangles = new List<int>(700);

            internal int Vertex(Vector3 position, Vector3 normal, Color colour, Vector2 uv)
            {
                int index = Positions.Count;
                Positions.Add(position); normals.Add(normal); colours.Add(colour); uvs.Add(uv);
                return index;
            }

            internal int CopyVertex(int source, Vector3 normal, Vector2 uv) =>
                Vertex(Positions[source], normal, colours[source], uv);

            internal void Triangle(int a, int b, int c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]), outward) < 0f)
                { int swap = b; b = c; c = swap; }
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }

            internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color tint,
                Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
            {
                int start = Vertex(a, normal, tint, uv0);
                Vertex(b, normal, tint, uv1); Vertex(c, normal, tint, uv2); Vertex(d, normal, tint, uv3);
                Triangle(start, start + 1, start + 2, normal); Triangle(start, start + 2, start + 3, normal);
            }

            internal Mesh Finish(string name)
            {
                var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
                mesh.SetVertices(Positions); mesh.SetNormals(normals); mesh.SetColors(colours);
                mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
                Bounds bounds = mesh.bounds; bounds.Expand(.1f); mesh.bounds = bounds; // 5 cm wind margin.
                return mesh;
            }
        }
    }
}
