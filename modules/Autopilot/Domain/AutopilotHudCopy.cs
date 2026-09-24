using System.Text;

namespace BoscaliSummer.Features.Autopilot.Domain
{
    /// <summary>
    /// The landing-autopilot widget's copy, pure so the test project links it directly. The
    /// widget supplies the already-formatted distance, because the game's own unit converter is
    /// not available to the pure tests.
    /// </summary>
    internal static class AutopilotHudCopy
    {
        /// <summary>"AUTOPILOT · FINAL", or the plain engaged word when a phase is missing.</summary>
        public static string Text(string phaseWord) =>
            string.IsNullOrEmpty(phaseWord) ? "AUTOPILOT · ENGAGED" : "AUTOPILOT · " + phaseWord;

        /// <summary>"RUB 27 · 6.2km"; whichever part is missing is dropped, never dashed.</summary>
        public static string Detail(string target, string distanceText)
        {
            bool hasTarget = !string.IsNullOrEmpty(target);
            bool hasDistance = !string.IsNullOrEmpty(distanceText);
            if (!hasTarget && !hasDistance) return null;
            if (!hasTarget) return distanceText;
            if (!hasDistance) return target;
            var builder = new StringBuilder(target.Length + distanceText.Length + 3);
            builder.Append(target).Append(" · ").Append(distanceText);
            return builder.ToString();
        }
    }
}
