using BoscaliSummer.Features.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    internal static class ShotTests
    {
        public static void Run()
        {
            Tracking();
            Refusals();
            Words();
        }

        private static void Tracking()
        {
            ShotTrack fresh = ShotMath.Start(4000f, 10f);
            TestAssert.That(fresh.Range == 4000f && fresh.FirstRange == 4000f && fresh.Fraction == 1f,
                "A new track must open full-scale at its first range");
            TestAssert.That(float.IsNaN(fresh.Eta) && fresh.Closure == 0f,
                "A track with one sample must not invent a countdown");

            ShotTrack closing = ShotMath.Update(fresh, 3600f, 11f);
            TestAssert.That(closing.Closure == 400f,
                "Closure must be the range lost per second");
            TestAssert.That(closing.Eta == 9f,
                "ETA must be the remaining range over the closure");
            TestAssert.That(System.Math.Abs(closing.Fraction - 0.9f) < 0.0001f,
                "The bar must drain against the first-seen range");

            ShotTrack opening = ShotMath.Update(fresh, 4400f, 11f);
            TestAssert.That(opening.Closure == -400f && float.IsNaN(opening.Eta),
                "An opening shot must read as not closing, never as a negative countdown");
            TestAssert.That(opening.Fraction == 1f,
                "A shot past its first range must pin the bar full, not overfill it");

            ShotTrack arrived = ShotMath.Update(closing, 0f, 20f);
            TestAssert.That(arrived.Fraction == 0f && float.IsNaN(arrived.Eta),
                "A shot with no range left must read empty with no countdown");

            ShotTrack stale = ShotMath.Update(fresh, 100f, 99f);
            TestAssert.That(stale.Closure == 0f && float.IsNaN(stale.Eta) && stale.FirstRange == 4000f,
                "A sample from another era must reset the countdown but keep the bar's scale");

            ShotTrack slow = ShotMath.Update(ShotMath.Start(100f, 0f), 99.8f, 1f);
            TestAssert.That(float.IsNaN(slow.Eta),
                "A hover must not dress noise up as a six-hundred-second intercept");

            ShotTrack far = ShotMath.Update(ShotMath.Start(2000000f, 0f), 1999999f, 1f);
            TestAssert.That(far.Eta == ShotMath.MaxEta,
                "A crawl from beyond the horizon must pin the countdown, not print hours");
        }

        private static void Refusals()
        {
            ShotTrack same = ShotMath.Update(ShotMath.Start(500f, 5f), 400f, 5f);
            TestAssert.That(same.Range == 400f && same.Closure == 0f && float.IsNaN(same.Eta),
                "A sample with no time behind it must move the range but not the countdown");

            ShotTrack backwards = ShotMath.Update(ShotMath.Start(500f, 5f), 400f, 4f);
            TestAssert.That(backwards.Closure == 0f && float.IsNaN(backwards.Eta),
                "A clock that runs backwards must not mint closure");

            ShotTrack garbage = ShotMath.Update(ShotMath.Start(500f, 5f), float.NaN, 6f);
            TestAssert.That(garbage.Range == 500f && garbage.Closure == 0f,
                "An unreadable range must hold the last one, never poison the track");

            ShotTrack negative = ShotMath.Start(-12f, 0f);
            TestAssert.That(negative.Range == 0f && negative.Fraction == 0f,
                "A negative range must read as arrived, not as a bar below empty");

            ShotTrack timeless = ShotMath.Update(ShotMath.Start(500f, 5f), 400f, float.NaN);
            TestAssert.That(timeless.Range == 400f && timeless.Closure == 0f,
                "A sample with no clock must move the range but mint no countdown");
            ShotTrack recovered = ShotMath.Update(timeless, 300f, 6f);
            TestAssert.That(recovered.Closure == 100f && recovered.Eta == 3f,
                "The clock after a timeless sample must pick up where the last good one left off");
        }

        private static void Words()
        {
            TestAssert.That(ShotCopy.ShortName("F-16C Fighting Falcon") == "F-16C FIGHTING",
                "A target name must read uppercased and fit the row");
            TestAssert.That(ShotCopy.ShortName(" B ") == "B",
                "A short name must survive untouched");
            TestAssert.That(ShotCopy.ShortName(null) == "TGT" && ShotCopy.ShortName("  ") == "TGT",
                "A shot with no name must still print something readable");

            TestAssert.That(ShotCopy.SeekerTag("arh") == "ARH",
                "A seeker tag must read uppercased");
            TestAssert.That(ShotCopy.SeekerTag("") == "MSL",
                "A shot with no seeker must still say missile");

            TestAssert.That(ShotCopy.EtaText(12.4f) == "12s",
                "A countdown must print whole seconds");
            TestAssert.That(ShotCopy.EtaText(float.NaN) == "--" && ShotCopy.EtaText(5000f) == "--",
                "No intercept must print as a dash, never as infinity");
            TestAssert.That(ShotCopy.EtaText(float.PositiveInfinity) == "--" && ShotCopy.EtaText(-5f) == "--",
                "An infinite or negative countdown must print as a dash too");
        }
    }
}
