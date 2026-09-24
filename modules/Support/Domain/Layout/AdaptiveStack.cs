using System;

namespace BoscaliSummer.Features.Support.Domain.Layout
{
    /// <summary>One MFD section. A fill section takes whatever height remains, down to its minimum.</summary>
    internal readonly struct StackPiece
    {
        public readonly int Tier;
        public readonly float Preferred;
        public readonly float Minimum;
        public readonly bool Fill;

        public StackPiece(int tier, float preferred, float minimum, bool fill)
        {
            Tier = tier;
            Preferred = preferred;
            Minimum = minimum;
            Fill = fill;
        }
    }

    internal static class AdaptiveStack
    {
        public static float Fit(StackPiece[] pieces, int count, float available, float[] heights)
        {
            if (heights != null)
            {
                for (int i = 0; i < heights.Length; i++) heights[i] = 0f;
            }
            if (pieces == null || heights == null || count <= 0 || !(available > 0f)) return 0f;
            count = Math.Min(count, Math.Min(pieces.Length, heights.Length));
            int n = Math.Min(count, 16);
            Span<int> order = stackalloc int[16];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = 1; i < n; i++)
            {
                int key = order[i];
                int j = i - 1;
                while (j >= 0 && pieces[order[j]].Tier > pieces[key].Tier)
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = key;
            }

            float used = 0f;
            float remaining = available;
            for (int k = 0; k < n; k++)
            {
                int index = order[k];
                StackPiece piece = pieces[index];
                float need = piece.Fill ? piece.Minimum : piece.Preferred;
                if (need > remaining) continue;
                float height = piece.Fill ? remaining : piece.Preferred;
                if (height > remaining) height = remaining;
                heights[index] = height;
                used += height;
                remaining -= height;
            }
            return used;
        }
    }
}
