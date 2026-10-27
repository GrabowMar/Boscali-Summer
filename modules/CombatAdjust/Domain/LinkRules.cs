namespace BoscaliSummer.Modules.CombatAdjust.Domain
{
    /// <summary>Who saw it: a networked sensor (any aircraft, any radar), a missile seeker, or a lookout's eyes.</summary>
    internal enum Reporter { Networked, Missile, Eyes }

    internal static class LinkRules
    {
        /// <summary>Does this sighting reach the faction's shared picture? Lookouts always report what is close, the rest by chance.</summary>
        public static bool Shares(Reporter reporter, float distanceM, float closeRangeM, float chance, float roll01) =>
            reporter == Reporter.Networked ||
            (reporter == Reporter.Eyes && (distanceM < closeRangeM || roll01 < chance));
    }
}
