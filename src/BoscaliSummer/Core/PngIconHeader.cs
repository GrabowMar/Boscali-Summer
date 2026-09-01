using System;

namespace BoscaliSummer.Core
{
    internal static class PngIconHeader
    {
        public const int MaximumFileBytes = 256 * 1024;
        public const int MaximumDimension = 256;

        private static readonly byte[] Signature =
        {
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a
        };

        public static bool IsSupported(byte[] data, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (data == null || data.Length < 24 || data.Length > MaximumFileBytes)
                return false;

            for (int i = 0; i < Signature.Length; i++)
                if (data[i] != Signature[i]) return false;

            if (data[12] != (byte)'I' || data[13] != (byte)'H' ||
                data[14] != (byte)'D' || data[15] != (byte)'R')
                return false;

            uint rawWidth = ReadBigEndianUInt32(data, 16);
            uint rawHeight = ReadBigEndianUInt32(data, 20);
            if (rawWidth == 0 || rawHeight == 0 ||
                rawWidth > MaximumDimension || rawHeight > MaximumDimension)
                return false;

            width = (int)rawWidth;
            height = (int)rawHeight;
            return true;
        }

        private static uint ReadBigEndianUInt32(byte[] data, int offset) =>
            ((uint)data[offset] << 24) |
            ((uint)data[offset + 1] << 16) |
            ((uint)data[offset + 2] << 8) |
            data[offset + 3];
    }
}
