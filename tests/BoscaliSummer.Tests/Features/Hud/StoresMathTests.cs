using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The STORES HEADER's rules: store classes, pooled rounds, the state tab, a dispenser's band, the rail and the dot grid, the capacitor's rate, and the native takeover's fail-open decision.</summary>
    internal static class StoresMathTests
    {
        public static void Run()
        {
            Classes();
            Pools();
            Tab();
            Dispensers();
            Rail();
            Grids();
            Capacitor();
            Takeover();
        }

        private static StoresScope Scope(params StoreStation[] stations)
        {
            var d = new StoresScope { Stations = new StoreStation[StoresScope.MaxStations], StationCount = stations.Length, Selected = 0 };
            for (int i = 0; i < stations.Length; i++) d.Stations[i] = stations[i];
            return d;
        }

        private static StoreStation Station(string name, int ammo, int max, StoreClass cls = StoreClass.Aam) =>
            new StoreStation { Name = name, Ammo = ammo, Max = max, Class = cls, FiredAgeS = 99f, ReloadElapsed = float.NaN, ReloadLeftS = float.NaN };

        private static void Classes()
        {
            TestAssert.That(StoresMath.ClassOf(new WeaponFlags { Gun = true }) == StoreClass.Gun, "A gun is a gun");
            TestAssert.That(StoresMath.ClassOf(new WeaponFlags { Gun = true, Energy = true }) == StoreClass.Gun, "A gun pod stays a gun even if it says energy");
            TestAssert.That(StoresMath.ClassOf(new WeaponFlags { Energy = true }) == StoreClass.Rail, "A powered weapon is a rail");
            TestAssert.That(StoresMath.ClassOf(new WeaponFlags { Bomb = true }) == StoreClass.Bomb, "A bomb is a bomb");
            TestAssert.That(StoresMath.ClassOf(new WeaponFlags { Missile = true, Jammer = true }) == StoreClass.Kit, "A decoy that rides a missile is a kit");
            TestAssert.That(StoresMath.ClassOf(new WeaponFlags { Cargo = true }) == StoreClass.Kit, "Cargo is a kit");
            TestAssert.That(StoresMath.ClassOf(new WeaponFlags { Launcher = true, Missile = true }) == StoreClass.Pod, "A launcher of rockets is a pod");
            TestAssert.That(StoresMath.ClassOf(new WeaponFlags { Missile = true, AntiAir = 1f, AntiSurface = 0f }) == StoreClass.Aam, "A missile rated for air is an AAM");
            TestAssert.That(StoresMath.ClassOf(new WeaponFlags { Missile = true, AntiAir = 0.2f, AntiSurface = 0.9f }) == StoreClass.Agm, "A missile rated for the surface is an AGM");
            TestAssert.That(StoresMath.ClassOf(new WeaponFlags { Missile = true, AntiAir = 0.5f, AntiSurface = 0.5f }) == StoreClass.Aam, "A tie goes to air");
            TestAssert.That(StoresMath.ClassOf(default) == StoreClass.Kit, "Anything unknown is a kit");
            TestAssert.That(StoresMath.ClassWord(StoreClass.Agm) == "AGM" && StoresMath.ClassWord(StoreClass.Gun) == "GUN", "Classes have words");
        }

        private static void Pools()
        {
            StoresScope d = Scope(Station("AAM-4", 1, 1), Station("GUN", 372, 500, StoreClass.Gun), Station("AAM-4", 0, 1));
            StoresMath.Pool(d, 0, out int total, out int max);
            TestAssert.That(total == 1 && max == 2, "Two pylons of one weapon read as one pool: " + total + "/" + max);
            StoresMath.Pool(d, 1, out total, out max);
            TestAssert.That(total == 372 && max == 500, "A lone gun is its own pool");
            StoresMath.Pool(d, 9, out total, out max);
            TestAssert.That(total == 0 && max == 0, "A station that is not there has no rounds");
            TestAssert.That(StoresMath.Low(100, 500) && !StoresMath.Low(101, 500), "LOW is a fifth of the load or less");
            TestAssert.That(!StoresMath.Low(0, 500) && !StoresMath.Low(5, 0), "None left is EMPTY, not LOW; an unknown load is never LOW");
            TestAssert.That(StoresMath.Empty(0) && !StoresMath.Empty(1), "Empty at zero");
            TestAssert.Near(StoresMath.Fraction(372, 500), 0.744f, "The share left");
            TestAssert.Near(StoresMath.Fraction(5, 0), 0f, "An unknown full load is no share");
        }

        private static void Tab()
        {
            StoresScope d = Scope(Station("GUN", 372, 500, StoreClass.Gun));
            TestAssert.That(StoresMath.StateOf(d) == StoreState.Armed, "A loaded weapon is ARMED");
            d.Safe = true;
            TestAssert.That(StoresMath.StateOf(d) == StoreState.Safe, "...SAFE with the master arm lock on");
            d.Stations[0].FiredAgeS = 0.3f;
            TestAssert.That(StoresMath.StateOf(d) == StoreState.Firing, "A gun that just fired says FIRING");
            d.Stations[0].Class = StoreClass.Bomb;
            TestAssert.That(StoresMath.StateOf(d) == StoreState.Release, "A bomb says RELEASE");
            d.Stations[0].Class = StoreClass.Aam;
            TestAssert.That(StoresMath.StateOf(d) == StoreState.Launch, "A missile says LAUNCH");
            d.Stations[0].FiredAgeS = 2f;
            d.Stations[0].Ammo = 0;
            TestAssert.That(StoresMath.StateOf(d) == StoreState.Empty, "Nothing left, no shot just gone: EMPTY");
            d.Stations[0].FiredAgeS = 0.2f;
            TestAssert.That(StoresMath.StateOf(d) == StoreState.Launch, "The shot that emptied it still says LAUNCH for a moment");
            d.Stations[0].Reloading = true;
            TestAssert.That(StoresMath.StateOf(d) == StoreState.Reload, "A reload beats every other word");
            d.Selected = -1;
            TestAssert.That(StoresMath.StateOf(d) == StoreState.Armed, "No selection reads the default");
            TestAssert.That(StoresMath.Word(StoreState.Reload) == "RELOAD" && StoresMath.Word(StoreState.Empty) == "EMPTY" && StoresMath.Word(StoreState.Safe) == "SAFE", "The tab's words");
        }

        private static void Dispensers()
        {
            var flares = new CmStation { Tag = "IR", Ammo = 48, Max = 48, Mask = HudCm.IrMask };
            var chaff = new CmStation { Tag = "RDR", Ammo = 30, Max = 30, Mask = HudCm.RadarMask, Selected = true };
            var jammer = new CmStation { Tag = "JAM", Ammo = 1, Max = 1, Chargeable = true, Mask = HudCm.RadarMask };
            TestAssert.That(StoresMath.CmStateOf(flares, false, false) == CmCellState.Ready, "A plain dispenser is READY");
            TestAssert.That(StoresMath.CmStateOf(chaff, false, false) == CmCellState.Selected, "A selected one says SEL");
            TestAssert.That(StoresMath.CmStateOf(chaff, true, false) == CmCellState.Dispense, "A call beats a selection");
            TestAssert.That(StoresMath.CmStateOf(chaff, false, true) == CmCellState.Firing, "Rounds just fired say FIRING");
            flares.Ammo = 12;
            TestAssert.That(StoresMath.CmStateOf(flares, false, false) == CmCellState.Low, "A quarter or less is LOW");
            flares.Ammo = 0;
            TestAssert.That(StoresMath.CmStateOf(flares, false, false) == CmCellState.Empty, "None left is EMPTY");
            TestAssert.That(StoresMath.CmStateOf(flares, true, false) == CmCellState.Dispense, "...but a call still pulses (the pilot learns there is nothing to fire)");
            TestAssert.That(StoresMath.Called(chaff, HudCm.RadarMask) && !StoresMath.Called(chaff, HudCm.IrMask) && !StoresMath.Called(chaff, 0), "A call names the seeker class it is for");
            TestAssert.That(!StoresMath.Called(jammer, HudCm.RadarMask), "A jammer is never called: it is a switch, not a round");
        }

        private static void Rail()
        {
            TestAssert.Near(StoresMath.PipWidth(8, 1, 238f, 32f, 4f), 25f, "Eight stations with a gun: the prototype's 25 px pips");
            TestAssert.Near(StoresMath.PipWidth(3, 1, 238f, 32f, 4f), 30f, "A few stations do not stretch into slabs");
            TestAssert.Near(StoresMath.PipWidth(12, 4, 238f, 32f, 4f), 12f, "A crowded rail stays readable at 12 px");
            TestAssert.Near(StoresMath.PipWidth(2, 2, 238f, 32f, 4f), 32f, "Guns only: no other pips to size");
            TestAssert.Near(StoresMath.PipWidth(5, 0, 238f, 32f, 4f), 30f, "No gun, five stations");
        }

        private static void Grids()
        {
            int[][] dims = { new[] { 48, 12, 4 }, new[] { 30, 10, 3 }, new[] { 72, 12, 6 }, new[] { 1, 10, 1 }, new[] { 0, 10, 1 } };
            foreach (int[] g in dims)
            {
                StoresMath.Grid(g[0], 80f, 36f, out int cols, out int rows, out float pitch, out int perDot);
                TestAssert.That(cols == g[1] && rows == g[2] && pitch == 6f && perDot == 1, "A magazine of " + g[0] + " is " + cols + "x" + rows + " at pitch " + pitch);
            }
            StoresMath.Grid(120, 80f, 30f, out int c2, out int r2, out float p2, out int pd2);
            TestAssert.That(p2 < 6f && c2 * p2 <= 80f && r2 * p2 <= 30f && pd2 == 1 && c2 * r2 >= 120, "A big load narrows the pitch until it fits: " + c2 + "x" + r2 + " at " + p2);
            StoresMath.Grid(2000, 80f, 30f, out int c3, out int r3, out float p3, out int pd3);
            TestAssert.That(pd3 > 1 && c3 * r3 * pd3 >= 2000 && c3 * p3 <= 80f && r3 * p3 <= 30f, "An absurd load makes one dot stand for several rounds: " + pd3);
            StoresMath.Grid(30, 52f, 30f, out int c4, out int r4, out float p4, out int pd4);
            TestAssert.That(c4 * p4 <= 52f && r4 * p4 <= 30f && c4 * r4 >= 30 && pd4 == 1, "A narrow column still holds its rounds");
        }

        private static void Capacitor()
        {
            TestAssert.That(StoresMath.CapWordOf(-14f, 0.9f) == CapWord.Drain && StoresMath.CapWordOf(20f, 0.5f) == CapWord.Charge, "A moving charge reads DRN or CHG");
            TestAssert.That(StoresMath.CapWordOf(0.2f, 1f) == CapWord.Full && StoresMath.CapWordOf(-0.5f, 0.6f) == CapWord.Hold, "A steady one is FULL or HOLD");
            TestAssert.That(StoresMath.CapLow(100f, 500f) && !StoresMath.CapLow(101f, 500f) && !StoresMath.CapLow(5f, 0f), "LOW at a fifth");

            var t = new CapacitorTrend();
            TestAssert.Near(t.Push(0f, 500f), 0f, "No history, no rate");
            TestAssert.Near(t.Push(0.1f, 500f), 0f, "Not enough history yet");
            float rate = 0f;
            for (int i = 2; i <= 12; i++) rate = t.Push(i * 0.1f, 500f - 14f * i * 0.1f);
            TestAssert.That(rate < -13f && rate > -15f, "A steady 14 kJ/s drain reads -14: " + rate);
            for (int i = 13; i <= 24; i++) rate = t.Push(i * 0.1f, 500f - 14f * 1.2f + 30f * (i - 12) * 0.1f);
            TestAssert.That(rate > 29f && rate < 31f, "...and then a 30 kJ/s charge reads +30: " + rate);
            t.Push(0.5f, 100f);
            TestAssert.Near(t.Push(0.6f, 100f), 0f, "A clock that went back starts over");
        }

        private static void Takeover()
        {
            TestAssert.That(NativeTakeover.Hide(true, true, true, false, true), "Everything up and drawing: the native box is hidden");
            TestAssert.That(!NativeTakeover.Hide(false, true, true, false, true), "No board: native shows");
            TestAssert.That(!NativeTakeover.Hide(true, false, true, false, true), "A paint that threw: native shows");
            TestAssert.That(!NativeTakeover.Hide(true, true, false, false, true), "HUD hidden: native shows");
            TestAssert.That(!NativeTakeover.Hide(true, true, true, true, true), "Batch mode: native shows");
            TestAssert.That(!NativeTakeover.Hide(true, true, true, false, false), "The header did not draw this box's replacement: native shows");
        }
    }
}
