namespace BoscaliSummer.Core.Util
{
    /// <summary>PNG header validation, kept Unity-free so tests link it. Decoding to a sprite is in PngSprites.Unity.cs.</summary>
    internal static partial class PngSprites
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };

        /// <summary>True for a PNG whose IHDR width and height are each 1..<paramref name="maxDimension"/>; reads nothing past the header.</summary>
        internal static bool IsSupported(byte[] data, int maxDimension, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (data == null || data.Length < 24) return false;
            for (int i = 0; i < Signature.Length; i++)
                if (data[i] != Signature[i]) return false;
            if (data[12] != (byte)'I' || data[13] != (byte)'H' || data[14] != (byte)'D' || data[15] != (byte)'R')
                return false;

            uint rawWidth = BigEndian(data, 16), rawHeight = BigEndian(data, 20);
            if (rawWidth == 0 || rawHeight == 0 || rawWidth > maxDimension || rawHeight > maxDimension) return false;
            width = (int)rawWidth;
            height = (int)rawHeight;
            return true;
        }

        private static uint BigEndian(byte[] data, int offset) =>
            ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
}
