Shader "Boscali/FlightCloudComposite"
{
    // Draws the reduced-resolution cloud target over the scene at the volume's render queue,
    // so water, cloud and vanilla smoke keep their order. A joint bilateral upsample: each
    // pixel blends the nine nearest cloud texels against the nearest relevant depth, so
    // opaque foreground stays clear and terrain behind visible cloud cannot cut holes in it.
    Properties
    {
        [HideInInspector] _CloudLowResColour ("Cloud colour", 2D) = "black" { }
        [HideInInspector] _CloudLowResDepth ("Cloud depth", 2D) = "black" { }
        [HideInInspector] _CloudQuarterColour ("Fresh colour", 2D) = "black" { }
        [HideInInspector] _CloudQuarterData ("Fresh depth", 2D) = "black" { }
        [HideInInspector] _CloudHistoryTex ("History colour", 2D) = "black" { }
        [HideInInspector] _CloudHistoryDepth ("History depth", 2D) = "black" { }
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Pass
        {
            Name "CloudComposite"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _CameraDepthTexture;
            sampler2D _CloudLowResColour;
            sampler2D _CloudLowResDepth;
            float4 _CloudLowResSize;

            // The curved Earth seen from high up (HighAltitudeSky): x camera height above the sea (m), y fade 0..1 (0 below 12 km),
            // z column optical depth (the scene fog density times the height, so the far plane meets the backdrop in the same haze), w planet radius.
            float4 _EarthView;
            float4 _EarthHaze; // rgb: the fog colour, linear
            float4 _EarthSun;  // xyz: toward the sun
            float4 _EarthSea;  // rgb: the ocean's deep colour under this light, linear (OceanDressing.CalmDeep)

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
                o.world = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                return o;
            }

            // Kasten-Young air mass for a zenith cosine (38 at the horizon).
            float AirMass(float mu)
            {
                mu = saturate(mu);
                return 1.0 / (mu + 0.50572 * pow(max(96.07995 - degrees(acos(mu)), 0.01), -1.6364));
            }

            // Daylight on ground whose up has cosine mu with the sun, relative to the camera's own ground: 1 across a sunlit theatre (so the
            // haze meets the fogged sea at the far plane), darker past the terminator, up to twice as bright toward a dawn the camera has not seen yet.
            float DayRatio(float mu)
            {
                return clamp(lerp(0.12, 1.0, smoothstep(-0.12, 0.08, mu)) / lerp(0.12, 1.0, smoothstep(-0.12, 0.08, _EarthSun.y)), 0.1, 2.0);
            }

            // Past the far plane from high up the skybox drew black below the horizon and the flat world just ended (2026-10-28). The planet
            // instead, premultiplied: a sphere of radius _EarthView.w under the camera, its sea seen through the column haze (air mass at the
            // ground point times _EarthView.z), and above its horizon the limb: the tangent path's Rayleigh glow, white at the rim, deep blue above
            // it, brighter toward the sun and orange along the terminator, translucent so the stars show through.
            float4 EarthBeyond(float3 ray)
            {
                float h = max(_EarthView.x, 1.0), R = _EarthView.w, tau0 = _EarthView.z;
                float3 o = float3(0.0, R + h, 0.0);
                float3 sun = _EarthSun.xyz;
                float B = (R + h) * ray.y;
                float near = h * (2.0 * R + h);
                float disc = B * B - near;
                float3 haze = _EarthHaze.rgb;
                if (B < 0.0 && disc >= 0.0)
                {
                    float t = near / (-B + sqrt(disc));
                    float3 n = normalize(o + ray * t);
                    float T = exp(-tau0 * AirMass(dot(n, -ray)));
                    // Sun glint (2026-11-06): a wind-roughened sea seen from orbit shows a broad bright patch around the sun's mirror point.
                    float3 hv = normalize(sun - ray);
                    float nh = saturate(dot(n, hv)), nv = max(dot(n, -ray), 0.05), a2 = 0.04;
                    float dd = nh * nh * (a2 - 1.0) + 1.0;
                    float fres = 0.02 + 0.98 * pow(1.0 - saturate(dot(-ray, hv)), 5.0);
                    float glint = min(a2 / (3.14159 * dd * dd) * fres / (4.0 * nv), 6.0) * saturate(dot(n, sun));
                    float3 glintColour = float3(1.0, 0.93, 0.80) * glint * 0.35 * T * T;
                    return float4((_EarthSea.rgb * T + haze * (1.0 - T)) * DayRatio(dot(n, sun)) + glintColour, 1.0);
                }
                // Tangent height of a ray over the limb (the camera's own height for a ray going up) and its path through the air in scale heights:
                // the Chapman integral from the camera on, sqrt(2 pi R / H) = 70.7 for the whole pass, half of it for a level ray (erf ~ tanh(1.2 x),
                // sqrt(2 R H) = 319 km); an upward ray takes the air mass, scaled to meet the level value. A faint 25 km tail stands in for multiple scattering.
                bool grazing = B < 0.0;
                float tangent = grazing ? sqrt(max(0.0, (R + h) * (R + h) - B * B)) - R : h;
                float path = grazing ? 35.35 * (1.0 + tanh(1.2 * -B / 319000.0)) : 0.93 * AirMass(ray.y);
                float tau = tau0 * path * (exp(-tangent / 8000.0) + 0.05 * exp(-tangent / 25000.0));
                // The tangent point (closest approach to the planet's centre): the sun there lights this stretch of limb, not the camera's own ground,
                // so from high up at dusk the limb toward the sun still glows while the ground below is dark (2026-10-28: lit by the local fog colour,
                // which vanilla darkens at dusk, the whole limb went black). The hue stays the fog's at a clear day's brightness; a near-black fog
                // (night) carries no hue, so a plain sky blue stands in.
                float mu = dot(normalize(grazing ? o - ray * B : o), sun);
                float lum = dot(haze, float3(0.2126, 0.7152, 0.0722));
                float3 day = lerp(float3(0.22, 0.42, 0.85), haze * (0.45 / max(lum, 0.02)), smoothstep(0.02, 0.12, lum));
                float3 glow = day * 1.25 * (1.0 - exp(-tau * float3(0.45, 1.0, 2.2))) * smoothstep(-0.1, 0.1, mu) * (1.0 + 1.5 * pow(saturate(dot(ray, sun)), 6.0));
                float dusk = exp(-mu * mu / 0.005) * smoothstep(-0.2, 0.0, mu);
                glow = lerp(glow, dot(glow, float3(0.3, 0.5, 0.2)) * float3(1.7, 0.75, 0.32), dusk * 0.6);
                return float4(glow, 1.0 - exp(-tau));
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
                // A silhouette pixel (a ridge, a tree, a mast against the sky) is part sky after anti-aliasing, but its depth says ground, so the cloud behind it
                // was rejected and the clear-sky colour baked into it stayed: a bright outline a few pixels wide around everything in front of a dark cloud.
                // Where a neighbour 1.5 px away is far behind this pixel, composite the cloud against that neighbour's depth at part strength.
                float2 rimStep = 1.5 / _ScreenParams.xy;
                float behind = max(
                    max(LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv + float2(rimStep.x, 0.0))), LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv - float2(rimStep.x, 0.0)))),
                    max(LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv + float2(0.0, rimStep.y))), LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv - float2(0.0, rimStep.y)))));
                bool rim = behind > depth * 1.5 + 50.0;
                float ownDepth = depth;
                if (rim) depth = behind;
                // The 3x3 cloud texels around this pixel: a Gaussian over distance (which also
                // melts the march's per-texel dither) times a depth match.
                float2 p = uv * _CloudLowResSize.xy - 0.5;
                float2 centre = floor(p + 0.5);
                // Distant cloud is drawn soft: past 15-60 km the kernel widens to the whole 3x3 (about two texels of blur), which melts
                // the far march's coarse steps that otherwise read as hard horizontal slices along the horizon.
                float far = smoothstep(15000.0, 60000.0, min(depth, tex2Dlod(_CloudLowResDepth, float4((centre + 0.5) * _CloudLowResSize.zw, 0, 0)).r));
                // 2026-10-18: near 0.45 -> 0.35, so the kernel spans the 2x2 block whose texels march the four quarters of a step (FlightCloud's stratified dither).
                // High up the clouds are FlightCloud's orbit deck (2026-11-06), a baked texture with no march grain: keep its detail sharp.
                float orbit = _EarthView.y > 0.0 ? smoothstep(16000.0, 32000.0, _EarthView.x) : 0.0;
                float spread = lerp(0.35, 0.15, far * (1.0 - orbit));
                float4 c = 0.0;
                float total = 0.0, depthSum = 0.0;
                // The tap nearest in depth, for a pixel no tap matches within 10 %, and the taps' depth range.
                float4 nearest = 0.0;
                float nearestDifference = 1e9, nearestDepth = depth, tapNear = 1e20, tapFar = 0.0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        // Taps stay one texel apart: spread two apart (bilinear colour, point depth) they disagreed along depth edges and
                        // drew distant cloud as 2-texel blocks on the sea horizon.
                        float2 texel = centre + float2(x, y);
                        float2 tuv = (texel + 0.5) * _CloudLowResSize.zw;
                        float2 offset = texel - p;
                        // 0.45: about one texel of blur. At 1.1 the kernel was so narrow that the dither of nearby cloud came through as speckle.
                        float spatial = exp(-dot(offset, offset) * spread);
                        float texelDepth = tex2Dlod(_CloudLowResDepth, float4(tuv, 0, 0)).r;
                        float4 sample = tex2Dlod(_CloudLowResColour, float4(tuv, 0, 0));
                        // Only a cloud that hides what is behind it may ignore that depth. A thin sample cut short by terrain is not the sky beside it:
                        // accepted there, it thinned the cloud over the sky along every ridge and the clear sky glowed through as a blue outline.
                        // Empty foreground samples retain their scene depth and must still
                        // be rejected completely, or they dilute the sky into a pale outline.
                        float pixelDepth = sample.a > 0.97 ? min(depth, texelDepth) : depth;
                        float difference = abs(texelDepth - pixelDepth) / max(1.0, min(texelDepth, pixelDepth));
                        float w = difference < 0.1 ? spatial / (0.02 + difference) : 0.0;
                        c += sample * w;
                        total += w;
                        depthSum += min(texelDepth, pixelDepth) * w;
                        if (difference < nearestDifference) { nearestDifference = difference; nearest = sample; nearestDepth = min(texelDepth, pixelDepth); }
                        tapNear = min(tapNear, texelDepth);
                        tapFar = max(tapFar, texelDepth);
                    }
                }
                // Near the horizon a grazing surface changes depth by more than 10 % from one row to the next (sea 72 km under sky at the
                // 80 km far plane, 61 km a row lower), so the first sea row matched no tap, was discarded and drew the sea unhazed: thin bright
                // segments along the horizon under storm decks (2026-10-19). Take the nearest tap where the pixel lies inside the taps' depth
                // range (a surface running on through it); a silhouette in front of every tap (an aircraft, a mast) still rejects the cloud behind.
                if (total <= 1e-5 && depth >= tapNear && depth <= tapFar) { c = nearest; total = 1.0; depthSum = nearestDepth; }
                c /= max(1e-5, total);
                // Only cloud behind the silhouette is thinned (2026-10-18): cloud in front of the ridge covers it whole. Thinned everywhere, every
                // ridge hidden in cloud, or seen from inside it, showed through as a thin line.
                if (rim)
                {
                    float cloudDepth = depthSum / max(1e-5, total);
                    c *= lerp(0.65, 1.0, saturate((ownDepth - cloudDepth) / max(1.0, ownDepth * 0.1)));
                }
                // The planet goes behind the cloud in sky pixels, and the last stretch before the far plane fades into it: the clipped sea's edge
                // drew a thin bright line where the world met the backdrop (2026-10-28).
                float beyond = ownDepth >= _ProjectionParams.z * 0.98 ? 1.0 : smoothstep(_ProjectionParams.z * 0.88, _ProjectionParams.z * 0.98, ownDepth);
                // From high up the map's own sea (the ocean disc, its macro tint, its fog) and the backdrop's sea past the far plane disagreed in
                // colour and drew bands across the view (2026-11-06, user screenshot from ~50 km). Sea pixels (a surface within 3 m of sea
                // level) take the backdrop's sea too, with the orbit deck's fade, so the planet is one sea out to the limb.
                if (orbit > 0.0 && beyond < 1.0)
                {
                    float3 viewRay = normalize(i.world - _WorldSpaceCameraPos);
                    float along = ownDepth / max(dot(viewRay, -UNITY_MATRIX_V[2].xyz), 0.02);
                    float surfaceHeight = _EarthView.x + viewRay.y * along;
                    beyond = max(beyond, orbit * (1.0 - smoothstep(1.0, 3.0, surfaceHeight)));
                }
                if (_EarthView.y > 0.0 && beyond > 0.0)
                {
                    float4 earth = EarthBeyond(normalize(i.world - _WorldSpaceCameraPos)) * (_EarthView.y * beyond);
                    c += (1.0 - c.a) * earth;
                }
                if (c.a < 0.002) discard;
                return c;
            }
            ENDHLSL
        }

        // Temporal resolve (drawn by the render pass only): the half-size sky from this frame's
        // quarter-size march. The three texels of each 2x2 block not marched this frame are
        // last frame's sky, reprojected along the cloud's distance and clamped to the range
        // of the fresh texels around them, so a cloud that moved or appeared never leaves a
        // ghost. The marched texel blends toward that carried value instead of replacing it,
        // so the per-frame march dither averages out instead of drawing a checkerboard grid
        // on thin cloud. Where last frame did not see the point, the fresh texels are
        // upsampled instead.
        Pass
        {
            Name "CloudResolve"
            Tags { "LightMode"="BoscaliCloudResolve" }
            Blend Off
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex resolveVert
            #pragma fragment resolveFrag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _CameraDepthTexture;
            sampler2D _CloudQuarterColour;
            sampler2D _CloudQuarterData;
            sampler2D _CloudHistoryTex;
            sampler2D _CloudHistoryDepth;
            float4 _CloudLowResSize, _CloudQuarterSize;
            float4x4 _CloudFrustum, _CloudPrevMatrix;
            float4 _CloudCamDelta;
            float4 _CloudChecker;
            float _CloudHistoryValid;
            float _CloudFlashChange;
            float _CloudBlastChange;
            float _CloudBlastCount;
            float3 _CloudCameraGlobal;
            float4 _CloudBlastA[4], _CloudBlastB[4];

            float BlastRayChange(float3 ray, float sceneDistance)
            {
                if (_CloudBlastChange < 0.5) return 0.0;
                [loop] for (int index = 0; index < min(4, (int)_CloudBlastCount); index++)
                {
                    float3 delta = _CloudBlastA[index].xyz - _CloudCameraGlobal;
                    float along = clamp(dot(delta, ray), 0.0, sceneDistance);
                    float3 closest = delta - ray * along;
                    float reach = _CloudBlastA[index].w + _CloudBlastB[index].y;
                    if (dot(closest, closest) < reach * reach) return 1.0;
                }
                return 0.0;
            }

            struct rv2f { float4 pos : SV_POSITION; };
            struct ResolveOut
            {
                float4 colour : SV_Target0;
                float4 depth : SV_Target1;
            };

            rv2f resolveVert(uint id : SV_VertexID)
            {
                rv2f o;
                float2 p = float2((id << 1) & 2, id & 2);
                o.pos = float4(p * 2.0 - 1.0, 0.5, 1.0);
                return o;
            }

            float3 ViewRay(float2 uv)
            {
                return lerp(lerp(_CloudFrustum[0].xyz, _CloudFrustum[1].xyz, uv.x),
                    lerp(_CloudFrustum[2].xyz, _CloudFrustum[3].xyz, uv.x), uv.y);
            }

            // Depth-tested bilinear history at a target position. Bilinear history must
            // reject foreground neighbours; testing only one depth texel would still let
            // their empty colour bleed through interpolation.
            float4 HistoryAt(float2 position, float pointDepth, out float weight, out float cloudEye)
            {
                float2 historyPixel = position * _CloudLowResSize.xy - 0.5;
                float2 historyBase = floor(historyPixel), fraction = frac(historyPixel);
                float4 history = 0.0;
                float cloudSum = 0.0, cloudWeight = 0.0;
                weight = 0.0;
                [unroll]
                for (int y = 0; y <= 1; y++)
                {
                    [unroll]
                    for (int x = 0; x <= 1; x++)
                    {
                        float2 huv = (historyBase + float2(x, y) + 0.5) * _CloudLowResSize.zw;
                        float2 depths = tex2Dlod(_CloudHistoryDepth, float4(huv, 0, 0)).rg;
                        float depth = min(depths.r, pointDepth);
                        float difference = abs(depth - pointDepth) / max(1.0, min(depth, pointDepth));
                        if (difference >= 0.1) continue;
                        float w = (x == 0 ? 1.0 - fraction.x : fraction.x) *
                            (y == 0 ? 1.0 - fraction.y : fraction.y);
                        float4 colour = tex2Dlod(_CloudHistoryTex, float4(huv, 0, 0));
                        history += colour * w;
                        float visible = colour.a > 0.002 ? colour.a * w : 0.0;
                        cloudSum += depths.g * visible;
                        cloudWeight += visible;
                        weight += w;
                    }
                }
                cloudEye = cloudWeight > 1e-5 ? cloudSum / cloudWeight : pointDepth;
                return weight > 1e-5 ? history / weight : 0.0;
            }

            ResolveOut Resolved(float4 colour, float sceneDepth, float cloudEye)
            {
                ResolveOut o;
                o.colour = colour;
                // R keeps the established upsample/reprojection rejection. G is independent:
                // the actual cloud eye distance even for translucent layers.
                o.depth = float4(colour.a > 0.97 ? min(sceneDepth, cloudEye) : sceneDepth, cloudEye, 0, 0);
                return o;
            }

            ResolveOut resolveFrag(rv2f i)
            {
                float2 texel = floor(i.pos.xy);
                float2 uv = (texel + 0.5) * _CloudLowResSize.zw;
                float2 block = floor(texel * 0.5);
                float2 quv = (block + 0.5) * _CloudQuarterSize.zw;
                float sceneDepth = LinearEyeDepth(tex2Dlod(_CameraDepthTexture, float4(uv, 0, 0)).r);
                float3 view = ViewRay(uv);
                float viewLength = length(view);
                float blastMotion = BlastRayChange(view / viewLength, sceneDepth * viewLength);

                bool fresh = all(texel - block * 2.0 == _CloudChecker.xy);
                float4 freshVal = tex2Dlod(_CloudQuarterColour, float4(quv, 0, 0));
                float2 freshData = tex2Dlod(_CloudQuarterData, float4(quv, 0, 0)).rg;
                // A checker sample may hit the aircraft while this half-resolution pixel
                // sees sky. Reconstruct only from fresh samples on the same side of depth.
                float4 upsampled = 0.0, low = 1e5, high = -1e5, soft = 0.0;
                float total = 0.0, softTotal = 0.0, distanceSum = 0.0, distanceWeight = 0.0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 sampleBlock = block + float2(x, y);
                        float2 sampleUv = (sampleBlock + 0.5) * _CloudQuarterSize.zw;
                        float2 data = tex2Dlod(_CloudQuarterData, float4(sampleUv, 0, 0)).rg;
                        float4 n = tex2Dlod(_CloudQuarterColour, float4(sampleUv, 0, 0));
                        float2 marchUv = (sampleBlock * 2.0 + _CloudChecker.xy + 0.5) * _CloudLowResSize.zw;
                        float cloudEye = n.a > 0.97 ? data.g / length(ViewRay(marchUv)) : 1e20;
                        float sampleDepth = min(data.r, cloudEye), pixelDepth = min(sceneDepth, cloudEye);
                        float difference = abs(sampleDepth - pixelDepth) / max(1.0, min(sampleDepth, pixelDepth));
                        if (difference >= 0.1) continue;
                        float2 offset = sampleBlock * 2.0 + _CloudChecker.xy - texel;
                        float w = exp(-dot(offset, offset) * 0.275) / (0.02 + difference);
                        upsampled += n * w;
                        // Wider kernel for texels with no usable history (disocclusion behind a
                        // moving aircraft): a soft fill instead of quarter-resolution blocks.
                        float ws = exp(-dot(offset, offset) * 0.09) / (0.02 + difference);
                        soft += n * ws;
                        softTotal += ws;
                        // Empty rays carry a far fallback, not a cloud point. Weight
                        // depth by visible opacity so sky cannot drag edge parallax away.
                        float cloudWeight = n.a > 0.002 ? n.a * w : 0.0;
                        distanceSum += data.g * cloudWeight;
                        distanceWeight += cloudWeight;
                        total += w;
                        low = min(low, n);
                        high = max(high, n);
                    }
                }
                upsampled /= max(1e-5, total);
                soft /= max(1e-5, softTotal);
                // A distant fresh sample must use the same spatial filtering at rest and
                // in motion; raw refreshes every fourth frame exposed the march dither.
                float4 stableFresh = lerp(freshVal, upsampled,
                    0.7 * smoothstep(15000.0, 45000.0, freshData.g));
                bool historyUsable = _CloudHistoryValid > 0.5 && _CloudChecker.w > 0.5;
                // Frozen sky: the march is deterministic per texel, so with the camera at rest every texel is final once marched.
                if (historyUsable && blastMotion < 0.5 && _CloudCamDelta.w < 0.001 && dot(_CloudCamDelta.xyz, _CloudCamDelta.xyz) < 1e-4)
                {
                    if (fresh) return Resolved(stableFresh, sceneDepth, freshData.g / viewLength);
                    ResolveOut parked;
                    parked.colour = tex2Dlod(_CloudHistoryTex, float4(uv, 0, 0));
                    parked.depth = tex2Dlod(_CloudHistoryDepth, float4(uv, 0, 0));
                    return parked;
                }
                // No fresh neighbour on this texel's side of a depth edge (sky beside the terrain horizon) this phase: keep its own
                // history instead of drawing it empty, which blinked the horizon line every checker cycle.
                if (total <= 1e-5)
                {
                    float keptWeight = 0.0, keptEye = sceneDepth;
                    float4 kept = 0.0;
                    if (historyUsable) kept = HistoryAt(uv, sceneDepth, keptWeight, keptEye);
                    ResolveOut retained = Resolved(keptWeight > 1e-5 ? kept : 0.0, sceneDepth, sceneDepth);
                    // Preserve the established R edge fallback; G must follow the retained visible colour.
                    retained.depth.g = keptEye;
                    return retained;
                }
                float distance = distanceWeight > 1e-5 ? distanceSum / distanceWeight : freshData.g;
                // One marched texel is one dithered sample: on its own it is grain. The nine-sample blend around it is the same sky with the dither averaged out.
                float4 smooth = upsampled;
                if (fresh) { upsampled = stableFresh; distance = freshData.g; }
                float cloudDepth = distance / viewLength;
                // No history (first frame, a cut, a zoom): fresh texels are sharp, the rest
                // fall back to the upsampled march. Sharpness returns over the next frames.
                if (_CloudHistoryValid < 0.5 || _CloudChecker.w < 0.5) return Resolved(lerp(upsampled, smooth, 0.5), sceneDepth, cloudDepth);

                // This texel's ray, out to the cloud distance its fresh neighbour measured,
                // seen from last frame's camera.
                float3 ray = view / viewLength;
                float4 clip = mul(_CloudPrevMatrix, float4(ray * distance + _CloudCamDelta.xyz, 1.0));
                float2 previous = clip.xy / clip.w * 0.5 + 0.5;
                float4 fallback = fresh ? lerp(upsampled, smooth, 0.5) : soft;
                if (clip.w <= 0.0 || any(previous < 0.0) || any(previous > 1.0)) return Resolved(fallback, sceneDepth, cloudDepth);
                // clip.w is this cloud point's previous eye depth. A changed terrain distance
                // behind it cannot invalidate the visible cloud.
                float pointDepth = upsampled.a > 0.002 ? clip.w : sceneDepth;
                float historyWeight, historyEye;
                float4 history = HistoryAt(previous, pointDepth, historyWeight, historyEye);
                // Sky streaming out from the point the camera flies toward was hidden behind
                // the followed aircraft last frame: every frame those texels lost history and
                // drew single-frame fallback, a fan of radial streaks from the aircraft. Use
                // the accumulated history at this texel's own position instead (a few pixels
                // of lag) and refresh it harder below.
                bool occludedHistory = false;
                if (historyWeight <= 1e-5)
                {
                    history = HistoryAt(uv, pointDepth, historyWeight, historyEye);
                    occludedHistory = true;
                }
                if (historyWeight <= 1e-5) return Resolved(fallback, sceneDepth, cloudDepth);
                float4 boxed = clamp(history, low, high);
                // A single representative depth cannot reproject all the material in a
                // nearby volume. Reduce history when translation is large relative to it.
                // Distant skies keep the same temporal savings and accumulation.
                float motion = max(saturate(length(_CloudCamDelta.xyz) / max(4.0, distance * 0.08)), _CloudCamDelta.w);
                // Repeated bilinear history resampling softens moving features. Refresh in
                // proportion to their movement on this target, retaining accumulation at rest.
                // 0.5 here threw half the accumulated sky away on every frame of ordinary flight, which showed the raw dither as boiling grain.
                motion = max(motion, 0.2 * saturate(length((previous - uv) * _CloudLowResSize.xy)));
                // Fresh neighbours expose a local flash promptly without clearing sky history.
                motion = max(motion, saturate(_CloudFlashChange * 12.0));
                motion = max(motion, blastMotion);
                if (occludedHistory) motion = max(motion, 0.5);
                // The clamp box comes from the nine quarter-size texels marched this frame, one of every four half-size texels. A cloud thinner than that (a far layer seen edge-on) is
                // inside the box only on the frames whose marched row happens to cross it: clamped hard, it blinked on and off along the horizon. A sky that barely moves on screen
                // keeps most of its history (a 15 % pull toward the box per frame still clears a cloud that has really gone); a moving one is clamped as before.
                // Distant cloud is cached harder: past the near limit (45 km, fully by 120 km)
                // a texel covers kilometres of sky, so each fresh sample disagrees with the
                // last far more than the cloud changed. History then yields only 4 % toward
                // the clamp box and a fresh texel enters at 3 % per visit, while motion and
                // lightning (in `motion`) still force a prompt refresh.
                float farCache = smoothstep(45000.0, 120000.0, distance);
                // Frozen sky: the march dither is fixed per texel, so a texel marched again from the same view returns the same value.
                // `rest` holds history for a sky that barely moves on screen; `carried` blends only when motion demands it.
                // Blending toward neighbours marched in a different checker phase made a 4-frame wobble, so a moving view keeps
                // the distance-weighted cache below instead of snapping a fresh quarter.
                float rest = occludedHistory ? 0.0 : 1.0 - saturate(motion * 40.0);
                float4 carried = lerp(history, boxed, saturate(lerp(0.15, 0.04, farCache) + motion * 3.0) * (1.0 - rest));
                if (!fresh) return Resolved(lerp(carried, upsampled, motion), sceneDepth, cloudDepth);
                // Moving sky: the clamp already pulled the carried value into the fresh range, so new and vanished cloud still
                // converge within a few frames. A fresh texel enters half as its own sample and half as the blend around it,
                // at most a quarter per visit (distant cloud 3 %).
                float entry = max(lerp(0.08 + 0.17 * freshVal.a, 0.03, farCache), motion);
                // A still camera is handled by the frozen branch above. Under a slow drift the old
                // 1.0 snap replaced one checker quarter outright, so the four phases alternated the
                // sky's brightness (4-frame shimmer measured 2026-11-18). Every moving view keeps
                // the distance-weighted cache instead.
                float restEntry = lerp(0.25, 0.08, farCache);
                // 2026-10-18: a moving fresh texel enters as 0.3 its own sample and 0.7 the stratified nine-sample blend (was half and half): its single sample is the grain.
                return Resolved(lerp(carried, lerp(lerp(freshVal, smooth, 0.7), stableFresh, rest), lerp(entry, restEntry, rest)), sceneDepth, cloudDepth);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
