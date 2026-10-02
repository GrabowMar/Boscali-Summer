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
    /// <para>A frame ("plate") sits behind every glyph: a dark translucent fill that lifts the
    /// symbol off busy terrain, a bright tintable outline whose shape marks allegiance (rounded
    /// square friendly, diamond hostile, circle unknown, the APP-6 convention the game's own HUD
    /// symbols already follow) and a black keyline outside the outline. Glyphs are pure white so
    /// the game's own friendly/hostile/selected tint carries through untouched. Rasterising is
    /// separated from sprite creation so the shapes stay testable and previewable offline.</para>
    /// </summary>
    internal static class MapSymbolAtlas
    {
        internal const int Size = 64;
        private const int Samples = 4;

        private static readonly Sprite[] plates = new Sprite[3];
        private static readonly Sprite[] glyphs = new Sprite[Enum.GetValues(typeof(MapGlyph)).Length];
        private static Sprite airbase;

        internal static Sprite AirbaseMark => airbase != null ? airbase :
            (airbase = MakeSprite("AirbaseMark", RenderAirbaseMark()));

        internal static Sprite Plate(MapSide side)
        {
            int index = (int)side;
            if (plates[index] == null) plates[index] = MakeSprite("Plate" + side, RenderPlate(side));
            return plates[index];
        }

        internal static Sprite Glyph(MapGlyph glyph)
        {
            int index = (int)glyph;
            if (glyphs[index] == null) glyphs[index] = MakeSprite("Glyph" + glyph, RenderGlyph(glyph));
            return glyphs[index];
        }

        // -------------------------------------------------------------- raster

        /// <summary>RGBA32 pixels, row 0 at the bottom (Unity texture order).</summary>
        internal static Color32[] RenderPlate(MapSide side)
        {
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            float u = ((x + (sx + .5f) / Samples) / Size) * 2f - 1f;
                            float v = ((y + (sy + .5f) / Samples) / Size) * 2f - 1f;
                            float d = PlateDistance(side, u, v);
                            float pr, pa;
                            if (d > .055f) continue;
                            if (d > 0f) { pr = 0f; pa = .55f; }               // keyline
                            else if (d > -.07f) { pr = 1f; pa = .94f; }       // fine outline
                            else { pr = .08f; pa = .38f; }                    // translucent fill
                            r += pr * pa; g += pr * pa; b += pr * pa; a += pa;
                        }
                    }
                    pixels[y * Size + x] = Resolve(r, g, b, a);
                }
            }
            return pixels;
        }

        internal static Color32[] RenderGlyph(MapGlyph glyph)
        {
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            float u = ((x + (sx + .5f) / Samples) / Size) * 2f - 1f;
                            float v = ((y + (sy + .5f) / Samples) / Size) * 2f - 1f;
                            if (GlyphDistance(glyph, u, v) < 0f) inside++;
                        }
                    }
                    float coverage = inside / (float)(Samples * Samples);
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255f));
                }
            }
            return pixels;
        }

        /// <summary>Runway and threshold inside a dark landing-zone ring, legible at 32 pixels.</summary>
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
                    float edge = Box(u, v, 0f, 0f, .82f, .82f, .14f);
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

        /// <summary>Signed distance to the outer edge of the outline; negative inside.</summary>
        private static float PlateDistance(MapSide side, float x, float y)
        {
            switch (side)
            {
                case MapSide.Hostile: return (Mathf.Abs(x) + Mathf.Abs(y) - .92f) * .70710678f;
                case MapSide.Neutral: return Mathf.Sqrt(x * x + y * y) - .86f;
                default: return Box(x, y, 0f, 0f, .82f, .82f, .2f);
            }
        }

        // Glyphs live in a [-1, 1] square. Content stays inside the inner diamond |x| + |y| <= 1.4 of the
        // glyph square so it still clears the hostile diamond frame (the glyph fills ~67% of the plate).
        private static float GlyphDistance(MapGlyph glyph, float x, float y)
        {
            switch (glyph)
            {
                case MapGlyph.Armor:
                {
                    // Main battle tank in profile: track hull, turret, long gun.
                    float hull = Box(x, y, 0f, -.42f, .86f, .2f, .1f);
                    float turret = Box(x, y, -.12f, -.04f, .42f, .19f, .1f);
                    float gun = Segment(x, y, .25f, -.02f, .84f, -.02f, .075f);
                    return Math.Min(hull, Math.Min(turret, gun));
                }
                case MapGlyph.Light:
                {
                    // Wheeled light vehicle in profile: low body, cab with a dark window, two wheels.
                    float body = Box(x, y, 0f, -.2f, .85f, .2f, .12f);
                    float cab = Box(x, y, -.05f, .2f, .46f, .2f, .12f);
                    float window = Box(x, y, -.05f, .22f, .3f, .09f, .03f);
                    float wheels = Math.Min(Circle(x, y, -.5f, -.45f, .2f), Circle(x, y, .5f, -.45f, .2f));
                    return Math.Min(Math.Max(Math.Min(body, cab), -window), wheels);
                }
                case MapGlyph.Supply:
                {
                    // Cargo truck in profile: box body, cab, two wheels.
                    float cargo = Box(x, y, -.35f, .0f, .5f, .4f, .06f);
                    float cab = Box(x, y, .62f, -.12f, .25f, .28f, .08f);
                    float wheels = Math.Min(Circle(x, y, -.5f, -.5f, .2f), Circle(x, y, .56f, -.5f, .2f));
                    return Math.Min(Math.Min(cargo, cab), wheels);
                }
                case MapGlyph.Artillery:
                {
                    // Self-propelled gun: low hull, turret, barrel raised to the upper right.
                    float hull = Box(x, y, -.1f, -.55f, .72f, .17f, .1f);
                    float turret = Box(x, y, -.15f, -.22f, .36f, .17f, .08f);
                    float barrel = Segment(x, y, -.05f, -.15f, .72f, .6f, .095f);
                    return Math.Min(hull, Math.Min(turret, barrel));
                }
                case MapGlyph.AntiAir:
                {
                    // Gun carriage with a pair of raised barrels.
                    float baseBox = Box(x, y, 0f, -.6f, .72f, .16f, .08f);
                    float mount = Box(x, y, -.05f, -.32f, .3f, .16f, .05f);
                    float guns = Math.Min(Segment(x, y, -.12f, -.25f, .3f, .72f, .085f),
                        Segment(x, y, .12f, -.25f, .54f, .72f, .085f));
                    return Math.Min(baseBox, Math.Min(mount, guns));
                }
                case MapGlyph.Sam:
                {
                    // Dome over a base line with one upright missile: the APP-6 air-defence mark.
                    float dome = Math.Max(Math.Abs(Length(x, y + .45f) - .8f) - .13f, -(y + .45f));
                    float baseLine = Box(x, y, 0f, -.5f, .88f, .08f, .02f);
                    float body = Box(x, y, 0f, .05f, .17f, .5f, .17f);
                    float nose = Triangle(x, y, -.17f, .5f, .17f, .5f, 0f, .9f);
                    return Math.Min(Math.Min(dome, baseLine), Math.Min(body, nose));
                }
                case MapGlyph.Radar:
                {
                    // Dish on a mast, feed horn out to the upper right.
                    float bowl = Math.Max(Circle(x, y, -.05f, .05f, .56f), (x + y - .05f) * .7071f);
                    float feed = Segment(x, y, -.05f, .05f, .5f, .5f, .055f);
                    float horn = Circle(x, y, .56f, .56f, .14f);
                    float mast = Segment(x, y, -.2f, -.72f, -.2f, -.15f, .075f);
                    float floor = Box(x, y, -.2f, -.78f, .5f, .11f, .05f);
                    return Math.Min(Math.Min(bowl, feed), Math.Min(horn, Math.Min(mast, floor)));
                }
                case MapGlyph.Helicopter:
                {
                    // Top view: fuselage capsule, tail boom, tail rotor and a crossed main rotor.
                    float body = Box(x, y, 0f, .08f, .15f, .4f, .15f);
                    float boom = Segment(x, y, 0f, -.3f, 0f, -.8f, .06f);
                    float tail = Segment(x, y, -.17f, -.8f, .17f, -.8f, .05f);
                    float rotorA = Segment(x, y, -.6f, -.55f, .6f, .7f, .07f);
                    float rotorB = Segment(x, y, -.6f, .7f, .6f, -.55f, .07f);
                    float hub = Circle(x, y, 0f, .08f, .17f);
                    return Math.Min(Math.Min(body, boom), Math.Min(Math.Min(tail, hub), Math.Min(rotorA, rotorB)));
                }
                case MapGlyph.Aircraft:
                {
                    float fuselage = Box(x, y, 0f, 0f, .11f, .9f, .11f);
                    float wing = Triangle(x, y, -.9f, -.32f, .9f, -.32f, 0f, .32f);
                    float tail = Triangle(x, y, -.42f, -.9f, .42f, -.9f, 0f, -.5f);
                    return Math.Min(fuselage, Math.Min(wing, tail));
                }
                case MapGlyph.Contact:
                    return Math.Min(Math.Abs(Length(x, y) - .58f) - .075f,
                        Circle(x, y, 0f, 0f, .13f));
                default:
                {
                    // Ship: pointed bow, full beam amidships, square stern, dark superstructure notch.
                    float hull = Polygon(x, y, HullOutline);
                    float deck = Box(x, y, 0f, -.18f, .12f, .22f, .03f);
                    return Math.Max(hull, -deck);
                }
            }
        }

        private static readonly float[] HullOutline =
            { 0f, .92f, .3f, .45f, .36f, -.25f, .3f, -.88f, -.3f, -.88f, -.36f, -.25f, -.3f, .45f };

        /// <summary>Inside test for a convex clockwise outline: negative inside, positive outside.</summary>
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

        private static float Circle(float x, float y, float cx, float cy, float radius) =>
            Length(x - cx, y - cy) - radius;

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

        /// <summary>A quarter ring centred on (cx, cy) that opens to the upper right.</summary>
        private static float ArcBand(float x, float y, float cx, float cy, float radius, float halfWidth)
        {
            float dx = x - cx, dy = y - cy;
            float band = Math.Abs(Length(dx, dy) - radius) - halfWidth;
            // Clip to the upper-right quadrant; the ring ends are cut square.
            float clip = Math.Max(-dx, -dy);
            return Math.Max(band, clip);
        }

        private static float Triangle(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d = Math.Min(Edge(x, y, ax, ay, bx, by), Math.Min(Edge(x, y, bx, by, cx, cy), Edge(x, y, cx, cy, ax, ay)));
            float cross1 = (bx - ax) * (y - ay) - (by - ay) * (x - ax);
            float cross2 = (cx - bx) * (y - by) - (cy - by) * (x - bx);
            float cross3 = (ax - cx) * (y - cy) - (ay - cy) * (x - cx);
            bool inside = (cross1 >= 0f && cross2 >= 0f && cross3 >= 0f) || (cross1 <= 0f && cross2 <= 0f && cross3 <= 0f);
            return inside ? -d : d;
        }

        private static float Edge(float x, float y, float ax, float ay, float bx, float by) =>
            Segment(x, y, ax, ay, bx, by, 0f);

        // ----------------------------------------------------------- sprites

        private static Sprite MakeSprite(string name, Color32[] pixels)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = "NOAvionics.MapSymbol." + name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
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
