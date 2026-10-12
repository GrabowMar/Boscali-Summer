using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>A contact's platform class, as the map legend names it.</summary>
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
        /// <summary>Rotorcraft.</summary>
        Helicopter,
        /// <summary>Fixed-wing aircraft.</summary>
        Aircraft,
        /// <summary>A fixed site or emplacement.</summary>
        Contact,
        /// <summary>Surface ship.</summary>
        Hull
    }

    /// <summary>Which side a contact is on; the map marks it with colour.</summary>
    internal enum MapSide { Friendly, Hostile, Neutral }

    /// <summary>
    /// One representative map icon per platform class, for the legend and for vehicles the baked sheet
    /// does not know, plus the airbase mark.
    ///
    /// <para>The map's icons are the game's own plan-view silhouettes wherever the game has one
    /// (aircraft, helicopters, ships, emplacements) and <see cref="MapVehicleIcons"/> for ground
    /// vehicles, which the game draws as one capsule. Nothing here is drawn by hand: a class stands in
    /// as one typical member of it.</para>
    /// </summary>
    internal static class MapSymbolAtlas
    {
        private const int Size = 128;
        private const int Samples = 4;

        private static readonly Dictionary<string, Sprite> vanilla =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static bool vanillaScanned;
        private static Sprite airbase;

        internal static Sprite AirbaseMark => airbase != null ? airbase :
            (airbase = MakeSprite("AirbaseMark", RenderAirbaseMark()));

        /// <summary>A typical icon of the class; null when neither the sheet nor the game has it loaded.</summary>
        internal static Sprite Glyph(MapGlyph glyph)
        {
            switch (glyph)
            {
                case MapGlyph.Armor: return MapVehicleIcons.For("MBT1");
                case MapGlyph.Light: return MapVehicleIcons.For("LCV45");
                case MapGlyph.Supply: return MapVehicleIcons.For("Truck2-L");
                case MapGlyph.Artillery: return MapVehicleIcons.For("HLT-MArt");
                case MapGlyph.AntiAir: return MapVehicleIcons.For("SPAAG1");
                case MapGlyph.Sam: return MapVehicleIcons.For("Linebreaker_SAM");
                case MapGlyph.Radar: return MapVehicleIcons.For("RadarSAM1");
                case MapGlyph.Helicopter: return Vanilla("mapIcon_attackHelo1");
                case MapGlyph.Aircraft: return Vanilla("mapIcon_multirole1");
                case MapGlyph.Hull: return Vanilla("frigate1_mapIcon");
                default: return Vanilla("mapIcon_emplacement1");
            }
        }

        /// <summary>One of the game's own map icons by sprite name. The loaded sprites are scanned once.</summary>
        private static Sprite Vanilla(string name)
        {
            if (!vanillaScanned)
            {
                vanillaScanned = true;
                foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (sprite != null && sprite.name.IndexOf("mapIcon", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        !vanilla.ContainsKey(sprite.name)) vanilla.Add(sprite.name, sprite);
            }
            return vanilla.TryGetValue(name, out Sprite found) ? found : null;
        }

        // -------------------------------------------------------------- raster

        /// <summary>Runway and threshold inside a dark cut-corner landing-zone frame, legible at 32 pixels.
        /// RGBA32 pixels, row 0 at the bottom (Unity texture order).</summary>
        private static Color32[] RenderAirbaseMark()
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
                    float edge = Polygon(u, v, Frame);
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
        private static readonly float[] Frame =
            { -.82f, .82f, .5f, .82f, .82f, .5f, .82f, -.82f, -.5f, -.82f, -.82f, -.5f };

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

        private static float Length(float x, float y) => Mathf.Sqrt(x * x + y * y);

        private static float Box(float x, float y, float cx, float cy, float hx, float hy, float radius)
        {
            float qx = Math.Abs(x - cx) - hx + radius;
            float qy = Math.Abs(y - cy) - hy + radius;
            float ox = Math.Max(qx, 0f), oy = Math.Max(qy, 0f);
            return Length(ox, oy) + Math.Min(Math.Max(qx, qy), 0f) - radius;
        }

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
