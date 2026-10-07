using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Server: puts GLAIVE's UGVs on dry, flat ground 60 m short of the release point, 25 m apart.</summary>
    internal static class PayloadRelease
    {
        private static int serial;

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
            for (int i = 0; i < count; i++)
            {
                Vector3 desired = centre + right * ((i - (count - 1) * 0.5f) * 25f);
                if (!GroundPlacement.TryPlace(definition, desired, rotation, out Vector3 point)) continue;
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
