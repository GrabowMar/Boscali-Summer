Shader "Boscali/FlightCloud"
{
    Properties { _CloudDensity ("Density", Range(0,2)) = 1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Front
        Pass
        {
            Name "CloudBody"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _CameraDepthTexture;
            sampler2D _WeatherMapTex;
            sampler2D _WeatherProfileTex;
            sampler2D _WeatherFarMapTex;
            sampler2D _WeatherFarProfileTex;
            sampler3D _CloudNoiseTex;
            float3 _CloudWorldOffset, _CloudCameraForward, _CloudSunDirection;
            float3 _CloudSunColor, _CloudAmbientColor, _CloudGroundColor, _CloudFogColor;
            float2 _CloudWindOffset, _CloudAltitudeBounds;
            float _CloudSteps, _CloudFarSteps, _CloudStorm, _WeatherMapSpan, _WeatherFarSpan;
            float _CloudBase, _CloudHeightShift;
            float _CloudAirExtinction;

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

            // Near march limit: beyond it the far map's level of detail takes over.
            #define CLOUD_NEAR_LIMIT 45000.0
            #define CLOUD_FAR_LIMIT 220000.0

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

            float Density(float3 world, float coarse)
            {
                float3 g = world + _CloudWorldOffset;
                if (g.y < _CloudAltitudeBounds.x || g.y > _CloudAltitudeBounds.y) return 0.0;
                float lod = max(coarse, clamp((distance(world, _WorldSpaceCameraPos.xyz) - 12000.0) / 14000.0, 0.0, 2.0));
                float3 n1 = float3((g.xz - _CloudWindOffset) / 5800.0, g.y / 3900.0);
                float3 n2 = float3((g.xz - _CloudWindOffset * 1.7) / 1250.0, g.y / 1250.0);
                float2 broad = tex3Dlod(_CloudNoiseTex, float4(n1, lod)).rg;
                // Warp the meteorological boundary at cloud scale; otherwise a smooth
                // front map reads as a vertical polygon even when its rain band is curved.
                float4 weather, profile;
                float farShare;
                if (!SampleWeather(g.xz + (broad.rg - 0.5) * 2800.0, weather, profile, farShare)) return 0.0;
                float layer = weather.r, front = weather.g, cell = weather.b;
                if (max(layer, max(front, cell)) < 0.025) return 0.0;
                // The far map cannot resolve erosion detail; keep its envelope smooth.
                coarse = max(coarse, farShare);
                float baseY = profile.b * 16000.0 + _CloudHeightShift;
                float topY = max(baseY + 1600.0, weather.a * 16000.0 + _CloudHeightShift);
                float frontBase = profile.r * 16000.0 + _CloudHeightShift;
                float frontCrown = max(frontBase + 600.0, profile.g * 16000.0 + _CloudHeightShift);
                float height01 = saturate((g.y - baseY) / max(1.0, topY - baseY));
                if (g.y < min(baseY, frontBase) - 250.0 || g.y > max(topY, frontCrown) + 1500.0) return 0.0;
                // Sun samples need the envelope, not the high-frequency erosion.
                float detail = coarse >= 1.0 ? 0.5 : lerp(tex3Dlod(_CloudNoiseTex, float4(n2, lod)).r, 0.5, coarse);
                float baseFlat = smoothstep(-100.0, 170.0, g.y - baseY);

                // Layer (stratocumulus / broken deck): separate domed bodies. Each body's top
                // rises with its own strength, its base is gently lumpy, and detail erodes edges.
                float body = Bodies(g, 2600.0, lod);
                float layerMaskBody = CoverMask(body, saturate(layer * 1.1));
                float layerThick = 450.0 + 1300.0 * saturate(layer);
                float layerBase = baseY + (body - 0.5) * 160.0;
                float layerTopY = layerBase + layerThick * (0.35 + 0.65 * layerMaskBody);
                float hl = saturate((g.y - layerBase) / max(1.0, layerTopY - layerBase));
                float layerShape = saturate(layerMaskBody * 1.35 - hl * hl * 1.1) *
                    smoothstep(-60.0, 120.0, g.y - layerBase) * step(g.y, layerTopY + 50.0);
                float layerDensity = saturate((layerShape - (1.0 - detail) * 0.30) / 0.70) *
                    smoothstep(0.02, 0.10, layer);

                // Fronts: the same bodies at a larger scale and much denser cover, so the band
                // is a rolled, lumpy mass with ragged edges rather than a plate.
                float frontDepth = frontCrown - frontBase;
                float frontBody = Bodies(g + 5311.0, 3600.0, lod);
                float frontMaskBody = CoverMask(frontBody, saturate(front * 1.05));
                float frontTop = frontCrown + (broad.r - 0.5) * min(2200.0, frontDepth * 0.45) -
                    (1.0 - frontMaskBody) * min(1600.0, frontDepth * 0.35);
                float frontProfile = smoothstep(-120.0, 220.0, g.y - (frontBase + (frontBody - 0.5) * 220.0)) *
                    (1.0 - smoothstep(frontTop - min(600.0, frontDepth * 0.25), frontTop + 250.0, g.y));
                float frontHeight = saturate((g.y - frontBase) / max(600.0, frontDepth));
                // Deep frontal uplift resolves into connected lobes and cell towers;
                // a thin warm-front shield keeps its stratiform shape.
                float uplift = smoothstep(3500.0, 6500.0, frontDepth) * smoothstep(0.15, 0.55, frontHeight);
                float lobes = smoothstep(0.28 + frontHeight * 0.20, 0.68 + frontHeight * 0.12,
                    max(cell, broad.r * 0.85 + broad.g * 0.30));
                frontProfile *= lerp(1.0, lobes, uplift);
                float frontShape = frontMaskBody * frontProfile;
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
                return max(layerDensity * 0.52, max(frontDensity * 0.58,
                    max(towerDensity * 0.78, anvil * 0.35)));
            }

            float SunOpticalDepth(float3 world, float farPass)
            {
                // Four increasingly coarse intervals resolve nearby lobes and the
                // thicker cloud behind them. Extinction uses distance in metres.
                // Distant cloud needs only the two coarse ones: at range the fine
                // intervals are indistinguishable.
                float optical = 0.0;
                if (farPass <= 0.0)
                {
                    optical += Density(world + _CloudSunDirection * 120.0, 0.0) * 240.0;
                    optical += Density(world + _CloudSunDirection * 460.0, 1.1) * 440.0;
                }
                optical += Density(world + _CloudSunDirection * 1150.0, 1.6) * (farPass > 0.0 ? 1620.0 : 940.0);
                optical += Density(world + _CloudSunDirection * 2600.0, 2.0) * 1960.0;
                return optical * 0.0021;
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

            float Phase(float cosine, float g)
            {
                // Henyey-Greenstein, normalised to an isotropic value of one.
                return (1.0 - g * g) / pow(max(0.08, 1.0 + g * g - 2.0 * g * cosine), 1.5);
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float3 origin = _WorldSpaceCameraPos.xyz;
                float3 ray = normalize(i.world - origin);
                float3 ro = origin + _CloudWorldOffset;
                float3 rd = ray;
                float3 safeDir = rd + (1.0 - abs(sign(rd))) * 1e-6;
                float3 inv = 1.0 / safeDir;
                float3 lower = float3(-_WeatherFarSpan * 0.5, _CloudAltitudeBounds.x, -_WeatherFarSpan * 0.5);
                float3 upper = float3(_WeatherFarSpan * 0.5, _CloudAltitudeBounds.y, _WeatherFarSpan * 0.5);
                float3 lo = (lower - ro) * inv;
                float3 hi = (upper - ro) * inv;
                float3 a = min(lo, hi), b = max(lo, hi);
                float start = max(0.0, max(a.x, max(a.y, a.z)));
                float finish = min(b.x, min(b.y, b.z));
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                float sceneDistance = LinearEyeDepth(rawDepth) /
                    max(0.025, dot(ray, -UNITY_MATRIX_V[2].xyz));
                // Sky pixels sit on the far plane: the horizon cloud lies beyond it.
                #if defined(UNITY_REVERSED_Z)
                if (rawDepth <= 0.000001) sceneDistance = CLOUD_FAR_LIMIT;
                #else
                if (rawDepth >= 0.999999) sceneDistance = CLOUD_FAR_LIMIT;
                #endif
                finish = min(finish, min(sceneDistance, CLOUD_FAR_LIMIT));
                if (finish <= start) discard;

                float transmittance = 1.0;
                float3 colour = 0.0;
                float weightedDistance = 0.0;
                float cosine = dot(ray, _CloudSunDirection);
                float phase = 0.70 * Phase(cosine, 0.58) + 0.30 * Phase(cosine, -0.25);
                // Stable spatial dither breaks coherent marching bands without any
                // temporal history, ghosting, or camera-motion dependency.
                float jitter = lerp(0.18, 0.82, frac(52.9829189 *
                    frac(dot(i.pos.xy, float2(0.06711056, 0.00583715)))));

                // Two segments: full detail to the near limit, then a short coarse march
                // through the far level of detail out to the horizon.
                [loop]
                for (int segment = 0; segment < 2; segment++)
                {
                    float farPass = (float)segment;
                    float s0 = segment == 0 ? start : max(start, CLOUD_NEAR_LIMIT);
                    float s1 = segment == 0 ? min(finish, CLOUD_NEAR_LIMIT) : finish;
                    if (s1 <= s0 || transmittance < 0.025) continue;
                    float span = s1 - s0;
                    float steps = segment == 0 ? _CloudSteps : _CloudFarSteps;
                    if (segment == 0 && s0 > 25000.0) steps *= 0.68;
                    steps = max(1.0, floor(steps));
                    [loop]
                    for (int n = 0; n < 96; n++)
                    {
                        if (n >= steps || transmittance < 0.025) break;
                        float a = n / steps, b = (n + 1.0) / steps;
                        float stepLength = span * (b * b - a * a);
                        float t = s0 + span * lerp(a * a, b * b, jitter);
                        float3 world = origin + ray * t;
                        float d = Density(world, farPass * 1.2);
                        if (d > 0.003)
                        {
                            float optical = SunOpticalDepth(world, farPass);
                            float h = saturate((world.y + _CloudWorldOffset.y - _CloudBase) / 6000.0);
                            float direct = exp(-optical);
                            // One broad secondary-scattering lobe retains detail in thick
                            // shade without flattening it to a constant minimum colour.
                            float scattered = 0.18 * exp(-optical * 0.24);
                            float skyAccess = lerp(0.32, 1.0, h) * exp(-d * 0.65);
                            float3 light = _CloudAmbientColor * skyAccess * 0.72 +
                                _CloudGroundColor * (1.0 - h) * exp(-d) +
                                _CloudSunColor * (direct * phase * 0.65 + scattered);
                            float absorb = exp(-d * stepLength * 0.003);
                            float contribution = transmittance * (1.0 - absorb);
                            colour += contribution * light;
                            weightedDistance += contribution * t;
                            transmittance *= absorb;
                        }
                    }
                }
                float opacity = 1.0 - transmittance;
                float cloudDistance = weightedDistance / max(0.0001, opacity);
                float air = AirTransmittance(cloudDistance, ro.y, ro.y + ray.y * cloudDistance);
                // Opacity-weighted cloud depth puts airlight in front of the visible
                // cloud surface, with one atmospheric integral per pixel.
                colour = lerp(_CloudFogColor * opacity, colour, air);
                return float4(colour, opacity);
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
            #include "UnityCG.cginc"
            sampler2D _MainTex, _CloudMapTarget;
            float _WeatherMapBlend;
            float4 blendMap(v2f_img i) : SV_Target
            {
                return lerp(tex2D(_MainTex, i.uv), tex2D(_CloudMapTarget, i.uv), _WeatherMapBlend);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
