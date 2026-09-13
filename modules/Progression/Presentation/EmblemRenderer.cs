using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Features.Progression.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Progression.Presentation
{
    /// <summary>
    /// Local squadron-emblem art: a procedural silhouette/charge/palette renderer plus a
    /// bounded picker for user-supplied PNGs. Nothing is ever bundled or downloaded; files
    /// are read only from the canonical config folder.
    /// </summary>
    internal static class EmblemRenderer
    {
        private const int Size = 64;
        private const int Supersample = 4;
        private const int MaximumCachedFiles = 16;

        private static readonly Dictionary<string, Sprite> designs =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Sprite> files =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        public static string Directory
        {
            get
            {
                try { return Path.Combine(BepInEx.Paths.ConfigPath, "BoscaliSummer", "Emblems"); }
                catch (Exception) { return string.Empty; }
            }
        }

        /// <summary>Bounded PNG scan. Missing folder means no custom art, never an error.</summary>
        public static string[] ScanFiles()
        {
            try
            {
                string dir = Directory;
                if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir))
                    return Array.Empty<string>();
                string[] found = System.IO.Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly);
                Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                var result = new List<string>(Mathf.Min(found.Length, MaximumCachedFiles));
                for (int i = 0; i < found.Length && result.Count < MaximumCachedFiles; i++)
                {
                    try
                    {
                        if (new FileInfo(found[i]).Length <= 1 << 20) result.Add(found[i]);
                    }
                    catch (Exception) { }
                }
                return result.ToArray();
            }
            catch (Exception) { return Array.Empty<string>(); }
        }

        public static Sprite Procedural(EmblemDesign design)
        {
            string key = design.Encode();
            if (designs.TryGetValue(key, out Sprite cached) && cached != null) return cached;
            Sprite sprite = Build(design);
            designs[key] = sprite;
            return sprite;
        }

        /// <summary>User PNG, bounded by size and count; cached sprites are never destroyed
        /// because a live panel may still display one, so the cache simply stops growing.</summary>
        public static Sprite File(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (files.TryGetValue(path, out Sprite cached) && cached != null) return cached;
            if (files.Count >= MaximumCachedFiles) return null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > 1 << 20) return null;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "BoscaliEmblem_" + Path.GetFileNameWithoutExtension(path),
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                if (!ImageConversion.LoadImage(texture, System.IO.File.ReadAllBytes(path), false) ||
                    texture.width < 8 || texture.height < 8 || texture.width > 512 || texture.height > 512)
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
                Sprite sprite = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = texture.name;
                sprite.hideFlags = HideFlags.HideAndDontSave;
                files[path] = sprite;
                return sprite;
            }
            catch (Exception) { return null; }
        }

        // ---- Procedural rasteriser -------------------------------------------------------

        private static Sprite Build(EmblemDesign design)
        {
            Color primary = AvTheme.Unity(EmblemDesign.Primary(design.Palette));
            Color secondary = AvTheme.Unity(EmblemDesign.Secondary(design.Palette));
            Color field = Color.Lerp(AvTheme.Unity(AvTokens.Ground), primary, 0.22f);
            Color border = Color.Lerp(Color.black, primary, 0.55f);
            Color clear = new Color(0f, 0f, 0f, 0f);

            int high = Size * Supersample;
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sy = 0; sy < Supersample; sy++)
                    {
                        for (int sx = 0; sx < Supersample; sx++)
                        {
                            float u = (x * Supersample + sx + 0.5f) / high;
                            float v = (y * Supersample + sy + 0.5f) / high;
                            Color sample = Sample(design, u, v, primary, secondary, field, border, clear);
                            r += sample.r;
                            g += sample.g;
                            b += sample.b;
                            a += sample.a;
                        }
                    }
                    float count = Supersample * Supersample;
                    pixels[y * Size + x] = new Color(r / count, g / count, b / count, a / count);
                }
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "BoscaliEmblem_" + design.Encode(),
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            Sprite sprite = UnityEngine.Sprite.Create(texture,
                new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Color Sample(EmblemDesign design, float u, float v,
            Color primary, Color secondary, Color field, Color border, Color clear)
        {
            if (!InShape(design.Shape, u, v)) return clear;
            const float edge = 0.018f;
            if (!InShape(design.Shape, u + edge, v) || !InShape(design.Shape, u - edge, v) ||
                !InShape(design.Shape, u, v + edge) || !InShape(design.Shape, u, v - edge))
                return border;
            return InCharge(design.Charge, u, v) ? secondary : field;
        }

        private static bool InShape(byte shape, float u, float v)
        {
            switch (shape)
            {
                case 1: return (u - 0.5f) * (u - 0.5f) + (v - 0.52f) * (v - 0.52f) <= 0.46f * 0.46f;
                case 2: return PointInPolygon(Delta, u, v);
                case 3: return PointInPolygon(Banner, u, v);
                default: return PointInPolygon(Shield, u, v);
            }
        }

        private static bool InCharge(byte charge, float u, float v)
        {
            switch (charge)
            {
                case 0: return PointInPolygon(Star, u, v);
                case 1: return PointInPolygon(Bolt, u, v);
                case 2: return PointInPolygon(Wings, u, v);
                case 3: return u >= 0.4f && u <= 0.6f && v >= 0.18f && v <= 0.82f ||
                               v >= 0.42f && v <= 0.62f && u >= 0.18f && u <= 0.82f;
                default: return PointInPolygon(Chevron, u, v);
            }
        }

        private static bool PointInPolygon(Vector2[] polygon, float u, float v)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if ((a.y > v) == (b.y > v)) continue;
                float x = (b.x - a.x) * (v - a.y) / (b.y - a.y) + a.x;
                if (x > u) inside = !inside;
            }
            return inside;
        }

        private static readonly Vector2[] Shield =
        {
            new Vector2(0.5f, 0.98f), new Vector2(0.08f, 0.78f), new Vector2(0.08f, 0.42f),
            new Vector2(0.5f, 0.02f), new Vector2(0.92f, 0.42f), new Vector2(0.92f, 0.78f),
        };

        private static readonly Vector2[] Delta =
        {
            new Vector2(0.5f, 0.98f), new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.04f),
        };

        private static readonly Vector2[] Banner =
        {
            new Vector2(0.12f, 0.95f), new Vector2(0.88f, 0.95f), new Vector2(0.88f, 0.2f),
            new Vector2(0.5f, 0.36f), new Vector2(0.12f, 0.2f),
        };

        private static readonly Vector2[] Star =
        {
            new Vector2(0.50f, 0.84f), new Vector2(0.59f, 0.60f), new Vector2(0.84f, 0.59f),
            new Vector2(0.64f, 0.43f), new Vector2(0.71f, 0.18f), new Vector2(0.50f, 0.33f),
            new Vector2(0.29f, 0.18f), new Vector2(0.36f, 0.43f), new Vector2(0.16f, 0.59f),
            new Vector2(0.41f, 0.60f),
        };

        private static readonly Vector2[] Bolt =
        {
            new Vector2(0.60f, 0.95f), new Vector2(0.30f, 0.55f), new Vector2(0.47f, 0.55f),
            new Vector2(0.38f, 0.10f), new Vector2(0.72f, 0.55f), new Vector2(0.52f, 0.55f),
        };

        private static readonly Vector2[] Wings =
        {
            new Vector2(0.46f, 0.66f), new Vector2(0.10f, 0.84f), new Vector2(0.28f, 0.34f),
            new Vector2(0.46f, 0.50f), new Vector2(0.54f, 0.50f), new Vector2(0.72f, 0.34f),
            new Vector2(0.90f, 0.84f), new Vector2(0.54f, 0.66f),
        };

        private static readonly Vector2[] Chevron =
        {
            new Vector2(0.50f, 0.86f), new Vector2(0.12f, 0.42f), new Vector2(0.28f, 0.28f),
            new Vector2(0.50f, 0.58f), new Vector2(0.72f, 0.28f), new Vector2(0.88f, 0.42f),
        };
    }
}
