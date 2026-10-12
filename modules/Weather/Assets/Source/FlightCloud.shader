Shader "Boscali/FlightCloud"
{
    Properties { _CloudDensity ("Density", Range(0,2)) = 1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }

        HLSLINCLUDE
        #include "UnityCG.cginc"

        sampler2D _CameraDepthTexture;
        sampler2D _WeatherMapTex;
        sampler2D _WeatherProfileTex;
        sampler2D _WeatherFarMapTex;
        sampler2D _WeatherFarProfileTex;
        // Conservative empty space over the near map (CloudMaps.Envelope), point sampled.
        sampler2D _WeatherEnvelopeTex;
        float _WeatherEnvelopeOn;
        sampler3D _CloudNoiseTex;
        float3 _CloudWorldOffset, _CloudCameraForward, _CloudSunDirection, _CloudCameraPos;
        float3 _CloudSunColor, _CloudAmbientColor, _CloudGroundColor, _CloudFogColor;
        float2 _CloudWindOffset, _CloudAltitudeBounds, _CloudHeroBounds;
        float _CloudSteps, _CloudFarSteps, _CloudStorm, _WeatherMapSpan, _WeatherFarSpan;
        float _CloudBase, _CloudHeightShift;
        float _CloudAirExtinction;
        // Lightning flash envelope, 0..1 (LightningDirector.FlashNow).
        float _CloudFlash;
        float4 _CloudFlashA, _CloudFlashB;
        // The weather tape (BakedTape, uploaded by TapeTexture): every per-sample low genus and thin layer column comes from it.
        // RGBAFloat, _TapeInfo.x texels by 11 rows, point sampled and lerped by hand along s (TapeRow). Rows 0-2 hold the low
        // genus, 3-6 the middle layer, 7-10 the high layer; SkyColumns.GpuRow / GpuChannel say which column sits where.
        // ShapeProfile / ShapeFootprint / BaseGate match CloudShape.cs. Change both together.
        sampler2D _TapeTex;
        float4 _TapeInfo;   // x = texel count, y = 1 / texel count, z = 1 / texel metres
        float4 _TapeFrame;  // xy = tape axis u (unit, toward upwind), z = meander amplitude (m), w = meander wavelength (m)
        float4 _TapeFrame2; // x = meander phase, y = D, the tape offset at the map origin (m), z = spacing of the cloud-street rows (m), w = their phase
        float4 _TapeChord;  // x,y = middle layer chord y0,y1 (m); z,w = high layer chord y0,y1
        float4 _TapeFlags;  // x = a middle layer exists on the tape, y = a high layer exists, z = the tape is one texel (read _TapeConstLow)
        float4 _TapeConstLow0, _TapeConstLow1, _TapeConstLow2; // rows 0-2 of a one-texel tape: live play reads these instead of the texture
        // The noise decorrelation of this tape (NoiseXform.cs): per noise layer cos, sin, offset x, offset z. Every tap of the shared noise
        // volume reads the position NoiseXZ gives it; none divides a raw world coordinate (a source scan and a grep prove it).
        float4 _NoiseXf0, _NoiseXf1, _NoiseXf2, _NoiseXf3, _NoiseXf4, _NoiseXf5;
        float2 _CloudWindDir;
        // Radians one screen pixel spans: sets the noise level of detail by footprint.
        float _CloudPixelAngle;
        // The horizon deck: an analytic cloud plane continuing the weather past the march.
        float _HorizonDeck, _HorizonDepth, _HorizonCover;
        // Set-piece storms: xy position, z heading (radians), w kind; B: size, top,
        // strength, extent. Static scenery the weather state builds up or lets decay.
        float4 _HeroA[5];
        float4 _HeroB[5];
        // Console fog bank over sea and valleys, 0..1.
        float _FogBank;
        float4 _CloudEye; // centre x/z, radius, faded strength
        float _HeroCount;

        // 0..1 how deep the camera is inside cloud: near-field density and wisps.
        // Reduced-resolution march: world view rays through the screen corners (rows:
        // bottom-left, bottom-right, top-left, top-right) and the target size (w, h, 1/w, 1/h).
        float4x4 _CloudFrustum;
        float4 _CloudLowResSize;
        // Temporal update: xy the texel of each 2x2 block marched this frame, z the
        // per-frame dither offset; _CloudCheckerOn turns the quarter-resolution march on.
        float4 _CloudChecker;
        float _CloudCheckerOn;

        // Pixel footprint of the current march sample in metres: noise is filtered to it,
        // and to the step length, so sparse samples never pick out single speckles.
        static float gFoot = 0.0;
        // Half the altitude a far march step spans: thin layers average over it instead of
        // being hit or missed by whole steps (the source of banding on distant decks).
        static float gVert = 0.0;
        // Set-pieces this ray passes near; the rest are never evaluated along it.
        static uint gHeroMask = 0u;
        static bool gHeroOnly = false;
        // Dominant density body's own normalized height. Capture after the primary sample;
        // subsequent shadow-density samples overwrite it. Empty samples always reset it.
        static float gLocalLightingHeight = 0.0;
        // Round 6: how much a lit sample belongs to a smooth deck (the low layer won the sample and its genus is a sheet), 0 elsewhere. Captured like the height.
        static float gDeckSheet = 0.0;
        // The low-genus row of the sample being shaded. A primary sample fetches its own; the sun taps of that march sample
        // (gLowReuse, set by MarchSky) reuse it: they lie within 1.5 km and a ramp is at least 12 km wide.
        static bool gLowValid = false;
        static bool gLowReuse = false;

        // Near march limit: beyond it the far map's level of detail takes over.
        // The weather map profiles: the cloud-base byte spans 0-4096 m (CloudMaps.BaseRange); the structure
        // tops 0-16000 m (CloudMaps.VolumeTop).
        #define CLOUD_BASE_RANGE 4096.0
        // Cloud streets (StreetMath.cs): the body field is stretched along the wind by 1 + STREET_STRETCH * Streets, and rows of
        // amplitude STREET_ROW_AMP * Streets run across it.
        #define STREET_STRETCH 3.0
        #define STREET_ROW_AMP 0.45
        #define CLOUD_NEAR_LIMIT 45000.0
        #define CLOUD_FAR_LIMIT 220000.0
        // Thin layers and the horizon deck reach this far along sky pixels; haze hides the rest.
        #define CLOUD_HORIZON_LIMIT 1200000.0
        #define CLOUD_HORIZON_FADE 450000.0

        // The near map covers the flight domain; the coarse far map continues the same
        // field to the horizon. They agree in their overlap, so a short band blends them.
        bool SampleWeather(float2 xz, out float4 weather, out float4 profile, out float farShare)
        {
            float2 nearUv = xz / _WeatherMapSpan + 0.5;
            float2 farUv = xz / _WeatherFarSpan + 0.5;
            float2 nearEdge = min(nearUv, 1.0 - nearUv);
            float2 farEdge = min(farUv, 1.0 - farUv);
            float nearWeight = smoothstep(0.0, 0.04, min(nearEdge.x, nearEdge.y));
            float farFade = smoothstep(0.0, 0.08, min(farEdge.x, farEdge.y));
            farShare = 1.0 - nearWeight;
            weather = 0.0;
            profile = 0.0;
            if (farFade <= 0.0) return false;
            if (nearWeight >= 1.0)
            {
                weather = tex2Dlod(_WeatherMapTex, float4(nearUv, 0, 0));
                profile = tex2Dlod(_WeatherProfileTex, float4(nearUv, 0, 0));
                return true;
            }
            float4 farWeather = tex2Dlod(_WeatherFarMapTex, float4(farUv, 0, 0));
            float4 farProfile = tex2Dlod(_WeatherFarProfileTex, float4(farUv, 0, 0));
            // Coverage fades toward the far edge; heights stay put so the fade thins
            // the cloud instead of lowering it into the ground.
            farWeather.rgb *= farFade;
            if (nearWeight <= 0.0) { weather = farWeather; profile = farProfile; return true; }
            weather = lerp(farWeather, tex2Dlod(_WeatherMapTex, float4(nearUv, 0, 0)), nearWeight);
            profile = lerp(farProfile, tex2Dlod(_WeatherProfileTex, float4(nearUv, 0, 0)), nearWeight);
            return true;
        }

        #define NOISE_LOW 0
        #define NOISE_MID 1
        #define NOISE_HIGH 2
        #define NOISE_HORIZON 3
        #define NOISE_MID_ALIGNED 4
        #define NOISE_HIGH_ALIGNED 5

        float4 NoiseXf(int layer)
        {
            if (layer == 0) return _NoiseXf0;
            if (layer == 1) return _NoiseXf1;
            if (layer == 2) return _NoiseXf2;
            if (layer == 3) return _NoiseXf3;
            if (layer == 4) return _NoiseXf4;
            return _NoiseXf5;
        }

        // Where the noise volume is read for a position: TapeFrame.NoiseXZ in BakedTape.cs is the twin. The aligned layers (streak taps)
        // carry no rotation, so a stretch along the wind stays along the wind.
        float2 NoiseXZ(float2 xz, int layer)
        {
            float4 t = NoiseXf(layer);
            return float2(t.x * xz.x - t.y * xz.y + t.z, t.y * xz.x + t.x * xz.y + t.w);
        }

        // A world position as the point the low volume's noise is read at: (NoiseXZ(x, z), y).
        float3 NoiseG(float3 g, int layer)
        {
            float2 n = NoiseXZ(g.xz, layer);
            return float3(n.x, g.y, n.y);
        }

        // Smooth two-channel value noise from an integer hash (SmoothNoise.cs is the twin; change both together). The look pass's warps need a bounded
        // slope, which the noise volume's fine octaves cannot give (they folded the warped coordinate over a third of the plane).
        #define SALT_BEND 0x1B3Du
        #define SALT_CURL 0x2C57u
        #define SALT_MEANDER 0x3E91u
        #define SALT_LOBE_A 0x4A05u
        #define SALT_LOBE_B 0x5D73u
        #define SALT_CELL_A 0x6A11u
        #define SALT_CELL_B 0x6A22u
        #define SALT_CELL_C 0x6A33u
        #define SALT_CELL_S 0x6A44u
        #define SALT_STREAK_WIDTH 0x7A10u
        #define SALT_STREAK_A 0x7A21u
        #define SALT_STREAK_B 0x7A32u
        #define SALT_STREAK_SEG 0x7A43u
        #define SALT_ROWS 0x8B15u
        #define SALT_MESO 0x9D41u
        #define SALT_DECK_A 0x9C21u
        #define SALT_DECK_B 0x9C32u

        uint SmoothHash(int x, int z, uint salt)
        {
            uint h = (uint)x * 0x27D4EB2Fu + (uint)z * 0x165667B1u + salt * 0x9E3779B1u;
            h ^= h >> 15; h *= 0x85EBCA6Bu;
            h ^= h >> 13; h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return h;
        }

        float2 SmoothNoise2(float2 uv, uint salt)
        {
            float2 f = floor(uv);
            int2 i = (int2)f;
            float2 t = uv - f;
            t = t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
            uint h00 = SmoothHash(i.x, i.y, salt), h10 = SmoothHash(i.x + 1, i.y, salt);
            uint h01 = SmoothHash(i.x, i.y + 1, salt), h11 = SmoothHash(i.x + 1, i.y + 1, salt);
            const float k = 1.0 / 65535.0;
            float2 v00 = float2(h00 & 0xFFFFu, h00 >> 16) * k, v10 = float2(h10 & 0xFFFFu, h10 >> 16) * k;
            float2 v01 = float2(h01 & 0xFFFFu, h01 >> 16) * k, v11 = float2(h11 & 0xFFFFu, h11 >> 16) * k;
            return lerp(lerp(v00, v10, t.x), lerp(v01, v11, t.x), t.y);
        }

        // Cloud streets (StreetMath.Compress, Rows and Body are the twins). The share of the along-wind coordinate removed before the
        // noise is read, the 0..1 rows across the wind (the broad noise bends them) and the body value with the rows added.
        float StreetCompress(float streets)
        {
            float k = STREET_STRETCH * streets;
            return k / (1.0 + k);
        }

        float StreetRows(float across, float wobble)
        {
            return 0.5 + 0.5 * sin(6.2831853 * across / _TapeFrame2.z + _TapeFrame2.w + wobble);
        }

        float StreetBody(float body, float streets, float rows)
        {
            return saturate(body + streets * STREET_ROW_AMP * (rows - 0.5));
        }

        float LodFor(float foot, float period);
        float LadderMix(float raw0, float raw1, float w, float mean);

        // Individual cloud bodies: cellular noise at non-integer scales (so the 64^3
        // tile never visibly repeats), in [0, 1]. Coverage thresholds it into separate
        // clouds with sky between them, the way broken and scattered decks really look.
        // The raw weighted sum of the three lobe taps at one lobe size (mean about 0.5), before the remap.
        // Lobe dropout and amplitude variance from the broad tap's lobe (B) and erosion (G) channels (SlabMath.LobeKeep is the twin): a lobe-channel value under 0.38
        // removes the lobe (0.40 stays inside the hole), over 0.56 keeps it; the erosion channel scales the rest by 0.5-1.4; 1.475 restores the mean.
        float LobeKeep(float lobe, float erosion)
        {
            return lerp(0.40, 1.0, smoothstep(0.38, 0.56, lobe)) * lerp(0.5, 1.4, smoothstep(0.3, 0.7, erosion)) * 1.475;
        }

        float BodiesRaw(float3 ng, float scale, float lod)
        {
            float period = scale * 4.0;
            float a = tex3Dlod(_CloudNoiseTex, float4(ng.xz / period, ng.y / (period * 0.9), lod)).b;
            float4 bt = tex3Dlod(_CloudNoiseTex, float4(float2(ng.z, -ng.x) / (period * 2.73) + 0.37,
                ng.y / (period * 2.1), lod));
            float b = bt.r;
            // Lobe size varies inside a group (SlabMath.LobeSmall): where the slow field b is high a second, smaller lobe tap (0.6, turned 37 degrees) takes over.
            float fineMix = smoothstep(0.46, 0.60, b);
            if (fineMix > 0.0)
            {
                float2 nrp = float2(ng.x * 0.8 + ng.z * 0.6, ng.z * 0.8 - ng.x * 0.6);
                float pf = period * 0.6;
                float a2 = tex3Dlod(_CloudNoiseTex, float4(nrp / pf + float2(0.29, 0.17), ng.y / (period * 0.9), max(lod, LodFor(gFoot, pf)))).b;
                a = LadderMix(a, a2, fineMix, 0.5);
            }
            a *= LobeKeep(bt.b, bt.g);
            float c = tex3Dlod(_CloudNoiseTex, float4(ng.xz / (period * 0.29) + float2(0.17, 0.61),
                ng.y / (period * 0.24), max(lod, LodFor(gFoot, period * 0.29)))).b;
            return a * 0.65 + b * 0.15 + c * 0.20;
        }

        float BodiesRemap(float raw)
        {
            return saturate((raw - 0.22) * 2.5);
        }

        float Bodies(float3 ng, float scale, float lod)
        {
            return BodiesRemap(BodiesRaw(ng, scale, lod));
        }

        // The feature-size ladders (Ladder.cs): sqrt(2) steps, 700 m lobes and 250 m periods.
        float LobeMetres(float rung) { return 700.0 * exp2(0.5 * rung); }
        float PeriodMetres(float rung) { return 250.0 * exp2(0.5 * rung); }
        // The edge texture follows the lobe: clamp(0.9 * lobe, 600, 2500) / 1250 (Ladder.EdgeScale).
        float EdgeScale(float lobeMetres) { return clamp(0.9 * lobeMetres, 600.0, 2500.0) / 1250.0; }

        // A fractional rung index to (lower rung, weight of the next one): w = smoothstep(0.35, 0.65, frac).
        float2 LadderSplit(float index)
        {
            float r0 = floor(index);
            return float2(r0, smoothstep(0.35, 0.65, index - r0));
        }

        // Two raw values mixed at w with the contrast kept (Ladder.Mix). Outside 0.02 < w < 0.98 one rung decides.
        float LadderMix(float raw0, float raw1, float w, float mean)
        {
            if (w <= 0.02) return raw0;
            if (w >= 0.98) return raw1;
            return mean + (lerp(raw0, raw1, w) - mean) * rsqrt((1.0 - w) * (1.0 - w) + w * w);
        }

        // Bodies at a lobe rung index: one call in steady state, two inside a ramp (CloudBodies.BodiesLadder is the twin).
        // Noise is never sampled at a scale that varies in space or time: the scale is the rung's lobe.
        float BodiesLadder(float3 ng, float rungIndex, float lod)
        {
            float2 sp = LadderSplit(rungIndex);
            float raw0 = 0.0, raw1 = 0.0;
            if (sp.y < 0.98) raw0 = BodiesRaw(ng, LobeMetres(sp.x), lod);
            if (sp.y > 0.02) raw1 = BodiesRaw(ng, LobeMetres(sp.x + 1.0), lod);
            return BodiesRemap(LadderMix(raw0, raw1, sp.y, 0.5));
        }

        // Coverage remap: `cover` of the sky becomes cloud, the rest stays clear. Near full
        // cover the deck closes but keeps its lumps.
        float CoverMask(float body, float cover)
        {
            float open = saturate((body - (1.0 - cover)) / max(0.18, cover));
            return lerp(open, max(open, 0.55 + body * 0.45), smoothstep(0.82, 1.0, cover));
        }

        // Mip level for a noise feature of `period` metres (one 64-texel tile) seen with a
        // pixel footprint of `foot` metres: filters instead of aliasing into speckle.
        float LodFor(float foot, float period)
        {
            return clamp(log2(max(1.0, foot * 64.0 / period)), 0.0, 5.0);
        }

        // Fraction of the current far step's altitude window [y - gVert, y + gVert] that lies
        // inside [y0, y1]: the vertical average of a thin layer along a long step.
        float LayerOverlap(float y, float y0, float y1)
        {
            return max(0.0, min(y + gVert, y1) - max(y - gVert, y0)) / (2.0 * gVert);
        }

        // ---- the weather tape ---------------------------------------------------------------------
        // Position along the tape of a map point: TapeFrame.S in BakedTape.cs is the twin.
        float TapeS(float2 xz)
        {
            if (_TapeFrame.z == 0.0) return dot(xz, _TapeFrame.xy) + _TapeFrame2.y; // no meander: skip the sines
            float across = -xz.x * _TapeFrame.y + xz.y * _TapeFrame.x;
            float f = 6.2831853 * across / _TapeFrame.w + _TapeFrame2.x;
            float meander = _TapeFrame.z * (sin(f) + 0.35 * sin(2.1 * f)) / 1.35;
            return dot(xz, _TapeFrame.xy) + _TapeFrame2.y + meander;
        }

        // The one accessor to the tape texture: row `row` at position s, lerped by hand between the two nearest texels
        // (TapeSampler.At on the CPU reads the same numbers). s clamps to the ends.
        float4 TapeRow(float row, float s)
        {
            float u = s * _TapeInfo.z - 0.5;
            float fl = floor(u);
            float last = _TapeInfo.x - 1.0;
            float i0 = clamp(fl, 0.0, last);
            float i1 = clamp(fl + 1.0, 0.0, last);
            float v = (row + 0.5) / 11.0;
            float4 a = tex2Dlod(_TapeTex, float4((i0 + 0.5) * _TapeInfo.y, v, 0, 0));
            if (i0 == i1) return a;
            float4 b = tex2Dlod(_TapeTex, float4((i1 + 0.5) * _TapeInfo.y, v, 0, 0));
            return lerp(a, b, u - fl);
        }

        struct LowRow
        {
            float lobeRung, puffDepth, baseSharp, baseWobble;
            float dome, billow, anvil, deckDepth;
            float deckSmooth, streets, horizonCover, horizonDepth;
        };

        // Rows 0-2: the low genus at position s. Smoothness fades lobes into a sheet; shape formulas match CloudShape.cs.
        LowRow TapeLow(float s)
        {
            float4 r0, r1, r2;
            if (_TapeFlags.z > 0.5) { r0 = _TapeConstLow0; r1 = _TapeConstLow1; r2 = _TapeConstLow2; }
            else { r0 = TapeRow(0.0, s); r1 = TapeRow(1.0, s); r2 = TapeRow(2.0, s); }
            LowRow o;
            o.lobeRung = r0.x; o.puffDepth = r0.y; o.baseSharp = r0.z; o.baseWobble = r0.w;
            o.dome = r1.x; o.billow = r1.y; o.anvil = r1.z; o.deckDepth = r1.w;
            o.deckSmooth = r2.x; o.streets = r2.y; o.horizonCover = r2.z; o.horizonDepth = r2.w;
            return o;
        }

        static LowRow gLow;

        struct LayerRow
        {
            float baseY, thick, cover, periodRung;
            float softness, detail, strength, cells;
            float ripples, streaks, veil, curl;
            float aspect, rippleWave;
        };

        // Rows 3-6 (layer 0, the middle layer) or 7-10 (layer 1, the high layer) at position s.
        LayerRow TapeLayer(float s, float layer)
        {
            float first = layer < 0.5 ? 3.0 : 7.0;
            float4 r0 = TapeRow(first, s), r1 = TapeRow(first + 1.0, s), r2 = TapeRow(first + 2.0, s), r3 = TapeRow(first + 3.0, s);
            LayerRow o;
            o.baseY = r0.x; o.thick = r0.y; o.cover = r0.z; o.periodRung = r0.w;
            o.softness = r1.x; o.detail = r1.y; o.strength = r1.z; o.cells = r1.w;
            o.ripples = r2.x; o.streaks = r2.y; o.veil = r2.z; o.curl = r2.w;
            o.aspect = r3.x; o.rippleWave = r3.y;
            return o;
        }

        // The console fog bank as a constant layer (-30..330 m): a veil with a little streaking.
        LayerRow FogRow()
        {
            LayerRow o;
            o.baseY = -30.0; o.thick = 360.0; o.cover = _FogBank; o.periodRung = 7.0;
            o.softness = 0.5; o.detail = 0.7; o.strength = 0.55; o.cells = 0.0;
            o.ripples = 0.0; o.streaks = 0.15; o.veil = 0.85; o.curl = 0.0;
            o.aspect = 1.6; o.rippleWave = 0.0;
            return o;
        }


        // Cauliflower erosion for the set-pieces: two octaves of the base channel at a
        // non-integer scale ratio and rotated against each other, so a wall tens of km wide
        // never shows the 64^3 tile as a regular grid of lumps (bubble wrap).
        float Billow(float3 g, float lod)
        {
            float3 ng = NoiseG(g, NOISE_LOW);
            float a = tex3Dlod(_CloudNoiseTex, float4(ng.xz / 12000.0 + 0.43, ng.y / 10000.0 + 0.11, lod)).b;
            float b = tex3Dlod(_CloudNoiseTex, float4(float2(ng.z, -ng.x) / 4200.0 + 0.71, ng.y / 3600.0 + 0.57, lod)).b;
            return saturate((a * 0.65 + b * 0.35 - 0.22) * 2.5);
        }

        // Turns a smooth set-piece envelope into a lumpy cumuliform surface.
        float Erode(float shape, float3 g, float lod)
        {
            return saturate((shape * 1.3 - (1.0 - Billow(g, lod)) * 0.55) / 0.75);
        }

        float HeroNoise(float3 g, float scale, float lod)
        {
            float3 ng = NoiseG(g, NOISE_LOW);
            return tex3Dlod(_CloudNoiseTex, float4(ng.xz / scale + 0.17, ng.y / (scale * 0.8) + 0.23, lod)).r;
        }

        // Bounding circle of a set-piece on the ground (xy centre, z radius). Mirrored by
        // each shape's early-out, so a ray culled here never misses cloud.
        float3 HeroReach(float4 a, float4 b)
        {
            float2 dir = float2(cos(a.z), sin(a.z));
            if (a.w < 0.5)
            {
                // Squall line: u along the line, v ahead of it (from -extent-7 km to +10 km).
                float2 ahead = float2(dir.y, -dir.x);
                float lengthHalf = b.x + 9000.0, depthHalf = (b.w + 17000.0) * 0.5;
                return float3(a.xy + ahead * (10000.0 - depthHalf), sqrt(lengthHalf * lengthHalf + depthHalf * depthHalf));
            }
            if (a.w < 1.5) return float3(a.xy, max(b.w * 2.8, b.x * 1.4 + 4000.0) + 1000.0);
            if (a.w < 2.5) return float3(a.xy, b.x + b.w * 4.0);
            float lengthHalf = b.w + 1.5 * b.x;
            return float3(a.xy + dir * b.w, sqrt(lengthHalf * lengthHalf + b.x * b.x));
        }

        // A squall line: a dark storm mass behind the line and a low shelf wedge ahead of
        // it, its leading face cut into horizontal tiers (the arcus in storm photographs).
        float ShelfLine(float3 g, float4 a, float4 b, float lod)
        {
            float2 along = float2(cos(a.z), sin(a.z));
            float2 ahead = float2(along.y, -along.x);
            float2 d = g.xz - a.xy;
            float u = dot(d, along), v = dot(d, ahead);
            const float shelfReach = 7000.0;
            if (abs(u) > b.x + 9000.0 || v < -b.w - 7000.0 || v > shelfReach + 3000.0 || g.y > b.y + 1500.0) return 0.0;
            float n = HeroNoise(g, 26000.0, lod);
            float n2 = HeroNoise(g + 911.0, 6500.0, lod);
            float taper = 1.0 - smoothstep(b.x * 0.55, b.x + 7000.0 * n, abs(u));
            // Lumpy, rounded crowns: the top rolls off over a kilometre instead of a flat cut.
            float stormTop = b.y * (0.72 + 0.28 * n);
            float mass = (1.0 - smoothstep(stormTop - 1200.0, stormTop + 300.0, g.y)) * smoothstep(1250.0, 1600.0, g.y) *
                smoothstep(-b.w - 5000.0 * n, -b.w * 0.55, v) * (1.0 - smoothstep(-700.0, 500.0, v)) *
                smoothstep(0.28, 0.55, n * 0.6 + n2 * 0.4 + 0.2);
            float f = saturate(v / shelfReach);
            float bottom = 450.0 + 220.0 * f + (n2 - 0.5) * 140.0;
            float top = bottom + 200.0 + pow(1.0 - f, 0.8) * 3200.0;
            float reach = shelfReach * (0.8 + 0.35 * n);
            float inShelf = smoothstep(-400.0, 0.0, v) * (1.0 - smoothstep(reach - 600.0, reach, v)) *
                smoothstep(bottom - 40.0, bottom + 90.0, g.y) * (1.0 - smoothstep(top - 350.0, top, g.y));
            float tiers = 0.88 + 0.12 * sin((g.y + (n - 0.5) * 1400.0 + v * 0.08) * 0.008);
            float shelf = inShelf * lerp(1.0, tiers, smoothstep(0.25, 0.9, f)) *
                smoothstep(0.3, 0.52, n2 * 0.5 + n * 0.5 + 0.08);
            gLocalLightingHeight = shelf * 0.72 > mass * 0.85
                ? saturate((g.y - bottom) / max(1.0, top - bottom))
                : saturate((g.y - 1250.0) / max(1.0, stormTop + 300.0 - 1250.0));
            return Erode(max(mass * 0.85, shelf * 0.72), g, lod) * taper * b.z;
        }

        // A supercell: a tilted, lumpy tower and a flat anvil blown downwind, with mammatus
        // pouches hanging from its underside.
        float Supercell(float3 g, float4 a, float4 b, float lod)
        {
            float2 dir = float2(cos(a.z), sin(a.z));
            float2 d = g.xz - a.xy;
            // The anvil reaches 2.5 extents downwind: the bound must enclose it, or the anvil
            // ends in a flat vertical wall.
            float reach = max(b.w * 2.8, b.x * 1.4 + 4000.0) + 1000.0;
            if (dot(d, d) > reach * reach || g.y > b.y + 1500.0) return 0.0;
            float n = HeroNoise(g, 22000.0, lod);
            float n2 = HeroNoise(g + 1733.0, 6500.0, lod);
            // Turrets: lobes of their own size bulge out of the column at every height.
            float bulge = HeroNoise(g + 517.0, 10000.0, lod);
            float height = saturate(g.y / b.y);
            float2 lean = dir * height * 1800.0;
            // A column that domes over at the top instead of ending in a flat cut.
            float dome = sqrt(saturate(1.0 - pow(max(0.0, g.y / b.y - 0.72) / 0.36, 2.0)));
            float radius = b.x * (0.52 + 0.3 * n + 0.3 * bulge + 0.65 * smoothstep(0.35, 0.82, height)) * dome;
            float tower = (1.0 - smoothstep(radius * 0.7, radius * 1.1, length(d - lean))) *
                smoothstep(1100.0, 1450.0, g.y);
            float along = dot(d, dir) - b.w * 0.35, across = dot(d, float2(-dir.y, dir.x));
            float e = length(float2(along * 0.55, across)) / b.w;
            float pouch = pow(saturate(n2 * 1.6 - 0.45), 2.0);
            // The anvil: thick where it leaves the tower, thin and torn at its downwind edge,
            // its top gently sloping; a clean ellipse seen edge-on reads as a saucer.
            float edge = 0.7 + 0.45 * (n - 0.5) + 0.2 * (bulge - 0.5);
            float outline = 1.0 - smoothstep(edge - 0.3, edge + 0.08, e);
            float anvilTop = b.y + 150.0 - 1200.0 * e * e + (n2 - 0.5) * 350.0;
            float anvilBase = anvilTop - lerp(6000.0, 500.0, smoothstep(0.05, 0.75, e)) - 700.0 * pouch;
            float anvil = outline * smoothstep(anvilBase - 50.0, anvilBase + 260.0, g.y) *
                (1.0 - smoothstep(anvilTop - 350.0, anvilTop + 120.0, g.y));
            // The anvil is a thin, fibrous ice sheet; the tower a lumpy heap.
            float towerDensity = Erode(tower * 0.9, g, lod);
            float anvilDensity = anvil * 0.34 * (0.55 + 0.45 * n2);
            gLocalLightingHeight = anvilDensity > towerDensity
                ? saturate((g.y - anvilBase) / max(1.0, anvilTop - anvilBase))
                : saturate((g.y - 1100.0) / max(1.0, b.y * 1.08 - 1100.0));
            return max(towerDensity, anvilDensity) * b.z;
        }

        // A hurricane eyewall: a clear eye whose wall leans outward with height (the
        // stadium effect), spiral rain bands wrapping outside it, and low cloud on the floor.
        float StormEye(float3 g, float4 a, float4 b, float lod)
        {
            float2 d = g.xz - a.xy;
            float r = length(d);
            float eye = b.x, wallWidth = b.w, top = b.y;
            if (r > eye + wallWidth * 4.0 || g.y > top + 1000.0) return 0.0;
            float n = HeroNoise(g, 7000.0, lod);
            float height = saturate(g.y / top);
            float inner = eye * (1.0 + 0.9 * height * height);
            float crown = top * (0.82 + 0.18 * n);
            float wall = smoothstep(inner - 1500.0, inner + 1500.0 + 2500.0 * n, r) *
                (1.0 - smoothstep(eye + wallWidth * 0.8, eye + wallWidth * 1.6 + 3000.0 * n, r)) *
                smoothstep(300.0, 900.0, g.y) * (1.0 - smoothstep(crown - 1500.0, crown, g.y));
            float angle = atan2(d.y, d.x);
            float spiral = 0.5 + 0.5 * sin(angle * 2.0 - log(max(r, 1.0)) * 9.0 + n * 3.0);
            float bands = smoothstep(0.55, 0.85, spiral) * smoothstep(eye + wallWidth, eye + wallWidth * 1.4, r) *
                (1.0 - smoothstep(eye + wallWidth * 2.5, eye + wallWidth * 4.0, r)) *
                smoothstep(400.0, 900.0, g.y) * (1.0 - smoothstep(5000.0, 7500.0 + 2000.0 * n, g.y));
            float floorCloud = (1.0 - smoothstep(inner * 0.7, inner, r)) * smoothstep(500.0, 700.0, g.y) *
                (1.0 - smoothstep(1300.0, 1800.0, g.y)) * smoothstep(0.55, 0.7, HeroNoise(g + 300.0, 2500.0, lod));
            float stormDensity = Erode(max(wall, bands * 0.8), g, lod);
            gLocalLightingHeight = floorCloud * 0.6 > stormDensity
                ? saturate((g.y - 500.0) / 1300.0)
                : wall >= bands * 0.8 ? saturate((g.y - 300.0) / max(1.0, crown - 300.0))
                : saturate((g.y - 400.0) / max(1.0, 7100.0 + 2000.0 * n));
            return max(stormDensity, floorCloud * 0.6) * b.z;
        }

        // Lenticular (mountain-wave) clouds: smooth stacked lenses in a downwind train.
        // Unlike everything else they are not eroded: real lenticulars are glassy smooth.
        float Lenticulars(float3 g, float4 a, float4 b)
        {
            float2 dir = float2(cos(a.z), sin(a.z));
            float2 d = g.xz - a.xy;
            float along = dot(d, dir), across = dot(d, float2(-dir.y, dir.x));
            float radius = b.x, spacing = b.w;
            if (abs(across) > radius || along < -radius * 1.5 || along > spacing * 2.0 + radius * 1.5 ||
                g.y < b.y - 400.0 || g.y > b.y + 1800.0) return 0.0;
            float best = 0.0;
            [loop]
            for (int k = 0; k < 3; k++)
            {
                float scale = 1.0 - 0.22 * k;
                float ca = along - spacing * k;
                [loop]
                for (int j = 0; j < 3; j++)
                {
                    float ra = radius * scale * (1.0 - 0.18 * j);
                    float lens = (ca / ra) * (ca / ra) + (across / (ra * 0.45)) * (across / (ra * 0.45)) +
                        ((g.y - b.y - j * 560.0 * scale) / (170.0 * scale)) * ((g.y - b.y - j * 560.0 * scale) / (170.0 * scale));
                    if (1.0 - lens > best)
                    {
                        best = 1.0 - lens;
                        gLocalLightingHeight = saturate((g.y - b.y - j * 560.0 * scale + 170.0 * scale) / (340.0 * scale));
                    }
                }
            }
            return smoothstep(0.0, 0.3, best) * 0.7 * b.z;
        }

        float Heroes(float3 g, float lod)
        {
            float hero = 0.0;
            float heroHeight = 0.0;
            [loop]
            for (int k = 0; k < 5; k++)
            {
                if ((gHeroMask & (1u << k)) == 0u) continue;
                float kind = _HeroA[k].w;
                float h = kind < 0.5 ? ShelfLine(g, _HeroA[k], _HeroB[k], lod)
                    : kind < 1.5 ? Supercell(g, _HeroA[k], _HeroB[k], lod)
                    : kind < 2.5 ? StormEye(g, _HeroA[k], _HeroB[k], lod)
                    : Lenticulars(g, _HeroA[k], _HeroB[k]);
                if (h > hero) { hero = h; heroHeight = gLocalLightingHeight; }
            }
            gLocalLightingHeight = heroHeight;
            return hero;
        }

        // True where no cloud can be drawn: no cover within ~2.5 km, or above the highest top
        // or below the lowest base there (CloudMaps.Envelope). One fetch instead of a density.
        bool EmptyAt(float3 g)
        {
            float2 uv = g.xz / _WeatherMapSpan + 0.5;
            // The outer band blends in the far map, which the envelope does not cover.
            if (_WeatherEnvelopeOn < 0.5 || min(uv.x, uv.y) < 0.05 || max(uv.x, uv.y) > 0.95) return false;
            float4 e = tex2Dlod(_WeatherEnvelopeTex, float4(uv, 0, 0));
            if (e.r <= 0.0) return true;
            return g.y > e.g * 16000.0 + _CloudHeightShift + 800.0 || g.y < e.b * 16000.0 + _CloudHeightShift - 800.0;
        }

        // True near a set-piece this ray passes (the envelope does not know about them).
        bool NearHero(float3 g)
        {
            if (gHeroMask == 0u) return false;
            [loop]
            for (int k = 0; k < 5; k++)
            {
                if ((gHeroMask & (1u << k)) == 0u) continue;
                float3 reach = HeroReach(_HeroA[k], _HeroB[k]);
                float2 d = g.xz - reach.xy;
                if (dot(d, d) < reach.z * reach.z && g.y < _HeroB[k].y + 1600.0) return true;
            }
            return false;
        }

        // CloudShape.Profile. h is 0 at the base and 1 at the top.
        float ShapeProfile(float h, float dome, float anvil)
        {
            if (h <= 0.0 || h >= 1.0) return 0.0;
            float round = smoothstep(0.18, 1.0, h);
            float body = 1.0 - round * saturate(dome);
            float pinch = max(0.0, dome - 1.0) * smoothstep(0.15, 0.55, h);
            float cap = smoothstep(0.64, 0.80, h);
            return saturate(body - pinch + anvil * cap) * (1.0 - smoothstep(0.90, 1.0, h));
        }

        // CloudShape.Mass: round the boundary without turning the whole cloud into mist.
        float ShapeMass(float mask, float profile)
        {
            return saturate((mask - (1.0 - profile) * 0.8) * 2.6) * smoothstep(0.0, 0.18, profile);
        }

        // CloudShape.LocalAnvil / LocalDome. Shallow ordinary clouds remain rounded
        // under a deep independent shield; no extra weather-map or noise reads.
        float LocalAnvil(float depth, float anvil)
        {
            return saturate(anvil) * smoothstep(2400.0, 6500.0, depth);
        }

        float LocalDome(float dome, float anvil)
        {
            return lerp(min(1.0, dome), dome, smoothstep(0.18, 0.60, anvil));
        }

        // CloudShape.Footprint. The anvil widens the top of a tower.
        float ShapeFootprint(float h, float anvil)
        {
            float cap = smoothstep(0.58, 0.86, saturate(h));
            return 1.0 + anvil * cap * 1.7;
        }

        // CloudShape.BaseGate. A small sharp value is a flat cumulus base.
        float BaseGate(float y, float baseY, float sharp)
        {
            float s = max(20.0, sharp);
            return smoothstep(baseY - s * 0.2, baseY + s, y);
        }

        float EyeCloudKeep(float3 g)
        {
            if (_CloudEye.z <= 0.0 || _CloudEye.w <= 0.0) return 1.0;
            float r = length(g.xz - _CloudEye.xy);
            return 1.0 - _CloudEye.w * (1.0 - smoothstep(_CloudEye.z * 0.65, _CloudEye.z * 0.98, r)) *
                smoothstep(1500.0, 2300.0, g.y);
        }

        // Boundary erosion at edge scale k (1 = the 1250 m erosion and 340 / 300 m micro periods). CloudBodies.EdgeDetail is the twin.
        float EdgeDetail(float3 ng, float coarse, float k, float edge, float sheetBlend)
        {
            float lod = max(coarse, LodFor(gFoot, 1250.0 * k));
            float3 n2 = float3((ng.xz - _CloudWindOffset * 1.7) / (1250.0 * k), ng.y / (1250.0 * k));
            float d1 = tex3Dlod(_CloudNoiseTex, float4(n2, lod)).r;
            float nearDetail = saturate(1.0 - lod);
            // Cumulus edges take the rounded Worley lobes (B) for the near octave, so
            // boundaries read as cauliflower billows; sheets keep the stringy erosion (G).
            float2 d2n = nearDetail > 0.0
                ? tex3Dlod(_CloudNoiseTex, float4(n2 * 3.1 + 0.21, lod)).gb : 0.5;
            float d2 = lerp(d2n.y, d2n.x, sheetBlend);
            float detail = lerp(d1, d1 * 0.72 + d2 * 0.28, nearDetail);
            float close = saturate(1.0 - gFoot / 3.0);
            if (close > 0.0 && edge > 0.35)
            {
                float micro = tex3Dlod(_CloudNoiseTex, float4(ng.xz / (340.0 * k) + 0.19, ng.y / (300.0 * k) + 0.83, 0.0)).g;
                detail = lerp(detail, detail * 0.65 + micro * 0.35, close);
            }
            return detail;
        }

        float Density(float3 world, float coarse)
        {
            gLocalLightingHeight = 0.0;
            gDeckSheet = 0.0;
            // A primary sample fetches its own genus row; the sun taps of one march sample reuse it.
            if (!gLowReuse) gLowValid = false;
            float3 g = world + _CloudWorldOffset;
            // The low volume's noise is read where this tape's rotation and offset put it.
            float3 ng = NoiseG(g, NOISE_LOW);
            // Filter the 1250 m detail to what one pixel and one step can resolve.
            float lod = max(coarse, LodFor(gFoot, 1250.0));
            float bodyLod = max(coarse, LodFor(gFoot, 24000.0));
            float hero = gHeroMask != 0u ? Heroes(g, max(lod, 1.0)) : 0.0;
            float heroHeight = gLocalLightingHeight;
            if (gHeroOnly) return hero;
            if (g.y < _CloudAltitudeBounds.x || g.y > _CloudAltitudeBounds.y) return hero;
            float3 n1 = float3((ng.xz - _CloudWindOffset) / 24000.0, 0.37);
            float2 broad = tex3Dlod(_CloudNoiseTex, float4(n1, bodyLod)).rg;
            // Warp the meteorological boundary at cloud scale; otherwise a smooth
            // front map reads as a vertical polygon even when its rain band is curved.
            float2 warped = g.xz + (broad.rg - 0.5) * 2800.0;
            float4 weather, profile;
            float farShare;
            if (!SampleWeather(warped, weather, profile, farShare)) return hero;
            float layer = weather.r, cell = weather.b;
            if (max(layer, cell) < 0.025) return hero;
            // The low genus of this sample, read from the tape at the warped position: one s serves cover and genus (spec 7.3).
            if (!gLowValid) { gLow = TapeLow(_TapeFlags.z > 0.5 ? 0.0 : TapeS(warped)); gLowValid = true; }
            LowRow row = gLow;
            float2 lobe = LadderSplit(row.lobeRung);
            float puffLod = max(coarse, LodFor(gFoot, max(1200.0, LobeMetres(lobe.x) * 4.0)));
            // The far map cannot resolve erosion detail; keep its envelope smooth.
            coarse = max(coarse, farShare);
            float baseY = profile.b * CLOUD_BASE_RANGE + _CloudHeightShift;
            float topY = max(baseY + 280.0, weather.a * 16000.0 + _CloudHeightShift);
            float towerBlend = smoothstep(0.18, 0.60, row.anvil);
            float capTop = baseY + max(280.0, max(row.puffDepth, row.deckDepth)) * 1.15;
            float columnTop = max(baseY + 280.0, lerp(min(topY, capTop), topY, towerBlend));
            float deckTop = baseY + max(row.puffDepth, row.deckDepth) + row.baseWobble * 0.5;
            float lowFloor = baseY;
            if (g.y < lowFloor - 700.0 - gVert || g.y > max(deckTop, columnTop) + 600.0 + gVert) return hero;

            float smooth = saturate(row.deckSmooth);
            float sheetBlend = smoothstep(0.45, 0.85, smooth);
            bool sheet = sheetBlend >= 0.999;
            float3 gw = g + float3((broad.r - 0.5) * 1400.0, 0.0, (broad.g - 0.5) * 1400.0);
            // Cloud streets: where the row asks, the body field is stretched along the wind and given rows across it.
            float streets = saturate(row.streets);
            float3 gs = gw;
            if (streets > 0.0) gs.xz -= _TapeFrame.xy * (dot(gw.xz, _TapeFrame.xy) * StreetCompress(streets));
            float3 ngw = NoiseG(gs, NOISE_LOW);
            float puffBody = 0.72;
            if (!sheet)
            {
                // Warp before the lobe lookup (SlabMath.LobeWarp; CloudBodies.WarpLobes is the twin): two octaves of smooth noise, fades out with streets.
                float2 lobeWarp = (SmoothNoise2(ngw.xz / 3500.0 + float2(0.23, 0.41), SALT_LOBE_A) - 0.5) +
                    (SmoothNoise2(ngw.xz / 1500.0 + float2(0.61, 0.17), SALT_LOBE_B) - 0.5) * 0.3;
                ngw.xz += lobeWarp * (min(LobeMetres(row.lobeRung) * 1.6, 2200.0) * (1.0 - streets));
                float bodies = BodiesLadder(ngw, row.lobeRung, puffLod);
                if (streets > 0.0)
                    bodies = StreetBody(bodies, streets, StreetRows(dot(gw.xz, float2(-_TapeFrame.y, _TapeFrame.x)), (broad.g - 0.5) * 4.0));
                puffBody = lerp(bodies, 0.72, sheetBlend);
            }
            float relief = lerp(0.55, 1.0, smoothstep(0.2, 0.85, puffBody));
            float localAnvil = LocalAnvil(columnTop - baseY, row.anvil);
            float localTowerBlend = smoothstep(0.18, 0.60, localAnvil);
            columnTop = lerp(baseY + (columnTop - baseY) * relief, columnTop, max(sheetBlend, localTowerBlend));

            // Low deck. Each puff has its own base and a domed top; a smooth genus is one sheet.
            float layerShape = 0.0;
            float layerHeight = 0.0;
            if (layer > 0.02)
            {
                float deckBody = lerp(puffBody, 0.72 + 0.28 * puffBody, smooth);
                float mask = CoverMask(deckBody, saturate(layer * 1.2));
                float thick = max(280.0, lerp(row.puffDepth, max(row.puffDepth, row.deckDepth), sheetBlend));
                thick *= lerp(0.75 + 0.10 * sheetBlend, 1.0, broad.r);
                thick *= lerp(relief, 1.0, sheetBlend);
                float layerBase = baseY + (puffBody - 0.5) * lerp(row.baseWobble, row.baseWobble * 0.3, smooth);
                float layerTop = layerBase + thick;
                layerHeight = saturate((g.y - layerBase) / max(1.0, thick));
                if (gVert > 40.0)
                    layerShape = saturate(mask) * LayerOverlap(g.y, layerBase, layerTop) * lerp(0.7, 0.9, sheetBlend);
                else
                {
                    float hl = (g.y - layerBase) / max(1.0, thick);
                    float deckAnvil = LocalAnvil(thick, row.anvil) * (1.0 - smooth);
                    layerShape = ShapeMass(mask, ShapeProfile(hl, LocalDome(lerp(row.dome, row.dome * 0.25, smooth), deckAnvil), deckAnvil)) *
                        BaseGate(g.y, layerBase, lerp(row.baseSharp, row.baseSharp * 2.2, smooth));
                }
            }


            // Towers follow the cell footprint: narrow with height, flare where the genus has an anvil.
            // Cumulus is carved into puffs. A cumulonimbus stays one mass.
            // Match CloudBodies: the cell gaussian is already ~0.2 at the stem edge, so the
            // footprint has to scale radius (cell ^ 1/foot²), not the threshold.
            float height01 = saturate((g.y - baseY) / max(1.0, columnTop - baseY));
            float foot = ShapeFootprint(height01, localAnvil);
            float wide = pow(saturate(cell), 1.0 / max(1.0, foot * foot));
            float need = 0.18 + 0.42 * height01 * height01;
            float inside = smoothstep(need - 0.12, need + 0.12, wide);
            float carved = lerp(CoverMask(puffBody, saturate(0.45 + 0.35 * cell)),
                lerp(0.55 + 0.45 * puffBody, 1.0, sheetBlend), max(sheetBlend, localTowerBlend));
            float towerShape = inside * ShapeMass(carved, ShapeProfile(height01, LocalDome(row.dome, localAnvil), localAnvil)) *
                BaseGate(g.y, baseY, row.baseSharp) * smoothstep(0.04, 0.20, wide);

            // Detail noise only on the boundary. The interior of a deck is one mass, and an
            // empty sample never pays for the 3D taps.
            float coarseShape = max(layerShape, towerShape);
            float edge = saturate(1.0 - abs(coarseShape - 0.42) * 2.4);
            float detail = 0.5;
            if (coarse < 1.0 && row.billow > 0.01 && edge > 0.08)
            {
                // The edge texture follows the lobe (spec 4.2): the erosion periods scale with the rung's lobe, and inside a
                // ramp the two rungs' textures mix by the weight of their bodies.
                if (lobe.y < 0.98) detail = EdgeDetail(ng, coarse, EdgeScale(LobeMetres(lobe.x)), edge, sheetBlend);
                if (lobe.y > 0.02)
                    detail = lerp(detail, EdgeDetail(ng, coarse, EdgeScale(LobeMetres(lobe.x + 1.0)), edge, sheetBlend),
                        lobe.y < 0.98 ? lobe.y : 1.0);
            }
            // Erosion only removes the boundary; signed noise used to create detached wisps.
            // Heaped cloud is nibbled harder than sheets: crisp lobes instead of soft blobs.
            // GPU only; CloudBodies is the shape without boundary erosion.
            float nibble = max(0.0, lerp(0.72, 0.65, sheetBlend) - detail) * row.billow * edge * lerp(0.70, 0.45, sheetBlend);
            layerShape = saturate(layerShape - nibble);
            towerShape = saturate(towerShape - nibble);
            // Heaped cloud gets a steeper boundary ramp after erosion (GPU only, like the
            // nibble): lobes end in a crisp sunlit rim instead of a soft halo.
            float crisp = (1.0 - sheetBlend) * 0.5 * edge;
            layerShape = lerp(layerShape, saturate((layerShape - 0.08) * 1.6), crisp);
            towerShape = lerp(towerShape, saturate((towerShape - 0.08) * 1.6), crisp);
            float layerDensity = saturate((layerShape - 0.12) / 0.70) * smoothstep(0.02, 0.10, layer);
            float towerDensity = towerShape;

            // Fractus (scud): ragged fragments hanging under rain-bearing bases.
            float scud = 0.0;
            float rain = profile.a;
            float floorY = lowFloor;
            // Bounded (Task 13): with rain everywhere (skies 35, 36, 39, 40) the band lay under the whole view and cost up to +50 %. It fades out
            // from a pixel footprint of 30 m and is skipped from 60 m. gFoot is 0 in the density check and the cookie, so the CPU twin is unchanged.
            if (rain > 0.02 && gFoot < 60.0 && g.y < floorY && g.y > floorY - 700.0)
            {
                float frag = tex3Dlod(_CloudNoiseTex, float4(ng.xz / 900.0 + 0.29, ng.y / 420.0 + 0.61, lod)).r;
                float band = smoothstep(floorY - 700.0, floorY - 450.0, g.y) * (1.0 - smoothstep(floorY - 180.0, floorY, g.y));
                scud = smoothstep(0.62, 0.78, frag + rain * 0.35) * band * saturate(rain * 6.0) * (1.0 - smoothstep(30.0, 60.0, gFoot));
            }
            float keep = EyeCloudKeep(g);
            float field = hero;
            gLocalLightingHeight = heroHeight;
            if (layerDensity * 0.55 * keep > field) { field = layerDensity * 0.55 * keep; gLocalLightingHeight = layerHeight; gDeckSheet = sheetBlend; }
            if (towerDensity * 0.85 * keep > field) { field = towerDensity * 0.85 * keep; gLocalLightingHeight = height01; }
            if (scud * 0.4 * keep > field) { field = scud * 0.4 * keep; gLocalLightingHeight = saturate((g.y - floorY + 700.0) / 700.0); }
            // Near-field carving: filaments and thin slots around the camera, so flying inside
            // is rushing structure instead of flat murk. Inside-out only (never below 0.03 into
            // clear air, never through the densest cores): the CPU mirror and shadows agree.
            // Dense interiors keep most of their body (CloudBodies mirrors CarveKeep): slots
            // belong to thin cloud, not tunnels out of a core the aircraft is flying through.
            float carveFade = saturate(1.0 - gFoot / 120.0);
            if (carveFade > 0.01 && field > 0.03 && field < 0.98)
            {
                float cav = tex3Dlod(_CloudNoiseTex, float4(ng.xz / 900.0 + 0.71, ng.y / 760.0 + 0.29, LodFor(gFoot, 900.0))).g;
                float carveKeep = 1.0 - 0.7 * smoothstep(0.25, 0.6, field);
                field = saturate(field - (cav - 0.48) * 1.1 * carveFade * carveKeep);
            }
            if (field <= 0.0) { gLocalLightingHeight = 0.0; gDeckSheet = 0.0; }
            return field;
        }

        // Density, or zero at once where the envelope and set-pieces rule cloud out.
        float DensityOrEmpty(float3 world, float coarse)
        {
            gLocalLightingHeight = 0.0;
            gDeckSheet = 0.0;
            float3 g = world + _CloudWorldOffset;
            if (!NearHero(g) && EmptyAt(g)) return 0.0;
            return Density(world, coarse);
        }

        float AverageAirDensity(float cameraHeight, float sampleHeight, float scaleHeight)
        {
            float h0 = max(0.0, cameraHeight) / scaleHeight;
            float h1 = max(0.0, sampleHeight) / scaleHeight;
            float delta = h1 - h0;
            return abs(delta) < 0.01 ? exp(-0.5 * (h0 + h1)) : (exp(-h0) - exp(-h1)) / delta;
        }

        float AirTransmittance(float distanceToSample, float cameraHeight, float sampleHeight)
        {
            // Molecular haze thins too: a constant floor obscured storm silhouettes even
            // above the aerosol layer. Integrate both profiles along the same view chord.
            float molecular = AverageAirDensity(cameraHeight, sampleHeight, 8000.0);
            float aerosol = AverageAirDensity(cameraHeight, sampleHeight, 2200.0);
            float extinction = 0.000008 * molecular + max(0.0, _CloudAirExtinction - 0.000008) * aerosol;
            return exp(-distanceToSample * extinction);
        }

        // The thin layers (middle alto-, high cirro-) as a 2D field at the layer's mid altitude, from the tape row at the
        // sample: four weighted morphologies (cells, ripples, streaks, veil), each at the row's period rung (CloudBodies.Slab2D
        // is the twin at footprint zero). Two octaves at non-integer scales and a slow warp keep any tile from repeating visibly.
        // SlabCoverMask (SlabMath.SlabCoverMask): sharp below cover 0.15, the old CoverMask from 0.30. Softness 0.5 is the spec's edge.
        float SlabCoverMask(float body, float cover, float softness)
        {
            float edge = max(0.02, 0.5 * cover) * lerp(0.5, 1.5, softness);
            float sharp = saturate((body - (1.0 - cover)) / edge);
            return lerp(sharp, CoverMask(body, cover), smoothstep(0.15, 0.30, cover));
        }

        // The four morphology weights, normalised, any under 0.05 dropped and the rest renormalised (SlabMath.Weights).
        float4 SlabWeights(LayerRow row)
        {
            float sum = row.cells + row.ripples + row.streaks + row.veil;
            if (sum <= 0.0) return float4(0.0, 0.0, 0.0, 1.0);
            float4 w = float4(row.cells, row.ripples, row.streaks, row.veil) / sum;
            w *= step(0.05, w);
            return w / (w.x + w.y + w.z + w.w);
        }

        // The thin layers filter one step coarser than a pixel (Task 13): at the pixel's own footprint the cellular noise still held
        // detail near the pixel grid, and cirrocumulus beat against it as concentric arcs (moire). The extra step rises with the
        // footprint (none at zero, so the CPU twins are untouched).
        float SlabLod(float foot, float period)
        {
            float l = LodFor(foot, period);
            return min(5.0, l + saturate(l));
        }

        // The thin-layer rank law (SlabMath.Rank): the rank of a pattern value is its place in the pattern's own distribution, a Gaussian CDF
        // in the tanh form. The argument is clamped so tanh cannot overflow. SlabSigma is SlabMath.PatternSigma (the measured spreads).
        float SlabRank(float pattern, float sigma)
        {
            float z = clamp((pattern - 0.52) / sigma, -5.0, 5.0);
            return 0.5 + 0.5 * tanh(0.7978846 * (z + 0.044715 * z * z * z));
        }

        float SlabSigma(float3 wts)
        {
            float s = wts.x + wts.y + wts.z;
            return s <= 0.0 ? 0.071 : max(0.02, (wts.x * 0.071 + wts.y * 0.115 + wts.z * 0.082) / s);
        }

        // The weighted pattern value (about 0.52 on average) of a layer at one period. Streak and cell taps fade to their mean
        // once filtered past what the pixel resolves; a morphology with no weight is not fetched. sizeMask (0 large cells, 1 small) is
        // SlabMath.SizeMask of the mesoscale size field.
        // The share of an octave that survives the pixel footprint: full while the footprint is under a twentieth of its lattice, gone from a third (a hash noise has no mip chain,
        // so a lattice finer than the pixel would alias into the stipple the horizon showed). The CPU twin is at footprint zero.
        float OctaveFade(float foot, float lattice)
        {
            return 1.0 - smoothstep(0.05, 0.35, foot / lattice);
        }

        float SlabPattern(float3 g, float layer, LayerRow row, float scale, float2 low, float sizeMask, float patchMask, float foot, float3 wts)
        {
            if (wts.x + wts.y + wts.z <= 0.0) return 0.52;
            // Streak layers keep 30 % of the 27 km warp (SlabMath.StreakWarpCut): it bent fibres into marbled ribbons.
            float2 warp = (low - 0.5) * scale * (1.6 * (1.0 - 0.7 * wts.z / (wts.x + wts.y + wts.z)));
            float2 p = g.xz + warp;
            float2 w = float2(dot(p, _CloudWindDir), dot(p, float2(-_CloudWindDir.y, _CloudWindDir.x)));
            // Rounded taps read the noise where the layer's rotation puts it; streak taps keep their stretch along the wind, so they take
            // the layer's offset only (the aligned layers carry no rotation). The fog bank (layer 2) takes the high layer's.
            float2 np = NoiseXZ(p, layer < 0.5 ? NOISE_MID : NOISE_HIGH);
            float2 nsw = NoiseXZ(w, layer < 0.5 ? NOISE_MID_ALIGNED : NOISE_HIGH_ALIGNED);
            float streak = 0.52, cells = 0.52;
            float2 bend = float2(0.0, 0.0);
            if (wts.z > 0.0)
            {
                // Round 5 streaks (CloudBodies.SlabPattern is the twin): two octaves of smooth value noise stretched along the wind (fibres and broad streaks turned 7 degrees),
                // their weights shifted by a low-frequency width field, broken into dashes of random length by a low-frequency along-wind mask. Only low frequencies bend the coordinate.
                float span = scale * row.aspect;
                float2 sac = nsw;
                if (row.curl > 0.01)
                    sac += (SmoothNoise2(np / (scale * 3.1) + 0.61, SALT_CURL) - 0.5) * scale * 0.8 * row.curl;
                float2 ms = SmoothNoise2(np / (scale * 3.0) + float2(0.29, 0.83), SALT_MEANDER);
                sac.y += (ms.y - 0.5) * scale * 0.9;
                sac.x += (ms.x - 0.5) * span * 0.15;
                float widthField = SmoothNoise2(float2(sac.x / (span * 2.5) + 0.13, sac.y / (scale * 3.0) + 0.71), SALT_STREAK_WIDTH).x;
                float nA = SmoothNoise2(float2(sac.x / (span * 1.4) + 0.37, sac.y / (scale * 0.12) + 0.81), SALT_STREAK_A).x;
                float2 rb = float2(sac.x * 0.9925462 + sac.y * 0.1218693, sac.y * 0.9925462 - sac.x * 0.1218693);
                float nB = SmoothNoise2(float2(rb.x / (span * 0.8) + 0.53, rb.y / (scale * 0.28) + 0.29), SALT_STREAK_B).x;
                float seg = SmoothNoise2(float2(sac.x / (span * 0.9) + 0.77, sac.y / (scale * 0.8) + 0.23), SALT_STREAK_SEG).x;
                float sw = smoothstep(0.2, 0.8, widthField);
                float wA = 0.35 + 0.65 * (1.0 - sw), wB = 0.45 + 0.55 * sw;
                float segF = 0.25 + 1.25 * smoothstep(0.25, 0.75, seg);
                streak = 0.52 + 0.36 * ((wA * OctaveFade(foot, scale * 0.12) * (nA - 0.5) + wB * OctaveFade(foot, scale * 0.28) * (nB - 0.5)) * rsqrt(wA * wA + wB * wB)) * segF;
            }
            if (wts.x + wts.y > 0.0)
            {
                // Round 5 cells (CloudBodies.CellField is the twin): a thresholded sum of three octaves of smooth value noise at 1, 2.3 and 5.1 x 0.14 scale, turned 19, 37 and 71
                // degrees; the coarse octave warps the mid one and both warp the fine one, and a low-frequency size field shifts the weights between fine and coarse.
                // Each octave fades out as the pixel footprint nears its lattice (OctaveFade).
                float cs = scale * 0.45;
                bend = SmoothNoise2(np / (cs * 4.0) + 0.57, SALT_BEND) - 0.5;
                float2 q = np + bend * cs;
                float l1 = scale * 0.14;
                float2 r3 = float2(q.x * 0.3255682 + q.y * 0.9455186, q.y * 0.3255682 - q.x * 0.9455186);
                float2 n3 = SmoothNoise2(r3 / (l1 * 5.1) + float2(0.11, 0.43), SALT_CELL_C);
                float2 r2 = float2(q.x * 0.7986355 + q.y * 0.6018150, q.y * 0.7986355 - q.x * 0.6018150);
                r2 += (n3 - 0.5) * (l1 * 2.3 * 0.8);
                float2 n2 = SmoothNoise2(r2 / (l1 * 2.3) + float2(0.53, 0.19), SALT_CELL_B);
                float2 r1 = float2(q.x * 0.9455186 + q.y * 0.3255682, q.y * 0.9455186 - q.x * 0.3255682);
                r1 += ((n2 - 0.5) + (n3 - 0.5)) * (l1 * 1.2);
                float n1 = SmoothNoise2(r1 / l1 + float2(0.31, 0.77), SALT_CELL_A).x;
                float sizeField = SmoothNoise2(q / (l1 * 12.0) + float2(0.7, 0.2), SALT_CELL_S).x;
                float sblend = smoothstep(0.2, 0.8, sizeField + (sizeMask - 0.5) * 0.5);
                float w1 = 0.2 + 0.45 * sblend, w2 = 0.7, w3 = 1.0 - 0.7 * sblend;
                cells = 0.52 + 0.071 * (w1 * OctaveFade(foot, l1) * (n1 - 0.5) + w2 * OctaveFade(foot, l1 * 2.3) * (n2 - 0.5) + w3 * OctaveFade(foot, l1 * 5.1) * (n3 - 0.5))
                    * rsqrt(w1 * w1 + w2 * w2 + w3 * w3) / 0.226;
            }
            float ripples = cells;
            if (wts.y > 0.0)
            {
                // Parallel rows (altocumulus undulatus, cirrocumulus ripples), only where the Ripples weight is above 0.05 (SlabWeights drops the rest): their amplitude fades once
                // the row spacing is filtered past what the pixel resolves, so the horizon never moires. Round 5: the wavelength varies by about a third (a phase noise of 3 cycles on a
                // lattice of 7 wavelengths), two row sets (1 and 1.43 wavelengths, turned 17 degrees) mix at a weight that follows the size field so rows meet in dislocations, and the
                // patch mask gates the amplitude.
                float wavelength = max(1.0, row.rippleWave);
                float rippleKeep = 1.0 - smoothstep(1.0, 3.0, LodFor(foot, wavelength));
                float2 rowN = SmoothNoise2(np / (wavelength * 7.0) + float2(0.41, 0.17), SALT_ROWS);
                float rows1 = 0.55 + 0.45 * sin((w.y / wavelength + 3.0 * (rowN.x - 0.5)) * 6.2831853 + warp.x / scale * 3.0 + bend.x * 4.0);
                float across2 = w.y * 0.9563 + w.x * 0.2924;
                float rows2 = 0.55 + 0.45 * sin((across2 / (wavelength * 1.43) + 3.0 * (rowN.y - 0.5)) * 6.2831853 + warp.y / scale * 3.0 + bend.y * 3.6 + 1.7);
                float rows = lerp(rows1, rows2, 0.25 + 0.5 * sizeMask);
                float rowKeep = saturate(0.7 + bend.y * 3.5) * lerp(0.35, 1.0, patchMask);
                // rows averages 0.55: dividing by it keeps the pattern's mean at the cells' (CloudBodies.SlabPattern is the twin).
                ripples = cells * lerp(1.0, rows / 0.55, 0.8 * rippleKeep * rowKeep);
            }
            return dot(wts, float3(cells, ripples, streak)) / (wts.x + wts.y + wts.z);
        }

        // One thin layer's 2D field at a map point: pattern, flattening from the veil weight, the
        // edge from SlabCoverMask (with the Detail-independent rank, the covered area follows Cover) and the interior density
        // from Detail. No altitude profile and no strength.
        float Slab2D(float3 g, float layer, LayerRow row, float foot)
        {
            float cover = row.cover * EyeCloudKeep(g);
            if (cover <= 0.005) return 0.0;
            // Mesoscale patches (a layer is never uniform across the whole map) and a slow warp
            // that bends the streaks (hooked cirrus) and breaks the tile period: one fetch.
            float2 nlow = NoiseXZ(g.xz, layer < 0.5 ? NOISE_MID : NOISE_HIGH);
            // Round 6: the two mesoscale fetches are trilinear reads of a coarse mip, whose iso-contours are straight lines: the patch mask drew triangles and diamonds (sky 11). Bend the
            // lookup point by smooth noise first (SlabMath.MesoWarpTile / MesoWarpAmp; CloudBodies.Slab2D is the twin). Hash noise has no mip chain, so it fades with the footprint.
            nlow += (SmoothNoise2(nlow / 2400.0 + float2(0.37, 0.19), SALT_MESO) - 0.5) * (900.0 * OctaveFade(foot, 2400.0));
            float2 low = tex3Dlod(_CloudNoiseTex, float4(nlow / 27000.0 + 0.27, 0.71, 1.5)).rg;
            float patch = low.g;
            // The patch field (R) and the cell-size field (G) of a second mesoscale fetch at another tile (SlabMath.PatchTile).
            float2 mk = tex3Dlod(_CloudNoiseTex, float4(nlow / 31000.0 + float2(0.63, 0.19), 0.37, 1.5)).rg;
            float sizeMask = smoothstep(0.15, 0.85, mk.y);
            float4 wts = SlabWeights(row);
            float2 sp = LadderSplit(row.periodRung);
            float pat0 = 0.0, pat1 = 0.0;
            float patchMask = smoothstep(0.38, 0.66, mk.x);
            if (sp.y < 0.98) pat0 = SlabPattern(g, layer, row, PeriodMetres(sp.x), low, sizeMask, patchMask, foot, wts.xyz);
            if (sp.y > 0.02) pat1 = SlabPattern(g, layer, row, PeriodMetres(sp.x + 1.0), low, sizeMask, patchMask, foot, wts.xyz);
            float pattern = LadderMix(pat0, pat1, sp.y, 0.52);
            // The cover law (SlabMath.Rank): the rank is the pattern's place in its own distribution, so a layer covers about its Cover
            // however crisp or flat it is; Detail only scales the density inside the cover (the return below).
            float rank = SlabRank(pattern, SlabSigma(wts.xyz) * 1.25);
            float body = lerp(rank, 0.7 + 0.3 * rank, wts.w);
            // The mesoscale cover: the old slow modulation, times the patch mask that clusters the cover into patches and clears the lanes between
            // (SlabMath.PatchMask / Patchiness / PatchFactor / CoverTrim).
            float patchiness = (1.0 - smoothstep(0.6, 1.0, cover)) * (1.0 - wts.w);
            float c = saturate(cover * lerp(1.05 + (1.4 * wts.z + 1.2 * (1.0 - wts.z - wts.w)) * (1.0 - smoothstep(0.02, 0.3, cover)), 1.0, wts.w) * (0.55 + 0.9 * saturate(0.5 + (patch - 0.52) * 3.16)) *
                lerp(1.0, smoothstep(0.38, 0.66, mk.x) * 2.17, patchiness));
            // How deep a point sits inside the cover (0 at the edge, 1 at the top rank): Detail turns it into density.
            float depth = saturate((rank - (1.0 - c)) / max(c, 0.05));
            float interior = lerp(0.2 + 0.8 * depth, body, wts.w);
            return SlabCoverMask(body, c, row.softness) * lerp(1.0, interior, lerp(sqrt(row.detail), row.detail, wts.w));
        }

        // The layer's vertical profile saturate(5.2 h (1 - h)) and its integral (SlabMath.Profile / Integral).
        float SlabProfile(float h)
        {
            return (h <= 0.0 || h >= 1.0) ? 0.0 : min(1.0, 5.2 * h * (1.0 - h));
        }

        float SlabRise(float h) { return 5.2 * (0.5 * h * h - h * h * h / 3.0); }

        float SlabF(float h)
        {
            const float h1 = 0.2598076; // (1 - sqrt(1 - 4 / 5.2)) / 2: where the profile reaches 1
            h = saturate(h);
            float f = SlabRise(min(h, h1));
            if (h > h1) f += min(h, 1.0 - h1) - h1;
            if (h > 1.0 - h1) f += SlabRise(h1) - SlabRise(1.0 - h);
            return f;
        }

        // The optical weight of a ray sample spanning altitudes [lo, hi] through a layer [baseY, topY]: the mean of the profile
        // over the sample (SlabMath.Overlap). Length x overlap x density is the sample's optical depth whatever the slant.
        float SlabOverlap(float lo, float hi, float baseY, float topY)
        {
            if (hi < lo) { float t = lo; lo = hi; hi = t; }
            float thick = topY - baseY;
            if (thick <= 0.0) return 0.0;
            float span = hi - lo;
            if (span < 40.0) return SlabProfile(((lo + hi) * 0.5 - baseY) / thick);
            float ha = saturate((lo - baseY) / thick), hb = saturate((hi - baseY) / thick);
            if (hb <= ha) return 0.0;
            return thick * (SlabF(hb) - SlabF(ha)) / span;
        }

        struct SlabLayer
        {
            float3 colour;
            float opacity;
            float weightedDistance;
        };

        struct SlabLayers
        {
            SlabLayer front;
            SlabLayer back;
        };

        // Look pass round 4, the tone shoulder: cloud light is linear HDR (a sunlit face toward the sun summed to 2-3) and the display
        // clipped every sunlit lobe to flat 255 white (about 17 of 47 skies). Linear up to the knee, then a tanh roll-off to one: the lit
        // side keeps its form, and storms flashed by lightning stop clipping too. Per channel, so the warm sun tint desaturates to white
        // in the brightest parts, as film does. GPU-only (the CPU twins are densities).
        // Round 6: a rational shoulder (t / (1 + t)) instead of tanh. tanh reached its ceiling within a knee width, so every lit face above about 1.6 came out the same flat
        // white (7-37 % of the sky pixels of skies 10, 17, 23, 27, 41 over 0.93); this one keeps gradation up to a light of 3 and costs less.
        float3 CloudShoulder(float3 x, float knee, float room)
        {
            float3 t = max(x - knee, 0.0) / room;
            return min(x, knee) + room * t / (1.0 + t);
        }

        // Round 4: the in-scatter toward a cloud takes the cloud's own shade. Airlight was the full horizon colour for every sample, so a
        // shaded base 15 km away came out at about half the luminance of the fog (lit/dark of distant fair-weather cumulus stuck at 1.7,
        // overcast sheets at 1.1-1.4: flat). Air in the shadow of a cloud or a deck is dimmer too; the fog keeps 35 % of its brightness
        // at black and all of it from a luminance of 0.85 up, so lit tops and clear sky are unchanged.
        float3 ShadedFog(float3 light)
        {
            float l = dot(light, float3(0.2126, 0.7152, 0.0722));
            // Round 6: the shaded airlight is darker (0.35 -> 0.10 of the fog at black, curved) and sky-blue, so a distant shaded base keeps its depth against a lit top.
            float s = saturate(l / 0.85);
            return _CloudFogColor * lerp(0.10, 1.0, pow(s, 1.4)) * lerp(float3(0.84, 0.95, 1.14), 1.0, s);
        }

        void AddSlabSample(inout SlabLayer layer, float optical, float3 light, float distance)
        {
            float contribution = (1.0 - layer.opacity) * (1.0 - exp(-optical));
            layer.colour += contribution * light;
            layer.opacity += contribution;
            layer.weightedDistance += contribution * distance;
        }

        void FinishSlab(inout SlabLayer layer, float3 ro, float3 ray)
        {
            if (layer.opacity <= 0.0) return;
            float depth = layer.weightedDistance / layer.opacity;
            float fade = 1.0 - smoothstep(CLOUD_HORIZON_FADE, CLOUD_HORIZON_LIMIT, depth);
            float air = pow(AirTransmittance(depth, ro.y, ro.y + ray.y * depth), 0.7); // round 6: cloud is hazed at 0.7 of the ground haze
            layer.colour = lerp(ShadedFog(layer.colour / layer.opacity) * layer.opacity, layer.colour, air) * fade;
            layer.opacity *= fade;
            layer.weightedDistance *= fade;
        }

        void AppendSlab(inout SlabLayer nearLayer, SlabLayer farLayer)
        {
            float transmit = 1.0 - nearLayer.opacity;
            nearLayer.colour += transmit * farLayer.colour;
            nearLayer.weightedDistance += transmit * farLayer.weightedDistance;
            nearLayer.opacity += transmit * farLayer.opacity;
        }

        // March one chord (layer 0 = middle, 1 = high, 2 = the console fog bank), split around the opaque cloud's representative
        // distance. Camera altitude alone cannot order a nearby storm against an oblique layer. The chord [y0, y1] is the
        // tape-wide range of the layer; each sample reads its own row, so a base that changes along the tape is followed, and is
        // weighed by the part of the local layer its piece of chord crosses (SlabOverlap). Long chords get more samples and blur
        // the pattern to their step instead of aliasing.
        SlabLayers MarchSlab(float3 ro, float3 ray, float limit, float splitDistance, float layer, float y0, float y1,
            float phase, float jitter)
        {
            SlabLayers result = (SlabLayers)0;
            // A tape with no such layer costs nothing (spec 7.4).
            bool present = layer < 0.5 ? _TapeFlags.x > 0.5 : layer < 1.5 ? _TapeFlags.y > 0.5 : _FogBank > 0.005;
            if (!present || y1 <= y0) return result;
            // Inside the layer a level ray must not be skipped (a one-pixel gap at the
            // horizon) nor stretched to the horizon limit (ten samples over 1200 km drew a
            // bright speckled line); 60 km of layer is already opaque.
            bool insideSlab = ro.y > y0 && ro.y < y1;
            if (!insideSlab && abs(ray.y) < 0.0005) return result;
            float slope = abs(ray.y) < 0.0005 ? (ray.y < 0.0 ? -0.0005 : 0.0005) : ray.y;
            float ta = (y0 - ro.y) / slope, tb = (y1 - ro.y) / slope;
            float t0 = max(0.0, min(ta, tb)), t1 = min(max(ta, tb), insideSlab ? min(limit, 60000.0) : limit);
            if (t1 <= t0) return result;
            float span = t1 - t0;
            // A steep chord through a thin layer is one sample (a 2D texture, in effect);
            // grazing chords get up to 24.
            float samples = clamp(ceil(span / 6000.0), 1.0, 24.0);
            float stepLength = span / samples;
            float slant = length(ray.xz);
            // A one-sample chord is a 2D texture in effect (Task 13): it is read at the middle of the chord and filtered by the pixel
            // footprint only. Blurring it by the chord's own horizontal extent (1 km at 10 degrees through a 400 m layer) averaged every cell
            // under about 1 km to its mean, so cirrocumulus and altocumulus cells (periods 250-1000 m) drew nothing. Chords of several samples
            // keep the blur to their step: the samples are that far apart.
            bool single = samples < 1.5;
            // Round 6: a thin layer along a slant. The pixel's ground footprint is 1 / sin(elevation) longer than it is wide, so the pattern is filtered by that (the single sample was
            // filtered by the isotropic footprint only: the stipple strip and the converging wedges at the horizon were cell patterns aliased along the view). And the optical depth of
            // a thin layer grows as sin(elevation) ^ -0.85, not ^ -1: at 10 degrees cirrus of strength 0.12 was five times as opaque as overhead and filled the lower sky.
            float sinE = max(abs(ray.y), 0.02);
            float aniso = 1.0 + 4.0 * (1.0 - smoothstep(0.06, 0.22, sinE));
            float slantGain = layer < 1.5 ? pow(sinE, 0.15) : 1.0;
            float patternFoot = single ? 0.0 : stepLength * 0.5 * slant;

            [loop]
            for (int n = 0; n < 24; n++)
            {
                if (n >= samples) break;
                float t = t0 + (n + (single ? 0.5 : jitter)) * stepLength;
                float3 g = ro + ray * t;
                // (HLSL has no ?: on structs.)
                LayerRow row;
                if (layer > 1.5) row = FogRow(); else row = TapeLayer(TapeS(g.xz), layer);
                if (row.cover <= 0.005) continue;
                // Round 6: a thin layer dissolves into the haze over the last five degrees (its streaks and cells, foreshortened to nothing, were the converging wedges and stipple at the
                // horizon of skies 2, 3, 5 and 7); a full deck keeps its horizon.
                float horizonKeep = layer < 1.5 ? lerp(smoothstep(0.02, 0.09, sinE), 1.0, smoothstep(0.45, 0.75, row.cover)) : 1.0;
                // The altitudes this sample's piece of the chord spans, against the local layer.
                float yA = ro.y + ray.y * (t0 + n * stepLength), yB = ro.y + ray.y * (t0 + (n + 1.0) * stepLength);
                float overlap = SlabOverlap(yA, yB, row.baseY, row.baseY + row.thick);
                if (overlap <= 0.002) continue;
                g.y = row.baseY + 0.5 * row.thick;
                float d = Slab2D(g, layer, row, max(t * _CloudPixelAngle * aniso, patternFoot)) * row.strength * slantGain * horizonKeep;
                if (d <= 0.002) continue;
                // Thin cloud: its own depth toward the sun is all that shades it.
                float toSun = row.thick * 0.5 / max(0.12, _CloudSunDirection.y);
                float direct = exp(-d * toSun * 0.003);
                // Look pass round 4: the old constant 0.22 sun fill lit a thick sheet the same from every direction (lit/dark 1.1-1.5, luma
                // deviation under 0.02). It is now a dim base plus a glow toward the sun (hotspot, the reference's "light hotspot where the sun
                // is"), and the sum goes through the shoulder so a thin layer facing the sun no longer clips flat white.
                float slabGlow = saturate((phase - 0.5) / 1.5);
                // A full deck of thin layer has no structure to light either (sky 27): the same two mesoscale lights as the low decks, gated by the layer's cover.
                float deckGate = layer < 1.5 ? smoothstep(0.55, 0.9, row.cover) : 0.0;
                // Darkens by at most 0.15 and brightens by up to 0.75: the density check holds that a middle layer seen through 40 km of air is visibly present (colour over 0.4; its probe sits at slabN 0.35,
                // where the colour is 0.50 x this factor), and the deck needs both sides to read as structure (lit/dark of the altostratus decks 31, 33, 38 fell under 1.5 when it only brightened).
                float slabN = SmoothNoise2(g.xz / 5200.0 + float2(0.37, 0.58), SALT_DECK_A).x * 0.65 + SmoothNoise2(g.xz / 1900.0 + float2(0.81, 0.12), SALT_DECK_B).x * 0.35;
                float slabMod = 1.0 + deckGate * (slabN < 0.5 ? 1.0 : 1.5) * (slabN - 0.5);
                float3 light = CloudShoulder((_CloudAmbientColor * float3(0.85, 0.96, 1.18) * 0.64 +
                    _CloudSunColor * (direct * lerp(1.0, phase, 0.6) * 0.7 + 0.08 + 0.60 * slabGlow)) * slabMod, 0.62, 0.30);
                float segmentStart = t0 + n * stepLength;
                float frontLength = clamp(splitDistance - segmentStart, 0.0, stepLength);
                float backLength = stepLength - frontLength;
                if (frontLength > 0.0)
                    AddSlabSample(result.front, d * overlap * frontLength * 0.003, light,
                        segmentStart + frontLength * jitter);
                if (backLength > 0.0)
                    AddSlabSample(result.back, d * overlap * backLength * 0.003, light,
                        segmentStart + frontLength + backLength * jitter);
            }
            FinishSlab(result.front, ro, ray);
            FinishSlab(result.back, ro, ray);
            return result;
        }

        float Phase(float cosine, float g)
        {
            // Henyey-Greenstein, normalised to an isotropic value of one.
            // A numerical epsilon must not clip the forward lobe into a broad flat halo.
            // The lobe weights below control its energy instead of changing its shape.
            float inverse = rsqrt(max(0.001, 1.0 + g * g - 2.0 * g * cosine));
            return (1.0 - g * g) * inverse * inverse * inverse;
        }

        // Independent precipitation optics from the displayed weather maps. Cloud
        // density, wetness, local falling rain and shadows retain their own authority.
        float _CloudRainVisuals;

        struct RainCurtainLayer
        {
            float3 colour;           // Premultiplied, with its own aerial perspective.
            float opacity;
            float weightedDistance; // Visible contribution times ray distance.
        };

        struct RainCurtainLayers
        {
            RainCurtainLayer front;
            RainCurtainLayer back;
        };

        void AccumulateRainCurtain(inout RainCurtainLayer layer, float opticalDepth,
            float3 light, float distance)
        {
            float contribution = (1.0 - layer.opacity) * (1.0 - exp(-min(20.0, opticalDepth)));
            layer.colour += contribution * light;
            layer.weightedDistance += contribution * distance;
            layer.opacity += contribution;
        }

        bool ClipRainCurtainHeight(float cameraHeight, float rayVertical, float ceiling,
            inout float start, inout float finish)
        {
            if (abs(rayVertical) < 0.00001)
                return cameraHeight >= -30.0 && cameraHeight <= ceiling && finish > start;
            float lowerT = (-30.0 - cameraHeight) / rayVertical;
            float upperT = (ceiling - cameraHeight) / rayVertical;
            start = max(start, min(lowerT, upperT));
            finish = min(finish, max(lowerT, upperT));
            return finish > start;
        }

        RainCurtainLayers MarchRainCurtain(float3 origin, float3 ray, float sceneDistance,
            float jitter, float phase, float cloudDistance)
        {
            RainCurtainLayers result = (RainCurtainLayers)0;
            const float nearLimit = 1500.0;
            float start = nearLimit;
            float finish = min(sceneDistance, 40000.0);
            // The client setting and displayed-map maximum gate before any map fetch.
            if (_CloudRainVisuals <= 0.001 || finish <= nearLimit ||
                _WeatherMapSpan <= 1.0 || _WeatherFarSpan <= 1.0) return result;

            float3 ro = origin + _CloudWorldOffset;
            if (!ClipRainCurtainHeight(ro.y, ray.y, _CloudAltitudeBounds.y, start, finish)) return result;
            // Eight bounded intervals reuse the existing temporal ray jitter. This
            // coarse quadrature can miss a narrow distant core; it adds no 3D taps.
            [loop]
            for (int k = 0; k < 8; k++)
            {
                float a = k / 8.0, b = (k + 1.0) / 8.0;
                float s0 = start + (finish - start) * a * a;
                float s1 = start + (finish - start) * b * b;
                float t = lerp(s0, s1, clamp(jitter, 0.1, 0.9));
                float3 p = ro + ray * t;

                float4 weather, profile;
                float farShare;
                if (!SampleWeather(p.xz, weather, profile, farShare) || profile.a <= 0.02) continue;
                float baseY = profile.b * CLOUD_BASE_RANGE + _CloudHeightShift;
                if (!ClipRainCurtainHeight(ro.y, ray.y, baseY + 40.0, s0, s1)) continue;
                t = lerp(s0, s1, clamp(jitter, 0.1, 0.9));
                p = ro + ray * t;
                float heightGate = (1.0 - smoothstep(baseY - 180.0, baseY + 40.0, p.y)) *
                    smoothstep(-30.0, 0.0, p.y);
                float rangeGate = smoothstep(nearLimit, 3500.0, t) *
                    (1.0 - smoothstep(32000.0, 40000.0, t));
                // Cloud coverage fades at the map edge; profile rain needs its own
                // fade so the field cannot end as a vertical precipitation wall.
                float mapEdge = 0.5 - max(abs(p.x), abs(p.z)) / _WeatherFarSpan;
                float mapGate = smoothstep(0.0, 0.08, mapEdge);
                float rain = saturate((profile.a - 0.02) / 0.98);
                float extinction = 0.00022 * pow(rain, 0.85) * heightGate * rangeGate * mapGate;
                if (extinction <= 0.0) continue;

                float coverShade = 1.0 - 0.75 * saturate(max(weather.r, weather.b));
                float3 light = _CloudAmbientColor * 0.55 + _CloudGroundColor * 0.10 +
                    _CloudSunColor * (0.02 * coverShade * min(max(phase, 0.0), 2.0));
                if (_CloudFlash > 0.001)
                {
                    float flashA = saturate(1.0 - length(p - _CloudFlashA.xyz) / 12000.0);
                    float flashB = saturate(1.0 - length(p - _CloudFlashB.xyz) / 12000.0);
                    light += float3(0.65, 0.75, 1.0) * 0.4 *
                        (flashA * flashA * saturate(_CloudFlashA.w) +
                         flashB * flashB * saturate(_CloudFlashB.w));
                }
                // Clouds collapse to one representative depth: front/back ordering
                // approximates their volume rather than interleaving every sample.
                float frontLength = clamp(cloudDistance - s0, 0.0, s1 - s0);
                float backLength = (s1 - s0) - frontLength;
                float frontT = s0 + frontLength * clamp(jitter, 0.1, 0.9);
                float backT = s0 + frontLength + backLength * clamp(jitter, 0.1, 0.9);
                // Each half keeps its own atmosphere/depth: do not haze near rain
                // using a far cloud's representative distance.
                if (frontLength > 0.0)
                {
                    float3 frontLight = lerp(_CloudFogColor, light,
                        AirTransmittance(frontT, ro.y, ro.y + ray.y * frontT));
                    AccumulateRainCurtain(result.front, extinction * frontLength, frontLight, frontT);
                }
                if (backLength > 0.0)
                {
                    float3 backLight = lerp(_CloudFogColor, light,
                        AirTransmittance(backT, ro.y, ro.y + ray.y * backT));
                    AccumulateRainCurtain(result.back, extinction * backLength, backLight, backT);
                }
            }
            return result;
        }

        float4 CompositeRainCurtain(float4 cloud, inout float cloudDistance, RainCurtainLayers rain)
        {
            // Exact identity also preserves the depth of a dry/disabled cloud view.
            if (rain.front.opacity + rain.back.opacity <= 0.0) return cloud;
            float frontTransmit = 1.0 - rain.front.opacity;
            float cloudTransmit = 1.0 - cloud.a;
            float3 colour = rain.front.colour + frontTransmit *
                (cloud.rgb + cloudTransmit * rain.back.colour);
            float opacity = rain.front.opacity + frontTransmit *
                (cloud.a + cloudTransmit * rain.back.opacity);
            float weightedDistance = rain.front.weightedDistance + frontTransmit *
                (cloud.a * cloudDistance + cloudTransmit * rain.back.weightedDistance);
            if (opacity > 0.002) cloudDistance = weightedDistance / opacity;
            return float4(colour, opacity);
        }

        bool RayBox(float3 ro, float3 rd, float2 heights, out float start, out float finish)
        {
            float3 safeDir = rd + (1.0 - abs(sign(rd))) * 1e-6;
            float3 inv = 1.0 / safeDir;
            float3 lower = float3(-_WeatherFarSpan * 0.5, heights.x, -_WeatherFarSpan * 0.5);
            float3 upper = float3(_WeatherFarSpan * 0.5, heights.y, _WeatherFarSpan * 0.5);
            float3 lo = (lower - ro) * inv;
            float3 hi = (upper - ro) * inv;
            float3 a = min(lo, hi), b = max(lo, hi);
            start = max(0.0, max(a.x, max(a.y, a.z)));
            finish = min(b.x, min(b.y, b.z));
            return finish > start;
        }

        // Which set-pieces this ray passes near, from its ground track against each one's
        // bounding circle (widened by the sun samples' reach).
        uint HeroMaskFor(float3 ro, float3 rd, float tMax)
        {
            uint mask = 0u;
            float2 track = rd.xz;
            float trackSq = max(1e-8, dot(track, track));
            [loop]
            for (int k = 0; k < 5; k++)
            {
                if (k >= _HeroCount || _HeroB[k].z <= 0.001) continue;
                float3 reach = HeroReach(_HeroA[k], _HeroB[k]);
                float t = clamp(dot(reach.xy - ro.xz, track) / trackSq, 0.0, tMax);
                float2 closest = ro.xz + track * t - reach.xy;
                float r = reach.z + 3000.0;
                if (dot(closest, closest) < r * r) mask |= 1u << k;
            }
            return mask;
        }

        // The stretch of the ray (t from, t to) inside the bounding circles of its set-pieces.
        float2 HeroInterval(float3 ro, float3 rd)
        {
            float2 track = rd.xz;
            float aa = max(1e-8, dot(track, track));
            float enter = 1e9, leave = -1e9;
            [loop]
            for (int k = 0; k < 5; k++)
            {
                if ((gHeroMask & (1u << k)) == 0u) continue;
                float3 reach = HeroReach(_HeroA[k], _HeroB[k]);
                float2 o = ro.xz - reach.xy;
                float bb = dot(track, o), cc = dot(o, o) - reach.z * reach.z;
                float disc = bb * bb - aa * cc;
                if (disc <= 0.0) continue;
                float root = sqrt(disc);
                enter = min(enter, (-bb - root) / aa);
                leave = max(leave, (-bb + root) / aa);
            }
            return float2(enter, leave);
        }

        // The whole sky along one view ray: premultiplied colour and opacity.
        // origin: local camera position; ray: unit view direction; sceneDistance: metres to the
        // first opaque surface (or CLOUD_HORIZON_LIMIT for sky); pixel: screen pixel (dither).
        float4 MarchSky(float3 origin, float3 ray, float sceneDistance, float2 pixel, out float cloudDistance)
        {
            float3 ro = origin + _CloudWorldOffset;
            float2 bounds = _CloudAltitudeBounds;
            float sceneLimit = min(sceneDistance, CLOUD_HORIZON_LIMIT);
            float start, finish;
            gHeroMask = 0u;
            if (_HeroCount > 0.0)
            {
                // Set-pieces widen the march only for the rays that pass near one.
                float2 wide = float2(min(bounds.x, _CloudHeroBounds.x), max(bounds.y, _CloudHeroBounds.y));
                if (RayBox(ro, ray, wide, start, finish))
                {
                    gHeroMask = HeroMaskFor(ro, ray, min(finish, sceneLimit));
                    if (gHeroMask != 0u) bounds = wide;
                }
            }
            RayBox(ro, ray, bounds, start, finish);
            finish = min(finish, sceneLimit);
            // Ordinary weather retains its fixed budget/range. A visible set-piece gets
            // its own bounded interval beyond it, even from the opposite map corner.
            float2 heroSpan = gHeroMask != 0u ? HeroInterval(ro, ray) : float2(1e9, -1e9);
            finish = min(finish, max(CLOUD_FAR_LIMIT, heroSpan.y));

            float transmittance = 1.0;
            float3 colour = 0.0;
            float weightedDistance = 0.0;
            // Once a sample's sun is extinguished, later samples in the same cloud take one
            // coarse tap instead of three. Clear air re-opens it.
            float sunOpen = 1.0;
            float cosine = dot(ray, _CloudSunDirection);
            // Forward scattering, a sharp silver-lining lobe at the rims toward the
            // sun, and some back scatter.
            // The narrow lobe keeps a silver lining rather than the old 16-degree plateau.
            // Its low weight bounds the combined peak below eight (isotropic = one).
            float phase = 0.58 * Phase(cosine, 0.58) + 0.08 * Phase(cosine, 0.82) + 0.34 * Phase(cosine, -0.25);
            // One broad secondary lobe per ray; reuse the measured sun optical depth below.
            float secondaryPhase = 0.70 * Phase(cosine, 0.18) + 0.30;
            // Look pass round 4: dense cloud scatters many times, which washes the anisotropy out, so a body samples a mild version of the
            // phase and only thin cloud (rims, sheets: low density) gets the silver-lining lobe, at 0.85 of its excess. Sunlit faces then sit at about
            // the same brightness from every direction instead of 2-3 x brighter toward the sun (the blown whites).
            float phaseBody = lerp(1.0, phase, 0.35);
            float phaseRim = max(0.0, phase - phaseBody) * 0.85;
            // The hotspot: sun light that gets through a sheet or a deck is forward-scattered, so a thick layer glows around the sun (the
            // references' "light hotspot where the sun is") and is otherwise a flat grey. 0 beyond about 60 degrees from the sun.
            float sunGlow = 0.55 * pow(saturate((phase - 0.5) / 1.5), 2.0);
            float awayFromSun = saturate(0.5 - 0.5 * cosine);
            // Integer avalanche removes IGN's visible diagonal lattice. This changes ray
            // sample placement only: the actual cloud field and detail stay anchored.
            uint pixelHash = (uint)pixel.x * 1973u + (uint)pixel.y * 9277u + 89173u;
            pixelHash ^= pixelHash >> 16;
            pixelHash *= 0x7feb352du;
            pixelHash ^= pixelHash >> 15;
            pixelHash *= 0x846ca68bu;
            pixelHash ^= pixelHash >> 16;
            float ign = (pixelHash & 65535u) / 65536.0;
            // Temporal accumulation: the dither cycles each frame, so carried values average
            // the march noise away instead of freezing one dither pattern in. Full and half
            // resolution keep the stable dither (nothing averages it there).
            if (_CloudCheckerOn > 0.5) ign = frac(ign + _CloudChecker.z);
            float jitter = lerp(0.1, 0.9, ign);

            // Up to four segments along the ray: full detail to the near limit, a coarse march
            // through the far level of detail out to the horizon, and a fine march across the
            // set-pieces this ray passes beyond 20 km. Distant set-pieces are the size of a far
            // step; marched with far steps they come out sliced into flat plates.
            float cutNear = clamp(CLOUD_NEAR_LIMIT, start, max(start, finish));
            float h0 = finish, h1 = finish;
            if (gHeroMask != 0u && finish > 20000.0)
            {
                h0 = clamp(max(heroSpan.x, 20000.0), start, finish);
                h1 = clamp(heroSpan.y, start, finish);
                if (h1 <= h0) { h0 = finish; h1 = finish; }
            }
            float b1 = min(cutNear, h0), b3 = max(cutNear, h1);
            float b2 = cutNear <= h0 ? h0 : cutNear <= h1 ? cutNear : h1;
            float nearTotal = max(1.0, cutNear - start - max(0.0, min(h1, cutNear) - min(h0, cutNear)));
            float ordinaryFinish = min(finish, CLOUD_FAR_LIMIT);
            float farTotal = max(1.0, ordinaryFinish - cutNear -
                max(0.0, min(h1, ordinaryFinish) - max(h0, cutNear)));
            [loop]
            for (int segment = 0; segment < 4; segment++)
            {
                float s0 = segment == 0 ? start : segment == 1 ? b1 : segment == 2 ? b2 : b3;
                float s1 = segment == 0 ? b1 : segment == 1 ? b2 : segment == 2 ? b3 : finish;
                if (finish <= start || s1 <= s0 + 1.0 || transmittance < 0.05) continue;
                float mid = 0.5 * (s0 + s1);
                bool hero = mid > h0 && mid < h1;
                bool farSeg = !hero && mid > CLOUD_NEAR_LIMIT;
                if (!hero)
                {
                    s1 = min(s1, CLOUD_FAR_LIMIT);
                    if (s1 <= s0 + 1.0) continue;
                }
                float farPass = farSeg ? 1.0 : 0.0;
                float span = s1 - s0;
                float steps;
                if (hero) steps = 96.0;
                else if (farSeg) steps = _CloudFarSteps * span / farTotal;
                else
                {
                    steps = _CloudSteps * span / nearTotal * saturate(nearTotal / 12000.0 + 0.35);
                    if (s0 > 25000.0) steps *= 0.68;
                }
                steps = clamp(floor(steps), 4.0, 96.0);
                // Resolve the first few hundred metres during entry/exit, including short
                // upward chords. Fade back to the ordinary budget before distant clouds.
                float proximity = farSeg || hero ? 0.0 : 1.0 - smoothstep(500.0, 2500.0, s0);
                steps = max(steps, floor(24.0 * proximity));
                // Set-pieces: adaptive steps of about 0.6 % of the distance (a fraction of a
                // tower's width at any range), four at a time through empty air, backing up to
                // fine steps where cloud begins.
                float tHero = s0 + jitter * clamp(s0 * 0.006, 150.0, 900.0);
                float fineUntil = -1.0;
                bool stride = false;
                [loop]
                for (int n = 0; n < 96; n++)
                {
                    if ((hero ? tHero >= s1 : n >= steps) || transmittance < 0.05) break;
                    float a = n / steps, b = (n + 1.0) / steps;
                    // Cubic spacing spends the same bounded budget on nearby filaments;
                    // distant segments keep quadratic spacing and their existing detail.
                    float fine = clamp(tHero * 0.006, 150.0, 900.0);
                    float sampleStart = lerp(a * a, a * a * a, proximity);
                    float sampleEnd = lerp(b * b, b * b * b, proximity);
                    float stepLength = hero ? fine : span * (sampleEnd - sampleStart);
                    float t = hero ? tHero : s0 + span * lerp(sampleStart, sampleEnd, jitter);
                    float3 world = origin + ray * t;
                    gHeroOnly = t > CLOUD_FAR_LIMIT;
                    gFoot = max(t * _CloudPixelAngle, stepLength * 0.35);
                    gVert = farSeg ? 0.5 * abs(ray.y) * stepLength : 0.0;
                    float d = DensityOrEmpty(world, farSeg ? 1.2 : hero ? 1.0 : 0.0);
                    // Shadow probes also evaluate Density and overwrite its local profile.
                    // Keep the primary sample's height within its own cloud body.
                    float localLightingHeight = gLocalLightingHeight;
                    float deckSheet = gDeckSheet;
                    if (hero)
                    {
                        if (d <= 0.003 && tHero > fineUntil) { tHero += fine * 4.0; stride = true; continue; }
                        if (d > 0.003 && stride) { fineUntil = tHero; tHero -= fine * 3.0; stride = false; continue; }
                        stride = false;
                        tHero += fine;
                    }
                    if (d <= 0.003) sunOpen = 1.0;
                    if (d > 0.003)
                    {
                        // Light taps in one loop, so the (large) density function is compiled
                        // once for all of them: three increasingly coarse intervals toward the
                        // sun resolve nearby lobes and the thicker cloud behind them, and one
                        // tap straight up measures the cloud shading this point from the sky.
                        // Far away one coarse sun interval is indistinguishable and height
                        // stands in for the cloud above.
                        float optical = 0.0, above = 0.0;
                        bool coarseSun = farSeg || sunOpen < 0.06;
                        int taps = coarseSun ? 1 : 3;
                        gLowReuse = true; // the sun taps reuse this sample's genus row
                        [loop]
                        for (int k = 0; k < taps; k++)
                        {
                            float3 offset = k == 2 ? float3(0.0, 380.0, 0.0)
                                : _CloudSunDirection * (coarseSun ? 1500.0 : k == 0 ? 170.0 : 1250.0);
                            float tapCoarse = coarseSun ? 1.8 : k == 0 ? 1.0 : k == 1 ? 1.6 : 1.2;
                            float tap = DensityOrEmpty(world + offset, tapCoarse);
                            if (k == 2) above = tap;
                            else optical += tap * (coarseSun ? 3000.0 : k == 0 ? 340.0 : 2300.0);
                        }
                        gLowReuse = false;
                        optical *= 0.0040; // round 4: 0.0021 left fair-weather bodies lit through (flat 1.7-1.8 lit/dark)
                        float h = saturate(localLightingHeight);
                        float direct = exp(-optical);
                        // The upward tap was only skipped when the sun was already out.
                        bool sunOut = sunOpen < 0.06;
                        sunOpen = direct;
                        // Subtle edge darkening away from the sun. This only attenuates:
                        // direct-light occlusion comes from the sun march, without a gain
                        // on dense samples that could flatten the sunlit structure.
                        float powder = 1.0 - exp(-d * 6.0);
                        direct *= lerp(1.0, lerp(0.72, 1.0, powder), awayFromSun * 0.5);
                        // Sky light reaches a point only through the cloud above it, so
                        // bases go dark and tops stay bright. Far away the height says enough.
                        if (farSeg) above = d * (1.0 - h);
                        else if (sunOut) above = d;
                        float skyAccess = lerp(0.27, 1.0, h) * exp(-d * 0.70) * exp(-above * 1.7);
                        // A cheaper multiple-scattering approximation, not a path tracer:
                        // broaden one lobe and reduce its extinction, while local sky access
                        // keeps bases and buried cores darker than exposed crowns. No
                        // density-independent sun floor remains inside an opaque storm.
                        float secondaryAccess = lerp(0.35, 1.0, h) * exp(-above * 0.5);
                        // Round 4: 0.22 -> 0.12 (its decay follows the steeper optical depth). The fill was sun-coloured and as bright as the shade itself, which greyed the shaded sides and
                        // capped lit/dark near 1.8; the shade is now the sky's blue (below) and this is only the in-cloud glow.
                        float scattered = 0.12 * exp(-optical * 0.13) * secondaryPhase * secondaryAccess;
                        float phaseHere = phaseBody + phaseRim * exp(-d * 2.2);
                        // Shade is lit by blue sky, not grey: a cool tint keeps sunlit lobes and
                        // shadowed lobes apart, like the reference flight footage.
                        // Round 4: the shade is sky-blue light with a floor (0.19 of the ambient, tinted blue 0.80/0.96/1.25 so the shade reads (39,61,97)-like rather than slate; the open-sky part is 0.40, was 0.72, whose exposed shaded sides sat near 0.4 luminance against 0.95 lit): a base is never black (the references' darkest
                        // 3 % is (39,61,97) in blue-sky scenes, (48,66,94) under a Cb), and lit/dark of towers stays under 4.4.
                        float3 light = _CloudAmbientColor * float3(0.80, 0.96, 1.25) * (skyAccess * 0.40 + 0.19) +
                            _CloudGroundColor * (1.0 - h) * exp(-d) +
                            _CloudSunColor * (direct * phaseHere * 0.55 + scattered + sunGlow * exp(-optical * 0.10));
                        // Lightning: the bolt's light scattering off the droplets around it. It
                        // reaches the shadowed cores the sun cannot, so the whole storm flickers.
                        if (_CloudFlash > 0.001)
                        {
                            float3 globalSample = world + _CloudWorldOffset;
                            float a = saturate(1.0 - length(globalSample - _CloudFlashA.xyz) / 12000.0);
                            float b = saturate(1.0 - length(globalSample - _CloudFlashB.xyz) / 12000.0);
                            light += float3(0.65, 0.75, 1.0) * (a * a * _CloudFlashA.w + b * b * _CloudFlashB.w) * d * 1.4;
                        }
                        // Near-field wisps: inside cloud the march saturates within metres and every
                        // pixel would be the same flat grey. Modulate the scattered light itself by
                        // world-anchored noise (filtered to the sample footprint), so filaments rush
                        // past the camera. Light only: density, the CPU mirror and shadows agree.
                        float wispFade = saturate(1.0 - gFoot / 150.0);
                        if (wispFade > 0.01)
                        {
                            float3 wg = world + _CloudWorldOffset;
                            float3 nwg = NoiseG(wg, NOISE_LOW);
                            float3 wispN = tex3Dlod(_CloudNoiseTex, float4(nwg.xz / 1200.0 + 0.47, nwg.y / 1000.0 + 0.13, LodFor(gFoot, 1200.0))).rgb;
                            float wisp = wispN.x * 0.45 + wispN.y * 0.20 + wispN.z * 0.35;
                            // Smaller ragged filaments supply parallax as the aircraft crosses
                            // the volume. Filter them out before a march interval can alias them.
                            float filamentFade = (1.0 - smoothstep(600.0, 1800.0, t)) *
                                (1.0 - smoothstep(20.0, 70.0, gFoot));
                            if (filamentFade > 0.01)
                            {
                                float filament = tex3Dlod(_CloudNoiseTex, float4(nwg.xz / 360.0 + 0.19,
                                    nwg.y / 300.0 + 0.67, LodFor(gFoot, 360.0))).g;
                                wisp += (filament - 0.5) * 0.55 * filamentFade;
                            }
                            light *= 1.0 + (wisp - 0.5) * 1.1 * (0.5 + saturate(d)) * wispFade;
                        }
                        float deckMul = 1.0;
                        if (deckSheet > 0.01)
                        {
                            // Round 6: a smooth deck has no density structure to light, so its base was one grey (luma deviation 0.03-0.05 of skies 29, 34, 35, 36, 40). The transmitted
                            // light varies with the deck's thickness at two mesoscales (5.2 km and 1.9 km, hash noise anchored to the world). Light only: density, the CPU twin and shadows agree.
                            float3 dg = world + _CloudWorldOffset;
                            float deckN = SmoothNoise2(dg.xz / 5200.0 + float2(0.37, 0.58), SALT_DECK_A).x * 0.65 + SmoothNoise2(dg.xz / 1900.0 + float2(0.81, 0.12), SALT_DECK_B).x * 0.35;
                            deckMul = 1.0 + 1.4 * (deckN - 0.5) * deckSheet; // applied after the haze blend below: a deck is mostly airlight at 5-30 km and the modulation of its own light vanished in it
                        }
                        light = CloudShoulder(light, 0.62, 0.40);
                        // Integrate actual density along this ray. A camera-wide multiplier
                        // made the same edge eight times more opaque after entering it.
                        // Dense cloud within a few hundred metres closes in: the far-tuned
                        // extinction left ~2 km of view inside a cumulus core. It depends on
                        // distance and density only, so nothing pops on entry and slots stay open.
                        float nearClose = 1.0 + 5.0 * (1.0 - smoothstep(150.0, 900.0, t)) * smoothstep(0.15, 0.45, d);
                        float absorb = exp(-d * stepLength * 0.003 * nearClose);
                        float contribution = transmittance * (1.0 - absorb);
                        // Haze each sample at its own distance: one haze per ray at the
                        // opacity-weighted distance jumped where that distance jumps (grazing
                        // rays crossing from the near map to the far map drew a step above
                        // the horizon under overcast).
                        float sampleAir = pow(AirTransmittance(t, ro.y, ro.y + ray.y * t), 0.7); // round 6: lit tops and shaded bases keep more of their contrast at 20-30 km
                        colour += contribution * deckMul * lerp(ShadedFog(light), light, sampleAir);
                        weightedDistance += contribution * t;
                        
                        transmittance *= absorb;
                    }
                }
            }
            gVert = 0.0;
            gHeroOnly = false;
            float opacity = 1.0 - transmittance;
            // Visible wisps need their real depth too: the composite displays opacity
            // above 0.002, so its reprojection cannot use the empty-ray far fallback.
            cloudDistance = opacity > 0.002 ? weightedDistance / opacity : CLOUD_FAR_LIMIT;
            // Airlight is already applied per sample inside the march.

            RainCurtainLayers rain = MarchRainCurtain(origin, ray, sceneDistance,
                jitter, phase, cloudDistance);
            float4 rainyCloud = CompositeRainCurtain(float4(colour, opacity), cloudDistance, rain);
            colour = rainyCloud.rgb;
            opacity = rainyCloud.a;

            // Horizon deck: past the march the weather continues as one analytic plane at
            // the deck's altitude, with the far map's own outer coverage, so the clouds
            // run on to the horizon instead of ending in a ring.
            float horizonT = (_HorizonDeck - ro.y) / ray.y;
            // Sky pixels carry no occluder: let the deck run past the horizon limit there
            // (aerial perspective whitens it), so its far edge never draws a line.
            float deckLimit = sceneDistance >= CLOUD_HORIZON_LIMIT ? 2e7 : sceneDistance;
            if (_HorizonCover > 0.01 && horizonT > CLOUD_FAR_LIMIT * 0.75 && horizonT < deckLimit)
            {
                float2 hp = ro.xz + ray.xz * horizonT;
                float2 nhp = NoiseXZ(hp, NOISE_HORIZON);
                float hn = tex3Dlod(_CloudNoiseTex, float4(nhp / 52000.0 + 0.31, 0.47, 3.0)).r * 0.62 +
                    tex3Dlod(_CloudNoiseTex, float4(float2(nhp.y, -nhp.x) / 141000.0 + 0.77, 0.19, 3.5)).r * 0.38;
                float hBody = saturate(0.5 + (hn - 0.52) * 3.16);
                float hCover = CoverMask(hBody, _HorizonCover);
                float hPath = min(_HorizonDepth / max(0.015, abs(ray.y)), 150000.0);
                // A distance fade collapses to less than a pixel when the camera flies near
                // the deck's altitude (hundreds of km per milliradian), drawing a hard step.
                // Dissolve by view angle instead: always about four texels into the haze.
                float angleFade = smoothstep(0.0, _CloudPixelAngle * 4.0, abs(ray.y));
                float hOpacity = hCover * (1.0 - exp(-hPath * 0.00003)) *
                    smoothstep(CLOUD_FAR_LIMIT * 0.75, CLOUD_FAR_LIMIT, horizonT) *
                    angleFade;
                bool below = ro.y < _HorizonDeck;
                float3 hLight = below
                    ? _CloudAmbientColor * 0.5 + _CloudGroundColor * 0.25 + _CloudSunColor * 0.06
                    : _CloudAmbientColor * 0.8 + _CloudSunColor * (phase * 0.35 + 0.2);
                float hAir = AirTransmittance(horizonT, ro.y, _HorizonDeck);
                float3 hColour = lerp(_CloudFogColor * hOpacity, hLight * hOpacity, hAir);
                float oldOpacity = opacity;
                float oldDistance = cloudDistance;
                if (horizonT < cloudDistance)
                {
                    colour = hColour + (1.0 - hOpacity) * colour;
                    opacity = hOpacity + (1.0 - hOpacity) * opacity;
                    if (opacity > 0.002)
                        cloudDistance = (hOpacity * horizonT + (1.0 - hOpacity) * oldOpacity * oldDistance) / opacity;
                }
                else
                {
                    float contribution = (1.0 - opacity) * hOpacity;
                    colour += (1.0 - opacity) * hColour;
                    opacity += contribution;
                    if (opacity > 0.002)
                        cloudDistance = (oldOpacity * oldDistance + contribution * horizonT) / opacity;
                }
            }

            // Non-overlapping height bands have a known order along a rising/falling ray.
            // Split each chord at cloud depth, then compose its front and back portions in
            // that order. Reuse each density sample; no second slab march is needed.
            float slabLimit = min(sceneDistance, CLOUD_HORIZON_LIMIT);
            if (opacity > 0.996) slabLimit = min(slabLimit, cloudDistance);
            SlabLayers fog = MarchSlab(ro, ray, slabLimit, cloudDistance, 2.0, -30.0, 330.0, phase, jitter);
            SlabLayers middle = MarchSlab(ro, ray, slabLimit, cloudDistance, 0.0, _TapeChord.x, _TapeChord.y, phase, jitter);
            SlabLayers high = MarchSlab(ro, ray, slabLimit, cloudDistance, 1.0, _TapeChord.z, _TapeChord.w, phase, jitter);
            SlabLayer front = fog.front;
            SlabLayer back = fog.back;
            if (ray.y < 0.0) { front = high.front; back = high.back; }
            AppendSlab(front, middle.front);
            AppendSlab(back, middle.back);
            if (ray.y >= 0.0) { AppendSlab(front, high.front); AppendSlab(back, high.back); }
            else { AppendSlab(front, fog.front); AppendSlab(back, fog.back); }
            float frontTransmit = 1.0 - front.opacity;
            float cloudTransmit = 1.0 - opacity;
            float totalOpacity = front.opacity + frontTransmit * (opacity + cloudTransmit * back.opacity);
            if (totalOpacity > 0.002)
                cloudDistance = (front.weightedDistance + frontTransmit *
                    (opacity * cloudDistance + cloudTransmit * back.weightedDistance)) / totalOpacity;
            return float4(front.colour + frontTransmit * (colour + cloudTransmit * back.colour), totalOpacity);
        }

        // Metres along the view ray to the scene; sky pixels (far plane) lie beyond the horizon.
        float SceneDistance(float rawDepth, float rayDotForward)
        {
            #if defined(UNITY_REVERSED_Z)
            if (rawDepth <= 0.000001) return CLOUD_HORIZON_LIMIT;
            #else
            if (rawDepth >= 0.999999) return CLOUD_HORIZON_LIMIT;
            #endif
            return LinearEyeDepth(rawDepth) / max(0.025, rayDotForward);
        }
        ENDHLSL

        // Full resolution, drawn by the camera-following cube: the fallback when the
        // reduced-resolution pass cannot run.
        Pass
        {
            Name "CloudBody"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 screen : TEXCOORD0;
                float3 world : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.pos);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float3 origin = _WorldSpaceCameraPos.xyz;
                float3 ray = normalize(i.world - origin);
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                float cloudDistance;
                float4 sky = MarchSky(origin, ray, SceneDistance(rawDepth, dot(ray, -UNITY_MATRIX_V[2].xyz)), i.pos.xy, cloudDistance);
                if (sky.a < 0.002) discard;
                return sky;
            }
            ENDHLSL
        }

        Pass
        {
            Name "WeatherMapBlend"
            Tags { "LightMode"="BoscaliWeatherMapBlend" }
            Blend Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment blendMap
            sampler2D _MainTex, _CloudMapTarget;
            float _WeatherMapBlend;
            float4 blendMap(v2f_img i) : SV_Target
            {
                return lerp(tex2D(_MainTex, i.uv), tex2D(_CloudMapTarget, i.uv), _WeatherMapBlend);
            }
            ENDHLSL
        }

        // Reduced resolution: a full-screen triangle into the cloud target (colour) and a
        // depth target (the scene eye depth each texel marched against), which the composite
        // uses to upsample without halos along terrain edges. Drawn by the render pass only.
        Pass
        {
            Name "CloudLowRes"
            Tags { "LightMode"="BoscaliCloudLowRes" }
            Blend Off
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex lowVert
            #pragma fragment lowFrag
            #pragma target 3.5

            struct lowV2f { float4 pos : SV_POSITION; };
            struct LowOut
            {
                float4 colour : SV_Target0;
                float4 depth : SV_Target1;
            };

            lowV2f lowVert(uint id : SV_VertexID)
            {
                lowV2f o;
                float2 p = float2((id << 1) & 2, id & 2);
                o.pos = float4(p * 2.0 - 1.0, 0.5, 1.0);
                return o;
            }

            LowOut lowFrag(lowV2f i)
            {
                // Texture-space uv of this texel: Unity's convention (v up), which is also how
                // the depth texture and the composite's screen uv address the screen.
                // With temporal update on, this quarter-size target marches one texel of each
                // 2x2 block of the half-size sky; the resolve fills in the other three.
                float2 texel = _CloudCheckerOn > 0.5 ? floor(i.pos.xy) * 2.0 + _CloudChecker.xy + 0.5 : i.pos.xy;
                float2 uv = texel * _CloudLowResSize.zw;
                float3 bottom = lerp(_CloudFrustum[0].xyz, _CloudFrustum[1].xyz, uv.x);
                float3 top = lerp(_CloudFrustum[2].xyz, _CloudFrustum[3].xyz, uv.x);
                float3 view = lerp(bottom, top, uv.y);
                float viewLength = length(view);
                float3 ray = view / viewLength;
                float rawDepth = tex2Dlod(_CameraDepthTexture, float4(uv, 0, 0)).r;
                float eyeDepth = LinearEyeDepth(rawDepth);
                // view has unit length along the camera axis, so metres along the ray are
                // eye depth times its length.
                #if defined(UNITY_REVERSED_Z)
                float sceneDistance = rawDepth <= 0.000001 ? CLOUD_HORIZON_LIMIT : eyeDepth * viewLength;
                #else
                float sceneDistance = rawDepth >= 0.999999 ? CLOUD_HORIZON_LIMIT : eyeDepth * viewLength;
                #endif
                LowOut o;
                float cloudDistance;
                o.colour = MarchSky(_CloudCameraPos, ray, sceneDistance, texel, cloudDistance);
                // Quarter data retains scene depth for resolve. The direct half-size
                // output needs only the nearest relevant depth for the final upsample.
                float upsampleDepth = _CloudCheckerOn < 0.5 && o.colour.a > 0.002 ?
                    min(eyeDepth, cloudDistance / viewLength) : eyeDepth;
                o.depth = float4(upsampleDepth, cloudDistance, 0.0, 0.0);
                return o;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
