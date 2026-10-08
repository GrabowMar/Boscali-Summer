using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Server: puts GLAIVE's UGVs on the nearest dry, flat ground around a point 60 m short of the release point, 20 m apart.</summary>
    internal static class PayloadRelease
    {
        private static int serial;
        private static readonly List<Vector2> Candidates = CarrierProfile.DropCandidates();

        /// <returns>How many vehicles were actually placed (0 over water or steep ground).</returns>
        public static int Drop(string unitKey, GlobalPosition over, Vector3 heading, FactionHQ hq, int count)
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (hq == null || unitKey == null || spawner == null || !spawner.IsServer ||
                !Encyclopedia.Lookup.TryGetValue(unitKey, out UnitDefinition definition)) return 0;
            Vector3 fwd = new Vector3(heading.x, 0f, heading.z);
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            Quaternion rotation = Quaternion.LookRotation(fwd);
            Vector3 centre = over.ToLocalPosition() - fwd * 60f;
            int placed = 0;
            var taken = new List<Vector2>(count);
            for (int i = 0; i < Candidates.Count && placed < count; i++)
            {
                Vector2 c = Candidates[i];
                if (!CarrierProfile.Spaced(c, taken)) continue;
                Vector3 desired = centre + right * c.x + fwd * c.y;
                if (!GroundPlacement.TryPlace(definition, desired, rotation, out Vector3 point)) continue;
                taken.Add(c);
                Unit unit = spawner.SpawnVehicle(definition.unitPrefab, point.ToGlobalPosition(), rotation, Vector3.zero, hq,
                    "VG_UGV_" + (++serial), 1f, false, null);
                if (unit == null) continue;
                PayloadLifetime.Track(unit);
                VanguardStats.UgvsSpawned++;
                placed++;
            }
            return placed;
        }
    }
}
