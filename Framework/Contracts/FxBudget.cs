using System;

namespace BoscaliSummer.Framework.Contracts
{
    internal readonly struct FxBudget
    {
        public long MaxRtBytes { get; }
        public int MaxPasses { get; }
        public int MaxVoices { get; }
        public bool ScalesWithQuality { get; }

        public FxBudget(long maxRtBytes, int maxPasses, int maxVoices, bool scalesWithQuality)
        {
            MaxRtBytes = Math.Max(0, maxRtBytes);
            MaxPasses = Math.Max(0, maxPasses);
            MaxVoices = Math.Max(0, maxVoices);
            ScalesWithQuality = scalesWithQuality;
        }

        public static long RtBytes(int width, int height, int colourBits, int depthBits)
        {
            if (width <= 0 || height <= 0) return 0;
            return (long)width * height *
                ((Math.Max(0, colourBits) + 7) / 8 + (Math.Max(0, depthBits) + 7) / 8);
        }

        public static int ColourBits(string format)
        {
            switch (format)
            {
                case "ARGBHalf": return 64;
                case "ARGBFloat": return 128;
                case "R8": return 8;
                default: return 32;
            }
        }
    }
}
