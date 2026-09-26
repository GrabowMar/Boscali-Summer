namespace BoscaliSummer.Features.Hud.Domain
{
    /// <summary>
    /// The shots card's words. Names are the game's own unit and seeker labels, shortened to fit
    /// a 336px corner row; the countdown is seconds or nothing, never a guess dressed as one.
    /// </summary>
    internal static class ShotCopy
    {
        public const int MaxTarget = 14;
        public const int MaxSeeker = 6;

        public static string ShortName(string unitName)
        {
            if (string.IsNullOrWhiteSpace(unitName)) return "TGT";
            string name = unitName.Trim().ToUpperInvariant();
            return name.Length <= MaxTarget ? name : name.Substring(0, MaxTarget);
        }

        public static string SeekerTag(string seekerType)
        {
            if (string.IsNullOrWhiteSpace(seekerType)) return "MSL";
            string tag = seekerType.Trim().ToUpperInvariant();
            return tag.Length <= MaxSeeker ? tag : tag.Substring(0, MaxSeeker);
        }

        public static string EtaText(float eta)
        {
            if (float.IsNaN(eta) || float.IsInfinity(eta) || eta < 0f || eta > ShotMath.MaxEta) return "--";
            return ((int)System.Math.Round(eta, System.MidpointRounding.AwayFromZero))
                .ToString(System.Globalization.CultureInfo.InvariantCulture) + "s";
        }
    }
}
