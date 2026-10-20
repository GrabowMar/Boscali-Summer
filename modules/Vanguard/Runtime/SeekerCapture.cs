using HarmonyLib;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Points a radar missile at a decoy for real. Missile.SetTarget only changes the networked target id; ARH/SARH
    /// seekers keep homing on their own MissileSeeker.targetUnit (and proxy fuse), so both are moved as well —
    /// the same switch vanilla's home-on-jam path makes.
    /// </summary>
    internal static class SeekerCapture
    {
        private static readonly AccessTools.FieldRef<MissileSeeker, Unit> TargetUnit =
            AccessTools.FieldRefAccess<MissileSeeker, Unit>("targetUnit");

        public static void Retarget(Missile threat, Unit decoy)
        {
            threat.SetTarget(decoy);
            MissileSeeker seeker = threat.GetComponent<MissileSeeker>();
            if (seeker != null) TargetUnit(seeker) = decoy;
            if (decoy.rb != null) threat.SetProxyFuse(decoy.transform, decoy.rb);
        }

        /// <summary>Flares: the seeker loses its target outright and the missile flies on unguided.</summary>
        public static void Drop(Missile threat)
        {
            threat.SetTarget(null);
            MissileSeeker seeker = threat.GetComponent<MissileSeeker>();
            if (seeker != null) TargetUnit(seeker) = null;
        }

        /// <summary>
        /// Chaff. An ARH seeker drops into the game's own lost-target state (ARHSeeker.Seek flies on, SlowChecks ends the
        /// missile); any other radar seeker switches to the decoy while it lives. False when neither was possible.
        /// </summary>
        public static bool Blind(Missile threat, Unit decoy)
        {
            MissileSeeker seeker = threat.GetComponent<MissileSeeker>();
            if (seeker is ARHSeeker)
            {
                threat.SetTarget(null);
                TargetUnit(seeker) = null;
                return true;
            }
            if (decoy == null || decoy.disabled) return false;
            Retarget(threat, decoy);
            return true;
        }
    }
}
