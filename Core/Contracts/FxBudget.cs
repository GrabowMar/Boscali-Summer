using System;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// Hard ceilings one client effect promises to stay under. The bus ledgers render-target
    /// bytes and voices globally; passes are counted by the effect itself in DescribeFx.
    /// </summary>
    internal readonly struct FxBudget
    {
        public int MaxRtBytes { get; }
        public int MaxPasses { get; }
        public int MaxVoices { get; }
        public bool ScalesWithQuality { get; }

        public FxBudget(int maxRtBytes, int maxPasses, int maxVoices, bool scalesWithQuality)
        {
            MaxRtBytes = System.Math.Max(0, maxRtBytes);
            MaxPasses = System.Math.Max(0, maxPasses);
            MaxVoices = System.Math.Max(0, maxVoices);
            ScalesWithQuality = scalesWithQuality;
        }

        /// <summary>Bytes one render target costs the ledger (colour + depth, no mips).</summary>
        public static long RtBytes(int width, int height, int colourBitsPerPixel, int depthBits)
        {
            if (width <= 0 || height <= 0) return 0;
            long pixels = (long)width * height;
            return pixels * System.Math.Max(8, colourBitsPerPixel) / 8 + pixels * System.Math.Max(0, depthBits) / 8;
        }

        /// <summary>
        /// Scale a full pool cap or per-frame drain by a quality multiplier. A non-positive
        /// full budget stays off; anything positive keeps at least one slot so reduced
        /// quality throttles effects instead of silencing them.
        /// </summary>
        public static int ScaleCount(int full, float scale)
        {
            if (full <= 0) return 0;
            if (scale >= 1f) return full;
            if (scale <= 0f) return 1;
            return System.Math.Max(1, (int)System.Math.Round(full * scale));
        }

        /// <summary>Colour bits for the formats effects actually allocate; unknown defaults to 32.</summary>
        public static int ColourBits(string formatName)
        {
            if (string.IsNullOrEmpty(formatName)) return 32;
            switch (formatName)
            {
                case "R8": return 8;
                case "RHalf": case "R16": case "ARGB4444": case "ARGB1555": case "RGB565": return 16;
                case "RGHalf": case "RFloat": case "RInt": return 32;
                case "ARGBHalf": case "RGFloat": case "RGInt": return 64;
                case "ARGBFloat": case "ARGBInt": return 128;
                default: return 32;
            }
        }
    }
}
