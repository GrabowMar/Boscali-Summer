namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>Chip rows that wrap (SUPPLY's FIT chips, LOADOUT's TEMPLATE chips): chips of given widths placed left to right from
    /// <c>startX</c>, a chip that would pass <c>width</c> starting the next line.</summary>
    internal static class ChipFlow
    {
        /// <summary>Fills <paramref name="xs"/> and <paramref name="lines"/> for each of <paramref name="count"/> chips; returns how many
        /// lines were used (at least 1). A chip wider than the line sits alone, clipped to it by the caller.</summary>
        public static int Place(float[] widths, int count, float startX, float width, float gap, float[] xs, int[] lines)
        {
            float x = startX;
            int line = 0;
            for (int i = 0; i < count; i++)
            {
                float w = widths[i];
                if (x > startX && x + w > width)
                {
                    line++;
                    x = startX;
                }
                xs[i] = x;
                lines[i] = line;
                x += w + gap;
            }
            return line + 1;
        }
    }
}
