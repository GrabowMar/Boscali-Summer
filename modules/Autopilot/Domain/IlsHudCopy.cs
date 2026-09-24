using System.Text;

namespace BoscaliSummer.Features.Autopilot.Domain
{
    internal static class IlsHudCopy
    {
        public static string Text(bool caution) => caution ? "ILS · CORRECT" : "ILS";

        public static string Detail(string locWord, string gsWord, bool inside, string distanceText)
        {
            var builder = new StringBuilder(48);
            if (!string.IsNullOrEmpty(locWord)) builder.Append(locWord);
            if (!string.IsNullOrEmpty(gsWord))
            {
                if (builder.Length > 0) builder.Append(" · ");
                builder.Append(gsWord);
            }
            builder.Append(inside ? " · INSIDE" : " · OUT");
            if (!string.IsNullOrEmpty(distanceText))
            {
                builder.Append(" · ");
                builder.Append(distanceText);
            }
            return builder.ToString();
        }

        /// <summary>On-course is a full bar; deviation empties it.</summary>
        public static float Bar(float locBeam)
        {
            float a = locBeam < 0f ? -locBeam : locBeam;
            if (float.IsNaN(a)) return 0f;
            float v = 1f - a;
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }
    }
}
