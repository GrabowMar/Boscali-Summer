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
        sampler3D _CloudNoiseTex;
        float3 _CloudWorldOffset, _CloudCameraForward, _CloudSunDirection, _CloudCameraPos;
        float3 _CloudSunColor, _CloudAmbientColor, _CloudGroundColor, _CloudFogColor;
        float2 _CloudWindOffset, _CloudAltitudeBounds, _CloudHeroBounds;
        float _CloudSteps, _CloudFarSteps, _CloudStorm, _WeatherMapSpan, _WeatherFarSpan;
        float _CloudBase, _CloudHeightShift;
        float _CloudAirExtinction;
        // Cloud genera: low-deck depth/smoothness, middle and high layers.
        float _LayerDepth, _LayerSmooth, _MidCover, _MidSheet, _HighCover, _HighVeil;
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
        float _HeroCount;
        // Frontal boundary (SkySplit): A = normal x, normal z, offset, width;
        // B = amount, meander amplitude, wavelength, phase.
        float4 _SplitA, _SplitB;
        // 0..1 how deep the camera is inside cloud: near-field density and wisps.
        float _CameraInCloud;
        // Reduced-resolution march: world view rays through the screen corners (rows:
        // bottom-left, bottom-right, top-left, top-right) and the target size (w, h, 1/w, 1/h).
        float4x4 _CloudFrustum;
        float4 _CloudLowResSize;

        // Pixel footprint of the current march sample in metres: noise is filtered to it,
        // and to the step length, so sparse samples never pick out single speckles.
        static float gFoot = 0.0;
        // Half the altitude a far march step spans: thin layers average over it instead of
        // being hit or missed by whole steps (the source of banding on distant decks).
        static float gVert = 0.0;
        // Set-pieces this ray passes near; the rest are never evaluated along it.
        static uint gHeroMask = 0u;

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

        // Individual cloud bodies: cellular noise at two non-integer scales (so the 64^3
        // tile never visibly repeats), in [0, 1]. Coverage thresholds it into separate
        // clouds with sky between them, the way broken and scattered decks really look.
        float Bodies(float3 g, float scale, float lod)
        {
            float a = tex3Dlod(_CloudNoiseTex, float4(g.xz / scale, g.y / (scale * 0.9), lod)).r;
            float b = tex3Dlod(_CloudNoiseTex, float4(float2(g.z, -g.x) / (scale * 2.73) + 0.37,
                g.y / (scale * 2.1), lod)).r;
            // The blend clusters around 0.52 (measured p10 0.39, p90 0.64); stretch it to a
            // near-uniform [0, 1] so a cover of 0.4 really leaves 60 % of the sky open.
            return saturate(0.5 + (a * 0.62 + b * 0.38 - 0.52) * 3.16);
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

        // Cauliflower erosion for the set-pieces: two octaves of the base channel.
        float Billow(float3 g, float lod)
        {
            float a = tex3Dlod(_CloudNoiseTex, float4(g.xz / 1500.0 + 0.43, g.y / 1300.0 + 0.11, lod)).r;
            float b = tex3Dlod(_CloudNoiseTex, float4(g.xz / 560.0 + 0.71, g.y / 480.0 + 0.57, lod + 0.5)).r;
            return saturate(0.5 + (a * 0.65 + b * 0.35 - 0.52) * 3.0);
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
            float n = HeroNoise(g, 6000.0, lod);
            float n2 = HeroNoise(g + 911.0, 2200.0, lod);
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
            float tiers = 0.62 + 0.38 * sin((g.y + (n - 0.5) * 900.0 + v * 0.08) * 0.0285);
            float shelf = inShelf * lerp(1.0, tiers, smoothstep(0.25, 0.9, f)) *
                smoothstep(0.3, 0.52, n2 * 0.5 + n * 0.5 + 0.08);
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
            float n = HeroNoise(g, 5000.0, lod);
            float n2 = HeroNoise(g + 1733.0, 1800.0, lod);
            float height = saturate(g.y / b.y);
            float2 lean = dir * height * 4000.0;
            float radius = b.x * (0.8 + 0.4 * n) * (1.0 - 0.25 * smoothstep(0.6, 1.0, height));
            float tower = (1.0 - smoothstep(radius * 0.7, radius * 1.15, length(d - lean))) *
                smoothstep(1100.0, 1450.0, g.y) * (1.0 - smoothstep(b.y - 900.0, b.y + 600.0 * n, g.y));
            float along = dot(d, dir) - b.w * 0.35, across = dot(d, float2(-dir.y, dir.x));
            float e = length(float2(along * 0.55, across)) / b.w;
            float pouch = pow(saturate(n2 * 1.6 - 0.45), 2.0);
            float anvilBase = b.y - 2200.0 + (n2 - 0.5) * 300.0 - 700.0 * pouch;
            // The anvil thins toward its downwind edge and its top rounds off.
            float anvil = (1.0 - smoothstep(0.55 + 0.2 * n, 1.0 + 0.2 * n, e)) *
                smoothstep(anvilBase - 50.0, anvilBase + 220.0, g.y) * (1.0 - smoothstep(b.y - 500.0, b.y + 300.0, g.y));
            // The anvil is a thin, fibrous ice sheet; the tower a lumpy heap.
            return max(Erode(tower * 0.9, g, lod), anvil * 0.34 * (0.55 + 0.45 * n2)) * b.z;
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
            return max(Erode(max(wall, bands * 0.8), g, lod), floorCloud * 0.6) * b.z;
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
                    best = max(best, 1.0 - lens);
                }
            }
            return smoothstep(0.0, 0.3, best) * 0.7 * b.z;
        }

        float Heroes(float3 g, float lod)
        {
            float hero = 0.0;
            [loop]
            for (int k = 0; k < 5; k++)
            {
                if ((gHeroMask & (1u << k)) == 0u) continue;
                float kind = _HeroA[k].w;
                float h = kind < 0.5 ? ShelfLine(g, _HeroA[k], _HeroB[k], lod)
                    : kind < 1.5 ? Supercell(g, _HeroA[k], _HeroB[k], lod)
                    : kind < 2.5 ? StormEye(g, _HeroA[k], _HeroB[k], lod)
                    : Lenticulars(g, _HeroA[k], _HeroB[k]);
                hero = max(hero, h);
            }
            return hero;
        }

        float Density(float3 world, float coarse)
        {
            float3 g = world + _CloudWorldOffset;
            // Filter the 1250 m detail to what one pixel and one step can resolve.
            float lod = max(coarse, LodFor(gFoot, 1250.0));
            float bodyLod = max(coarse, LodFor(gFoot, 4200.0));
            float hero = gHeroMask != 0u ? Heroes(g, max(lod, 1.0)) : 0.0;
            if (g.y < _CloudAltitudeBounds.x || g.y > _CloudAltitudeBounds.y) return hero;
            float3 n1 = float3((g.xz - _CloudWindOffset) / 5800.0, g.y / 3900.0);
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
            float topY = max(baseY + 1600.0, weather.a * 16000.0 + _CloudHeightShift);
            float frontBase = profile.r * 16000.0 + _CloudHeightShift;
            float frontCrown = max(frontBase + 600.0, profile.g * 16000.0 + _CloudHeightShift);
            float height01 = saturate((g.y - baseY) / max(1.0, topY - baseY));
            if (g.y < min(baseY, frontBase) - 250.0 - gVert || g.y > max(topY, frontCrown) + 1500.0 + gVert) return hero;
            // Sun samples need the envelope, not the high-frequency erosion.
            float detail = 0.5;
            if (coarse < 1.0)
            {
                float d1 = tex3Dlod(_CloudNoiseTex, float4(n2, lod)).r;
                // A finer octave only where it can be seen.
                float nearDetail = saturate(1.0 - lod);
                float d2 = nearDetail > 0.0
                    ? tex3Dlod(_CloudNoiseTex, float4(n2 * 3.1 + 0.21, lod)).g : 0.5;
                float fine = lerp(d1, d1 * 0.72 + d2 * 0.28, nearDetail);
                // Within a couple of km the cloud is made of wisps that stream past.
                float close = saturate(1.0 - gFoot / 3.0);
                if (close > 0.0)
                {
                    float micro = tex3Dlod(_CloudNoiseTex, float4(g.xz / 340.0 + 0.19, g.y / 300.0 + 0.83, 0.0)).g;
                    fine = lerp(fine, fine * 0.65 + micro * 0.35, close);
                }
                // Wispy, torn bases and rounded, billowing tops.
                float lift = saturate((g.y - baseY) / 350.0);
                detail = lerp(lerp(1.0 - fine, fine, lift), 0.5, coarse);
            }
            float baseFlat = smoothstep(-100.0, 170.0, g.y - baseY);

            // Layer (stratocumulus / broken deck): separate domed bodies. Each body's top
            // rises with its own strength, its base is gently lumpy, and detail erodes edges.
            // Warp by the broad noise and use a larger scale than the 64^3 tile's period,
            // so no regular grid of puffs shows toward the horizon.
            float3 gw = g + float3((broad.r - 0.5) * 2200.0, 0.0, (broad.g - 0.5) * 2200.0);
            float body = Bodies(gw, 4200.0, bodyLod);
            // Smooth decks (stratus, nimbostratus) keep a gentle texture instead of bodies.
            float deckBody = lerp(body, 0.72 + 0.28 * body, _LayerSmooth);
            float layerMaskBody = CoverMask(deckBody, saturate(layer * 1.1));
            float layerThick = max(300.0, _LayerDepth) * (0.55 + 0.45 * saturate(layer));
            float layerBase = baseY + (body - 0.5) * lerp(160.0, 60.0, _LayerSmooth);
            float layerTopY = layerBase + layerThick *
                lerp(0.35 + 0.65 * layerMaskBody, 0.88 + 0.12 * layerMaskBody, _LayerSmooth);
            float layerShape;
            if (gVert > 40.0)
            {
                // A far step spans more altitude than the deck is thick: use the deck's
                // vertical average (its dome profile integrates to mask * 1.35 - 0.37).
                layerShape = saturate(layerMaskBody * 1.35 - 0.37) * LayerOverlap(g.y, layerBase, layerTopY);
            }
            else
            {
                float hl = saturate((g.y - layerBase) / max(1.0, layerTopY - layerBase));
                layerShape = saturate(layerMaskBody * 1.35 - hl * hl * 1.1) *
                    smoothstep(-60.0, 120.0, g.y - layerBase) * (1.0 - smoothstep(layerTopY - 20.0, layerTopY + 60.0, g.y));
            }
            float layerDensity = saturate((layerShape - (1.0 - detail) * 0.30) / 0.70) *
                smoothstep(0.02, 0.10, layer);

            // Fronts: the same bodies at a larger scale and much denser cover, so the band
            // is a rolled, lumpy mass with ragged edges rather than a plate.
            float frontDepth = frontCrown - frontBase;
            float frontBody = Bodies(gw + 5311.0, 5200.0, bodyLod);
            float frontMaskBody = CoverMask(frontBody, saturate(front * 1.05));
            float frontTop = frontCrown + (broad.r - 0.5) * min(2200.0, frontDepth * 0.45) -
                (1.0 - frontMaskBody) * min(1600.0, frontDepth * 0.35);
            float frontFloor = frontBase + (frontBody - 0.5) * 220.0;
            float frontProfile = gVert > 40.0 ? LayerOverlap(g.y, frontFloor, frontTop)
                : smoothstep(-120.0, 220.0, g.y - frontFloor) *
                  (1.0 - smoothstep(frontTop - min(600.0, frontDepth * 0.25), frontTop + 250.0, g.y));
            float frontHeight = saturate((g.y - frontBase) / max(600.0, frontDepth));
            // Deep frontal uplift resolves into connected lobes and cell towers;
            // a thin warm-front shield keeps its stratiform shape.
            float uplift = smoothstep(3500.0, 6500.0, frontDepth) * smoothstep(0.15, 0.55, frontHeight);
            float lobes = smoothstep(0.28 + frontHeight * 0.20, 0.68 + frontHeight * 0.12,
                max(cell, broad.r * 0.85 + broad.g * 0.30));
            frontProfile *= lerp(1.0, lobes, uplift);
            // A thin shield (the high leading veil of a warm front) is translucent.
            float frontShape = frontMaskBody * frontProfile * lerp(0.3, 1.0, smoothstep(900.0, 2200.0, frontDepth));
            float frontDensity = saturate((frontShape - (1.0 - detail) * 0.26) / 0.74) *
                smoothstep(0.04, 0.20, front);

            // Towers keep the weather field's shape; noise only erodes their boundary.
            float erode = (broad.r - 0.5) * 0.25 + (detail - 0.5) * 0.10;
            float towerThreshold = 0.25 + 0.43 * height01 * height01;
            float towerFootprint = smoothstep(towerThreshold - 0.16,
                towerThreshold + 0.16, cell + erode);
            float towerProfile = baseFlat * (1.0 - smoothstep(0.84, 1.0, height01));
            float cellSupport = smoothstep(0.04, 0.20, cell);
            float towerDensity = towerFootprint * towerProfile * (0.76 + broad.r * 0.24) * cellSupport;
            float anvil = smoothstep(0.68, 0.83, height01) *
                (1.0 - smoothstep(0.91, 1.0, height01)) *
                smoothstep(0.18, 0.46, cell + erode) * cellSupport;
            // Fractus (scud): ragged fragments hanging under rain-bearing bases.
            float scud = 0.0;
            float rain = profile.a;
            float floorY = min(baseY, frontBase);
            if (rain > 0.02 && g.y < floorY && g.y > floorY - 700.0)
            {
                float frag = tex3Dlod(_CloudNoiseTex, float4(g.xz / 900.0 + 0.29, g.y / 420.0 + 0.61, lod)).r;
                float band = smoothstep(floorY - 700.0, floorY - 450.0, g.y) * (1.0 - smoothstep(floorY - 180.0, floorY, g.y));
                scud = smoothstep(0.62, 0.78, frag + rain * 0.35) * band * saturate(rain * 6.0);
            }
            return max(max(hero, scud * 0.4), max(layerDensity * 0.52, max(frontDensity * 0.58,
                max(towerDensity * 0.78, anvil * 0.35))));
        }

        float AirTransmittance(float distanceToSample, float cameraHeight, float sampleHeight)
        {
            // Integral of an exponential aerosol layer along the viewing ray.
            float h0 = max(0.0, cameraHeight) / 2200.0;
            float h1 = max(0.0, sampleHeight) / 2200.0;
            float delta = h1 - h0;
            float average = abs(delta) < 0.01 ? exp(-h0) : (exp(-h0) - exp(-h1)) / delta;
            float extinction = 0.000008 + max(0.0, _CloudAirExtinction - 0.000008) * average;
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
            // Mesoscale patches: a layer is never uniform across the whole map.
            float patch = tex3Dlod(_CloudNoiseTex, float4(g.xz / 23000.0 + 0.61, 0.33, 2.0)).r;
            // A slow warp bends the streaks (hooked cirrus) and breaks the tile period.
            float2 warp = (tex3Dlod(_CloudNoiseTex, float4(g.xz / 31000.0 + 0.27, 0.71, 1.0)).rg - 0.5) * scale * 1.6;
            float2 p = g.xz + warp;
            float2 w = float2(dot(p, _CloudWindDir), dot(p, float2(-_CloudWindDir.y, _CloudWindDir.x)));
            float along = scale * stretch;
            float s1 = tex3Dlod(_CloudNoiseTex, float4(w.x / along, w.y / scale,
                g.y / (scale * 0.6) + 0.17, LodFor(foot, scale))).r;
            float2 w2 = float2(w.x * 0.982 - w.y * 0.191, w.x * 0.191 + w.y * 0.982);
            float s2 = tex3Dlod(_CloudNoiseTex, float4(w2.x / (along * 1.73) + 0.53, w2.y / (scale * 1.73) + 0.29,
                g.y / (scale * 1.1) + 0.61, LodFor(foot, scale * 1.73))).r;
            float streak = s1 * 0.6 + s2 * 0.4;
            // Rounded cells from the smooth base channel, also at two scales.
            float cs = scale * 0.45;
            float c1 = tex3Dlod(_CloudNoiseTex, float4(p / cs + 0.41, g.y / (cs * 0.8), LodFor(foot, cs))).r;
            float c2 = tex3Dlod(_CloudNoiseTex, float4(float2(p.y, -p.x) / (cs * 2.37) + 0.13,
                g.y / (cs * 1.9) + 0.77, LodFor(foot, cs * 2.37))).r;
            float cells = c1 * 0.62 + c2 * 0.38;
            // Cloud streets: cumuliform rows along the wind over ascending air.
            float street = 0.55 + 0.45 * sin(w.y / (scale * 0.85) * 6.2831853 + warp.x / scale * 2.0);
            cells *= lerp(1.0, street, 0.8);
            float raw = lerp(streak, cells, ripple);
            float body = saturate(0.5 + (raw - 0.52) * 3.16);
            body = lerp(body, 0.7 + 0.3 * body, sheet);
            // Ahead of the front (share 0) and behind it (share 1) the layer differs.
            cover *= lerp(splitMul.x, splitMul.y, SplitShare(g.xz));
            float c = saturate(cover * (0.55 + 0.9 * saturate(0.5 + (patch - 0.52) * 3.16)));
            // 0.77: the layer's dome profile averaged through its depth.
            return CoverMask(body, c) * 0.77;
        }

        // Marches one slab along its chord of the view ray and returns premultiplied colour
        // (rgb) and transmittance (a). Each sample stands for the whole depth of the layer
        // over its stretch of chord, so grazing rays never slice it into bands; long chords
        // get more samples and blur the pattern to their step instead of aliasing.
        float4 MarchSlab(float3 ro, float3 ray, float limit, float y0, float y1, float cover,
            float sheet, float scale, float stretch, float ripple, float strength, float phase, float jitter,
            float2 splitMul)
        {
            if (cover <= 0.005 || abs(ray.y) < 0.0005) return float4(0, 0, 0, 1);
            float ta = (y0 - ro.y) / ray.y, tb = (y1 - ro.y) / ray.y;
            float t0 = max(0.0, min(ta, tb)), t1 = min(max(ta, tb), limit);
            if (t1 <= t0) return float4(0, 0, 0, 1);
            float span = t1 - t0;
            // A steep chord through a thin layer is one sample (a 2D texture, in effect);
            // grazing chords get up to ten.
            float samples = clamp(ceil(span / 6000.0), 1.0, 10.0);
            float stepLength = span / samples;
            float yMid = (y0 + y1) * 0.5;
            float toSun = (y1 - y0) * 0.5 / max(0.12, _CloudSunDirection.y);
            float3 colour = 0.0;
            float transmittance = 1.0;
            [loop]
            for (int n = 0; n < 10; n++)
            {
                if (n >= samples) break;
                float t = t0 + (n + jitter) * stepLength;
                float3 g = ro + ray * t;
                g.y = yMid;
                float d = Slab(g, cover, sheet, scale, stretch, ripple,
                    max(t * _CloudPixelAngle, stepLength * 0.25), splitMul) * strength;
                if (d <= 0.002) continue;
                // Thin cloud: its own depth toward the sun is all that shades it.
                float direct = exp(-d * toSun * 0.003);
                float3 light = _CloudAmbientColor * 0.8 * 0.8 +
                    _CloudSunColor * (direct * phase * 0.6 + 0.22);
                float absorb = exp(-d * stepLength * 0.003);
                colour += transmittance * (1.0 - absorb) * light;
                transmittance *= absorb;
            }
            float mid = t0 + span * 0.5;
            float fadeOut = 1.0 - smoothstep(CLOUD_HORIZON_FADE, CLOUD_HORIZON_LIMIT, mid);
            colour *= fadeOut;
            transmittance = lerp(1.0, transmittance, fadeOut);
            float air = AirTransmittance(mid, ro.y, ro.y + ray.y * mid);
            colour = lerp(_CloudFogColor * (1.0 - transmittance), colour, air);
            return float4(colour, transmittance);
        }

        float SunOpticalDepth(float3 world, float farPass)
        {
            // Three increasingly coarse intervals resolve nearby lobes and the thicker cloud
            // behind them; at range one coarse interval is indistinguishable. Extinction
            // uses distance in metres.
            float optical;
            if (farPass <= 0.0)
            {
                optical = Density(world + _CloudSunDirection * 150.0, 1.0) * 300.0 +
                    Density(world + _CloudSunDirection * 650.0, 1.1) * 700.0 +
                    Density(world + _CloudSunDirection * 1900.0, 1.8) * 1800.0;
            }
            else optical = Density(world + _CloudSunDirection * 1500.0, 1.8) * 3000.0;
            return optical * 0.0021;
        }

        float Phase(float cosine, float g)
        {
            // Henyey-Greenstein, normalised to an isotropic value of one.
            return (1.0 - g * g) / pow(max(0.08, 1.0 + g * g - 2.0 * g * cosine), 1.5);
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

        // The whole sky along one view ray: premultiplied colour and opacity.
        // origin: local camera position; ray: unit view direction; sceneDistance: metres to the
        // first opaque surface (or CLOUD_HORIZON_LIMIT for sky); pixel: screen pixel (dither).
        float4 MarchSky(float3 origin, float3 ray, float sceneDistance, float2 pixel)
        {
            float3 ro = origin + _CloudWorldOffset;
            float2 bounds = _CloudAltitudeBounds;
            float sceneLimit = min(sceneDistance, CLOUD_FAR_LIMIT);
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

            float transmittance = 1.0;
            float3 colour = 0.0;
            float weightedDistance = 0.0;
            float cosine = dot(ray, _CloudSunDirection);
            // Forward scattering, a sharp silver-lining lobe at the rims toward the
            // sun, and some back scatter.
            float phase = 0.60 * Phase(cosine, 0.58) + 0.12 * Phase(cosine, 0.88) + 0.28 * Phase(cosine, -0.25);
            float awayFromSun = saturate(0.5 - 0.5 * cosine);
            // Stable spatial dither (interleaved gradient noise) breaks coherent marching
            // bands without any temporal history, ghosting, or camera-motion dependency.
            float jitter = lerp(0.1, 0.9, frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715)))));

            // Two segments: full detail to the near limit, then a coarse march through the
            // far level of detail out to the horizon.
            [loop]
            for (int segment = 0; segment < 2; segment++)
            {
                float farPass = (float)segment;
                float s0 = segment == 0 ? start : max(start, CLOUD_NEAR_LIMIT);
                float s1 = segment == 0 ? min(finish, CLOUD_NEAR_LIMIT) : finish;
                if (finish <= start || s1 <= s0 || transmittance < 0.025) continue;
                float span = s1 - s0;
                float steps = segment == 0 ? _CloudSteps : _CloudFarSteps;
                if (segment == 0 && s0 > 25000.0) steps *= 0.68;
                // A short span needs fewer steps than a long one.
                steps = max(8.0, floor(steps * saturate(span / 12000.0 + 0.35)));
                [loop]
                for (int n = 0; n < 96; n++)
                {
                    if (n >= steps || transmittance < 0.025) break;
                    float a = n / steps, b = (n + 1.0) / steps;
                    float stepLength = span * (b * b - a * a);
                    float t = s0 + span * lerp(a * a, b * b, jitter);
                    float3 world = origin + ray * t;
                    gFoot = max(t * _CloudPixelAngle, stepLength * 0.35);
                    gVert = segment == 0 ? 0.0 : 0.5 * abs(ray.y) * stepLength;
                    float d = Density(world, farPass * 1.2);
                    if (d > 0.003)
                    {
                        float optical = SunOpticalDepth(world, farPass);
                        float h = saturate((world.y + _CloudWorldOffset.y - _CloudBase) / 6000.0);
                        float direct = exp(-optical);
                        // Powder: seen away from the sun, dense cores stay darker than
                        // their sunlit rims (the cauliflower look of real cumulus).
                        float powder = 1.0 - exp(-d * 6.0);
                        direct *= lerp(1.0, powder * 1.35, awayFromSun * 0.6);
                        // One broad secondary-scattering lobe retains detail in thick
                        // shade without flattening it to a constant minimum colour.
                        float scattered = 0.18 * exp(-optical * 0.24);
                        // Sky light reaches a point only through the cloud above it, so
                        // bases go dark and tops stay bright. Far away the height says enough.
                        float above = segment == 0 ? Density(world + float3(0.0, 380.0, 0.0), 1.2) : d * (1.0 - h);
                        float skyAccess = lerp(0.32, 1.0, h) * exp(-d * 0.65) * exp(-above * 1.6);
                        float3 light = _CloudAmbientColor * skyAccess * 0.72 +
                            _CloudGroundColor * (1.0 - h) * exp(-d) +
                            _CloudSunColor * (direct * phase * 0.65 + scattered);
                        // Inside cloud, droplets limit visibility to tens or hundreds of
                        // metres: the cloud right around the camera is far denser.
                        float nearField = 1.0 + 7.0 * _CameraInCloud * exp(-t / 450.0);
                        float absorb = exp(-d * stepLength * 0.003 * nearField);
                        float contribution = transmittance * (1.0 - absorb);
                        colour += contribution * light;
                        weightedDistance += contribution * t;
                        transmittance *= absorb;
                    }
                }
            }
            gVert = 0.0;
            float opacity = 1.0 - transmittance;
            float cloudDistance = weightedDistance / max(0.0001, opacity);
            float air = AirTransmittance(cloudDistance, ro.y, ro.y + ray.y * cloudDistance);
            // Opacity-weighted cloud depth puts airlight in front of the visible
            // cloud surface, with one atmospheric integral per pixel.
            colour = lerp(_CloudFogColor * opacity, colour, air);

            // Horizon deck: past the march the weather continues as one analytic plane at
            // the deck's altitude, with the far map's own outer coverage, so the clouds
            // run on to the horizon instead of ending in a ring.
            float horizonT = abs(ray.y) > 0.0002 ? (_HorizonDeck - ro.y) / ray.y : -1.0;
            if (_HorizonCover > 0.01 && horizonT > CLOUD_FAR_LIMIT * 0.75 && horizonT < min(sceneDistance, CLOUD_HORIZON_LIMIT))
            {
                float2 hp = ro.xz + ray.xz * horizonT;
                float hn = tex3Dlod(_CloudNoiseTex, float4(hp / 52000.0 + 0.31, 0.47, 3.0)).r * 0.62 +
                    tex3Dlod(_CloudNoiseTex, float4(float2(hp.y, -hp.x) / 141000.0 + 0.77, 0.19, 3.5)).r * 0.38;
                float hBody = saturate(0.5 + (hn - 0.52) * 3.16);
                float hCover = CoverMask(hBody, _HorizonCover * (1.0 - _SplitB.x * (1.0 - SplitShare(hp))));
                float hPath = min(_HorizonDepth / max(0.015, abs(ray.y)), 150000.0);
                float hOpacity = hCover * (1.0 - exp(-hPath * 0.00003)) *
                    smoothstep(CLOUD_FAR_LIMIT * 0.75, CLOUD_FAR_LIMIT, horizonT) *
                    (1.0 - smoothstep(CLOUD_HORIZON_FADE, CLOUD_HORIZON_LIMIT, horizonT));
                bool below = ro.y < _HorizonDeck;
                float3 hLight = below
                    ? _CloudAmbientColor * 0.5 + _CloudGroundColor * 0.25 + _CloudSunColor * 0.06
                    : _CloudAmbientColor * 0.8 + _CloudSunColor * (phase * 0.35 + 0.2);
                float hAir = AirTransmittance(horizonT, ro.y, _HorizonDeck);
                float3 hColour = lerp(_CloudFogColor * hOpacity, hLight * hOpacity, hAir);
                colour += (1.0 - opacity) * hColour;
                opacity += (1.0 - opacity) * hOpacity;
            }

            // Middle (3.3-5.5 km) and high (8.6-9.5 km) layers and the fog bank. A layer the
            // camera is above lies in front of the low cloud; one it is below lies behind it
            // and is skipped when the low cloud is already opaque. The fog bank (0-330 m) is
            // under every cloud base, so it is behind them unless the camera is in it.
            float slabLimit = min(sceneDistance, CLOUD_HORIZON_LIMIT);
            float transmit = 1.0 - opacity;
            float midBase = lerp(4200.0, 3300.0, _MidSheet);
            float midTop = midBase + lerp(400.0, 1700.0, _MidSheet);
            float highBase = 8600.0;
            float highTop = highBase + lerp(500.0, 900.0, _HighVeil);
            if (ro.y < 330.0 || transmit > 0.02)
            {
                float4 fogLayer = MarchSlab(ro, ray, slabLimit, -30.0, 330.0, _FogBank,
                    0.85, 3200.0, 1.6, 0.0, 0.55, phase, jitter, float2(1.0, 1.0));
                if (ro.y < 330.0) { colour = fogLayer.rgb + fogLayer.a * colour; }
                else { colour += transmit * fogLayer.rgb; }
                transmit *= fogLayer.a;
            }
            if (ro.y > midTop || transmit > 0.02)
            {
                float4 midLayer = MarchSlab(ro, ray, slabLimit, midBase, midTop, _MidCover,
                    smoothstep(0.4, 1.0, _MidSheet), 2600.0, 1.4, 0.7 * (1.0 - _MidSheet), 0.42, phase, jitter,
                    float2(1.0 - 0.75 * _SplitB.x, 1.0));
                if (ro.y > midTop) { colour = midLayer.rgb + midLayer.a * colour; }
                else { colour += transmit * midLayer.rgb; }
                transmit *= midLayer.a;
            }
            if (ro.y > highTop || transmit > 0.02)
            {
                float4 highLayer = MarchSlab(ro, ray, slabLimit, highBase, highTop, _HighCover,
                    smoothstep(0.6, 1.0, _HighVeil), 5000.0, lerp(7.0, 1.0, _HighVeil),
                    4.0 * _HighVeil * (1.0 - _HighVeil), 0.12, phase, jitter,
                    float2(1.0 + 0.4 * _SplitB.x, 1.0 - 0.3 * _SplitB.x));
                if (ro.y > highTop) { colour = highLayer.rgb + highLayer.a * colour; }
                else { colour += transmit * highLayer.rgb; }
                transmit *= highLayer.a;
            }
            return float4(colour, 1.0 - transmit);
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
                float4 sky = MarchSky(origin, ray, SceneDistance(rawDepth, dot(ray, -UNITY_MATRIX_V[2].xyz)), i.pos.xy);
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
                float2 uv = i.pos.xy * _CloudLowResSize.zw;
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
                o.colour = MarchSky(_CloudCameraPos, ray, sceneDistance, i.pos.xy);
                o.depth = float4(eyeDepth, 0.0, 0.0, 0.0);
                return o;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
