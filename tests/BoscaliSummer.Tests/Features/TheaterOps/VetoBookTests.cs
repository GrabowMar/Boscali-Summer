using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>A veto by key and by place, so a sector key that flips to the neighbouring airbase cannot bring a called-off front back (annex 01 E2).</summary>
    internal static class VetoBookTests
    {
        private const float Radius = 6000f;

        public static void Run()
        {
            TheSameKeyIsVetoedWhereverItIs();
            ANearbyFixIsVetoedInsideTheRadiusOnly();
            AVetoExpiresAtItsBoundary();
            TheOldestVetoIsEvictedAtCapacity();
            ReAddingAKeyRefreshesItWithoutSpendingASlot();
            PruneAndCountReadLiveVetoesOnly();
            BadInputNeverVetoesByPosition();
        }

        private static void TheSameKeyIsVetoedWhereverItIs()
        {
            var book = new VetoBook();
            book.Add("@air_1", 1000f, 2000f, 180f);
            TestAssert.That(book.IsVetoed("@air_1", 90000f, -90000f, 10f, Radius), "the same key is vetoed far from the vetoed fix");
            TestAssert.That(!book.IsVetoed("@air_2", 90000f, -90000f, 10f, Radius), "another key far away is free");
            TestAssert.That(!book.IsVetoed(null, 90000f, -90000f, 10f, Radius), "no key is not the vetoed key");
        }

        private static void ANearbyFixIsVetoedInsideTheRadiusOnly()
        {
            var book = new VetoBook();
            book.Add("@air_1", 0f, 0f, 180f);
            TestAssert.That(book.IsVetoed("@air_2", 5900f, 0f, 10f, Radius), "another key at 5.9 km is the same place, so vetoed");
            TestAssert.That(book.IsVetoed("@air_2", 6000f, 0f, 10f, Radius), "exactly on the radius is vetoed");
            TestAssert.That(!book.IsVetoed("@air_2", 6100f, 0f, 10f, Radius), "at 6.1 km it is another place");
            TestAssert.That(book.IsVetoed("@air_2", 4200f, 4200f, 10f, Radius), "the radius is a circle: 5.94 km on the diagonal is inside");
            TestAssert.That(!book.IsVetoed("@air_2", 4300f, 4300f, 10f, Radius), "the radius is a circle: 6.08 km on the diagonal is outside");
            TestAssert.That(!book.IsVetoed("@air_2", 100f, 0f, 10f, 0f), "a zero radius vetoes by key only");
        }

        private static void AVetoExpiresAtItsBoundary()
        {
            var book = new VetoBook();
            book.Add("@air_1", 0f, 0f, 100f);
            TestAssert.That(book.IsVetoed("@air_1", 0f, 0f, 99.9f, Radius), "a veto holds until its time");
            TestAssert.That(!book.IsVetoed("@air_1", 0f, 0f, 100f, Radius), "a veto is over at its time");
            TestAssert.That(!book.IsVetoed("@air_2", 10f, 0f, 100f, Radius), "an expired veto no longer covers its place");
            TestAssert.Eq(book.Count(99.9f), 1, "it counts while live");
            TestAssert.Eq(book.Count(100f), 0, "and not once expired");
        }

        private static void TheOldestVetoIsEvictedAtCapacity()
        {
            var book = new VetoBook();
            for (int i = 0; i < VetoBook.Capacity + 1; i++) book.Add("k" + i, i * 100000f, 0f, 500f);
            TestAssert.Eq(book.Count(0f), VetoBook.Capacity, "the book holds eight");
            TestAssert.That(!book.IsVetoed("k0", 0f, 5000000f, 0f, Radius), "the oldest veto was evicted");
            TestAssert.That(book.IsVetoed("k1", 0f, 5000000f, 0f, Radius), "the next oldest survives");
            TestAssert.That(book.IsVetoed("k8", 0f, 5000000f, 0f, Radius), "the newest is held");
        }

        private static void ReAddingAKeyRefreshesItWithoutSpendingASlot()
        {
            var book = new VetoBook();
            for (int i = 0; i < VetoBook.Capacity; i++) book.Add("k" + i, i * 100000f, 0f, 100f);
            book.Add("k0", 0f, 0f, 400f);
            TestAssert.Eq(book.Count(0f), VetoBook.Capacity, "re-adding a key does not grow the book");
            TestAssert.That(book.IsVetoed("k0", 0f, 5000000f, 300f, Radius), "the refreshed veto holds past the old time");
            TestAssert.That(book.IsVetoed("k1", 0f, 5000000f, 50f, Radius), "nobody was evicted");
            book.Add("fresh", 7e5f, 0f, 400f);
            TestAssert.That(book.IsVetoed("k0", 0f, 5000000f, 50f, Radius), "the refreshed key counts as the newest, so the next eviction takes k1");
            TestAssert.That(!book.IsVetoed("k1", 0f, 5000000f, 50f, Radius), "k1 was the oldest after the refresh");
        }

        private static void PruneAndCountReadLiveVetoesOnly()
        {
            var book = new VetoBook();
            book.Add("a", 0f, 0f, 10f);
            book.Add("b", 0f, 0f, 20f);
            book.Add("c", 0f, 0f, 30f);
            book.Prune(20f);
            TestAssert.Eq(book.Count(0f), 1, "pruning at 20 s drops the vetoes that ended at or before it");
            TestAssert.That(book.IsVetoed("c", 0f, 0f, 25f, Radius), "the live veto stays");
            TestAssert.That(!book.IsVetoed("a", 9e6f, 9e6f, 5f, Radius), "a pruned veto is gone even when asked about an earlier time");
            book.Prune(1000f);
            TestAssert.Eq(book.Count(0f), 0, "everything expires");
            book.Add("d", 0f, 0f, 2000f);
            TestAssert.Eq(book.Count(0f), 1, "the book is reusable after a full prune");
        }

        private static void BadInputNeverVetoesByPosition()
        {
            var book = new VetoBook();
            book.Add("a", float.NaN, 0f, 100f);
            TestAssert.That(book.IsVetoed("a", 0f, 0f, 1f, Radius), "a veto with no usable fix still holds its key");
            TestAssert.That(!book.IsVetoed("b", 0f, 0f, 1f, Radius), "but covers no place");
            var near = new VetoBook();
            near.Add("a", 0f, 0f, 100f);
            TestAssert.That(!near.IsVetoed("b", float.NaN, 0f, 1f, Radius), "a front with no usable fix is vetoed by key only");
            TestAssert.That(!near.IsVetoed("b", 0f, 0f, float.NaN, Radius), "a non-finite clock vetoes nothing");
        }
    }
}
