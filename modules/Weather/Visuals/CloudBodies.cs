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
        private readonly SkySplit split;
        private readonly float fog;
        private readonly WeatherField field;
        private readonly float[] shownHeroes;
        private readonly bool nearDetail;
        private readonly Func<float, float, WeatherPoint> weatherSampler;
        private readonly float eyeX, eyeZ, eyeRadius, eyeStrength;

        /// <summary>One sky everywhere: a one-keyframe tape of <paramref name="sky"/> (tests, and callers with no tape).</summary>
        public CloudBodies(byte[] noise, int size, in SkyParams sky, float prevailingHeading = 0f, SkySplit split = default,
            float fog = 0f, WeatherField field = null, float[] shownHeroes = null, bool nearDetail = false,
            Func<float, float, WeatherPoint> weatherSampler = null)
            : this(noise, size, TapeSampler.Constant(sky), default, prevailingHeading, split, fog, field, shownHeroes,
                nearDetail, weatherSampler)
        {
        }

        public CloudBodies(byte[] noise, int size, TapeSampler tape, TapeFrame frame, float prevailingHeading = 0f,
            SkySplit split = default, float fog = 0f, WeatherField field = null, float[] shownHeroes = null,
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
            this.split = split;
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

        /// <summary>Cloud density at a global position; <paramref name="shift"/> is the
        /// renderer's cloud-height shift, <paramref name="p"/> the weather at that column.</summary>
        public float Density(WeatherPoint p, float x, float y, float z, float shift)
            => Density(p, x, y, z, shift, out _);

        public float Density(WeatherPoint p, float x, float y, float z, float shift, out float localLightingHeight)
        {
            localLightingHeight = 0f;
            float broadR = Sample(x / 24000f, z / 24000f, 0.37f, 0);
            float broadG = Sample(x / 24000f, z / 24000f, 0.37f, 1);
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
            float layer = p.BackgroundCover, front = p.FrontCover, cell = p.CellShape;
            if (Math.Max(layer, Math.Max(front, cell)) < 0.025f) return background;
            float smooth = Scalar.Clamp01(sky.DeckSmooth);
            float baseY = p.CloudBase + shift;
            // CloudMaps stores a conservative top including the low deck's independent depth.
            float lowTop = p.LowTop > 0f ? p.LowTop : p.CloudTop;
            float topY = Math.Max(baseY + Math.Max(280f, Math.Max(genus.PuffDepth, sky.DeckDepth) + genus.BaseWobble * 0.5f), lowTop + shift);
            float columnTop = WeatherMath.Lerp(Math.Min(topY, CloudShape.TowerCap(baseY, genus, sky.DeckDepth)), topY, genus.TowerBlend);
            columnTop = Math.Max(columnTop, baseY + 280f);
            float lowest = front > 0.02f ? Math.Min(baseY, p.FrontBase + shift) : baseY;
            float highest = Math.Max(columnTop, baseY + Math.Max(genus.PuffDepth, sky.DeckDepth) + genus.BaseWobble * 0.5f);
            if (front > 0.02f) highest = Math.Max(highest, p.FrontTop + shift);
            if (y < lowest - 700f || y > highest + 600f) return background;

            // The shader warps bodies by its broad noise so no grid shows at range.
            float wx = x + (broadR - 0.5f) * 1400f, wz = z + (broadG - 0.5f) * 1400f;
            float puffBody = genus.Sheet ? 0.72f
                : WeatherMath.Lerp(BodiesLadder(wx, y, wz, genus.LobeRung, false, genus.SheetBlend), 0.72f, genus.SheetBlend);
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

            // Fronts: the same profile at a larger scale. A thin shield stays stratiform;
            // a deep band picks up the genus dome.
            float frontShape = 0f;
            float frontHeight = 0f;
            if (front > 0.02f)
            {
                float frontBase = p.FrontBase + shift;
                float frontCrown = Math.Max(frontBase + 300f, p.FrontTop + shift);
                float depth = frontCrown - frontBase;
                float fb = BodiesLadder(wx + 5311f, y + 5311f, wz + 5311f, genus.LobeRung, true, genus.SheetBlend);
                float fm = CoverMask(fb, Scalar.Clamp01(front * 1.05f));
                float frontFloor = frontBase + (fb - 0.5f) * Math.Min(220f, genus.BaseWobble + 40f);
                float frontTop = frontCrown + (broadR - 0.5f) * Math.Min(1200f, depth * 0.25f) - (1f - fm) * Math.Min(depth * 0.25f, 900f);
                float fh = (y - frontFloor) / Math.Max(1f, frontTop - frontFloor);
                frontHeight = Scalar.Clamp01(fh);
                float uplift = WeatherMath.Smoothstep(3500f, 6500f, depth);
                float frontAnvil = CloudShape.LocalAnvil(depth, genus.Anvil);
                float prof = CloudShape.Profile(fh, CloudShape.LocalDome(WeatherMath.Lerp(0.25f, genus.Dome, uplift), frontAnvil), frontAnvil);
                float gate = CloudShape.BaseGate(y, frontFloor, Math.Max(genus.BaseSharp, 80f));
                float thin = WeatherMath.Lerp(0.35f, 1f, WeatherMath.Smoothstep(900f, 2200f, depth));
                frontShape = CloudShape.Mass(fm, prof) * gate * thin;
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
                float edge = Scalar.Clamp01(1f - Math.Abs(Math.Max(layerShape, Math.Max(frontShape, tower)) - 0.42f) * 2.4f);
                // The edge texture follows the lobe: the erosion periods scale with the rung's lobe size (spec 4.2), and a ramp
                // blends the two rungs' textures by the same weight as their bodies.
                Ladder.Split(genus.LobeRung, out float edgeRung, out float edgeW);
                float sheet = genus.SheetBlend;
                float detail = 0.5f;
                if (genus.Billow > 0.01f && edge > 0.08f)
                {
                    float d0 = 0f;
                    if (edgeW < 0.98f) d0 = EdgeDetail(x, y, z, Ladder.EdgeScale(Ladder.LobeMetres(edgeRung)), edge, sheet);
                    detail = d0;
                    if (edgeW > 0.02f)
                        detail = WeatherMath.Lerp(d0, EdgeDetail(x, y, z, Ladder.EdgeScale(Ladder.LobeMetres(edgeRung + 1f)), edge, sheet),
                            edgeW < 0.98f ? edgeW : 1f);
                }
                // The shader's nibble and its steeper rim on heaped cloud (not on the front shield), so the mirror and the march
                // agree on the boundary at a standing camera. The far map's smoothing (coarse >= 1) is not mirrored.
                float nibble = Math.Max(0f, WeatherMath.Lerp(0.72f, 0.65f, sheet) - detail) * genus.Billow * edge * WeatherMath.Lerp(0.70f, 0.45f, sheet);
                layerShape = Scalar.Clamp01(layerShape - nibble); frontShape = Scalar.Clamp01(frontShape - nibble); tower = Scalar.Clamp01(tower - nibble);
                float crisp = (1f - sheet) * 0.5f * edge;
                layerShape = WeatherMath.Lerp(layerShape, Scalar.Clamp01((layerShape - 0.08f) * 1.6f), crisp);
                tower = WeatherMath.Lerp(tower, Scalar.Clamp01((tower - 0.08f) * 1.6f), crisp);
            }
            float layerDensity = Scalar.Clamp01((layerShape - 0.12f) / 0.70f) * WeatherMath.Smoothstep(0.02f, 0.10f, layer);
            float frontDensity = Scalar.Clamp01((frontShape - 0.12f) / 0.72f) * WeatherMath.Smoothstep(0.04f, 0.20f, front);
            float rain = p.RainRate / 100f, scud = 0f;
            if (rain > 0.02f && y < lowest && y > lowest - 700f)
            {
                float fragment = Sample(x / 900f + 0.29f, z / 900f + 0.29f, y / 420f + 0.61f, 0);
                float band = WeatherMath.Smoothstep(lowest - 700f, lowest - 450f, y) * (1f - WeatherMath.Smoothstep(lowest - 180f, lowest, y));
                scud = WeatherMath.Smoothstep(0.62f, 0.78f, fragment + rain * 0.35f) * band * Scalar.Clamp01(rain * 6f);
            }
            float density = heroes;
            localLightingHeight = heroHeight;
            if (layerDensity * 0.55f * eyeKeep > density) { density = layerDensity * 0.55f * eyeKeep; localLightingHeight = layerHeight; }
            if (frontDensity * 0.60f * eyeKeep > density) { density = frontDensity * 0.60f * eyeKeep; localLightingHeight = frontHeight; }
            if (tower * 0.85f * eyeKeep > density) { density = tower * 0.85f * eyeKeep; localLightingHeight = h; }
            if (scud * 0.4f * eyeKeep > density)
            {
                density = scud * 0.4f * eyeKeep;
                localLightingHeight = CloudShape.LightingHeight(y, lowest - 700f, lowest);
            }
            if (nearDetail && density > 0.03f && density < 0.98f)
            {
                float cavity = Sample(x / 900f + 0.71f, z / 900f + 0.71f, y / 760f + 0.29f, 1);
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
        private float BodiesRaw(float x, float y, float z, float scale)
        {
            float period = scale * 4f;
            float a = Sample(x / period, z / period, y / (period * 0.9f), 2);
            float b = Sample(z / (period * 2.73f) + 0.37f, -x / (period * 2.73f) + 0.37f, y / (period * 2.1f), 0);
            float c = Sample(x / (period * 0.29f) + 0.17f, z / (period * 0.29f) + 0.61f, y / (period * 0.24f), 2);
            return a * 0.65f + b * 0.15f + c * 0.20f;
        }

        private static float RemapBodies(float raw) => Scalar.Clamp01((raw - 0.22f) * 2.5f);

        /// <summary>The front body's scale for a lobe: the shield's lumps are larger than a deck's, and a sheet fades to a fixed 5.2 km.
        /// INTERIM(slice 4): the front parameters of LadderScale and BodiesLadder go with the frontal shield. Its scale lerps with
        /// sheetBlend, a scale that varies in space, and is the one place the ladder rule of spec 6 is broken knowingly.</summary>
        private static float LadderScale(float rung, bool front, float sheetBlend)
        {
            float lobe = Ladder.LobeMetres(rung);
            return front ? WeatherMath.Lerp(Math.Max(1800f, lobe * 1.35f), 5200f, sheetBlend) : lobe;
        }

        /// <summary>Bodies at a lobe rung index. A whole rung is one call; inside a ramp the two neighbouring rungs are mixed with
        /// the contrast kept, and the second rung is evaluated only when it shows (spec 6). A sheet front is one fixed scale at every
        /// rung, so it never takes a second tap (and Ladder.Mix would otherwise boost the deviation of two identical fields by up to 1.41).
        /// Mirrors BodiesLadder in the shader. INTERIM(slice 4): the front and sheetBlend parameters go with the frontal shield.</summary>
        public float BodiesLadder(float x, float y, float z, float rungIndex, bool front, float sheetBlend)
        {
            Ladder.Split(rungIndex, out float r0, out float w);
            if (front && sheetBlend >= 0.999f) w = 0f;
            float raw0 = 0f, raw1 = 0f;
            if (w < 0.98f) raw0 = BodiesRaw(x, y, z, LadderScale(r0, front, sheetBlend));
            if (w > 0.02f) raw1 = BodiesRaw(x, y, z, LadderScale(r0 + 1f, front, sheetBlend));
            return RemapBodies(Ladder.Mix(raw0, raw1, w));
        }

        /// <summary>
        /// A thin layer's 2D cloud field at a point (spec 4.3, 7.4): the pattern of the layer's weighted morphologies (cells,
        /// ripples, streaks) at its period rung, the flattening from the veil weight, the edge from SlabCoverMask (which, with the
        /// Detail-independent rank, makes the covered area follow Cover) and the interior density from Detail. No altitude
        /// profile and no strength: the shader's Slab2D is the twin, at footprint zero.
        /// </summary>
        public float Slab2D(in SkyLayer row, float x, float y, float z)
        {
            float cover = row.Cover;
            if (cover <= 0.005f) return 0f;
            // The visible slab's mesoscale patch and warp share this RG sample.
            float lowR = Sample(x / 27000f + 0.27f, z / 27000f + 0.27f, 0.71f, 0);
            float lowG = Sample(x / 27000f + 0.27f, z / 27000f + 0.27f, 0.71f, 1);
            SlabMath.Weights(row, out float wc, out float wr, out float ws, out float wv);
            Ladder.Split(row.PeriodRung, out float r0, out float w);
            float pat0 = 0f, pat1 = 0f;
            if (w < 0.98f) pat0 = SlabPattern(row, x, y, z, Ladder.PeriodMetres(r0), lowR, lowG, wc, wr, ws);
            if (w > 0.02f) pat1 = SlabPattern(row, x, y, z, Ladder.PeriodMetres(r0 + 1f), lowR, lowG, wc, wr, ws);
            float pattern = Ladder.Mix(pat0, pat1, w, 0.52f);
            // The cover law: the rank does not depend on Detail, so the layer covers about its Cover of the sky however crisp or flat it is;
            // Detail only scales the density inside the cover.
            float rank = Scalar.Clamp01(0.5f + (pattern - 0.52f) * SlabMath.RankGain);
            float body = WeatherMath.Lerp(rank, 0.7f + 0.3f * rank, wv);
            float c = Scalar.Clamp01(cover * (0.55f + 0.9f * Scalar.Clamp01(0.5f + (lowG - 0.52f) * 3.16f)));
            return SlabMath.SlabCoverMask(body, c, row.Softness) * WeatherMath.Lerp(1f, body, row.Detail);
        }

        /// <summary>The weighted pattern value (about 0.52 on average) of a layer at one period; 0.52 when no pattern has weight.</summary>
        private float SlabPattern(in SkyLayer row, float x, float y, float z, float scale, float lowR, float lowG,
            float wc, float wr, float ws)
        {
            if (wc + wr + ws <= 0f) return 0.52f;
            float warpX = (lowR - 0.5f) * scale * 1.6f, warpZ = (lowG - 0.5f) * scale * 1.6f;
            float px = x + warpX, pz = z + warpZ;
            float along = px * windX + pz * windZ, across = -px * windZ + pz * windX;
            float streak = 0.52f, cells = 0.52f;
            if (ws > 0f)
            {
                float a = along, b = across;
                if (row.Curl > 0.01f)
                {
                    // Curl bends the streak coordinate with low-frequency vector noise: hooked cirrus. One fixed sign.
                    a += (Sample(px / (scale * 3.1f) + 0.61f, pz / (scale * 3.1f) + 0.61f, 0.33f, 0) - 0.5f) * scale * 2f * row.Curl;
                    b += (Sample(px / (scale * 3.1f) + 0.61f, pz / (scale * 3.1f) + 0.61f, 0.33f, 1) - 0.5f) * scale * 2f * row.Curl;
                }
                float span = scale * row.Aspect;
                float s1 = Sample(a / span, b / scale, y / (scale * 0.6f) + 0.17f, 0);
                float a2 = a * 0.982f - b * 0.191f, c2x = a * 0.191f + b * 0.982f;
                float s2 = Sample(a2 / (span * 1.73f) + 0.53f, c2x / (scale * 1.73f) + 0.29f, y / (scale * 1.1f) + 0.61f, 0);
                streak = s1 * 0.6f + s2 * 0.4f;
            }
            if (wc + wr > 0f)
            {
                float cs = scale * 0.45f;
                float c1 = Sample(px / cs + 0.41f, pz / cs + 0.41f, y / (cs * 0.8f), 0);
                float c2 = Sample(pz / (cs * 2.37f) + 0.13f, -px / (cs * 2.37f) + 0.13f, y / (cs * 1.9f) + 0.77f, 0);
                cells = c1 * 0.62f + c2 * 0.38f;
            }
            float ripples = cells;
            if (wr > 0f)
            {
                float rows = 0.55f + 0.45f * (float)Math.Sin(across / row.RippleWavelength * 2f * Math.PI + warpX / scale * 2f);
                // rows averages 0.55, so dividing by it keeps the pattern's mean at the cells' (0.52): without it a ripple layer sat at 0.64 of a cell
                // layer's mean and, through the cover mask, covered a fraction of its cover.
                ripples = cells * WeatherMath.Lerp(1f, rows / 0.55f, 0.8f);
            }
            return (wc * cells + wr * ripples + ws * streak) / (wc + wr + ws);
        }

        private SkyParams RowAt(float x, float z) => tape.IsConstant ? constantSky : tape.At(frame.S(x, z));

        /// <summary>The boundary erosion at one edge scale k (1 = the 1250 m erosion and 340/300 m micro periods).</summary>
        private float EdgeDetail(float x, float y, float z, float k, float edge, float sheetBlend)
        {
            // Cumulus edges take the rounded lobes (channel 2) for the near octave, sheets the stringy erosion (channel 1).
            float u = x / (1250f * k) * 3.1f + 0.21f, v = z / (1250f * k) * 3.1f + 0.21f, w = y / (1250f * k) * 3.1f + 0.21f;
            float detail = Sample(x / (1250f * k), z / (1250f * k), y / (1250f * k), 0) * 0.72f +
                WeatherMath.Lerp(Sample(u, v, w, 2), Sample(u, v, w, 1), sheetBlend) * 0.28f;
            if (edge > 0.35f)
                detail = detail * 0.65f + Sample(x / (340f * k) + 0.19f, z / (340f * k) + 0.19f, y / (300f * k) + 0.83f, 1) * 0.35f;
            return detail;
        }

        /// <summary>The middle layer (altocumulus .. altostratus) at a point: its 2D field, its altitude profile and its optical strength.</summary>
        public float MidLayer(float x, float y, float z)
        {
            SkyLayer row = RowAt(x, z).Mid;
            if (row.Cover <= 0.005f) return 0f;
            float h = (y - row.Base) / Math.Max(1f, row.Thick);
            if (h <= 0f || h >= 1f) return 0f;
            // Thins ahead of the frontal boundary, as the shader's middle layer does. INTERIM(slice 4): the split goes with the front.
            if (split.Amount > 0f) row.Cover *= WeatherMath.Lerp(1f - 0.75f * split.Amount, 1f, split.Share(x, z));
            return Slab2D(row, x, y, z) * SlabMath.Profile(h) * row.Strength;
        }

        /// <summary>Rounded lobe field. Four cells per texture tile: scale is the lobe width,
        /// not the tile width. Broad variation breaks equal-sized cellular packing.</summary>
        public float Bodies(float x, float y, float z, float scale) => RemapBodies(BodiesRaw(x, y, z, scale));

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
            float frontCover = MapChannel(structure, a, b, c, d, 1, tx, tz);
            float frontTop = MapChannel(profiles, a, b, c, d, 1, tx, tz) * 16000f;
            float cloudBase = MapChannel(profiles, a, b, c, d, 2, tx, tz) * 16000f;
            return new WeatherPoint
            {
                BackgroundCover = MapChannel(structure, a, b, c, d, 0, tx, tz),
                FrontCover = frontCover,
                CellShape = MapChannel(structure, a, b, c, d, 2, tx, tz),
                LowTop = lowTop,
                CloudTop = Math.Max(lowTop, WeatherMath.Lerp(cloudBase, frontTop, frontCover)),
                FrontBase = MapChannel(profiles, a, b, c, d, 0, tx, tz) * 16000f,
                FrontTop = frontTop,
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
            FrontCover = WeatherMath.Lerp(a.FrontCover, b.FrontCover, weight),
            CellShape = WeatherMath.Lerp(a.CellShape, b.CellShape, weight),
            LowTop = WeatherMath.Lerp(a.LowTop, b.LowTop, weight),
            CloudTop = WeatherMath.Lerp(a.CloudTop, b.CloudTop, weight),
            FrontBase = WeatherMath.Lerp(a.FrontBase, b.FrontBase, weight),
            FrontTop = WeatherMath.Lerp(a.FrontTop, b.FrontTop, weight),
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

        /// <summary>Trilinear, repeating read of one channel (R = body, G = detail, B = lobe), like the shader's tex3D.</summary>
        private float Sample(float u, float v, float w, int channel)
        {
            float fx = u * size - 0.5f, fy = v * size - 0.5f, fz = w * size - 0.5f;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy), z0 = (int)Math.Floor(fz);
            float tx = fx - x0, ty = fy - y0, tz = fz - z0;
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
