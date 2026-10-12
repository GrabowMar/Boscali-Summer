using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Contacts as miniatures on the relief model: one low-poly shape per <see cref="MapGlyph"/>,
    /// standing on the terrain, turned to its known heading and tinted with its map colour.
    ///
    /// <para>Built for cost, not detail. Shapes are a handful of boxes and extruded outlines made once
    /// in code; lighting is baked into vertex shade, so the material is unlit. Every miniature on the
    /// map is written into one shared mesh - one renderer, one draw call, no per-unit objects - and
    /// because that mesh lives in the relief scene, panning and orbiting cost nothing. The relief
    /// rebuilds it on its own beat (see <c>MfdTerrainRelief.UpdateMinis</c>).</para>
    /// </summary>
    internal static class MapMinis
    {
        /// <summary>Hard ceiling on written vertices; contacts past it keep their flat glyph.</summary>
        internal const int MaximumVertices = 120000;

        private sealed class Shape
        {
            internal readonly List<Vector3> Points = new List<Vector3>(96);
            internal readonly List<byte> Shade = new List<byte>(96);
            internal readonly List<int> Triangles = new List<int>(144);
            /// <summary>Where the body ends; the dark base slab after it is skipped for airborne contacts.</summary>
            internal int BodyPoints, BodyTriangles;
        }

        private static Shape[] shapes;
        private static readonly List<Vector3> points = new List<Vector3>(4096);
        private static readonly List<Color32> colours = new List<Color32>(4096);
        private static readonly List<int> triangles = new List<int>(8192);
        private static Mesh mesh;
        private static Material material;
        private static GameObject holder;

        /// <summary>Starts a rebuild; creates the single renderer under the relief scene on first use.</summary>
        internal static void Begin(Transform scene, int layer)
        {
            points.Clear();
            colours.Clear();
            triangles.Clear();
            if (shapes == null) shapes = BuildShapes();
            if (mesh == null)
            {
                mesh = new Mesh { name = "NOAvionics.MapMinis", indexFormat = IndexFormat.UInt32,
                    hideFlags = HideFlags.HideAndDontSave };
                mesh.MarkDynamic();
            }
            if (holder != null) return;
            holder = new GameObject("NOAvionics.MapMinis", typeof(MeshFilter), typeof(MeshRenderer)) { layer = layer };
            holder.transform.SetParent(scene, false);
            holder.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (material == null)
            {
                // Same choice as the relief ground: the URP particle unlit shader ships an opaque,
                // depth-writing variant that multiplies vertex colour.
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                bool opaque = shader != null;
                material = new Material(opaque ? shader : Shader.Find("Sprites/Default"))
                    { mainTexture = Texture2D.whiteTexture, hideFlags = HideFlags.HideAndDontSave };
                if (opaque)
                {
                    material.SetFloat("_Cull", 0f);
                    material.renderQueue = 2001;
                }
            }
            var renderer = holder.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// Writes one miniature. <paramref name="at"/> is its foot in the relief scene's local space,
        /// <paramref name="forward"/> its unit heading on the ground plane, <paramref name="length"/>
        /// its nose-to-tail size in the same units.
        /// </summary>
        internal static bool Add(MapGlyph glyph, Vector3 at, Vector2 forward, float length, Color32 tint, bool grounded)
        {
            Shape shape = shapes[(int)glyph];
            int count = grounded ? shape.Points.Count : shape.BodyPoints;
            if (points.Count + count > MaximumVertices) return false;
            int first = points.Count;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = shape.Points[i] * length;
                // Right is (forward.y, -forward.x): the shape's +Z is its nose.
                points.Add(new Vector3(at.x + p.x * forward.y + p.z * forward.x, at.y + p.y,
                    at.z - p.x * forward.x + p.z * forward.y));
                int shade = shape.Shade[i];
                colours.Add(new Color32((byte)(tint.r * shade / 255), (byte)(tint.g * shade / 255),
                    (byte)(tint.b * shade / 255), 255));
            }
            int indices = grounded ? shape.Triangles.Count : shape.BodyTriangles;
            for (int i = 0; i < indices; i++) triangles.Add(first + shape.Triangles[i]);
            return true;
        }

        internal static void End()
        {
            if (mesh == null) return;
            mesh.Clear();
            if (points.Count == 0) return;
            mesh.SetVertices(points);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0, false);
            // The relief camera sees the whole model; never let a stale bound cull the layer.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 1e5f));
        }

        /// <summary>Empties the layer; the renderer goes with the relief scene that owns it.</summary>
        internal static void Clear()
        {
            if (mesh != null) mesh.Clear();
        }

        // ------------------------------------------------------------ shapes
        // Unit space: nose toward +Z, up +Y, about one unit long, standing on y = 0.

        private static Shape[] BuildShapes()
        {
            var all = new Shape[System.Enum.GetValues(typeof(MapGlyph)).Length];
            for (int i = 0; i < all.Length; i++) all[i] = new Shape();

            Shape s = all[(int)MapGlyph.Armor];
            Box(s, 0f, .12f, 0f, .5f, .2f, .9f);
            Box(s, 0f, .3f, -.05f, .32f, .16f, .36f);
            Box(s, 0f, .3f, .4f, .07f, .07f, .5f);
            Base(s, .66f, 1.06f);

            s = all[(int)MapGlyph.Light];
            Box(s, 0f, .12f, 0f, .42f, .16f, .76f);
            Box(s, 0f, .27f, -.08f, .36f, .14f, .36f);
            Base(s, .58f, .92f);

            s = all[(int)MapGlyph.Supply];
            Box(s, 0f, .24f, -.13f, .42f, .36f, .6f);
            Box(s, 0f, .17f, .33f, .4f, .24f, .24f);
            Base(s, .58f, 1.04f);

            s = all[(int)MapGlyph.Artillery];
            Box(s, 0f, .1f, 0f, .46f, .16f, .8f);
            Box(s, 0f, .25f, -.1f, .3f, .14f, .3f);
            Box(s, 0f, .44f, .2f, .08f, .08f, .72f, 30f);
            Base(s, .62f, .96f);

            s = all[(int)MapGlyph.AntiAir];
            Box(s, 0f, .1f, 0f, .46f, .16f, .7f);
            Box(s, 0f, .24f, 0f, .26f, .12f, .26f);
            Box(s, -.08f, .46f, .13f, .055f, .055f, .52f, 55f);
            Box(s, .08f, .46f, .13f, .055f, .055f, .52f, 55f);
            Base(s, .62f, .86f);

            s = all[(int)MapGlyph.Sam];
            Box(s, 0f, .1f, 0f, .46f, .16f, .8f);
            Box(s, 0f, .4f, 0f, .3f, .2f, .64f, 35f);
            Base(s, .62f, .96f);

            s = all[(int)MapGlyph.Radar];
            Box(s, 0f, .1f, 0f, .44f, .16f, .6f);
            Box(s, 0f, .32f, 0f, .07f, .3f, .07f);
            Box(s, 0f, .58f, .03f, .52f, .3f, .05f, 15f);
            Base(s, .6f, .76f);

            s = all[(int)MapGlyph.Helicopter];
            Box(s, 0f, .2f, .08f, .2f, .2f, .5f);
            Box(s, 0f, .22f, -.38f, .06f, .06f, .45f);
            Box(s, 0f, .3f, -.6f, .03f, .18f, .08f);
            Box(s, 0f, .34f, .08f, .95f, .015f, .08f);
            Box(s, 0f, .34f, .08f, .08f, .015f, .95f);
            Base(s, .5f, .9f);

            s = all[(int)MapGlyph.Aircraft];
            Prism(s, .02f, .12f, 0f, .55f, .09f, .2f, .09f, -.5f, -.09f, -.5f, -.09f, .2f);
            Prism(s, .05f, .09f, 0f, .22f, .5f, -.34f, .5f, -.45f, -.5f, -.45f, -.5f, -.34f);
            Box(s, 0f, .18f, -.4f, .03f, .14f, .2f);
            Base(s, .7f, 1.1f);

            s = all[(int)MapGlyph.Contact];
            Box(s, 0f, .2f, 0f, .46f, .4f, .46f);
            Base(s, .64f, .64f);

            s = all[(int)MapGlyph.Hull];
            Prism(s, 0f, .1f, 0f, .6f, .14f, .3f, .14f, -.55f, -.14f, -.55f, -.14f, .3f);
            Box(s, 0f, .17f, -.1f, .16f, .14f, .3f);
            Box(s, 0f, .3f, -.1f, .03f, .12f, .03f);
            Base(s, .4f, 1.3f);
            return all;
        }

        /// <summary>The dark slab a ground contact stands on: contrast on any terrain, and a footprint.</summary>
        private static void Base(Shape shape, float width, float length)
        {
            shape.BodyPoints = shape.Points.Count;
            shape.BodyTriangles = shape.Triangles.Count;
            Box(shape, 0f, -.02f, 0f, width, .04f, length, 0f, .22f);
        }

        /// <summary>A box by centre and size, optionally pitched nose-up about its own centre.</summary>
        private static void Box(Shape shape, float cx, float cy, float cz, float sx, float sy, float sz,
            float pitch = 0f, float tone = 1f)
        {
            var centre = new Vector3(cx, cy, cz);
            Quaternion turn = Quaternion.Euler(-pitch, 0f, 0f);
            Vector3 x = turn * Vector3.right * (sx * .5f), y = turn * Vector3.up * (sy * .5f),
                z = turn * Vector3.forward * (sz * .5f);
            Face(shape, centre + y, x, z, y, tone);
            Face(shape, centre - y, x, z, -y, tone);
            Face(shape, centre + x, z, y, x, tone);
            Face(shape, centre - x, z, y, -x, tone);
            Face(shape, centre + z, x, y, z, tone);
            Face(shape, centre - z, x, y, -z, tone);
        }

        /// <summary>A convex outline on the ground plane (x, z pairs) extruded between two heights.</summary>
        private static void Prism(Shape shape, float bottom, float top, params float[] outline)
        {
            int n = outline.Length / 2;
            for (int cap = 0; cap < 2; cap++)
            {
                float y = cap == 0 ? top : bottom;
                byte shade = Tone(cap == 0 ? Vector3.up : Vector3.down, 1f);
                int first = shape.Points.Count;
                for (int i = 0; i < n; i++)
                {
                    shape.Points.Add(new Vector3(outline[i * 2], y, outline[i * 2 + 1]));
                    shape.Shade.Add(shade);
                }
                for (int i = 1; i + 1 < n; i++)
                {
                    shape.Triangles.Add(first);
                    shape.Triangles.Add(first + i);
                    shape.Triangles.Add(first + i + 1);
                }
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var a = new Vector3(outline[i * 2], 0f, outline[i * 2 + 1]);
                var b = new Vector3(outline[j * 2], 0f, outline[j * 2 + 1]);
                Vector3 half = (b - a) * .5f, rise = Vector3.up * ((top - bottom) * .5f);
                Face(shape, a + half + Vector3.up * ((top + bottom) * .5f), half, rise,
                    Vector3.Cross(half, rise), 1f);
            }
        }

        /// <summary>One flat quad: centre, two half-edges and the way it faces. Drawn two-sided, so
        /// winding is free.</summary>
        private static void Face(Shape shape, Vector3 centre, Vector3 u, Vector3 v, Vector3 outward, float tone)
        {
            int first = shape.Points.Count;
            byte shade = Tone(outward.normalized, tone);
            shape.Points.Add(centre - u - v);
            shape.Points.Add(centre + u - v);
            shape.Points.Add(centre + u + v);
            shape.Points.Add(centre - u + v);
            for (int i = 0; i < 4; i++) shape.Shade.Add(shade);
            shape.Triangles.Add(first);
            shape.Triangles.Add(first + 1);
            shape.Triangles.Add(first + 2);
            shape.Triangles.Add(first);
            shape.Triangles.Add(first + 2);
            shape.Triangles.Add(first + 3);
        }

        /// <summary>Baked light: tops full, sides stepped by which way they face, undersides dim.</summary>
        private static byte Tone(Vector3 normal, float tone)
        {
            float lit = Mathf.Abs(normal.y) > .7f ? (normal.y > 0f ? 1f : .45f)
                : .4f + .34f * Mathf.Abs(Vector3.Dot(normal, new Vector3(.8f, 0f, .6f)));
            return (byte)(255f * Mathf.Clamp01(lit * tone));
        }
    }
}
