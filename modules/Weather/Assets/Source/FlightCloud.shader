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
        // Cloud genera: low-deck depth/smoothness, middle and high layers.
        float _LayerDepth, _LayerSmooth, _MidCover, _MidSheet, _HighCover, _HighVeil;
        // Low-cloud genus, already resolved (CloudShape.Resolve). Smoothness fades lobes into a sheet.
        // ShapeProfile / ShapeFootprint / BaseGate match CloudShape.cs. Change both together.
        float _PuffScale, _PuffDepth, _BaseSharp, _BaseWobble, _Dome, _Billow, _Anvil;
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
        // Frontal boundary (SkySplit): A = normal x, normal z, offset, width;
        // B = amount, meander amplitude, wavelength, phase.
        float4 _SplitA, _SplitB;
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

        // Near march limit: beyond it the far map's level of detail takes over.
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

        float LodFor(float foot, float period);

        // Individual cloud bodies: cellular noise at non-integer scales (so the 64^3
        // tile never visibly repeats), in [0, 1]. Coverage thresholds it into separate
        // clouds with sky between them, the way broken and scattered decks really look.
        float Bodies(float3 g, float scale, float lod)
        {
            float period = scale * 4.0;
            float a = tex3Dlod(_CloudNoiseTex, float4(g.xz / period, g.y / (period * 0.9), lod)).b;
            float b = tex3Dlod(_CloudNoiseTex, float4(float2(g.z, -g.x) / (period * 2.73) + 0.37,
                g.y / (period * 2.1), lod)).r;
            float c = tex3Dlod(_CloudNoiseTex, float4(g.xz / (period * 0.29) + float2(0.17, 0.61),
                g.y / (period * 0.24), max(lod, LodFor(gFoot, period * 0.29)))).b;
            return saturate((a * 0.65 + b * 0.15 + c * 0.20 - 0.22) * 2.5);
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

        // Mirror of SkySplit.Share: 0 on the open side of the front, 1 behind it.
        float SplitShare(float2 xz)
        {
            if (_SplitB.x <= 0.0) return 1.0;
            float along = -xz.x * _SplitA.y + xz.y * _SplitA.x;
            float phase = along * (6.2831853 / _SplitB.z) + _SplitB.w;
            float offset = _SplitA.z + _SplitB.y * (sin(phase) - sin(_SplitB.w) +
                0.35 * (sin(phase * 2.1 + _SplitB.w) - sin(_SplitB.w * 2.1 + _SplitB.w)));
            return smoothstep(-_SplitA.w, _SplitA.w, offset - dot(xz, _SplitA.xy));
        }

        // Cauliflower erosion for the set-pieces: two octaves of the base channel at a
        // non-integer scale ratio and rotated against each other, so a wall tens of km wide
        // never shows the 64^3 tile as a regular grid of lumps (bubble wrap).
        float Billow(float3 g, float lod)
        {
            float a = tex3Dlod(_CloudNoiseTex, float4(g.xz / 12000.0 + 0.43, g.y / 10000.0 + 0.11, lod)).b;
            float b = tex3Dlod(_CloudNoiseTex, float4(float2(g.z, -g.x) / 4200.0 + 0.71, g.y / 3600.0 + 0.57, lod)).b;
            return saturate((a * 0.65 + b * 0.35 - 0.22) * 2.5);
        }

        // Turns a smooth set-piece envelope into a lumpy cumuliform surface.
        float Erode(float shape, float3 g, float lod)
        {
            return saturate((shape * 1.3 - (1.0 - Billow(g, lod)) * 0.55) / 0.75);
        }

        float HeroNoise(float3 g, float scale, float lod)
        {
            return tex3Dlod(_CloudNoiseTex, float4(g.xz / scale + 0.17, g.y / (scale * 0.8) + 0.23, lod)).r;
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

        float Density(float3 world, float coarse)
        {
            gLocalLightingHeight = 0.0;
            float3 g = world + _CloudWorldOffset;
            // Filter the 1250 m detail to what one pixel and one step can resolve.
            float lod = max(coarse, LodFor(gFoot, 1250.0));
            float bodyLod = max(coarse, LodFor(gFoot, 24000.0));
            float puffLod = max(coarse, LodFor(gFoot, max(1200.0, _PuffScale * 4.0)));
            float hero = gHeroMask != 0u ? Heroes(g, max(lod, 1.0)) : 0.0;
            float heroHeight = gLocalLightingHeight;
            if (gHeroOnly) return hero;
            if (g.y < _CloudAltitudeBounds.x || g.y > _CloudAltitudeBounds.y) return hero;
            float3 n1 = float3((g.xz - _CloudWindOffset) / 24000.0, 0.37);
            float3 n2 = float3((g.xz - _CloudWindOffset * 1.7) / 1250.0, g.y / 1250.0);
            float2 broad = tex3Dlod(_CloudNoiseTex, float4(n1, bodyLod)).rg;
            // Warp the meteorological boundary at cloud scale; otherwise a smooth
            // front map reads as a vertical polygon even when its rain band is curved.
            float4 weather, profile;
            float farShare;
            if (!SampleWeather(g.xz + (broad.rg - 0.5) * 2800.0, weather, profile, farShare)) return hero;
            float layer = weather.r, front = weather.g, cell = weather.b;
            if (max(layer, max(front, cell)) < 0.025) return hero;
            // The far map cannot resolve erosion detail; keep its envelope smooth.
            coarse = max(coarse, farShare);
            float baseY = profile.b * 16000.0 + _CloudHeightShift;
            float topY = max(baseY + 280.0, weather.a * 16000.0 + _CloudHeightShift);
            float frontBase = profile.r * 16000.0 + _CloudHeightShift;
            float frontCrown = max(frontBase + 300.0, profile.g * 16000.0 + _CloudHeightShift);
            float towerBlend = smoothstep(0.18, 0.60, _Anvil);
            float capTop = baseY + max(280.0, max(_PuffDepth, _LayerDepth)) * 1.15;
            float columnTop = max(baseY + 280.0, lerp(min(topY, capTop), topY, towerBlend));
            float deckTop = baseY + max(_PuffDepth, _LayerDepth) + _BaseWobble * 0.5;
            float lowFloor = front > 0.02 ? min(baseY, frontBase) : baseY;
            if (g.y < lowFloor - 700.0 - gVert || g.y > max(deckTop, max(columnTop, frontCrown)) + 600.0 + gVert) return hero;

            float smooth = saturate(_LayerSmooth);
            float sheetBlend = smoothstep(0.45, 0.85, smooth);
            bool sheet = sheetBlend >= 0.999;
            float3 gw = g + float3((broad.r - 0.5) * 1400.0, 0.0, (broad.g - 0.5) * 1400.0);
            float puffBody = 0.72;
            if (!sheet) puffBody = lerp(Bodies(gw, _PuffScale, puffLod), 0.72, sheetBlend);
            float relief = lerp(0.55, 1.0, smoothstep(0.2, 0.85, puffBody));
            float localAnvil = LocalAnvil(columnTop - baseY, _Anvil);
            float localTowerBlend = smoothstep(0.18, 0.60, localAnvil);
            columnTop = lerp(baseY + (columnTop - baseY) * relief, columnTop, max(sheetBlend, localTowerBlend));

            // Low deck. Each puff has its own base and a domed top; a smooth genus is one sheet.
            float layerShape = 0.0;
            float layerHeight = 0.0;
            if (layer > 0.02)
            {
                float deckBody = lerp(puffBody, 0.72 + 0.28 * puffBody, smooth);
                float mask = CoverMask(deckBody, saturate(layer * 1.2));
                float thick = max(280.0, lerp(_PuffDepth, max(_PuffDepth, _LayerDepth), sheetBlend));
                thick *= lerp(0.75 + 0.10 * sheetBlend, 1.0, broad.r);
                thick *= lerp(relief, 1.0, sheetBlend);
                float layerBase = baseY + (puffBody - 0.5) * lerp(_BaseWobble, _BaseWobble * 0.3, smooth);
                float layerTop = layerBase + thick;
                layerHeight = saturate((g.y - layerBase) / max(1.0, thick));
                if (gVert > 40.0)
                    layerShape = saturate(mask) * LayerOverlap(g.y, layerBase, layerTop) * lerp(0.7, 0.9, sheetBlend);
                else
                {
                    float hl = (g.y - layerBase) / max(1.0, thick);
                    float deckAnvil = LocalAnvil(thick, _Anvil) * (1.0 - smooth);
                    layerShape = ShapeMass(mask, ShapeProfile(hl, LocalDome(lerp(_Dome, _Dome * 0.25, smooth), deckAnvil), deckAnvil)) *
                        BaseGate(g.y, layerBase, lerp(_BaseSharp, _BaseSharp * 2.2, smooth));
                }
            }

            // Fronts use the same profile at a larger scale. A thin shield stays stratiform.
            float frontShape = 0.0;
            float frontHeight = 0.0;
            float frontDepth = frontCrown - frontBase;
            if (front > 0.02)
            {
                float fScale = lerp(max(1800.0, _PuffScale * 1.35), 5200.0, sheetBlend);
                float frontBody = Bodies(gw + 5311.0, fScale, sheet ? bodyLod : puffLod);
                float fm = CoverMask(frontBody, saturate(front * 1.05));
                float frontFloor = frontBase + (frontBody - 0.5) * min(220.0, _BaseWobble + 40.0);
                float frontTop = frontCrown + (broad.r - 0.5) * min(1200.0, frontDepth * 0.25) -
                    (1.0 - fm) * min(frontDepth * 0.25, 900.0);
                frontHeight = saturate((g.y - frontFloor) / max(1.0, frontTop - frontFloor));
                float uplift = smoothstep(3500.0, 6500.0, frontDepth);
                float thin = lerp(0.35, 1.0, smoothstep(900.0, 2200.0, frontDepth));
                if (gVert > 40.0)
                    frontShape = saturate(fm) * LayerOverlap(g.y, frontFloor, frontTop) * thin;
                else
                {
                    float fh = (g.y - frontFloor) / max(1.0, frontTop - frontFloor);
                    float frontAnvil = LocalAnvil(frontDepth, _Anvil);
                    frontShape = ShapeMass(fm, ShapeProfile(fh, LocalDome(lerp(0.25, _Dome, uplift), frontAnvil), frontAnvil)) *
                        BaseGate(g.y, frontFloor, max(_BaseSharp, 80.0)) * thin;
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
            float towerShape = inside * ShapeMass(carved, ShapeProfile(height01, LocalDome(_Dome, localAnvil), localAnvil)) *
                BaseGate(g.y, baseY, _BaseSharp) * smoothstep(0.04, 0.20, wide);

            // Detail noise only on the boundary. The interior of a deck is one mass, and an
            // empty sample never pays for the 3D taps.
            float coarseShape = max(layerShape, max(frontShape, towerShape));
            float edge = saturate(1.0 - abs(coarseShape - 0.42) * 2.4);
            float detail = 0.5;
            if (coarse < 1.0 && _Billow > 0.01 && edge > 0.08)
            {
                float d1 = tex3Dlod(_CloudNoiseTex, float4(n2, lod)).r;
                float nearDetail = saturate(1.0 - lod);
                // Cumulus edges take the rounded Worley lobes (B) for the near octave, so
                // boundaries read as cauliflower billows; sheets keep the stringy erosion (G).
                float2 d2n = nearDetail > 0.0
                    ? tex3Dlod(_CloudNoiseTex, float4(n2 * 3.1 + 0.21, lod)).gb : 0.5;
                float d2 = lerp(d2n.y, d2n.x, sheetBlend);
                detail = lerp(d1, d1 * 0.72 + d2 * 0.28, nearDetail);
                float close = saturate(1.0 - gFoot / 3.0);
                if (close > 0.0 && edge > 0.35)
                {
                    float micro = tex3Dlod(_CloudNoiseTex, float4(g.xz / 340.0 + 0.19, g.y / 300.0 + 0.83, 0.0)).g;
                    detail = lerp(detail, detail * 0.65 + micro * 0.35, close);
                }
            }
            // Erosion only removes the boundary; signed noise used to create detached wisps.
            // Heaped cloud is nibbled harder than sheets: crisp lobes instead of soft blobs.
            // GPU only; CloudBodies is the shape without boundary erosion.
            float nibble = max(0.0, lerp(0.72, 0.65, sheetBlend) - detail) * _Billow * edge * lerp(0.70, 0.45, sheetBlend);
            layerShape = saturate(layerShape - nibble);
            frontShape = saturate(frontShape - nibble);
            towerShape = saturate(towerShape - nibble);
            // Heaped cloud gets a steeper boundary ramp after erosion (GPU only, like the
            // nibble): lobes end in a crisp sunlit rim instead of a soft halo.
            float crisp = (1.0 - sheetBlend) * 0.5 * edge;
            layerShape = lerp(layerShape, saturate((layerShape - 0.08) * 1.6), crisp);
            towerShape = lerp(towerShape, saturate((towerShape - 0.08) * 1.6), crisp);
            float layerDensity = saturate((layerShape - 0.12) / 0.70) * smoothstep(0.02, 0.10, layer);
            float frontDensity = saturate((frontShape - 0.12) / 0.72) * smoothstep(0.04, 0.20, front);
            float towerDensity = towerShape;

            // Fractus (scud): ragged fragments hanging under rain-bearing bases.
            float scud = 0.0;
            float rain = profile.a;
            float floorY = lowFloor;
            if (rain > 0.02 && g.y < floorY && g.y > floorY - 700.0)
            {
                float frag = tex3Dlod(_CloudNoiseTex, float4(g.xz / 900.0 + 0.29, g.y / 420.0 + 0.61, lod)).r;
                float band = smoothstep(floorY - 700.0, floorY - 450.0, g.y) * (1.0 - smoothstep(floorY - 180.0, floorY, g.y));
                scud = smoothstep(0.62, 0.78, frag + rain * 0.35) * band * saturate(rain * 6.0);
            }
            float keep = EyeCloudKeep(g);
            float field = hero;
            gLocalLightingHeight = heroHeight;
            if (layerDensity * 0.55 * keep > field) { field = layerDensity * 0.55 * keep; gLocalLightingHeight = layerHeight; }
            if (frontDensity * 0.60 * keep > field) { field = frontDensity * 0.60 * keep; gLocalLightingHeight = frontHeight; }
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
                float cav = tex3Dlod(_CloudNoiseTex, float4(g.xz / 900.0 + 0.71, g.y / 760.0 + 0.29, LodFor(gFoot, 900.0))).g;
                float carveKeep = 1.0 - 0.7 * smoothstep(0.25, 0.6, field);
                field = saturate(field - (cav - 0.48) * 1.1 * carveFade * carveKeep);
            }
            if (field <= 0.0) gLocalLightingHeight = 0.0;
            return field;
        }

        // Density, or zero at once where the envelope and set-pieces rule cloud out.
        float DensityOrEmpty(float3 world, float coarse)
        {
            gLocalLightingHeight = 0.0;
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

        // A thin layer between y0 and y1: the middle (alto-) and high (cirro-) clouds, as a
        // 2D field at the layer's mid altitude. stretch > 1 draws streaks along the wind
        // (cirrus); ripple swaps in rounded cells (altocumulus, cirrocumulus); sheet
        // flattens it into a veil (-stratus). Two octaves at non-integer scales and a slow
        // warp keep any tile from repeating visibly.
        float Slab(float3 g, float cover, float sheet, float scale,
            float stretch, float ripple, float foot, float2 splitMul)
        {
            cover *= EyeCloudKeep(g);
            if (cover <= 0.005) return 0;
            // Mesoscale patches (a layer is never uniform across the whole map) and a slow warp
            // that bends the streaks (hooked cirrus) and breaks the tile period: one fetch.
            float2 low = tex3Dlod(_CloudNoiseTex, float4(g.xz / 27000.0 + 0.27, 0.71, 1.5)).rg;
            float patch = low.g;
            float2 warp = (low - 0.5) * scale * 1.6;
            float2 p = g.xz + warp;
            float2 w = float2(dot(p, _CloudWindDir), dot(p, float2(-_CloudWindDir.y, _CloudWindDir.x)));
            float along = scale * stretch;
            // Each pattern has two octaves; the finer one fades to its mean (0.52) once it is
            // filtered past what the pixel resolves, and is not fetched at all beyond that.
            // A pattern the layer does not use (ripple 0 or 1) is not fetched either.
            float streak = 0.52, cells = 0.52;
            if (ripple < 0.98)
            {
                float lod1 = LodFor(foot, scale), lod2 = LodFor(foot, scale * 1.73);
                float s1 = tex3Dlod(_CloudNoiseTex, float4(w.x / along, w.y / scale, g.y / (scale * 0.6) + 0.17, lod1)).r;
                float2 w2 = float2(w.x * 0.982 - w.y * 0.191, w.x * 0.191 + w.y * 0.982);
                float fine2 = saturate(4.0 - lod2);
                float s2 = fine2 > 0.0 ? lerp(0.52, tex3Dlod(_CloudNoiseTex, float4(w2.x / (along * 1.73) + 0.53,
                    w2.y / (scale * 1.73) + 0.29, g.y / (scale * 1.1) + 0.61, lod2)).r, fine2) : 0.52;
                streak = s1 * 0.6 + s2 * 0.4;
            }
            if (ripple > 0.02)
            {
                // Rounded cells from the smooth base channel, also at two scales.
                float cs = scale * 0.45;
                float lodC = LodFor(foot, cs);
                float c1 = tex3Dlod(_CloudNoiseTex, float4(p / cs + 0.41, g.y / (cs * 0.8), lodC)).r;
                float fineC = saturate(4.0 - lodC);
                float c2 = fineC > 0.0 ? lerp(0.52, tex3Dlod(_CloudNoiseTex, float4(float2(p.y, -p.x) / (cs * 2.37) + 0.13,
                    g.y / (cs * 1.9) + 0.77, LodFor(foot, cs * 2.37))).r, fineC) : 0.52;
                cells = c1 * 0.62 + c2 * 0.38;
            }
            // Cloud streets: cumuliform rows along the wind over ascending air.
            float street = 0.55 + 0.45 * sin(w.y / (scale * 0.85) * 6.2831853 + warp.x / scale * 2.0);
            float streetKeep = 1.0 - smoothstep(1.0, 3.0, LodFor(foot, scale * 0.85));
            cells *= lerp(1.0, street, 0.8 * streetKeep);
            float raw = lerp(streak, cells, ripple);
            float body = saturate(0.5 + (raw - 0.52) * 3.16);
            body = lerp(body, 0.7 + 0.3 * body, sheet);
            // Ahead of the front (share 0) and behind it (share 1) the layer differs.
            cover *= lerp(splitMul.x, splitMul.y, SplitShare(g.xz));
            float c = saturate(cover * (0.55 + 0.9 * saturate(0.5 + (patch - 0.52) * 3.16)));
            // 0.77: the layer's dome profile averaged through its depth.
            return CoverMask(body, c) * 0.77;
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
            float air = AirTransmittance(depth, ro.y, ro.y + ray.y * depth);
            layer.colour = lerp(_CloudFogColor * layer.opacity, layer.colour, air) * fade;
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

        // March one chord, split around the opaque cloud's representative distance.
        // Camera altitude alone cannot order a nearby storm against an oblique layer.
        // Each sample stands for the whole depth of the layer
        // over its stretch of chord, so grazing rays never slice it into bands; long chords
        // get more samples and blur the pattern to their step instead of aliasing.
        SlabLayers MarchSlab(float3 ro, float3 ray, float limit, float splitDistance, float y0, float y1, float cover,
            float sheet, float scale, float stretch, float ripple, float strength, float phase, float jitter,
            float2 splitMul)
        {
            SlabLayers result = (SlabLayers)0;
            // Inside the layer a level ray must not be skipped (a one-pixel gap at the
            // horizon) nor stretched to the horizon limit (ten samples over 1200 km drew a
            // bright speckled line); 60 km of layer is already opaque.
            bool insideSlab = ro.y > y0 && ro.y < y1;
            if (cover <= 0.005 || (!insideSlab && abs(ray.y) < 0.0005)) return result;
            float slope = abs(ray.y) < 0.0005 ? (ray.y < 0.0 ? -0.0005 : 0.0005) : ray.y;
            float ta = (y0 - ro.y) / slope, tb = (y1 - ro.y) / slope;
            float t0 = max(0.0, min(ta, tb)), t1 = min(max(ta, tb), insideSlab ? min(limit, 60000.0) : limit);
            if (t1 <= t0) return result;
            float span = t1 - t0;
            // A steep chord through a thin layer is one sample (a 2D texture, in effect);
            // grazing chords get up to ten.
            float samples = clamp(ceil(span / 6000.0), 1.0, 10.0);
            float stepLength = span / samples;
            float slant = length(ray.xz);
            float patternFoot = stepLength * 0.5 * slant;
            float yMid = (y0 + y1) * 0.5;
            float toSun = (y1 - y0) * 0.5 / max(0.12, _CloudSunDirection.y);
            [loop]
            for (int n = 0; n < 10; n++)
            {
                if (n >= samples) break;
                float t = t0 + (n + jitter) * stepLength;
                float3 g = ro + ray * t;
                g.y = yMid;
                float d = Slab(g, cover, sheet, scale, stretch, ripple,
                    max(t * _CloudPixelAngle, patternFoot), splitMul) * strength;
                if (d <= 0.002) continue;
                // Thin cloud: its own depth toward the sun is all that shades it.
                float direct = exp(-d * toSun * 0.003);
                float3 light = _CloudAmbientColor * 0.8 * 0.8 +
                    _CloudSunColor * (direct * phase * 0.6 + 0.22);
                float segmentStart = t0 + n * stepLength;
                float frontLength = clamp(splitDistance - segmentStart, 0.0, stepLength);
                float backLength = stepLength - frontLength;
                if (frontLength > 0.0)
                    AddSlabSample(result.front, d * frontLength * 0.003, light,
                        segmentStart + frontLength * jitter);
                if (backLength > 0.0)
                    AddSlabSample(result.back, d * backLength * 0.003, light,
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
                float frontShare = weather.g / max(0.001, weather.r + weather.g + weather.b);
                float baseY = lerp(profile.b, profile.r, saturate(frontShare)) * 16000.0 + _CloudHeightShift;
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

                float coverShade = 1.0 - 0.75 * saturate(max(weather.r, max(weather.g, weather.b)));
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
                        optical *= 0.0021;
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
                        float scattered = 0.22 * exp(-optical * 0.24) * secondaryPhase * secondaryAccess;
                        // Shade is lit by blue sky, not grey: a cool tint keeps sunlit lobes and
                        // shadowed lobes apart, like the reference flight footage.
                        float3 light = _CloudAmbientColor * float3(0.90, 0.97, 1.10) * skyAccess * 0.72 +
                            _CloudGroundColor * (1.0 - h) * exp(-d) +
                            _CloudSunColor * (direct * phase * 0.75 + scattered);
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
                            float3 wispN = tex3Dlod(_CloudNoiseTex, float4(wg.xz / 1200.0 + 0.47, wg.y / 1000.0 + 0.13, LodFor(gFoot, 1200.0))).rgb;
                            float wisp = wispN.x * 0.45 + wispN.y * 0.20 + wispN.z * 0.35;
                            // Smaller ragged filaments supply parallax as the aircraft crosses
                            // the volume. Filter them out before a march interval can alias them.
                            float filamentFade = (1.0 - smoothstep(600.0, 1800.0, t)) *
                                (1.0 - smoothstep(20.0, 70.0, gFoot));
                            if (filamentFade > 0.01)
                            {
                                float filament = tex3Dlod(_CloudNoiseTex, float4(wg.xz / 360.0 + 0.19,
                                    wg.y / 300.0 + 0.67, LodFor(gFoot, 360.0))).g;
                                wisp += (filament - 0.5) * 0.55 * filamentFade;
                            }
                            light *= 1.0 + (wisp - 0.5) * 1.1 * (0.5 + saturate(d)) * wispFade;
                        }
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
                        float sampleAir = AirTransmittance(t, ro.y, ro.y + ray.y * t);
                        colour += contribution * lerp(_CloudFogColor, light, sampleAir);
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
                float hn = tex3Dlod(_CloudNoiseTex, float4(hp / 52000.0 + 0.31, 0.47, 3.0)).r * 0.62 +
                    tex3Dlod(_CloudNoiseTex, float4(float2(hp.y, -hp.x) / 141000.0 + 0.77, 0.19, 3.5)).r * 0.38;
                float hBody = saturate(0.5 + (hn - 0.52) * 3.16);
                float hCover = CoverMask(hBody, _HorizonCover * (1.0 - _SplitB.x * (1.0 - SplitShare(hp))));
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
            float midBase = lerp(4200.0, 3300.0, _MidSheet);
            float midTop = midBase + lerp(400.0, 1700.0, _MidSheet);
            float highBase = 8600.0;
            float highTop = highBase + lerp(500.0, 900.0, _HighVeil);
            SlabLayers fog = MarchSlab(ro, ray, slabLimit, cloudDistance, -30.0, 330.0, _FogBank,
                0.85, 3200.0, 1.6, 0.0, 0.55, phase, jitter, float2(1.0, 1.0));
            SlabLayers middle = MarchSlab(ro, ray, slabLimit, cloudDistance, midBase, midTop, _MidCover,
                smoothstep(0.4, 1.0, _MidSheet), 2600.0, 1.4, 0.7 * (1.0 - _MidSheet), 0.42, phase, jitter,
                float2(1.0 - 0.75 * _SplitB.x, 1.0));
            SlabLayers high = MarchSlab(ro, ray, slabLimit, cloudDistance, highBase, highTop, _HighCover,
                smoothstep(0.6, 1.0, _HighVeil), 5000.0, lerp(7.0, 1.0, _HighVeil),
                4.0 * _HighVeil * (1.0 - _HighVeil), 0.12, phase, jitter,
                float2(1.0 + 0.4 * _SplitB.x, 1.0 - 0.3 * _SplitB.x));
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
