using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation.Window
{
    /// <summary>
    /// Procedural sprites for the OPS window and its rooms. Created once, cached, never written to
    /// disk. Every texture is white with alpha so the room tints it with its own stylesheet ink.
    /// The ring, disc and pulse sprites already owned by <see cref="SupportTacticalIcons"/> are
    /// reused rather than drawn twice.
    /// </summary>
    internal static class OpsSprites
    {
        public const int GlyphSize = 32;
        private const int GlyphColumns = 8;

        /// <summary>Glyph atlas slots. Station modules use their <c>ModuleKind</c> value (0 = empty port).</summary>
        public static class G
        {
            public const int Port = 0;
            public const int NodeCommand = 16, NodeBase = 17, NodeAirfield = 18, NodeCity = 19;
            public const int ObjAirfield = 20, ObjOutpost = 21, ObjTown = 22, ObjAirDefence = 23;
            public const int Space = 24, Cyber = 25, SpecOps = 26;
            public const int RadarScan = 27, Elint = 28, Rod = 29, Emp = 30, Uplink = 31;
            public const int Ping = 32, Track = 33, Blackout = 34, Ghost = 35, Spoof = 36;
            public const int Spot = 37, Suppress = 38, Fortify = 39;
            public const int Alert = 40, Pin = 41, Team = 42, Lock = 43, Check = 44, Flare = 45;
            public const int Count = 46;
        }

        // Root.
        public static Sprite Vignette { get; private set; }
        public static Sprite Shadow { get; private set; }
        public static Sprite Notch { get; private set; }
        // SPACE.
        public static Sprite Blueprint { get; private set; }
        public static Sprite Stars { get; private set; }
        public static Sprite Limb { get; private set; }
        public static Sprite Guard { get; private set; }
        // CYBER.
        public static Sprite Lattice { get; private set; }
        public static Sprite Cursor { get; private set; }
        public static Sprite HexFill { get; private set; }
        public static Sprite HexLine { get; private set; }
        // SPEC OPS.
        public static Sprite Contours { get; private set; }
        public static Sprite Paper { get; private set; }
        public static Sprite Stamp { get; private set; }
        public static Sprite Folder { get; private set; }
        public static Sprite DogTag { get; private set; }
        public static Sprite Clip { get; private set; }
        // IMAGER.
        public static Sprite Reticle { get; private set; }
        public static Sprite Brackets { get; private set; }
        public static Sprite Scan { get; private set; }
        // Shared shapes.
        public static Sprite Dash { get; private set; }
        public static Sprite Dot { get; private set; }
        public static Sprite Diamond { get; private set; }
        public static Sprite Triangle { get; private set; }
        public static Sprite Chevron { get; private set; }
        public static Sprite Arc { get; private set; }
        public static Sprite Soft { get; private set; }
        public static Sprite Atlas { get; private set; }

        public static int Bytes { get; private set; }
        public static int TextureCount => textures.Count;

        private static readonly List<Texture2D> textures = new List<Texture2D>(40);
        private static readonly Sprite[] glyphs = new Sprite[G.Count];
        private static bool ready;

        public static void Ensure()
        {
            if (ready) return;
            ready = true;
            Vignette = Save(VignetteTexture(256), 0);
            Shadow = Save(ShadowTexture(128, 48), 48);
            Notch = Save(NotchTexture(48, 26, 7), 10);
            Blueprint = Save(Grid(64, 16), 0, true);
            Stars = Save(Specks(256, 90), 0, true);
            Limb = Save(LimbTexture(256, 96), 0);
            Guard = Save(Hatch(32, 8), 0, true);
            Lattice = Save(HexLattice(48, 28, 16f), 0, true);
            Cursor = Save(Solid(8, 16), 0);
            HexFill = Save(Hexagon(64, true), 0);
            HexLine = Save(Hexagon(64, false), 0);
            Contours = Save(ContourTexture(256), 0, true);
            Paper = Save(Grain(128), 0, true);
            Stamp = Save(RoundFrame(64, 3, 6), 12);
            Folder = Save(FolderTab(64, 48, 22), new Vector4(6f, 0f, 30f, 0f));
            DogTag = Save(RoundFill(64, 32, 10), 12);
            Clip = Save(ClipTexture(16, 40), 0);
            Reticle = Save(ReticleTexture(128), 0);
            Brackets = Save(BracketTexture(48, 14, 2), 16);
            Scan = Save(ScanTexture(4, 64), 0);
            Dash = Save(DashTexture(12, 4, 7), 0, true);
            Dot = Save(Disc(32), 0);
            Diamond = Save(DiamondTexture(32), 0);
            Triangle = Save(TriangleTexture(32), 0);
            Chevron = Save(ChevronTexture(32), 0);
            Arc = Save(ArcTexture(64, 5f), 0);
            Soft = Save(SoftDot(32), 0);
            Texture2D atlas = GlyphAtlas();
            Atlas = Save(atlas, 0);
            for (int i = 0; i < glyphs.Length; i++)
            {
                var rect = new Rect((i % GlyphColumns) * GlyphSize, atlas.height - (i / GlyphColumns + 1) * GlyphSize,
                    GlyphSize, GlyphSize);
                glyphs[i] = Sprite.Create(atlas, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                glyphs[i].hideFlags = HideFlags.HideAndDontSave;
            }
            SupportTacticalIcons.EnsureInitialized();
        }

        /// <summary>A cached glyph sprite; out-of-range indices return the empty port.</summary>
        public static Sprite Glyph(int index)
        {
            Ensure();
            return glyphs[index >= 0 && index < glyphs.Length ? index : 0];
        }

        public static Sprite Ring => SupportTacticalIcons.RingSprite;
        public static Sprite DottedRing => SupportTacticalIcons.DottedRingSprite;
        public static Sprite Disc32 => SupportTacticalIcons.CoverageDiscSprite;
        public static Sprite Pulse => SupportTacticalIcons.DataPulseSprite;
        public static Sprite Sweep => SupportTacticalIcons.SweepWedgeSprite;
        public static Sprite RadialFill => SupportTacticalIcons.RadialFillSprite;

        // ---- Plumbing -------------------------------------------------------------------------

        private static Sprite Save(Texture2D texture, int border, bool repeat = false) =>
            Save(texture, new Vector4(border, border, border, border), repeat);

        private static Sprite Save(Texture2D texture, Vector4 border, bool repeat = false)
        {
            texture.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.Apply(false, true);
            Bytes += texture.width * texture.height * 4;
            textures.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Texture2D Make(int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };

        private static Texture2D Fill(int w, int h, System.Func<int, int, float> alpha)
        {
            Texture2D texture = Make(w, h);
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x, y)) * 255f));
            texture.SetPixels32(pixels);
            return texture;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263 + seed * 144269504;
                n = (n ^ (n >> 13)) * 1274126177;
                n ^= n >> 16;
                return (n & 0xFFFF) / 65535f;
            }
        }

        // ---- Root -----------------------------------------------------------------------------

        private static Texture2D VignetteTexture(int size)
        {
            float half = (size - 1) * 0.5f;
            return Fill(size, size, (x, y) =>
            {
                float dx = (x - half) / half, dy = (y - half) / half;
                return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - 0.25f) / 1.1f));
            });
        }

        /// <summary>A box falloff: opaque inside the border, fading to nothing across it.</summary>
        private static Texture2D ShadowTexture(int size, int border)
        {
            float lo = border, hi = size - border;
            return Fill(size, size, (x, y) =>
            {
                float dx = Mathf.Max(0f, Mathf.Max(lo - (x + 0.5f), (x + 0.5f) - hi));
                float dy = Mathf.Max(0f, Mathf.Max(lo - (y + 0.5f), (y + 0.5f) - hi));
                float d = Mathf.Sqrt(dx * dx + dy * dy) / border;
                float a = Mathf.Clamp01(1f - d);
                return a * a * (3f - 2f * a);
            });
        }

        private static Texture2D NotchTexture(int w, int h, int cut) =>
            Fill(w, h, (x, y) =>
            {
                int top = h - 1 - y;
                bool cutLeft = x + top < cut;
                bool cutRight = (w - 1 - x) + top < cut;
                return cutLeft || cutRight ? 0f : 1f;
            });

        // ---- SPACE ----------------------------------------------------------------------------

        private static Texture2D Grid(int size, int minor) =>
            Fill(size, size, (x, y) =>
            {
                if (x == 0 || y == 0) return 0.55f;
                if (x % minor == 0 || y % minor == 0) return 0.22f;
                return 0f;
            });

        private static Texture2D Specks(int size, int count)
        {
            Texture2D texture = Make(size, size);
            var pixels = new Color32[size * size];
            for (int i = 0; i < count; i++)
            {
                int x = Mathf.FloorToInt(Hash(i, 3, 11) * size);
                int y = Mathf.FloorToInt(Hash(i, 7, 13) * size);
                float bright = 0.25f + 0.75f * Hash(i, 1, 17) * Hash(i, 2, 19);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(bright * 255f));
                if (bright > 0.7f && x + 1 < size) pixels[y * size + x + 1] = new Color32(255, 255, 255, (byte)(bright * 90f));
            }
            texture.SetPixels32(pixels);
            return texture;
        }

        /// <summary>The Earth limb: a wide elliptical glow rising from the bottom edge.</summary>
        private static Texture2D LimbTexture(int w, int h) =>
            Fill(w, h, (x, y) =>
            {
                float u = (x - (w - 1) * 0.5f) / ((w - 1) * 0.5f);
                float curve = 0.18f + 0.55f * u * u;
                float v = y / (float)(h - 1);
                float edge = v - curve;
                if (edge > 0f) return Mathf.Clamp01(0.55f * Mathf.Exp(-edge * 9f));
                return Mathf.Clamp01(0.85f + edge * 2f);
            });

        private static Texture2D Hatch(int size, int pitch) =>
            Fill(size, size, (x, y) => ((x + y) % pitch) < pitch / 2 ? 0.9f : 0.15f);

        // ---- CYBER ----------------------------------------------------------------------------

        /// <summary>A tileable flat-top hex lattice drawn as the Voronoi edges of its centres.</summary>
        private static Texture2D HexLattice(int w, int h, float side)
        {
            var cx = new[] { 0f, w, 0f, w, w * 0.5f, w * 0.5f, w * 0.5f };
            var cy = new[] { 0f, 0f, h, h, h * 0.5f, -h * 0.5f, h * 1.5f };
            return Fill(w, h, (x, y) =>
            {
                float px = x + 0.5f, py = y + 0.5f;
                float d1 = float.MaxValue, d2 = float.MaxValue;
                for (int i = 0; i < cx.Length; i++)
                {
                    float d = Mathf.Sqrt((px - cx[i]) * (px - cx[i]) + (py - cy[i]) * (py - cy[i]));
                    if (d < d1) { d2 = d1; d1 = d; }
                    else if (d < d2) d2 = d;
                }
                return Mathf.Clamp01(1f - (d2 - d1) * 0.9f);
            });
        }

        private static Texture2D Solid(int w, int h) => Fill(w, h, (x, y) => 1f);

        private static Texture2D Hexagon(int size, bool filled)
        {
            float c = (size - 1) * 0.5f;
            float r = size * 0.46f;
            return Fill(size, size, (x, y) =>
            {
                float px = Mathf.Abs(x - c), py = Mathf.Abs(y - c);
                // Pointy-top hexagon distance (inside when negative).
                float d = Mathf.Max(px * 0.8660254f + py * 0.5f, py) - r * 0.8660254f;
                if (filled) return Mathf.Clamp01(0.5f - d);
                return Mathf.Clamp01(1.6f - Mathf.Abs(d + 1.6f));
            });
        }

        // ---- SPEC OPS -------------------------------------------------------------------------

        private static float Noise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int ax = ((x0 % period) + period) % period, bx = (ax + 1) % period;
            int ay = ((y0 % period) + period) % period, by = (ay + 1) % period;
            float a = Hash(ax, ay, seed), b = Hash(bx, ay, seed), c = Hash(ax, by, seed), d = Hash(bx, by, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Tileable topographic contours: thin lines, every fifth one heavier.</summary>
        private static Texture2D ContourTexture(int size)
        {
            var height = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                height[y * size + x] = Noise(u * 4f, v * 4f, 4, 5) * 0.62f + Noise(u * 8f, v * 8f, 8, 9) * 0.28f +
                                       Noise(u * 16f, v * 16f, 16, 21) * 0.10f;
            }
            const float levels = 14f;
            return Fill(size, size, (x, y) =>
            {
                float h = height[y * size + x] * levels;
                float hx = height[y * size + (x + 1) % size] * levels - h;
                float hy = height[((y + 1) % size) * size + x] * levels - h;
                float slope = Mathf.Max(0.02f, Mathf.Sqrt(hx * hx + hy * hy));
                float f = h - Mathf.Floor(h);
                float d = Mathf.Min(f, 1f - f) / slope;
                bool index = Mathf.RoundToInt(h) % 5 == 0;
                float width = index ? 1.1f : 0.6f;
                return Mathf.Clamp01(width - d) * (index ? 0.9f : 0.55f);
            });
        }

        private static Texture2D Grain(int size) =>
            Fill(size, size, (x, y) =>
            {
                float fibre = Noise(x * 0.5f, y * 0.06f, 64, 31) * 0.5f;
                return 0.25f + 0.45f * Hash(x, y, 7) * (0.5f + fibre);
            });

        private static Texture2D RoundFrame(int size, int thickness, int radius) =>
            Fill(size, size, (x, y) =>
            {
                float d = RoundRect(x + 0.5f, y + 0.5f, 0f, 0f, size, size, radius);
                return Mathf.Clamp01(1f - Mathf.Abs(d + thickness * 0.5f) + thickness * 0.5f - 0.5f);
            });

        private static Texture2D RoundFill(int w, int h, int radius) =>
            Fill(w, h, (x, y) => Mathf.Clamp01(0.5f - RoundRect(x + 0.5f, y + 0.5f, 0f, 0f, w, h, radius)));

        /// <summary>Signed distance to a rounded rectangle (negative inside).</summary>
        private static float RoundRect(float px, float py, float x, float y, float w, float h, float r)
        {
            float cx = x + w * 0.5f, cy = y + h * 0.5f;
            float qx = Mathf.Abs(px - cx) - (w * 0.5f - r);
            float qy = Mathf.Abs(py - cy) - (h * 0.5f - r);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>A folder tab: square left, a slanted right shoulder.</summary>
        private static Texture2D FolderTab(int w, int h, int slant) =>
            Fill(w, h, (x, y) =>
            {
                float edge = w - slant * (y / (float)(h - 1));
                return Mathf.Clamp01(edge - x);
            });

        private static Texture2D ClipTexture(int w, int h) =>
            Fill(w, h, (x, y) =>
            {
                float outer = RoundRect(x + 0.5f, y + 0.5f, 1f, 1f, w - 2f, h - 2f, (w - 2f) * 0.5f);
                float inner = RoundRect(x + 0.5f, y + 0.5f, 4f, 6f, w - 8f, h - 10f, (w - 8f) * 0.5f);
                return Mathf.Max(Mathf.Clamp01(1.5f - Mathf.Abs(outer + 1f)), Mathf.Clamp01(1.5f - Mathf.Abs(inner + 1f)));
            });

        // ---- IMAGER ---------------------------------------------------------------------------

        private static Texture2D ReticleTexture(int size)
        {
            float c = (size - 1) * 0.5f;
            return Fill(size, size, (x, y) =>
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Clamp01(1.2f - Mathf.Abs(r - size * 0.30f));
                bool gap = r < size * 0.09f;
                bool armX = Mathf.Abs(dy) < 1.1f && Mathf.Abs(dx) > size * 0.09f && Mathf.Abs(dx) < size * 0.46f;
                bool armY = Mathf.Abs(dx) < 1.1f && Mathf.Abs(dy) > size * 0.09f && Mathf.Abs(dy) < size * 0.46f;
                float tick = 0f;
                for (int k = 1; k <= 3; k++)
                {
                    float at = size * 0.10f * k + size * 0.06f;
                    if (Mathf.Abs(Mathf.Abs(dx) - at) < 0.8f && Mathf.Abs(dy) < 4f) tick = 1f;
                    if (Mathf.Abs(Mathf.Abs(dy) - at) < 0.8f && Mathf.Abs(dx) < 4f) tick = 1f;
                }
                return gap ? 0f : Mathf.Max(ring, Mathf.Max(armX || armY ? 1f : 0f, tick));
            });
        }

        private static Texture2D BracketTexture(int size, int arm, int thickness) =>
            Fill(size, size, (x, y) =>
            {
                bool nearX = x < arm || x >= size - arm;
                bool nearY = y < arm || y >= size - arm;
                bool edgeX = x < thickness || x >= size - thickness;
                bool edgeY = y < thickness || y >= size - thickness;
                return nearX && nearY && (edgeX || edgeY) ? 1f : 0f;
            });

        private static Texture2D ScanTexture(int w, int h) =>
            Fill(w, h, (x, y) =>
            {
                float v = y / (float)(h - 1);
                return Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v - 0.5f) * 2f), 3f);
            });

        // ---- Shapes ---------------------------------------------------------------------------

        private static Texture2D DashTexture(int w, int h, int on) => Fill(w, h, (x, y) => x < on ? 1f : 0f);

        private static Texture2D Disc(int size)
        {
            float c = size * 0.5f;
            return Fill(size, size, (x, y) =>
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                return Mathf.Clamp01(c - 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
            });
        }

        private static Texture2D SoftDot(int size)
        {
            float c = size * 0.5f;
            return Fill(size, size, (x, y) =>
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / c);
                return t * t;
            });
        }

        private static Texture2D DiamondTexture(int size)
        {
            float c = size * 0.5f;
            return Fill(size, size, (x, y) => Mathf.Clamp01(c - 1f - (Mathf.Abs(x + 0.5f - c) + Mathf.Abs(y + 0.5f - c))));
        }

        private static Texture2D TriangleTexture(int size) =>
            Fill(size, size, (x, y) =>
            {
                float half = (size - 2) * 0.5f * (y / (float)(size - 1));
                float c = size * 0.5f;
                return y < 2 ? 0f : Mathf.Clamp01(half - Mathf.Abs(x + 0.5f - c) + 0.5f);
            });

        private static Texture2D ChevronTexture(int size) =>
            Fill(size, size, (x, y) =>
            {
                float c = size * 0.5f;
                float v = Mathf.Abs(x + 0.5f - c) * 0.9f + (size - 1 - y) * 0.5f;
                float d = Mathf.Abs(v - size * 0.45f);
                return Mathf.Clamp01(2.6f - d);
            });

        /// <summary>A ring with tick marks every 30 degrees, for dials.</summary>
        private static Texture2D ArcTexture(int size, float width)
        {
            float c = (size - 1) * 0.5f;
            float radius = c - width * 0.5f - 1f;
            return Fill(size, size, (x, y) =>
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                return Mathf.Clamp01(width * 0.5f + 0.5f - Mathf.Abs(r - radius));
            });
        }

        // ---- Glyph atlas: 32 px line icons, drawn once ------------------------------------------

        // Primitive codes: 0 line(x0,y0,x1,y1,w), 1 circle(cx,cy,r,w), 2 disc(cx,cy,r), 3 box(x,y,w,h,w), 4 fill(x,y,w,h)
        private static float Distance(float px, float py, float[] s, int at, out bool solid)
        {
            solid = false;
            switch ((int)s[at])
            {
                case 0:
                {
                    float x0 = s[at + 1], y0 = s[at + 2], x1 = s[at + 3], y1 = s[at + 4];
                    float dx = x1 - x0, dy = y1 - y0;
                    float t = Mathf.Clamp01(((px - x0) * dx + (py - y0) * dy) / Mathf.Max(0.0001f, dx * dx + dy * dy));
                    float ex = px - (x0 + dx * t), ey = py - (y0 + dy * t);
                    return Mathf.Sqrt(ex * ex + ey * ey) - s[at + 5] * 0.5f;
                }
                case 1:
                {
                    float dx = px - s[at + 1], dy = py - s[at + 2];
                    return Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - s[at + 3]) - s[at + 4] * 0.5f;
                }
                case 2:
                {
                    float dx = px - s[at + 1], dy = py - s[at + 2];
                    return Mathf.Sqrt(dx * dx + dy * dy) - s[at + 3];
                }
                case 3:
                {
                    float d = RoundRect(px, py, s[at + 1], s[at + 2], s[at + 3], s[at + 4], 0.01f);
                    return Mathf.Abs(d) - s[at + 5] * 0.5f;
                }
                default:
                    return RoundRect(px, py, s[at + 1], s[at + 2], s[at + 3], s[at + 4], 0.01f);
            }
        }

        private static int Size(int code) => code == 0 ? 6 : code == 1 ? 5 : code == 2 ? 4 : code == 3 ? 6 : 5;

        private static Texture2D GlyphAtlas()
        {
            int rows = (G.Count + GlyphColumns - 1) / GlyphColumns;
            Texture2D texture = Make(GlyphColumns * GlyphSize, rows * GlyphSize);
            var pixels = new Color32[texture.width * texture.height];
            for (int i = 0; i < G.Count; i++)
            {
                float[] shape = Shape(i);
                int ox = (i % GlyphColumns) * GlyphSize;
                int oy = texture.height - (i / GlyphColumns + 1) * GlyphSize;
                for (int y = 0; y < GlyphSize; y++)
                for (int x = 0; x < GlyphSize; x++)
                {
                    // Shapes are authored top-down; textures run bottom-up.
                    float px = x + 0.5f, py = GlyphSize - y - 0.5f;
                    float best = float.MaxValue;
                    for (int at = 0; at < shape.Length; at += Size((int)shape[at]))
                        best = Mathf.Min(best, Distance(px, py, shape, at, out _));
                    float a = Mathf.Clamp01(0.5f - best);
                    if (a <= 0f) continue;
                    pixels[(oy + y) * texture.width + ox + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            texture.SetPixels32(pixels);
            return texture;
        }

        private static float[] L(params float[] v) => v;

        /// <summary>Each glyph as primitives in a 32 px cell, y down. Stroke width 2 unless noted.</summary>
        private static float[] Shape(int glyph)
        {
            switch (glyph)
            {
                // Station modules (by ModuleKind value).
                case 1: return L(3, 9, 9, 14, 14, 2.5f, 2, 16, 16, 3, 0, 16, 4, 16, 9, 2, 0, 16, 23, 16, 28, 2, 0, 4, 16, 9, 16, 2, 0, 23, 16, 28, 16, 2);
                case 2: return L(3, 3, 11, 26, 10, 2, 0, 16, 11, 16, 21, 2, 0, 9.5f, 11, 9.5f, 21, 1, 0, 22.5f, 11, 22.5f, 21, 1, 0, 3, 16, 29, 16, 1);
                case 3: return L(3, 8, 9, 16, 16, 2, 4, 24, 13, 3, 6, 4, 11, 12, 6, 3, 4, 11, 19, 6, 3);
                case 4: return L(1, 16, 16, 11, 2, 2, 16, 16, 4, 1, 16, 16, 6, 1.5f);
                case 5: return L(0, 8, 6, 8, 26, 2, 0, 13, 6, 13, 26, 2, 0, 18, 6, 18, 26, 2, 0, 23, 6, 23, 26, 2, 0, 5, 16, 26, 16, 1.5f);
                case 6: return L(1, 16, 22, 3, 2, 0, 16, 19, 16, 10, 2, 1, 16, 16, 9, 1.5f, 1, 16, 16, 14, 1.5f);
                case 7: return L(1, 16, 16, 10, 2, 1, 16, 16, 4, 2, 0, 16, 2, 16, 6, 2, 0, 16, 26, 16, 30, 2);
                case 8: return L(0, 16, 4, 27, 9, 2, 0, 27, 9, 25, 21, 2, 0, 25, 21, 16, 28, 2, 0, 16, 28, 7, 21, 2, 0, 7, 21, 5, 9, 2, 0, 5, 9, 16, 4, 2);
                case 9: return L(3, 11, 5, 10, 14, 2, 0, 11, 19, 7, 27, 2, 0, 21, 19, 25, 27, 2, 0, 16, 19, 16, 28, 2);
                case 10: return L(1, 16, 16, 11, 2, 1, 16, 16, 6, 1.5f, 0, 5, 16, 10, 16, 2, 0, 22, 16, 27, 16, 2);
                case 11: return L(3, 6, 9, 20, 14, 2, 1, 24, 16, 4, 2, 0, 12, 23, 12, 28, 2);
                case 12: return L(0, 16, 28, 16, 12, 2, 1, 16, 11, 3, 2, 1, 16, 11, 8, 1.5f, 0, 10, 28, 22, 28, 2);
                case 13: return L(0, 12, 4, 12, 24, 2, 0, 20, 4, 20, 24, 2, 0, 12, 24, 16, 29, 2, 0, 20, 24, 16, 29, 2);
                case 14: return L(0, 16, 3, 11, 15, 2, 0, 11, 15, 19, 16, 2, 0, 19, 16, 14, 29, 2);
                case 15: return L(3, 6, 8, 20, 18, 2, 0, 6, 13, 26, 13, 1.5f, 0, 13, 8, 13, 26, 1.5f);
                // Network nodes.
                case G.NodeCommand: return L(0, 16, 5, 19, 13, 2, 0, 19, 13, 27, 13, 2, 0, 27, 13, 20, 18, 2, 0, 20, 18, 23, 27, 2, 0, 23, 27, 16, 22, 2, 0, 16, 22, 9, 27, 2, 0, 9, 27, 12, 18, 2, 0, 12, 18, 5, 13, 2, 0, 5, 13, 13, 13, 2, 0, 13, 13, 16, 5, 2);
                case G.NodeBase: return L(0, 7, 25, 25, 7, 3, 0, 5, 13, 13, 5, 2, 0, 19, 27, 27, 19, 2);
                case G.NodeAirfield: return L(0, 16, 4, 16, 28, 2.5f, 0, 5, 15, 27, 15, 2.5f, 0, 11, 26, 21, 26, 2);
                case G.NodeCity: return L(3, 5, 12, 8, 16, 2, 3, 14, 6, 7, 22, 2, 3, 22, 15, 6, 13, 2);
                // Objectives.
                case G.ObjAirfield: return L(0, 4, 22, 22, 4, 2, 0, 10, 28, 28, 10, 2, 0, 10, 22, 13, 19, 2, 0, 16, 16, 19, 13, 2, 0, 22, 10, 24, 8, 2);
                case G.ObjOutpost: return L(0, 9, 4, 9, 28, 2.5f, 0, 9, 5, 24, 9, 2, 0, 24, 9, 9, 14, 2, 0, 5, 28, 15, 28, 2);
                case G.ObjTown: return L(3, 4, 14, 9, 14, 2, 3, 14, 7, 8, 21, 2, 3, 23, 12, 6, 16, 2, 0, 3, 28, 29, 28, 2);
                case G.ObjAirDefence: return L(1, 13, 13, 8, 2, 0, 13, 13, 22, 4, 2, 0, 13, 21, 13, 28, 2, 0, 7, 28, 19, 28, 2, 1, 22, 4, 3, 1.5f);
                // Domains.
                case G.Space: return L(1, 16, 16, 12, 1.5f, 2, 16, 16, 4, 2, 26, 9, 3);
                case G.Cyber: return L(2, 16, 6, 3, 2, 7, 22, 3, 2, 25, 22, 3, 0, 16, 6, 7, 22, 1.5f, 0, 16, 6, 25, 22, 1.5f, 0, 7, 22, 25, 22, 1.5f);
                case G.SpecOps: return L(0, 6, 10, 16, 18, 2.5f, 0, 16, 18, 26, 10, 2.5f, 0, 6, 17, 16, 25, 2.5f, 0, 16, 25, 26, 17, 2.5f);
                // Station and support abilities.
                case G.RadarScan: return L(1, 16, 28, 19, 2, 1, 16, 28, 12.5f, 2, 1, 16, 28, 6, 2, 2, 16, 28, 2);
                case G.Elint: return L(0, 16, 12, 16, 28, 2, 1, 16, 12, 5, 1.5f, 1, 16, 12, 10, 1.5f, 2, 16, 12, 2);
                case G.Rod: return L(0, 16, 3, 16, 24, 3, 0, 10, 18, 16, 27, 2.5f, 0, 22, 18, 16, 27, 2.5f);
                case G.Emp: return L(1, 16, 16, 5, 2, 0, 16, 2, 16, 8, 2, 0, 16, 24, 16, 30, 2, 0, 2, 16, 8, 16, 2, 0, 24, 16, 30, 16, 2, 0, 6, 6, 10, 10, 2, 0, 22, 22, 26, 26, 2, 0, 6, 26, 10, 22, 2, 0, 22, 10, 26, 6, 2);
                case G.Uplink: return L(1, 16, 16, 9, 2, 2, 16, 16, 4, 0, 4, 16, 7, 16, 1.5f, 0, 25, 16, 28, 16, 1.5f);
                case G.Ping: return L(2, 16, 16, 3, 1, 16, 16, 8, 1.5f, 1, 16, 16, 13, 1.5f);
                case G.Track: return L(1, 16, 16, 10, 2, 0, 16, 2, 16, 10, 2, 0, 16, 22, 16, 30, 2, 0, 2, 16, 10, 16, 2, 0, 22, 16, 30, 16, 2);
                case G.Blackout: return L(1, 16, 16, 11, 2, 0, 8, 8, 24, 24, 2.5f);
                case G.Ghost: return L(0, 8, 28, 8, 13, 2, 0, 24, 28, 24, 13, 2, 1, 16, 13, 8, 2, 0, 8, 28, 12, 24, 2, 0, 12, 24, 16, 28, 2, 0, 16, 28, 20, 24, 2, 0, 20, 24, 24, 28, 2);
                case G.Spoof: return L(3, 4, 8, 12, 10, 2, 3, 16, 14, 12, 10, 2, 0, 16, 13, 16, 19, 1.5f);
                case G.Spot: return L(0, 3, 16, 16, 8, 2, 0, 16, 8, 29, 16, 2, 0, 29, 16, 16, 24, 2, 0, 16, 24, 3, 16, 2, 2, 16, 16, 4);
                case G.Suppress: return L(1, 16, 16, 12, 2, 0, 8, 8, 24, 24, 2.5f, 0, 24, 8, 8, 24, 2.5f);
                case G.Fortify: return L(3, 4, 14, 24, 14, 2, 0, 4, 10, 4, 14, 2, 0, 10, 10, 10, 14, 2, 0, 16, 10, 16, 14, 2, 0, 22, 10, 22, 14, 2, 0, 28, 10, 28, 14, 2);
                // Utilities.
                case G.Alert: return L(0, 16, 4, 29, 27, 2, 0, 29, 27, 3, 27, 2, 0, 3, 27, 16, 4, 2, 0, 16, 12, 16, 20, 2.5f, 2, 16, 24, 1.5f);
                case G.Pin: return L(2, 16, 11, 7, 0, 16, 16, 16, 29, 2);
                case G.Team: return L(0, 6, 21, 16, 12, 3, 0, 16, 12, 26, 21, 3);
                case G.Lock: return L(4, 8, 15, 16, 13, 1, 16, 14, 6, 2);
                case G.Check: return L(0, 6, 17, 13, 24, 3, 0, 13, 24, 26, 8, 3);
                case G.Flare: return L(2, 16, 22, 4, 0, 16, 18, 10, 4, 2, 0, 16, 18, 22, 4, 2, 0, 16, 18, 16, 3, 2);
                default: return L(0, 16, 9, 16, 23, 2, 0, 9, 16, 23, 16, 2);
            }
        }
    }
}
