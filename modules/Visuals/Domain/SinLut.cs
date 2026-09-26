using System;

namespace BoscaliSummer.Features.Visuals.Domain
{
    /// <summary>
    /// Sine over a 1024-entry table with linear interpolation: within ~1e-6 of
    /// <see cref="Math.Sin"/> at a fraction of the cost, for per-vertex curves.
    /// </summary>
    public static class SinLut
    {
        public const int Size = 1024;

        private static readonly float InverseStep = Size / (2f * (float)Math.PI);
        private static readonly float[] Table = BuildTable();

        private static float[] BuildTable()
        {
            float[] table = new float[Size];
            for (int i = 0; i < Size; i++)
                table[i] = (float)Math.Sin(2.0 * Math.PI * i / Size);
            return table;
        }

        public static float Sin(float radians)
        {
            float wrapped = radians * InverseStep;
            wrapped -= (float)Math.Floor(wrapped / Size) * Size;
            int i0 = (int)wrapped;
            float frac = wrapped - i0;
            int i1 = (i0 + 1) & (Size - 1);
            return Table[i0] + (Table[i1] - Table[i0]) * frac;
        }
    }
}
