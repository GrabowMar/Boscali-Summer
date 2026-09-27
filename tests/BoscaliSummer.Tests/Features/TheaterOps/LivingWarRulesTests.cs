using System.Collections.Generic;
using BoscaliSummer.Features.TheaterOps.Domain;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    internal static class LivingWarRulesTests
    {
        public static void Run()
        {
            var fronts = new List<WarFrontRead>
            {
                new WarFrontRead("held", "Held airfield", 100, 100, .8f, 3, 5, true, true, true),
                new WarFrontRead("opening", "Road", 200, 200, .3f, 4, 1, false, false, true),
                new WarFrontRead("mission", "Mission depot", 300, 300, .3f, 4, 1, true, false, true),
                new WarFrontRead("empty", "Empty", 400, 400, 0f, 0, 0, true, false, false),
            };
            var offers = new List<WarOffer>();
            LivingWarRules.Choose(fronts, TheaterWarPosture.Steady, offers);
            TestAssert.That(offers.Count == 3 && offers[0].Kind == "DEFEND" &&
                offers[0].Front.Key == "held", "a threatened held objective leads the staff choices");
            TestAssert.That(offers[1].Front.Key == "mission" &&
                offers[2].Front.Key == "opening", "mission objectives lead equivalent sector attacks");
            TestAssert.That(LivingWarRules.Opening(0f, .3f, false, false, 150f, 120f) &&
                !LivingWarRules.Opening(0f, .3f, false, false, 100f, 120f),
                "contact pressure opens offers after the cooldown");
        }
    }
}
