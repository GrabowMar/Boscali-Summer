using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    internal static class OpsDomainTests
    {
        public static void Run()
        {
            TestTabs();
            TestFormatting();
            TestAbilityGates();
        }

        private static void TestTabs()
        {
            string[] labels = OpsDomains.TabLabels();
            TestAssert.That(labels.Length == 3, "espionage belongs inside SPEC OPS, not a fourth tab");
            string[] expected = { "SPACE", "CYBER", "SPEC OPS" };
            for (int i = 0; i < expected.Length; i++)
            {
                TestAssert.That(labels[i] == expected[i], "OPS tab " + i + " must read " + expected[i]);
                TestAssert.That(labels[i].Length <= 8, "an OPS tab label must fit its share of the bezel");
                TestAssert.That(!string.IsNullOrEmpty(OpsDomains.Mission(OpsDomains.All[i])),
                    "every OPS domain needs a mission line");
            }
        }

        private static void TestFormatting()
        {
            TestAssert.That(TheaterGrid.Kilometres(12400.0, -3100.0) == "12.4 / -3.1 KM", "grid must read in kilometres");
            TestAssert.That(TheaterGrid.Kilometres(double.NaN, 0.0).StartsWith("—"), "a non-finite grid must not print NaN");
            TestAssert.That(TheaterGrid.Clock(125.4) == "02:05" && TheaterGrid.Clock(3725.0) == "1:02:05",
                "countdowns must read MM:SS, then H:MM:SS");
            TestAssert.That(TheaterGrid.Elapsed(393856.0) == "109:24:16", "GET must read HHH:MM:SS like the Apollo wall clock");
            TestAssert.That(TheaterGrid.Clock(-1.0) == "--:--", "a negative countdown must not print");
        }

        /// <summary>The ability gates: a stage-2 location unlocks the basic tier, a stage-3 one
        /// the mid tier, and only a location's own radius carries an ability to a point. The
        /// tracker verbs reach only where a hacked location's radius does.</summary>
        private static void TestAbilityGates()
        {
            var cyber = new CyberNetwork();
            TestAssert.That(!cyber.AnyTier(1), "a fresh network unlocks nothing");
            TestAssert.That(cyber.CheckBreach(CyberNetwork.TargetBase, 0.0) == BreachDenial.NoCommand,
                "a breach needs Cyber Command");

            cyber.PlaceStatic(1, NodeKind.Command, 0f, 0f);
            int home = cyber.PlaceStatic(2, NodeKind.Base, 5000f, 0f);
            cyber.BeginLocations();
            int city = cyber.ReportLocation(100, LocationKind.City, 8000f, 0f, 0.0);
            cyber.EndLocations(0.0);
            TestAssert.That(city >= CyberNetwork.TargetBase, "the city lands in a target slot");
            TestAssert.That(cyber.CheckBreach(city, 0.0) == BreachDenial.LowComputing,
                "a fresh network cannot pay the probe");
            TestAssert.That(cyber.TryStartBreach(city, true, 0.0) == BreachDenial.LowComputing,
                "the start re-checks the computing");

            TestAssert.That(cyber.EarCovers(8000f, 0f, 0.0) && !cyber.EarCovers(60000f, 0f, 0.0),
                "friendly home infrastructure provides bounded defender warning coverage before any attack");
            // Bank only the resources needed for one operation.
            double now = 0.0;
            double bank = 0.0;
            while (cyber.Computing < 42f && bank < 400.0)
            {
                cyber.Tick(bank, 0.25f, 0f);
                bank += 0.25;
            }
            now = bank;
            TestAssert.That(cyber.TryStartBreach(city, true, now) == BreachDenial.None, "the breach opens");
            while (cyber.BreachActive && now < bank + 200.0)
            {
                cyber.Tick(now, 0.25f, 0f);
                now += 0.25;
            }
            TestAssert.That(cyber.Stage(city) == 3 && cyber.AccessRemaining(now) > 70f,
                "one breach opens a 75-second stage-3 compatibility lease");
            TestAssert.That(cyber.AnyTier(1, now) && cyber.AnyTier(2, now) && !cyber.AnyTier(3, now),
                "the live lease authorizes basic and mid actions, never capstones");
            TestAssert.That(cyber.AbilityCovers(1, 8000f, 0f, now) && cyber.AbilityCovers(2, 8000f, 0f, now),
                "the lease carries both action tiers at its actual site");
            TestAssert.That(!cyber.AbilityCovers(1, 60000f, 0f, now), "the site's radius does not reach across the map");
            TestAssert.That(cyber.TryVerb(CyberVerb.Honeypot, home, now) == CyberDenial.None,
                "defender orders work on friendly infrastructure without a breach");
            TestAssert.That(cyber.ConsumeAccess(8000f, 0f, now), "an accepted effect consumes access at its target");
            TestAssert.That(!cyber.AnyTier(1, now) && !cyber.AbilityCovers(1, 8000f, 0f, now),
                "consumption closes offensive authorization immediately");
            TestAssert.That(cyber.EarCovers(8000f, 0f, now), "the home ear remains after lease consumption");
        }
    }
}
