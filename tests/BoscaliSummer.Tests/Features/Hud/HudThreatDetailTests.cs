using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>Threat ranking, time to impact and the words of the detail rows.</summary>
    internal static class HudThreatDetailTests
    {
        public static void Run()
        {
            TimeToImpact();
            Ranking();
            Words();
        }

        private static ThreatContact Missile(float range, float tti, bool locked = false, float age = 10f, string seeker = "SARH") =>
            new ThreatContact { Kind = ThreatKind.Missile, Seeker = seeker, RangeM = range, TtiS = tti, Locked = locked, AgeS = age, ClosureMps = 300f };

        private static ThreatContact Emitter(ThreatKind kind, string name, float range, float age = 10f) =>
            new ThreatContact { Kind = kind, Name = name, RangeM = range, TtiS = float.NaN, AgeS = age };

        private static void TimeToImpact()
        {
            TestAssert.Near(ThreatRank.Tti(2400f, 300f), 8f, "2.4 km at 300 m/s is 8 s");
            TestAssert.That(float.IsNaN(ThreatRank.Tti(2400f, 10f)), "A missile barely closing has no time to impact");
            TestAssert.That(float.IsNaN(ThreatRank.Tti(2400f, -50f)), "An opening missile has none either");
            TestAssert.That(float.IsNaN(ThreatRank.Tti(100000f, 200f)), "Beyond 99 s is not shown");
            TestAssert.That(float.IsNaN(ThreatRank.Tti(float.NaN, 300f)) && float.IsNaN(ThreatRank.Tti(1000f, float.NaN)), "Unknown range or closure: unknown");
            TestAssert.That(ThreatRank.IsNew(Missile(1000f, 3f, age: 3f)) && !ThreatRank.IsNew(Missile(1000f, 3f, age: 4.5f)), "A contact pulses for its first four seconds");
        }

        private static void Ranking()
        {
            ThreatContact soon = Missile(1000f, 3f), later = Missile(2400f, 8f), blind = Missile(5000f, float.NaN);
            ThreatContact lockC = Emitter(ThreatKind.Lock, "SA-19", 9400f), spike = Emitter(ThreatKind.Spike, "EWR", 20000f);

            TestAssert.That(ThreatRank.Compare(soon, later) < 0 && ThreatRank.Compare(later, soon) > 0, "The sooner missile ranks first");
            TestAssert.That(ThreatRank.Compare(later, blind) < 0, "A missile with a time to impact outranks one without");
            TestAssert.That(ThreatRank.Compare(blind, lockC) < 0 && ThreatRank.Compare(lockC, spike) < 0, "Missile, then lock, then spike");
            TestAssert.That(ThreatRank.Compare(Missile(1000f, 5f, locked: true), Missile(1000f, 5f)) < 0, "At the same time to impact a locked seeker ranks first");
            TestAssert.That(ThreatRank.Compare(Emitter(ThreatKind.Lock, "A", 5000f), Emitter(ThreatKind.Lock, "B", 9000f)) < 0, "The nearer emitter ranks first");

            var rows = new ThreatContact[ThreatRank.MaxRows];
            int count = 0;
            foreach (ThreatContact c in new[] { spike, blind, lockC, later, soon })
                count = ThreatRank.Insert(rows, count, c);
            TestAssert.That(count == ThreatRank.MaxRows && rows[0].TtiS == 3f && rows[1].TtiS == 8f, "The two best survive whatever order they arrive in");

            count = 0;
            foreach (ThreatContact c in new[] { soon, later, blind, lockC, spike })
                count = ThreatRank.Insert(rows, count, c);
            TestAssert.That(count == 2 && rows[0].TtiS == 3f && rows[1].TtiS == 8f, "...in either order");

            var roomy = new ThreatContact[ThreatRank.MaxRows];
            int one = ThreatRank.Insert(roomy, 0, spike);
            TestAssert.That(one == 1 && roomy[0].Name == "EWR", "A lone spike is the only row");
            one = ThreatRank.Insert(roomy, one, lockC);
            TestAssert.That(one == 2 && roomy[0].Kind == ThreatKind.Lock && roomy[1].Kind == ThreatKind.Spike, "A lock moves ahead of the spike");
        }

        private static void Words()
        {
            string missile = ThreatText.Row(Missile(2400f, 7.2f), AvUnits.Metric);
            TestAssert.That(missile == "SARH 2.4KM TTI 7S", "Missile row: seeker, range, time to impact: " + missile);
            TestAssert.That(ThreatText.Row(Missile(800f, 0.2f, seeker: "IR"), AvUnits.Metric) == "IR 800M TTI 1S", "A last-instant impact still reads 1 s");
            TestAssert.That(ThreatText.Row(Missile(2400f, float.NaN), AvUnits.Metric) == "SARH 2.4KM", "No closure, no time to impact");
            TestAssert.That(ThreatText.Row(Emitter(ThreatKind.Lock, "SA-19", 9400f), AvUnits.Metric) == "SA-19 9.4KM", "Emitter row: its name and range");
            TestAssert.That(ThreatText.Row(Emitter(ThreatKind.Spike, "", float.NaN), AvUnits.Metric) == "RADAR", "An unnamed emitter is RADAR");
            TestAssert.That(ThreatText.Chip(ThreatKind.Missile) == "MSL" && ThreatText.Chip(ThreatKind.Lock) == "LOCK" && ThreatText.Chip(ThreatKind.Spike) == "SPKE", "Chips match the lamps");
        }
    }
}
