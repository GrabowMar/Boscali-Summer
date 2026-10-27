using System;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// XQ-90 MANTA, the ground striker (host only): its scan and its round; the rest of the pipeline is the REMORA's (RemoraFlight).
    /// Targets rank the pilot's ground designation, a ground launcher whose missile homes on the launcher (SEAD), then, when sent to a
    /// point, the nearest hostile vehicle or ship around it (<see cref="GroundCue"/>). Rounds are a vanilla fire-and-forget AGM.
    /// </summary>
    internal sealed partial class VanguardFlight
    {
        // Preferred AGM; any vanilla OpticalSeeker missile stands in when the key is missing (game update).
        private const string StrikeRoundKey = "AGM1";
        private static MissileDefinition strikeRound;

        // 2 Hz ground picture: ground launchers whose missiles home on the launcher, and hostile vehicles / ships near the sent point.
        private void ScanGround(GlobalPosition pos, GlobalPosition? area)
        {
            droneScan.Clear();
            FactionHQ mine = missile.NetworkHQ;
            ThreatScan.Inbound(launcher, inbound);
            for (int i = 0; i < inbound.Count; i++)
            {
                Unit shooter = inbound[i].owner;
                if (!IsGroundTarget(shooter) || !Hostile(shooter, mine)) continue;
                float range = FastMath.Distance(shooter.GlobalPosition(), pos);
                if (range <= MantaRules.ShooterRange) droneScan.Add(new DroneTarget { Unit = shooter, Cue = (byte)GroundCue.Shooter, Range = range });
            }
            if (!area.HasValue) return;
            BattlefieldGrid.GetUnitsInRangeNonAlloc(area.Value, MantaRules.AreaRange, droneGrid);
            for (int i = 0; i < droneGrid.Count; i++)
                if (IsGroundTarget(droneGrid[i]) && Hostile(droneGrid[i], mine))
                    droneScan.Add(new DroneTarget { Unit = droneGrid[i], Cue = (byte)GroundCue.Area, Range = FastMath.Distance(droneGrid[i].GlobalPosition(), pos) });
        }

        // A MANTA target: anything on the ground or the sea that is not an aircraft or a missile (vehicles, SAM sites, ships, buildings).
        private static bool IsGroundTarget(Unit unit) => unit != null && !(unit is Aircraft) && !(unit is Missile);

        private static MissileDefinition StrikeRound()
        {
            if (strikeRound != null) return strikeRound;
            if (Encyclopedia.Lookup != null && Encyclopedia.Lookup.TryGetValue(StrikeRoundKey, out UnitDefinition def) &&
                def is MissileDefinition agm && agm.unitPrefab != null && agm.unitPrefab.GetComponent<OpticalSeeker>() != null) return Picked(agm);
            Encyclopedia e = Encyclopedia.i;
            if (e == null) return null;
            MissileDefinition any = null;
            foreach (MissileDefinition m in e.missiles)
            {
                if (m == null || m.unitPrefab == null || m.jsonKey == null || m.jsonKey.StartsWith("VG_", StringComparison.Ordinal) ||
                    m.unitPrefab.GetComponent<OpticalSeeker>() == null) continue;
                if (m.jsonKey.StartsWith("AGM", StringComparison.Ordinal)) return Picked(m);
                any = any ?? m;
            }
            return any != null ? Picked(any) : null;
        }

        private static MissileDefinition Picked(MissileDefinition m)
        {
            Plugin.Logger?.LogInfo("[Vanguard] MANTA strike round: " + m.jsonKey);
            return strikeRound = m;
        }
    }
}
