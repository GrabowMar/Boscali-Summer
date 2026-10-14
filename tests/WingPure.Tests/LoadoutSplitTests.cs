using System.Collections.Generic;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    internal static class LoadoutSplitTests
    {
        public static void Run()
        {
            var layout = new StationLayout(new[] { "Left outer", "Right outer", "Centre" },
                new[] { false, true, false }, new[] { 1, 1, 2 },
                new[] { new[] { 2 }, new int[0], new int[0] });
            TestAssert.Eq(layout.Stations, 3, "Each native set has an independent row");
            TestAssert.That(layout.Name(0) == "Left outer" && layout.Name(1) == "Right outer", "Keep side names");
            TestAssert.That(layout.Partner(0) == 1 && layout.Partner(1) == 0 && layout.Partner(2) == -1, "Pair sides find each other; a lone station has no partner");
            var keys = new List<string> { "AGM", "AAM" };
            layout.Normalize(keys);
            TestAssert.That(keys.Count == 3 && keys[0] == "AGM" && keys[1] == "AAM" && keys[2] == null, "Opening a saved asymmetric fit preserves both sides");
            layout.Pick(keys, 1, "ECM");
            TestAssert.That(keys[0] == "AGM" && keys[1] == "ECM", "Picking right does not change left");
            layout.Clear(keys, 0);
            TestAssert.That(keys[0] == null && keys[1] == "ECM", "Clearing left leaves right fitted");
            var saved = new LoadoutTemplateRecord("split", "cricket", "Asymmetric", keys);
            var loaded = LoadoutTemplateCodec.Decode(LoadoutTemplateCodec.Encode(new[] { saved }))[0];
            layout.Normalize(loaded.MountKeys);
            TestAssert.That(loaded.KeyAt(0) == null && loaded.KeyAt(1) == "ECM", "Save/load preserves an empty side");
            layout.Pick(keys, 0, "AGM");
            TestAssert.Eq(layout.Pick(keys, 2, "TANK"), 1, "Only the conflicting side is emptied");
            TestAssert.That(keys[0] == null && keys[1] == "ECM" && keys[2] == "TANK", "Exclusions preserve the other side");
            TestAssert.Eq(layout.Pick(keys, 0, "AGM"), -1, "A blocked side refuses a store");
            var summary = LoadoutSummary.Of(layout, new[] {
                new StoreFacts { HasKey = true, Known = true, Mass = 10, Ammo = 1, Kind = StoreKind.AirToGround },
                new StoreFacts { HasKey = true, Known = true, Mass = 20, Ammo = 2, Kind = StoreKind.AirToAir },
                new StoreFacts { HasKey = true, Known = true, Mass = 30, Ammo = 3, Kind = StoreKind.Bomb } });
            TestAssert.That(summary.Stations == 3 && summary.Fitted == 2 && summary.Blocked == 1 && summary.Mass == 80 && summary.Aam == 2 && summary.Bombs == 6,
                "Summary counts independent stores and physical pylons after exclusion");
            var spots = new StationMapMath.Spot[6];
            int n = StationMapMath.Spots(new[] { 1, 1, 2 }, 62, 80, 16, spots, new[] { false, true, false });
            TestAssert.That(n == 4 && spots[0].Station == 0 && spots[1].Station == 1 && spots[0].X == -spots[1].X && spots[0].Y == spots[1].Y,
                "Mirrored map boxes select their own sides");
        }
    }
}
