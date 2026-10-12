namespace BoscaliSummer.Core.Math
{
    internal static class MapScale
    {
        /// <summary>Mean of the east and north runs; false when either is not finite or not above zero.</summary>
        public static bool Mean(float east, float north, out float mean)
        {
            mean = 0f;
            if (!(east > 0f) || !(north > 0f) || float.IsInfinity(east) || float.IsInfinity(north)) return false;
            mean = 0.5f * (east + north);
            return true;
        }
    }
}
