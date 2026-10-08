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
    }
}
