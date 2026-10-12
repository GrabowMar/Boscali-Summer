using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The storm lattice (spec 6, 6.1, 10.1): the resolver, the finite circular supports and the 32 km reach rule, the 25 x 25
    /// enumeration lightning uses, and the density the lattice gives the theater.</summary>
    internal static class StormLatticeTests
    {
        public static void Run()
        {
            ResolverLaw();
            SupportsAreFiniteCircularAndInReach();
            FiveByFiveEqualsSevenBySeven();
            EnumerateIsNearestFirstAndCapped();
            EventsDoNotDependOnTheObserver();
            DensityTable();
        }

        internal static LatticeSource Source(uint salt, float convective, float severity, uint layoutSeed = 0u, float headingDeg = 90f)
        {
            var row = new SkyParams { Convective = convective, Severity = severity, CloudBase = 1300f };
            return new LatticeSource(salt, layoutSeed == 0u ? salt ^ 0x77u : layoutSeed, TapeSampler.Constant(row, LowGenus.Cumulonimbus), new TapeFrame(headingDeg, 0f));
        }

        private static void ResolverLaw()
        {
            LatticeSource src = Source(5u, 0.8f, 0.5f);
            var histogram = new int[5];
            var seeds = new HashSet<uint>();
            int cells = 0, resolved = 0;
            for (int gz = -60; gz < 60; gz++)
            for (int gx = -60; gx < 60; gx++)
            {
                int count = 0;
                for (int k = 0; k < 5; k++)
                {
                    if (!StormLattice.Resolve(src, gx, gz, k, out StormCell c)) continue;
                    count++; resolved++;
                    TestAssert.That(c.X >= gx * StormLattice.Cell && c.X < (gx + 1) * StormLattice.Cell && c.Z >= gz * StormLattice.Cell && c.Z < (gz + 1) * StormLattice.Cell,
                        "a cell centre stays inside its own lattice cell");
                    TestAssert.That(c.Radius >= StormLattice.RadiusMin && c.Radius <= StormLattice.RadiusMax, "the radius is the Cumulonimbus range 3-5.3 km");
                    float wanted = WeatherMath.Smoothstep(c.Rank * 0.7f, c.Rank * 0.7f + 0.35f, 0.8f);
                    TestAssert.That(Math.Abs(c.Level - 0.8f) < 1e-6f && Math.Abs(c.Strength - wanted) < 1e-6f, "activation is read at the centre: level = Convective, strength = smoothstep");
                    TestAssert.That(Math.Abs(c.PeakRain - (30f + 90f * 0.5f)) < 1e-4f, "peak rain is 30 + 90 * Severity");
                    TestAssert.That(c.Base >= 400f && c.Depth > 1200f && c.Top <= c.TopMax + 1e-3f, "base and depth are sane");
                    seeds.Add(c.Seed);
                }
                histogram[count]++; cells++;
            }
            TestAssert.That(histogram[4] == 0, "a lattice cell holds 0 to 3 cells");
            for (int c = 0; c <= 3; c++)
                TestAssert.That(Math.Abs(histogram[c] - cells / 4f) < 0.1f * cells / 4f, c + " candidates in " + histogram[c] + " of " + cells + " lattice cells, expected " + cells / 4f);
            TestAssert.That(seeds.Count > resolved * 0.999f, "every cell has its own seed");
            LatticeSource sameLayout = Source(6u, 0.8f, 0.5f, 5u ^ 0x77u);
            StormLattice.Resolve(src, 3, 4, 0, out StormCell a);
            StormLattice.Resolve(sameLayout, 3, 4, 0, out StormCell b);
            TestAssert.That(a.Seed == b.Seed && a.X != b.X, "the cell seed is the layout's (Seed and LayoutSalt), the sites are the tape's");
            LatticeSource calm = Source(5u, 0f, 0f);
            TestAssert.That(StormLattice.ResolveCell(calm, 3, 4, new StormCell[3]) == 0, "no convection, no cells");
        }

        private static void SupportsAreFiniteCircularAndInReach()
        {
            LatticeSource src = Source(9u, 1f, 1f);
            int tested = 0;
            for (int gz = -12; gz < 12; gz++)
            for (int gx = -12; gx < 12; gx++)
            for (int k = 0; k < 3; k++)
            {
                if (!StormLattice.Resolve(src, gx, gz, k, out StormCell c)) continue;
                c.Strength = 1f;
                tested++;
                float reach = Math.Max(Math.Max(StormCell.CoverSupport, StormCell.WindSupport), Math.Max(StormCell.HaloSupport, StormCell.LightningSupport)) * c.Radius;
                TestAssert.That(reach <= ReachLimit && Math.Abs(c.Reach - reach) < 1f,
                    "max(2.9R, 4R, 5.5R, 6R) <= 32 km = 2 cells: " + reach);
                foreach (float angle in new[] { 0f, 0.7f, 2.1f, 4.4f })
                {
                    float dx = (float)Math.Cos(angle), dz = (float)Math.Sin(angle);
                    float Rain(float d) => c.RainAt(c.X + dx * d, c.Z + dz * d);
                    TestAssert.That(Math.Abs(Rain(0.9f * c.Radius) - c.RainAt(c.X + 0.9f * c.Radius, c.Z)) < 1e-3f, "cells are circular: rain does not depend on the direction");
                    TestAssert.That(Rain(StormCell.HaloSupport * c.Radius) == 0f && Rain(StormCell.HaloSupport * c.Radius * 1.5f) == 0f, "rain has no reach past 5.5 R");
                    TestAssert.That(c.CoverAt(c.X + dx * 2.2f * c.Radius, c.Z + dz * 2.2f * c.Radius) == 0f, "cover is gone by 2.2 R");
                    TestAssert.That(c.ShapeAt(c.X + dx * StormCell.CoverSupport * c.Radius, c.Z + dz * StormCell.CoverSupport * c.Radius) == 0f &&
                        c.CoreAt(c.X + dx * StormCell.CoverSupport * c.Radius, c.Z + dz * StormCell.CoverSupport * c.Radius) == 0f, "shape and core end at 2.9 R");
                    float wd = StormCell.WindSupport * c.Radius;
                    c.WindAt(c.X + dx * wd, c.Z + dz * wd, out float ox, out float oz, out float up, out float turbulence);
                    TestAssert.That(ox == 0f && oz == 0f && up == 0f && turbulence == 0f, "wind and turbulence end at 4 R");
                    TestAssert.That(c.LightningWeight(c.X + dx * StormCell.LightningSupport * c.Radius, c.Z + dz * StormCell.LightningSupport * c.Radius) == 0f, "lightning ends at 6 R");
                    // No step at a support edge: just inside it every function is already negligible.
                    c.WindAt(c.X + dx * wd * 0.999f, c.Z + dz * wd * 0.999f, out ox, out oz, out up, out turbulence);
                    TestAssert.That(Math.Abs(ox) + Math.Abs(oz) + Math.Abs(up) + turbulence < 0.02f && Rain(StormCell.HaloSupport * c.Radius * 0.999f) < 0.02f &&
                        c.LightningWeight(c.X + dx * 5.999f * c.Radius, c.Z + dz * 5.999f * c.Radius) < 0.02f, "and nothing steps at the edge of a support");
                }
                TestAssert.That(c.RainAt(c.X, c.Z) > 0.9f * c.PeakRain && c.CoverAt(c.X, c.Z) == 1f, "full strength: the core carries the peak rain and full cover");
            }
            TestAssert.That(tested > 500, "cells were tested: " + tested);
        }

        private const float ReachLimit = StormLattice.Half * StormLattice.Cell;

        private static void FiveByFiveEqualsSevenBySeven()
        {
            for (uint seed = 1; seed <= 200; seed++)
            {
                LatticeSource src = Source(seed * 6007u, 0.9f, 0.7f, 0u, seed * 31f);
                float x = ((int)(seed * 7919u % 90000u)) - 45000f, z = ((int)(seed * 104729u % 60000u)) - 30000f;
                float a = Block(src, x, z, StormLattice.Half), b = Block(src, x, z, StormLattice.Half + 1);
                TestAssert.That(a == b, "seed " + seed + ": a 5 x 5 block holds every cell that can touch the point (" + a + " vs " + b + ")");
            }
        }

        /// <summary>Everything the field reads from the cells at a point, summed over a (2 half + 1)^2 block of lattice cells.</summary>
        private static float Block(in LatticeSource src, float qx, float qz, int half)
        {
            float sum = 0f;
            int cx = LatticeHash.CellOf(qx, StormLattice.Cell), cz = LatticeHash.CellOf(qz, StormLattice.Cell);
            for (int gz = cz - half; gz <= cz + half; gz++)
            for (int gx = cx - half; gx <= cx + half; gx++)
            {
                int n = StormLattice.Fetch(src, null, gx, gz, out StormCell[] cells);
                for (int i = 0; i < n; i++)
                {
                    StormCell c = cells[i];
                    c.WindAt(qx, qz, out float ox, out float oz, out float up, out float turbulence);
                    sum += c.RainAt(qx, qz) + c.CoverAt(qx, qz) + c.ShapeAt(qx, qz) + c.CoreAt(qx, qz) + ox + oz + up + turbulence + c.LightningWeight(qx, qz);
                }
            }
            return sum;
        }

        private static void EnumerateIsNearestFirstAndCapped()
        {
            var kept = new StormCell[StormLattice.EnumerateCap];
            int capped = 0;
            for (uint seed = 1; seed <= 12; seed++)
            {
                LatticeSource src = Source(seed * 977u, 0.9f, 0.5f);
                float qx = seed * 5300f - 30000f, qz = 17000f - seed * 3100f;
                int count = StormLattice.Enumerate(src, qx, qz, StormLattice.EnumerateRange, kept);
                // Brute force: every active cell in the same 25 x 25 block within range, sorted by distance then id.
                var all = new List<StormCell>();
                int cx = LatticeHash.CellOf(qx, StormLattice.Cell), cz = LatticeHash.CellOf(qz, StormLattice.Cell);
                var here = new StormCell[3];
                for (int gz = cz - 12; gz <= cz + 12; gz++)
                for (int gx = cx - 12; gx <= cx + 12; gx++)
                {
                    int n = StormLattice.ResolveCell(src, gx, gz, here);
                    for (int i = 0; i < n; i++)
                        if (here[i].Strength >= StormLattice.ActiveStrength && Dist(here[i], qx, qz) <= StormLattice.EnumerateRange) all.Add(here[i]);
                }
                all.Sort((p, q) =>
                {
                    int byDistance = Dist(p, qx, qz).CompareTo(Dist(q, qx, qz));
                    if (byDistance != 0) return byDistance;
                    if (p.Gx != q.Gx) return p.Gx.CompareTo(q.Gx);
                    return p.Gz != q.Gz ? p.Gz.CompareTo(q.Gz) : p.K.CompareTo(q.K);
                });
                TestAssert.That(count == Math.Min(StormLattice.EnumerateCap, all.Count), "seed " + seed + ": the cap is 24 and nothing less when more are out (" + all.Count + " within 180 km)");
                if (all.Count > StormLattice.EnumerateCap) capped++;
                for (int i = 0; i < count; i++)
                    TestAssert.That(kept[i].Gx == all[i].Gx && kept[i].Gz == all[i].Gz && kept[i].K == all[i].K,
                        "seed " + seed + ": slot " + i + " is the i-th nearest active cell, ties by cell id");
                TestAssert.That(count == 0 || Dist(kept[count - 1], qx, qz) <= Dist(all[Math.Min(count, all.Count - 1)], qx, qz) + 1e-3f,
                    "no cell nearer than the last kept one was dropped");
                int again = StormLattice.Enumerate(src, qx, qz, StormLattice.EnumerateRange, new StormCell[StormLattice.EnumerateCap]);
                TestAssert.That(again == count, "a pure function of the tape and the point");
            }
            TestAssert.That(capped >= 6, "the cap really bites in a stormy sky: " + capped + " of 12");
            LatticeSource quiet = Source(3u, 0.1f, 0f);
            TestAssert.That(StormLattice.Enumerate(quiet, 0f, 0f, StormLattice.EnumerateRange, kept) <= 3, "a calm sky enumerates almost nothing");
        }

        private static float Dist(in StormCell c, float qx, float qz)
        {
            float dx = c.X - qx, dz = c.Z - qz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static void EventsDoNotDependOnTheObserver()
        {
            LatticeSource src = Source(41u, 0.95f, 0.8f);
            var frame = src.Frame;
            var a = new StormCell[StormLattice.EnumerateCap];
            var b = new StormCell[StormLattice.EnumerateCap];
            int na = StormLattice.Enumerate(src, 0f, 0f, StormLattice.EnumerateRange, a);
            int nb = StormLattice.Enumerate(src, 4000f, -3000f, StormLattice.EnumerateRange, b);
            int common = 0, events = 0;
            for (int i = 0; i < na; i++)
            for (int j = 0; j < nb; j++)
            {
                if (a[i].Gx != b[j].Gx || a[i].Gz != b[j].Gz || a[i].K != b[j].K) continue;
                common++;
                for (int slot = 0; slot < 400; slot++)
                {
                    bool x = StormLightning.TryCell(a[i], frame, slot, out LightningEvent first);
                    bool y = StormLightning.TryCell(b[j], frame, slot, out LightningEvent second);
                    TestAssert.That(x == y && (!x || (first.Time == second.Time && first.X == second.X && first.Z == second.Z && first.Seed == second.Seed)),
                        "a cell's event is a pure function of the cell and the slot: two observers see the same one");
                    if (x) events++;
                }
            }
            TestAssert.That(common >= 18 && events > 20, "two observers 5 km apart share most of their 24 cells (" + common + ") and the strikes are real (" + events + ")");
        }

        private static void DensityTable()
        {
            // Informational: how many active cells stand within 64 km of the theater centre at each Convective value (13,400 km^2 of theater).
            foreach (float convective in new[] { 0.15f, 0.25f, 0.30f, 0.45f, 0.60f, 0.70f, 1.00f })
            {
                double sum = 0; const int runs = 30;
                for (uint seed = 1; seed <= runs; seed++)
                    sum += StormLattice.CountActive(Source(seed * 313u, convective, 0.5f), null, 0f, 0f, 64000f);
                Console.WriteLine("  storm cells within 64 km at Convective " + convective.ToString("0.00") + ": " + (sum / runs).ToString("0.0"));
                if (convective <= 0.15f) TestAssert.That(sum / runs < 6.0, "isolated cells only at Convective 0.15");
            }
        }
    }
}
