using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Contacts as miniatures on the relief model: one low-poly shape per <see cref="MapGlyph"/>,
    /// standing on the terrain, turned to its known heading and tinted with its map colour.
    ///
    /// <para>Built for cost first. Shapes are a handful of boxes and extruded outlines made once in
    /// code, with the light baked into vertex shade, so the material is unlit. Every miniature on the
    /// map is written into ONE mesh: one renderer, one draw call, no per-unit objects, and because the
    /// mesh lives in the relief scene a pan or an orbit costs nothing at all.</para>
    ///
    /// <para>A rewrite fills flat arrays with no allocation. The upload then sends only what changed:
    /// when the same contacts are drawn as last time (the usual case: someone drove a little, or the
    /// zoom moved) it is one vertex-buffer copy, with the index buffer and the colours left alone. The
    /// relief asks for a rewrite only when a contact, the selection or the zoom really changed
    /// (see <c>MfdTerrainRelief.UpdateMinis</c>).</para>
    /// </summary>
    internal static class MapMinis
    {
        /// <summary>Hard ceiling on written vertices; contacts past it keep their flat glyph.</summary>
        internal const int MaximumVertices = 120000;

        private sealed class Shape
        {
            internal Vector3[] Points;
            internal byte[] Shade;
            internal int[] Triangles;
            /// <summary>Where the body ends; the dark base slab after it is skipped for airborne contacts.</summary>
            internal int BodyPoints, BodyTriangles;
        }

        private static Shape[] shapes;
        /// <summary>One extruded silhouette per airframe icon, built on first sight (null = unreadable icon).</summary>
        private static readonly Dictionary<Sprite, Shape> iconShapes = new Dictionary<Sprite, Shape>();
        private static Vector3[] points = new Vector3[8192];
        private static Color32[] colours = new Color32[8192];
        private static int[] triangles = new int[16384];
        private static int count, indexCount;
        private static uint layout, tints;
        private static int sentCount = -1;
        private static uint sentLayout, sentTints;
        private static Mesh mesh;
        private static Material material;
        private static GameObject holder;

        /// <summary>Uploads that had to resend the index buffer, and all uploads: the harness reads these.</summary>
        internal static int FullUploads, Uploads;

        private const MeshUpdateFlags Quiet = MeshUpdateFlags.DontRecalculateBounds |
            MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;

        /// <summary>Starts a rewrite; creates the single renderer under the relief scene on first use.</summary>
        internal static void Begin(Transform scene, int layer)
        {
            count = indexCount = 0;
            layout = tints = 2166136261u;
            if (shapes == null) shapes = BuildShapes();
            if (mesh == null)
            {
                mesh = new Mesh { name = "NOAvionics.MapMinis", indexFormat = IndexFormat.UInt32,
                    hideFlags = HideFlags.HideAndDontSave };
                mesh.MarkDynamic();
                sentCount = -1;
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
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
        }

        /// <summary>
        /// Writes one miniature. <paramref name="at"/> is its foot in the relief scene's local space,
        /// <paramref name="forward"/> its unit heading on the ground plane, <paramref name="length"/>
        /// its nose-to-tail size in the same units.
        /// </summary>
        internal static bool Add(MapGlyph glyph, Vector3 at, Vector2 forward, float length, Color32 tint, bool grounded) =>
            Write(shapes[(int)glyph], (uint)((int)glyph * 2 + (grounded ? 1 : 0)), at, forward, length, tint, grounded);

        /// <summary>
        /// Writes a miniature extruded from the airframe's own map icon, so every type stands as its
        /// own silhouette; falls back to <paramref name="fallback"/> when the icon cannot be read.
        /// </summary>
        internal static bool Add(Sprite icon, MapGlyph fallback, Vector3 at, Vector2 forward, float length, Color32 tint, bool grounded)
        {
            if (icon == null) return Add(fallback, at, forward, length, tint, grounded);
            if (!iconShapes.TryGetValue(icon, out Shape shape)) iconShapes[icon] = shape = IconShape(icon);
            return shape == null ? Add(fallback, at, forward, length, tint, grounded)
                : Write(shape, (uint)icon.GetInstanceID() * 2u + 0x9e3779b9u + (grounded ? 1u : 0u), at, forward, length, tint, grounded);
        }

        private static bool Write(Shape shape, uint key, Vector3 at, Vector2 forward, float length, Color32 tint, bool grounded)
        {
            int vertices = grounded ? shape.Points.Length : shape.BodyPoints;
            int indices = grounded ? shape.Triangles.Length : shape.BodyTriangles;
            if (count + vertices > MaximumVertices) return false;
            if (count + vertices > points.Length)
            {
                int size = Mathf.Min(MaximumVertices, Mathf.Max(points.Length * 2, count + vertices));
                System.Array.Resize(ref points, size);
                System.Array.Resize(ref colours, size);
            }
            if (indexCount + indices > triangles.Length)
                System.Array.Resize(ref triangles, Mathf.Max(triangles.Length * 2, indexCount + indices));

            // The map tint is a UI colour, loud on a solid: pull it a fifth of the way to its own grey.
            int grey = (tint.r * 77 + tint.g * 150 + tint.b * 29) >> 8;
            int inkR = tint.r + (grey - tint.r) / 5, inkG = tint.g + (grey - tint.g) / 5, inkB = tint.b + (grey - tint.b) / 5;
            // Right is (forward.y, -forward.x): the shape's +Z is its nose.
            float rx = forward.y * length, rz = -forward.x * length, fx = forward.x * length, fz = forward.y * length;
            Vector3[] source = shape.Points;
            byte[] shade = shape.Shade;
            int first = count;
            for (int i = 0; i < vertices; i++)
            {
                Vector3 p = source[i];
                points[first + i] = new Vector3(at.x + p.x * rx + p.z * fx, at.y + p.y * length, at.z + p.x * rz + p.z * fz);
                int lit = shade[i] + 1;
                colours[first + i] = new Color32((byte)(inkR * lit >> 8), (byte)(inkG * lit >> 8), (byte)(inkB * lit >> 8), 255);
            }
            int[] order = shape.Triangles;
            for (int i = 0; i < indices; i++) triangles[indexCount + i] = first + order[i];
            count += vertices;
            indexCount += indices;
            // What is drawn (which shapes, in which order) and how it is tinted, as two running hashes:
            // the upload skips whatever did not change.
            layout = (layout ^ key) * 16777619u;
            tints = (tints ^ (uint)(tint.r | tint.g << 8 | tint.b << 16)) * 16777619u;
            return true;
        }

        internal static void End()
        {
            if (mesh == null) return;
            if (count == 0)
            {
                if (sentCount != 0) mesh.Clear();
                sentCount = 0;
                return;
            }
            Uploads++;
            if (count == sentCount && layout == sentLayout)
            {
                // Same contacts as last time: they only moved or changed size.
                mesh.SetVertices(points, 0, count, Quiet);
                if (tints != sentTints) mesh.SetColors(colours, 0, count, Quiet);
            }
            else
            {
                FullUploads++;
                mesh.Clear();
                mesh.SetVertices(points, 0, count, Quiet);
                mesh.SetColors(colours, 0, count, Quiet);
                mesh.SetTriangles(triangles, 0, indexCount, 0, false);
                // The relief camera sees the whole model; never let a stale bound cull the layer.
                mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 1e5f));
            }
            sentCount = count;
            sentLayout = layout;
            sentTints = tints;
        }

        /// <summary>Empties the layer; the renderer goes with the relief scene that owns it.</summary>
        internal static void Clear()
        {
            if (mesh != null && sentCount != 0) mesh.Clear();
            sentCount = 0;
        }

        // ------------------------------------------------------------ shapes
        // Unit space: nose toward +Z, up +Y, about one unit long, standing on y = 0.

        private sealed class Draft
        {
            internal readonly List<Vector3> Points = new List<Vector3>(96);
            internal readonly List<byte> Shade = new List<byte>(96);
            internal readonly List<int> Triangles = new List<int>(144);
            internal int BodyPoints, BodyTriangles;
        }

        private static Shape[] BuildShapes()
        {
            var all = new Draft[System.Enum.GetValues(typeof(MapGlyph)).Length];
            for (int i = 0; i < all.Length; i++) all[i] = new Draft();

            Draft s = all[(int)MapGlyph.Armor];
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

            var built = new Shape[all.Length];
            for (int i = 0; i < all.Length; i++) built[i] = Finish(all[i]);
            return built;
        }

        private static Shape Finish(Draft draft) => new Shape { Points = draft.Points.ToArray(), Shade = draft.Shade.ToArray(),
            Triangles = draft.Triangles.ToArray(), BodyPoints = draft.BodyPoints, BodyTriangles = draft.BodyTriangles };

        /// <summary>Silhouette grid along the icon's longest side.</summary>
        private const int IconCells = 24, IconSamples = 4;

        /// <summary>
        /// Extrudes a map icon (top-down, nose up) into a miniature: the whole silhouette as a thin
        /// wing slab, the cells along its centre line raised as a fuselage spine, both greedily merged
        /// into as few boxes as the outline allows. The icon is read back once through a blit, so an
        /// unreadable or atlas-packed texture works too.
        /// </summary>
        private static Shape IconShape(Sprite icon)
        {
            bool[] solid;
            int w, h;
            try
            {
                // ponytail: tight-packed or rotated atlas sprites throw/read wrong; they keep the common shape.
                Rect rect = icon.textureRect;
                Texture texture = icon.texture;
                if (texture == null || rect.width < 4f || rect.height < 4f) return null;
                bool wide = rect.width >= rect.height;
                w = wide ? IconCells : Mathf.Max(4, Mathf.RoundToInt(IconCells * rect.width / rect.height));
                h = wide ? Mathf.Max(4, Mathf.RoundToInt(IconCells * rect.height / rect.width)) : IconCells;
                int sw = w * IconSamples, sh = h * IconSamples;
                RenderTexture target = RenderTexture.GetTemporary(sw, sh, 0, RenderTextureFormat.ARGB32);
                RenderTexture active = RenderTexture.active;
                Graphics.Blit(texture, target, new Vector2(rect.width / texture.width, rect.height / texture.height),
                    new Vector2(rect.x / texture.width, rect.y / texture.height));
                RenderTexture.active = target;
                var read = new Texture2D(sw, sh, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, sw, sh), 0, 0, false);
                Color32[] pixels = read.GetPixels32();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(read);
                // A cell is solid when a third of its samples are: thin wings survive the downsample.
                solid = new bool[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int hits = 0;
                    for (int j = 0; j < IconSamples; j++)
                    for (int i = 0; i < IconSamples; i++)
                        if (pixels[(y * IconSamples + j) * sw + x * IconSamples + i].a > 110) hits++;
                    solid[y * w + x] = hits * 3 >= IconSamples * IconSamples;
                }
            }
            catch (System.Exception)
            {
                return null;
            }

            int minX = w, maxX = -1, minY = h, maxY = -1;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (solid[y * w + x])
                {
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            if (maxX < 0) return null;
            float cell = 1f / Mathf.Max(maxX - minX + 1, maxY - minY + 1);
            float midX = (minX + maxX + 1) * .5f, midY = (minY + maxY + 1) * .5f;
            var spine = new bool[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                spine[y * w + x] = solid[y * w + x] && Mathf.Abs(x + .5f - midX) <= 1.6f;

            var shape = new Draft();
            Extrude(shape, solid, w, h, midX, midY, cell, .02f, .08f);
            Extrude(shape, spine, w, h, midX, midY, cell, .02f, .16f);
            Base(shape, (maxX - minX + 1) * cell, (maxY - minY + 1) * cell);
            return Finish(shape);
        }

        /// <summary>Greedy rectangles over a cell mask, each extruded between two heights.</summary>
        private static void Extrude(Draft shape, bool[] mask, int w, int h, float midX, float midY, float cell,
            float bottom, float top)
        {
            var used = new bool[mask.Length];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!mask[y * w + x] || used[y * w + x]) continue;
                int across = 1;
                while (x + across < w && mask[y * w + x + across] && !used[y * w + x + across]) across++;
                int down = 1;
                for (bool grow = true; grow && y + down < h; )
                {
                    for (int i = 0; i < across && grow; i++)
                        grow = mask[(y + down) * w + x + i] && !used[(y + down) * w + x + i];
                    if (grow) down++;
                }
                for (int j = 0; j < down; j++)
                for (int i = 0; i < across; i++) used[(y + j) * w + x + i] = true;
                Box(shape, (x + across * .5f - midX) * cell, (bottom + top) * .5f, (y + down * .5f - midY) * cell,
                    across * cell, top - bottom, down * cell);
            }
        }

        /// <summary>The dark slab a ground contact stands on: contrast on any terrain, and a footprint.</summary>
        private static void Base(Draft shape, float width, float length)
        {
            shape.BodyPoints = shape.Points.Count;
            shape.BodyTriangles = shape.Triangles.Count;
            Box(shape, 0f, -.02f, 0f, width + .08f, .04f, length + .08f, 0f, .1f);
        }

        /// <summary>A box by centre and size, optionally pitched nose-up about its own centre.</summary>
        private static void Box(Draft shape, float cx, float cy, float cz, float sx, float sy, float sz,
            float pitch = 0f, float tone = 1f)
        {
            var centre = new Vector3(cx, cy, cz);
            Quaternion turn = Quaternion.Euler(-pitch, 0f, 0f);
            Vector3 x = turn * Vector3.right * (sx * .5f), y = turn * Vector3.up * (sy * .5f),
                z = turn * Vector3.forward * (sz * .5f);
            Face(shape, centre + y, x, z, y, tone);
            // The view always looks down: a level box never shows its underside, a raised barrel can.
            if (pitch != 0f) Face(shape, centre - y, x, z, -y, tone);
            Face(shape, centre + x, z, y, x, tone);
            Face(shape, centre - x, z, y, -x, tone);
            Face(shape, centre + z, x, y, z, tone);
            Face(shape, centre - z, x, y, -z, tone);
        }

        /// <summary>A convex outline on the ground plane (x, z pairs) extruded between two heights; no underside.</summary>
        private static void Prism(Draft shape, float bottom, float top, params float[] outline)
        {
            int n = outline.Length / 2;
            byte shade = Tone(Vector3.up, 1f);
            int first = shape.Points.Count;
            for (int i = 0; i < n; i++)
            {
                shape.Points.Add(new Vector3(outline[i * 2], top, outline[i * 2 + 1]));
                shape.Shade.Add(shade);
            }
            for (int i = 1; i + 1 < n; i++)
            {
                shape.Triangles.Add(first);
                shape.Triangles.Add(first + i);
                shape.Triangles.Add(first + i + 1);
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
        private static void Face(Draft shape, Vector3 centre, Vector3 u, Vector3 v, Vector3 outward, float tone)
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
            float lit = Mathf.Abs(normal.y) > .7f ? (normal.y > 0f ? .94f : .3f)
                : .3f + .36f * Mathf.Abs(Vector3.Dot(normal, new Vector3(.8f, 0f, .6f)));
            return (byte)(255f * Mathf.Clamp01(lit * tone));
        }
    }
}
