using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Collects a mesh: positions, normals, colours (sRGB as authored; <see cref="LinearColours"/> converts for the vertex
/// stream), a sway weight in colour alpha and a UV. Faces are wound clockwise seen from outside, as Unity wants.
/// Only managed parts of UnityEngine are used, so tools/MapAssets/Tests can run it outside the editor.
/// </summary>
public sealed class MapMeshBuilder
{
    public readonly List<Vector3> Vertices = new List<Vector3>(), Normals = new List<Vector3>();
    public readonly List<Color> Colours = new List<Color>();
    public readonly List<Vector2> Uvs = new List<Vector2>();
    public readonly List<int> Triangles = new List<int>();

    public int TriangleCount { get { return Triangles.Count / 3; } }

    public int Add(Vector3 p, Vector3 normal, Color colour, float sway = 0f) { return Add(p, normal, colour, sway, Vector2.zero); }

    public int Add(Vector3 p, Vector3 normal, Color colour, float sway, Vector2 uv)
    {
        Vertices.Add(p); Normals.Add(normal); Uvs.Add(uv);
        Colours.Add(new Color(Mathf.Clamp01(colour.r), Mathf.Clamp01(colour.g), Mathf.Clamp01(colour.b), sway));
        return Vertices.Count - 1;
    }

    public void Tri(int a, int b, int c) { Triangles.Add(a); Triangles.Add(b); Triangles.Add(c); }

    public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

    /// <summary>A flat polygon with its own vertices; points clockwise seen from the lit side.</summary>
    public void Face(Color colour, params Vector3[] points)
    {
        Vector3 normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]).normalized;
        int first = Vertices.Count;
        foreach (Vector3 p in points) Add(p, normal, colour);
        for (int i = 1; i + 1 < points.Length; i++) Tri(first, first + i, first + i + 1);
    }

    /// <summary>A flat polygon turned so that it faces along <paramref name="facing"/>.</summary>
    public void FaceTo(Color colour, Vector3 facing, params Vector3[] points)
    {
        if (Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), facing) < 0f)
        {
            points = (Vector3[])points.Clone();
            Array.Reverse(points);
        }
        Face(colour, points);
    }

    /// <summary>A flat polygon turned so that it faces away from the point <paramref name="from"/> (the inside of the thing).</summary>
    public void FaceAway(Color colour, Vector3 from, params Vector3[] points)
    {
        Vector3 centre = Vector3.zero;
        foreach (Vector3 p in points) centre += p;
        FaceTo(colour, centre / points.Length - from, points);
    }

    private static Vector3 Centre(Vector3[] a, Vector3[] b)
    {
        Vector3 centre = Vector3.zero;
        foreach (Vector3 p in a) centre += p;
        foreach (Vector3 p in b) centre += p;
        return centre / (a.Length + b.Length);
    }

    /// <summary>Flat quads between two rings of equal length, facing outward.</summary>
    public void Band(Color colour, Vector3[] lower, Vector3[] upper)
    {
        Vector3 centre = Centre(lower, upper);
        for (int i = 0; i < lower.Length; i++)
        {
            int j = (i + 1) % lower.Length;
            FaceAway(colour, centre, lower[i], upper[i], upper[j], lower[j]);
        }
    }

    /// <summary>Flat triangles from a ring to one point, facing outward.</summary>
    public void Cone(Color colour, Vector3[] ring, Vector3 apex)
    {
        Vector3 centre = Centre(ring, new Vector3[0]);
        for (int i = 0; i < ring.Length; i++) FaceAway(colour, centre, ring[i], apex, ring[(i + 1) % ring.Length]);
    }

    /// <summary>Closes a convex ring (counter-clockwise seen from above) with one flat polygon facing up or down.</summary>
    public void Cap(Color colour, Vector3[] ring, bool up)
    {
        var points = (Vector3[])ring.Clone();
        if (up) Array.Reverse(points);
        Face(colour, points);
    }

    /// <summary>A box from its centre and three half-extent vectors (right, up, forward); the bottom is optional.</summary>
    public void Box(Vector3 centre, Vector3 right, Vector3 up, Vector3 forward, Color colour, bool bottom = false)
    {
        Vector3 a = centre - right - up - forward, b = centre + right - up - forward, c = centre + right - up + forward, d = centre - right - up + forward;
        Vector3 e = a + 2f * up, f = b + 2f * up, g = c + 2f * up, h = d + 2f * up;
        FaceAway(colour, centre, e, h, g, f);           // top
        FaceAway(colour, centre, a, e, f, b);           // back (-forward)
        FaceAway(colour, centre, b, f, g, c);           // right
        FaceAway(colour, centre, c, g, h, d);           // front
        FaceAway(colour, centre, d, h, e, a);           // left
        if (bottom) FaceAway(colour, centre, a, b, c, d);
    }

    public void Box(Vector3 centre, Vector3 half, Color colour, float yaw = 0f, bool bottom = false)
    {
        Box(centre, MapKitShapes.Turn(new Vector3(half.x, 0f, 0f), yaw), new Vector3(0f, half.y, 0f), MapKitShapes.Turn(new Vector3(0f, 0f, half.z), yaw), colour, bottom);
    }

    /// <summary>Rings of shared vertices joined by quads, optionally closed by an apex: the smooth counterpart of Band and Cone.</summary>
    public int Skin(Vector3[][] rings, Func<int, int, Color> colour, bool closeTop, Vector3 apex, Color apexColour)
    {
        int first = Vertices.Count, sides = rings[0].Length;
        for (int k = 0; k < rings.Length; k++)
            for (int i = 0; i < sides; i++) Add(rings[k][i], Vector3.up, colour(k, i));
        for (int k = 0; k + 1 < rings.Length; k++)
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides, low = first + k * sides, high = low + sides;
                Quad(low + i, high + i, high + j, low + j);
            }
        if (closeTop)
        {
            int top = Add(apex, Vector3.up, apexColour), last = first + (rings.Length - 1) * sides;
            for (int i = 0; i < sides; i++) Tri(last + i, top, last + (i + 1) % sides);
        }
        Smooth(first);
        return first;
    }

    /// <summary>Area-weighted normals for the shared vertices from <paramref name="first"/> on.</summary>
    public void Smooth(int first)
    {
        for (int i = first; i < Normals.Count; i++) Normals[i] = Vector3.zero;
        for (int t = 0; t < Triangles.Count; t += 3)
        {
            int a = Triangles[t], b = Triangles[t + 1], c = Triangles[t + 2];
            if (a < first || b < first || c < first) continue;
            Vector3 face = Vector3.Cross(Vertices[b] - Vertices[a], Vertices[c] - Vertices[a]);
            Normals[a] += face; Normals[b] += face; Normals[c] += face;
        }
        for (int i = first; i < Normals.Count; i++) Normals[i] = Normals[i].sqrMagnitude > 1e-12f ? Normals[i].normalized : Vector3.up;
    }

    /// <summary>Multiplies the colours from <paramref name="first"/> on by a factor that runs from low at the lowest vertex to high at the highest.</summary>
    public void Gradient(int first, float low, float high)
    {
        float bottom = float.MaxValue, top = float.MinValue;
        for (int i = first; i < Vertices.Count; i++) { bottom = Mathf.Min(bottom, Vertices[i].y); top = Mathf.Max(top, Vertices[i].y); }
        for (int i = first; i < Vertices.Count; i++)
        {
            float k = Mathf.Lerp(low, high, top > bottom ? (Vertices[i].y - bottom) / (top - bottom) : 1f);
            Color c = Colours[i];
            Colours[i] = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
        }
    }

    /// <summary>Adds another mesh, turned and moved.</summary>
    public void Append(MapMeshBuilder part, Func<Vector3, Vector3> rotate, Vector3 offset)
    {
        int first = Vertices.Count;
        for (int i = 0; i < part.Vertices.Count; i++)
        {
            Vertices.Add(rotate(part.Vertices[i]) + offset); Normals.Add(rotate(part.Normals[i])); Colours.Add(part.Colours[i]); Uvs.Add(part.Uvs[i]);
        }
        foreach (int index in part.Triangles) Triangles.Add(first + index);
    }

    public static float Linear(float c) { return c <= .04045f ? c / 12.92f : Mathf.Pow((c + .055f) / 1.055f, 2.4f); }

    /// <summary>The colours for the vertex stream: linear RGB (the game shades in linear space), alpha = sway weight.</summary>
    public List<Color> LinearColours()
    {
        var list = new List<Color>(Colours.Count);
        foreach (Color c in Colours) list.Add(new Color(Linear(c.r), Linear(c.g), Linear(c.b), c.a));
        return list;
    }

    public float Radius()
    {
        float radius = 0f;
        foreach (Vector3 p in Vertices) radius = Mathf.Max(radius, p.magnitude);
        return radius;
    }
}

/// <summary>
/// The scenery kit of design/quick-specs/2026-11-18-map-builder-v2.md section 4.3: 23 kinds, each a handful of low
/// polygon variants built in code. Pivot at ground centre, +z forward, metres. Rock and ice are smooth shaded with a
/// vertical gradient; built things are flat shaded.
/// </summary>
public static class MapKitShapes
{
    public sealed class Kind
    {
        public readonly string Name, Draw;
        public readonly int Variants, MaxTriangles;
        public readonly bool Solid, Sway;
        public readonly float MinSize, MaxSize; // the allowed largest dimension of a variant, metres (a sanity check)

        public Kind(string name, int variants, int maxTriangles, string draw, bool solid, bool sway, float minSize, float maxSize)
        {
            Name = name; Variants = variants; MaxTriangles = maxTriangles; Draw = draw; Solid = solid; Sway = sway; MinSize = minSize; MaxSize = maxSize;
        }
    }

    public static readonly Kind[] Kinds =
    {
        new Kind("boulder", 5, 40, "s", false, false, 1.2f, 8f),
        new Kind("boulder_dark", 3, 40, "s", false, false, 1.2f, 8f),
        new Kind("outcrop", 4, 120, "m", true, false, 10f, 36f),
        new Kind("sea_stack", 3, 160, "l", true, false, 25f, 75f),
        new Kind("palm", 5, 220, "s", false, true, 8f, 17f),
        new Kind("conifer", 4, 60, "s", false, true, 13f, 23f),
        new Kind("conifer_snow", 3, 60, "s", false, true, 13f, 23f),
        new Kind("conifer_clump", 2, 120, "", false, true, 13f, 26f),
        new Kind("conifer_snow_clump", 2, 120, "", false, true, 13f, 26f),
        new Kind("ice_floe", 6, 30, "m", false, false, 14f, 26f),
        new Kind("iceberg", 5, 180, "l", true, false, 28f, 60f),
        new Kind("iceberg_tabular", 3, 80, "l", true, false, 180f, 230f),
        new Kind("hut", 3, 60, "m", true, false, 5f, 8f),
        new Kind("railgun", 3, 90, "l", true, false, 44f, 130f),   // one gun of a ruined superweapon: standing, fallen, a stump
        new Kind("hangar_shelter", 2, 140, "l", true, false, 28f, 34f),
        new Kind("control_tower", 1, 120, "l", true, false, 13f, 16f),
        new Kind("windsock", 1, 40, "s", false, true, 5.5f, 7f),
        new Kind("fuel_tank", 2, 120, "m", true, false, 6.5f, 10f),
        new Kind("pier", 2, 150, "m", true, false, 58f, 64f),
        new Kind("lighthouse", 2, 220, "l", true, false, 22f, 26f),
        new Kind("radar_dome", 1, 260, "l", true, false, 16f, 20f),
        new Kind("radio_mast", 1, 180, "l", true, false, 44f, 66f),
        new Kind("wreck", 2, 300, "m", true, false, 26f, 62f),
        new Kind("buoy", 2, 60, "s", false, false, 2.6f, 4.4f),
    };

    public sealed class Shape
    {
        public readonly MapMeshBuilder Mesh = new MapMeshBuilder();
        /// <summary>Null: collide with the convex hull of the mesh. Otherwise boxes; one box is convex, several are not.</summary>
        public MapMeshBuilder Collision;
        public bool CollisionConvex = true;
    }

    /// <summary>One variant. Call with the same <see cref="System.Random"/> for variants 0, 1, ... of a kind (seed 4242 + kind index).</summary>
    public static Shape Build(string kind, int variant, System.Random random)
    {
        var shape = new Shape();
        MapMeshBuilder b = shape.Mesh;
        switch (kind)
        {
            case "boulder": Boulder(b, random, variant, 5, Rgb(128, 124, 118), .8f, 1.2f); break;
            case "boulder_dark": Boulder(b, random, variant, 3, Rgb(62, 58, 56), .72f, 1.28f); break;
            case "outcrop": Outcrop(b, random, variant); break;
            case "sea_stack": SeaStack(b, random, variant); break;
            case "palm": Palm(b, random); break;
            case "conifer": Conifer(b, random, false); break;
            case "conifer_snow": Conifer(b, random, true); break;
            case "conifer_clump":
            case "conifer_snow_clump": ConiferClump(b, random); break;
            case "ice_floe": IceFloe(b, random); break;
            case "iceberg": Iceberg(b, random, variant); break;
            case "iceberg_tabular": IcebergTabular(b, random); break;
            case "hut": Hut(b, variant); break;
            case "railgun": Railgun(b, variant); break;
            case "hangar_shelter": HangarShelter(shape, variant); break;
            case "control_tower": ControlTower(b); break;
            case "windsock": Windsock(b); break;
            case "fuel_tank": FuelTank(b, variant); break;
            case "pier": Pier(shape, variant); break;
            case "lighthouse": Lighthouse(b, variant); break;
            case "radar_dome": RadarDome(b, random); break;
            case "radio_mast": RadioMast(shape); break;
            case "wreck": Wreck(shape, variant); break;
            case "buoy": Buoy(b, variant); break;
            default: throw new ArgumentException("Unknown kit kind: " + kind);
        }
        return shape;
    }

    // ---- small helpers ---------------------------------------------------------------------------------------------

    private static float Range(System.Random random, float low, float high) { return low + (float)random.NextDouble() * (high - low); }
    private static Color Rgb(int r, int g, int b) { return new Color(r / 255f, g / 255f, b / 255f, 1f); }
    private static Color Mul(Color c, float k) { return new Color(c.r * k, c.g * k, c.b * k, 1f); }
    private static Color Mix(Color a, Color b, float t) { return new Color(Mathf.Lerp(a.r, b.r, t), Mathf.Lerp(a.g, b.g, t), Mathf.Lerp(a.b, b.b, t), 1f); }
    private static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

    /// <summary>Yaw about the vertical, degrees clockwise seen from above (Unity's sign): +z turns toward +x.</summary>
    public static Vector3 Turn(Vector3 p, float degrees)
    {
        float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
        return new Vector3(p.x * c + p.z * s, p.y, -p.x * s + p.z * c);
    }

    /// <summary>Roll about z, then pitch about x, then yaw about y, in degrees.</summary>
    private static Vector3 Rotate(Vector3 v, float pitch, float yaw, float roll)
    {
        float r = roll * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad, cr = Mathf.Cos(r), sr = Mathf.Sin(r), cp = Mathf.Cos(p), sp = Mathf.Sin(p);
        v = new Vector3(v.x * cr - v.y * sr, v.x * sr + v.y * cr, v.z);
        v = new Vector3(v.x, v.y * cp - v.z * sp, v.y * sp + v.z * cp);
        return Turn(v, yaw);
    }

    /// <summary>A ring counter-clockwise seen from above, starting at +x.</summary>
    private static Vector3[] Ring(Vector3 centre, float radiusX, float radiusZ, int sides, float phase = 0f)
    {
        var ring = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = phase + i * Mathf.PI * 2f / sides;
            ring[i] = centre + new Vector3(Mathf.Cos(a) * radiusX, 0f, Mathf.Sin(a) * radiusZ);
        }
        return ring;
    }

    private static Vector3[] Ring(float y, float radius, int sides, float phase = 0f) { return Ring(new Vector3(0f, y, 0f), radius, radius, sides, phase); }

    /// <summary>A lumpy ball of shared vertices: an octahedron split <paramref name="level"/> times, every corner pushed in or out, then warped.</summary>
    private static int Lump(MapMeshBuilder b, System.Random random, Vector3 centre, Vector3 radii, int level, float low, float high, Color colour, Func<Vector3, Vector3> warp = null)
    {
        Vector3[] corners = { Vector3.up, Vector3.right, Vector3.forward, Vector3.left, Vector3.back, Vector3.down };
        int[] faces = { 0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4, 5, 1, 2, 5, 2, 3, 5, 3, 4, 5, 4, 1 };
        var triangles = new List<Vector3[]>();
        for (int f = 0; f < faces.Length; f += 3) triangles.Add(new[] { corners[faces[f]], corners[faces[f + 1]], corners[faces[f + 2]] });
        for (int l = 0; l < level; l++)
        {
            var split = new List<Vector3[]>();
            foreach (Vector3[] t in triangles)
            {
                Vector3 m01 = (t[0] + t[1]).normalized, m12 = (t[1] + t[2]).normalized, m20 = (t[2] + t[0]).normalized;
                split.Add(new[] { t[0], m01, m20 }); split.Add(new[] { m01, t[1], m12 }); split.Add(new[] { m20, m12, t[2] }); split.Add(new[] { m01, m12, m20 });
            }
            triangles = split;
        }
        int first = b.Vertices.Count;
        var index = new Dictionary<Vector3, int>();
        foreach (Vector3[] t in triangles)
        {
            var at = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (!index.TryGetValue(t[i], out at[i]))
                {
                    Vector3 p = centre + Vector3.Scale(t[i] * Range(random, low, high), radii);
                    index[t[i]] = at[i] = b.Add(warp != null ? warp(p) : p, Vector3.up, colour);
                }
            }
            b.Tri(at[0], at[1], at[2]);
        }
        b.Smooth(first);
        return first;
    }

    // ---- rock --------------------------------------------------------------------------------------------------------

    private static void Boulder(MapMeshBuilder b, System.Random random, int variant, int variants, Color rock, float low, float high)
    {
        float size = Mathf.Lerp(1.5f, 6f, variants > 1 ? variant / (float)(variants - 1) : .5f) * Range(random, .9f, 1.05f), r = size * .5f;
        float squash = Range(random, .5f, .85f);
        float lean = Range(random, -.25f, .25f);
        Lump(b, random, V(0f, r * squash * .45f, 0f), V(r, r * squash, r * Range(random, .7f, 1f)), 1, low, high, Mul(rock, Range(random, .92f, 1.08f)),
            p => new Vector3(p.x + lean * p.y, p.y, p.z));
        b.Gradient(0, .7f, 1.05f);
    }

    private static void Outcrop(MapMeshBuilder b, System.Random random, int variant)
    {
        Color rock = Rgb(104, 100, 98), lichen = Rgb(128, 126, 84);
        float width = Mathf.Lerp(12f, 26f, variant / 3f) * Range(random, .95f, 1.1f), height = width * Range(random, .55f, .8f);
        float turn = Range(random, 0f, 360f);
        Vector3 shear = Turn(V(Range(random, .15f, .4f), 0f, 0f), turn);
        Func<Vector3, Vector3> lean = p => new Vector3(p.x + shear.x * Mathf.Max(0f, p.y), p.y, p.z + shear.z * Mathf.Max(0f, p.y));
        Lump(b, random, V(0f, height * .28f, 0f), V(width * .5f, height * .72f, width * .36f), 1, .7f, 1.25f, rock, lean);
        Vector3 side = Turn(V(width * .36f, 0f, 0f), turn + 70f);
        Lump(b, random, side + V(0f, height * .12f, 0f), V(width * .3f, height * .42f, width * .26f), 1, .7f, 1.25f, Mul(rock, .94f), lean);
        Vector3 other = Turn(V(width * .3f, 0f, 0f), turn + 215f);
        Lump(b, random, other + V(0f, height * .05f, 0f), V(width * .24f, height * .26f, width * .2f), 1, .7f, 1.2f, Mul(rock, 1.05f), lean);
        b.Gradient(0, .7f, 1.05f);
        float top = 0f;
        foreach (Vector3 p in b.Vertices) top = Mathf.Max(top, p.y);
        for (int i = 0; i < b.Vertices.Count; i++) // lichen where the rock faces the sky
        {
            float cover = Mathf.Clamp01((b.Normals[i].y - .45f) / .35f) * Mathf.Clamp01(b.Vertices[i].y / (top * .45f));
            b.Colours[i] = Mix(b.Colours[i], lichen, .65f * cover);
        }
    }

    private static void SeaStack(MapMeshBuilder b, System.Random random, int variant)
    {
        Color rock = Rgb(96, 90, 84), guano = Rgb(226, 222, 208);
        const int Sides = 8, Rings = 9;
        float height = Mathf.Lerp(26f, 56f, variant / 2f) * Range(random, .95f, 1.08f), radius = height * Range(random, .2f, .27f);
        float turn = Range(random, 0f, Mathf.PI * 2f), lean = height * Range(random, .04f, .12f);
        float[] profile = { 1.3f, 1.12f, .92f, .8f, .74f, .82f, .9f, .78f, .52f };
        var rings = new Vector3[Rings][];
        for (int k = 0; k < Rings; k++)
        {
            float t = k / (float)(Rings - 1), y = Mathf.Lerp(-6f, height, t * t * .35f + t * .65f);
            Vector3 centre = V(Mathf.Cos(turn) * lean * t * t, y, Mathf.Sin(turn) * lean * t * t);
            rings[k] = new Vector3[Sides];
            for (int i = 0; i < Sides; i++)
            {
                float a = i * Mathf.PI * 2f / Sides + k * .21f, r = radius * profile[k] * Range(random, .82f, 1.16f);
                rings[k][i] = centre + V(Mathf.Cos(a) * r, Range(random, -.03f, .03f) * height, Mathf.Sin(a) * r * .85f);
            }
        }
        float[] strata = new float[Rings];
        for (int k = 0; k < Rings; k++) strata[k] = k % 2 == 0 ? 1f : Range(random, .84f, .93f);
        Vector3 apex = V(Mathf.Cos(turn) * lean, height + radius * .12f, Mathf.Sin(turn) * lean);
        b.Skin(rings, (k, i) => Mul(rock, strata[k]), true, apex, rock);
        b.Gradient(0, .7f, 1.05f);
        int last = (Rings - 1) * Sides;
        for (int i = 0; i < Sides; i++) // the cap is white with droppings, and they run a little down the sides
        {
            b.Colours[last + i] = Mix(b.Colours[last + i], guano, .8f);
            b.Colours[last - Sides + i] = Mix(b.Colours[last - Sides + i], guano, Range(random, .1f, .5f));
        }
        b.Colours[last + Sides] = guano;
    }

    // ---- trees -------------------------------------------------------------------------------------------------------

    private static void Palm(MapMeshBuilder b, System.Random random)
    {
        float height = Range(random, 8.5f, 12.5f), lean = Range(random, .6f, 2.4f), turn = Range(random, 0f, Mathf.PI * 2f);
        var leanTo = new Vector3(Mathf.Cos(turn), 0f, Mathf.Sin(turn));
        Color bark = Mul(new Color(.47f, .39f, .28f), Range(random, .85f, 1.1f));
        const int Sides = 5, Rings = 4;
        int first = b.Vertices.Count;
        for (int ring = 0; ring <= Rings; ring++)
        {
            float t = ring / (float)Rings, radius = Mathf.Lerp(.24f, .12f, t);
            Vector3 centre = Vector3.up * (height * t) + leanTo * (lean * t * t);
            for (int side = 0; side < Sides; side++)
            {
                float a = side * Mathf.PI * 2f / Sides;
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                b.Add(centre + outward * radius, outward, Mul(bark, Mathf.Lerp(.8f, 1.05f, t)), t * t);
            }
        }
        for (int ring = 0; ring < Rings; ring++)
            for (int side = 0; side < Sides; side++)
            {
                int a = first + ring * Sides + side, c = first + ring * Sides + (side + 1) % Sides;
                b.Quad(a, a + Sides, c + Sides, c);
            }
        Vector3 crown = Vector3.up * height + leanTo * lean;
        int fronds = random.Next(7, 10);
        for (int f = 0; f < fronds; f++)
        {
            // A frond is a shallow V: the spine rides above the two leaf edges, so it catches light from either side.
            float a = turn + f * Mathf.PI * 2f / fronds + Range(random, -.25f, .25f), length = Range(random, 3.4f, 4.8f), droop = Range(random, 1.5f, 2.8f);
            var along = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var across = new Vector3(-along.z, 0f, along.x);
            Color leaf = Mul(new Color(.30f, .52f, .20f), Range(random, .8f, 1.2f));
            int previous = -1;
            const int Segments = 4;
            for (int s = 0; s <= Segments; s++)
            {
                float t = s / (float)Segments, width = Mathf.Sin(Mathf.Lerp(.2f, 1f, t) * Mathf.PI) * .8f + .04f;
                Vector3 spine = crown + along * (length * t) + Vector3.up * (1f * t - droop * t * t);
                Vector3 normal = (Vector3.up + along * (t - .3f)).normalized, sag = Vector3.down * (width * .45f);
                Color shade = Mul(leaf, Mathf.Lerp(.8f, 1.1f, t));
                int left = b.Add(spine - across * width + sag, (normal - across * .5f).normalized, Mul(shade, .85f), 1f);
                b.Add(spine, normal, shade, 1f);
                b.Add(spine + across * width + sag, (normal + across * .5f).normalized, Mul(shade, .85f), 1f);
                if (s > 0) { b.Quad(previous, previous + 1, left + 1, left); b.Quad(previous + 1, previous + 2, left + 2, left + 1); }
                previous = left;
            }
        }
    }

    private static void Conifer(MapMeshBuilder b, System.Random random, bool snow)
    {
        Color green = Mul(Rgb(34, 70, 48), Range(random, .9f, 1.15f)), trunk = Rgb(70, 52, 38), white = Rgb(236, 240, 246);
        float height = Range(random, 14f, 22f), girth = Range(random, .16f, .2f);
        Vector3[] foot = Ring(0f, height * .016f + .12f, 3), neck = Ring(height * .2f, height * .012f + .08f, 3);
        b.Band(trunk, foot, neck);
        int sides = snow ? 6 : 8;
        for (int tier = 0; tier < 3; tier++)
        {
            float low = height * (.13f + .25f * tier), high = tier == 2 ? height : low + height * .45f, radius = height * girth * (1f - .27f * tier);
            float phase = Range(random, 0f, 1f);
            var rim = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = phase + i * Mathf.PI * 2f / sides, r = radius * Range(random, .9f, 1.08f);
                rim[i] = V(Mathf.Cos(a) * r, low + Range(random, -.02f, .02f) * height, Mathf.Sin(a) * r);
            }
            var apex = V(0f, high, 0f);
            // Each side has its own apex vertex so the cone shades round, not like a pinched fan.
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Vector3 ni = ConeNormal(rim[i], high - low), nj = ConeNormal(rim[j], high - low), na = (ni + nj).normalized;
                float si = Sq(rim[i].y / height) * .6f, sa = Sq(high / height) * .6f;
                if (!snow)
                {
                    int a = b.Add(rim[i], ni, Mul(green, .72f), si), c = b.Add(apex, na, Mul(green, 1.18f), sa), d = b.Add(rim[j], nj, Mul(green, .72f), si);
                    b.Tri(a, c, d);
                    continue;
                }
                // Snow lies on the upper part of each tier; the green skirt shows below it.
                float line = Range(random, .34f, .46f);
                Vector3 mi = Vector3.Lerp(rim[i], apex, line), mj = Vector3.Lerp(rim[j], apex, line);
                int g0 = b.Add(rim[i], ni, Mul(green, .74f), si), g1 = b.Add(mi, ni, Mul(green, .95f), si), g2 = b.Add(mj, nj, Mul(green, .95f), si), g3 = b.Add(rim[j], nj, Mul(green, .74f), si);
                b.Quad(g0, g1, g2, g3);
                int w0 = b.Add(mi, ni, Mul(white, .9f), si), w1 = b.Add(apex, na, white, sa), w2 = b.Add(mj, nj, Mul(white, .9f), si);
                b.Tri(w0, w1, w2);
            }
            if (!snow) b.Cap(Mul(green, .45f), rim, false); // the dark underside of the boughs
        }
    }

    private static float Sq(float v) { return v * v; }

    private static Vector3 ConeNormal(Vector3 rim, float rise)
    {
        var outward = new Vector3(rim.x, 0f, rim.z);
        float radius = outward.magnitude;
        return (outward.normalized * rise + Vector3.up * radius).normalized;
    }

    /// <summary>Five conifers as one mesh for the game's tree renderer: textured (u round the tree, v from foot to tip), white vertex colour.</summary>
    private static void ConiferClump(MapMeshBuilder b, System.Random random)
    {
        const int Sides = 6;
        var spots = new List<Vector3>();
        float start = Range(random, 0f, Mathf.PI * 2f);
        spots.Add(V(Range(random, -.8f, .8f), 0f, Range(random, -.8f, .8f)));
        for (int n = 0; n < 4; n++)
        {
            float a = start + n * Mathf.PI * .5f + Range(random, -.35f, .35f), r = Range(random, 2.6f, 4.2f);
            spots.Add(V(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }
        for (int n = 0; n < spots.Count; n++)
        {
            float height = (n == 0 ? Range(random, 19f, 22f) : Range(random, 14f, 20f)), girth = Range(random, .15f, .19f);
            Vector3 at = spots[n];
            var trunk = new Color(.42f, .33f, .26f);
            Vector3[] foot = Ring(at, .3f, .3f, 3), neck = Ring(at + Vector3.up * (height * .2f), .2f, .2f, 3);
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                Vector3 normal = Vector3.Cross(neck[i] - foot[i], neck[j] - foot[i]).normalized;
                int a = b.Add(foot[i], normal, trunk, 0f, new Vector2(.5f, 0f)), c = b.Add(neck[i], normal, trunk, 0f, new Vector2(.5f, .02f));
                int d = b.Add(neck[j], normal, trunk, 0f, new Vector2(.5f, .02f)), e = b.Add(foot[j], normal, trunk, 0f, new Vector2(.5f, 0f));
                b.Quad(a, c, d, e);
            }
            for (int tier = 0; tier < 2; tier++)
            {
                float low = height * (tier == 0 ? .12f : .44f), high = tier == 0 ? height * .66f : height, radius = height * girth * (tier == 0 ? 1f : .68f);
                float phase = Range(random, 0f, 1f);
                for (int i = 0; i < Sides; i++)
                {
                    float a0 = phase + i * Mathf.PI * 2f / Sides, a1 = phase + (i + 1) * Mathf.PI * 2f / Sides;
                    Vector3 p0 = at + V(Mathf.Cos(a0) * radius, low, Mathf.Sin(a0) * radius), p1 = at + V(Mathf.Cos(a1) * radius, low, Mathf.Sin(a1) * radius);
                    Vector3 n0 = ConeNormal(p0 - at, high - low), n1 = ConeNormal(p1 - at, high - low);
                    float shade = tier == 0 ? .82f : .92f;
                    int a = b.Add(p0, n0, new Color(shade, shade, shade), Sq(low / height) * .6f, new Vector2(i / (float)Sides, low / height));
                    int c = b.Add(at + Vector3.up * high, (n0 + n1).normalized, Color.white, Sq(high / height) * .6f, new Vector2((i + .5f) / Sides, high / height));
                    int d = b.Add(p1, n1, new Color(shade, shade, shade), Sq(low / height) * .6f, new Vector2((i + 1) / (float)Sides, low / height));
                    b.Tri(a, c, d);
                }
            }
        }
    }

    // ---- ice ---------------------------------------------------------------------------------------------------------

    private static void IceFloe(MapMeshBuilder b, System.Random random)
    {
        Color snow = Rgb(232, 240, 246), edge = Rgb(150, 200, 220);
        int sides = random.Next(7, 10);
        float stretch = Range(random, .7f, 1f), phase = Range(random, 0f, Mathf.PI * 2f);
        var rim = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = phase + (i + Range(random, -.28f, .28f)) * Mathf.PI * 2f / sides, r = 10f * Range(random, .78f, 1.1f);
            rim[i] = V(Mathf.Cos(a) * r, 1.2f, Mathf.Sin(a) * r * stretch);
        }
        int centre = b.Add(V(0f, 1.45f, 0f), Vector3.up, Mul(snow, 1.03f));
        int top = b.Vertices.Count;
        foreach (Vector3 p in rim) b.Add(p, (Vector3.up * 3f + new Vector3(p.x, 0f, p.z).normalized).normalized, snow);
        for (int i = 0; i < sides; i++) b.Tri(centre, top + (i + 1) % sides, top + i);
        int wall = b.Vertices.Count;
        foreach (Vector3 p in rim)
        {
            Vector3 outward = new Vector3(p.x, 0f, p.z).normalized;
            b.Add(p, outward, edge);
            b.Add(new Vector3(p.x * .9f, -1.5f, p.z * .9f), (outward - Vector3.up * .4f).normalized, Mul(edge, .7f));
        }
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            b.Quad(wall + i * 2 + 1, wall + i * 2, wall + j * 2, wall + j * 2 + 1);
        }
    }

    private static void Iceberg(MapMeshBuilder b, System.Random random, int variant)
    {
        Color ice = Rgb(226, 238, 246), waterline = Rgb(120, 190, 215), blue = Rgb(176, 214, 236);
        bool wedge = variant % 2 == 1;
        const int Sides = 12;
        float radius = wedge ? Range(random, 17f, 21f) : Range(random, 14f, 19f), height = wedge ? Range(random, 15f, 22f) : Range(random, 26f, 38f);
        float turn = Range(random, 0f, Mathf.PI * 2f), stretch = wedge ? Range(random, .62f, .78f) : Range(random, .8f, 1f);
        Vector3 peak = V(Mathf.Cos(turn), 0f, Mathf.Sin(turn)) * (radius * Range(random, .15f, .4f));
        float[] rise = wedge ? new[] { -6f, .7f, 3.5f, .45f, .8f, 1f } : new[] { -6f, .7f, 3.5f, .34f, .64f, .86f };
        float[] width = wedge ? new[] { .96f, 1.03f, .98f, .9f, .72f, .42f } : new[] { .96f, 1.03f, .97f, .76f, .46f, .2f };
        var rings = new Vector3[rise.Length][];
        for (int k = 0; k < rise.Length; k++)
        {
            float t = k < 3 ? 0f : (k - 2) / (float)(rise.Length - 2);
            rings[k] = new Vector3[Sides];
            for (int i = 0; i < Sides; i++)
            {
                float a = i * Mathf.PI * 2f / Sides, r = radius * width[k] * (k < 3 ? Range(random, .95f, 1.05f) : Range(random, .82f, 1.16f));
                float y = k < 3 ? rise[k] : rise[k] * height * Range(random, .9f, 1.08f);
                // A wedge is a tilted slab: high on the side it leans to, low on the other, with a cliff all round.
                if (wedge && k >= 3) y *= Mathf.Lerp(.42f, 1f, .5f + .5f * Mathf.Cos(a - turn));
                rings[k][i] = peak * (t * t) + V(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r * stretch);
            }
        }
        var tints = new float[rise.Length * Sides];
        for (int i = 0; i < tints.Length; i++) tints[i] = Range(random, 0f, 1f) < .3f ? Range(random, .35f, .8f) : Range(random, 0f, .12f);
        Vector3 apex = peak + V(0f, wedge ? height * .8f : height, 0f);
        b.Skin(rings, (k, i) => k == 0 ? Mul(waterline, .8f) : k == 1 ? waterline : k == 2 ? Mix(waterline, ice, .75f) : Mix(ice, blue, tints[k * Sides + i]), true, apex, ice);
        b.Gradient(0, .7f, 1.05f);
    }

    private static void IcebergTabular(MapMeshBuilder b, System.Random random)
    {
        Color ice = Rgb(226, 238, 246), waterline = Rgb(120, 190, 215), blue = Rgb(182, 216, 236);
        const int Sides = 11;
        float[] rise = { -8f, .9f, 26f, 28f }, width = { .97f, 1.02f, 1f, .955f };
        var outline = new Vector2[Sides];
        float phase = Range(random, 0f, Mathf.PI * 2f);
        for (int i = 0; i < Sides; i++)
        {
            // A rounded rectangle 200 x 140 m with a ragged edge.
            float a = phase + (i + Range(random, -.2f, .2f)) * Mathf.PI * 2f / Sides, c = Mathf.Cos(a), s = Mathf.Sin(a);
            float r = Mathf.Pow(Mathf.Pow(Mathf.Abs(c), 4f) + Mathf.Pow(Mathf.Abs(s), 4f), -.25f) * Range(random, .9f, 1f);
            outline[i] = new Vector2(c * r * 100f, s * r * 70f);
        }
        var rings = new Vector3[rise.Length][];
        for (int k = 0; k < rise.Length; k++)
        {
            rings[k] = new Vector3[Sides];
            for (int i = 0; i < Sides; i++) rings[k][i] = V(outline[i].x * width[k], rise[k] + (k == 2 ? Range(random, -1.5f, .5f) : 0f), outline[i].y * width[k]);
        }
        var tints = new float[Sides];
        for (int i = 0; i < Sides; i++) tints[i] = Range(random, 0f, .7f);
        b.Skin(rings, (k, i) => k == 0 ? Mul(waterline, .8f) : k == 1 ? waterline : k == 2 ? Mix(ice, blue, tints[i]) : ice, true, V(0f, 28.8f, 0f), ice);
        b.Gradient(0, .7f, 1.05f);
    }

    // ---- built things ------------------------------------------------------------------------------------------------

    private static void Hut(MapMeshBuilder b, int variant)
    {
        Color wall = variant == 0 ? Rgb(110, 82, 56) : variant == 1 ? Rgb(96, 110, 100) : Rgb(150, 40, 36);
        Color roof = variant == 0 ? Rgb(64, 60, 58) : variant == 1 ? Rgb(124, 134, 126) : Rgb(50, 50, 54);
        Color trim = variant == 2 ? Rgb(232, 230, 222) : Mul(wall, .62f), glass = Rgb(58, 82, 98);
        const float X = 3f, Z = 2f, Eave = 2.2f, Ridge = 3.2f;
        Vector3 inside = V(0f, 1f, 0f);
        b.FaceAway(wall, inside, V(-X, 0, Z), V(-X, Eave, Z), V(X, Eave, Z), V(X, 0, Z));           // front, +z
        b.FaceAway(wall, inside, V(X, 0, -Z), V(X, Eave, -Z), V(-X, Eave, -Z), V(-X, 0, -Z));       // back
        b.FaceAway(Mul(wall, .94f), inside, V(X, 0, Z), V(X, Eave, Z), V(X, Ridge, 0), V(X, Eave, -Z), V(X, 0, -Z));       // gable ends
        b.FaceAway(Mul(wall, .94f), inside, V(-X, 0, -Z), V(-X, Eave, -Z), V(-X, Ridge, 0), V(-X, Eave, Z), V(-X, 0, Z));
        const float Over = .35f, Drop = Over * (Ridge - Eave) / Z;
        b.FaceAway(roof, inside, V(-X - .3f, Eave - Drop, Z + Over), V(-X - .3f, Ridge + .04f, 0), V(X + .3f, Ridge + .04f, 0), V(X + .3f, Eave - Drop, Z + Over));
        b.FaceAway(Mul(roof, .9f), inside, V(X + .3f, Eave - Drop, -Z - Over), V(X + .3f, Ridge + .04f, 0), V(-X - .3f, Ridge + .04f, 0), V(-X - .3f, Eave - Drop, -Z - Over));
        b.FaceAway(trim, inside, V(-.45f, 0, Z + .04f), V(-.45f, 1.9f, Z + .04f), V(.45f, 1.9f, Z + .04f), V(.45f, 0, Z + .04f));                 // door
        b.FaceAway(glass, inside, V(1.3f, 1f, Z + .04f), V(1.3f, 1.75f, Z + .04f), V(2.3f, 1.75f, Z + .04f), V(2.3f, 1f, Z + .04f));            // windows
        b.FaceAway(glass, inside, V(-2.3f, 1f, Z + .04f), V(-2.3f, 1.75f, Z + .04f), V(-1.3f, 1.75f, Z + .04f), V(-1.3f, 1f, Z + .04f));
        b.FaceAway(glass, inside, V(X + .04f, 1f, .5f), V(X + .04f, 1.75f, .5f), V(X + .04f, 1.75f, -.5f), V(X + .04f, 1f, -.5f));
        b.Box(V(-1.8f, 3.2f, -.8f), V(.16f, .55f, .16f), Rgb(52, 50, 50));              // stove pipe
        b.Gradient(0, .84f, 1.03f);
    }

    private static void HangarShelter(MapKitShapes.Shape shape, int variant)
    {
        MapMeshBuilder b = shape.Mesh;
        Color shell = variant == 0 ? Rgb(150, 150, 146) : Rgb(84, 92, 70), inside = Mul(shell, .55f), floor = Rgb(104, 104, 102);
        const int Steps = 8;
        const float HalfDepth = 11f;
        var outer = new Vector3[Steps + 1]; var inner = new Vector3[Steps + 1];
        for (int k = 0; k <= Steps; k++)
        {
            float a = Mathf.PI * k / Steps;
            outer[k] = V(-15f * Mathf.Cos(a), 9f * Mathf.Sin(a), 0f);
            inner[k] = V(-14.1f * Mathf.Cos(a), 8.35f * Mathf.Sin(a), 0f);
        }
        Vector3 front = V(0, 0, HalfDepth), back = V(0, 0, -HalfDepth);
        for (int k = 0; k < Steps; k++)
        {
            float shade = .9f + .1f * Mathf.Sin(Mathf.PI * (k + .5f) / Steps);
            b.FaceAway(Mul(shell, shade), Vector3.zero, outer[k] + back, outer[k + 1] + back, outer[k + 1] + front, outer[k] + front); // the shell
            b.FaceTo(inside, -(inner[k] + inner[k + 1]), inner[k] + front, inner[k + 1] + front, inner[k + 1] + back, inner[k] + back); // its inside
            b.FaceTo(Mul(shell, 1.08f), Vector3.forward, outer[k] + front, outer[k + 1] + front, inner[k + 1] + front, inner[k] + front); // the open front's rim
            // Two ribs stand proud of the shell, front and back.
            foreach (float z in new[] { HalfDepth - 1.2f, -HalfDepth + 1.2f })
            {
                Vector3 lift0 = outer[k] * 1.035f, lift1 = outer[k + 1] * 1.035f, at = V(0, 0, z);
                b.FaceAway(Mul(shell, .8f), at, lift0 + at - V(0, 0, .6f), lift1 + at - V(0, 0, .6f), lift1 + at + V(0, 0, .6f), lift0 + at + V(0, 0, .6f));
            }
        }
        var wall = new Vector3[Steps + 1];
        for (int k = 0; k <= Steps; k++) wall[k] = outer[Steps - k] + back;
        b.FaceTo(Mul(shell, .86f), Vector3.back, wall);                                                                      // back wall
        b.FaceTo(floor, Vector3.up, V(-14.1f, .06f, -HalfDepth), V(-14.1f, .06f, HalfDepth + 6f), V(14.1f, .06f, HalfDepth + 6f), V(14.1f, .06f, -HalfDepth));
        b.Gradient(0, .86f, 1.03f);
        shape.Collision = new MapMeshBuilder();
        shape.CollisionConvex = false; // two side walls, a roof and the back wall: an aircraft can roll in
        shape.Collision.Box(V(-13.6f, 3f, 0f), V(1.4f, 3f, HalfDepth), Color.white, 0f, true);
        shape.Collision.Box(V(13.6f, 3f, 0f), V(1.4f, 3f, HalfDepth), Color.white, 0f, true);
        shape.Collision.Box(V(0f, 7.9f, 0f), V(12.2f, 1.1f, HalfDepth), Color.white, 0f, true);
        shape.Collision.Box(V(0f, 4.5f, -HalfDepth + .5f), V(15f, 4.5f, .5f), Color.white, 0f, true);
    }

    private static void ControlTower(MapMeshBuilder b)
    {
        Color white = Rgb(228, 228, 224), glass = Rgb(60, 90, 110), grey = Rgb(120, 122, 124), dark = Rgb(58, 60, 64);
        b.Box(V(0f, 4.8f, 0f), V(2.2f, 4.8f, 2.2f), white);                                         // shaft
        b.Box(V(4.4f, 1.6f, 0f), V(2.4f, 1.6f, 3f), Mul(white, .93f));                              // annex
        b.FaceTo(dark, Vector3.forward, V(5.2f, 0f, 3.04f), V(5.2f, 2.1f, 3.04f), V(6.2f, 2.1f, 3.04f), V(6.2f, 0f, 3.04f));               // its door
        foreach (float y in new[] { 3.2f, 6.2f })
            b.FaceTo(glass, Vector3.forward, V(-.5f, y, 2.24f), V(-.5f, y + 1.4f, 2.24f), V(.5f, y + 1.4f, 2.24f), V(.5f, y, 2.24f));    // stair windows
        b.Box(V(0f, 9.75f, 0f), V(3.2f, .15f, 3.2f), grey, 0f, true);                               // cab floor and walkway
        Vector3[] sill = Square(9.9f, 2.7f), head = Square(12.4f, 3.3f);
        b.Band(glass, sill, head);                                                                  // glazing, leaning out
        for (int i = 0; i < 4; i++)                                                                 // corner posts
        {
            Vector3 low = sill[i] * 1.02f, high = head[i] * 1.02f, side = Turn(V(.16f, 0f, 0f), 45f + i * 90f);
            low.y = sill[i].y; high.y = head[i].y;
            b.FaceAway(white, V(0f, 11f, 0f), low - side, high - side, high + side, low + side);
        }
        b.Box(V(0f, 12.62f, 0f), V(3.7f, .22f, 3.7f), white, 0f, true);                             // roof
        b.Box(V(-1.4f, 13.2f, -1.2f), V(.7f, .36f, .5f), grey);                                     // plant
        b.Box(V(1.2f, 13.42f, .8f), V(.07f, .58f, .07f), dark);                                     // aerial
        b.Gradient(0, .86f, 1.03f);
    }

    /// <summary>A square ring, counter-clockwise seen from above, corners on the diagonals.</summary>
    private static Vector3[] Square(float y, float half) { return new[] { V(half, y, -half), V(half, y, half), V(-half, y, half), V(-half, y, -half) }; }

    private static void Windsock(MapMeshBuilder b)
    {
        Color orange = Rgb(230, 110, 30), white = Rgb(236, 236, 232), pole = Rgb(196, 196, 192);
        b.Band(pole, Ring(0f, .09f, 3), Ring(6f, .05f, 3));
        // The sock streams along +z and droops; its tail sways (colour alpha) under the scenery material.
        const int Sides = 5, Bands = 3;
        Vector3 mouth = V(0f, 5.72f, .12f), along = V(0f, -.22f, 1f).normalized, up = Vector3.Cross(along, Vector3.right).normalized;
        for (int k = 0; k < Bands; k++)
        {
            float t0 = k / (float)Bands, t1 = (k + 1) / (float)Bands;
            Vector3[] near = SockRing(mouth + along * (2.4f * t0), up, Mathf.Lerp(.46f, .2f, t0), Sides), far = SockRing(mouth + along * (2.4f * t1), up, Mathf.Lerp(.46f, .2f, t1), Sides);
            for (int i = 0; i < Sides; i++)
            {
                int j = (i + 1) % Sides;
                Vector3 normal = Vector3.Cross(far[i] - near[i], far[j] - near[i]).normalized;
                bool flip = Vector3.Dot(normal, near[i] - (mouth + along * (2.4f * t0))) < 0f;
                if (flip) normal = -normal;
                Color colour = k % 2 == 0 ? orange : white;
                int a = b.Add(near[i], normal, colour, t0), c = b.Add(far[i], normal, colour, t1), d = b.Add(far[j], normal, colour, t1), e = b.Add(near[j], normal, colour, t0);
                if (flip) b.Quad(a, e, d, c); else b.Quad(a, c, d, e);
            }
        }
    }

    private static Vector3[] SockRing(Vector3 centre, Vector3 up, float radius, int sides)
    {
        var ring = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            ring[i] = centre + (Vector3.right * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius;
        }
        return ring;
    }

    private static void FuelTank(MapMeshBuilder b, int variant)
    {
        Color tank = Rgb(225, 225, 220), concrete = Rgb(140, 140, 136), dark = Rgb(70, 72, 76), red = Rgb(176, 40, 34);
        if (variant == 0)
        {
            // 8 x 2.6 m, lying on two saddles along x.
            const int Sides = 10;
            const float R = 1.3f, Y = 1.75f, Half = 3.4f;
            Func<float, float, Vector3[]> ring = (x, r) =>
            {
                var points = new Vector3[Sides];
                for (int i = 0; i < Sides; i++)
                {
                    float a = i * Mathf.PI * 2f / Sides;
                    points[i] = V(x, Y + Mathf.Sin(a) * r, Mathf.Cos(a) * r); // counter-clockwise seen from +x
                }
                return points;
            };
            Vector3[] left = ring(-Half, R), right = ring(Half, R), leftEnd = ring(-Half - .6f, R * .62f), rightEnd = ring(Half + .6f, R * .62f);
            for (int i = 0; i < Sides; i++)
            {
                int j = (i + 1) % Sides;
                Vector3 axis = V(0f, Y, 0f);
                b.FaceAway(tank, axis, left[i], left[j], right[j], right[i]);
                b.FaceAway(Mul(tank, .96f), axis, right[i], right[j], rightEnd[j], rightEnd[i]);
                b.FaceAway(Mul(tank, .96f), axis, left[j], left[i], leftEnd[i], leftEnd[j]);
            }
            b.FaceTo(Mul(tank, .92f), Vector3.right, rightEnd);
            b.FaceTo(Mul(tank, .92f), Vector3.left, leftEnd);
            foreach (float x in new[] { -2.2f, 2.2f }) b.Box(V(x, .45f, 0f), V(.35f, .45f, 1.25f), concrete);
            b.Box(V(.8f, Y + R + .12f, 0f), V(.35f, .16f, .35f), dark);                 // manhole
            b.FaceTo(red, Vector3.forward, V(-1.2f, Y - .25f, R + .02f), V(-1.2f, Y + .25f, R + .02f), V(-.2f, Y + .25f, R + .02f), V(-.2f, Y - .25f, R + .02f)); // hazard plate
        }
        else
        {
            // 6 m across, 7 m tall, standing on a plinth.
            const int Sides = 12;
            b.Band(concrete, Ring(0f, 3.25f, Sides), Ring(.3f, 3.25f, Sides));
            b.Band(tank, Ring(.3f, 3f, Sides), Ring(5.6f, 3f, Sides));
            b.Band(Mul(tank, .9f), Ring(5.6f, 3f, Sides), Ring(6.1f, 3f, Sides));        // wind girder
            b.Cone(Mul(tank, .97f), Ring(6.1f, 3f, Sides), V(0f, 7f, 0f));
            b.Box(V(0f, 7f, 0f), V(.3f, .22f, .3f), dark);                                // vent
            b.Box(V(3.3f, 3f, 0f), V(.14f, 3f, .3f), dark);                               // ladder
            b.Box(V(0f, .55f, 3.5f), V(.2f, .2f, .6f), red);                              // valve
        }
        b.Gradient(0, .86f, 1.03f);
    }

    private static void Pier(Shape shape, int variant)
    {
        MapMeshBuilder b = shape.Mesh;
        bool timber = variant == 0;
        Color deck = timber ? Rgb(120, 96, 70) : Rgb(150, 150, 146), pile = timber ? Rgb(84, 66, 50) : Rgb(118, 118, 114), post = timber ? Rgb(70, 54, 40) : Rgb(190, 160, 40);
        b.Box(V(0f, 1.95f, 0f), V(3f, .25f, 30f), deck, 0f, true);
        if (timber)
        {
            for (int n = 0; n < 7; n++)
                foreach (float x in new[] { -2.6f, 2.6f })
                {
                    float z = -27f + n * 9f;
                    b.Band(pile, Ring(V(x, -14f, z), .3f, .3f, 3, n), Ring(V(x, 2.75f, z), .24f, .24f, 3, n));
                }
            foreach (float z in new[] { 27.5f, 9f, -9f }) b.Box(V(2.3f, 2.5f, z), V(.2f, .3f, .2f), post);
        }
        else
        {
            for (int n = 0; n < 4; n++)
            {
                float z = -25.5f + n * 17f;
                foreach (float x in new[] { -2.2f, 2.2f }) b.Band(pile, Ring(V(x, -14f, z), .45f, .45f, 4, Mathf.PI * .25f), Ring(V(x, 1.3f, z), .45f, .45f, 4, Mathf.PI * .25f));
                b.Box(V(0f, 1.5f, z), V(3f, .2f, .6f), Mul(pile, 1.08f));               // cap beam
            }
            foreach (float x in new[] { -2.85f, 2.85f }) b.Box(V(x, 2.33f, 0f), V(.15f, .13f, 30f), post);  // kerbs
        }
        b.Gradient(0, .7f, 1.03f); // the piles go dark toward the water
        shape.Collision = new MapMeshBuilder();
        shape.Collision.Box(V(0f, -5.9f, 0f), V(3f, 8.1f, 30f), Color.white, 0f, true);
    }

    /// <summary>One gun of a ring of giant guns, +z down the barrel: an eight-sided concrete pad, a turret house and a
    /// barrel 90 m long. Variant 0 still stands; 1 lies broken across its pad; 2 is a burnt stump.</summary>
    private static void Railgun(MapMeshBuilder b, int variant)
    {
        Color steel = Rgb(98, 102, 108), burnt = Rgb(40, 36, 34), concrete = Rgb(136, 132, 124);
        Color body = variant == 0 ? steel : Mix(steel, burnt, .65f), pad = variant == 0 ? concrete : Mix(concrete, burnt, .4f);
        b.Box(V(0, 2f, 0), V(22f, 2f, 22f), pad);
        b.Box(V(0, 2f, 0), V(22f, 2f, 22f), Mul(pad, .96f), 45f);
        b.Box(V(0, 8.5f, -2f), V(10f, 4.5f, 13f), body);
        if (variant == 0)
        {
            float tilt = 22f * Mathf.Deg2Rad;
            Vector3 along = V(0, Mathf.Sin(tilt), Mathf.Cos(tilt)), up = V(0, Mathf.Cos(tilt), -Mathf.Sin(tilt));
            foreach (float x in new[] { -3.2f, 3.2f })
                b.Box(V(x, 13f, 6f) + along * 45f, V(1.5f, 0, 0), up * 2.4f, along * 45f, x < 0 ? body : Mul(body, .92f));
            b.Box(V(0, 13f, 6f) + along * 12f, V(5.2f, 0, 0), up * 3.6f, along * 12f, Mul(body, .8f));   // the breech
        }
        else if (variant == 1)
        {
            b.Box(V(7f, 6.2f, 34f), V(2.6f, 2.2f, 24f), body, 14f);
            b.Box(V(-11f, 2.4f, 82f), V(2.6f, 2.4f, 17f), burnt, -27f);
            b.Box(V(-9f, 5.5f, -6f), V(4f, 1.5f, 6f), burnt, 30f);
        }
        else
        {
            float tilt = 50f * Mathf.Deg2Rad;
            Vector3 along = V(0, Mathf.Sin(tilt), Mathf.Cos(tilt)), up = V(0, Mathf.Cos(tilt), -Mathf.Sin(tilt));
            b.Box(V(0, 12f, 4f) + along * 9f, V(3.4f, 0, 0), up * 2.6f, along * 9f, burnt);
        }
        b.Gradient(0, .85f, 1.03f);
    }

    private static void Lighthouse(MapMeshBuilder b, int variant)
    {
        Color white = Rgb(236, 234, 228), red = Rgb(176, 34, 30), grey = Rgb(96, 98, 102), lamp = Rgb(236, 222, 150), stone = Rgb(128, 126, 122);
        int sides = variant == 0 ? 10 : 8;
        float phase = Mathf.PI / sides;
        Func<float, float> taper = y => Mathf.Lerp(3.4f, 2.25f, (y - 1.2f) / 15.6f);
        b.Band(stone, Ring(0f, 4.3f, sides, phase), Ring(1.2f, 4.1f, sides, phase));
        b.Cap(Mul(stone, 1.06f), Ring(1.2f, 4.1f, sides, phase), true);
        float[] marks = variant == 0 ? new[] { 1.2f, 4.3f, 7.4f, 10.5f, 13.6f, 16.8f } : new[] { 1.2f, 6.4f, 11.6f, 16.8f };
        for (int k = 0; k + 1 < marks.Length; k++)
            b.Band(k % 2 == 1 ? red : white, Ring(marks[k], taper(marks[k]), sides, phase), Ring(marks[k + 1], taper(marks[k + 1]), sides, phase));
        b.Band(grey, Ring(16.8f, 2.25f, sides, phase), Ring(17.3f, 3.3f, sides, phase));               // corbel under the gallery
        b.Cap(Mul(grey, 1.15f), Ring(17.3f, 3.3f, sides, phase), true);
        b.Band(Mul(grey, .8f), Ring(17.3f, 3.25f, sides, phase), Ring(18.25f, 3.25f, sides, phase));   // railing
        b.Band(lamp, Ring(17.3f, 1.9f, sides, phase), Ring(20.3f, 1.9f, sides, phase));                // lantern
        b.Cone(red, Ring(20.3f, 2.25f, sides, phase), V(0f, 22.7f, 0f));
        b.Box(V(0f, 23.3f, 0f), V(.12f, .7f, .12f), grey);                                             // finial
        if (variant == 1)
        {
            // The keeper's house against the tower.
            const float X0 = 3.2f, X1 = 9.2f, Z = 2.4f, Eave = 2.6f, Ridge = 3.9f;
            Vector3 home = V(6.2f, 1f, 0f);
            b.FaceAway(white, home, V(X0, 0, Z), V(X0, Eave, Z), V(X1, Eave, Z), V(X1, 0, Z));
            b.FaceAway(white, home, V(X1, 0, -Z), V(X1, Eave, -Z), V(X0, Eave, -Z), V(X0, 0, -Z));
            b.FaceAway(Mul(white, .94f), home, V(X1, 0, Z), V(X1, Eave, Z), V(X1, Ridge, 0), V(X1, Eave, -Z), V(X1, 0, -Z));
            b.FaceAway(red, home, V(X0, Eave, Z + .3f), V(X0, Ridge, 0), V(X1 + .3f, Ridge, 0), V(X1 + .3f, Eave, Z + .3f));
            b.FaceAway(Mul(red, .9f), home, V(X1 + .3f, Eave, -Z - .3f), V(X1 + .3f, Ridge, 0), V(X0, Ridge, 0), V(X0, Eave, -Z - .3f));
            b.FaceAway(grey, home, V(5.6f, 0, Z + .04f), V(5.6f, 1.9f, Z + .04f), V(6.5f, 1.9f, Z + .04f), V(6.5f, 0, Z + .04f));
        }
        b.Gradient(0, .88f, 1.03f);
    }

    private static void RadarDome(MapMeshBuilder b, System.Random random)
    {
        Color panel = Rgb(236, 236, 232), plinth = Rgb(168, 168, 162), dark = Rgb(62, 64, 68);
        const int Sides = 8;
        float phase = Mathf.PI / Sides;
        b.Band(plinth, Ring(0f, 5.2f, Sides, phase), Ring(6f, 5.2f, Sides, phase));
        b.Cap(Mul(plinth, 1.06f), Ring(6f, 5.2f, Sides, phase), true);
        b.Band(Mul(plinth, .8f), Ring(6f, 4.3f, Sides, phase), Ring(6.5f, 4.3f, Sides, phase));        // collar
        b.FaceTo(dark, Vector3.forward, V(-.6f, 0f, 4.86f), V(-.6f, 2.1f, 4.86f), V(.6f, 2.1f, 4.86f), V(.6f, 0f, 4.86f)); // door
        b.Gradient(0, .86f, 1.02f);
        // A geodesic sphere: every face of an icosahedron cut into nine, flat panels of slightly different white.
        float g = (1f + Mathf.Sqrt(5f)) * .5f;
        Vector3[] corners =
        {
            V(-1, g, 0), V(1, g, 0), V(-1, -g, 0), V(1, -g, 0), V(0, -1, g), V(0, 1, g), V(0, -1, -g), V(0, 1, -g), V(g, 0, -1), V(g, 0, 1), V(-g, 0, -1), V(-g, 0, 1),
        };
        int[] faces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        Vector3 centre = V(0f, 10.6f, 0f);
        const float Radius = 6f;
        const int Split = 3;
        for (int f = 0; f < faces.Length; f += 3)
        {
            Vector3 p0 = corners[faces[f]], p1 = corners[faces[f + 1]], p2 = corners[faces[f + 2]];
            Func<int, int, Vector3> at = (i, j) => centre + ((p0 * (Split - i - j) + p1 * i + p2 * j) / Split).normalized * Radius;
            for (int i = 0; i < Split; i++)
                for (int j = 0; i + j < Split; j++)
                {
                    Panel(b, centre, Mul(panel, Range(random, .93f, 1.02f)), at(i, j), at(i + 1, j), at(i, j + 1));
                    if (i + j < Split - 1) Panel(b, centre, Mul(panel, Range(random, .93f, 1.02f)), at(i + 1, j), at(i + 1, j + 1), at(i, j + 1));
                }
        }
    }

    /// <summary>A flat triangle facing away from <paramref name="centre"/>; dropped when it lies inside the plinth.</summary>
    private static void Panel(MapMeshBuilder b, Vector3 centre, Color colour, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        if (Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y)) < 6.4f) return;
        if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), (p0 + p1 + p2) / 3f - centre) < 0f) { Vector3 swap = p1; p1 = p2; p2 = swap; }
        float shade = .9f + .1f * Mathf.Clamp01(((p0 + p1 + p2).y / 3f - 6f) / 10f);
        b.Face(Mul(colour, shade), p0, p1, p2);
    }

    private static void RadioMast(Shape shape)
    {
        MapMeshBuilder b = shape.Mesh;
        Color red = Rgb(200, 40, 34), white = Rgb(234, 234, 230), steel = Rgb(120, 124, 128), concrete = Rgb(140, 140, 136);
        const float Height = 45f, Reach = 1.15f;
        const int Bands = 5;
        b.Box(V(0f, .2f, 0f), V(1.8f, .2f, 1.8f), concrete);
        var legs = new Vector3[3];
        for (int i = 0; i < 3; i++) legs[i] = V(Mathf.Cos(Mathf.PI * (.5f + i * 2f / 3f)) * Reach, 0f, Mathf.Sin(Mathf.PI * (.5f + i * 2f / 3f)) * Reach);
        for (int k = 0; k < Bands; k++)
        {
            float low = .4f + (Height - .4f) * k / Bands, high = .4f + (Height - .4f) * (k + 1) / Bands;
            Color colour = k % 2 == 0 ? red : white;
            for (int i = 0; i < 3; i++)
            {
                b.Band(colour, Ring(legs[i] + Vector3.up * low, .2f, .2f, 3, i), Ring(legs[i] + Vector3.up * high, .2f, .2f, 3, i));
                // One diagonal and one girt per face and band, as flat straps in the plane of the face.
                Vector3 from = legs[i], to = legs[(i + 1) % 3], outward = (from + to).normalized * .01f;
                Vector3 a = (k % 2 == 0 ? from : to) + Vector3.up * low + outward, c = (k % 2 == 0 ? to : from) + Vector3.up * high + outward;
                Strap(b, Mul(colour, .9f), a, c, .22f);
                Strap(b, Mul(colour, .9f), from + Vector3.up * high + outward, to + Vector3.up * high + outward, .22f);
            }
        }
        for (int i = 0; i < 3; i++) Strap(b, steel, legs[i] + Vector3.up * 31f, legs[i].normalized * 24f, .16f);    // guys
        b.Box(V(0f, Height + .35f, 0f), V(.3f, .35f, .3f), red);                                                   // beacon
        shape.Collision = new MapMeshBuilder();
        shape.Collision.Box(V(0f, Height * .5f, 0f), V(1.2f, Height * .5f, 1.2f), Color.white, 0f, true);
    }

    /// <summary>A flat strap from a to c, as wide as given, lying as upright as it can.</summary>
    private static void Strap(MapMeshBuilder b, Color colour, Vector3 a, Vector3 c, float width)
    {
        Vector3 along = (c - a).normalized, side = Vector3.Cross(along, Mathf.Abs(along.y) > .9f ? Vector3.right : Vector3.up).normalized;
        side = Vector3.Cross(side, along).normalized * (width * .5f);
        b.Face(colour, a - side, c - side, c + side, a + side);
    }

    private static void Buoy(MapMeshBuilder b, int variant)
    {
        const int Sides = 8;
        Color paint = variant == 0 ? Rgb(190, 40, 36) : Rgb(40, 130, 70), dark = Rgb(46, 48, 52), white = Rgb(232, 232, 226);
        b.Band(dark, Ring(-.7f, .8f, Sides), Ring(.1f, .95f, Sides));                    // float, mostly under water
        b.Band(paint, Ring(.1f, .95f, Sides), Ring(.55f, .95f, Sides));
        b.Cap(Mul(paint, .9f), Ring(.55f, .95f, Sides), true);
        if (variant == 0)
        {
            b.Band(paint, Ring(.55f, .55f, Sides), Ring(2.3f, .55f, Sides));             // can
            b.Cap(Mul(paint, 1.1f), Ring(2.3f, .55f, Sides), true);
            b.Box(V(0f, 2.68f, 0f), V(.2f, .32f, .2f), paint);                           // topmark
        }
        else
        {
            b.Band(white, Ring(.55f, .62f, Sides), Ring(.95f, .52f, Sides));             // reflective band
            b.Cone(paint, Ring(.95f, .52f, Sides), V(0f, 2.55f, 0f));                    // cone
            b.Cone(paint, Ring(2.5f, .28f, 4), V(0f, 3.05f, 0f));                        // topmark
        }
        b.Gradient(0, .8f, 1.04f);
    }

    // ---- wrecks ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A stretch of hull lofted through stations along z: each is (z, half beam, deck height, keel height). The skin
    /// runs deck edge, bilge, keel, bilge, deck edge; the deck closes the top. Open ends are closed by bulkheads.
    /// </summary>
    private static MapMeshBuilder Hull(float[][] stations, Color hull, Color bottom, Color deck, bool closeAft, bool closeFore)
    {
        var b = new MapMeshBuilder();
        var sections = new Vector3[stations.Length][];
        for (int s = 0; s < stations.Length; s++)
        {
            float z = stations[s][0], beam = stations[s][1], top = stations[s][2], keel = stations[s][3], bilge = keel + (top - keel) * .28f;
            sections[s] = new[] { V(-beam, top, z), V(-beam * .86f, bilge, z), V(0f, keel, z), V(beam * .86f, bilge, z), V(beam, top, z) };
        }
        for (int s = 0; s + 1 < sections.Length; s++)
        {
            Vector3[] aft = sections[s], fore = sections[s + 1];
            Vector3 mid = V(0f, (aft[0].y + aft[2].y + fore[0].y + fore[2].y) * .25f, (aft[0].z + fore[0].z) * .5f);
            b.FaceAway(hull, mid, aft[1], aft[0], fore[0], fore[1]);        // port side
            b.FaceAway(bottom, mid, aft[2], aft[1], fore[1], fore[2]);      // port bottom
            b.FaceAway(bottom, mid, aft[3], aft[2], fore[2], fore[3]);      // starboard bottom
            b.FaceAway(hull, mid, aft[4], aft[3], fore[3], fore[4]);        // starboard side
            b.FaceAway(deck, mid, aft[0], aft[4], fore[4], fore[0]);        // deck
        }
        Vector3[] first = sections[0], last = sections[sections.Length - 1];
        if (closeAft) b.FaceTo(Mul(hull, .8f), Vector3.back, first[0], first[1], first[2], first[3], first[4]);
        if (closeFore) b.FaceTo(Mul(hull, .8f), Vector3.forward, last[4], last[3], last[2], last[1], last[0]);
        return b;
    }

    private static void Wreck(Shape shape, int variant)
    {
        MapMeshBuilder b = shape.Mesh;
        Color rust = Rgb(120, 70, 46), bottom = Rgb(88, 44, 36), deck = Rgb(98, 86, 72), house = Rgb(190, 180, 164), dark = Rgb(56, 52, 50);
        shape.Collision = new MapMeshBuilder();
        if (variant == 0)
        {
            // A 55 m coaster broken in two: the stern sits upright in the sand, the bow has swung off and heeled over.
            MapMeshBuilder stern = Hull(new[]
            {
                new[] { -27.5f, 3.4f, 5.2f, 1.2f }, new[] { -25f, 4.6f, 5f, .2f }, new[] { -18f, 5f, 4.6f, 0f }, new[] { -9f, 5f, 4.4f, 0f }, new[] { -1.5f, 5f, 4.4f, 0f },
            }, rust, bottom, deck, true, true);
            stern.Box(V(0f, 7f, -20f), V(3.6f, 2.4f, 3.4f), house);                    // deckhouse
            stern.Box(V(0f, 10.4f, -19.2f), V(2.8f, 1f, 2f), Mul(house, 1.05f));       // wheelhouse
            stern.Box(V(0f, 11.2f, -23f), V(.9f, 1.8f, 1.1f), dark);                   // funnel
            stern.Box(V(0f, 4.9f, -8f), V(3.2f, .45f, 4.2f), Mul(rust, .8f));          // hatch coaming
            MapMeshBuilder bow = Hull(new[]
            {
                new[] { 1.5f, 5f, 4.4f, 0f }, new[] { 10f, 5f, 4.5f, 0f }, new[] { 18f, 4.6f, 4.9f, .1f }, new[] { 24f, 2.8f, 5.6f, .6f }, new[] { 27.5f, .25f, 6.2f, 2.2f },
            }, rust, bottom, deck, true, true);
            bow.Box(V(0f, 4.95f, 9f), V(3.2f, .45f, 5f), Mul(rust, .8f));
            bow.Box(V(0f, 8.6f, 17f), V(.22f, 3.8f, .22f), dark);                      // foremast
            Func<Vector3, Vector3> sternTurn = p => Rotate(p, -2f, 0f, 7f), bowTurn = p => Rotate(p, 5f, 16f, -19f);
            Vector3 sternAt = V(0f, -1.6f, 0f), bowAt = V(2.5f, -1.2f, 3.5f);
            b.Append(stern, sternTurn, sternAt);
            b.Append(bow, bowTurn, bowAt);
            shape.CollisionConvex = false;
            shape.Collision.Box(sternTurn(V(0f, 2.6f, -14.5f)) + sternAt, sternTurn(V(5f, 0f, 0f)), sternTurn(V(0f, 2.6f, 0f)), sternTurn(V(0f, 0f, 13f)), Color.white, true);
            shape.Collision.Box(bowTurn(V(0f, 2.6f, 14f)) + bowAt, bowTurn(V(4.6f, 0f, 0f)), bowTurn(V(0f, 2.6f, 0f)), bowTurn(V(0f, 0f, 12.5f)), Color.white, true);
        }
        else
        {
            // A 30 m trawler on her side.
            MapMeshBuilder hull = Hull(new[]
            {
                new[] { -15f, 2.4f, 4.2f, 1.4f }, new[] { -12.5f, 3.3f, 3.9f, .3f }, new[] { -5f, 3.6f, 3.6f, 0f }, new[] { 4f, 3.5f, 3.8f, 0f }, new[] { 11f, 2.4f, 4.5f, .3f }, new[] { 15f, .2f, 5.2f, 1.8f },
            }, Rgb(52, 84, 96), bottom, deck, true, true);
            hull.Box(V(0f, 5.2f, 5.5f), V(2.2f, 1.5f, 2.4f), house);                   // wheelhouse, forward
            hull.Box(V(0f, 7.1f, 5.2f), V(1.5f, .4f, 1.6f), Mul(house, .9f));
            hull.Box(V(0f, 7.4f, -4f), V(.18f, 3.6f, .18f), dark);                     // mast
            hull.Box(V(0f, 9.3f, -8f), V(.12f, .12f, 4f), dark);                       // boom
            hull.Box(V(0f, 4.1f, -9.5f), V(2.2f, .5f, 2f), Mul(rust, .9f));            // winch
            Func<Vector3, Vector3> turn = p => Rotate(p, 3f, 0f, 64f);
            Vector3 at = V(1.6f, 1.2f, 0f);
            b.Append(hull, turn, at);
            shape.Collision.Box(turn(V(0f, 2.2f, 0f)) + at, turn(V(3.4f, 0f, 0f)), turn(V(0f, 2.2f, 0f)), turn(V(0f, 0f, 15f)), Color.white, true);
        }
        // Rust takes the plating from the waterline up.
        for (int i = 0; i < b.Vertices.Count; i++)
        {
            float streak = Mathf.Clamp01(1f - b.Vertices[i].y / 3.5f) * .45f;
            b.Colours[i] = Mix(b.Colours[i], rust, streak);
        }
        b.Gradient(0, .78f, 1.04f);
    }
}
