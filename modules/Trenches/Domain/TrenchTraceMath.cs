using System;

namespace BoscaliSummer.Modules.Trenches.Domain
{
    /// <summary>How far a field position has been built out.</summary>
    internal enum TrenchStage
    {
        Scrape = 0,      // Shallow hasty cut along the trace
        FireTrench = 1,  // Full fire trench with parapet, traverses and MG teams
        Support = 2,     // Support trace behind the fire line, dugouts, more works
        Redoubt = 3,     // Rear redoubt trace, weapon pits, air watch
        Saps = 4         // Forward listening posts pushed into no man's land
    }

    /// <summary>Why a planning attempt produced no position, for one bounded log line.</summary>
    internal enum TrenchRefusal
    {
        None = 0,
        TooShort,    // The trace is shorter than one position
        NoStations,  // Resampling produced too few curve stations
        NoSide,      // The control field never resolved which side owns the trace
        NoGround,    // Terrain refused every candidate depth
        NoRun,       // Buildable stretches are shorter than the minimum run
        TooClose     // A position already holds this stretch of front
    }

    /// <summary>
    /// Pure engine-free rules for turning a Command front trace into a natural trench line:
    /// a smooth Bezier chain through the sparse contour, the ground-seeking offset on the
    /// owned side, the traversed ditch polyline, and the stages a position matures through.
    /// Unit-testable without the game running.
    /// </summary>
    /// <summary>One trace of a staged chunk rebuild, ordered near-visible first by PlanBuildSteps.</summary>
    internal enum TrenchBuildStep
    {
        Fire = 0,
        Colliders,
        Wire,
        WireOuter,
        Support,
        Redoubt,
        Links,
        Spurs,
        FireLod1,
        SupportLod1,
        RedoubtLod1,
        FireLod2,
        SupportLod2,
        RedoubtLod2,
        Cover
    }

    internal static class TrenchTraceMath
    {
        // The contour arrives one point per control-grid cell, so the trace is resampled
        // into a curve before anything else touches it.
        public const float CurveSpacing = 10f;
        public const float MeshSpacing = 3.5f;
        // The traverse wave is a subtle traversed zigzag: a man-scale trench bends gently
        // every nine metres or so, never a flight-visibility sawtooth that reads as blocky
        // square bays beside the 1.8m-tall vanilla emplacements.
        public const float TraverseSpacing = 9f;
        public const float TraverseAmplitude = 0.5f;
        public const float MinRunLength = 140f;
        public const int MaximumStations = 320;

        // A fire trench is a chain of bays, not one uniform ribbon: weapon nests stand in the
        // ditch at a regular pitch, and the ditch flares around each bay so a nest fits on the
        // crest. The count is capped, so the extra width
        // stays a bounded widening of one position's earthwork.
        public const float NodeSpacing = 20f;      // fire-bay pitch along the fire trench
        public const int MaximumNodes = 32;
        public const float BayExtraWidth = 1.4f;   // how much wider a bay is than the ditch
        public const float BayFlatRadius = 2.5f;   // full width this close to a node
        public const float BayFadeRadius = 6.5f;   // back to the plain ditch by here

        /// <summary>
        /// Bay pitch along a line: the fixed spacing, widened so a long line still fits within
        /// <see cref="MaximumNodes"/> bays instead of packing them past the budget.
        /// </summary>
        public static float NodeSpacingFor(float lineLength)
            => Math.Max(NodeSpacing, Math.Max(0f, lineLength) / (MaximumNodes - 1));

        /// <summary>
        /// Fraction of the line a bay slot sits at, centred in the slot so the end bays are
        /// not half-cut by the line's caps. A spent budget yields the line's start.
        /// </summary>
        public static float NodeFraction(int slot, int budget)
            => budget <= 0 ? 0f : Math.Clamp((slot + 0.5f) / budget, 0f, 1f);

        /// <summary>
        /// Extra half-width a bay adds at a distance from its node: full width through the
        /// bay's core, easing monotonically to none by the fade radius, so a bay swells out
        /// of the plain ditch and neighbouring bays still meet through the traverses.
        /// </summary>
        public static float BayExtra(float distanceFromNode)
        {
            if (distanceFromNode <= BayFlatRadius) return BayExtraWidth;
            if (distanceFromNode >= BayFadeRadius) return 0f;
            float t = (distanceFromNode - BayFlatRadius) / (BayFadeRadius - BayFlatRadius);
            return BayExtraWidth * (1f - t * t * (3f - 2f * t));
        }

        // Belt: the fire trench sits behind the line on the owned side; the support and
        // redoubt traces sit at deliberate field depths behind it, so the position reads as
        // the two-line defence real doctrine digs instead of a single ribbon.
        public const float FireDepth = 80f;
        public const float SupportDepth = 150f;
        public const float RedoubtDepth = 300f;
        // A belt trace that the ground refuses at doctrine depth is tried shallower before
        // the stage gives up on it: a rear line closer in is better than no rear line. A
        // rear trace may also be shorter than the fire line, and after a bounded number of
        // refusals the stage advances without it so the belt never holds the defender
        // budget hostage (live positions sat at three defenders forever for this).
        private static readonly float[] SupportLadder = { SupportDepth, 120f, 100f };
        private static readonly float[] RedoubtLadder = { RedoubtDepth, 240f, 190f };
        public const int BeltLadderLength = 3;
        public const float BeltMinRunLength = 70f;
        public const int BeltRefusalLimit = 3;
        public const float DepthSearchStep = 12f;
        public const int DepthSearchLevels = 5;
        public const float DepthStayWeight = 0.045f;
        public const float UndulationWeight = 6f;
        public const float SapDepth = 45f;
        public const float SapLateralFraction = 0.3f;
        public const int MaximumLinkTraces = 3;
        public const int MaximumSpurTraces = 2;
        public const float SapGapRadius = 7f;

        // Siting: a real position is dug for observation and drainage, not for shelter. The
        // fire line belongs on a crest or knoll standing above the ground either side of the
        // trace — grazing fire over no man's land, a dry floor, hollows in front skylined.
        // The first planner minimised ground height and so settled every position into the
        // lowest hollow it could reach; relief is what is scored now.
        public const float ReliefProbeDistance = 40f;  // ground sampled either side of the trace
        public const float ReliefWeight = 0.06f;       // reward per metre above the local low ground
        public const float ReliefCap = 12f;            // high ground is good; a peak is not a fortress
        public const float HollowPenalty = 0.5f;       // penalty per metre below the ground either side

        // Beach fallback: a trace along the shore refuses every candidate at the fire depth
        // (wet ground), so the planner retries one band deeper landward instead of dropping
        // the beachhead. The wire belts still sit between the ditch and the water.
        public const float BeachFallbackExtraDepth = 60f;

        // Foliage: a position digs at the forest edge, not inside the stand (no fields of
        // fire) and not far out in the open (no concealment). The inside penalty outweighs
        // one depth step, so a boundary line hugs the edge; the edge bonus is a tiebreaker.
        // Both nudge the route; neither refuses ground on its own.
        public const float FoliageInsidePenalty = 8f;
        public const float FoliageEdgeBonus = 0.25f;
        public const float FoliageEdgeProbeDistance = 40f;

        // Roads: the ditch stays off the road surface (one depth step of penalty), while
        // anti-tank coverage watches the nearest road inside the watch distance.
        public const float RoadClearDistance = 15f;
        public const float RoadDitchPenalty = 8f;
        public const float RoadWatchDistance = 300f;

        // Validated ground: dry, level enough to dig, and a safe trench-side probe distance.
        public const float MinimumGroundHeight = 2f;
        public const float MinimumNormalY = 0.97f;
        public const float ConstructionSuppressionSeconds = 60f;

        // A front trace runs through contested ground, where cell ownership answers neither
        // side, so the owning side is read from the sign of the signed control field instead.
        // The ladder deepens until the field separates, covering coarsened grid cells.
        public const float HoldSeparation = 1e-4f;
        private static readonly float[] SideProbeDistances = { 40f, 250f, 1000f, 3000f, 8000f };

        /// <summary>
        /// Floor of the contested band a dug line survives in. A position digs a hundred-ish
        /// metres behind the trace, well inside the front cell, and the field is one value per
        /// kilometre cell: on a ragged front that cell can read hostile-leaning (a salient, a
        /// cell the enemy has just pushed into) while the ground is still the faction's own
        /// side of the line. Only ground deep inside the enemy's cells is given up.
        /// </summary>
        public const float HoldOwnSideFloor = -0.75f;

        /// <summary>
        /// Floor for digging new ground: firmer than the floor for staying, so a line dug at
        /// the edge of its band is not abandoned by the next sample. One shared floor dug
        /// positions at -0.5 and abandoned them at -0.5, which killed every position on a hot
        /// front within a tick of its commit.
        /// </summary>
        public const float DigHoldFloor = -0.25f;

        // A position on a moving front is not given up on a single sample: the field must
        // read deep enemy at its centre for a sustained spell, and never inside the dig-in
        // grace after commit while the garrison is still standing up.
        public const float DigInGraceSeconds = 120f;
        public const float AbandonSeconds = 30f;

        /// <summary>True when a signed control value is ground a dug line still holds.</summary>
        public static bool HoldsOwnSide(float hold) => !float.IsNaN(hold) && hold >= HoldOwnSideFloor;

        /// <summary>True when a signed control value is ground a new line may dig into.</summary>
        public static bool CanDig(float hold) => !float.IsNaN(hold) && hold >= DigHoldFloor;

        /// <summary>
        /// Whether the control field abandons a position: <paramref name="hostileSince"/> is
        /// the time its centre first read below <see cref="HoldOwnSideFloor"/> (negative
        /// while it reads own side), and the verdict needs both the dig-in grace spent and
        /// the hostile read sustained.
        /// </summary>
        public static bool FieldAbandons(float now, float dugAt, float hostileSince)
            => hostileSince >= 0f && now >= dugAt + DigInGraceSeconds &&
                now - hostileSince >= AbandonSeconds;

        /// <summary>
        /// Side of the front the faction holds at a station: samples the signed control field
        /// (positive = the faction's ground) a short distance either way and reads the stronger
        /// sign, deepening the probe until the field separates so the previous cell's plateau
        /// cannot decide. <paramref name="holdAt"/> returns NaN where no control value exists;
        /// a field that never separates fails closed rather than guessing.
        /// </summary>
        public static bool TryResolveInward(float x, float z, float nx, float nz,
            Func<float, float, float> holdAt, out float ix, out float iz)
        {
            ix = nx;
            iz = nz;
            if (holdAt == null) return false;
            for (int i = 0; i < SideProbeDistances.Length; i++)
            {
                float distance = SideProbeDistances[i];
                float positive = holdAt(x + nx * distance, z + nz * distance);
                float negative = holdAt(x - nx * distance, z - nz * distance);
                if (float.IsNaN(positive) || float.IsNaN(negative)) continue;
                if (Math.Abs(positive - negative) < HoldSeparation) continue;
                if (negative > positive)
                {
                    ix = -nx;
                    iz = -nz;
                }
                return true;
            }
            return false;
        }

        public static bool IsBuildableGround(float heightAboveSea, float normalY)
            => !float.IsNaN(heightAboveSea) && !float.IsInfinity(heightAboveSea) &&
                heightAboveSea > MinimumGroundHeight && normalY >= MinimumNormalY && normalY <= 1f;

        /// <summary>
        /// Siting cost of one candidate depth. Ground standing above the land either side of
        /// the trace (a crest or knoll) is rewarded up to a cap; ground below both sides (the
        /// hollows the first planner preferred for their low ground alone) is penalised. The
        /// absolute ground height is deliberately absent, so a valley floor never wins for
        /// being low. Depth error keeps the line near its intended offset; probes that never
        /// landed leave only that term.
        /// </summary>
        public static float DefensibleCost(float groundHeight, float forwardHeight, float backHeight,
            float depthError)
        {
            float cost = DepthStayWeight * depthError * depthError;
            if (float.IsNaN(forwardHeight) || float.IsNaN(backHeight)) return cost;
            float relief = groundHeight - Math.Min(forwardHeight, backHeight);
            if (relief >= 0f) return cost - ReliefWeight * Math.Min(relief, ReliefCap);
            return cost + HollowPenalty * -relief;
        }

        /// <summary>
        /// Foliage nudge for one candidate: ground inside the stand costs a depth step (no
        /// fields of fire), ground just outside it earns a small bonus (concealment). Open
        /// ground far from any stand scores nothing.
        /// </summary>
        public static float FoliageCost(bool inside, bool nearEdge)
        {
            if (inside) return FoliageInsidePenalty;
            return nearEdge ? -FoliageEdgeBonus : 0f;
        }

        /// <summary>
        /// Road nudge for one candidate: ground on the road surface costs a depth step, so
        /// the ditch sidesteps onto the verge where the terrain allows. Unknown distance
        /// (no road index) scores nothing.
        /// </summary>
        public static float RoadCost(float distance)
        {
            if (float.IsNaN(distance)) return 0f;
            return distance < RoadClearDistance ? RoadDitchPenalty : 0f;
        }

        /// <summary>
        /// True when a trace's ends meet: Command repeats a pocket ring's first point last,
        /// so a beachhead ring is resampled with wraparound tangents instead of a seam kink.
        /// </summary>
        public static bool IsClosed(float[] x, float[] z, int count)
        {
            if (x == null || z == null || count < 3) return false;
            float dx = x[count - 1] - x[0], dz = z[count - 1] - z[0];
            return dx * dx + dz * dz < 1f;
        }

        /// <summary>
        /// Traverse wave of a trench: -1..1 with a rounded turn every
        /// <paramref name="segmentLength"/> metres. Each half period is a cubic Bezier ease,
        /// so the bay runs straight through every turn and the traverses curl instead of
        /// cutting a corner, which is what the ditch reads as from the air.
        /// </summary>
        public static float TraverseWave(float distance, float segmentLength)
        {
            float t = distance / segmentLength;
            float corner = (float)Math.Floor(t);
            float u = t - corner;
            float ease = u * u * (3f - 2f * u);
            float value = 1f - 2f * ease;
            return ((int)corner & 1) == 0 ? value : -value;
        }

        /// <summary>
        /// Resamples a sparse trace into a smooth cubic Bezier (Catmull-Rom form) chain at
        /// roughly <paramref name="spacing"/> metres. Returns the station count written.
        /// </summary>
        public static int Resample(float[] x, float[] z, int count, float spacing, bool closed,
            float[] outX, float[] outZ)
            => Resample(x, z, 0, count, spacing, closed, outX, outZ);

        /// <summary>
        /// Index of the last trace point a position-length window may span, walking the
        /// cumulative arc lengths (<c>arc[i]</c> = distance from the first point to point
        /// <c>i</c>). The window always spans at least one segment while the trace continues,
        /// so a single cell-sized stride is still a position. <paramref name="count"/> is the
        /// number of usable points — a closed ring's repeated first point is not one.
        /// </summary>
        public static int WindowEnd(float[] arc, int count, int start, float maximumLength)
        {
            if (arc == null || count <= 0 || !(maximumLength > 0f)) return -1;
            start = Math.Clamp(start, 0, count - 1);
            float end = arc[start] + maximumLength;
            int last = start;
            while (last + 1 < count && arc[last + 1] <= end) last++;
            if (last == start && last + 1 < count) last++;
            return last;
        }

        /// <summary>
        /// Resamples the stretch of a trace that starts at <paramref name="start"/> and is
        /// <paramref name="count"/> points long, using a cubic Hermite spline with chord-length
        /// Catmull-Rom tangents and dense arc-length parameterization (ported from Arma 3 SplinePlacer).
        /// Produces constant station spacing along the curve regardless of curvature.
        /// </summary>
        public static int Resample(float[] x, float[] z, int start, int count, float spacing, bool closed,
            float[] outX, float[] outZ)
            => ResampleSpline(x, z, start, count, spacing, closed, outX, outZ, null, null, null, null);

        /// <summary>
        /// Full spline sampling: evaluates cubic Hermite spline along the trace with arc-length
        /// parameterization, outputting positions, tangents, and station normals without distortion.
        /// </summary>
        public static int ResampleSpline(float[] x, float[] z, int start, int count, float spacing, bool closed,
            float[] outX, float[] outZ, float[] outTanX = null, float[] outTanZ = null, float[] outNormX = null, float[] outNormZ = null)
        {
            if (x == null || z == null || outX == null || outZ == null || count < 2 ||
                outX.Length < 2 || outZ.Length < 2) return 0;
            if (start < 0 || start + count > x.Length || start + count > z.Length) return 0;
            spacing = Math.Max(1f, spacing);

            int m = closed ? count + 3 : count + 2;
            Span<float> px = stackalloc float[m];
            Span<float> pz = stackalloc float[m];

            if (closed)
            {
                px[0] = x[start + count - 1];
                pz[0] = z[start + count - 1];
                for (int i = 0; i < count; i++)
                {
                    px[i + 1] = x[start + i];
                    pz[i + 1] = z[start + i];
                }
                px[count + 1] = x[start];
                pz[count + 1] = z[start];
                px[count + 2] = x[start + 1];
                pz[count + 2] = z[start + 1];
            }
            else
            {
                px[0] = 2f * x[start] - x[start + 1];
                pz[0] = 2f * z[start] - z[start + 1];
                for (int i = 0; i < count; i++)
                {
                    px[i + 1] = x[start + i];
                    pz[i + 1] = z[start + i];
                }
                px[count + 1] = 2f * x[start + count - 1] - x[start + count - 2];
                pz[count + 1] = 2f * z[start + count - 1] - z[start + count - 2];
            }

            int chordCount = m - 1;
            Span<float> chords = stackalloc float[chordCount];
            for (int i = 0; i < chordCount; i++)
            {
                float cdx = px[i + 1] - px[i];
                float cdz = pz[i + 1] - pz[i];
                chords[i] = Math.Max(1e-4f, (float)Math.Sqrt(cdx * cdx + cdz * cdz));
            }

            Span<float> tanX = stackalloc float[m];
            Span<float> tanZ = stackalloc float[m];
            for (int i = 1; i < m - 1; i++)
            {
                float dp = chords[i - 1];
                float dn = chords[i];
                float scale = dn / (dp + dn);
                tanX[i] = (px[i + 1] - px[i - 1]) * scale;
                tanZ[i] = (pz[i + 1] - pz[i - 1]) * scale;
            }

            const int Resolution = 16;
            int segments = closed ? count : count - 1;
            int tableCapacity = segments * Resolution + 1;

            float[] heapCumDist = null, heapX = null, heapZ = null, heapTX = null, heapTZ = null;
            Span<float> arcCumDist = tableCapacity <= 1024 ? stackalloc float[tableCapacity] : (heapCumDist = new float[tableCapacity]);
            Span<float> arcX = tableCapacity <= 1024 ? stackalloc float[tableCapacity] : (heapX = new float[tableCapacity]);
            Span<float> arcZ = tableCapacity <= 1024 ? stackalloc float[tableCapacity] : (heapZ = new float[tableCapacity]);
            Span<float> arcTX = tableCapacity <= 1024 ? stackalloc float[tableCapacity] : (heapTX = new float[tableCapacity]);
            Span<float> arcTZ = tableCapacity <= 1024 ? stackalloc float[tableCapacity] : (heapTZ = new float[tableCapacity]);

            int tableCount = 0;
            float cumDist = 0f;
            float prevX = px[1], prevZ = pz[1];

            for (int seg = 1; seg <= segments; seg++)
            {
                float paX = px[seg], paZ = pz[seg];
                float pbX = px[seg + 1], pbZ = pz[seg + 1];
                float taX = tanX[seg], taZ = tanZ[seg];
                float tbX = tanX[seg + 1], tbZ = tanZ[seg + 1];

                int startK = (seg == 1) ? 0 : 1;
                for (int k = startK; k <= Resolution; k++)
                {
                    float t = (float)k / Resolution;
                    float t2 = t * t;
                    float t3 = t2 * t;

                    float h00 = 2f * t3 - 3f * t2 + 1f;
                    float h10 = t3 - 2f * t2 + t;
                    float h01 = -2f * t3 + 3f * t2;
                    float h11 = t3 - t2;

                    float curX = h00 * paX + h10 * taX + h01 * pbX + h11 * tbX;
                    float curZ = h00 * paZ + h10 * taZ + h01 * pbZ + h11 * tbZ;

                    if (k > 0 || seg > 1)
                    {
                        float ddx = curX - prevX;
                        float ddz = curZ - prevZ;
                        cumDist += (float)Math.Sqrt(ddx * ddx + ddz * ddz);
                    }

                    float dh00 = 6f * t2 - 6f * t;
                    float dh10 = 3f * t2 - 4f * t + 1f;
                    float dh01 = -6f * t2 + 6f * t;
                    float dh11 = 3f * t2 - 2f * t;

                    float curTX = dh00 * paX + dh10 * taX + dh01 * pbX + dh11 * tbX;
                    float curTZ = dh00 * paZ + dh10 * taZ + dh01 * pbZ + dh11 * tbZ;
                    float tlen = (float)Math.Sqrt(curTX * curTX + curTZ * curTZ);
                    if (tlen > 1e-4f) { curTX /= tlen; curTZ /= tlen; }
                    else { curTX = 0f; curTZ = 1f; }

                    if (tableCount < tableCapacity)
                    {
                        arcCumDist[tableCount] = cumDist;
                        arcX[tableCount] = curX;
                        arcZ[tableCount] = curZ;
                        arcTX[tableCount] = curTX;
                        arcTZ[tableCount] = curTZ;
                        tableCount++;
                    }

                    prevX = curX;
                    prevZ = curZ;
                }
            }

            float totalLength = cumDist;
            if (totalLength < 1e-4f) return 0;

            int written = 0;
            float targetDist = 0f;
            int tableIdx = 0;
            int maxOut = Math.Min(outX.Length, outZ.Length);

            while (targetDist <= totalLength && written < maxOut)
            {
                while (tableIdx < tableCount - 1 && arcCumDist[tableIdx + 1] <= targetDist)
                {
                    tableIdx++;
                }

                float sampleX, sampleZ, sampleTX, sampleTZ;

                if (tableIdx >= tableCount - 1)
                {
                    sampleX = arcX[tableCount - 1];
                    sampleZ = arcZ[tableCount - 1];
                    sampleTX = arcTX[tableCount - 1];
                    sampleTZ = arcTZ[tableCount - 1];
                    targetDist = totalLength + spacing;
                }
                else
                {
                    float dLo = arcCumDist[tableIdx];
                    float dHi = arcCumDist[tableIdx + 1];
                    float delta = dHi - dLo;
                    float frac = delta > 1e-5f ? Math.Clamp((targetDist - dLo) / delta, 0f, 1f) : 0f;

                    sampleX = arcX[tableIdx] + frac * (arcX[tableIdx + 1] - arcX[tableIdx]);
                    sampleZ = arcZ[tableIdx] + frac * (arcZ[tableIdx + 1] - arcZ[tableIdx]);
                    sampleTX = arcTX[tableIdx] + frac * (arcTX[tableIdx + 1] - arcTX[tableIdx]);
                    sampleTZ = arcTZ[tableIdx] + frac * (arcTZ[tableIdx + 1] - arcTZ[tableIdx]);

                    float tlen = (float)Math.Sqrt(sampleTX * sampleTX + sampleTZ * sampleTZ);
                    if (tlen > 1e-4f) { sampleTX /= tlen; sampleTZ /= tlen; }

                    targetDist += spacing;
                }

                outX[written] = sampleX;
                outZ[written] = sampleZ;
                if (outTanX != null && written < outTanX.Length) outTanX[written] = sampleTX;
                if (outTanZ != null && written < outTanZ.Length) outTanZ[written] = sampleTZ;
                if (outNormX != null && written < outNormX.Length) outNormX[written] = -sampleTZ;
                if (outNormZ != null && written < outNormZ.Length) outNormZ[written] = sampleTX;

                written++;
            }

            if (!closed && written > 0 && written < maxOut)
            {
                float lastX = x[start + count - 1];
                float lastZ = z[start + count - 1];
                float dx = lastX - outX[written - 1];
                float dz = lastZ - outZ[written - 1];
                float endGap = (float)Math.Sqrt(dx * dx + dz * dz);

                if (endGap > 0.4f * spacing)
                {
                    outX[written] = lastX;
                    outZ[written] = lastZ;
                    if (outTanX != null && written < outTanX.Length) outTanX[written] = arcTX[tableCount - 1];
                    if (outTanZ != null && written < outTanZ.Length) outTanZ[written] = arcTZ[tableCount - 1];
                    if (outNormX != null && written < outNormX.Length) outNormX[written] = -arcTZ[tableCount - 1];
                    if (outNormZ != null && written < outNormZ.Length) outNormZ[written] = arcTX[tableCount - 1];
                    written++;
                }
                else
                {
                    outX[written - 1] = lastX;
                    outZ[written - 1] = lastZ;
                }
            }
            else if (closed && written < maxOut)
            {
                outX[written] = x[start];
                outZ[written] = z[start];
                if (outTanX != null && written < outTanX.Length) outTanX[written] = arcTX[0];
                if (outTanZ != null && written < outTanZ.Length) outTanZ[written] = arcTZ[0];
                if (outNormX != null && written < outNormX.Length) outNormX[written] = -arcTZ[0];
                if (outNormZ != null && written < outNormZ.Length) outNormZ[written] = arcTX[0];
                written++;
            }

            return written;
        }

        private static int Neighbour(int index, int count, bool closed)
        {
            if (closed) return ((index % count) + count) % count;
            return Math.Clamp(index, 0, count - 1);
        }

        /// <summary>Station normal (left-hand perpendicular) from the neighbouring stations.</summary>
        public static void Normal(float[] x, float[] z, int count, int index, out float nx, out float nz)
        {
            int before = Math.Max(0, index - 1);
            int after = Math.Min(count - 1, index + 1);
            float dx = x[after] - x[before], dz = z[after] - z[before];
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 1e-4f) { nx = 0f; nz = 1f; return; }
            nx = -dz / length;
            nz = dx / length;
        }

        /// <summary>
        /// Routes a line through candidate positions. Each candidate carries the ground
        /// height and a position cost (low ground plus distance from the intended offset);
        /// the route minimizes that cost plus the height change between neighbouring
        /// candidates, so the line settles into the flattest, lowest corridor it can reach
        /// and stays straight where the ground is level. NaN marks ground that cannot be
        /// dug, and an unbuildable stretch breaks the line instead of failing it whole.
        /// <paramref name="route"/> receives one candidate index per station, or -1.
        /// </summary>
        private static float[,] routeCumulative;
        private static int[,] routeBack;

        /// <summary>Wire leaves a gap where a sap pushes through the belt.</summary>
        public static bool WithinSapGap(float x, float z, float headX, float headZ)
        {
            float dx = x - headX, dz = z - headZ;
            return dx * dx + dz * dz < SapGapRadius * SapGapRadius;
        }

        public const int MaximumBuildSteps = 15;

        // Overhead cover against observation and plunging fire from above: timber-and-earth
        // roof panels over the fire ditch between the open firing bays, the single most cited
        // field adaptation of the drone age (KJMS 2026; BALTOPS 2025 Seabee/Marine trench
        // network). Panels appear once the position matures past its open scrape-and-rifle
        // stage, never over a bay a nest stands in.
        public const float CoverSpacing = 12f;
        public const float CoverLength = 5f;
        public const float CoverSpanExtra = 2f;
        public const float CoverNodeClearance = 7f;
        public const int MaximumCoverSpans = 256;

        /// <summary>True once a position is mature enough to roof its fire ditch between bays.</summary>
        public static bool HasOverheadCover(TrenchStage stage) => stage >= TrenchStage.Support;

        /// <summary>
        /// True when a roof panel at (x, z) would sit over a bay node: panels keep clear of
        /// the open firing bays so nests keep their crest and their field of fire.
        /// </summary>
        public static bool WithinCoverClearance(float x, float z, float nodeX, float nodeZ)
        {
            float dx = x - nodeX, dz = z - nodeZ;
            return dx * dx + dz * dz < CoverNodeClearance * CoverNodeClearance;
        }

        /// <summary>
        /// Build order for one chunk rebuild, near-visible first: the fire ditch, its
        /// colliders, wire and overhead cover, the belt at full detail, then the coarser
        /// LOD passes. One step per trace keeps every frame of a staged rebuild under
        /// budget. Writes at most <paramref name="steps"/> entries and returns the number
        /// written.
        /// </summary>
        public static int PlanBuildSteps(bool support, bool redoubt, int links, int spurs,
            TrenchBuildStep[] steps)
        {
            if (steps == null) return 0;
            int count = 0;
            PlanBuildStep(TrenchBuildStep.Fire, steps, ref count);
            PlanBuildStep(TrenchBuildStep.Colliders, steps, ref count);
            PlanBuildStep(TrenchBuildStep.Wire, steps, ref count);
            PlanBuildStep(TrenchBuildStep.WireOuter, steps, ref count);
            PlanBuildStep(TrenchBuildStep.Cover, steps, ref count);
            if (support) PlanBuildStep(TrenchBuildStep.Support, steps, ref count);
            if (redoubt) PlanBuildStep(TrenchBuildStep.Redoubt, steps, ref count);
            if (links > 0) PlanBuildStep(TrenchBuildStep.Links, steps, ref count);
            if (spurs > 0) PlanBuildStep(TrenchBuildStep.Spurs, steps, ref count);
            PlanBuildStep(TrenchBuildStep.FireLod1, steps, ref count);
            if (support) PlanBuildStep(TrenchBuildStep.SupportLod1, steps, ref count);
            if (redoubt) PlanBuildStep(TrenchBuildStep.RedoubtLod1, steps, ref count);
            PlanBuildStep(TrenchBuildStep.FireLod2, steps, ref count);
            if (support) PlanBuildStep(TrenchBuildStep.SupportLod2, steps, ref count);
            if (redoubt) PlanBuildStep(TrenchBuildStep.RedoubtLod2, steps, ref count);
            return count;
        }

        private static void PlanBuildStep(TrenchBuildStep step, TrenchBuildStep[] steps, ref int count)
        {
            if (count < steps.Length) steps[count++] = step;
        }

        public static bool PlanRoute(float[,] groundHeight, float[,] positionCost, int stations,
            float undulationWeight, int[] route)
        {
            if (groundHeight == null || positionCost == null || route == null) return false;
            int candidates = groundHeight.GetLength(1);
            if (stations <= 0 || candidates == 0 || route.Length < stations) return false;

            float[,] cumulative = routeCumulative;
            int[,] back = routeBack;
            if (cumulative == null || cumulative.GetLength(0) < stations || cumulative.GetLength(1) < candidates)
            {
                cumulative = routeCumulative = new float[stations, candidates];
                back = routeBack = new int[stations, candidates];
            }

            for (int k = 0; k < candidates; k++)
            {
                cumulative[0, k] = RouteValid(groundHeight, positionCost, 0, k)
                    ? positionCost[0, k] : float.PositiveInfinity;
                back[0, k] = -1;
            }

            for (int s = 1; s < stations; s++)
            {
                for (int k = 0; k < candidates; k++)
                {
                    cumulative[s, k] = float.PositiveInfinity;
                    back[s, k] = -1;
                    if (!RouteValid(groundHeight, positionCost, s, k)) continue;
                    for (int j = 0; j < candidates; j++)
                    {
                        float prefix = cumulative[s - 1, j];
                        if (float.IsPositiveInfinity(prefix)) continue;
                        float cost = prefix + positionCost[s, k] +
                            undulationWeight * Math.Abs(groundHeight[s, k] - groundHeight[s - 1, j]);
                        if (cost >= cumulative[s, k]) continue;
                        cumulative[s, k] = cost;
                        back[s, k] = j;
                    }
                }
            }

            for (int s = 0; s < stations; s++) route[s] = -1;
            int routed = 0;
            for (int s = stations - 1; s >= 0; s--)
            {
                if (route[s] >= 0) continue;
                int bestK = -1;
                float bestCost = float.PositiveInfinity;
                for (int k = 0; k < candidates; k++)
                    if (cumulative[s, k] < bestCost) { bestCost = cumulative[s, k]; bestK = k; }
                if (bestK < 0) continue;
                routed++;
                for (int ss = s, kk = bestK; ss >= 0 && kk >= 0 && route[ss] < 0; ss--)
                {
                    route[ss] = kk;
                    kk = back[ss, kk];
                }
            }
            return routed > 0;
        }

        private static bool RouteValid(float[,] groundHeight, float[,] positionCost, int station, int candidate)
            => !float.IsNaN(groundHeight[station, candidate]) && !float.IsNaN(positionCost[station, candidate]);

        /// <summary>
        /// Indices of the packed trace buffers ordered by pressure, hottest first: the scan
        /// digs the sectors the fighting is on before it walks the quiet stretches. Insertion
        /// sort over the bounded trace budget, no allocation; equal pressures keep their
        /// original order. Returns the number of indices written.
        /// </summary>
        public static int OrderByPressure(float[] pressure, int count, int[] order)
        {
            if (pressure == null || order == null || count <= 0) return 0;
            count = Math.Min(count, Math.Min(pressure.Length, order.Length));
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = 1; i < count; i++)
            {
                int key = order[i];
                float keyPressure = pressure[key];
                int j = i - 1;
                while (j >= 0 && pressure[order[j]] < keyPressure)
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = key;
            }
            return count;
        }

        /// <summary>
        /// Densifies the curve into ditch-mesh rings and lays the traverse wave along it.
        /// The wave is phased off the line's world position, so neighbouring lines continue
        /// one pattern instead of restarting it. Returns the ring count written.
        /// </summary>
        public static int DensifyTraversed(float[] x, float[] z, int count, float spacing,
            float traverseSpacing, float amplitude, float phase, float[] outX, float[] outZ)
        {
            if (x == null || z == null || outX == null || outZ == null || count < 2 ||
                outX.Length < 2 || outZ.Length < 2) return 0;
            spacing = Math.Max(1f, spacing);

            int written = 0;
            outX[written] = x[0];
            outZ[written] = z[0];
            written++;

            float arc = 0f;
            for (int i = 1; i < count; i++)
            {
                float dx = x[i] - x[i - 1], dz = z[i] - z[i - 1];
                float length = (float)Math.Sqrt(dx * dx + dz * dz);
                if (length < 0.0001f) continue;
                int steps = Math.Max(1, (int)Math.Ceiling(length / spacing));
                for (int s = 1; s <= steps && written < outX.Length; s++)
                {
                    float k = (float)s / steps;
                    float offset = amplitude * TraverseWave(phase + arc + length * k, traverseSpacing);
                    outX[written] = x[i - 1] + dx * k - dz / length * offset;
                    outZ[written] = z[i - 1] + dz * k + dx / length * offset;
                    written++;
                }
                arc += length;
            }
            return written;
        }

        /// <summary>
        /// Splits a validity mask into runs no shorter than <paramref name="minStations"/>.
        /// Returns the run count written; a front interrupted by water or a cliff simply
        /// continues on the far side instead of being dropped whole.
        /// </summary>
        public static int SplitRuns(bool[] valid, int count, int minStations, float[] runStarts,
            float[] runLengths, int maximumRuns)
        {
            if (valid == null || runStarts == null || runLengths == null || count <= 0) return 0;
            int runs = 0;
            int start = -1;
            for (int i = 0; i <= count; i++)
            {
                bool ok = i < count && valid[i];
                if (ok)
                {
                    if (start < 0) start = i;
                    continue;
                }
                if (start >= 0)
                {
                    int length = i - start;
                    if (length >= minStations && runs < maximumRuns &&
                        runs < runStarts.Length && runs < runLengths.Length)
                    {
                        runStarts[runs] = start;
                        runLengths[runs] = length;
                        runs++;
                    }
                    start = -1;
                }
            }
            return runs;
        }

        public static bool CanAdvance(bool overrun, float now, float suppressedUntil, float nextGrowth)
            => !overrun && now >= suppressedUntil && now >= nextGrowth;

        /// <summary>
        /// Depth behind the trace of the belt trace <paramref name="stage"/> adds next, on
        /// rung <paramref name="attempt"/> of the ladder; NaN past the ladder or for a stage
        /// that adds no belt trace.
        /// </summary>
        public static float BeltDepth(TrenchStage stage, int attempt)
        {
            float[] ladder = stage == TrenchStage.FireTrench ? SupportLadder
                : stage == TrenchStage.Support ? RedoubtLadder : null;
            if (ladder == null || attempt < 0 || attempt >= ladder.Length) return float.NaN;
            return ladder[attempt];
        }

        /// <summary>True once a stage has refused its belt trace often enough to advance without it.</summary>
        public static bool AdvancesWithoutBelt(int refusals) => refusals >= BeltRefusalLimit;

        // One budget for the whole position: the two opening MG teams of a Scrape grow into a
        // full chain of bay strongpoints, not a second free garrison on top of the works.
        public static int DefenderBudget(TrenchStage stage)
            => stage >= TrenchStage.Saps ? 8
                : stage >= TrenchStage.Redoubt ? 7
                : stage >= TrenchStage.Support ? 5
                : stage >= TrenchStage.FireTrench ? 3
                : stage >= TrenchStage.Scrape ? 2
                : 0;

        // Spacing is measured on position centres. Same-side positions hold 360m apart
        // so parallel lines never stack; opposing lines may close to 120m, because a
        // mirror pair dug 80m behind the same trace sits ~160m apart — inside the
        // historical 90-275m no-man's-land band — and must not refuse each other. The
        // old 250m cross-faction floor did exactly that: only one side of any front
        // ever got a ditch.
        public const float SameOwnerSpacing = 360f;
        public const float OtherOwnerSpacing = 120f;

        /// <summary>
        /// Next faction slot in the build rotation, wrapping around: every attempt digs
        /// on another side, so no single front can fill the theater's quota alone.
        /// </summary>
        public static int RotateFaction(int current, int count)
            => count <= 1 ? 0 : (current + 1) % count;

        public static float SpacingFor(bool sameOwner)
            => sameOwner ? SameOwnerSpacing : OtherOwnerSpacing;

        public static bool CentresConflict(float distance, bool sameOwner)
            => !(distance >= 0f) || distance < SpacingFor(sameOwner);

        /// <summary>
        /// Growth pacing for one position: a hot sector digs with priority (half the
        /// base interval at full pressure) while a quiet one still matures, slowly.
        /// Never faster than one stage per 15s.
        /// </summary>
        public static float GrowthInterval(float baseSeconds, float pressure)
        {
            float p = float.IsNaN(pressure) ? 0f : Math.Clamp(pressure, 0f, 1f);
            return Math.Max(15f, Math.Max(0f, baseSeconds) * (1.3f - 0.8f * p));
        }

        /// <summary>
        /// Heaviest garrison a sector's pressure pays for: a quiet line holds a light
        /// MG screen (the first four slots), a warm one adds its road and air watch,
        /// and only a hot one stands the full eight with both 23mm guns.
        /// </summary>
        public static int MaxDefendersForPressure(float pressure)
        {
            float p = float.IsNaN(pressure) ? 0f : Math.Clamp(pressure, 0f, 1f);
            return p < 0.3f ? 4 : p < 0.6f ? 6 : 8;
        }

        // Harassing bombardment between opposing lines. Mortars work close pairs with
        // single shells, guns work distant pairs with three-round salvos, and every
        // round aims at no-man's-land with a radial miss: the wire gets churned, the
        // ditch only catches the odd short round, and the payoff is suppression — the
        // historical creeping barrage worked the same way.
        public const float BarragePairRange = 2500f;
        public const float MortarPairDistance = 800f;
        public const float MortarScatter = 60f;
        public const float ArtilleryScatter = 150f;
        public const int MortarRounds = 1;
        public const int ArtilleryRounds = 3;
        public const int BarrageInflightCeiling = 4;

        public static bool InBarrageRange(float distance)
            => distance > 1f && distance <= BarragePairRange;

        public static int BarrageRounds(float pairDistance)
            => pairDistance < MortarPairDistance ? MortarRounds : ArtilleryRounds;

        public static float BarrageScatter(float pairDistance)
            => pairDistance < MortarPairDistance ? MortarScatter : ArtilleryScatter;

        /// <summary>
        /// Aim point of one shell: the no-man's-land midpoint of the two centres plus
        /// a square miss of <paramref name="scatter"/> metres. <paramref name="ux"/> and
        /// <paramref name="uz"/> are unit offsets (-1..1, clamped); zero scatter lands
        /// exactly on the midpoint.
        /// </summary>
        public static void BarrageAim(float ownX, float ownZ, float enemyX, float enemyZ,
            float ux, float uz, float scatter, out float x, out float z)
        {
            float mx = (ownX + enemyX) * 0.5f;
            float mz = (ownZ + enemyZ) * 0.5f;
            if (!(scatter > 0f))
            {
                x = mx;
                z = mz;
                return;
            }
            x = mx + Math.Clamp(ux, -1f, 1f) * scatter;
            z = mz + Math.Clamp(uz, -1f, 1f) * scatter;
        }

        /// <summary>
        /// Seconds until a position's next fire mission: a uniform draw from the
        /// configured window, quickened by pressure so hot sectors shoot often and
        /// quiet ones only occasionally. Corrupt inputs fail closed to the 5s floor
        /// instead of a NaN that would fire every tick.
        /// </summary>
        public static float BarrageDelay(float minSeconds, float maxSeconds, float pressure, float unit)
        {
            float lo = Math.Max(5f, Math.Min(minSeconds, maxSeconds));
            float hi = Math.Max(lo, Math.Max(minSeconds, maxSeconds));
            if (!(lo >= 5f)) lo = 5f;
            if (!(hi >= lo)) hi = lo;
            float p = float.IsNaN(pressure) ? 0f : Math.Clamp(pressure, 0f, 1f);
            float u = float.IsNaN(unit) ? 0.5f : Math.Clamp(unit, 0f, 1f);
            return Math.Max(5f, (lo + (hi - lo) * u) * (1.3f - 0.8f * p));
        }

        // Runways are never dug: ground inside the clear radius of any airbase centre
        // refuses the ditch, so a position sidesteps the field instead of crossing it.
        public const float AirfieldClearDistance = 500f;
        public const int MaximumAirfields = 64;

        public static bool NearAirfield(float x, float z, float[] airfieldX, float[] airfieldZ, int count)
        {
            if (airfieldX == null || airfieldZ == null || count <= 0) return false;
            float radiusSq = AirfieldClearDistance * AirfieldClearDistance;
            int n = Math.Min(count, Math.Min(airfieldX.Length, airfieldZ.Length));
            for (int i = 0; i < n; i++)
            {
                float dx = airfieldX[i] - x, dz = airfieldZ[i] - z;
                if (dx * dx + dz * dz < radiusSq) return true;
            }
            return false;
        }

        public static int WorksBudget(TrenchStage stage)
            => stage >= TrenchStage.Saps ? 10
                : stage >= TrenchStage.Redoubt ? 8
                : stage >= TrenchStage.Support ? 6
                : stage >= TrenchStage.FireTrench ? 3
                : 0;
    }
}
