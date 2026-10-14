using System.Collections.Generic;
using BoscaliSummer.Modules.Command.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Reads the game's terrain splatmaps (forest, farmland, grassland, rock) once, through
    /// the GPU because they are not CPU-readable, and paints them into the relief's intel
    /// texture with <see cref="LandCover"/>. Nothing is shipped: a map whose splatmaps are
    /// not loaded keeps the plain intel look.
    /// </summary>
    internal static class ReliefLandCover
    {
        /// <summary>Mask texels on a side: 80 m on the 81.92 km map, finer than a relief sample.</summary>
        private const int MaskSize = 1024;

        // Splatmap names per relief map: forest (lush), farmland, grassland, rock.
        private static readonly Dictionary<string, string[]> Splatmaps = new Dictionary<string, string[]>
        {
            ["terrain2_map"] = new[] { "splatmap_lush", "splatmap_fields", "splatmap_grassy", "splatmap_rocky" },
            ["terrain_naval_map"] = new[] { "terrain_naval_splat_lush", "terrain_naval_splat_fields",
                "terrain_naval_splat_grass", "terrain_naval_splat_rock" },
        };

        /// <summary>Tints a readable RGB24 or RGBA32 <paramref name="style"/> in place; false leaves it untouched.</summary>
        public static bool Paint(Texture2D style, string mapAssetName, bool[] land, int samples)
        {
            if (style == null || (style.format != TextureFormat.RGB24 && style.format != TextureFormat.RGBA32) || mapAssetName == null ||
                !Splatmaps.TryGetValue(mapAssetName, out string[] names)) return false;

            var found = new Texture2D[names.Length];
            foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>())
                for (int i = 0; i < names.Length; i++)
                    if (found[i] == null && texture.name == names[i]) found[i] = texture;
            for (int i = 0; i < found.Length; i++)
                if (found[i] == null)
                {
                    Plugin.Logger.LogInfo("[MAP] Land cover off: splatmap " + names[i] + " is not loaded.");
                    return false;
                }

            var masks = new LandCoverMasks
            {
                Size = MaskSize,
                Forest = ReadMask(found[0]),
                Fields = ReadMask(found[1]),
                Grass = ReadMask(found[2]),
                Rock = ReadMask(found[3]),
            };
            if (!masks.Valid) return false;

            byte[] rgb = style.GetRawTextureData();
            LandCover.Tint(rgb, style.width, style.height, style.format == TextureFormat.RGB24 ? 3 : 4, masks, land, samples);
            style.LoadRawTextureData(rgb);
            return true;
        }

        /// <summary>One channel of a non-readable texture at <see cref="MaskSize"/>, row 0 at the bottom.</summary>
        private static byte[] ReadMask(Texture2D source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(MaskSize, MaskSize, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            var read = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, MaskSize, MaskSize), 0, 0, false);
                Color32[] pixels = read.GetPixels32();
                var mask = new byte[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) mask[i] = pixels[i].r;
                return mask;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(read);
            }
        }
    }
}
