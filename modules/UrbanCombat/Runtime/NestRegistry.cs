using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Client-side nest registry: every peer learns the live rooftop nests from the
    /// replicated nest names when they spawn, so remote clients can guard strongpoint
    /// shells, read zone tiers and dress the warzone without scene scans. Bounded to
    /// the 96-nest theater ceiling; dead nests prune lazily on read and on the slow
    /// dressing tick.
    /// </summary>
    internal static class NestRegistry
    {
        private sealed class Entry
        {
            public Building Nest;
            public GameObject Shell;
            public string Zone;
            public int Tier;
            public UnitPart Dugout;
        }

        private const int MaxEntries = 96;

        private static readonly Dictionary<int, Entry> byNest = new Dictionary<int, Entry>(MaxEntries);
        private static readonly Dictionary<int, Entry> byShell = new Dictionary<int, Entry>(MaxEntries);
        private static readonly Dictionary<string, int> zonePeaks = new Dictionary<string, int>();
        private static readonly RaycastHit[] rayHits = new RaycastHit[4];

        /// <summary>
        /// Records a nest from its replicated name plus one downward raycast to its
        /// shell. Called for every nest on every peer: directly at spawn on the server,
        /// from the client-visual patch everywhere else. Idempotent per nest.
        /// </summary>
        public static void Add(Building nest)
        {
            if (nest == null) return;
            string name = nest.NetworkUniqueName;
            if (string.IsNullOrEmpty(name) ||
                !name.StartsWith(RooftopPlacement.NamePrefix, System.StringComparison.Ordinal))
                return;
            int nestId = nest.GetInstanceID();
            if (byNest.Count >= MaxEntries && !byNest.ContainsKey(nestId))
            {
                Prune();
                if (byNest.Count >= MaxEntries) return;
            }

            GarrisonMarkerInfo.TryReadZone(name, ZoneGarrisonManager.NamePrefix, out string zone);
            if (!GarrisonMarkerInfo.TryReadTier(name, out int tier)) tier = 0;
            var entry = new Entry
            {
                Nest = nest,
                Zone = zone,
                Tier = tier,
                Shell = ResolveShell(nest),
                Dugout = ResolveDugout(nest)
            };
            if (byNest.TryGetValue(nestId, out Entry previous) &&
                previous.Shell != null && previous.Shell != entry.Shell)
                byShell.Remove(previous.Shell.GetInstanceID());
            byNest[nestId] = entry;
            if (entry.Shell != null)
                byShell[entry.Shell.GetInstanceID()] = entry;
            if (zone != null)
            {
                int live = 0;
                foreach (KeyValuePair<int, Entry> candidate in byNest)
                    if (candidate.Value.Nest != null && zone.Equals(candidate.Value.Zone, System.StringComparison.Ordinal))
                        live++;
                if (!zonePeaks.TryGetValue(zone, out int peak) || live > peak)
                    zonePeaks[zone] = live;
            }
        }

        /// <summary>O(1) strongpoint-shell test for the client damage guard.</summary>
        public static bool IsStrongpointShell(MapBuilding shell)
        {
            if (shell == null) return false;
            GameObject go = shell.gameObject;
            if (go == null) return false;
            int id = go.GetInstanceID();
            if (!byShell.TryGetValue(id, out Entry entry)) return false;
            if (entry.Shell == null)
            {
                byShell.Remove(id);
                return false;
            }
            // Reference check, not id alone: a recycled instance id must never guard a
            // building that was never garrisoned.
            return ReferenceEquals(entry.Shell, go);
        }

        public static int TierForZone(string zone)
        {
            if (string.IsNullOrEmpty(zone)) return 0;
            int best = 0;
            foreach (KeyValuePair<int, Entry> candidate in byNest)
            {
                if (candidate.Value.Nest == null) continue;
                if (!zone.Equals(candidate.Value.Zone, System.StringComparison.Ordinal)) continue;
                if (candidate.Value.Tier > best) best = candidate.Value.Tier;
            }
            return best;
        }

        public static int IntactCount(string zone, FactionHQ owner)
        {
            if (string.IsNullOrEmpty(zone) || owner == null) return 0;
            int intact = 0;
            foreach (KeyValuePair<int, Entry> candidate in byNest)
            {
                Entry entry = candidate.Value;
                if (entry.Nest == null || entry.Nest.disabled) continue;
                if (!zone.Equals(entry.Zone, System.StringComparison.Ordinal)) continue;
                if (entry.Nest.NetworkHQ != owner) continue;
                if (StrongpointHitPolicy.DugoutStage(DugoutHitPoints(entry)) >= 3) continue;
                intact++;
            }
            return intact;
        }

        /// <summary>Live-over-peak zone health for the marking poll. 1 when unknown.</summary>
        public static float ZoneHealth(string zone)
        {
            if (string.IsNullOrEmpty(zone)) return 1f;
            int live = 0;
            foreach (KeyValuePair<int, Entry> candidate in byNest)
            {
                if (candidate.Value.Nest == null || candidate.Value.Nest.disabled) continue;
                if (zone.Equals(candidate.Value.Zone, System.StringComparison.Ordinal)) live++;
            }
            if (!zonePeaks.TryGetValue(zone, out int peak) || peak < 1) return 1f;
            return Mathf.Clamp01(live / (float)peak);
        }

        /// <summary>Fills the destination with live nests for the dressing rebuild.</summary>
        public static int CopyLiveNests(List<Building> dest, int cap)
        {
            if (dest == null || cap <= 0) return 0;
            int count = 0;
            foreach (KeyValuePair<int, Entry> candidate in byNest)
            {
                if (count >= cap) break;
                Building nest = candidate.Value.Nest;
                if (nest == null || nest.disabled) continue;
                if (!nest.gameObject.scene.IsValid()) continue;
                dest.Add(nest);
                count++;
            }
            return count;
        }

        public static void Prune()
        {
            var deadNests = new List<int>();
            foreach (KeyValuePair<int, Entry> candidate in byNest)
                if (candidate.Value.Nest == null) deadNests.Add(candidate.Key);
            for (int i = 0; i < deadNests.Count; i++)
            {
                if (byNest.TryGetValue(deadNests[i], out Entry entry) && entry.Shell != null)
                    byShell.Remove(entry.Shell.GetInstanceID());
                byNest.Remove(deadNests[i]);
            }
            var deadShells = new List<int>();
            foreach (KeyValuePair<int, Entry> candidate in byShell)
                if (candidate.Value.Shell == null) deadShells.Add(candidate.Key);
            for (int i = 0; i < deadShells.Count; i++) byShell.Remove(deadShells[i]);
            var emptyZones = new List<string>();
            foreach (KeyValuePair<string, int> peak in zonePeaks)
            {
                bool any = false;
                foreach (KeyValuePair<int, Entry> candidate in byNest)
                {
                    if (candidate.Value.Nest == null) continue;
                    if (peak.Key.Equals(candidate.Value.Zone, System.StringComparison.Ordinal)) { any = true; break; }
                }
                if (!any) emptyZones.Add(peak.Key);
            }
            for (int i = 0; i < emptyZones.Count; i++) zonePeaks.Remove(emptyZones[i]);
        }

        public static void Reset()
        {
            byNest.Clear();
            byShell.Clear();
            zonePeaks.Clear();
        }

        private static float DugoutHitPoints(Entry entry) =>
            entry.Dugout != null ? entry.Dugout.hitPoints : 100f;

        private static GameObject ResolveShell(Building nest)
        {
            Vector3 origin = nest.transform.position + Vector3.up * 10f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, 40f,
                PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = rayHits[i].collider;
                if (collider == null) continue;
                Transform hit = collider.transform;
                if (hit == nest.transform || hit.IsChildOf(nest.transform)) continue;
                MapBuilding shell = collider.GetComponentInParent<MapBuilding>();
                if (shell != null) return shell.gameObject;
            }
            return null;
        }

        private static UnitPart ResolveDugout(Building nest)
        {
            Transform dugout = nest.transform.Find("dugout");
            if (dugout != null)
            {
                UnitPart part = dugout.GetComponent<UnitPart>();
                if (part != null) return part;
            }
            UnitPart[] parts = nest.GetComponentsInChildren<UnitPart>(true);
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null &&
                    parts[i].name.IndexOf("dugout", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return parts[i];
            return null;
        }
    }
}
