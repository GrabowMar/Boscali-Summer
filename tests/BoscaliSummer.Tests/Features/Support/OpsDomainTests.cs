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
            cyber.BeginLocations();
            int city = cyber.ReportLocation(100, LocationKind.City, 8000f, 0f, 0.0);
            cyber.EndLocations(0.0);
            TestAssert.That(city >= CyberNetwork.TargetBase, "the city lands in a target slot");
            TestAssert.That(cyber.CheckBreach(city, 0.0) == BreachDenial.LowComputing,
                "a fresh network cannot pay the probe");
            TestAssert.That(cyber.TryStartBreach(city, true, 0.0) == BreachDenial.LowComputing,
                "the start re-checks the computing");

            // Bank computing, take the city to stage 2, and the basic tier opens on its radius.
            double now = 0.0;
            double bank = 0.0;
            while (cyber.Computing < 140f && bank < 400.0)
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
            TestAssert.That(cyber.Stage(city) == 1, "the city is at stage 1");
            TestAssert.That(!cyber.AnyTier(1), "stage 1 unlocks nothing");
            double bank2 = now;
            while (cyber.Computing < 140f && bank2 < now + 400.0)
            {
                cyber.Tick(bank2, 0.25f, 0f);
                bank2 += 0.25;
            }
            now = bank2;
            TestAssert.That(cyber.TryStartBreach(city, true, now) == BreachDenial.None, "the second breach opens");
            while (cyber.BreachActive && now < bank2 + 200.0)
            {
                cyber.Tick(now, 0.25f, 0f);
                now += 0.25;
            }
            TestAssert.That(cyber.Stage(city) == 2 && cyber.AnyTier(1), "stage 2 opens the basic tier");
            TestAssert.That(cyber.AbilityCovers(1, 8000f, 0f, now), "the city's radius covers itself");
            TestAssert.That(!cyber.AbilityCovers(1, 60000f, 0f, now), "the radius does not reach across the map");
            TestAssert.That(!cyber.AnyTier(2), "the mid tier still needs stage 3");

            // The ear a trace needs: a stage-2 location's radius over the incident.
            TestAssert.That(cyber.EarCovers(8000f, 0f, now), "a stage-2 location is an ear");
            TestAssert.That(!cyber.EarCovers(60000f, 0f, now), "the ear ends at the radius");

            // A compromised location goes dark until patched.
            TestAssert.That(cyber.TryVerb(CyberVerb.Honeypot, city, now) == CyberDenial.None, "the bait applies");
            cyber.Tick(now + CyberLocations.HoneypotSeconds + 1.0, 0.25f, 0f);
            TestAssert.That(cyber.Node(city).HoneypotUntil == 0.0, "the bait lapses");
        }
    }
}
