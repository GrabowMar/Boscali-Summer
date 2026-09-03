using System;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Pure mathematical policy for stronghold armor mitigation.
    /// Free of UnityEngine types so it is directly testable without an engine runner.
    /// </summary>
    internal static class StrongholdDamagePolicy
    {
        public static float CalculateMitigatedDamage(
            float pierceDamage,
            float blastDamage,
            float amountAffected,
            float fireDamage,
            float impactDamage,
            float pierceArmor,
            float blastArmor)
        {
            float effPierceArmor = Math.Max(0f, pierceArmor);
            float effBlastArmor = Math.Max(0f, blastArmor);
            float effAmount = Math.Max(0f, amountAffected);

            float netPierce = Math.Max(0f, pierceDamage - effPierceArmor) / Math.Max(effPierceArmor * 0.5f, 1f);
            float netBlast = (Math.Max(0f, blastDamage - effBlastArmor) * effAmount) / Math.Max(effBlastArmor * 0.5f, 1f);
            float netFire = Math.Max(0f, fireDamage - 15f) * 0.5f;
            float netImpact = Math.Max(0f, impactDamage);

            return netPierce + netBlast + netFire + netImpact;
        }
    }
}
