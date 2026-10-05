namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>Which doctrine axes a scope flies differently from its stance (the * and the outlined base value on ORDERS).</summary>
    internal static class StanceDiff
    {
        public static int Mask(byte[] live, byte[] stance)
        {
            if (live == null || stance == null || live.Length < Stance.AxisCount || stance.Length < Stance.AxisCount) return 0;
            int m = 0;
            for (int i = 0; i < Stance.AxisCount; i++) if (live[i] != stance[i]) m |= 1 << i;
            return m;
        }

        public static bool Changed(int mask, int axis) => axis >= 0 && axis < Stance.AxisCount && (mask & (1 << axis)) != 0;

        public static int Count(int mask)
        {
            int n = 0;
            for (int i = 0; i < Stance.AxisCount; i++) if ((mask & (1 << i)) != 0) n++;
            return n;
        }
    }
}
