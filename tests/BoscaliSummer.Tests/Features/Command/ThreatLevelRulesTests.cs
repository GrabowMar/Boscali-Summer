using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The threat level (DEFCON 1 maximum to 5 nominal, 0 unknown) from the host's census, at every boundary (annex 01 E12).</summary>
    internal static class ThreatLevelRulesTests
    {
        public static void Run()
        {
            UnmeasuredAndUnknownReadZero();
            TwoContestedBasesAreMaximum();
            OneContestedBaseIsMaximumOnlyWhenOutnumbered();
            OutnumberedByAtLeastFourAndDoubleIsLevelTwo();
            ManyContestedSectorsOrAnyOutnumberingIsLevelThree();
            AnyTrackedHostileOrSomeContestedSectorsIsLevelFour();
            NominalNeedsEverythingKnown();
            ARuleOnKnownInputsStandsWhileOthersAreUnknown();
            WhyNamesTheWinningRule();
        }

        private static TheaterCensus Known() { TheaterCensus c = TheaterCensus.Unmeasured; c.Known = true; return c; }

        private static TheaterCensus Calm()
        {
            TheaterCensus c = Known();
            c.BasesContested = 0; c.AirFriendly = 6; c.AirHostileTracked = 0; c.SectorsContested = 0;
            return c;
        }

        private static void Is(TheaterCensus c, int level, string label)
        {
            byte got = ThreatLevelRules.Level(c, out _);
            TestAssert.That(got == level, label + " (got " + got + ", want " + level + ")");
        }

        private static void UnmeasuredAndUnknownReadZero()
        {
            TheaterThreatWhy why;
            TestAssert.Eq((int)ThreatLevelRules.Level(TheaterCensus.Unmeasured, out why), 0, "an unmeasured census is level 0");
            TestAssert.Eq(why, TheaterThreatWhy.None, "with no reason");
            TestAssert.Eq((int)ThreatLevelRules.Level(default, out why), 0, "a default census is level 0");
            TheaterCensus notKnown = Calm();
            notKnown.Known = false; notKnown.BasesContested = 5;
            TestAssert.Eq((int)ThreatLevelRules.Level(notKnown, out why), 0, "numbers without Known are not believed");
            TestAssert.Eq(why, TheaterThreatWhy.None, "and have no reason");
            Is(Known(), 0, "a measured census with every number unknown");
        }

        private static void TwoContestedBasesAreMaximum()
        {
            TheaterCensus c = Calm();
            c.BasesContested = 2;
            Is(c, 1, "two contested bases");
            c.BasesContested = 3; c.AirFriendly = -1; c.AirHostileTracked = -1;
            Is(c, 1, "three, with the air picture unknown");
        }

        private static void OneContestedBaseIsMaximumOnlyWhenOutnumbered()
        {
            TheaterCensus c = Calm();
            c.BasesContested = 1; c.AirFriendly = 2; c.AirHostileTracked = 3;
            Is(c, 1, "one contested base and more tracked hostile than friendly air");
            c.AirHostileTracked = 2;
            Is(c, 2, "one contested base and equal air");
            c.AirHostileTracked = 1;
            Is(c, 2, "one contested base and fewer hostile");
            c.AirFriendly = -1; c.AirHostileTracked = 9;
            Is(c, 2, "one contested base with our own air unknown cannot be called outnumbered");
        }

        private static void OutnumberedByAtLeastFourAndDoubleIsLevelTwo()
        {
            TheaterCensus c = Calm();
            c.AirHostileTracked = 4; c.AirFriendly = 1;
            Is(c, 2, "four against one");
            c.AirFriendly = 2;
            Is(c, 3, "four against two is not double, so only outnumbering");
            c.AirHostileTracked = 3; c.AirFriendly = 0;
            Is(c, 3, "three against none is below four");
            c.AirHostileTracked = 5; c.AirFriendly = 2;
            Is(c, 2, "five against two is more than double");
            c.AirFriendly = 3;
            Is(c, 3, "five against three is not");
        }

        private static void ManyContestedSectorsOrAnyOutnumberingIsLevelThree()
        {
            TheaterCensus c = Calm();
            c.SectorsContested = 7;
            Is(c, 4, "seven contested sectors");
            c.SectorsContested = 8;
            Is(c, 3, "eight contested sectors");
            c = Calm();
            c.AirFriendly = 1; c.AirHostileTracked = 2;
            Is(c, 3, "two hostile against one");
            c.AirFriendly = 2;
            Is(c, 4, "equal air is only contact");
        }

        private static void AnyTrackedHostileOrSomeContestedSectorsIsLevelFour()
        {
            TheaterCensus c = Calm();
            c.AirHostileTracked = 1;
            Is(c, 4, "one tracked hostile aircraft");
            c = Calm();
            c.SectorsContested = 1;
            Is(c, 4, "one contested sector");
            c.SectorsContested = 7;
            Is(c, 4, "seven contested sectors");
            c = Calm();
            c.AirFriendly = -1; c.AirHostileTracked = 3;
            Is(c, 4, "tracked hostile air with ours unknown is contact, not an outnumbering");
        }

        private static void NominalNeedsEverythingKnown()
        {
            Is(Calm(), 5, "bases, air and sectors known and quiet");
            TheaterCensus c = Calm(); c.BasesContested = -1;
            Is(c, 0, "bases unknown");
            c = Calm(); c.AirFriendly = -1;
            Is(c, 0, "our air unknown");
            c = Calm(); c.AirHostileTracked = -1;
            Is(c, 0, "hostile air unknown");
            c = Calm(); c.SectorsContested = -1;
            Is(c, 0, "sectors unknown");
        }

        private static void ARuleOnKnownInputsStandsWhileOthersAreUnknown()
        {
            TheaterCensus c = Known();
            c.AirFriendly = 1; c.AirHostileTracked = 5;
            Is(c, 2, "outnumbered with bases and sectors unknown");
            c = Known();
            c.BasesContested = 1;
            Is(c, 2, "a contested base with everything else unknown");
            c = Known();
            c.SectorsContested = 9;
            Is(c, 3, "many contested sectors with everything else unknown");
            c = Known();
            c.SectorsContested = 2;
            Is(c, 4, "some contested sectors with everything else unknown");
        }

        private static void WhyNamesTheWinningRule()
        {
            TheaterThreatWhy why;
            TheaterCensus c = Calm(); c.BasesContested = 2;
            ThreatLevelRules.Level(c, out why); TestAssert.Eq(why, TheaterThreatWhy.BasesContested, "two bases");
            c = Calm(); c.BasesContested = 1; c.AirFriendly = 1; c.AirHostileTracked = 2;
            ThreatLevelRules.Level(c, out why); TestAssert.Eq(why, TheaterThreatWhy.BaseContested, "one base, outnumbered, is named for the base");
            c = Calm(); c.BasesContested = 1;
            ThreatLevelRules.Level(c, out why); TestAssert.Eq(why, TheaterThreatWhy.BaseContested, "one base");
            c = Calm(); c.AirFriendly = 1; c.AirHostileTracked = 4;
            ThreatLevelRules.Level(c, out why); TestAssert.Eq(why, TheaterThreatWhy.AirOutnumbered, "four against one");
            c = Calm(); c.SectorsContested = 8; c.AirFriendly = 1; c.AirHostileTracked = 2;
            ThreatLevelRules.Level(c, out why); TestAssert.Eq(why, TheaterThreatWhy.GroundContact, "sectors are listed before air within level 3");
            c = Calm(); c.AirFriendly = 1; c.AirHostileTracked = 2;
            ThreatLevelRules.Level(c, out why); TestAssert.Eq(why, TheaterThreatWhy.AirOutnumbered, "outnumbered");
            c = Calm(); c.AirHostileTracked = 1; c.SectorsContested = 3;
            ThreatLevelRules.Level(c, out why); TestAssert.Eq(why, TheaterThreatWhy.AirContact, "air contact is listed before sectors within level 4");
            c = Calm(); c.SectorsContested = 3;
            ThreatLevelRules.Level(c, out why); TestAssert.Eq(why, TheaterThreatWhy.GroundContact, "contested sectors");
            ThreatLevelRules.Level(Calm(), out why); TestAssert.Eq(why, TheaterThreatWhy.Nominal, "nominal");
        }
    }
}
