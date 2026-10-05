using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;

namespace BoscaliSummer.Modules.Support.Domain.Sof
{
    /// <summary>Spec 2.1 camp numbers. A camp reuses the CYBER anchor health words, grace and restore bar; only the goal and the effects differ.</summary>
    internal static class CampRules
    {
        public const int MaxCamps = 2;
        public const float RebuildGoal = 120f, RaiseDamagedFactor = 1.5f, MinAirbaseMetres = 6000f;

        /// <summary>A damaged camp raises teams 1.5x slower; a down camp raises none; teams already in the field carry on.</summary>
        public static float RaiseFactor(AnchorHealth health) => health == AnchorHealth.Damaged ? RaiseDamagedFactor : 1f;
    }

    /// <summary>One faction's SOF camps: health, position, grace and the restore bar. Pure state the host runtime measures into.</summary>
    internal sealed class SofCampSet
    {
        private sealed class Slot
        {
            public AnchorHealth Health = AnchorHealth.Live;
            public float X, Z, DownSince = -1f;
            public bool HasPosition;
        }

        private readonly Slot[] slots;

        public SofCampSet(int camps)
        {
            slots = new Slot[Math.Max(0, Math.Min(CampRules.MaxCamps, camps))];
            for (int i = 0; i < slots.Length; i++) slots[i] = new Slot();
        }

        public int Count => slots.Length;

        public AnchorHealth Health(int index) => index >= 0 && index < slots.Length ? slots[index].Health : AnchorHealth.Down;

        public int LiveCount()
        {
            int n = 0;
            for (int i = 0; i < slots.Length; i++) if (slots[i].Health != AnchorHealth.Down) n++;
            return n;
        }

        /// <summary>A measured camp: health fraction and the down flag, like the CYBER anchors. Down stamps the grace clock once.</summary>
        public void Set(int index, float healthFraction, bool down, float now)
        {
            if (index < 0 || index >= slots.Length) return;
            Slot s = slots[index];
            AnchorHealth next = AnchorRules.Of(healthFraction, down);
            if (next == AnchorHealth.Down && s.Health != AnchorHealth.Down) s.DownSince = now;
            if (next != AnchorHealth.Down) s.DownSince = -1f;
            s.Health = next;
        }

        public void SetPosition(int index, float x, float z)
        {
            if (index < 0 || index >= slots.Length) return;
            slots[index].X = x; slots[index].Z = z; slots[index].HasPosition = true;
        }

        public bool TryPosition(int index, out float x, out float z)
        {
            x = z = 0f;
            if (index < 0 || index >= slots.Length || !slots[index].HasPosition) return false;
            x = slots[index].X; z = slots[index].Z;
            return true;
        }

        public bool PastGrace(int index, float now) =>
            index >= 0 && index < slots.Length && slots[index].Health == AnchorHealth.Down && slots[index].DownSince >= 0f && now - slots[index].DownSince >= AnchorRules.GraceSeconds;

        public void Restore(int index, float x, float z, float now)
        {
            if (index < 0 || index >= slots.Length) return;
            slots[index].Health = AnchorHealth.Live; slots[index].DownSince = -1f;
            SetPosition(index, x, z);
        }

        /// <summary>The camp teams are raised at: the first live one, else a damaged one. False when none stands.</summary>
        public bool TryBest(out int index, out float x, out float z, out AnchorHealth health)
        {
            index = -1; x = z = 0f; health = AnchorHealth.Down;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < slots.Length; i++)
                {
                    AnchorHealth want = pass == 0 ? AnchorHealth.Live : AnchorHealth.Damaged;
                    if (slots[i].Health != want || !slots[i].HasPosition) continue;
                    index = i; x = slots[i].X; z = slots[i].Z; health = want;
                    return true;
                }
            return false;
        }

        /// <summary>Distance from a point to the nearest standing camp (metres); +infinity when none stands.</summary>
        public float NearestStanding(float px, float pz, out float bx, out float bz)
        {
            float best = float.PositiveInfinity; bx = px; bz = pz;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Health == AnchorHealth.Down || !slots[i].HasPosition) continue;
                float d = SofRules.Distance(px, pz, slots[i].X, slots[i].Z);
                if (d < best) { best = d; bx = slots[i].X; bz = slots[i].Z; }
            }
            return best;
        }

        public void Clear()
        {
            for (int i = 0; i < slots.Length; i++) { slots[i].Health = AnchorHealth.Live; slots[i].DownSince = -1f; slots[i].HasPosition = false; }
        }
    }

    /// <summary>Opaque target ids (like <see cref="NodeIdTable"/>): a client only ever holds an id, never a unit key. Capacity bounded.</summary>
    internal sealed class SofTargetIds
    {
        public const int Capacity = 128;
        private readonly Dictionary<ulong, int> byKey = new Dictionary<ulong, int>();
        private readonly List<ulong> keys = new List<ulong>();

        public int Count => keys.Count;

        private static ulong Pack(TargetKind kind, AnchorSub sub, uint key) => ((ulong)(byte)kind << 40) | ((ulong)(byte)sub << 32) | key;

        /// <summary>The id of (kind, sub, key); 0 when the table is full.</summary>
        public int GetOrAdd(TargetKind kind, AnchorSub sub, uint key)
        {
            ulong k = Pack(kind, sub, key);
            if (byKey.TryGetValue(k, out int id)) return id;
            if (keys.Count >= Capacity) return 0;
            keys.Add(k);
            id = keys.Count;
            byKey[k] = id;
            return id;
        }

        public bool TryKey(int id, out TargetKind kind, out AnchorSub sub, out uint key)
        {
            kind = default; sub = default; key = 0;
            if (id <= 0 || id > keys.Count) return false;
            ulong k = keys[id - 1];
            kind = (TargetKind)(byte)(k >> 40); sub = (AnchorSub)(byte)(k >> 32); key = (uint)k;
            return true;
        }

        public void Clear() { byKey.Clear(); keys.Clear(); }
    }

    /// <summary>A real thing the runtime looked at this refresh, with its fog verdict. Fog: <see cref="Sighted"/> is a fresh native sighting, never host truth.</summary>
    internal readonly struct SofSeed
    {
        public readonly TargetKind Kind;
        public readonly AnchorSub Sub;
        public readonly uint Key;
        public readonly float X, Z, Front;
        public readonly bool Sighted, Gone;

        public SofSeed(TargetKind kind, AnchorSub sub, uint key, float x, float z, float front, bool sighted, bool gone)
        { Kind = kind; Sub = sub; Key = key; X = x; Z = z; Front = front; Sighted = sighted; Gone = gone; }
    }

    internal readonly struct SofTarget
    {
        public readonly int Id;
        public readonly TargetKind Kind;
        public readonly AnchorSub Sub;
        public readonly uint Key;
        public readonly float X, Z, Front;

        public SofTarget(int id, TargetKind kind, AnchorSub sub, uint key, float x, float z, float front)
        { Id = id; Kind = kind; Sub = sub; Key = key; X = x; Z = z; Front = front; }
    }
}
