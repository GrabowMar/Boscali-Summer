using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The cumulus lattice inside the field (spec 6): the sample is the lattice, the class skies reach their cover, the nearest-group
    /// finder, and the lattice as a pure function of the tape identity.</summary>
    internal static class LatticeFieldTests
    {
        private const float Half = 40960f;

        public static void Run()
        {
            SampleIsTheLatticeBlock();
            NearestCloudIsAGroupWithAnExtent();
            StreetSkyHasStreetZones();
            LatticeFollowsTheTapeIdentityOnly();
        }

        private static void SampleIsTheLatticeBlock()
        {
            foreach (WeatherKey key in new[]
            {
                new WeatherKey(11u, 0f, false, (byte)WeatherRegimeType.Scattered),
                new WeatherKey(12u, 0f, false, skyPin: 14),
                new WeatherKey(13u, 0f, false, skyPin: 9),
            })
            {
                var field = new WeatherField();
                field.Build(key, 600f, Half, Half);
                for (int i = 0; i < 120; i++)
                {
                    float x = (i * 7919 % 80000) - 40000f, z = (i * 104729 % 70000) - 35000f;
                    WeatherPoint p = field.Sample(x, z);
                    field.Frame.ToTape(x, z, out float qx, out float qz);
                    CumulusLattice.Sample(field.Lattice, null, qx, qz, CumulusLattice.Half + 1, out float clear, out float shape, out _);
                    TestAssert.That(Math.Abs(p.ClusterCover - (1f - clear)) < 1e-6f && p.CellShape >= shape - 1e-6f,
                        "the sample is the lattice block around the point (key " + key.Seed + ")");
                }
            }
        }

        private static void NearestCloudIsAGroupWithAnExtent()
        {
            var field = new WeatherField();
            field.Build(new WeatherKey(21u, 0f, false, (byte)WeatherRegimeType.Scattered), 600f, Half, Half);
            TestAssert.That(field.FindNearestCloud(0f, 0f, out FieldFeature near), "a scattered sky has a cloud near the centre");
            TestAssert.That(near.Strength >= CumulusLattice.ActiveStrength && near.Radius >= 2000f && near.Top > near.Base + 200f, "it has a radius, a base and a top");
            WeatherPoint under = field.Sample(near.X, near.Z);
            TestAssert.That(Math.Abs(near.Base - under.CloudBase) < 0.01f, "its base is the local cloud base");
            for (int i = 1; i < 6; i++)
            {
                TestAssert.That(field.FindNearestCloud(i * 21000f, -i * 13000f, out FieldFeature other, 0.5f, 6), "and so do other places");
                float d = (float)Math.Sqrt((other.X - i * 21000f) * (other.X - i * 21000f) + (other.Z + i * 13000f) * (other.Z + i * 13000f));
                TestAssert.That(d < 6 * CumulusLattice.Cell, "the nearest cloud is within the rings searched");
            }
            var clear = new WeatherField();
            clear.Build(new WeatherKey(21u, 0f, false, (byte)WeatherRegimeType.Clear), 600f, Half, Half);
            TestAssert.That(!clear.FindNearestCloud(0f, 0f, out _) && clear.ActiveGroupCount(0f, 0f, 60000f) == 0, "a clear sky has none");
            TestAssert.That(field.ActiveGroupCount(0f, 0f, 60000f) > 2, "a scattered one counts several");
        }

        private static void StreetSkyHasStreetZones()
        {
            var field = new WeatherField();
            field.Build(new WeatherKey(31u, 0f, false, skyPin: 9), 600f, Half, Half);
            int streets = 0;
            for (int gz = -4; gz <= 4; gz++)
            for (int gx = -4; gx <= 4; gx++)
            {
                int n = CumulusLattice.Fetch(field.Lattice, null, gx, gz, out CumulusGroup[] groups);
                for (int i = 0; i < n; i++)
                {
                    TestAssert.That(groups[i].Aspect >= 3f, "every group of the Cloud streets sky is a street zone");
                    streets++;
                }
            }
            TestAssert.That(streets > 5, "the street sky has groups: " + streets);
        }

        private static void LatticeFollowsTheTapeIdentityOnly()
        {
            var a = new WeatherField(); var b = new WeatherField(); var c = new WeatherField();
            WeatherKey key = new WeatherKey(41u, 0f, true, (byte)WeatherRegimeType.Scattered, headingDeg: 130f);
            a.Build(key, 100f, Half, Half);
            b.Build(key.Rebased(500f, false).WithSets(1).WithAnchor(5f, 6f), 100f, Half, Half);
            c.Build(key.WithLayoutSalt(1), 100f, Half, Half);
            int differs = 0;
            for (int i = 0; i < 60; i++)
            {
                float x = i * 1100f - 33000f, z = 20000f - i * 800f;
                TestAssert.That(a.Sample(x, z).ClusterCover == b.Sample(x, z).ClusterCover, "Epoch, Offset, Dynamic, Sets and Anchor never move the lattice");
                if (Math.Abs(a.Sample(x, z).ClusterCover - c.Sample(x, z).ClusterCover) > 0.01f) differs++;
            }
            TestAssert.That(differs > 10, "a re-roll gives another lattice: " + differs);
        }
    }
}
