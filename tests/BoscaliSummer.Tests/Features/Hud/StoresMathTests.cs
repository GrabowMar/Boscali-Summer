using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The stores side's rules: store classes, pooled rounds, the state word, which blocks exist, the arm marker, a station's mark on the rail, the capacitor's cells and rate, a dispenser's word and dot matrix, and the native takeover's fail-open decision.</summary>
    internal static class StoresMathTests
    {
        public static void Run()
        {
            Classes();
            Pools();
            Tab();
            Dispensers();
            Blocks();
            Hero();
            Rail();
            Cells();
            Matrices();
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

        private static void Blocks()
        {
            StoresScope d = Scope(Station("GUN", 500, 500, StoreClass.Gun), Station("LANCE", 1, 1, StoreClass.Rail));
            TestAssert.That(StoresMath.HasHero(d), "A selected station: HERO is there");
            d.Selected = -1;
            TestAssert.That(!StoresMath.HasHero(d), "None selected: no HERO");
            d.Selected = 2;
            TestAssert.That(!StoresMath.HasHero(d), "A selection past the stations: no HERO");
            TestAssert.That(!StoresMath.HasHero(default), "No feed arrays: no HERO");
            TestAssert.That(!StoresMath.HasPwr(d) && StoresMath.HasPwr(new StoresScope { HasCap = true }), "PWR BUS is there with a capacitor only");
            TestAssert.That(!StoresMath.HasExp(d) && StoresMath.HasExp(new StoresScope { CmCount = 1 }), "EXPENDABLES is there with a dispenser or a jammer only");
            d.HasCap = true;
            TestAssert.Near(StoresMath.CostKj(d), 0f, "A rail fitted but no cost given: no cost row");
            d.CapCostKj = 300f;
            TestAssert.Near(StoresMath.CostKj(d), 300f, "A rail fitted and a cost: the cost row shows it");
            d.HasCap = false;
            TestAssert.Near(StoresMath.CostKj(d), 0f, "No capacitor: nothing to price");
            d.HasCap = true;
            d.Stations[1].Class = StoreClass.Aam;
            TestAssert.Near(StoresMath.CostKj(d), 0f, "No energy weapon fitted: no cost row, whatever the feed says");
        }

        private static void Hero()
        {
            foreach (StoreState s in new[] { StoreState.Armed, StoreState.Firing, StoreState.Launch, StoreState.Release })
                TestAssert.That(StoresMath.ArmTone(s) == ScopeTone.Hi && StoresMath.ArmFilled(s), s + ": a bright filled marker");
            foreach (StoreState s in new[] { StoreState.Reload, StoreState.Empty })
                TestAssert.That(StoresMath.ArmTone(s) == ScopeTone.Amber && StoresMath.ArmFilled(s), s + ": an amber filled marker");
            TestAssert.That(StoresMath.ArmTone(StoreState.Safe) == ScopeTone.Amber && !StoresMath.ArmFilled(StoreState.Safe), "SAFE: an amber hollow marker");

            TestAssert.That(StoresMath.RoundsTone(false, false, false) == ScopeTone.Hi, "Rounds in hand: BRIGHT");
            TestAssert.That(StoresMath.RoundsTone(false, false, true) == ScopeTone.Amber && StoresMath.RoundsTone(false, true, false) == ScopeTone.Amber, "Low or none: amber");
            TestAssert.That(StoresMath.RoundsTone(true, true, false) == ScopeTone.Ink, "A reload: INK, the countdown is the amber word");

            TestAssert.That(StoresMath.ArtTone(StoreState.Safe, false) == ScopeTone.Ink, "The art is INK while SAFE");
            TestAssert.That(StoresMath.ArtTone(StoreState.Armed, false) == ScopeTone.Hi && StoresMath.ArtTone(StoreState.Launch, false) == ScopeTone.Hi, "...BRIGHT armed or hot");
            TestAssert.That(StoresMath.ArtTone(StoreState.Armed, true) == ScopeTone.Amber && StoresMath.ArtTone(StoreState.Reload, false) == ScopeTone.Amber && StoresMath.ArtTone(StoreState.Empty, false) == ScopeTone.Amber, "...amber low, reloading or empty");
            TestAssert.That(StoresMath.ClassTone(StoreClass.Gun) == ScopeTone.Dim && StoresMath.ClassTone(StoreClass.Aam) == ScopeTone.Ink, "GUN is furniture, STA n AAM is data");

            TestAssert.That(StoresMath.Magazine(StoreClass.Gun, 500) && StoresMath.Magazine(StoreClass.Pod, 38), "A gun's belt and a pod's rockets are magazines");
            TestAssert.That(!StoresMath.Magazine(StoreClass.Gun, 1) && !StoresMath.Magazine(StoreClass.Aam, 2) && !StoresMath.Magazine(StoreClass.Bomb, 4), "A missile or a bomb pooled over pylons is not: one picture is one round");
        }

        private static void Rail()
        {
            StoreStation q = Station("AAM-4", 1, 1);
            RailMark m = StoresMath.RailMarkOf(q, false);
            TestAssert.That(m.Tone == ScopeTone.Ink && m.Alpha == 1f && !m.Dashed && !m.Bold && !m.Bracketed, "A loaded station: INK, plain");
            m = StoresMath.RailMarkOf(q, true);
            TestAssert.That(m.Tone == ScopeTone.Hi && m.Bold && m.Bracketed && !m.Dashed, "The selected one: BRIGHT, bold, bracketed");
            q.Ammo = 0;
            m = StoresMath.RailMarkOf(q, false);
            TestAssert.That(m.Tone == ScopeTone.Ink && m.Dashed && m.Alpha < 1f && m.Alpha > 0.5f && !m.Bold, "A spent station: INK, dashed, a little dimmer but readable");
            m = StoresMath.RailMarkOf(q, true);
            TestAssert.That(m.Tone == ScopeTone.Hi && m.Dashed && m.Bracketed, "A spent station can still be the selected one");
            q.Reloading = true;
            m = StoresMath.RailMarkOf(q, false);
            TestAssert.That(m.Tone == ScopeTone.Amber && m.Bold && !m.Dashed && m.Alpha == 1f, "A reloading station: amber and bold, not spent");
            m = StoresMath.RailMarkOf(q, true);
            TestAssert.That(m.Tone == ScopeTone.Amber && m.Bracketed, "...even when selected");
        }

        private static void Cells()
        {
            TestAssert.That(StoresMath.Cells(180f) == 20 && StoresMath.Cells(216f) == 24, "WIDE's 180 holds 20 cells, STACK's 216 holds 24");
            TestAssert.That(StoresMath.Cells(1f) == 1, "A row always has a cell");
            TestAssert.That(StoresMath.LitCells(1f, 20) == 20 && StoresMath.LitCells(0f, 20) == 0 && StoresMath.LitCells(0.5f, 20) == 10, "Lit cells are the share of the row");
            TestAssert.That(StoresMath.LitCells(43f / 500f, 20) == 2 && StoresMath.LitCells(0.024f, 20) == 0 && StoresMath.LitCells(0.026f, 20) == 1, "...rounded to the nearest cell");
            TestAssert.That(StoresMath.LitCells(-1f, 20) == 0 && StoresMath.LitCells(2f, 20) == 20, "...and held inside the row");
            TestAssert.Near(StoresMath.CostTickX(20, 300f, 500f), 107f, "The cost tick of 300 of 500 on 20 cells: round(178 x 0.6)");
            TestAssert.Near(StoresMath.CostTickX(20, 900f, 500f), 178f, "...never past the row");
            TestAssert.Near(StoresMath.CostTickX(20, 300f, 0f), 0f, "...and nowhere without a full charge");
            TestAssert.That(StoresMath.Shot(500f, 200f, 0.1f, 500f, out float drop) && Math.Abs(drop - 300f) < 0.01f, "A 300 kJ drop in a tick is a shot");
            TestAssert.That(!StoresMath.Shot(500f, 498.6f, 0.1f, 500f, out _), "The jammer's trickle (1.4 kJ in a tick) is not");
            TestAssert.That(!StoresMath.Shot(100f, 80.5f, 0.1f, 500f, out _) && StoresMath.Shot(100f, 79.5f, 0.1f, 500f, out _), "A shot is at least 4 % of the full charge");
            TestAssert.That(!StoresMath.Shot(200f, 250f, 0.1f, 500f, out _), "A charge that rises is no shot");
            TestAssert.That(!StoresMath.Shot(500f, 479f, 1.5f, 500f, out _) && StoresMath.Shot(500f, 479f, 0.1f, 500f, out _), "The same drop over a stalled 1.5 s frame is the jammer's drain, not a shot"); 
            TestAssert.That(StoresMath.CostWord("RG-12 LANCE OVERCHARGE RAILGUN") == "LANCE", "The cost label names the weapon by its first word with no digit");
            TestAssert.That(StoresMath.CostWord("RG-12") == "RG-12" && StoresMath.CostWord("  ") == "CHG" && StoresMath.CostWord(null) == "CHG", "...else its first word, else CHG");
            TestAssert.That(StoresMath.CostWord("PLASMA-CANNON-MK2") == "PLASMA-CAN", "...at most 10 characters");
        }

        private static void Matrices()
        {
            int[][] dims = { new[] { 48, 3, 16 }, new[] { 30, 3, 10 }, new[] { 72, 3, 24 }, new[] { 12, 1, 12 }, new[] { 13, 2, 7 }, new[] { 1, 1, 1 }, new[] { 0, 1, 1 } };
            foreach (int[] g in dims)
            {
                StoresMath.Matrix(g[0], 112f, out int rows, out int cols, out int perDot);
                TestAssert.That(rows == g[1] && cols == g[2] && perDot == 1, "A load of " + g[0] + " is " + rows + " rows x " + cols + " columns, a dot each");
            }
            StoresMath.Matrix(300, 112f, out int r, out int c, out int per);
            TestAssert.That(per == 4 && r == 3 && c <= 28 && r * c * per >= 300, "A load too big for the room: one dot stands for several rounds: " + r + "x" + c + " at " + per);
            StoresMath.Matrix(300, 148f, out r, out c, out per);
            TestAssert.That(per == 3 && c <= 37 && r * c * per >= 300, "STACK's wider room needs fewer rounds a dot: " + r + "x" + c + " at " + per);
            StoresMath.Matrix(72, 20f, out r, out c, out per);
            TestAssert.That(c <= 5 && r * c * per >= 72, "A tiny room still holds the load: " + r + "x" + c + " at " + per);

            TestAssert.That(StoresMath.StateWord(CmCellState.Dispense) == "DISPENSE" && StoresMath.StateWord(CmCellState.Firing) == "FIRING" && StoresMath.StateWord(CmCellState.Low) == "LOW", "A dispenser's state words");
            TestAssert.That(StoresMath.StateWord(CmCellState.Empty) == null && StoresMath.StateWord(CmCellState.Selected) == null && StoresMath.StateWord(CmCellState.Ready) == null, "EMPTY takes the matrix's place; a selection is the marker; READY says nothing");
            TestAssert.That(StoresMath.CountTone(CmLevel.Ok) == ScopeTone.Hi && StoresMath.CountTone(CmLevel.Low) == ScopeTone.Amber && StoresMath.CountTone(CmLevel.Empty) == ScopeTone.Red, "The count: BRIGHT, amber low, red at none");
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
            TestAssert.That(!NativeTakeover.Stale(10.4f, 10f) && !NativeTakeover.Stale(10.5f, 10f), "A board presented within the last 0.5 s is alive");
            TestAssert.That(NativeTakeover.Stale(10.6f, 10f) && NativeTakeover.Stale(10f, -100f), "A board that stopped being presented is stale (and never presented is)");
            TestAssert.That(!NativeTakeover.Hide(!NativeTakeover.Stale(10.6f, 10f), true, true, false, true), "A stale board hands the native box back");
        }
    }
}
