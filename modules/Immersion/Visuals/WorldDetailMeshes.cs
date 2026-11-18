using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>Small authored meshes; build once, then share between instanced draws.</summary>
    internal static class WorldDetailMeshes
    {
        internal const int ShrubWide = 0, ShrubLoose = 1, ShrubTall = 2, Rock = 3, Log = 4, ShrubCount = 3;

        // Pale grass-coloured clumps rooted in the turf: the old dark broadleaf bushes on bare
        // trunks read as foreign objects against the game's dry grass.
        internal static Mesh[] Build() => new[]
        {
            Grass(1701, 7, 4, .42f, .72f, "Tussock"),
            Scrub(3109),
            Grass(7919, 5, 3, .26f, .98f, "Tall Grass"),
            Stone(), FallenWood()
        };

        private static Mesh Grass(uint seed, int blades, int skirt, float spread, float height, string name)
        {
            var mesh = new Builder();
            var random = new Seed(seed);
            for (int blade = 0; blade < blades; blade++)
            {
                Vector3 centre = new Vector3(random.Range(-.25f, .25f), 0f, random.Range(-.25f, .25f)) * spread;
                Tuft(mesh, ref random, blade * Mathf.PI / blades, centre, height, Vector3.zero);
            }
            Skirt(mesh, ref random, skirt, spread, height * .55f);
            return mesh.Finish("Boscali " + name);
        }

        private static Mesh Scrub(uint seed)
        {
            const int sprigs = 14;
            const float radius = .5f, height = .5f;
            var mesh = new Builder();
            var random = new Seed(seed);
            for (int sprig = 0; sprig < sprigs; sprig++)
            {
                float angle = sprig * 2.399963f + random.Range(-.2f, .2f);
                float reach = Mathf.Sqrt((sprig + .5f) / sprigs);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                // A low mound with no trunk: sprigs sit on a dome that meets the ground at its rim.
                Vector3 centre = outward * radius * reach +
                    Vector3.up * height * (1f - reach * reach * .6f) * random.Range(.6f, 1f);
                Vector3 normal = (outward * reach * 1.2f + Vector3.up +
                    new Vector3(random.Range(-.35f, .35f), 0f, random.Range(-.35f, .35f))).normalized;
                float shade = random.Range(.82f, 1f);
                Leaf(mesh, centre, normal, .44f * random.Range(.75f, 1.15f), random.Range(-3.1f, 3.1f), false,
                    new Color(shade, shade, shade * .94f));
            }
            Skirt(mesh, ref random, 5, radius * .9f, .4f);
            return mesh.Finish("Boscali Dry Scrub");
        }

        private static void Skirt(Builder mesh, ref Seed random, int count, float spread, float height)
        {
            // Short outward-leaning tufts feather the clump into the surrounding grass.
            for (int tuft = 0; tuft < count; tuft++)
            {
                float angle = (tuft + random.Range(-.25f, .25f)) * Mathf.PI * 2f / count;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Tuft(mesh, ref random, angle + Mathf.PI * .5f, outward * spread * random.Range(.7f, 1f), height,
                    outward * height * .35f);
            }
        }

        private static void Tuft(Builder mesh, ref Seed random, float angle, Vector3 centre, float height, Vector3 lean)
        {
            angle += random.Range(-.25f, .25f);
            Vector3 face = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 across = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * height * random.Range(.45f, .6f);
            Vector3 top = Vector3.up * height * random.Range(.8f, 1f) + lean + face * height * random.Range(-.12f, .12f);
            float shade = random.Range(.84f, 1f);
            mesh.Card(centre - across, centre + across, centre + across + top, centre - across + top,
                new Color(.74f * shade, .8f * shade, .66f * shade), new Color(shade, shade, shade * .94f));
        }

        private static void Leaf(Builder mesh, Vector3 centre, Vector3 normal, float size, float twist,
            bool narrow, Color tint)
        {
            Vector3 right = Vector3.Cross(Vector3.up, normal).normalized;
            if (right.sqrMagnitude < .01f) right = Vector3.right;
            Vector3 up = Vector3.Cross(normal, right).normalized;
            Vector3 across = (right * Mathf.Cos(twist) + up * Mathf.Sin(twist)) * size * .5f;
            Vector3 along = (-right * Mathf.Sin(twist) + up * Mathf.Cos(twist)) * size * .5f;
            // Upper atlas tile: a twiggy sprig of small pale leaves. Lower tile: a fan of grass blades.
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
            const int sides = 8, rings = 4;
            var mesh = new Builder();
            var random = new Seed(4591);
            // A low faceted fieldstone. WorldDetailDressing sinks it 0.2 m, so the widest ring
            // is underground and the stone rises out of the turf rather than sitting on it.
            float[] radii = { 1f, .95f, .72f, .38f };
            float[] heights = { -.1f, .22f, .42f, .55f };
            var ring = new Vector3[rings * sides];
            for (int r = 0; r < rings; r++)
                for (int side = 0; side < sides; side++)
                {
                    float angle = (side + random.Range(-.3f, .3f)) * Mathf.PI * 2f / sides;
                    float radius = radii[r] * random.Range(.8f, 1.12f);
                    ring[r * sides + side] = new Vector3(Mathf.Cos(angle) * radius * .52f + r * .03f,
                        heights[r] + (r == 0 ? 0f : random.Range(-.04f, .04f)), Mathf.Sin(angle) * radius * .38f);
                }
            Vector3 apex = new Vector3(.1f, .6f, -.03f);
            for (int r = 0; r < rings - 1; r++)
                for (int side = 0; side < sides; side++)
                {
                    Vector3 a = ring[r * sides + side], b = ring[r * sides + (side + 1) % sides];
                    Vector3 c = ring[(r + 1) * sides + side], d = ring[(r + 1) * sides + (side + 1) % sides];
                    // Alternating diagonals keep the facets from lining up into bands.
                    if (((r + side) & 1) == 0) { StoneFacet(mesh, ref random, a, b, c); StoneFacet(mesh, ref random, b, d, c); }
                    else { StoneFacet(mesh, ref random, a, b, d); StoneFacet(mesh, ref random, a, d, c); }
                }
            for (int side = 0; side < sides; side++)
                StoneFacet(mesh, ref random, ring[(rings - 1) * sides + side],
                    ring[(rings - 1) * sides + (side + 1) % sides], apex);
            return mesh.Finish("Boscali Fieldstone");
        }

        private static void StoneFacet(Builder mesh, ref Seed random, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(normal, (a + b + c) / 3f - new Vector3(0f, .1f, 0f)) < 0f) normal = -normal;
            float shade = random.Range(.86f, 1f);
            int first = StoneVertex(mesh, a, normal, shade);
            StoneVertex(mesh, b, normal, shade); StoneVertex(mesh, c, normal, shade);
            mesh.Triangle(first, first + 1, first + 2, normal);
        }

        private static int StoneVertex(Builder mesh, Vector3 position, Vector3 normal, float shade)
        {
            // Soil and moss stain at the ground line, clean stone on the crown.
            Color tint = Color.Lerp(new Color(.6f, .6f, .46f), new Color(1f, .98f, .93f),
                Mathf.Clamp01((position.y - .15f) / .3f)) * shade;
            // Half facet, half rounded and lifted toward the sky: a pure facet normal puts a white
            // plane next to a black one, which reads as folded paper rather than weathered stone.
            Vector3 rounded = (position - new Vector3(0f, .1f, 0f)).normalized + Vector3.up * .5f;
            return mesh.Vertex(position, (normal * .5f + rounded.normalized * .5f).normalized, tint,
                new Vector2(.875f + position.x * .15f, .5f + (position.z + position.y * .7f) * .4f));
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

            /// <summary>Upright grass card on the lower atlas tile; shaded mostly upward like turf.</summary>
            internal void Card(Vector3 bottomLeft, Vector3 bottomRight, Vector3 topRight, Vector3 topLeft,
                Color low, Color high)
            {
                Vector3 normal = Vector3.Cross(bottomRight - bottomLeft, topLeft - bottomLeft).normalized;
                if (normal.y < 0f) normal = -normal;
                Vector3 lighting = (normal + Vector3.up).normalized;
                int start = Vertex(bottomLeft, lighting, low, new Vector2(.015625f, .015625f));
                Vertex(bottomRight, lighting, low, new Vector2(.484375f, .015625f));
                Vertex(topRight, lighting, high, new Vector2(.484375f, .484375f));
                Vertex(topLeft, lighting, high, new Vector2(.015625f, .484375f));
                Triangle(start, start + 1, start + 2, lighting); Triangle(start, start + 2, start + 3, lighting);
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
