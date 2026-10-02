using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Intel.Domain
{
    /// <summary>The unit families Intel tells apart. Scenery, containers and pilots are <see cref="Other"/>.</summary>
    internal enum UnitClass : byte
    {
        Other = 0,
        Aircraft = 1,
        GroundVehicle = 2,
        Ship = 3,
        Building = 4,
        Missile = 5
    }

    internal enum UpsertResult : byte
    {
        Added = 0,
        Updated = 1,
        Ignored = 2,
        Evicted = 3,
        Refused = 4
    }

    /// <summary>What the faction's own tracking is allowed to put in its known-hostile list.</summary>
    internal static class KnownHostileFilter
    {
        public const byte PriorityStatic = 0;
        public const byte PriorityMobile = 1;
        public const byte PriorityAirDefence = 2;

        /// <summary>Re-seed from trackingDatabase once the event-counted size drifts further than this.</summary>
        public const int ReseedDrift = 16;

        /// <summary>At most this many trackingDatabase entries are enumerated per seed.</summary>
        public const int MaximumSeedEnumeration = 1024;

        /// <summary>
        /// Missiles are launch memory and scenery is scenery; a building counts only when it can
        /// shoot or see (an emplacement or a radar station).
        /// </summary>
        public static bool Admits(UnitClass unitClass, bool hasWeaponOrRadar)
        {
            switch (unitClass)
            {
                case UnitClass.Aircraft:
                case UnitClass.GroundVehicle:
                case UnitClass.Ship:
                    return true;
                case UnitClass.Building:
                    return hasWeaponOrRadar;
                default:
                    return false;
            }
        }

        /// <summary>Only surface units form air-defence rings or leave launch memory.</summary>
        public static bool IsSurface(UnitClass unitClass) =>
            unitClass == UnitClass.GroundVehicle || unitClass == UnitClass.Ship || unitClass == UnitClass.Building;

        public static byte Priority(bool airDefence, bool isStatic) =>
            airDefence ? PriorityAirDefence : isStatic ? PriorityStatic : PriorityMobile;

        /// <summary>
        /// Vanilla's late-join batch (RpcGetTrackingStateBatched → SetTrackingState) fills
        /// trackingDatabase without firing onDiscoverUnit, so the event-counted size drifts.
        /// </summary>
        public static bool NeedsReseed(int expected, int actual) => Math.Abs(actual - expected) > ReseedDrift;
    }

    /// <summary>One hostile unit as the observing faction knows it.</summary>
    internal struct KnownHostile
    {
        public uint Id;
        public float X;
        public float Z;
        /// <summary>Time.timeSinceLevelLoad of the last sighting (a pre-war seed: the seeding time).</summary>
        public float SpottedAt;
        public byte Priority;
        public bool Static;
        public UnitClass Class;
        public ForceRole Role;
        /// <summary>Index into the runtime profile cache; -1 when unknown.</summary>
        public short Profile;
        public bool PreWar;
        /// <summary>Has been in the observer's own tracking database.</summary>
        public bool Confirmed;
        public bool Emitting;
        /// <summary>When an unconfirmed pre-war site was first found dead; NaN while alive.</summary>
        public float DeadSince;
    }

    /// <summary>
    /// The known-hostile list of one faction, capped at 768. When full, the oldest static
    /// non-air-defence entry gives way first; an air-defence, radar or mobile unit is never
    /// refused while a lower-priority entry exists, and within one priority a newer sighting
    /// replaces the oldest.
    /// </summary>
    internal sealed class KnownHostileTable
    {
        public const int DefaultCapacity = 768;

        private readonly KnownHostile[] entries;
        private readonly int[] touched;
        private readonly Dictionary<uint, int> slots;
        private int count;
        private int generation;

        public KnownHostileTable(int capacity = DefaultCapacity)
        {
            if (capacity < 1) capacity = 1;
            entries = new KnownHostile[capacity];
            touched = new int[capacity];
            slots = new Dictionary<uint, int>(capacity);
        }

        public int Count => count;

        public int Capacity => entries.Length;

        public ref KnownHostile this[int index] => ref entries[index];

        public bool TryFind(uint id, out int slot) => slots.TryGetValue(id, out slot);

        public UpsertResult Upsert(in KnownHostile incoming)
        {
            if (slots.TryGetValue(incoming.Id, out int slot))
            {
                touched[slot] = generation;
                ref KnownHostile existing = ref entries[slot];
                // Live tracking outranks a pre-war seed: a seed never overwrites what was seen.
                if (incoming.PreWar && !existing.PreWar) return UpsertResult.Ignored;
                bool preWar = existing.PreWar || incoming.PreWar;
                bool confirmed = existing.Confirmed || incoming.Confirmed;
                existing = incoming;
                existing.PreWar = preWar;
                existing.Confirmed = confirmed;
                return UpsertResult.Updated;
            }
            if (count < entries.Length)
            {
                Place(count++, incoming);
                return UpsertResult.Added;
            }
            int victim = LowestRankOldest();
            ref KnownHostile weakest = ref entries[victim];
            if (weakest.Priority > incoming.Priority ||
                (weakest.Priority == incoming.Priority && !(weakest.SpottedAt < incoming.SpottedAt)))
                return UpsertResult.Refused;
            slots.Remove(weakest.Id);
            Place(victim, incoming);
            return UpsertResult.Evicted;
        }

        public bool Remove(uint id)
        {
            if (!slots.TryGetValue(id, out int slot)) return false;
            slots.Remove(id);
            int last = --count;
            if (slot != last)
            {
                entries[slot] = entries[last];
                touched[slot] = touched[last];
                slots[entries[slot].Id] = slot;
            }
            entries[last] = default;
            return true;
        }

        /// <summary>Starts a re-seed: every entry the seed upserts is marked seen.</summary>
        public void BeginSweep() => generation++;

        /// <summary>
        /// Removes tracked entries a completed re-seed did not see again. Unconfirmed pre-war
        /// intel is not tracking, so it stays.
        /// </summary>
        public int SweepUnseenConfirmed()
        {
            int removed = 0;
            for (int i = count - 1; i >= 0; i--)
            {
                if (!entries[i].Confirmed || touched[i] == generation) continue;
                Remove(entries[i].Id);
                removed++;
            }
            return removed;
        }

        public void Clear()
        {
            Array.Clear(entries, 0, count);
            slots.Clear();
            count = 0;
        }

        private void Place(int slot, in KnownHostile entry)
        {
            entries[slot] = entry;
            touched[slot] = generation;
            slots[entry.Id] = slot;
        }

        private int LowestRankOldest()
        {
            int best = 0;
            for (int i = 1; i < count; i++)
            {
                if (entries[i].Priority < entries[best].Priority ||
                    (entries[i].Priority == entries[best].Priority && entries[i].SpottedAt < entries[best].SpottedAt))
                    best = i;
            }
            return best;
        }
    }
}
