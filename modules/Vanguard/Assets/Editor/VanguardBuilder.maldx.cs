// MALD-X BAIT asset tweaks on top of the shared Missile() build (see VanguardBuilder.Run).
using UnityEngine;

namespace Vanguard
{
    public static partial class VanguardBuilder
    {
        // Enemy air defence has to want to shoot the bait. Missile.InterceptPriority returns 0 for a missile with no
        // target (or a missile target) unless WeaponInfo.blastDamage >= 500; for missiles that field is only read there
        // (the real warhead is Missile.blastYield = Spec.Yield = 1). A fighter-like air identity makes every anti-air
        // weapon rate it in CombatAI.CalcOpportunityThreat. Signature (radarSize 3.0) is the Spec row.
        static void MaldxIdentity(WeaponInfo info)
        {
            var def = Load<MissileDefinition>(R + "Def_VG_MaldX.asset");
            Edit(info, s => P(s, "blastDamage").floatValue = 500f);
            Edit(def, s => P(s, "typeIdentity.air").floatValue = 1f);
            Debug.Log("[Vanguard] MALDX_BAIT_IDENTITY radarSize=" + def.radarSize + " interceptFlag=500 air=1");
        }
    }
}
