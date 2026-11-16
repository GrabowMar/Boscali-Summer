namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// What happens to the pilot's jet that the mod's visor (the HUD's reactive layer, <c>VisorFx</c>) reacts to, written by the modules that really see it and read by the HUD: a hit on the followed aircraft
    /// (Immersion's hit patch) and the EMP disruption the mod runs on the player's cockpit (Support's <c>CockpitEmpDisruption</c>). Plain statics, no behaviour.
    /// </summary>
    internal static class VisorEvents
    {
        /// <summary>The followed aircraft's hit count (only grows) and how heavy the last hit was, 0..1: a round going through 0.5, a warhead's blast 1.</summary>
        public static int HitSerial;
        public static float HitWeight;

        /// <summary>The EMP disruption runs until this <c>Time.time</c> (the routine's own clock).</summary>
        public static float EmpUntil;

        public static void Hit(float pierceDamage, float blastDamage)
        {
            HitWeight = blastDamage > pierceDamage ? 1f : 0.5f;
            HitSerial++;
        }
    }
}
