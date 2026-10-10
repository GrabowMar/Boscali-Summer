using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// The shader's copy of a baked tape (spec 7.2): an RGBAFloat texture, one column per tape texel and 11 rows (0-2 low genus,
    /// 3-6 middle layer, 7-10 high layer; <see cref="SkyColumns.GpuRow"/> says where each column goes), point sampled and lerped by
    /// hand in the shader so no filter-format support is needed and rows never blend. The texture is the array's numbers, nothing
    /// else, so the CPU's TapeSampler reads the same sky. Rebuilt only when the tape's revision changes. Unity-only.
    /// </summary>
    internal sealed class TapeTexture
    {
        public const int Rows = 11;

        private Texture2D texture;
        private Color[] pixels;
        private BakedTape source;
        private int builtRevision = -1;

        /// <summary>False when the GPU cannot make float textures; the caller keeps native clouds.</summary>
        public static bool Supported => SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat);

        public Texture2D Texture => texture;

        /// <summary>Uploads the tape if it is new or changed. Returns false when the texture format is unsupported.</summary>
        public bool Update(BakedTape tape)
        {
            if (!Supported) return false;
            if (texture != null && ReferenceEquals(tape, source) && tape.Revision == builtRevision) return true;
            int n = tape.Texels;
            if (texture == null || texture.width != n)
            {
                if (texture != null) Object.Destroy(texture);
                texture = new Texture2D(n, Rows, TextureFormat.RGBAFloat, false, true)
                { name = "Boscali Weather Tape", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                pixels = new Color[n * Rows];
            }
            float[] data = tape.Data;
            int stride = SkyColumns.Count;
            for (int x = 0; x < n; x++)
                for (int c = 0; c < stride; c++)
                {
                    int row = SkyColumns.GpuRow[c];
                    if (row < 0) continue;
                    float v = data[x * stride + c];
                    int index = row * n + x;
                    Color p = pixels[index];
                    switch (SkyColumns.GpuChannel[c])
                    {
                        case 0: p.r = v; break;
                        case 1: p.g = v; break;
                        case 2: p.b = v; break;
                        default: p.a = v; break;
                    }
                    pixels[index] = p;
                }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            source = tape;
            builtRevision = tape.Revision;
            return true;
        }

        public void Dispose()
        {
            if (texture != null) Object.Destroy(texture);
            texture = null;
            pixels = null;
            source = null;
            builtRevision = -1;
        }
    }
}
