using System;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static class MfdChartScale
    {
        public static float Fraction(float value, float maximum) =>
            float.IsNaN(value) || float.IsInfinity(value) || float.IsNaN(maximum) ||
            float.IsInfinity(maximum) || maximum <= 0f ? 0f : Math.Max(0f, Math.Min(1f, value / maximum));
    }
}
