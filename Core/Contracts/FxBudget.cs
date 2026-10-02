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

        /// <summary>Colour bits for the formats effects actually allocate; unknown defaults to 32.</summary>
        public static int ColourBits(string formatName)
        {
            if (string.IsNullOrEmpty(formatName)) return 32;
            if (formatName.IndexOf("R8", StringComparison.Ordinal) >= 0) return 8;
            if (formatName.IndexOf("Half", StringComparison.Ordinal) >= 0) return 64;
            if (formatName.IndexOf("Float", StringComparison.Ordinal) >= 0) return 128;
            return 32;
        }
    }
}
