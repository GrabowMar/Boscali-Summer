using System;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// CPU mirror of the volume shader's cloud density (FlightCloud.shader: Bodies, CoverMask,
    /// CloudShape's profile, the low deck, fronts, towers, the middle layer and the set-piece
    /// envelopes). View samples include boundary warp, scud, erosion and cavity carving;
    /// shadow samples retain conservative bodies and one weather column per light ray.
    /// The thin frozen high layer and set-piece micro-erosion are deliberately omitted. Every sample reads its low
    /// genus (and the middle layer its row) from the tape at its own position: <see cref="TapeSampler"/> is the CPU twin of
    /// the shader's tape texture.
    /// The shadow cookie and the in-cloud test use it. Pure System.Math over
    /// the shared noise bytes, so it runs on a worker and in tests. Keep it in step with the shader.
    /// </summary>
    internal readonly struct CloudBodies
    {
        private readonly byte[] noise;
        private readonly int size;
        private readonly TapeSampler tape;
        private readonly TapeFrame frame;
        private readonly SkyParams constantSky;
        private readonly CloudGenus constantGenus;
        private readonly float windX, windZ;
        private readonly float fog;
        private readonly WeatherField field;
        private readonly float[] shownHeroes;
        private readonly bool nearDetail;
        private readonly Func<float, float, WeatherPoint> weatherSampler;
        private readonly float eyeX, eyeZ, eyeRadius, eyeStrength;

        /// <summary>One sky everywhere: a one-keyframe tape of <paramref name="sky"/> (tests, and callers with no tape).</summary>
        public CloudBodies(byte[] noise, int size, in SkyParams sky, float prevailingHeading = 0f,
            float fog = 0f, WeatherField field = null, float[] shownHeroes = null, bool nearDetail = false,
            Func<float, float, WeatherPoint> weatherSampler = null)
            : this(noise, size, TapeSampler.Constant(sky), new TapeFrame(0f, 0f), prevailingHeading, fog, field, shownHeroes,
                nearDetail, weatherSampler)
        {
        }

        public CloudBodies(byte[] noise, int size, TapeSampler tape, TapeFrame frame, float prevailingHeading = 0f,
            float fog = 0f, WeatherField field = null, float[] shownHeroes = null,
            bool nearDetail = false, Func<float, float, WeatherPoint> weatherSampler = null)
        {
            this.weatherSampler = weatherSampler;
            this.shownHeroes = shownHeroes;
            this.nearDetail = nearDetail;
            this.fog = fog;
            this.noise = noise;
            this.size = size;
            this.tape = tape;
            this.frame = frame;
            constantSky = tape.IsConstant ? tape.At(0f) : default;
            constantGenus = tape.IsConstant ? CloudShape.Resolve(constantSky) : default;
            this.field = field;
            eyeX = eyeZ = eyeRadius = eyeStrength = 0f;
            for (int i = 0; field != null && i < field.SuperstructureCount; i++)
            {
                Superstructure s = field.SuperstructureAt(i);
                if (s.Kind != SuperstructureKind.StormEye) continue;
                eyeX = s.X; eyeZ = s.Z; eyeRadius = s.Size;
                eyeStrength = shownHeroes != null && i < shownHeroes.Length ? shownHeroes[i] : s.Strength;
            }
            WeatherMath.HeadingToVector(prevailingHeading, out windX, out windZ);
        }

        /// <summary>Cloud density at a global position; <paramref name="p"/> is the weather at that column.</summary>
        public float Density(WeatherPoint p, float x, float y, float z)
            => Density(p, x, y, z, out _);

        public float Density(WeatherPoint p, float x, float y, float z, out float localLightingHeight)
        {
            localLightingHeight = 0f;
            // Every tap of the low volume reads the noise where this tape's rotation and offset put it (spec 6): nx, nz are noise-space.
            frame.NoiseXZ(x, z, NoiseXform.Low, out float nx, out float nz);
            float broadR = Sample(nx / 24000f, nz / 24000f, 0.37f, 0);
            float broadG = Sample(nx / 24000f, nz / 24000f, 0.37f, 1);
            // One tape position for cover and genus, taken at the warped point like the shader's.
            float warpedX = x + (broadR - 0.5f) * 2800f, warpedZ = z + (broadG - 0.5f) * 2800f;
            SkyParams sky = RowAt(warpedX, warpedZ);
            CloudGenus genus = tape.IsConstant ? constantGenus : CloudShape.Resolve(sky);
            // View occupation follows the GPU warp. The shadow worker deliberately retains
            // its one column sample per ray; resampling twelve cells at every tap is too costly.
            if (nearDetail && (field != null || weatherSampler != null))
                p = weatherSampler != null ? weatherSampler(warpedX, warpedZ) : field.Sample(warpedX, warpedZ);
            float heroes = Heroes(x, y, z, out float heroHeight);
            float eyeKeep = CloudShape.EyeCloudKeep(x, y, z, eyeX, eyeZ, eyeRadius, eyeStrength);
            float middle = MidLayer(x, y, z) * eyeKeep, fogDensity = FogBank(y);
            float mid = Math.Max(middle, fogDensity);
            float midHeight = middle >= fogDensity
                ? CloudShape.LightingHeight(y, sky.Mid.Base, sky.Mid.Top)
                : CloudShape.LightingHeight(y, -30f, 330f);
            float background = Math.Max(mid, heroes);
            localLightingHeight = background > 0f ? heroes >= mid ? heroHeight : midHeight : 0f;
            float layer = p.BackgroundCover, cell = p.CellShape;
            if (Math.Max(layer, cell) < 0.025f) return background;
            float smooth = Scalar.Clamp01(sky.DeckSmooth);
            float baseY = p.CloudBase;
            // CloudMaps stores a conservative top including the low deck's independent depth.
            float lowTop = p.LowTop > 0f ? p.LowTop : p.CloudTop;
            float topY = Math.Max(baseY + Math.Max(280f, Math.Max(genus.PuffDepth, sky.DeckDepth) + genus.BaseWobble * 0.5f), lowTop);
            float columnTop = WeatherMath.Lerp(Math.Min(topY, CloudShape.TowerCap(baseY, genus, sky.DeckDepth)), topY, genus.TowerBlend);
            columnTop = Math.Max(columnTop, baseY + 280f);
            float lowest = baseY;
            float highest = Math.Max(columnTop, baseY + Math.Max(genus.PuffDepth, sky.DeckDepth) + genus.BaseWobble * 0.5f);
            if (y < lowest - 700f || y > highest + 600f) return background;

            // The shader warps bodies by its broad noise so no grid shows at range.
            float wx = x + (broadR - 0.5f) * 1400f, wz = z + (broadG - 0.5f) * 1400f;
            // Cloud streets (spec 6, StreetMath): where the row asks, the body field is stretched along the wind and given rows across it.
            float streets = Scalar.Clamp01(sky.Streets);
            float sx = wx, sz = wz;
            if (streets > 0f)
            {
                float along = wx * frame.UX + wz * frame.UZ, compress = StreetMath.Compress(streets);
                sx -= frame.UX * along * compress;
                sz -= frame.UZ * along * compress;
            }
            frame.NoiseXZ(sx, sz, NoiseXform.Low, out float bnx, out float bnz);
            if (!genus.Sheet) WarpLobes(ref bnx, y, ref bnz, genus.LobeRung, streets);
            float bodies = genus.Sheet ? 0.72f : BodiesLadder(bnx, y, bnz, genus.LobeRung);
            if (!genus.Sheet && streets > 0f)
                bodies = StreetMath.Body(bodies, streets, StreetMath.Rows(-wx * frame.UZ + wz * frame.UX,
                    frame.Noise.StreetSpacing, frame.Noise.StreetPhase, (broadG - 0.5f) * 4f));
            float puffBody = genus.Sheet ? 0.72f : WeatherMath.Lerp(bodies, 0.72f, genus.SheetBlend);
            float relief = WeatherMath.Lerp(0.55f, 1f, WeatherMath.Smoothstep(0.2f, 0.85f, puffBody));
            float localAnvil = CloudShape.LocalAnvil(columnTop - baseY, genus.Anvil);
            float localTowerBlend = WeatherMath.Smoothstep(0.18f, 0.60f, localAnvil);
            columnTop = WeatherMath.Lerp(baseY + (columnTop - baseY) * relief, columnTop,
                Math.Max(genus.SheetBlend, localTowerBlend));

            // Low deck: puffs at the genus scale, or a smooth stratus/nimbostratus sheet.
            // Each puff carries its own base and its own domed top.
            float layerShape = 0f;
            float layerHeight = 0f;
            if (layer > 0.02f)
            {
                float deckBody = WeatherMath.Lerp(puffBody, 0.72f + 0.28f * puffBody, smooth);
                float mask = CoverMask(deckBody, Scalar.Clamp01(layer * 1.2f));
                float thick = Math.Max(280f, WeatherMath.Lerp(genus.PuffDepth, Math.Max(genus.PuffDepth, sky.DeckDepth), genus.SheetBlend));
                thick *= WeatherMath.Lerp(0.75f + 0.10f * genus.SheetBlend, 1f, broadR);
                thick *= WeatherMath.Lerp(relief, 1f, genus.SheetBlend);
                float layerBase = baseY + (puffBody - 0.5f) * WeatherMath.Lerp(genus.BaseWobble, genus.BaseWobble * 0.3f, smooth);
                float hl = (y - layerBase) / Math.Max(1f, thick);
                layerHeight = Scalar.Clamp01(hl);
                float deckAnvil = CloudShape.LocalAnvil(thick, genus.Anvil) * (1f - smooth);
                float prof = CloudShape.Profile(hl, CloudShape.LocalDome(WeatherMath.Lerp(genus.Dome, genus.Dome * 0.25f, smooth), deckAnvil), deckAnvil);
                float gate = CloudShape.BaseGate(y, layerBase, WeatherMath.Lerp(genus.BaseSharp, genus.BaseSharp * 2.2f, smooth));
                layerShape = CloudShape.Mass(mask, prof) * gate;
            }


            // Towers: the cell's footprint, narrowed with height, flared where the genus has an anvil.
            // Cumulus groups are carved into puffs; a cumulonimbus stays one mass.
            // The cell is a gaussian with no flat core, and it is already ~0.2 at the stem's
            // edge. Dividing the threshold by the footprint never clears that edge. Raising
            // the gaussian to 1/foot² is the radius scale Footprint describes.
            float h = Scalar.Clamp01((y - baseY) / Math.Max(1f, columnTop - baseY));
            float foot = CloudShape.Footprint(h, localAnvil);
            float wide = (float)Math.Pow(Scalar.Clamp01(cell), 1f / Math.Max(1f, foot * foot));
            float need = 0.18f + 0.42f * h * h;
            float inside = WeatherMath.Smoothstep(need - 0.12f, need + 0.12f, wide);
            float carved = WeatherMath.Lerp(CoverMask(puffBody, Scalar.Clamp01(0.45f + 0.35f * cell)),
                WeatherMath.Lerp(0.55f + 0.45f * puffBody, 1f, genus.SheetBlend), Math.Max(genus.SheetBlend, localTowerBlend));
            float tower = inside * CloudShape.Mass(carved, CloudShape.Profile(h, CloudShape.LocalDome(genus.Dome, localAnvil), localAnvil)) *
                CloudShape.BaseGate(y, baseY, genus.BaseSharp) *
                WeatherMath.Smoothstep(0.04f, 0.20f, wide);

            if (nearDetail)
            {
                float edge = Scalar.Clamp01(1f - Math.Abs(Math.Max(layerShape, tower) - 0.42f) * 2.4f);
                // The edge texture follows the lobe: the erosion periods scale with the rung's lobe size (spec 4.2), and a ramp
                // blends the two rungs' textures by the same weight as their bodies.
                Ladder.Split(genus.LobeRung, out float edgeRung, out float edgeW);
                float sheet = genus.SheetBlend;
                float detail = 0.5f;
                if (genus.Billow > 0.01f && edge > 0.08f)
                {
                    float d0 = 0f;
                    if (edgeW < 0.98f) d0 = EdgeDetail(nx, y, nz, Ladder.EdgeScale(Ladder.LobeMetres(edgeRung)), edge, sheet);
                    detail = d0;
                    if (edgeW > 0.02f)
                        detail = WeatherMath.Lerp(d0, EdgeDetail(nx, y, nz, Ladder.EdgeScale(Ladder.LobeMetres(edgeRung + 1f)), edge, sheet),
                            edgeW < 0.98f ? edgeW : 1f);
                }
                // The shader's nibble and its steeper rim on heaped cloud (not on the front shield), so the mirror and the march
                // agree on the boundary at a standing camera. The far map's smoothing (coarse >= 1) is not mirrored.
                float nibble = Math.Max(0f, WeatherMath.Lerp(0.72f, 0.65f, sheet) - detail) * genus.Billow * edge * WeatherMath.Lerp(0.70f, 0.45f, sheet);
                layerShape = Scalar.Clamp01(layerShape - nibble); tower = Scalar.Clamp01(tower - nibble);
                float crisp = (1f - sheet) * 0.5f * edge;
                layerShape = WeatherMath.Lerp(layerShape, Scalar.Clamp01((layerShape - 0.08f) * 1.6f), crisp);
                tower = WeatherMath.Lerp(tower, Scalar.Clamp01((tower - 0.08f) * 1.6f), crisp);
            }
            float layerDensity = Scalar.Clamp01((layerShape - 0.12f) / 0.70f) * WeatherMath.Smoothstep(0.02f, 0.10f, layer);
            float rain = p.RainRate / 100f, scud = 0f;
            if (rain > 0.02f && y < lowest && y > lowest - 700f)
            {
                float fragment = Sample(nx / 900f + 0.29f, nz / 900f + 0.29f, y / 420f + 0.61f, 0);
                float band = WeatherMath.Smoothstep(lowest - 700f, lowest - 450f, y) * (1f - WeatherMath.Smoothstep(lowest - 180f, lowest, y));
                scud = WeatherMath.Smoothstep(0.62f, 0.78f, fragment + rain * 0.35f) * band * Scalar.Clamp01(rain * 6f);
            }
            float density = heroes;
            localLightingHeight = heroHeight;
            if (layerDensity * 0.55f * eyeKeep > density) { density = layerDensity * 0.55f * eyeKeep; localLightingHeight = layerHeight; }
            if (tower * 0.85f * eyeKeep > density) { density = tower * 0.85f * eyeKeep; localLightingHeight = h; }
            if (scud * 0.4f * eyeKeep > density)
            {
                density = scud * 0.4f * eyeKeep;
                localLightingHeight = CloudShape.LightingHeight(y, lowest - 700f, lowest);
            }
            if (nearDetail && density > 0.03f && density < 0.98f)
            {
                float cavity = Sample(nx / 900f + 0.71f, nz / 900f + 0.71f, y / 760f + 0.29f, 1);
                // Mirrors the shader's carveKeep: dense interiors keep most of their body.
                float keep = 1f - 0.7f * Scalar.SmoothStep(0.25f, 0.6f, density);
                density = Scalar.Clamp01(density - (cavity - 0.48f) * 1.1f * keep);
            }
            if (mid > density) { density = mid; localLightingHeight = midHeight; }
            if (density <= 0f) localLightingHeight = 0f;
            return density;
        }

        /// <summary>The set-pieces (shelf line, supercell, storm eye, lenticulars) as smooth
        /// envelopes: the shader's shapes without noise erosion, so flying into a storm eye or
        /// a supercell reads as cloud and casts a shadow. Strengths are the field's (they ramp
        /// over minutes with the state); a console toggle leads the GPU's 20 s fade briefly.</summary>
        public float Heroes(float x, float y, float z)
            => Heroes(x, y, z, out _);

        private float Heroes(float x, float y, float z, out float localLightingHeight)
        {
            localLightingHeight = 0f;
            if (field == null || y < 0f) return 0f;
            float hero = 0f;
            int count = field.SuperstructureCount;
            for (int i = 0; i < count; i++)
            {
                Superstructure s = field.SuperstructureAt(i);
                if (shownHeroes != null && i < shownHeroes.Length) s.Strength = shownHeroes[i];
                if (s.Strength <= 0.001f || y > s.Top + 1600f) continue;
                // Bounding circle (mirror of the shader's HeroReach): a point culled here
                // holds no set-piece cloud.
                HeroReach(s, out float cx, out float cz, out float radius);
                float dx = x - cx, dz = z - cz;
                if (dx * dx + dz * dz > radius * radius) continue;
                float height;
                float h = s.Kind == SuperstructureKind.ShelfLine ? ShelfEnvelope(s, x, y, z, out height)
                    : s.Kind == SuperstructureKind.Supercell ? CellEnvelope(s, x, y, z, out height)
                    : s.Kind == SuperstructureKind.StormEye ? EyeEnvelope(s, x, y, z, out height)
                    : LensEnvelope(s, x, y, z, out height);
                if (h > hero) { hero = h; localLightingHeight = height; }
            }
            return hero;
        }

        private static void HeroReach(Superstructure s, out float cx, out float cz, out float radius)
        {
            float dirX = (float)Math.Cos(s.Heading), dirZ = (float)Math.Sin(s.Heading);
            if (s.Kind == SuperstructureKind.ShelfLine)
            {
                float lengthHalf = s.Size + 9000f, depthHalf = (s.Extent + 17000f) * 0.5f;
                float off = 10000f - depthHalf;
                cx = s.X + dirZ * off;
                cz = s.Z - dirX * off;
                radius = (float)Math.Sqrt(lengthHalf * lengthHalf + depthHalf * depthHalf);
            }
            else if (s.Kind == SuperstructureKind.Supercell)
            {
                cx = s.X;
                cz = s.Z;
                radius = Math.Max(s.Extent * 2.8f, s.Size * 1.4f + 4000f) + 1000f;
            }
            else if (s.Kind == SuperstructureKind.StormEye)
            {
                cx = s.X;
                cz = s.Z;
                radius = s.Size + s.Extent * 4f;
            }
            else
            {
                float lengthHalf = s.Extent + 1.5f * s.Size;
                cx = s.X + dirX * s.Extent;
                cz = s.Z + dirZ * s.Extent;
                radius = (float)Math.Sqrt(lengthHalf * lengthHalf + s.Size * s.Size);
            }
        }

        private static float ShelfEnvelope(Superstructure s, float x, float y, float z, out float height)
        {
            height = 0f;
            float alongX = (float)Math.Cos(s.Heading), alongZ = (float)Math.Sin(s.Heading);
            float dx = x - s.X, dz = z - s.Z;
            float u = dx * alongX + dz * alongZ, v = dx * alongZ - dz * alongX;
            float taper = 1f - WeatherMath.Smoothstep(s.Size * 0.55f, s.Size + 7000f, Math.Abs(u));
            if (taper <= 0f) return 0f;
            float mass = WeatherMath.Envelope(v, -s.Extent - 5000f, -s.Extent * 0.55f, -700f, 500f) *
                WeatherMath.Envelope(y, 1250f, 1600f, s.Top - 1200f, s.Top + 300f) * 0.85f;
            float topV = 3850f - 2980f * Scalar.Clamp01(v / 7000f);
            float shelf = WeatherMath.Envelope(v, -400f, 0f, 6400f, 7000f) *
                WeatherMath.Smoothstep(410f, 540f, y) * (1f - WeatherMath.Smoothstep(topV - 350f, topV, y)) * 0.72f;
            height = shelf > mass ? CloudShape.LightingHeight(y, 450f, topV)
                : CloudShape.LightingHeight(y, 1250f, s.Top + 300f);
            return Math.Max(mass, shelf) * taper * s.Strength;
        }

        private static float CellEnvelope(Superstructure s, float x, float y, float z, out float height)
        {
            float dirX = (float)Math.Cos(s.Heading), dirZ = (float)Math.Sin(s.Heading);
            float h = Scalar.Clamp01(y / s.Top);
            float lean = h * 1800f;
            float dx = x - s.X - dirX * lean, dz = z - s.Z - dirZ * lean;
            float dome = (float)Math.Sqrt(Scalar.Clamp01(1f - (float)Math.Pow(Math.Max(0f, h - 0.72f) / 0.36f, 2f)));
            float radius = s.Size * (0.82f + 0.65f * WeatherMath.Smoothstep(0.35f, 0.82f, h)) * dome;
            float tower = (1f - WeatherMath.Smoothstep(radius * 0.7f, radius * 1.1f + 1f,
                    (float)Math.Sqrt(dx * dx + dz * dz))) *
                WeatherMath.Smoothstep(1100f, 1450f, y) * 0.9f;
            float ax = x - s.X, az = z - s.Z;
            float along = ax * dirX + az * dirZ - s.Extent * 0.35f;
            float across = -ax * dirZ + az * dirX;
            float e = (float)Math.Sqrt(along * along * 0.3025f + across * across) / s.Extent;
            float anvilTop = s.Top + 150f - 1200f * e * e;
            float anvilBase = anvilTop - WeatherMath.Lerp(6000f, 500f, WeatherMath.Smoothstep(0.05f, 0.75f, e)) - 150f;
            float anvil = (1f - WeatherMath.Smoothstep(0.4f, 0.78f, e)) *
                WeatherMath.Envelope(y, anvilBase - 50f, anvilBase + 260f, anvilTop - 350f, anvilTop + 120f) * 0.34f;
            height = anvil > tower ? CloudShape.LightingHeight(y, anvilBase, anvilTop)
                : CloudShape.LightingHeight(y, 1100f, s.Top * 1.08f);
            return Math.Max(tower, anvil) * s.Strength;
        }

        private static float EyeEnvelope(Superstructure s, float x, float y, float z, out float height)
        {
            float dx = x - s.X, dz = z - s.Z;
            float r = (float)Math.Sqrt(dx * dx + dz * dz);
            float eye = s.Size, wall = s.Extent;
            float h = Scalar.Clamp01(y / s.Top);
            float inner = eye * (1f + 0.9f * h * h);
            float crown = s.Top * 0.91f;
            float wallM = WeatherMath.Smoothstep(inner - 1500f, inner + 2750f, r) *
                (1f - WeatherMath.Smoothstep(eye + wall * 0.8f, eye + wall * 1.6f + 1500f, r)) *
                WeatherMath.Envelope(y, 300f, 900f, crown - 1500f, crown);
            float floorM = (1f - WeatherMath.Smoothstep(inner * 0.7f, inner, r)) *
                WeatherMath.Envelope(y, 500f, 700f, 1300f, 1800f) * 0.6f;
            float bands = WeatherMath.Envelope(r, eye + wall, eye + wall * 1.4f, eye + wall * 2.5f, eye + wall * 4f) *
                WeatherMath.Envelope(y, 400f, 900f, 5000f, 8500f) * 0.4f;
            height = floorM > Math.Max(wallM, bands) ? CloudShape.LightingHeight(y, 500f, 1800f)
                : wallM >= bands ? CloudShape.LightingHeight(y, 300f, crown)
                : CloudShape.LightingHeight(y, 400f, 8500f);
            return Math.Max(wallM, Math.Max(floorM, bands)) * s.Strength;
        }

        private static float LensEnvelope(Superstructure s, float x, float y, float z, out float height)
        {
            float dirX = (float)Math.Cos(s.Heading), dirZ = (float)Math.Sin(s.Heading);
            float dx = x - s.X, dz = z - s.Z;
            float along = dx * dirX + dz * dirZ, across = -dx * dirZ + dz * dirX;
            float acrossM = 1f - WeatherMath.Smoothstep(s.Size * 0.8f, s.Size, Math.Abs(across));
            float alongM = WeatherMath.Envelope(along, -s.Size * 1.5f - 1000f, -s.Size * 1.5f + 1000f,
                s.Extent * 2f + s.Size * 1.5f - 1000f, s.Extent * 2f + s.Size * 1.5f + 1000f);
            float yM = WeatherMath.Envelope(y, s.Top - 400f, s.Top, s.Top + 1400f, s.Top + 1800f);
            height = CloudShape.LightingHeight(y, s.Top - 400f, s.Top + 1800f);
            return acrossM * alongM * yM * 0.7f * s.Strength;
        }

        /// <summary>The console fog bank (-30..330 m), near-continuous, for the in-cloud feel.</summary>
        public float FogBank(float y)
        {
            if (fog <= 0f || y <= -30f || y >= 330f) return 0f;
            float h = (y + 30f) / 360f;
            return fog * 0.55f * Scalar.Clamp01(4f * h * (1f - h) * 1.3f) * 0.85f;
        }

        /// <summary>The raw weighted sum of the three lobe taps at one lobe size, before the remap.</summary>
        private float BodiesRaw(float nx, float y, float nz, float scale)
        {
            float period = scale * 4f;
            float a = Sample(nx / period, nz / period, y / (period * 0.9f), 2);
            float bnx = nz / (period * 2.73f) + 0.37f, bnz = -nx / (period * 2.73f) + 0.37f, bw = y / (period * 2.1f); // lattice coordinates derived from the noise-space point
            float b = Sample(bnx, bnz, bw, 0);
            // Lobe size varies inside a group (look pass round 2): where the slow field b is high a second, smaller lobe tap (turned 37 degrees) takes over.
            float fine = WeatherMath.Smoothstep(0.46f, 0.60f, b);
            if (fine > 0f)
            {
                float rnx = nx * 0.8f + nz * 0.6f, rnz = nz * 0.8f - nx * 0.6f, pf = period * SlabMath.LobeSmall;
                a = Ladder.Mix(a, Sample(rnx / pf + 0.29f, rnz / pf + 0.17f, y / (period * 0.9f), 2), fine); // the same vertical slice as the main tap: a slice of the lobe lattice has its own mean, and another slice moved the deck's cover
            }
            // Round 5: lobe dropout and amplitude variance (SlabMath.LobeKeep) from the lobe channel (about 2.7 lobe widths between lobes) and the erosion channel of
            // the same broad fetch the shader already makes (no new tap): a third of the lobes go, the rest vary in height, so no row of equal domes survives.
            a *= SlabMath.LobeKeep(Sample(bnx, bnz, bw, 2), Sample(bnx, bnz, bw, 1));
            float c = Sample(nx / (period * 0.29f) + 0.17f, nz / (period * 0.29f) + 0.61f, y / (period * 0.24f), 2);
            return a * 0.65f + b * 0.15f + c * 0.20f;
        }

        /// <summary>Warp before the lobe lookup (look pass rounds 2 and 3): two octaves of smooth vector noise (SmoothNoise; fixed lattices of 3.5 and 1.5 km,
        /// only the amplitude follows the lobe rung) bend the lookup point of the low deck's puffs, so the lobes of a group are not one Worley tile of
        /// equal puffs on a regular pitch. The round-2 warp read the noise volume, whose fine octaves folded the lookup over a third of the plane (mirrored
        /// lobes and straight-edged facets); the smooth noise has a bounded slope (LobeWarp x 1.875 x spread / cell stays under 1). Streets keep their stretched,
        /// regular rows: the warp fades out with them. FlightCloud.shader's Density does the same before its BodiesLadder call.</summary>
        internal void WarpLobes(ref float nx, float y, ref float nz, float rung, float streets)
        {
            float amp = Math.Min(Ladder.LobeMetres(rung) * SlabMath.LobeWarp, SlabMath.LobeWarpMax) * (1f - streets);
            SmoothNoise.Vec2(nx / SlabMath.LobeWarpCell + 0.23f, nz / SlabMath.LobeWarpCell + 0.41f, SmoothNoise.SaltLobeA, out float a, out float b);
            SmoothNoise.Vec2(nx / SlabMath.LobeWarpCell2 + 0.61f, nz / SlabMath.LobeWarpCell2 + 0.17f, SmoothNoise.SaltLobeB, out float c, out float d);
            float wnx = nx + ((a - 0.5f) + (c - 0.5f) * SlabMath.LobeWarp2) * amp;
            float wnz = nz + ((b - 0.5f) + (d - 0.5f) * SlabMath.LobeWarp2) * amp;
            nx = wnx; nz = wnz;
        }

        private static float RemapBodies(float raw) => Scalar.Clamp01((raw - 0.22f) * 2.5f);

        /// <summary>Bodies at a lobe rung index. A whole rung is one call; inside a ramp the two neighbouring rungs are mixed with
        /// the contrast kept, and the second rung is evaluated only when it shows (spec 6). The scale is the rung's lobe, never a
        /// scale that varies in space or time. Mirrors BodiesLadder in the shader.</summary>
        public float BodiesLadder(float nx, float y, float nz, float rungIndex)
        {
            Ladder.Split(rungIndex, out float r0, out float w);
            float raw0 = 0f, raw1 = 0f;
            if (w < 0.98f) raw0 = BodiesRaw(nx, y, nz, Ladder.LobeMetres(r0));
            if (w > 0.02f) raw1 = BodiesRaw(nx, y, nz, Ladder.LobeMetres(r0 + 1f));
            return RemapBodies(Ladder.Mix(raw0, raw1, w));
        }

        /// <summary>
        /// A thin layer's 2D cloud field at a point (spec 4.3, 7.4): the pattern of the layer's weighted morphologies (cells,
        /// ripples, streaks) at its period rung, the flattening from the veil weight, the edge from SlabCoverMask (which, with the
        /// Detail-independent rank, makes the covered area follow Cover) and the interior density from Detail. No altitude
        /// profile and no strength: the shader's Slab2D is the twin, at footprint zero.
        /// </summary>
        public float Slab2D(in SkyLayer row, float x, float y, float z, int layer = 0) => Slab2D(row, x, y, z, layer, out _, out _);

        /// <summary>Slab2D that also returns the layer's pattern value and its patch field (the calibration tests read them).</summary>
        internal float Slab2D(in SkyLayer row, float x, float y, float z, int layer, out float patternValue, out float patchField)
        {
            patternValue = 0.52f; patchField = 0.5f;
            float cover = row.Cover;
            if (cover <= 0.005f) return 0f;
            // The visible slab's mesoscale patch and warp share this RG sample.
            frame.NoiseXZ(x, z, layer == 0 ? NoiseXform.Mid : NoiseXform.High, out float lnx, out float lnz);
            SmoothNoise.Vec2(lnx / SlabMath.MesoWarpTile + 0.37f, lnz / SlabMath.MesoWarpTile + 0.19f, SmoothNoise.SaltMeso, out float mwx, out float mwz);
            lnx += (mwx - 0.5f) * SlabMath.MesoWarpAmp; lnz += (mwz - 0.5f) * SlabMath.MesoWarpAmp;
            float lowR = Sample(lnx / 27000f + 0.27f, lnz / 27000f + 0.27f, 0.71f, 0);
            float lowG = Sample(lnx / 27000f + 0.27f, lnz / 27000f + 0.27f, 0.71f, 1);
            // The patch field (R) and the cell-size field (G) of the second mesoscale fetch (look pass round 1).
            float mkR = Sample(lnx / SlabMath.PatchTile + 0.63f, lnz / SlabMath.PatchTile + 0.19f, 0.37f, 0);
            float mkG = Sample(lnx / SlabMath.PatchTile + 0.63f, lnz / SlabMath.PatchTile + 0.19f, 0.37f, 1);
            float sizeMask = SlabMath.SizeMask(mkG);
            float patchMask = SlabMath.PatchMask(mkR);
            SlabMath.Weights(row, out float wc, out float wr, out float ws, out float wv);
            Ladder.Split(row.PeriodRung, out float r0, out float w);
            float pat0 = 0f, pat1 = 0f;
            if (w < 0.98f) pat0 = SlabPattern(row, layer, x, y, z, Ladder.PeriodMetres(r0), lowR, lowG, sizeMask, patchMask, wc, wr, ws);
            if (w > 0.02f) pat1 = SlabPattern(row, layer, x, y, z, Ladder.PeriodMetres(r0 + 1f), lowR, lowG, sizeMask, patchMask, wc, wr, ws);
            float pattern = Ladder.Mix(pat0, pat1, w, 0.52f);
            patternValue = pattern; patchField = mkR;
            // The cover law (SlabMath.Rank): the rank is the pattern's place in its own distribution, so the layer covers about its Cover of the
            // sky however crisp or flat it is; Detail only scales the density inside the cover.
            float rank = SlabMath.Rank(pattern, SlabMath.PatternSigma(wc, wr, ws) * SlabMath.RankSoft);
            float body = WeatherMath.Lerp(rank, 0.7f + 0.3f * rank, wv);
            // The mesoscale cover: the old slow modulation, times the patch mask that clusters the cover into patches and clears the lanes between.
            float c = Scalar.Clamp01(cover * SlabMath.Trim(cover, ws, wv) * (0.55f + 0.9f * Scalar.Clamp01(0.5f + (lowG - 0.52f) * 3.16f)) *
                SlabMath.PatchFactor(SlabMath.PatchMask(mkR), SlabMath.Patchiness(cover, wv)));
            // How deep a point sits inside the cover (0 at the edge, 1 at the top rank): Detail turns it into density, so the interior has cores and thin fringes.
            float depth = Scalar.Clamp01((rank - (1f - c)) / Math.Max(c, 0.05f));
            float interior = WeatherMath.Lerp(SlabMath.Interior(depth), body, wv);
            return SlabMath.SlabCoverMask(body, c, row.Softness) * WeatherMath.Lerp(1f, interior, SlabMath.DetailDrive(row.Detail, wv));
        }

        /// <summary>The vector noise that bends the cell coordinate (in half-units of cells, to be scaled by cs x BendGain): the two channels of one SmoothNoise
        /// lattice of cs x tileMul metres. It replaced a read of the noise volume whose slope folded the bent coordinate over 38 % of the plane (the concentric
        /// arcs and 8 px cross-hatch of sky 5); the smooth noise bends the cells by up to a cell without folding.</summary>
        internal void CellBend(float pnx, float pnz, float y, float cs, float tileMul, out float bendX, out float bendY)
        {
            SmoothNoise.Vec2(pnx / (cs * tileMul) + 0.57f, pnz / (cs * tileMul) + 0.57f, SmoothNoise.SaltBend, out float a, out float b);
            bendX = a - 0.5f; bendY = b - 0.5f;
        }


        /// <summary>The weighted pattern value (about 0.52 on average) of a layer at one period; 0.52 when no pattern has weight.</summary>
        private float SlabPattern(in SkyLayer row, int layer, float x, float y, float z, float scale, float lowR, float lowG, float sizeMask, float patchMask,
            float wc, float wr, float ws)
        {
            if (wc + wr + ws <= 0f) return 0.52f;
            float warpGain = SlabMath.WarpGain * (1f - SlabMath.StreakWarpCut * ws / (wc + wr + ws));
            float warpX = (lowR - 0.5f) * scale * warpGain, warpZ = (lowG - 0.5f) * scale * warpGain;
            float px = x + warpX, pz = z + warpZ;
            float along = px * windX + pz * windZ, across = -px * windZ + pz * windX;
            // Rounded taps read the noise where the layer's rotation puts it; streak taps keep their stretch along the wind, so they
            // take the layer's offset only (the aligned layers have no rotation).
            frame.NoiseXZ(px, pz, layer == 0 ? NoiseXform.Mid : NoiseXform.High, out float pnx, out float pnz);
            frame.NoiseXZ(along, across, layer == 0 ? NoiseXform.MidAligned : NoiseXform.HighAligned, out float anx, out float anz);
            float streak = 0.52f, cells = 0.52f, bendX = 0f, bendY = 0f;
            if (ws > 0f)
            {
                // Round 5: streaks are two octaves of smooth value noise stretched along the wind (no texture): fibres (narrow) and broad streaks (turned 7 degrees),
                // their weights shifted by a low-frequency width field (so width and spacing vary), the whole broken into dashes of random length by a low-frequency
                // along-wind mask. The coordinate is bent only by low frequencies: curl (3.1 scales) and meander (3 scales).
                float sa = anx, sc = anz, span = scale * row.Aspect;
                if (row.Curl > 0.01f)
                {
                    SmoothNoise.Vec2(pnx / (scale * 3.1f) + 0.61f, pnz / (scale * 3.1f) + 0.61f, SmoothNoise.SaltCurl, out float curlA, out float curlB);
                    sa += (curlA - 0.5f) * scale * SlabMath.CurlGain * row.Curl;
                    sc += (curlB - 0.5f) * scale * SlabMath.CurlGain * row.Curl;
                }
                SmoothNoise.Vec2(pnx / (scale * SlabMath.MeanderTile) + 0.29f, pnz / (scale * SlabMath.MeanderTile) + 0.83f, SmoothNoise.SaltMeander, out float meanderAlong, out float meander);
                sc += (meander - 0.5f) * scale * SlabMath.Meander;
                sa += (meanderAlong - 0.5f) * span * SlabMath.MeanderAlong;
                SmoothNoise.Vec2(sa / (span * SlabMath.StreakWidthAlong) + 0.13f, sc / (scale * SlabMath.StreakWidthAcross) + 0.71f, SmoothNoise.SaltStreakWidth, out float widthField, out _);
                SmoothNoise.Vec2(sa / (span * SlabMath.StreakAlongA) + 0.37f, sc / (scale * SlabMath.StreakAcrossA) + 0.81f, SmoothNoise.SaltStreakA, out float nA, out _);
                float rax = sa * SlabMath.CosStreakB + sc * SlabMath.SinStreakB, rcz = sc * SlabMath.CosStreakB - sa * SlabMath.SinStreakB;
                SmoothNoise.Vec2(rax / (span * SlabMath.StreakAlongB) + 0.53f, rcz / (scale * SlabMath.StreakAcrossB) + 0.29f, SmoothNoise.SaltStreakB, out float nB, out _);
                SmoothNoise.Vec2(sa / (span * SlabMath.SegAlong) + 0.77f, sc / (scale * SlabMath.SegAcross) + 0.23f, SmoothNoise.SaltStreakSeg, out float seg, out _);
                streak = SlabMath.StreakPattern(nA, nB, WeatherMath.Smoothstep(0.2f, 0.8f, widthField), seg, 1f, 1f);
            }
            if (wc + wr > 0f)
            {
                float cs = scale * 0.45f;
                CellBend(pnx, pnz, y, cs, SlabMath.BendTile, out bendX, out bendY);
                cells = CellField(pnx + bendX * cs * SlabMath.BendGain, pnz + bendY * cs * SlabMath.BendGain, scale, sizeMask);
            }
            float ripples = cells;
            if (wr > 0f)
            {
                // Round 5: rows only where the Ripples weight is above 0.05 (Weights drops the rest). The wavelength varies by about a third (a phase noise of RowCycles
                // cycles on a lattice of RowLowTile wavelengths), two row sets (1 and 1.43 wavelengths, turned 17 degrees) mix at a weight that follows the size field so rows meet in
                // dislocations, and the patch mask gates the amplitude (rows fade in the lanes between patches).
                float lambda = row.RippleWavelength;
                SmoothNoise.Vec2(pnx / (lambda * SlabMath.RowLowTile) + 0.41f, pnz / (lambda * SlabMath.RowLowTile) + 0.17f, SmoothNoise.SaltRows, out float rowA, out float rowB);
                float rows1 = 0.55f + 0.45f * (float)Math.Sin((across / lambda + SlabMath.RowCycles * (rowA - 0.5f)) * 2f * Math.PI + warpX / scale * 3f + bendX * SlabMath.RowBend1);
                float across2 = across * 0.9563f + along * 0.2924f;
                float rows2 = 0.55f + 0.45f * (float)Math.Sin((across2 / (lambda * 1.43f) + SlabMath.RowCycles * (rowB - 0.5f)) * 2f * Math.PI + warpZ / scale * 3f + bendY * SlabMath.RowBend2 + 1.7f);
                float rows = WeatherMath.Lerp(rows1, rows2, SlabMath.RowMixBase + SlabMath.RowMixSize * sizeMask);
                float rowKeep = Scalar.Clamp01(0.7f + bendY * 3.5f) * WeatherMath.Lerp(SlabMath.RowGateLo, 1f, patchMask);
                // rows averages 0.55, so dividing by it keeps the pattern's mean at the cells' (0.52).
                ripples = cells * WeatherMath.Lerp(1f, rows / 0.55f, 0.8f * rowKeep);
            }
            return (wc * cells + wr * ripples + ws * streak) / (wc + wr + ws);
        }

        /// <summary>The cell pattern (about 0.52 on average, spread SigmaCells) at a bent, noise-space point: a thresholded sum of three octaves of smooth value noise
        /// at incommensurate scales (1, 2.3 and 5.1 times CellFine x scale) turned 19, 37 and 71 degrees; the coarse octave warps the mid one and both warp the fine one (so no octave
        /// puts its blobs on its own lattice points), and a low-frequency size field (12 fine lattices) shifts the weights between fine and coarse. FlightCloud.shader's twin also
        /// fades each octave out as the pixel footprint approaches its lattice; here the footprint is zero.</summary>
        internal static float CellField(float qx, float qz, float scale, float sizeMask)
        {
            float l1 = scale * SlabMath.CellFine;
            float r3x = qx * SlabMath.Cos71 + qz * SlabMath.Sin71, r3z = qz * SlabMath.Cos71 - qx * SlabMath.Sin71;
            SmoothNoise.Vec2(r3x / (l1 * SlabMath.CellCoarse) + 0.11f, r3z / (l1 * SlabMath.CellCoarse) + 0.43f, SmoothNoise.SaltCellC, out float n3, out float n3b);
            float r2x = qx * SlabMath.Cos37 + qz * SlabMath.Sin37, r2z = qz * SlabMath.Cos37 - qx * SlabMath.Sin37;
            r2x += (n3 - 0.5f) * l1 * SlabMath.CellMid * SlabMath.CellWarpMid; r2z += (n3b - 0.5f) * l1 * SlabMath.CellMid * SlabMath.CellWarpMid;
            SmoothNoise.Vec2(r2x / (l1 * SlabMath.CellMid) + 0.53f, r2z / (l1 * SlabMath.CellMid) + 0.19f, SmoothNoise.SaltCellB, out float n2, out float n2b);
            float r1x = qx * SlabMath.Cos19 + qz * SlabMath.Sin19, r1z = qz * SlabMath.Cos19 - qx * SlabMath.Sin19;
            r1x += ((n2 - 0.5f) + (n3 - 0.5f)) * l1 * SlabMath.CellWarpFine; r1z += ((n2b - 0.5f) + (n3b - 0.5f)) * l1 * SlabMath.CellWarpFine;
            SmoothNoise.Vec2(r1x / l1 + 0.31f, r1z / l1 + 0.77f, SmoothNoise.SaltCellA, out float n1, out _);
            SmoothNoise.Vec2(qx / (l1 * SlabMath.CellSizeTile) + 0.7f, qz / (l1 * SlabMath.CellSizeTile) + 0.2f, SmoothNoise.SaltCellSize, out float sizeField, out _);
            return SlabMath.CellPattern(n1, n2, n3, SlabMath.CellSizeBlend(sizeField, sizeMask), 1f, 1f, 1f);
        }

        private SkyParams RowAt(float x, float z) => tape.IsConstant ? constantSky : tape.At(frame.S(x, z));

        /// <summary>The boundary erosion at one edge scale k (1 = the 1250 m erosion and 340/300 m micro periods).</summary>
        private float EdgeDetail(float nx, float y, float nz, float k, float edge, float sheetBlend)
        {
            // Cumulus edges take the rounded lobes (channel 2) for the near octave, sheets the stringy erosion (channel 1).
            float detail = Sample(nx / (1250f * k), nz / (1250f * k), y / (1250f * k), 0) * 0.72f +
                WeatherMath.Lerp(Sample(nx / (1250f * k) * 3.1f + 0.21f, nz / (1250f * k) * 3.1f + 0.21f, y / (1250f * k) * 3.1f + 0.21f, 2),
                    Sample(nx / (1250f * k) * 3.1f + 0.21f, nz / (1250f * k) * 3.1f + 0.21f, y / (1250f * k) * 3.1f + 0.21f, 1), sheetBlend) * 0.28f;
            if (edge > 0.35f)
                detail = detail * 0.65f + Sample(nx / (340f * k) + 0.19f, nz / (340f * k) + 0.19f, y / (300f * k) + 0.83f, 1) * 0.35f;
            return detail;
        }

        /// <summary>The middle layer (altocumulus .. altostratus) at a point: its 2D field, its altitude profile and its optical strength.</summary>
        public float MidLayer(float x, float y, float z)
        {
            SkyLayer row = RowAt(x, z).Mid;
            if (row.Cover <= 0.005f) return 0f;
            float h = (y - row.Base) / Math.Max(1f, row.Thick);
            if (h <= 0f || h >= 1f) return 0f;
            return Slab2D(row, x, y, z) * SlabMath.Profile(h) * row.Strength;
        }

        /// <summary>Rounded lobe field. Four cells per texture tile: scale is the lobe width,
        /// not the tile width. Broad variation breaks equal-sized cellular packing.</summary>
        public float Bodies(float nx, float y, float nz, float scale) => RemapBodies(BodiesRaw(nx, y, nz, scale));

        public static float CoverMask(float body, float cover) => SlabMath.CoverMask(body, cover);

        /// <summary>Bilinear clamped RGBA map read, with the same texel centres/units as SampleWeather.</summary>
        public static WeatherPoint SampleMap(byte[] structure, byte[] profiles, int mapSize, float u, float v)
        {
            float x = Math.Max(0f, Math.Min(mapSize - 1f, u * mapSize - 0.5f));
            float z = Math.Max(0f, Math.Min(mapSize - 1f, v * mapSize - 0.5f));
            int x0 = (int)x, z0 = (int)z, x1 = Math.Min(x0 + 1, mapSize - 1), z1 = Math.Min(z0 + 1, mapSize - 1);
            int a = (z0 * mapSize + x0) * 4, b = (z0 * mapSize + x1) * 4;
            int c = (z1 * mapSize + x0) * 4, d = (z1 * mapSize + x1) * 4;
            float tx = x - x0, tz = z - z0;
            float lowTop = MapChannel(structure, a, b, c, d, 3, tx, tz) * 16000f;
            float cloudBase = MapChannel(profiles, a, b, c, d, 2, tx, tz) * CloudMaps.BaseRange;
            return new WeatherPoint
            {
                BackgroundCover = MapChannel(structure, a, b, c, d, 0, tx, tz),
                CellShape = MapChannel(structure, a, b, c, d, 2, tx, tz),
                LowTop = lowTop,
                CloudTop = Math.Max(lowTop, cloudBase),
                CloudBase = cloudBase,
                RainRate = MapChannel(profiles, a, b, c, d, 3, tx, tz) * 100f,
            };
        }

        private static float MapChannel(byte[] bytes, int a, int b, int c, int d, int channel, float x, float z) =>
            WeatherMath.Lerp(WeatherMath.Lerp(bytes[a + channel], bytes[b + channel], x),
                WeatherMath.Lerp(bytes[c + channel], bytes[d + channel], x), z) / 255f;

        public static WeatherPoint BlendMaps(WeatherPoint a, WeatherPoint b, float weight) => new WeatherPoint
        {
            BackgroundCover = WeatherMath.Lerp(a.BackgroundCover, b.BackgroundCover, weight),
            CellShape = WeatherMath.Lerp(a.CellShape, b.CellShape, weight),
            LowTop = WeatherMath.Lerp(a.LowTop, b.LowTop, weight),
            CloudTop = WeatherMath.Lerp(a.CloudTop, b.CloudTop, weight),
            CloudBase = WeatherMath.Lerp(a.CloudBase, b.CloudBase, weight),
            RainRate = WeatherMath.Lerp(a.RainRate, b.RainRate, weight),
        };

        public static void MapWeights(float x, float z, float nearHalf, float farHalf, out float nearWeight, out float farFade)
        {
            float nearEdge = 0.5f - Math.Max(Math.Abs(x), Math.Abs(z)) / (nearHalf * 2f);
            float farEdge = 0.5f - Math.Max(Math.Abs(x), Math.Abs(z)) / (farHalf * 2f);
            nearWeight = WeatherMath.Smoothstep(0f, 0.04f, nearEdge);
            farFade = WeatherMath.Smoothstep(0f, 0.08f, farEdge);
        }

        /// <summary>Tests only: rounds the filter weights to 8 bits like the GPU's texture unit, to estimate the CPU-GPU delta of a formula before the
        /// density check runs it on the GPU.</summary>
        internal static bool QuantiseWeights;

        /// <summary>Trilinear, repeating read of one channel (R = body, G = detail, B = lobe), like the shader's tex3D.</summary>
        private float Sample(float u, float v, float w, int channel)
        {
            float fx = u * size - 0.5f, fy = v * size - 0.5f, fz = w * size - 0.5f;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy), z0 = (int)Math.Floor(fz);
            float tx = fx - x0, ty = fy - y0, tz = fz - z0;
            if (QuantiseWeights) { tx = (float)Math.Floor(tx * 256f) / 256f; ty = (float)Math.Floor(ty * 256f) / 256f; tz = (float)Math.Floor(tz * 256f) / 256f; }
            float c000 = C(x0, y0, z0, channel), c100 = C(x0 + 1, y0, z0, channel);
            float c010 = C(x0, y0 + 1, z0, channel), c110 = C(x0 + 1, y0 + 1, z0, channel);
            float c001 = C(x0, y0, z0 + 1, channel), c101 = C(x0 + 1, y0, z0 + 1, channel);
            float c011 = C(x0, y0 + 1, z0 + 1, channel), c111 = C(x0 + 1, y0 + 1, z0 + 1, channel);
            float x00 = c000 + (c100 - c000) * tx, x10 = c010 + (c110 - c010) * tx;
            float x01 = c001 + (c101 - c001) * tx, x11 = c011 + (c111 - c011) * tx;
            float y0v = x00 + (x10 - x00) * ty, y1v = x01 + (x11 - x01) * ty;
            return (y0v + (y1v - y0v) * tz) / 255f;
        }

        private float C(int x, int y, int z, int channel)
        {
            int m = size - 1; // size is a power of two
            return noise[(((z & m) * size + (y & m)) * size + (x & m)) * 4 + channel];
        }
    }
}
