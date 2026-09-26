using System.Collections.Generic;
using BoscaliSummer.Features.Intel.Domain;

namespace BoscaliSummer.Features.Intel.Runtime
{
    /// <summary>
    /// The one pre-war pass (see PreWarRules): every live, mission-placed, static air-defence
    /// installation of a non-neutral faction goes to every OTHER faction's picture not yet
    /// seeded. Mission-placed means linked to a SavedUnit (host) or carrying the mission's
    /// unique name (a replicated SyncVar, so a client can tell too). At most 4096 units.
    /// </summary>
    internal static class PreWarSeeder
    {
        public const int MaximumUnitScan = 4096;

        public static int Seed(FactionPicture[] pictures, int pictureCount, UnitProfiles profiles, float level)
        {
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null) return 0;
            int seeded = 0;
            int limit = all.Count < MaximumUnitScan ? all.Count : MaximumUnitScan;
            for (int i = 0; i < limit; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled) continue;
                FactionHQ owner = unit.NetworkHQ;
                if (owner == null || owner.faction == null ||
                    FactionHelper.EmptyOrNoFactionOrNeutral(owner.faction.factionName))
                    continue;
                UnitClass unitClass = UnitProfiles.ClassOf(unit);
                bool isStatic = FactionPicture.IsStatic(unit, unitClass);
                bool missionPlaced = unit.SavedUnit != null || !string.IsNullOrEmpty(unit.UniqueName);
                if (!missionPlaced || !isStatic) continue;
                short index = profiles.IndexOf(unit);
                if (index < 0 || !PreWarRules.Qualifies(missionPlaced, isStatic, unitClass, profiles[index].Air.IsAirDefence))
                    continue;
                GlobalPosition at = unit.GlobalPosition();
                for (int p = 0; p < pictureCount; p++)
                {
                    FactionPicture picture = pictures[p];
                    if (picture.PreWarSeeded || picture.Hq == null || ReferenceEquals(picture.Hq, owner)) continue;
                    picture.SeedPreWar(unit.persistentID.Id, at.x, at.z, unitClass, profiles[index].Role, index, level);
                    seeded++;
                }
            }
            return seeded;
        }
    }
}
