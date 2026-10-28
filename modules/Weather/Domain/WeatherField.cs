using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{

    /// <summary>The weather at one point and instant. Units: mm/h, metres, km, m/s, hPa, °C.</summary>
    internal struct WeatherPoint
    {
        public float RainRate;
        public float LowCover;
        /// <summary>The opacity-weighted cover of all three levels, 1 - (1 - low)(1 - mid x opacity)(1 - high x opacity): what vanilla's conditions and IWeatherView.Cover follow (spec 8.2).</summary>
        public float SkyCover;
        /// <summary>The class measure of this point (the same formula as the nominal one, with the sampled cover and rain). Random for lattice skies: readouts only, never a test or a pool.</summary>
        public float ClassSampled;
        /// <summary>The local row's Convective (0-1); the native conditions take their convective term from it.</summary>
        public float Convective;
        /// <summary>Base and top of the deck the native cloud height follows (<see cref="SkySummary.NativeDeck"/>); its clamp is 250-9500 m.</summary>
        public float NativeDeck, NativeDeckTop;
        /// <summary>Base and top of the lowest rain-bearing layer (cells, nimbostratus, or the altostratus of embedded showers): what the vertical rain profile reads.</summary>
        public float RainBase, RainTop;
        /// <summary>max(low cover, mid cover x opacity) of the local row: what Gate() reads to let stratiform rain fall (spec 5.3).</summary>
        public float RainCover;
        /// <summary>Top of the low deck from the local row (base + max(puff depth, deck depth) + half the wobble): the envelope's conservative ceiling.</summary>
        public float DeckTop;
        /// <summary>The local thin layers: cover and altitude range from the tape row at this point (spec 8.2).</summary>
        public float MidCover, MidBase, MidTop, HighCover, HighBase, HighTop;
        public float BackgroundCover;
        public float ClusterCover;
        public float CellCover;
        public float CellShape;
        public float CloudBase;
        /// <summary>How far the base-height field may move the base at this point (the row's BaseVariation, 150-400 m): the map widens its altitude bounds by it.</summary>
        public float BaseVariation;
        /// <summary>Deck/tower ceiling.</summary>
        public float LowTop;
        public float CloudTop;
        public float VisibilityKm;

        /// <summary>Horizontal wind, downwind components (x east, z north).</summary>
        public float WindX;
        public float WindZ;

        /// <summary>Vertical air motion: positive updraft, negative downdraft.</summary>
        public float WindUp;
        public float Gust;
        public float Turbulence;

        /// <summary>Strikes per minute from cells within a few radii.</summary>
        public float LightningRate;

        /// <summary>0..1 how deep the point is in the strongest cell core.</summary>
        public float CoreDepth;

        /// <summary>Share of the rain that is convective (cells), 0..1.</summary>
        public float ConvectiveShare;

        public bool Hail;
        public float Qnh;
        public float Temperature;
        public float Dewpoint;

        public float WindSpeed => (float)Math.Sqrt(WindX * WindX + WindZ * WindZ);
    }

    /// <summary>
    /// The weather at one instant: the timeline's state and a static cloud layout (up to two
    /// front bands, twelve storm-cell sites and eight cumulus groups), resolved once by
    /// <see cref="Build"/> and sampled anywhere by <see cref="Sample"/>. Nothing in the layout
    /// moves; a state change only grows or shrinks what is there. A field owns its buffers and is
    /// reused, so building one allocates nothing.
    ///
    /// <para>Everything is a pure function of (key, time, map extent, hour of day): the host,
    /// every client, the forecast and the tests all see the same sky.</para>
    /// </summary>
    internal sealed class WeatherField
    {
        public const float MaxRainRate = 150f;

        /// <summary>Scale of the static patch field that breaks up the sheet and area rain.</summary>
        public const float PatchScale = 14000f;

        /// <summary>Mean surface wind the state's wind factor scales, m/s. Display and local
        /// effects only; native wind keeps the mission's own speed.</summary>
        public const float BaseWindSpeed = 7f;

        private readonly CellCache<CumulusGroup> cumulusCache = new CellCache<CumulusGroup>();
        private readonly CellCache<StormCell> stormCache = new CellCache<StormCell>();
        /// <summary>The base-height field of the current layout seed (it remembers the last lattice cell it read).</summary>
        private BaseHeightField baseField;
        private readonly StormLattice.Neighbourhood stormNear = new StormLattice.Neighbourhood();
        private readonly CumulusLattice.Neighbourhood cumulusNear = new CumulusLattice.Neighbourhood();
        private readonly Superstructure[] superstructures = new Superstructure[Superstructures.MaxCount];
        /// <summary>The row every sample reads when the tape is one keyframe (live play, and a one-keyframe fixture): not a texel copy per sample.</summary>
        private SkyParams constantRow;
        private bool tapeIsConstant;
        /// <summary>The noise rotation and offsets of the current layout seed, built once per seed (spec 6).</summary>
        private NoiseXform noiseXform;
        private MarkerSet tapeMarkers = MarkerSet.Empty;


        public WeatherKey Key { get; private set; }
        public float Time { get; private set; }
        public float HalfX { get; private set; }
        public float HalfZ { get; private set; }
        public float HourOfDay { get; private set; }
        /// <summary>The tape this field was built from (null after UseFixtureTape): markers, fronts and segments for the console and the tests.</summary>
        public WeatherTape TapeSource { get; private set; }
        /// <summary>True when D follows the mission clock (<c>Build(..., moving: true)</c>); the live manager holds D at the key's Offset until slice 5.</summary>
        public bool Moving { get; private set; }
        /// <summary>Where every per-sample genus and thin-layer row is read from: one keyframe (<see cref="RowAt"/>) in slices 2-3, a fixture's baked tape in the density check and the bench.</summary>
        public TapeSampler Tape { get; private set; }
        public TapeFrame Frame => frame;
        /// <summary>Everything the lattices resolve from: the tape identity, the tape and its frame (spec 6). Rebuilt with every Build.</summary>
        public LatticeSource Lattice => latticeSource;
        // The two behind the properties above, so a sample reads them in place instead of copying a property's value.
        private TapeFrame frame;
        private LatticeSource latticeSource;
        private bool haveLattice;
        /// <summary>The diurnal swing of the temperature at this Build's hour of day: the same for every sample, so the sine is taken once.</summary>
        private float diurnal;
        /// <summary>The unit vector of <see cref="PrevailingHeading"/>: what a sample away from any front's veer starts its wind from.</summary>
        private float prevailingX, prevailingZ;
        /// <summary>Scenery storms outside the theater; see <see cref="Superstructures"/>.</summary>
        public int SuperstructureCount { get; private set; }

        /// <summary>Multiplier on haze visibility (1 = clear air; smoke from fires lowers it).</summary>
        public float HazeScale { get; private set; } = 1f;

        /// <summary>Downwind heading of the layout's prevailing wind; fixed for the key.</summary>
        public float PrevailingHeading { get; private set; }
        public bool IsBuilt => Key != null;

        /// <summary>The tape row at a world point: the local sky, with the low genus, the base, the thin layers and every CPU column.</summary>
        public SkyParams RowAt(float x, float z) => tapeIsConstant ? constantRow : Tape.At(frame.S(x, z));

        public Superstructure SuperstructureAt(int index) => superstructures[index];

        public void Build(WeatherKey key, float time, float halfX, float halfZ, float hourOfDay = 12f, float hazeScale = 1f, bool moving = false)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Time = time;
            HalfX = Math.Max(halfX, 1000f);
            HalfZ = Math.Max(halfZ, 1000f);
            HourOfDay = hourOfDay;
            HazeScale = Scalar.Clamp(hazeScale, 0.05f, 1f);

            // The tape is a pure function of the tape identity and shared (spec 5); D is the key's offset, or follows the mission clock when asked (the tests of
            // rigidity, and slice 5). Slice 4a holds D at the offset the opening gave: windows, cookie and temporal path slide in slice 5.
            WeatherTape tape = WeatherTape.For(key);
            TapeSource = tape;
            Moving = moving;
            Tape = new TapeSampler(tape.Baked);
            tapeIsConstant = false;
            if (noiseXform == null || noiseXform.LayoutSeed != key.LayoutSeed) noiseXform = new NoiseXform(key.LayoutSeed);
            float d = moving ? (float)key.OffsetAt(time) : key.Offset;
            frame = tape.Frame(key.HeadingDeg, d, noiseXform);
            diurnal = 5f * (float)Math.Sin((HourOfDay - 9f) / 24f * 2f * Math.PI);
            tapeMarkers = tape.Markers;
            ResetLattice();
            PrevailingHeading = key.DownwindHeading; // the mission wind's heading, set once at capture
            WeatherMath.HeadingToVector(PrevailingHeading + 0f, out prevailingX, out prevailingZ);

            SuperstructureCount = Superstructures.Fill(superstructures, key.LayoutSeed, RowAt(0f, 0f),
                HalfX, HalfZ, PrevailingHeading, key.Sets, key.HasAnchor, key.AnchorX, key.AnchorZ);
        }

        /// <summary>Fixtures only (the density check, the bench, tests): read the genus and thin-layer rows from a hand-built tape. The next <see cref="Build"/> restores the one-keyframe tape.</summary>
        public void UseFixtureTape(TapeSampler tape, TapeFrame frame)
        {
            Tape = tape;
            this.frame = frame;
            tapeIsConstant = tape.IsConstant;
            tapeMarkers = MarkerSet.Empty;
            TapeSource = null;
            if (tapeIsConstant) constantRow = tape.At(0f);
            ResetLattice();
        }

        private void ResetLattice()
        {
            var next = new LatticeSource(Key.TapeId, Key.LayoutSeed, Tape, frame, tapeMarkers);
            // The cached cells are a pure function of the source (see ResolvesLike): a rebuild for a later time or a new offset of the same tape resolves the same cells,
            // so it keeps them (and the neighbourhoods copied from them). Anything else starts clean.
            if (!haveLattice || !next.ResolvesLike(latticeSource))
            {
                cumulusCache.Reset();
                cumulusNear.Reset();
                stormCache.Reset();
                stormNear.Reset();
            }
            latticeSource = next;
            haveLattice = true;
            if (baseField == null || baseField.LayoutSeed != Key.LayoutSeed) baseField = new BaseHeightField(Key.LayoutSeed);
        }

        /// <summary>How many storm cells at or above <see cref="StormLattice.ActiveStrength"/> have their centre within a range of a world point (the activeCellCount readout).</summary>
        public int ActiveCellCount(float x, float z, float range)
        {
            Frame.ToTape(x, z, out float qx, out float qz);
            return StormLattice.CountActive(Lattice, stormCache, qx, qz, range);
        }

        /// <summary>How many cumulus groups at or above <see cref="CumulusLattice.ActiveStrength"/> have their centre within a range of a world point (the activeGroupCount readout).</summary>
        public int ActiveGroupCount(float x, float z, float range)
        {
            Frame.ToTape(x, z, out float qx, out float qz);
            return CumulusLattice.CountActive(Lattice, cumulusCache, qx, qz, range);
        }

        public WeatherPoint Sample(float x, float z)
        {
            // The per-sample row: the low genus, the base, the thin layers and every CPU column are read from the tape at this point's position.
            float s = frame.S(x, z);
            // The row is read in place (the constant row itself, or the one fetched here), not copied.
            SkyParams fetched = default;
            if (!tapeIsConstant) fetched = Tape.At(s);
            ref readonly SkyParams local = ref (tapeIsConstant ? ref constantRow : ref fetched);
            var point = new WeatherPoint();
            // The lattices live in the tape frame (spec 3.1): q = p + u * D, with D 0 until the tape moves (slice 5).
            frame.ToTape(x, z, out float qx, out float qz);

            // Area (stratiform) rain and the sheet, broken up by the static patch field so a
            // thinning deck opens in patches instead of fading uniformly.
            float patch = PatchAt(qx, qz);
            float areaRain = local.StratRain * 2.2f * WeatherMath.Smoothstep(0.45f, 0.8f, patch);
            float sheet = local.SheetCover;
            float coverBase = Scalar.Clamp01(sheet + (patch - 0.5f) * (0.10f + 0.55f * sheet));
            point.BackgroundCover = coverBase;

            float windSpeed = BaseWindSpeed * local.WindFactor;
            float turbulence = local.Turbulence;

            // Front markers (spec 5.3): thin overlays on the keyframe blend. A marker adds rain, gust, veer and a QNH dip; it never adds cover. The distance
            // d is measured across the wavy line: (s - s_k) / sqrt(1 + M'(a)^2), negative before the line arrives.
            float coreRain = 0f, markerGust = 0f, veer = 0f, qnhDip = 0f;
            float slope = 0f;
            bool haveSlope = false;
            for (int i = 0; i < tapeMarkers.Count; i++)
            {
                TapeMarker marker = tapeMarkers.Items[i];
                if (Math.Abs(s - marker.S) > FrontLine.Reach * 1.5f) continue;
                // The slope belongs to the point, not the marker: taken once, and only when a marker is near.
                if (!haveSlope) { slope = frame.MeanderSlope(frame.Across(x, z)); haveSlope = true; }
                float d = tapeMarkers.Distance(i, s, slope);
                LineEffect effect = FrontLine.Line(marker.Kind, d);
                coreRain += effect.Rain * marker.Strength;
                markerGust += effect.Gust * marker.Strength;
                veer += effect.VeerDegrees * marker.Strength;
                qnhDip += effect.QnhDip * marker.Strength;
            }
            float windX, windZ;
            // Away from every front the veer is zero and the heading is the Build's own: its sine and cosine were taken there.
            if (veer == 0f) { windX = prevailingX; windZ = prevailingZ; }
            else WeatherMath.HeadingToVector(PrevailingHeading + veer, out windX, out windZ);
            windX *= windSpeed;
            windZ *= windSpeed;

            float cellRain = 0f;
            float clearCells = 1f;
            float outX = 0f, outZ = 0f, vertical = 0f;
            float lightning = 0f;
            float core = 0f;
            float towerDepth = 0f;
            bool hail = false;
            // Storm cells (spec 6): the cells that can touch this point's lattice cell hold every cell that can touch the point.
            stormNear.Refresh(latticeSource, stormCache, qx, qz);
            // The flared anvil radius is the same for every cell of this point's row (0 when the row has none).
            float anvilFlare = local.Anvil > 0.05f ? CloudShape.Footprint(0.82f, local.Anvil) : 0f;
            for (int i = 0; i < stormNear.Count; i++)
            {
                ref StormCell cell = ref stormNear.Items[i];
                float dx = qx - cell.X, dz = qz - cell.Z;
                if (dx * dx + dz * dz >= cell.Reach * cell.Reach) continue;

                // Every reading below is a function of the distance to the cell: measured once.
                float dist = cell.Distance(qx, qz);
                cellRain += cell.RainAtDistance(dist);
                float cover = cell.CoverAtDistance(dist);
                clearCells *= 1f - cover;
                float shape = cell.ShapeAtDistance(dist);
                if (shape > point.CellShape) point.CellShape = shape;
                cell.WindAtOffset(dx, dz, dist, out float ox, out float oz, out float w, out float t);
                outX += ox;
                outZ += oz;
                vertical += w;
                turbulence += t;
                // A cell that is not electric (rate exactly 0, the usual case) adds exactly +0 (the weight is finite): its exponential is not taken.
                float strikeRate = cell.LightningRate;
                if (strikeRate != 0f) lightning += strikeRate * cell.LightningWeightAtDistance(dist);

                float depth = cell.CoreAtDistance(dist);
                if (depth > core) core = depth;
                if (cell.Hail && depth > 0.5f) hail = true;
                // The updraft is a dome. A real anvil holds that ceiling out to the flared
                // radius; otherwise the column top falls inside the stem and the flare in
                // CloudBodies never gets a column to fill.
                float column = shape > 0f ? (float)Math.Pow(shape, 0.65f) : 0f;
                float anvilReach = cell.Radius * 1.35f * anvilFlare;
                if (anvilFlare > 0f && dx * dx + dz * dz < (anvilReach + cell.Radius * 0.55f) * (anvilReach + cell.Radius * 0.55f))
                {
                    float shelf = 1f - WeatherMath.Smoothstep(anvilReach, anvilReach + cell.Radius * 0.55f, dist);
                    shelf *= WeatherMath.Smoothstep(0.25f, 0.45f, local.Anvil);
                    // The spreading crown is lower away from the updraft. A literal flat
                    // shelf=1 across this radius made every ordinary storm a giant saucer.
                    shelf *= 0.82f + 0.18f * Scalar.Clamp01(shape);
                    if (shelf > column) column = shelf;
                }
                float cellDepth = (cell.Top - cell.Base) * column;
                if (cellDepth > towerDepth) towerDepth = cellDepth;
            }

            // Cumulus groups: independent covers combine as 1 - (1 - c1)(1 - c2)...; the strongest one shapes the crown.
            CumulusLattice.Sample(latticeSource, cumulusCache, cumulusNear, qx, qz, out float clearClusters, out float groupShape, out float groupCrown);
            if (groupShape > point.CellShape) point.CellShape = groupShape;

            // Rain = (keyframe rain + marker core) * Gate(RainCover) + cell rain, capped at 120 mm/h (spec 5.3). The gate keeps a core from raining under
            // open sky; a cell supplies its own cover, so cell rain is never gated.
            float rainCover = SkyMath.RainCover(local);
            float stratiform = (areaRain + coreRain) * SkyMath.Gate(rainCover);
            float rain = Math.Min(stratiform + cellRain, MaxRainRate);
            point.RainRate = rain;
            point.RainCover = rainCover;
            point.ConvectiveShare = rain > 0.01f ? Math.Min(1f, cellRain / (stratiform + cellRain)) : 0f;
            point.LowCover = Scalar.Clamp01(1f - (1f - coverBase) * clearCells * clearClusters);
            point.ClusterCover = Scalar.Clamp01(1f - clearClusters);
            point.CellCover = Scalar.Clamp01(1f - clearCells);

            // The local row's base with the base-height field added (spec 6), and the rain-bearing lowering taken off.
            float cloudBase = local.CloudBase + local.BaseVariation * baseField.At(qx, qz) -
                300f * WeatherMath.Smoothstep(0f, 15f, rain);
            point.CloudBase = Scalar.Clamp(cloudBase, 250f, 3600f);
            point.BaseVariation = local.BaseVariation;
            float stratiformCover = Scalar.Clamp01(1f - (1f - coverBase) * clearClusters);
            CloudGenus genus = CloudShape.Resolve(local);
            float deck = Scalar.Lerp(genus.PuffDepth, Math.Max(genus.PuffDepth, local.DeckDepth), genus.SheetBlend);
            deck = Math.Max(280f, deck);
            float crownDepth = Math.Max(towerDepth, groupCrown);
            float crown = crownDepth > 0f ? point.CloudBase + crownDepth : 0f;
            // Fair and stratocumulus stay at the genus depth. A cumulonimbus keeps the cell's full column.
            crown = Scalar.Lerp(Math.Min(crown, CloudShape.TowerCap(point.CloudBase, genus, local.DeckDepth)), crown, genus.TowerBlend);
            point.LowTop = Math.Max(point.CloudBase + deck * (0.5f + 0.5f * stratiformCover), crown);
            point.CloudTop = point.LowTop;

            point.DeckTop = point.CloudBase + Math.Max(genus.PuffDepth, local.DeckDepth) + genus.BaseWobble * 0.5f;
            point.MidCover = local.Mid.Cover; point.MidBase = local.Mid.Base; point.MidTop = local.Mid.Top;
            point.HighCover = local.High.Cover; point.HighBase = local.High.Base; point.HighTop = local.High.Top;
            // The sky summary (spec 8.2): opacity-weighted cover, the sampled class, the deck the natives follow and the layer the rain falls from.
            point.SkyCover = SkyMath.SkyCover(point.LowCover, point.MidCover, local.OpacityMid, point.HighCover, local.OpacityHigh);
            point.Convective = local.Convective;
            point.ClassSampled = SkyMath.ClassConditions(point.SkyCover, stratiform, local.Convective, local.Severity);
            SkySummary.NativeDeck(point.LowCover, point.CloudBase, point.LowTop, point.MidCover, local.OpacityMid, point.MidBase, point.MidTop,
                point.HighCover, local.OpacityHigh, point.HighBase, point.HighTop, out point.NativeDeck, out point.NativeDeckTop);
            SkySummary.RainLayer(cellRain, stratiform, point.LowCover, point.MidCover * local.OpacityMid, point.CloudBase, point.LowTop, point.MidBase, point.MidTop,
                out point.RainBase, out point.RainTop);

            float haze = Math.Max(local.Haze * HazeScale, 0.5f);
            // A dry point (rain exactly 0) skips the call: Math.Pow(0, 0.66) is exactly 0.
            float extinction = 3.912f / haze + 0.25f * (rain == 0f ? 0f : (float)Math.Pow(rain, 0.66));
            point.VisibilityKm = Scalar.Clamp(3.912f / extinction, 0.3f, 50f);

            point.WindX = windX + outX;
            point.WindZ = windZ + outZ;
            point.WindUp = vertical;
            float outflow = (float)Math.Sqrt(outX * outX + outZ * outZ);
            point.Turbulence = Scalar.Clamp01(turbulence);
            point.Gust = 0.35f * windSpeed * point.Turbulence + outflow + markerGust;
            point.LightningRate = lightning;
            point.CoreDepth = Scalar.Clamp01(core);
            point.Hail = hail;

            point.Qnh = local.Qnh - qnhDip + (patch - 0.5f) * 2f;

            point.Temperature = local.Temperature + diurnal - 4f * WeatherMath.Smoothstep(0f, 15f, rain);
            float humid = Scalar.Clamp01(point.LowCover * 0.6f + WeatherMath.Smoothstep(0f, 5f, rain) * 0.6f);
            point.Dewpoint = point.Temperature - Scalar.Lerp(9f, 0.5f, humid);
            return point;
        }

        /// <summary>The regional sky at the map centre.</summary>
        public WeatherPoint Regional() => Sample(0f, 0f);

        /// <summary>Mean of the opacity-weighted <see cref="WeatherPoint.SkyCover"/> over a coarse grid: the theater mean the server's native conditions follow (spec 8.1).</summary>
        public float MeanCover(int steps = 5)
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < steps; i++)
            {
                for (int j = 0; j < steps; j++)
                {
                    float x = ((i + 0.5f) / steps * 2f - 1f) * HalfX;
                    float z = ((j + 0.5f) / steps * 2f - 1f) * HalfZ;
                    sum += Sample(x, z).SkyCover;
                    n++;
                }
            }
            return n > 0 ? sum / n : 0f;
        }

        /// <summary>The patch field lives in the tape frame (spec 3.1), so it slides with the weather.</summary>
        private float PatchAt(float qx, float qz) => WeatherMath.Fbm(Key.Seed ^ 0x5eedu, qx / PatchScale, qz / PatchScale);
    }

}
