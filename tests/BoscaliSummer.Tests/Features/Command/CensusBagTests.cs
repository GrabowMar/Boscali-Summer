using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The theater bag the news ticker reads is filled from the host's census and nothing else: unknown stays unknown, never a confident zero (annex 01 E12).</summary>
    internal static class CensusBagTests
    {
        public static void Run()
        {
            AnUnmeasuredCensusLeavesEveryFigureUnknown();
            AMeasuredCensusIsCopiedAndDerived();
            AirRatioNeedsBothCountsAndAnAircraft();
            TheSortieBoardIsObservedOnlyWhenEveryRoleIsKnown();
            ApplyingUnmeasuredAfterMeasuredLeavesNothingStale();
        }

        private static TheaterCensus Measured()
        {
            TheaterCensus c = TheaterCensus.Unmeasured;
            c.Known = true;
            c.AirFriendly = 6; c.AirParked = 3; c.AirHostileTracked = 2;
            c.GroundFriendly = 12; c.GroundHostileTracked = 5; c.ShipsFriendly = 1; c.ShipsHostileTracked = 0;
            c.BasesFriendly = 4; c.BasesHostile = 3; c.BasesNeutral = 1; c.BasesContested = 1; c.Radars = 7;
            c.SectorsFriendly = 10; c.SectorsHostile = 5; c.SectorsContested = 3; c.SectorsNeutral = 20;
            c.SortieCap = 2; c.SortieSead = 1; c.SortieCas = 0; c.SortieStrike = 1; c.SortieTransit = 3;
            c.FrontMetres = 18500f;
            c.Threat = 2; c.ThreatWhy = TheaterThreatWhy.BaseContested;
            return c;
        }

        private static void AnUnmeasuredCensusLeavesEveryFigureUnknown()
        {
            var bag = new TacticalTheaterState();
            CensusBag.Apply(bag, TheaterCensus.Unmeasured);
            TestAssert.Eq(bag.DefconLevel, 0, "an unmeasured census is threat level 0, which is no alert");
            TestAssert.Eq(bag.PrimaryThreatDescription, "", "no rule won, so there is no title");
            TestAssert.Eq(bag.ActiveThreatWarning, "", "and no warning line");
            TestAssert.Eq(bag.FriendlyAircraftCount, -1, "unknown aircraft stay -1");
            TestAssert.Eq(bag.ActiveClashesCount, -1, "unknown clashes stay -1 so the ticker never reads a hot zone");
            TestAssert.Eq(bag.ContestedAirbaseCount, -1, "unknown contested bases stay -1");
            TestAssert.Eq(bag.FriendlySectorCount, -1, "unknown sectors stay -1");
            TestAssert.Near(bag.FrontlineLengthMetres, -1f, "an unknown front length is -1");
            TestAssert.That(float.IsNaN(bag.TerritoryControlRatio), "an unknown territory share is NaN, not 0 or 50");
            TestAssert.That(float.IsNaN(bag.AirSuperiorityRatio), "an unknown air share is NaN");
            TestAssert.Eq(bag.Sorties.Observed, 0, "no sorties were observed");
        }

        private static void AMeasuredCensusIsCopiedAndDerived()
        {
            var bag = new TacticalTheaterState();
            CensusBag.Apply(bag, Measured());
            TestAssert.Eq(bag.FriendlyAircraftCount, 6, "airborne friendly aircraft");
            TestAssert.Eq(bag.HostileAircraftCount, 2, "tracked hostile aircraft");
            TestAssert.Eq(bag.FriendlyAirbaseCount, 4, "friendly bases");
            TestAssert.Eq(bag.HostileAirbaseCount, 3, "hostile bases");
            TestAssert.Eq(bag.NeutralAirbaseCount, 1, "neutral bases");
            TestAssert.Eq(bag.ContestedAirbaseCount, 1, "contested bases");
            TestAssert.Eq(bag.FriendlyRadarCount, 7, "radars");
            TestAssert.Eq(bag.FriendlyGroundUnitsCount, 12, "ground vehicles are not ships");
            TestAssert.Eq(bag.HostileGroundUnitsCount, 5, "tracked hostile ground");
            TestAssert.Eq(bag.ContestedSectorCount, 3, "contested sectors");
            TestAssert.Eq(bag.ActiveClashesCount, 3, "clashes are the contested sectors");
            TestAssert.Near(bag.FrontlineLengthMetres, 18500f, "front length");
            TestAssert.Near(bag.TerritoryControlRatio, 10f / 15f, "territory share is friendly / (friendly + hostile), the one allied-share definition");
            TestAssert.Near(bag.AirSuperiorityRatio, 0.75f, "six against two airborne");
            TestAssert.Eq(bag.DefconLevel, 2, "the threat level");
            TestAssert.Eq(bag.PrimaryThreatDescription, "ONE AIRBASE CONTESTED", "the winning rule's title");
            TestAssert.Eq(bag.ActiveThreatWarning, "DEFCON 2 - ONE AIRBASE CONTESTED", "the warning line carries the number");
            TestAssert.Eq(bag.Sorties.Observed, 7, "every role was counted");
            TestAssert.Eq(bag.Sorties.Tasked, 4, "transit is airborne, not tasked");
            TestAssert.Eq(bag.Sorties.Transit, 3, "parked jets are not in the transit count");
        }

        private static void AirRatioNeedsBothCountsAndAnAircraft()
        {
            var bag = new TacticalTheaterState();
            TheaterCensus c = Measured();
            c.AirFriendly = 0; c.AirHostileTracked = 0;
            CensusBag.Apply(bag, c);
            TestAssert.That(float.IsNaN(bag.AirSuperiorityRatio), "no aircraft on either side is unknown, not 0 percent");
            c.AirFriendly = 0; c.AirHostileTracked = 3;
            CensusBag.Apply(bag, c);
            TestAssert.Near(bag.AirSuperiorityRatio, 0f, "no friendly aircraft against three tracked is a real zero");
            c.AirFriendly = 4; c.AirHostileTracked = -1;
            CensusBag.Apply(bag, c);
            TestAssert.That(float.IsNaN(bag.AirSuperiorityRatio), "an unknown hostile count is unknown");
        }

        private static void TheSortieBoardIsObservedOnlyWhenEveryRoleIsKnown()
        {
            var bag = new TacticalTheaterState();
            TheaterCensus c = Measured();
            c.SortieCas = -1;
            CensusBag.Apply(bag, c);
            TestAssert.Eq(bag.Sorties.Observed, 0, "one unknown role makes the whole board unobserved");
            TestAssert.Eq(bag.Sorties.Cap, 0, "and no partial row is shown");
            c = Measured();
            c.SortieCap = c.SortieSead = c.SortieCas = c.SortieStrike = c.SortieTransit = 0;
            CensusBag.Apply(bag, c);
            TestAssert.Eq(bag.Sorties.Observed, 0, "a measured empty board has nothing observed to show");
        }

        private static void ApplyingUnmeasuredAfterMeasuredLeavesNothingStale()
        {
            var bag = new TacticalTheaterState();
            CensusBag.Apply(bag, Measured());
            CensusBag.Apply(bag, TheaterCensus.Unmeasured);
            TestAssert.Eq(bag.DefconLevel, 0, "the alert is gone");
            TestAssert.Eq(bag.ActiveThreatWarning, "", "and so is its line");
            TestAssert.Eq(bag.HostileAircraftCount, -1, "counts are unknown again");
            TestAssert.That(float.IsNaN(bag.TerritoryControlRatio), "the share is unknown again");
            TestAssert.Eq(bag.Sorties.Observed, 0, "the sortie board is unobserved again");
        }
    }
}
