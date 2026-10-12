using System;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>What a contact is, at the resolution the map can show at 12 px.</summary>
    internal enum MapGlyph
    {
        /// <summary>Tracked or wheeled armour: main battle tanks and fighting vehicles.</summary>
        Armor,
        /// <summary>Light or unarmoured combat vehicles.</summary>
        Light,
        /// <summary>Trucks and unmanned ground vehicles: logistics.</summary>
        Supply,
        /// <summary>Self-propelled artillery and rocket launchers.</summary>
        Artillery,
        /// <summary>Anti-aircraft guns.</summary>
        AntiAir,
        /// <summary>Surface-to-air missile launchers.</summary>
        Sam,
        /// <summary>Search and tracking radars.</summary>
        Radar,
        /// <summary>Rotorcraft: the game's rotor sprites dissolve at map size, so this replaces them.</summary>
        Helicopter,
        /// <summary>Stand-in fixed-wing silhouette (the map keeps the game's per-type sprite for jets).</summary>
        Aircraft,
        /// <summary>Observed contact without a reliable platform class.</summary>
        Contact,
        /// <summary>Surface ship: the game's hulls are hairlines at map size, so this replaces them.</summary>
        Hull
    }

    /// <summary>The frame family: shape carries allegiance so it survives colour-blind viewing.</summary>
    internal enum MapSide { Friendly, Hostile, Neutral }

    /// <summary>
    /// Procedural contact symbols for the tactical map, generated once into static memory.
    ///
    /// <para>Frameless angular line art in the avionics kit's icon style: one stroke weight, straight
    /// runs and 45-degree cuts, each stroke edged with a black keyline so it reads on any terrain.
    /// Strokes are pure white so the game's own friendly/hostile/selected tint carries through
    /// untouched; colour alone marks allegiance. Rasterising is
    /// separated from sprite creation so the shapes stay testable and previewable offline.</para>
    /// </summary>
    internal static class MapSymbolAtlas
    {
        internal const int Size = 128;
        private const int Samples = 4;

        private static readonly Sprite[] glyphs = new Sprite[Enum.GetValues(typeof(MapGlyph)).Length];
        private static Sprite airbase;

        internal static Sprite AirbaseMark => airbase != null ? airbase :
            (airbase = MakeSprite("AirbaseMark", RenderAirbaseMark()));

        internal static Sprite Glyph(MapGlyph glyph)
        {
            int index = (int)glyph;
            if (glyphs[index] == null) glyphs[index] = MakeSprite("Glyph" + glyph, RenderGlyph(glyph));
            return glyphs[index];
        }

        // -------------------------------------------------------------- raster

        /// <summary>RGBA32 pixels, row 0 at the bottom (Unity texture order).</summary>
        internal static Color32[] RenderGlyph(MapGlyph glyph)
        {
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float ink = 0f, alpha = 0f;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            float u = ((x + (sx + .5f) / Samples) / Size) * 2f - 1f;
                            float v = ((y + (sy + .5f) / Samples) / Size) * 2f - 1f;
                            float d = GlyphDistance(glyph, u, v);
                            if (d < 0f) { ink += 1f; alpha += 1f; }
                            else if (d < Keyline) alpha += KeylineAlpha;
                        }
                    }
                    pixels[y * Size + x] = Resolve(ink, ink, ink, alpha);
                }
            }
            return pixels;
        }

        // The symbols stand on the map with no frame, so each stroke carries its own dark edge:
        // black stays black under the side tint and lifts the line off bright terrain.
        private const float Keyline = .09f;
        private const float KeylineAlpha = .8f;

        /// <summary>Runway and threshold inside a dark cut-corner landing-zone frame, legible at 32 pixels.</summary>
        internal static Color32[] RenderAirbaseMark()
        {
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float ink = 0f, alpha = 0f;
                for (int sy = 0; sy < Samples; sy++)
                for (int sx = 0; sx < Samples; sx++)
                {
                    float u = ((x + (sx + .5f) / Samples) / Size) * 2f - 1f;
                    float v = ((y + (sy + .5f) / Samples) / Size) * 2f - 1f;
                    float edge = Polygon(u, v, FriendlyFrame);
                    if (edge > 0f) continue;
                    float runway = Box(u, v, 0f, 0f, .23f, .64f, .03f);
                    bool threshold = Mathf.Abs(v) > .43f && Mathf.Abs(v) < .52f && Mathf.Abs(u) < .31f;
                    bool centerline = Mathf.Abs(u) < .045f && Mathf.Abs(v) < .32f &&
                        Mathf.Repeat(v + .34f, .22f) < .1f;
                    float brightness = edge > -.08f || (runway > -.06f && runway < .04f) ||
                        threshold || centerline ? 1f : .08f;
                    float opacity = brightness > .5f ? .9f : .42f;
                    ink += brightness * opacity;
                    alpha += opacity;
                }
                pixels[y * Size + x] = Resolve(ink, ink, ink, alpha);
            }
            return pixels;
        }

        private static Color32 Resolve(float r, float g, float b, float a)
        {
            float n = Samples * Samples;
            if (a <= 0f) return new Color32(0, 0, 0, 0);
            // Premultiplied sums back to straight colour so mip levels do not bleed.
            return new Color32((byte)Mathf.RoundToInt(r / a * 255f), (byte)Mathf.RoundToInt(g / a * 255f),
                (byte)Mathf.RoundToInt(b / a * 255f), (byte)Mathf.RoundToInt(a / n * 255f));
        }

        // The airbase mark's frame: 45-degree cuts like the avionics kit's chamfered panels. Clockwise.
        private static readonly float[] FriendlyFrame =
            { -.82f, .82f, .5f, .82f, .82f, .5f, .82f, -.82f, -.5f, -.82f, -.82f, -.5f };
        // Glyphs are line art on a 24-unit grid, y down, drawn with one 2-unit stroke: straight runs,
        // mitred corners and 45-degree cuts. A path that ends where it starts is a closed outline.
        private const float Grid = 12f;
        private const float StrokeHalf = 1f;

        private static readonly float[][][] Strokes =
        {
            // Armor: tank in profile - chamfered hull, turret, long gun.
            new[]
            {
                new[] { 3f, 13.5f, 21f, 13.5f, 18.5f, 18.5f, 5.5f, 18.5f, 3f, 13.5f },
                new[] { 8f, 13.5f, 8f, 9.5f, 14.5f, 9.5f, 14.5f, 13.5f },
                new[] { 14.5f, 11.5f, 21.5f, 11.5f }
            },
            // Light: wheeled scout car - raked cab on a low body, two wheels.
            new[]
            {
                new[] { 3f, 15.5f, 3f, 12f, 8f, 12f, 10f, 8f, 16f, 8f, 18f, 12f, 21f, 12f, 21f, 15.5f, 3f, 15.5f },
                new[] { 7.5f, 16f, 9.5f, 18f, 7.5f, 20f, 5.5f, 18f, 7.5f, 16f },
                new[] { 16.5f, 16f, 18.5f, 18f, 16.5f, 20f, 14.5f, 18f, 16.5f, 16f }
            },
            // Supply: cargo truck - box body, cut-nose cab, two wheels.
            new[]
            {
                new[] { 3f, 8f, 13.5f, 8f, 13.5f, 16f, 3f, 16f, 3f, 8f },
                new[] { 13.5f, 11f, 18.5f, 11f, 21f, 13.5f, 21f, 16f, 13.5f, 16f },
                new[] { 7f, 16.5f, 9f, 18.5f, 7f, 20.5f, 5f, 18.5f, 7f, 16.5f },
                new[] { 17f, 16.5f, 19f, 18.5f, 17f, 20.5f, 15f, 18.5f, 17f, 16.5f }
            },
            // Artillery: low hull, breech block, barrel raised to the upper right.
            new[]
            {
                new[] { 4f, 15f, 18f, 15f, 16f, 19f, 6f, 19f, 4f, 15f },
                new[] { 7f, 15f, 7f, 11.5f, 11f, 11.5f },
                new[] { 9.5f, 14f, 18f, 5.5f }
            },
            // AntiAir: gun carriage with a pair of raised barrels.
            new[]
            {
                new[] { 5f, 16f, 19f, 16f, 17f, 19.5f, 7f, 19.5f, 5f, 16f },
                new[] { 9f, 16f, 12.5f, 5f },
                new[] { 13.5f, 16f, 17f, 5f }
            },
            // Sam: missile on its rail, arrowhead up and right, over a launcher base.
            new[]
            {
                new[] { 4f, 19.5f, 20f, 19.5f },
                new[] { 10f, 19.5f, 10f, 14f },
                new[] { 6.5f, 17.5f, 17f, 7f },
                new[] { 11.5f, 6.5f, 17.5f, 6.5f, 17.5f, 12.5f }
            },
            // Radar: angled dish with its feed, on a mast and foot.
            new[]
            {
                new[] { 5f, 5.5f, 5f, 10f, 12f, 17f, 16.5f, 17f },
                new[] { 10f, 12f, 17f, 5f },
                new[] { 12f, 17f, 12f, 20.5f },
                new[] { 7.5f, 20.5f, 16.5f, 20.5f }
            },
            // Helicopter, top view: faceted fuselage, crossed rotor, tail bar.
            new[]
            {
                new[] { 12f, 6f, 14f, 9f, 14f, 13.5f, 12f, 19.5f, 10f, 13.5f, 10f, 9f, 12f, 6f },
                new[] { 5.5f, 4.5f, 18.5f, 16.5f },
                new[] { 18.5f, 4.5f, 5.5f, 16.5f },
                new[] { 9.5f, 20.5f, 14.5f, 20.5f }
            },
            // Aircraft, top view: stand-in delta with a notched tail.
            new[]
            {
                new[]
                {
                    12f, 2.5f, 14f, 10f, 20.5f, 17.5f, 14.5f, 17.5f, 15.5f, 21f, 8.5f, 21f,
                    9.5f, 17.5f, 3.5f, 17.5f, 10f, 10f, 12f, 2.5f
                }
            },
            // Contact: unclassified - an octagon around a centre pip.
            new[]
            {
                new[] { 9f, 4f, 15f, 4f, 20f, 9f, 20f, 15f, 15f, 20f, 9f, 20f, 4f, 15f, 4f, 9f, 9f, 4f },
                new[] { 11.5f, 12f, 12.5f, 12f }
            },
            // Hull, top view: pointed bow, cut stern, superstructure block.
            new[]
            {
                new[] { 12f, 2.5f, 16f, 8f, 16f, 19f, 14f, 21.5f, 10f, 21.5f, 8f, 19f, 8f, 8f, 12f, 2.5f },
                new[] { 10.5f, 11f, 13.5f, 11f, 13.5f, 16f, 10.5f, 16f, 10.5f, 11f }
            }
        };

        private static float GlyphDistance(MapGlyph glyph, float x, float y)
        {
            float gx = Grid + x * Grid, gy = Grid - y * Grid;
            float nearest = float.MaxValue;
            foreach (float[] path in Strokes[(int)glyph])
                for (int i = 0; i + 3 < path.Length; i += 2)
                    nearest = Math.Min(nearest, Segment(gx, gy, path[i], path[i + 1], path[i + 2], path[i + 3], StrokeHalf));
            return nearest / Grid;
        }

        /// <summary>Inside test for a convex clockwise outline: negative inside, positive outside.
        /// Corners stay sharp at any offset, so an outline band drawn from it is mitred.</summary>
        private static float Polygon(float x, float y, float[] outline)
        {
            float worst = float.MinValue;
            int n = outline.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float ex = outline[j * 2] - outline[i * 2], ey = outline[j * 2 + 1] - outline[i * 2 + 1];
                float side = (ex * (y - outline[i * 2 + 1]) - ey * (x - outline[i * 2])) / Length(ex, ey);
                worst = Math.Max(worst, side);
            }
            return worst;
        }

        // ---------------------------------------------------- distance helpers

        private static float Length(float x, float y) => Mathf.Sqrt(x * x + y * y);

        private static float Box(float x, float y, float cx, float cy, float hx, float hy, float radius)
        {
            float qx = Math.Abs(x - cx) - hx + radius;
            float qy = Math.Abs(y - cy) - hy + radius;
            float ox = Math.Max(qx, 0f), oy = Math.Max(qy, 0f);
            return Length(ox, oy) + Math.Min(Math.Max(qx, qy), 0f) - radius;
        }

        private static float Segment(float x, float y, float ax, float ay, float bx, float by, float halfWidth)
        {
            float pax = x - ax, pay = y - ay, bax = bx - ax, bay = by - ay;
            float h = Mathf.Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
            return Length(pax - bax * h, pay - bay * h) - halfWidth;
        }

        // ----------------------------------------------------------- sprites

        private static Sprite MakeSprite(string name, Color32[] pixels)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = "NOAvionics.MapSymbol." + name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                // Symbols draw at a fifth of the sheet: lean toward the sharper mip level.
                mipMapBias = -.6f,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(.5f, .5f),
                Size, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
