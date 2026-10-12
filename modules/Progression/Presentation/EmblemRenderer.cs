using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Modules.Progression.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    /// <summary>
    /// Local squadron-emblem art: textures of the procedural <see cref="EmblemRaster"/> drawing
    /// plus a bounded picker for user-supplied PNGs. Nothing is ever bundled or downloaded; files
    /// are read only from the canonical config folder.
    /// </summary>
    internal static class EmblemRenderer
    {
        /// <summary>Card sprites: twice the largest size a panel shows them at.</summary>
        public const int CardSize = 128;

        /// <summary>Tail markings: twice the livery atlas's texel density on the largest fin, so the bake only ever
        /// samples the drawing down and a keyline stays a keyline.</summary>
        public const int PaintSize = 512;

        private const int MaximumCachedDesigns = 64;
        private const int MaximumCachedFiles = 16;

        private static readonly Dictionary<EmblemDesign, Sprite> designs = new Dictionary<EmblemDesign, Sprite>();
        private static readonly Queue<EmblemDesign> designOrder = new Queue<EmblemDesign>(MaximumCachedDesigns);
        private static readonly Dictionary<string, Sprite> files =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly byte[] cardPixels = new byte[CardSize * CardSize * 4];

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

        /// <summary>
        /// Shared, cached card sprite for a design that does not change under its viewer: the
        /// hostile crests. The oldest entry is destroyed once 64 are held, so a design the player
        /// is still editing must use an <see cref="EmblemCanvas"/> instead.
        /// </summary>
        public static Sprite Procedural(EmblemDesign design)
        {
            if (designs.TryGetValue(design, out Sprite cached) && cached != null) return cached;
            if (designs.Count >= MaximumCachedDesigns && designOrder.Count > 0)
            {
                EmblemDesign oldest = designOrder.Dequeue();
                if (designs.TryGetValue(oldest, out Sprite evicted) && evicted != null)
                {
                    UnityEngine.Object.Destroy(evicted.texture);
                    UnityEngine.Object.Destroy(evicted);
                }
                designs.Remove(oldest);
            }
            Sprite sprite = Wrap(Card(design));
            if (!designs.ContainsKey(design)) designOrder.Enqueue(design);
            designs[design] = sprite;
            return sprite;
        }

        /// <summary>The pixels of a flat, unshaded marking for metal. Pure arithmetic: safe on a worker thread.</summary>
        public static byte[] PaintPixels(EmblemDesign design, EmblemFinish finish)
        {
            var pixels = new byte[PaintSize * PaintSize * 4];
            EmblemRaster.Render(design, finish, PaintSize, false, pixels);
            return pixels;
        }

        /// <summary>Uploads <see cref="PaintPixels"/>. The caller owns and destroys the texture.</summary>
        public static Texture2D PaintTexture(byte[] pixels)
        {
            Texture2D texture = Blank(PaintSize, "BoscaliEmblemPaint");
            texture.SetPixelData(pixels, 0);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return texture;
        }

        internal static Texture2D Card(EmblemDesign design, Texture2D reuse = null)
        {
            EmblemRaster.Render(design, EmblemFinish.Colour, CardSize, true, cardPixels);
            Texture2D texture = reuse != null ? reuse : Blank(CardSize, "BoscaliEmblem");
            texture.SetPixelData(cardPixels, 0);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            return texture;
        }

        internal static Sprite Wrap(Texture2D texture)
        {
            Sprite sprite = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Texture2D Blank(int size, string name) =>
            new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
            {
                name = name,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

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
                Sprite sprite = Wrap(texture);
                files[path] = sprite;
                return sprite;
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// One reusable card sprite for a design that changes under its viewer: the player's own
    /// emblem and each proof on the STUDIO sheet. Redrawing in place keeps a long editing session
    /// at a fixed handful of textures.
    /// </summary>
    internal sealed class EmblemCanvas
    {
        private Texture2D texture;
        private Sprite sprite;
        private EmblemDesign drawn;

        public Sprite Draw(EmblemDesign design)
        {
            if (sprite != null && drawn == design) return sprite;
            texture = EmblemRenderer.Card(design, texture);
            if (sprite == null) sprite = EmblemRenderer.Wrap(texture);
            drawn = design;
            return sprite;
        }
    }
}
