using System.Text;

namespace BoscaliSummer.Features.Progression.Domain
{
    /// <summary>
    /// The ace-hunt widget's copy, pure so the test project links it directly. It names the
    /// encounter the SQD panel is showing, never a second version of it.
    /// </summary>
    internal static class AceHuntHudCopy
    {
        /// <summary>"ACE HUNT · VULTURE", falling back to the wing when the ace is unnamed.</summary>
        public static string Text(string aceName, string wingName)
        {
            string name = !string.IsNullOrEmpty(aceName) ? aceName
                : !string.IsNullOrEmpty(wingName) ? wingName : "UNKNOWN";
            return "ACE HUNT · " + name;
        }

        /// <summary>"TIER 3 · 3/4 UP · HUNTING"; empty parts are dropped, never dashed.</summary>
        public static string Detail(int tier, int alive, int count, string status)
        {
            var builder = new StringBuilder(40);
            builder.Append("TIER ").Append(tier);
            if (count > 0) builder.Append(" · ").Append(alive).Append('/').Append(count).Append(" UP");
            if (!string.IsNullOrEmpty(status)) builder.Append(" · ").Append(status);
            return builder.ToString();
        }

        /// <summary>Share of the wing still flying; 0 when the wing size is unknown.</summary>
        public static float Bar(int alive, int count)
        {
            if (count <= 0) return 0f;
            float ratio = (float)alive / count;
            return ratio < 0f ? 0f : ratio > 1f ? 1f : ratio;
        }
    }
}
