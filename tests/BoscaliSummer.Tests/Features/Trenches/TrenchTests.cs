using System;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Features.Trenches.Domain;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.Trenches
{
    internal static class TrenchTests
    {
        public static void Run()
        {
            TestBuildableGround();
            TestResample();
            TestWindowByArc();
            TestNormalsAndClosure();
            TestTraverseWave();
            TestDensifyAndWave();
            TestRoutePlanning();
            TestDefensibleSiting();
            TestSitingNudges();
            TestPressureOrder();
            TestRunSplitting();
            TestStageRules();
            TestBaySchedule();
            TestSideResolution();
            TestContestedFrontSide();
            TestHoldFloors();
            TestFieldAbandonment();
            TestBeltLadder();
        }

        /// <summary>
        /// Hysteresis on a moving front: the planner digs only well inside its own contested
        /// band, while a dug line is given up only when the field reads deep enemy. Live
        /// sessions dug at -0.5 and abandoned at -0.5, so a hot front killed every position at
        /// birth.
        /// </summary>
        private static void TestHoldFloors()
        {
            TestAssert.That(TrenchTraceMath.DigHoldFloor > TrenchTraceMath.HoldOwnSideFloor,
                "Digging needs firmer ground than staying does");
            TestAssert.That(TrenchTraceMath.CanDig(0.3f) && TrenchTraceMath.CanDig(-0.2f) &&
                !TrenchTraceMath.CanDig(-0.4f) && !TrenchTraceMath.CanDig(float.NaN),
                "Candidate ground is the faction's side or the near edge of its contested band");
            TestAssert.That(TrenchTraceMath.HoldsOwnSide(-0.4f) && TrenchTraceMath.HoldsOwnSide(-0.7f) &&
                !TrenchTraceMath.HoldsOwnSide(-0.8f),
                "A dug line stays through the contested band and yields only to deep enemy ground");
        }

        /// <summary>
        /// The field abandons a position only after a dig-in grace and a sustained hostile
        /// read at its centre; a single hostile sample never ends a line.
        /// </summary>
        private static void TestFieldAbandonment()
        {
            float dug = 100f;
            float grace = TrenchTraceMath.DigInGraceSeconds;
            float sustain = TrenchTraceMath.AbandonSeconds;
            TestAssert.That(grace >= 60f && sustain >= 10f, "Grace and sustain windows are real minutes and seconds");
            TestAssert.That(!TrenchTraceMath.FieldAbandons(dug + grace + 500f, dug, -1f),
                "A centre that reads own side is never abandoned");
            TestAssert.That(!TrenchTraceMath.FieldAbandons(dug + grace - 1f, dug, dug),
                "Inside the dig-in grace the field cannot end a position");
            TestAssert.That(!TrenchTraceMath.FieldAbandons(dug + grace + sustain, dug, dug + grace + sustain - 1f),
                "One hostile sample after the grace is not enough");
            TestAssert.That(TrenchTraceMath.FieldAbandons(dug + grace + sustain, dug, dug + grace),
                "A hostile read sustained past the grace abandons the line");
            TestAssert.That(TrenchTraceMath.FieldAbandons(dug + grace, dug, dug),
                "A line hostile since it was dug is abandoned the moment the grace ends");
        }

        /// <summary>
        /// Belt traces try shallower depths, accept shorter runs than the fire line, and stop
        /// holding the defender budget after a bounded number of refusals.
        /// </summary>
        private static void TestBeltLadder()
        {
            TestAssert.That(Near(TrenchTraceMath.BeltDepth(TrenchStage.FireTrench, 0), TrenchTraceMath.SupportDepth) &&
                Near(TrenchTraceMath.BeltDepth(TrenchStage.Support, 0), TrenchTraceMath.RedoubtDepth),
                "The first rung is the doctrine depth of the trace the stage adds");
            for (int rung = 1; rung < TrenchTraceMath.BeltLadderLength; rung++)
            {
                float support = TrenchTraceMath.BeltDepth(TrenchStage.FireTrench, rung);
                float redoubt = TrenchTraceMath.BeltDepth(TrenchStage.Support, rung);
                TestAssert.That(support < TrenchTraceMath.BeltDepth(TrenchStage.FireTrench, rung - 1) &&
                    support > TrenchTraceMath.FireDepth + 10f,
                    "Each support rung is shallower but still behind the fire line");
                TestAssert.That(redoubt < TrenchTraceMath.BeltDepth(TrenchStage.Support, rung - 1) &&
                    redoubt > TrenchTraceMath.SupportDepth,
                    "Each redoubt rung is shallower but still behind the support line");
            }
            TestAssert.That(float.IsNaN(TrenchTraceMath.BeltDepth(TrenchStage.FireTrench, TrenchTraceMath.BeltLadderLength)) &&
                float.IsNaN(TrenchTraceMath.BeltDepth(TrenchStage.Redoubt, 0)),
                "Past the ladder, or for a stage that adds no belt trace, there is no depth");
            TestAssert.That(TrenchTraceMath.BeltMinRunLength < TrenchTraceMath.MinRunLength &&
                TrenchTraceMath.BeltMinRunLength >= 50f,
                "A rear trace may be shorter than the fire line but is still a real run");
            TestAssert.That(!TrenchTraceMath.AdvancesWithoutBelt(0) && !TrenchTraceMath.AdvancesWithoutBelt(2) &&
                TrenchTraceMath.AdvancesWithoutBelt(3) && TrenchTraceMath.AdvancesWithoutBelt(9),
                "Three refusals let the stage advance without its belt trace");
        }

        /// <summary>
        /// The owning side is read from the sign of the signed control field, and a field that
        /// never separates fails closed instead of guessing.
        /// </summary>
        private static void TestSideResolution()
        {
            // Control falls off toward the enemy: the positive side is the own side.
            TestAssert.That(TrenchTraceMath.TryResolveInward(0f, 0f, 1f, 0f, (x, _) => -x, out float ix, out _) &&
                ix < 0f, "The probe points at the side whose control value is positive");
            TestAssert.That(TrenchTraceMath.TryResolveInward(0f, 0f, 1f, 0f, (x, _) => x, out ix, out _) &&
                ix > 0f, "The sign of the field picks the side, not the normal's direction");
            TestAssert.That(!TrenchTraceMath.TryResolveInward(0f, 0f, 1f, 0f, (_, _) => 0f, out _, out _),
                "A field that never separates fails closed");
            TestAssert.That(!TrenchTraceMath.TryResolveInward(0f, 0f, 1f, 0f, (_, _) => float.NaN, out _, out _),
                "Unknown control never picks a side");
            // A flat plateau inside the cell still resolves from the deeper probe.
            TestAssert.That(TrenchTraceMath.TryResolveInward(0f, 0f, 1f, 0f,
                (x, _) => Math.Abs(x) < 100f ? 0f : -Math.Sign(x), out ix, out _) && ix < 0f,
                "The probe deepens until the control field separates");
            TestAssert.That(TrenchTraceMath.HoldsOwnSide(0f) && TrenchTraceMath.HoldsOwnSide(0.2f) &&
                TrenchTraceMath.HoldsOwnSide(-0.2f) && !TrenchTraceMath.HoldsOwnSide(-0.9f) &&
                !TrenchTraceMath.HoldsOwnSide(float.NaN),
                "The faction's own side and its contested band count as held ground, deep enemy cells do not");
        }

        /// <summary>
        /// The trap the first curve planner fell into: a real front is engaged, so the cells its
        /// trace runs through answer "contested" on both sides and a Friendly-only gate can never
        /// place a trench. The signed control field still tells the owning side at every station.
        /// </summary>
        private static void TestContestedFrontSide()
        {
            var grid = new TacticalSectorGrid(1000f, 100000f);
            for (int step = 0; step < 40; step++)
            {
                grid.Clear();
                grid.RegisterNode(1, "Friendly base", -20000f, 0f, SectorControl.Friendly, 25000f, true);
                grid.RegisterNode(2, "Hostile base", 20000f, 0f, SectorControl.Hostile, 25000f, true);
                for (int z = -30000; z <= 30000; z += 3000)
                {
                    grid.AddTroopPresence(-2000f, z, 5f, false);
                    grid.AddTroopPresence(2000f, z, 5f, true);
                }
                grid.EvaluateSectors(0.5f);
            }

            var points = new FrontlineTracePoint[FrontlineTraceLimits.MaximumPoints];
            var lengths = new int[FrontlineTraceLimits.MaximumTraces];
            var pressure = new float[FrontlineTraceLimits.MaximumTraces];
            int traces = grid.CopyFrontlineTraces(points, lengths, pressure);
            TestAssert.That(traces > 0, "An engaged front still yields an ordered trace");

            int offset = 0, stations = 0, contestedBothSides = 0, resolved = 0;
            for (int t = 0; t < traces; t++)
            {
                for (int i = 0; i < lengths[t]; i++)
                {
                    int here = offset + i;
                    int before = offset + Math.Max(0, i - 1);
                    int after = offset + Math.Min(lengths[t] - 1, i + 1);
                    float dx = points[after].X - points[before].X;
                    float dz = points[after].Z - points[before].Z;
                    float length = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (length < 1e-4f) continue;
                    float nx = -dz / length, nz = dx / length;
                    float x = points[here].X, z = points[here].Z;
                    stations++;

                    bool friendlyPlus = Friendly(grid, x + nx * 40f, z + nz * 40f);
                    bool friendlyMinus = Friendly(grid, x - nx * 40f, z - nz * 40f);
                    if (!friendlyPlus && !friendlyMinus) contestedBothSides++;

                    float holdPlus = Hold(grid, x + nx * 40f, z + nz * 40f);
                    float holdMinus = Hold(grid, x - nx * 40f, z - nz * 40f);
                    if (Math.Abs(holdPlus - holdMinus) < TrenchTraceMath.HoldSeparation) continue;
                    float toPlus = (x + nx * 40f + 20000f) * (x + nx * 40f + 20000f) + (z + nz * 40f) * (z + nz * 40f);
                    float toMinus = (x - nx * 40f + 20000f) * (x - nx * 40f + 20000f) + (z - nz * 40f) * (z - nz * 40f);
                    TestAssert.That((holdPlus > holdMinus) == (toPlus < toMinus),
                        "The stronger control value is the ground nearer the holding faction");
                    resolved++;
                }
                offset += lengths[t];
            }

            TestAssert.That(stations > 10, "The engaged front is long enough to judge");
            TestAssert.That(contestedBothSides > stations / 2,
                "Most of a real front runs through cells that answer contested on both sides");
            TestAssert.That(resolved == stations,
                "The signed control field resolves the owning side at every front station");
        }

        private static bool Friendly(TacticalSectorGrid grid, float x, float z)
            => grid.WorldToCell(x, z, out int c, out int r) &&
                grid.GetSectorControl(c, r) == SectorControl.Friendly;

        private static float Hold(TacticalSectorGrid grid, float x, float z)
            => grid.WorldToCell(x, z, out int c, out int r) ? grid.GetSectorHoldStrength(c, r) : 0f;

        private static void TestBuildableGround()
        {
            TestAssert.That(TrenchTraceMath.IsBuildableGround(20f, 1f), "Dry level ground accepts trenches");
            TestAssert.That(TrenchTraceMath.IsBuildableGround(20f, 0.98f) &&
                !TrenchTraceMath.IsBuildableGround(20f, 0.9f), "Ground on a hill accepts trenches, cliffs refuse them");
            TestAssert.That(!TrenchTraceMath.IsBuildableGround(2f, 1f), "Shore and water reject trenches");
            TestAssert.That(!TrenchTraceMath.IsBuildableGround(float.NaN, 1f), "Unknown ground fails closed");
        }

        private static void TestResample()
        {
            // A sparse two-point trace is one straight Bezier run at the requested spacing.
            var x = new[] { 0f, 100f };
            var z = new[] { 0f, 0f };
            var outX = new float[64];
            var outZ = new float[64];
            int count = TrenchTraceMath.Resample(x, z, 2, 10f, false, outX, outZ);
            TestAssert.That(count >= 10 && count <= 12, "A 100m run yields about ten 10m stations");
            TestAssert.That(outX[0] == 0f && outZ[0] == 0f, "The near end stays put");
            TestAssert.That(Near(outX[count - 1], 100f), "The far end stays put");
            float maximumStep = 0f;
            for (int i = 1; i < count; i++)
                maximumStep = Math.Max(maximumStep, Math.Abs(outX[i] - outX[i - 1]));
            TestAssert.That(maximumStep <= 10.001f, "Resampling never exceeds the requested spacing");

            // A bent chain eases through its corner: no station-to-station chord turns more
            // than a fraction of the original 90 degrees, instead of one abrupt hinge.
            var bentX = new[] { 0f, 100f, 100f };
            var bentZ = new[] { 0f, 0f, 100f };
            int bent = TrenchTraceMath.Resample(bentX, bentZ, 3, 10f, false, outX, outZ);
            TestAssert.That(bent >= 18, "The bend keeps its full length");
            float worstTurn = 0f;
            for (int i = 2; i < bent; i++)
            {
                float ax = outX[i - 1] - outX[i - 2], az = outZ[i - 1] - outZ[i - 2];
                float bx = outX[i] - outX[i - 1], bz = outZ[i] - outZ[i - 1];
                float la = Math.Max(0.001f, (float)Math.Sqrt(ax * ax + az * az));
                float lb = Math.Max(0.001f, (float)Math.Sqrt(bx * bx + bz * bz));
                float dot = (ax * bx + az * bz) / (la * lb);
                dot = Math.Max(-1f, Math.Min(1f, dot));
                worstTurn = Math.Max(worstTurn, (float)Math.Acos(dot) * 57.29578f);
            }
            TestAssert.That(worstTurn < 30f, "A Bezier chain eases through the contour corner");
            for (int i = 1; i < bent; i++)
                TestAssert.That(Math.Abs(outX[i] - outX[i - 1]) + Math.Abs(outZ[i] - outZ[i - 1]) < 20f,
                    "The curve stays continuous across the join");

            // A closed loop resamples with wraparound tangents and returns to its start.
            var loopX = new[] { 0f, 100f, 100f, 0f };
            var loopZ = new[] { 0f, 0f, 100f, 100f };
            int loop = TrenchTraceMath.Resample(loopX, loopZ, 4, 10f, true, outX, outZ);
            TestAssert.That(loop >= 30, "A closed pocket keeps its full ring");
            TestAssert.That(Near(outX[loop - 1], 0f) && Near(outZ[loop - 1], 0f),
                "A closed ring returns to its first station");
        }

        /// <summary>
        /// A real front arrives as cell-sized strides over tens of kilometres. The old planner
        /// resampled the whole trace at <c>length / station budget</c> — about 150m per station
        /// on a fifty-kilometre front — so a "position" came out as eight straight slabs and the
        /// minimum run collapsed to a single station. A window is now picked on the raw arc
        /// lengths and resampled at the ditch's own station spacing.
        /// </summary>
        private static void TestWindowByArc()
        {
            var arc = new float[8];
            for (int i = 0; i < arc.Length; i++) arc[i] = i * 1000f;

            TestAssert.That(TrenchTraceMath.WindowEnd(arc, arc.Length, 0, 1200f) == 1,
                "A window spans the raw contour points inside one position's length");
            TestAssert.That(TrenchTraceMath.WindowEnd(arc, arc.Length, 2, 1200f) == 3,
                "The next window starts where the scan left off");
            TestAssert.That(TrenchTraceMath.WindowEnd(arc, arc.Length, 7, 1200f) == 7,
                "The last trace point ends the scan");
            TestAssert.That(TrenchTraceMath.WindowEnd(new[] { 0f, 5000f }, 2, 0, 1200f) == 1,
                "One stride longer than a position is still one window");
            TestAssert.That(TrenchTraceMath.WindowEnd(arc, 4, 0, 1200f) == 1,
                "A closed ring's repeated first point is not a usable station");
            TestAssert.That(TrenchTraceMath.WindowEnd(arc, arc.Length, 0, float.NaN) == -1,
                "An unknown window length plans nothing");

            // The window resample: two raw contour points a kilometre apart still become a
            // curve at the ten-metre station spacing the ditch, anchors and meshes assume.
            var wx = new[] { 0f, 1000f, 4000f };
            var wz = new[] { 0f, 0f, 0f };
            var outX = new float[400];
            var outZ = new float[400];
            int stations = TrenchTraceMath.Resample(wx, wz, 0, 2, TrenchTraceMath.CurveSpacing, false,
                outX, outZ);
            TestAssert.That(stations >= 100, "A one-kilometre window keeps the station spacing");
            float worstStep = 0f;
            for (int i = 1; i < stations; i++)
                worstStep = Math.Max(worstStep, Math.Abs(outX[i] - outX[i - 1]));
            TestAssert.That(worstStep <= TrenchTraceMath.CurveSpacing + 0.001f,
                "Window resampling never strides past the curve spacing");
            TestAssert.That(Near(outX[stations - 1], 1000f), "The window ends on its last raw point");
        }

        private static void TestNormalsAndClosure()
        {
            var x = new[] { 0f, 0f, 0f };
            var z = new[] { 0f, 10f, 20f };
            TrenchTraceMath.Normal(x, z, 3, 1, out float nx, out float nz);
            TestAssert.That(Math.Abs(nx) > 0.99f && Math.Abs(nz) < 0.01f,
                "A north-running trace has an east-west normal");

            // Command repeats a pocket ring's first point last; that is the closure signal.
            var ringX = new[] { 0f, 100f, 100f, 0f, 0f };
            var ringZ = new[] { 0f, 0f, 100f, 100f, 0f };
            TestAssert.That(TrenchTraceMath.IsClosed(ringX, ringZ, 5), "A returning pocket reads as closed");
            TestAssert.That(!TrenchTraceMath.IsClosed(new[] { 0f, 1000f }, new[] { 0f, 0f }, 2),
                "An open trace is not closed");
        }

        private static void TestTraverseWave()
        {
            const float span = 5.5f;
            TestAssert.That(Near(TrenchTraceMath.TraverseWave(0f, span), 1f) &&
                Near(TrenchTraceMath.TraverseWave(span, span), -1f) &&
                Near(TrenchTraceMath.TraverseWave(span * 2f, span), 1f),
                "Traverse wave corners alternate every bay");
            TestAssert.That(Near(TrenchTraceMath.TraverseWave(span * 0.5f, span), 0f),
                "A traverse crosses the line between two bays");
            TestAssert.That(Near(TrenchTraceMath.TraverseWave(1.37f + span, span),
                -TrenchTraceMath.TraverseWave(1.37f, span)),
                "One bay ahead the wave is exactly on the other side of the line");
            TestAssert.That(Math.Abs(TrenchTraceMath.TraverseWave(span - 0.01f, span) -
                TrenchTraceMath.TraverseWave(span + 0.01f, span)) < 0.01f,
                "The wave is continuous across a traverse corner");
            TestAssert.That(Math.Abs(TrenchTraceMath.TraverseWave(0.2f, span) -
                TrenchTraceMath.TraverseWave(0f, span)) < 0.02f,
                "A bay runs straight through a rounded traverse extreme");
        }

        private static void TestDensifyAndWave()
        {
            var x = new[] { 0f, 22f };
            var z = new[] { 0f, 0f };
            var outX = new float[512];
            var outZ = new float[512];
            int count = TrenchTraceMath.DensifyTraversed(x, z, 2, TrenchTraceMath.MeshSpacing,
                TrenchTraceMath.TraverseSpacing, TrenchTraceMath.TraverseAmplitude, 0f, outX, outZ);
            TestAssert.That(count >= 7, "A 22m ditch is cut into mesh rings");
            float excursion = 0f;
            for (int i = 0; i < count; i++)
            {
                excursion = Math.Max(excursion, Math.Abs(outZ[i]));
                if (i == 0) continue;
                float step = Math.Abs(outX[i] - outX[i - 1]) + Math.Abs(outZ[i] - outZ[i - 1]);
                TestAssert.That(step < TrenchTraceMath.MeshSpacing * 3f, "Mesh rings stay closely spaced");
            }
            TestAssert.That(excursion > TrenchTraceMath.TraverseAmplitude * 0.5f &&
                excursion <= TrenchTraceMath.TraverseAmplitude + 0.001f,
                "The ditch weaves through traverses within a trench width");
        }

        private static void TestRoutePlanning()
        {
            const int stations = 3, candidates = 5;
            int[] Route(Func<int, int, float> ground)
            {
                var height = new float[stations, candidates];
                var cost = new float[stations, candidates];
                for (int s = 0; s < stations; s++)
                    for (int k = 0; k < candidates; k++)
                    {
                        height[s, k] = ground(s, k);
                        float offset = (k - 2) * 2f;
                        cost[s, k] = height[s, k] + 0.05f * offset * offset;
                    }
                var planned = new int[stations];
                TestAssert.That(TrenchTraceMath.PlanRoute(height, cost, stations, 6f, planned),
                    "A route is planned");
                return planned;
            }

            TestAssert.That(Route((s, k) => 0f)[1] == 2,
                "Level ground keeps the trench on its intended line");
            TestAssert.That(Route((s, k) => k == 1 ? -1.5f : 0f)[1] == 1,
                "A hollow off the line is where the trench settles");
            TestAssert.That(Route((s, k) => k == 2 ? 3f : 0f)[1] != 2,
                "A rise on the intended line is avoided instead of climbed");

            // An unbuildable station breaks the line instead of failing it whole; the
            // stretches on either side still get a level each.
            var blockedHeight = new float[stations, candidates];
            var blockedCost = new float[stations, candidates];
            for (int s = 0; s < stations; s++)
                for (int k = 0; k < candidates; k++)
                {
                    blockedHeight[s, k] = 0f;
                    blockedCost[s, k] = k == 0 ? float.NaN : 0f;
                }
            var blockedRoute = new int[stations];
            TestAssert.That(TrenchTraceMath.PlanRoute(blockedHeight, blockedCost, stations, 6f, blockedRoute),
                "A partially blocked corridor still routes");
            TestAssert.That(blockedRoute[0] != 0 && blockedRoute[1] != 0 && blockedRoute[2] != 0,
                "Unbuildable ground is never routed through");

            var allBlocked = new float[stations, candidates];
            for (int s = 0; s < stations; s++)
                for (int k = 0; k < candidates; k++)
                    allBlocked[s, k] = float.NaN;
            TestAssert.That(!TrenchTraceMath.PlanRoute(allBlocked, allBlocked, stations, 6f, new int[stations]),
                "A wholly blocked band fails closed");
        }

        /// <summary>
        /// Siting: the first planner minimised ground height, so every position settled into
        /// the lowest hollow it could reach. Relief over the land either side is what is
        /// scored now, and absolute height is deliberately never scored at all.
        /// </summary>
        private static void TestDefensibleSiting()
        {
            float level = TrenchTraceMath.DefensibleCost(50f, 50f, 50f, 0f);
            TestAssert.That(Near(level, TrenchTraceMath.DefensibleCost(5f, 5f, 5f, 0f)),
                "Siting never rewards absolute height: a valley floor is not cheaper than a plateau");
            TestAssert.That(TrenchTraceMath.DefensibleCost(13f, 10f, 10f, 0f) < level,
                "Ground standing above the land either side (a crest) costs less than level ground");
            TestAssert.That(TrenchTraceMath.DefensibleCost(7f, 10f, 10f, 0f) > level,
                "Ground below both sides (a hollow) costs more than level ground");
            TestAssert.That(Near(TrenchTraceMath.DefensibleCost(10f + TrenchTraceMath.ReliefCap, 10f, 10f, 0f),
                TrenchTraceMath.DefensibleCost(200f, 10f, 10f, 0f)),
                "The relief reward is capped: a peak is not a fortress");
            TestAssert.That(Near(TrenchTraceMath.DefensibleCost(20f, 10f, 10f, 4f),
                TrenchTraceMath.DefensibleCost(20f, 10f, 10f, 0f) + TrenchTraceMath.DepthStayWeight * 16f),
                "Depth error still pulls the line to its intended offset");
            TestAssert.That(Near(TrenchTraceMath.DefensibleCost(10f, float.NaN, 10f, 0f), 0f),
                "A probe that never landed leaves only the depth term");
            TestAssert.That(TrenchTraceMath.DefensibleCost(80f, 10f, 10f, 0f) <
                TrenchTraceMath.DefensibleCost(5f, 10f, 10f, 0f),
                "On a crest the higher candidate wins regardless of its absolute height");
        }

        /// <summary>
        /// Strategic siting nudges: a beachhead retries one band deeper landward, the ditch
        /// hugs the forest edge instead of digging inside the stand, and it sidesteps off
        /// the road surface. All three nudge the route; none refuses ground on its own.
        /// </summary>
        private static void TestSitingNudges()
        {
            float step = TrenchTraceMath.DepthStayWeight * 12f * 12f; // one 12m depth level
            TestAssert.That(TrenchTraceMath.FoliageCost(true, false) > step,
                "Ground inside the stand costs more than one depth step, so a boundary line hugs the edge");
            TestAssert.That(TrenchTraceMath.FoliageCost(false, true) < 0f &&
                TrenchTraceMath.FoliageCost(false, false) == 0f,
                "The forest edge earns a small bonus; open ground far from any stand scores nothing");
            TestAssert.That(TrenchTraceMath.FoliageCost(true, true) > 0f,
                "Inside the stand stays penalised even at its edge");

            TestAssert.That(TrenchTraceMath.RoadCost(0f) > step,
                "Ground on the road surface costs more than one depth step, so the ditch sidesteps");
            TestAssert.That(TrenchTraceMath.RoadCost(TrenchTraceMath.RoadClearDistance) == 0f &&
                TrenchTraceMath.RoadCost(500f) == 0f,
                "Ground past the verge scores nothing");
            TestAssert.That(TrenchTraceMath.RoadCost(float.NaN) == 0f,
                "Unknown road distance (no road index) scores nothing");

            float reach = (TrenchTraceMath.DepthSearchLevels - 1) * 0.5f * TrenchTraceMath.DepthSearchStep;
            TestAssert.That(TrenchTraceMath.FireDepth + reach <
                TrenchTraceMath.FireDepth + TrenchTraceMath.BeachFallbackExtraDepth - reach,
                "The beach fallback band sits clear of the normal fire band instead of overlapping it");
            TestAssert.That(TrenchTraceMath.FireDepth + TrenchTraceMath.BeachFallbackExtraDepth + reach <
                TrenchTraceMath.RedoubtDepth + reach,
                "A beach fallback still leaves room for the belt behind it");
        }

        /// <summary>
        /// The trace scan digs the sectors the fighting is on first: pressure order is
        /// descending, every index is visited exactly once, and equal pressures keep their
        /// original front order.
        /// </summary>
        private static void TestPressureOrder()
        {
            var pressure = new[] { 0.1f, 0.9f, 0.0f, 0.9f, 0.4f };
            var order = new int[pressure.Length];
            TestAssert.That(TrenchTraceMath.OrderByPressure(pressure, pressure.Length, order) == 5,
                "Every trace gets an order slot");
            TestAssert.That(order[0] == 1 && order[1] == 3 && order[2] == 4 && order[3] == 0 && order[4] == 2,
                "Traces scan hottest first, with equal pressure keeping its front order");
            var seen = new bool[pressure.Length];
            for (int i = 0; i < order.Length; i++)
            {
                TestAssert.That(order[i] >= 0 && order[i] < pressure.Length && !seen[order[i]],
                    "No trace is visited twice, none is dropped");
                seen[order[i]] = true;
            }
            TestAssert.That(TrenchTraceMath.OrderByPressure(pressure, 0, order) == 0 &&
                TrenchTraceMath.OrderByPressure(null, 5, order) == 0,
                "An empty or missing front orders nothing");
        }

        private static void TestRunSplitting()
        {
            var valid = new[] { false, true, true, true, false, true, false, true, true, true, true };
            var starts = new float[8];
            var lengths = new float[8];
            int runs = TrenchTraceMath.SplitRuns(valid, valid.Length, 2, starts, lengths, 8);
            TestAssert.That(runs == 2, "Short fragments between valid runs are dropped");
            TestAssert.That((int)starts[0] == 1 && (int)lengths[0] == 3, "The first run is kept whole");
            TestAssert.That((int)starts[1] == 7 && (int)lengths[1] == 4, "The far side of a gap is kept too");
            TestAssert.That(TrenchTraceMath.SplitRuns(valid, valid.Length, 4, starts, lengths, 8) == 1,
                "The minimum run length is enforced");
        }

        private static void TestStageRules()
        {
            TestAssert.That(TrenchTraceMath.DefenderBudget(TrenchStage.Scrape) == 2 &&
                TrenchTraceMath.DefenderBudget(TrenchStage.FireTrench) == 3 &&
                TrenchTraceMath.DefenderBudget(TrenchStage.Support) == 5 &&
                TrenchTraceMath.DefenderBudget(TrenchStage.Redoubt) == 7 &&
                TrenchTraceMath.DefenderBudget(TrenchStage.Saps) == 8,
                "Defenses grow 2/3/5/7/8 and stay sparse");
            TestAssert.That(TrenchTraceMath.DefenderBudget((TrenchStage)(-1)) == 0,
                "Before the first scrape, a position fields nothing");
            TestAssert.That(TrenchTraceMath.WorksBudget(TrenchStage.Scrape) == 0 &&
                TrenchTraceMath.WorksBudget(TrenchStage.FireTrench) > 0 &&
                TrenchTraceMath.WorksBudget(TrenchStage.Redoubt) == 8 &&
                TrenchTraceMath.WorksBudget(TrenchStage.Saps) == 10,
                "Works unlock with the fire trench, grow to eight, and add the two sap listening posts");
            TestAssert.That(!TrenchTraceMath.CanAdvance(false, 59, 60, 45),
                "Damage suppresses construction");
            TestAssert.That(TrenchTraceMath.CanAdvance(false, 60, 60, 45),
                "Survivors resume after a full quiet minute");
            TestAssert.That(!TrenchTraceMath.CanAdvance(true, 600, 60, 45),
                "An overrun position never rebuilds");
            TestAssert.That(TrenchTraceMath.HasSupport(TrenchStage.Redoubt) &&
                !TrenchTraceMath.HasSupport(TrenchStage.FireTrench) &&
                TrenchTraceMath.HasSaps(TrenchStage.Saps) && !TrenchTraceMath.HasSaps(TrenchStage.Redoubt),
                "Belt predicates follow the stage order");
            TestAssert.That(TrenchTraceMath.SupportDepth - TrenchTraceMath.FireDepth >= 60f &&
                TrenchTraceMath.RedoubtDepth - TrenchTraceMath.FireDepth >= 180f,
                "A position digs a real two-line belt: support and reserve lines behind the fire trench");
            TestAssert.That(TrenchTraceMath.TraverseAmplitude <= 0.7f &&
                TrenchTraceMath.TraverseSpacing >= 8f,
                "The traverse wave is a subtle zigzag; a sawtooth reads as blocky bays beside man-scale models");
        }

        /// <summary>
        /// The fire ditch is a chain of bays, not one uniform ribbon: a pitch that widens on a
        /// long line so the bay count stays inside the budget, a slot fraction centred in its
        /// slot so the end bays are not half-cut, and a widening that eases out of the plain
        /// ditch instead of stepping. The schedule is sampled per curve in TrenchLine.Validate
        /// (Unity-checked); these are the pure numbers behind it.
        /// </summary>
        private static void TestBaySchedule()
        {
            TestAssert.That(Near(TrenchTraceMath.NodeSpacingFor(600f), TrenchTraceMath.NodeSpacing) &&
                Near(TrenchTraceMath.NodeSpacingFor(0f), TrenchTraceMath.NodeSpacing) &&
                Near(TrenchTraceMath.NodeSpacingFor(-500f), TrenchTraceMath.NodeSpacing),
                "A short or unknown line digs a bay every twenty metres");
            float longPitch = TrenchTraceMath.NodeSpacingFor(2400f);
            TestAssert.That(Near(longPitch, 2400f / (TrenchTraceMath.MaximumNodes - 1)) &&
                longPitch > 77f && longPitch < 78f,
                "A long line widens the pitch to about 77.4m, got " + longPitch);
            TestAssert.That(longPitch * (TrenchTraceMath.MaximumNodes - 1) >= 2400f,
                "The widest pitch still fits one position's 2400m inside the node budget");

            TestAssert.That(Near(TrenchTraceMath.NodeFraction(0, 3), 1f / 6f) &&
                Near(TrenchTraceMath.NodeFraction(1, 3), 0.5f) &&
                Near(TrenchTraceMath.NodeFraction(2, 3), 5f / 6f),
                "Three bays sit at 0.17/0.5/0.83 of the line, centred in their slots");
            TestAssert.That(Near(TrenchTraceMath.NodeFraction(0, 0), 0f) &&
                Near(TrenchTraceMath.NodeFraction(4, 0), 0f),
                "A spent slot budget puts the bay on the line's start");
            TestAssert.That(Near(TrenchTraceMath.NodeFraction(-2, 3), 0f) &&
                Near(TrenchTraceMath.NodeFraction(9, 3), 1f),
                "A slot outside the budget is clamped onto the line, never past its caps");

            TestAssert.That(Near(TrenchTraceMath.BayExtra(0f), TrenchTraceMath.BayExtraWidth) &&
                Near(TrenchTraceMath.BayExtra(2.5f), TrenchTraceMath.BayExtraWidth),
                "A bay keeps its full width through its core");
            TestAssert.That(TrenchTraceMath.BayExtra(6.5f) == 0f &&
                TrenchTraceMath.BayExtra(20f) == 0f,
                "A bay has faded back into the plain ditch by the fade radius");
            float middle = TrenchTraceMath.BayExtra(4.5f);
            TestAssert.That(middle > 0f && middle < TrenchTraceMath.BayExtraWidth,
                "A bay eases out between its flat core and the fade radius, got " + middle);
            float previous = float.MaxValue;
            for (float distance = 0f; distance <= 8f; distance += 0.05f)
            {
                float extra = TrenchTraceMath.BayExtra(distance);
                TestAssert.That(extra <= previous + 1e-5f,
                    "Bay widening never rises with distance, broke at " + distance + "m");
                previous = extra;
            }

            // The bay schedule that TrenchLine.Validate samples from a curve: the node count a
            // straight 400m/2400m curve produces through the real sampler is asserted in the
            // Unity harness, where TrenchLine and TrenchPlanner are compiled.
            TestAssert.That(400f / TrenchTraceMath.NodeSpacingFor(400f) >= 19f,
                "A 400m fire curve carries about twenty bays at the base pitch");
            TestAssert.That(2400f / longPitch <= TrenchTraceMath.MaximumNodes - 1,
                "A 2400m fire curve needs at most the node budget at the widened pitch");
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;
    }
}
