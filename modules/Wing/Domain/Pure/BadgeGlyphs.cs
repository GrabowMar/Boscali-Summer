namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>The map badge's status letters (spec 2026-10-04 §2): the stance's initial, then F low fuel, D damaged, S on a rescue.
    /// Words as well as colour: each letter is a rich-text coloured one, and the plain letters stay readable without colour.</summary>
    internal static class BadgeGlyphs
    {
        public const string Fuel = "<color=#FFC252>F</color>", Damage = "<color=#FF6166>D</color>", Rescue = "<color=#80C2D9>S</color>";

        /// <summary>"B4" or "B4 R", "B4 RFD": the badge, then the letters; the badge alone when there are none.</summary>
        public static string Label(string badge, char stance, bool lowFuel, bool damaged, bool sar)
        {
            string letters = (stance != '\0' ? stance.ToString() : "") + (lowFuel ? Fuel : "") + (damaged ? Damage : "") + (sar ? Rescue : "");
            return letters.Length == 0 ? badge : (badge ?? "") + " " + letters;
        }

        /// <summary>A bit per condition, so a mark can tell whether its label changed.</summary>
        public static int Mask(char stance, bool lowFuel, bool damaged, bool sar) =>
            stance | (lowFuel ? 1 << 16 : 0) | (damaged ? 1 << 17 : 0) | (sar ? 1 << 18 : 0);
    }
}
