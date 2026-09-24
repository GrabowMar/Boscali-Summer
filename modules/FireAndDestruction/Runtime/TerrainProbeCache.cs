using System.Collections.Generic;
using BoscaliSummer.Core;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Shared ground probe cache for the fire pipeline. Forest spread candidates, ignition
    /// snaps and scorch decals all ask the same question about the same terrain — where is
    /// the ground here — so the answer is kept per 24 m cell and reused until the scene
    /// resets. The fire front is the biggest raycast source in the mod; this turns most of
    /// those casts into dictionary lookups.
    ///
    /// ponytail: height and normal only — a building destroyed under a cached cell leaves a
    /// stale roof height until the next scene reset. Clear the cache if fire-on-rubble ever
    /// needs to be exact.
    /// </summary>
    internal static class TerrainProbeCache
    {
        private const float CellSize = 24f;
        private const int MaximumEntries = 4096;
        private const float ProbeHeight = 120f;
        private const float ProbeRange = 400f;

        private struct Probe
        {
            public bool Ok;
            public GlobalPosition Point;
            public Vector3 Normal;
        }

        private static readonly Dictionary<long, Probe> probes = new Dictionary<long, Probe>(256);

        public static void Clear() => probes.Clear();

        public static bool TryProbe(GlobalPosition position, out GlobalPosition point, out Vector3 normal)
        {
            long key = Deterministic.CellKey(position.x, position.z, CellSize);
            if (probes.TryGetValue(key, out Probe cached))
            {
                point = cached.Point;
                normal = cached.Normal;
                return cached.Ok;
            }

            Vector3 local = position.ToLocalPosition();
            Probe probe = default;
            if (Physics.Raycast(local + Vector3.up * ProbeHeight, Vector3.down, out RaycastHit hit,
                    ProbeRange, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
            {
                probe.Ok = true;
                probe.Point = hit.point.ToGlobalPosition();
                probe.Normal = hit.normal;
            }

            if (probes.Count >= MaximumEntries) probes.Clear();
            probes[key] = probe;
            point = probe.Point;
            normal = probe.Normal;
            return probe.Ok;
        }
    }
}
