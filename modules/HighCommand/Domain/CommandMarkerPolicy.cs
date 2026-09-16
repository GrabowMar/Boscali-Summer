namespace BoscaliSummer.Features.HighCommand.Domain
{
    /// <summary>
    /// What goes on a post's map marker, kept pure so the map obeys the same fog as the
    /// roster: a marker is drawn for a post the faction owns or has eyes on, and never for a
    /// dead one - a killed commander's building is rubble until it is re-established, and a
    /// marker says "there is someone to find here".
    /// </summary>
    internal static class CommandMarkerPolicy
    {
        public static bool Show(bool friendly, bool known, bool isKia) => !isKia && (friendly || known);

        /// <summary>Theater posts read bigger than base posts, so the map shows the hierarchy.</summary>
        public static float Size(int tier) => tier <= 0 ? 15f : tier == 1 ? 12f : 10f;

        /// <summary>A post under fire pulses: a triangle wave over the fraction of a second.</summary>
        public static float Pulse(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return 0f;
            float phase = seconds - (float)System.Math.Floor(seconds);
            return phase < 0.5f ? phase * 2f : (1f - phase) * 2f;
        }
    }
}
