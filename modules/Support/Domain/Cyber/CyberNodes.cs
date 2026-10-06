using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Cyber
{
    /// <summary>Wire values: never renumber (core §13).</summary>
    internal enum NodeKind : byte { Radar = 0, SamC2 = 1, Relay = 2, Uplink = 3, DataCenter = 4 }

    /// <summary>One visible, targetable network node of an enemy faction. <see cref="Victim"/> is the faction key that owns the real thing behind it.</summary>
    internal readonly struct CyberNode
    {
        public readonly int Id, Victim;
        public readonly NodeKind Kind;
        public readonly float X, Z, FrontDistance;
        /// <summary>The native PersistentID.Id of the unit behind the node (0 for an airbase relay).</summary>
        public readonly uint UnitId;

        public CyberNode(int id, NodeKind kind, float x, float z, uint unitId, float frontDistance, int victim)
        {
            Id = id; Kind = kind; X = x; Z = z; UnitId = unitId; FrontDistance = frontDistance; Victim = victim;
        }
    }

    /// <summary>What the runtime reads out of a real enemy unit; the builder never sees the engine.</summary>
    internal enum SourceClass : byte { Other = 0, Radar = 1, SamRadar = 2, SamLauncher = 3 }

    internal readonly struct SourceUnit
    {
        public readonly uint UnitId;
        public readonly SourceClass Class;
        public readonly float X, Z, FrontDistance;
        public SourceUnit(uint unitId, SourceClass cls, float x, float z, float frontDistance)
        { UnitId = unitId; Class = cls; X = x; Z = z; FrontDistance = frontDistance; }
    }

    /// <summary>A node the runtime already knows the kind of (an enemy relay airbase, uplink site or data center).</summary>
    internal readonly struct SourcePoint
    {
        public readonly uint Key;
        public readonly NodeKind Kind;
        public readonly float X, Z, FrontDistance;
        public SourcePoint(uint key, NodeKind kind, float x, float z, float frontDistance)
        { Key = key; Kind = kind; X = x; Z = z; FrontDistance = frontDistance; }
    }

    internal readonly struct NodeSeed
    {
        public readonly NodeKind Kind;
        public readonly uint Key;
        public readonly float X, Z, FrontDistance;
        public NodeSeed(NodeKind kind, uint key, float x, float z, float frontDistance)
        { Kind = kind; Key = key; X = x; Z = z; FrontDistance = frontDistance; }
    }

    /// <summary>Spec §1.2: SAM C2 = a SAM radar plus up to 3 launchers within 3 km; RADAR = any other radar; points pass through.</summary>
    internal static class CyberNodeBuilder
    {
        public const float SamClusterRadius = 3000f;
        public const int SamClusterMax = 4, MaxUnits = 4096, MaxPoints = 64;

        public static void Build(IReadOnlyList<SourceUnit> units, IReadOnlyList<SourcePoint> points, List<NodeSeed> into)
        {
            into.Clear();
            if (units != null && units.Count <= MaxUnits)
            {
                var order = new List<SourceUnit>(units);
                order.Sort((a, b) => a.UnitId.CompareTo(b.UnitId));
                var claimed = new HashSet<uint>();
                for (int i = 0; i < order.Count; i++)
                {
                    SourceUnit seed = order[i];
                    if (seed.Class != SourceClass.SamRadar || !Finite(seed.X, seed.Z) || claimed.Contains(seed.UnitId)) continue;
                    claimed.Add(seed.UnitId);
                    for (int take = 1; take < SamClusterMax; take++)
                    {
                        int best = -1;
                        double bestD = (double)SamClusterRadius * SamClusterRadius;
                        for (int j = 0; j < order.Count; j++)
                        {
                            SourceUnit c = order[j];
                            if (c.Class != SourceClass.SamLauncher || claimed.Contains(c.UnitId) || !Finite(c.X, c.Z)) continue;
                            double dx = (double)c.X - seed.X, dz = (double)c.Z - seed.Z, d = dx * dx + dz * dz;
                            if (d < bestD || (best < 0 && d == bestD)) { best = j; bestD = d; }
                        }
                        if (best < 0) break;
                        claimed.Add(order[best].UnitId);
                    }
                    into.Add(new NodeSeed(NodeKind.SamC2, seed.UnitId, seed.X, seed.Z, seed.FrontDistance));
                }
                for (int i = 0; i < order.Count; i++)
                    if (order[i].Class == SourceClass.Radar && Finite(order[i].X, order[i].Z))
                        into.Add(new NodeSeed(NodeKind.Radar, order[i].UnitId, order[i].X, order[i].Z, order[i].FrontDistance));
            }
            if (points == null || points.Count > MaxPoints) return;
            for (int i = 0; i < points.Count; i++)
                if (Finite(points[i].X, points[i].Z))
                    into.Add(new NodeSeed(points[i].Kind, points[i].Key, points[i].X, points[i].Z, points[i].FrontDistance));
        }

        private static bool Finite(float x, float z) => !float.IsNaN(x) && !float.IsInfinity(x) && !float.IsNaN(z) && !float.IsInfinity(z);
    }

    /// <summary>Small opaque node ids per faction: (kind, key) maps to the same id for the whole mission. 0 is never an id.</summary>
    internal sealed class NodeIdTable
    {
        public const int Capacity = 256;
        private readonly Dictionary<(NodeKind, uint), int> ids = new Dictionary<(NodeKind, uint), int>();
        private readonly Dictionary<int, (NodeKind, uint)> keys = new Dictionary<int, (NodeKind, uint)>();
        private int next;

        public int Count => ids.Count;

        public int GetOrAdd(NodeKind kind, uint key)
        {
            if (ids.TryGetValue((kind, key), out int id)) return id;
            if (ids.Count >= Capacity) return 0;
            id = ++next;
            ids.Add((kind, key), id);
            keys.Add(id, (kind, key));
            return id;
        }

        public bool TryGet(NodeKind kind, uint key, out int id) => ids.TryGetValue((kind, key), out id);

        public bool TryKey(int id, out NodeKind kind, out uint key)
        {
            if (keys.TryGetValue(id, out var pair)) { kind = pair.Item1; key = pair.Item2; return true; }
            kind = default; key = 0;
            return false;
        }

        public void Clear() { ids.Clear(); keys.Clear(); }
    }

    /// <summary>Fog: a node is visible while a real sighting is fresh, while it is held, and briefly after a release. Mission seconds.</summary>
    internal sealed class NodeReveal
    {
        public const float VisibleSeconds = 90f, AfterReleaseSeconds = 30f;
        public const int Capacity = NodeIdTable.Capacity;
        private readonly Dictionary<int, float> seen = new Dictionary<int, float>();
        private readonly Dictionary<int, float> linger = new Dictionary<int, float>();

        public void Note(int id, float now)
        {
            if (id <= 0 || float.IsNaN(now) || float.IsInfinity(now)) return;
            if (!seen.ContainsKey(id) && seen.Count >= Capacity) return;
            seen[id] = now;
        }

        public void Linger(int id, float now)
        {
            if (id <= 0 || float.IsNaN(now) || float.IsInfinity(now)) return;
            if (!linger.ContainsKey(id) && linger.Count >= Capacity) return;
            linger[id] = now + AfterReleaseSeconds;
        }

        public bool Visible(int id, float now, bool held)
        {
            if (held) return true;
            if (seen.TryGetValue(id, out float at) && now >= at && now - at < VisibleSeconds) return true;
            return linger.TryGetValue(id, out float until) && now < until;
        }

        /// <summary>Drops every stamp of <paramref name="id"/> (the id was released and may name another target later).</summary>
        public void Forget(int id) { seen.Remove(id); linger.Remove(id); }

        public void Clear() { seen.Clear(); linger.Clear(); }
    }
}
