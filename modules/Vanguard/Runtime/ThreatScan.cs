using System.Collections.Generic;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Server: missiles currently tracking a unit, straight from the unit registry. MissileWarning.knownMissiles is fed
    /// by a networked target-id hook and is not a reliable server-side source for client-flown aircraft.
    /// </summary>
    internal static class ThreatScan
    {
        public static void Inbound(Unit host, List<Missile> into)
        {
            into.Clear();
            if (host == null) return;
            List<Unit> units = UnitRegistry.allUnits;
            for (int i = 0; i < units.Count; i++)
                if (units[i] is Missile m && !m.disabled && m.targetID == host.persistentID) into.Add(m);
        }
    }
}
