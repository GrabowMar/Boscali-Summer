using BoscaliSummer.Features.Intel.Domain;

namespace BoscaliSummer.Tests.Features.Intel
{
    /// <summary>
    /// The faction's own list of hostiles: what is admitted, what gives way when it is full,
    /// and how a late-join batch that fired no events is recovered.
    /// </summary>
    internal static class KnownHostileTests
    {
        public static void Run()
        {
            FilterAdmitsOnlyWhatCanFightOrSee();
            EvictionKeepsAirDefenceAndMobileUnits();
            RediscoveryUpdatesInPlace();
            PreWarNeverDowngradesTracking();
            ReseedRecoversABatchedSyncThatFiredNoEvents();
        }

        private static KnownHostile Entry(uint id, byte priority, float spottedAt, bool isStatic = false) => new KnownHostile
        {
            Id = id,
            X = id * 100f,
            Z = 0f,
            SpottedAt = spottedAt,
            Priority = priority,
            Static = isStatic,
            Class = UnitClass.GroundVehicle,
            Role = ForceRole.Other,
            Profile = -1,
            Confirmed = true,
            DeadSince = float.NaN
        };

        private static void FilterAdmitsOnlyWhatCanFightOrSee()
        {
            TestAssert.That(!KnownHostileFilter.Admits(UnitClass.Missile, true), "missiles are launch memory, never known hostiles");
            TestAssert.That(!KnownHostileFilter.Admits(UnitClass.Other, true), "scenery, containers and pilots are never admitted");
            TestAssert.That(!KnownHostileFilter.Admits(UnitClass.Building, false), "a building without a weapon or radar is scenery");
            TestAssert.That(KnownHostileFilter.Admits(UnitClass.Building, true), "an emplacement or radar station is admitted");
            TestAssert.That(KnownHostileFilter.Admits(UnitClass.GroundVehicle, false), "every ground vehicle is admitted");
            TestAssert.That(KnownHostileFilter.Admits(UnitClass.Ship, false) && KnownHostileFilter.Admits(UnitClass.Aircraft, false),
                "ships and aircraft are admitted");
            TestAssert.That(KnownHostileFilter.Priority(true, true) == KnownHostileFilter.PriorityAirDefence, "air defence outranks everything");
            TestAssert.That(KnownHostileFilter.Priority(false, false) == KnownHostileFilter.PriorityMobile, "a mobile unit ranks over a static one");
            TestAssert.That(KnownHostileFilter.Priority(false, true) == KnownHostileFilter.PriorityStatic, "static non-AD is the first to go");
        }

        private static void EvictionKeepsAirDefenceAndMobileUnits()
        {
            var table = new KnownHostileTable(4);
            for (uint i = 1; i <= 4; i++) table.Upsert(Entry(i, KnownHostileFilter.PriorityStatic, 10f * i, true));
            TestAssert.That(table.Upsert(Entry(5, KnownHostileFilter.PriorityMobile, 1f)) == UpsertResult.Evicted,
                "a mobile unit displaces a static one even when its sighting is older");
            TestAssert.That(!table.TryFind(1, out _), "the oldest static entry goes first");
            TestAssert.That(table.Upsert(Entry(6, KnownHostileFilter.PriorityAirDefence, 2f)) == UpsertResult.Evicted && !table.TryFind(2, out _),
                "air defence displaces the next-oldest static entry");
            table.Upsert(Entry(7, KnownHostileFilter.PriorityAirDefence, 3f));
            table.Upsert(Entry(8, KnownHostileFilter.PriorityAirDefence, 4f));
            TestAssert.That(table.Upsert(Entry(9, KnownHostileFilter.PriorityStatic, 99f, true)) == UpsertResult.Refused,
                "static non-AD is refused while better entries fill the table");
            TestAssert.That(table.Upsert(Entry(10, KnownHostileFilter.PriorityAirDefence, 5f)) == UpsertResult.Evicted && !table.TryFind(5, out _),
                "air defence is never refused while a mobile entry exists");
            TestAssert.That(table.Upsert(Entry(11, KnownHostileFilter.PriorityAirDefence, 1f)) == UpsertResult.Refused,
                "an older sighting never displaces a newer one of the same rank");
            TestAssert.That(table.Upsert(Entry(12, KnownHostileFilter.PriorityAirDefence, 6f)) == UpsertResult.Evicted && !table.TryFind(6, out _),
                "a newer sighting replaces the oldest of its rank");
            TestAssert.That(table.Count == 4, "the table never grows past its ceiling");
        }

        private static void RediscoveryUpdatesInPlace()
        {
            var table = new KnownHostileTable(8);
            TestAssert.That(table.Upsert(Entry(1, KnownHostileFilter.PriorityMobile, 1f)) == UpsertResult.Added, "a first sighting is added");
            KnownHostile moved = Entry(1, KnownHostileFilter.PriorityMobile, 9f);
            moved.X = 4242f;
            TestAssert.That(table.Upsert(moved) == UpsertResult.Updated && table.Count == 1, "the same unit found again is one entry");
            TestAssert.That(table.TryFind(1, out int slot) && table[slot].X == 4242f && table[slot].SpottedAt == 9f, "updated in place");
            TestAssert.That(table.Remove(1) && !table.Remove(1) && table.Count == 0, "a forget removes it once; a second forget is harmless");
        }

        private static void PreWarNeverDowngradesTracking()
        {
            var table = new KnownHostileTable(8);
            table.Upsert(Entry(1, KnownHostileFilter.PriorityAirDefence, 5f));
            KnownHostile seed = Entry(1, KnownHostileFilter.PriorityAirDefence, 30f);
            seed.PreWar = true;
            seed.Confirmed = false;
            TestAssert.That(table.Upsert(seed) == UpsertResult.Ignored, "a pre-war seed never overwrites a tracked unit");
            table.TryFind(1, out int slot);
            TestAssert.That(!table[slot].PreWar && table[slot].Confirmed && table[slot].SpottedAt == 5f, "the tracked entry is untouched");

            KnownHostile site = Entry(2, KnownHostileFilter.PriorityAirDefence, 30f);
            site.PreWar = true;
            site.Confirmed = false;
            table.Upsert(site);
            table.Upsert(Entry(2, KnownHostileFilter.PriorityAirDefence, 40f));
            table.TryFind(2, out slot);
            TestAssert.That(table[slot].PreWar && table[slot].Confirmed && table[slot].SpottedAt == 40f,
                "tracking a pre-war site confirms it and keeps its pre-war label");
        }

        private static void ReseedRecoversABatchedSyncThatFiredNoEvents()
        {
            var table = new KnownHostileTable(64);
            int expected = 0;
            for (uint id = 1; id <= 10; id++)
            {
                table.Upsert(Entry(id, KnownHostileFilter.PriorityMobile, id)); // onDiscoverUnit fired
                expected++;
            }
            KnownHostile seed = Entry(99, KnownHostileFilter.PriorityAirDefence, 30f);
            seed.PreWar = true;
            seed.Confirmed = false;
            table.Upsert(seed);

            // RpcGetTrackingStateBatched -> SetTrackingState adds 30 units with no event, and
            // unit 3's forget was missed: the database now holds 1..40 except 3.
            const int databaseCount = 39;
            TestAssert.That(!KnownHostileFilter.NeedsReseed(10, 26), "a drift of 16 is tolerated");
            TestAssert.That(KnownHostileFilter.NeedsReseed(expected, databaseCount), "a silent batch is caught by the count drift");

            table.BeginSweep();
            for (uint id = 1; id <= 40; id++)
                if (id != 3) table.Upsert(Entry(id, KnownHostileFilter.PriorityMobile, 50f));
            int swept = table.SweepUnseenConfirmed();
            TestAssert.That(swept == 1 && !table.TryFind(3, out _), "the unit the database no longer holds is swept");
            TestAssert.That(table.TryFind(99, out _), "unconfirmed pre-war intel is not tracking and survives the sweep");
            TestAssert.That(table.Count == databaseCount + 1, "every tracked unit is known again after the re-seed");
        }
    }
}
