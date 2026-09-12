using System;

namespace NOAvionics.Tests
{
    /// <summary>
    /// Engine-free protocol tests. Compiled into each mod's test project alongside the
    /// shared avionics sources. Not a shipping type.
    /// </summary>
    public static class AvionicsProtocolTests
    {
        public static void Run(Action<bool, string> assert)
        {
            if (assert == null) throw new ArgumentNullException(nameof(assert));

            BezelRegistry.Reset();
            MapPicker.Reset();
            PresenceBoard.Reset();

            RunBezel(assert);
            RunPicker(assert);
            RunPresence(assert);
            RunScoring(assert);

            BezelRegistry.Reset();
            MapPicker.Reset();
            PresenceBoard.Reset();
        }

        private static void RunBezel(Action<bool, string> assert)
        {
            bool firstLeft = false;
            int firstSlot = -1;
            assert(BezelRegistry.TryClaim(BezelRegistry.Wmc, true, 6, 6, AllFree, out firstLeft, out firstSlot),
                "WMC should claim the first left slot");
            assert(firstLeft && firstSlot == 0, "WMC preferred left slot 0");

            bool opsLeft = false;
            int opsSlot = -1;
            assert(BezelRegistry.TryClaim(BezelRegistry.Ops, true, 6, 6, AllFree, out opsLeft, out opsSlot),
                "OPS should claim the next left slot");
            assert(opsLeft && opsSlot == 1, "OPS must not double-claim WMC's slot");

            bool radLeft = false;
            int radSlot = -1;
            assert(BezelRegistry.TryClaim(BezelRegistry.Rad, false, 6, 6, AllFree, out radLeft, out radSlot),
                "RAD should claim the first right slot");
            assert(!radLeft && radSlot == 0, "RAD preferred right slot 0");

            bool againLeft = false;
            int againSlot = -1;
            assert(BezelRegistry.TryClaim(BezelRegistry.Wmc, true, 6, 6, AllFree, out againLeft, out againSlot),
                "Re-claiming WMC returns the existing reservation");
            assert(againLeft == firstLeft && againSlot == firstSlot, "WMC reservation is stable");

            BezelRegistry.Release(BezelRegistry.Wmc);
            assert(!BezelRegistry.IsClaimed(BezelRegistry.Wmc), "Released WMC is free");

            bool reclaimedLeft = false;
            int reclaimedSlot = -1;
            assert(BezelRegistry.TryClaim(BezelRegistry.Wmc, true, 6, 6, AllFree, out reclaimedLeft, out reclaimedSlot),
                "Released slot can be claimed again");
            assert(reclaimedLeft && reclaimedSlot == 0, "WMC reclaims the first free left slot");

            bool setLeft = false;
            int setSlot = -1;
            assert(BezelRegistry.TryClaim(BezelRegistry.Set, false, 6, 6, AllFree, out setLeft, out setSlot),
                "SET should claim a free slot");
            assert(!setLeft && setSlot == 1, "SET claims slot on right after RAD");
        }

        private static void RunPicker(Action<bool, string> assert)
        {
            assert(!MapPicker.IsBusy, "picker starts idle");
            assert(MapPicker.TryArm(MapPicker.WingPoint, MapPicker.GestureLeft, "HOLD ARMED · CLICK MAP"),
                "wing can arm the map");
            assert(MapPicker.IsBusy, "picker is busy after arm");
            assert(MapPicker.IsOwner(MapPicker.WingPoint), "wing owns the picker");
            assert(!MapPicker.TryArm(MapPicker.Support, MapPicker.GestureRight, "ROD ARMED · RIGHT-CLICK MAP"),
                "support cannot steal an armed wing order");
            assert(MapPicker.Prompt == "HOLD ARMED · CLICK MAP", "prompt stays with the owner");

            MapPicker.Disarm(MapPicker.Support);
            assert(MapPicker.IsBusy, "a non-owner cannot disarm");

            MapPicker.Disarm(MapPicker.WingPoint);
            assert(!MapPicker.IsBusy, "owner can disarm");
            assert(MapPicker.TryArm(MapPicker.Support, MapPicker.GestureRight, "ROD ARMED · RIGHT-CLICK MAP"),
                "support can arm once the map is free");
            MapPicker.Disarm(MapPicker.Support);
        }

        private static void RunPresence(Action<bool, string> assert)
        {
            PresenceBoard.SetString(PresenceBoard.WingGuid, "com.marci.wingcommand");
            assert(PresenceBoard.GetString(PresenceBoard.WingGuid) == "com.marci.wingcommand",
                "guid round-trips");

            PresenceBoard.SetInts(PresenceBoard.WingMemberIds, new[] { 11, 22, 33 });
            int[] ids = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
            assert(ids.Length == 3 && ids[1] == 22, "member ids round-trip");
            assert(PresenceBoard.Contains(ids, 22), "contains finds a living wingman");
            assert(!PresenceBoard.Contains(ids, 99), "contains rejects a stranger");
        }

        private static void RunScoring(Action<bool, string> assert)
        {
            assert(TheaterScoring.Bias(false, false, TheaterScoring.DoctrineAirSuperiority, false, true, false, false) == 1f,
                "enemy analyzers are not biased");
            assert(TheaterScoring.Bias(true, true, TheaterScoring.DoctrineAirSuperiority, true, true, false, false) == 1f,
                "wingmen are never biased");

            float air = TheaterScoring.Bias(true, false, TheaterScoring.DoctrineAirSuperiority, false, true, false, false);
            assert(air == TheaterScoring.AirSuperiorityAir, "air-superiority air bias");

            float stacked = TheaterScoring.Bias(true, false, TheaterScoring.DoctrineSead, true, false, false, true);
            assert(stacked <= TheaterScoring.Maximum, "priority + doctrine cannot exceed the cap");
            assert(stacked >= TheaterScoring.Minimum, "bias stays above the floor");
        }

        private static bool AllFree(bool leftColumn, int index)
        {
            return index >= 0 && index < 6;
        }
    }
}
