// Boscali/Ocean (2026-10-19): the game's sea mesh (LevelInfo.ocean, a 362 km disc that follows the camera, dense within ~1 km) drawn as
// a wind- and storm-driven sea. Sixteen Gerstner waves (SeaState, uploaded by OceanDressing) displace the dense centre and shade every
// pixel; a wave too short for the vertex spacing or the pixel footprint fades out and its slope variance becomes GGX roughness, so the
// far sea turns into a widening sun glitter instead of aliasing. On top: the game's own ripple normals in two scrolling scales, wind gust
// patches, crest whitecaps, gale streaks, surf on the shallows, and the ruffled wash under low aircraft. Lit by URP's main light (with its
// cloud cookie and cascaded shadows), the game's realtime reflection probe and the ambient probe; fogged like the terrain.
// It replaces the vanilla WaterMat's shader in place, so LevelInfo keeps feeding _macro_basecolor, _macro_depth, _size and _OriginOffset.
// Never declare URP globals (_MainLight*, shadows) in Properties: a material value would hide the global.
Shader "Boscali/Ocean"
{
    Properties
    {
        [HideInInspector] _macro_basecolor ("Macro water colour", 2D) = "black" {}
        [HideInInspector] _macro_depth ("Macro shallowness", 2D) = "black" {}
        [HideInInspector] _size ("Map size", Vector) = (81920, 81920, 0, 0)
        [HideInInspector] _OriginOffset ("Origin offset", Vector) = (0, 0, 0, 0)
        [HideInInspector] _RippleNormal ("Ripple normals", 2D) = "bump" {}
        [HideInInspector] _FoamTex ("Foam cells", 2D) = "black" {}
        [HideInInspector] _StreakTex ("Streaks", 2D) = "gray" {}
        [HideInInspector] _GustTex ("Gust patches", 2D) = "gray" {}
        [HideInInspector] _OceanSky ("Sky probe", Cube) = "black" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            #define WAVES 16

            sampler2D _macro_basecolor, _macro_depth, _RippleNormal, _FoamTex, _StreakTex, _GustTex;
            samplerCUBE _OceanSky;
            float4 _size, _OriginOffset;

            // xy direction (toward), z wavenumber, w amplitude / x phase at the anchor, y lean (Gerstner Q), z wavelength.
            float4 _Waves[WAVES];
            float4 _WavePhase[WAVES];
            // Global xz of the point every wave phase and texture offset is measured from (a 2 km grid point near the camera).
            float4 _OceanAnchor;
            // x vertex displacement fade start, y end (horizontal metres from the camera), z wrapped time (s).
            float4 _OceanDisp;
            // xy wind direction (toward), z ripple strength, w gust contrast.
            float4 _OceanWind;
            // x base roughness, y breaking cores and z foam lace start (sigmas of crest compression), w whitecap coverage.
            float4 _OceanSea;
            // x streaks, y surf, z sky probe max mip, w reflection gain.
            float4 _OceanSea2;
            // xy offset, z 1/tile (w weight): ripples A, B and C, foam cells, streaks (z along the wind), gust patches.
            float4 _RippleA, _RippleB, _RippleC, _FoamUV, _StreakUV, _GustUV;
            // x shallowness where waves start to fade, y where they are gone, z shallowness where water turns clear, w clear alpha.
            float4 _OceanShore;
            float4 _OceanDeep, _OceanScatter, _OceanFoamTint, _OceanAmbient, _OceanFogColor;
            // x density, y 1 = exponential squared, z 1 = fog on, w 1 = the main light carries a cookie.
            float4 _OceanFog;
            // Ships: xy centre (anchor-relative), z radius of flat water, w radius of full waves. w = 0 marks an unused slot.
            float4 _OceanCalm[8];
            // Low aircraft: xy centre (anchor-relative), z radius, w strength; dir: xy heading, z length along it.
            float4 _OceanWash[4];
            float4 _OceanWashDir[4];

            // URP globals.
            float4 _MainLightPosition;
            float4 _MainLightColor;
            float4x4 _MainLightWorldToLight;
            float _MainLightCookieTextureFormat;
            sampler2D _MainLightCookieTexture;
            float4x4 _MainLightWorldToShadow[5];
            float4 _CascadeShadowSplitSpheres0, _CascadeShadowSplitSpheres1, _CascadeShadowSplitSpheres2, _CascadeShadowSplitSpheres3;
            float4 _CascadeShadowSplitSphereRadii;
            float4 _MainLightShadowParams;
            Texture2D _MainLightShadowmapTexture;
            SamplerComparisonState sampler_LinearClampCompare;

            struct appdata { float4 vertex : POSITION; };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float2 rel : TEXCOORD1;      // undisplaced global xz minus the anchor (the Gerstner parameter point)
                float4 map : TEXCOORD2;      // xy map uv, z shallowness, w eye depth
            };

            float Calm(float2 rel)
            {
                float k = 1;
                [unroll] for (int c = 0; c < 8; c++)
                {
                    float4 z = _OceanCalm[c];
                    if (z.w > 0.5) k *= smoothstep(z.z, z.w, length(rel - z.xy));
                }
                return k;
            }

            v2f vert(appdata v)
            {
                v2f o;
                float2 obj = v.vertex.xz;
                // The plane sits at the camera's xz: object xz minus _OriginOffset is the global position.
                float2 rel = obj - _OriginOffset.xy - _OceanAnchor.xy;
                // Clamped: the maps repeat, and past the map edge (black, deep) the open sea must stay deep, not read the far coast.
                float2 uv = saturate((rel + _OceanAnchor.xy) / _size.xy + 0.5);
                float shallow = tex2Dlod(_macro_depth, float4(uv, 0, 0)).r;
                float r = length(obj);
                // Measured vertex spacing of the vanilla disc: ~6 m at 100 m, ~20 m at 1 km. A wave needs four vertices.
                float spacing = 4 + 0.016 * r;
                float amp = (1 - smoothstep(_OceanShore.x, _OceanShore.y, shallow)) * Calm(rel)
                    * (1 - smoothstep(_OceanDisp.x, _OceanDisp.y, r));
                float3 disp = 0;
                [unroll] for (int i = 0; i < WAVES; i++)
                {
                    float4 w = _Waves[i], p = _WavePhase[i];
                    float a = w.w * saturate(p.z / (4 * spacing) - 1);
                    float s, c;
                    sincos(w.z * dot(w.xy, rel) + p.x, s, c);
                    disp.xz += p.y * a * w.xy * c;
                    disp.y += a * s;
                }
                float4 world = mul(unity_ObjectToWorld, v.vertex);
                world.xyz += disp * amp;
                o.pos = mul(UNITY_MATRIX_VP, world);
                o.world = world.xyz;
                o.rel = rel;
                o.map = float4(uv, shallow, -mul(UNITY_MATRIX_V, world).z);
                return o;
            }

            float CookieAt(float3 world)
            {
                if (_OceanFog.w < 0.5) return 1;
                float2 uv = mul(_MainLightWorldToLight, float4(world, 1)).xy * 0.5 + 0.5;
                float4 c = tex2Dlod(_MainLightCookieTexture, float4(uv, 0, 0));
                return _MainLightCookieTextureFormat < 0.5 ? dot(c.rgb, 1.0 / 3.0) : (_MainLightCookieTextureFormat < 1.5 ? c.a : c.r);
            }

            // URP's cascaded main-light shadow, one bilinear compare tap. Strength 0 (shadows off) is fully lit.
            float ShadowAt(float3 world)
            {
                if (_MainLightShadowParams.x <= 0) return 1;
                float3 c0 = world - _CascadeShadowSplitSpheres0.xyz, c1 = world - _CascadeShadowSplitSpheres1.xyz;
                float3 c2 = world - _CascadeShadowSplitSpheres2.xyz, c3 = world - _CascadeShadowSplitSpheres3.xyz;
                float4 weights = float4(float4(dot(c0, c0), dot(c1, c1), dot(c2, c2), dot(c3, c3)) < _CascadeShadowSplitSphereRadii);
                weights.yzw = saturate(weights.yzw - weights.xyz);
                int cascade = 4 - (int)dot(weights, float4(4, 3, 2, 1));
                float4 sc = mul(_MainLightWorldToShadow[cascade], float4(world, 1));
                if (sc.z <= 0 || sc.z >= 1) return 1;
                float s = _MainLightShadowmapTexture.SampleCmpLevelZero(sampler_LinearClampCompare, sc.xy, sc.z);
                s = lerp(1, s, _MainLightShadowParams.x);
                float3 fromCamera = world - _WorldSpaceCameraPos;
                return lerp(s, 1, saturate(dot(fromCamera, fromCamera) * _MainLightShadowParams.z + _MainLightShadowParams.w));
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 rel = i.rel;
                float shallow = i.map.z;
                // Metres one pixel covers on the water: waves shorter than four of them would alias.
                float footprint = max(length(ddx(rel)), length(ddy(rel))) + 1e-3;
                float shore = 1 - smoothstep(_OceanShore.x, _OceanShore.y, shallow);

                float sx = 0, sz = 0, height = 0, crest = 0, variance = 0, spread = 0;
                float jxx = 1, jxz = 0, jzz = 1;
                [unroll] for (int w = 0; w < WAVES; w++)
                {
                    float4 wv = _Waves[w], p = _WavePhase[w];
                    float kept = saturate(p.z / (4 * footprint) - 1);
                    float a = wv.w * shore;
                    float ka = wv.z * a;
                    float s, c;
                    sincos(wv.z * dot(wv.xy, rel) + p.x, s, c);
                    sx += wv.x * ka * c * kept;
                    sz += wv.y * ka * c * kept;
                    // Full horizontal Gerstner Jacobian: crossed waves compress in their own directions.
                    float pinch = p.y * ka * s * kept;
                    jxx -= pinch * wv.x * wv.x;
                    jxz -= pinch * wv.x * wv.y;
                    jzz -= pinch * wv.y * wv.y;
                    spread += 0.5 * (p.y * ka * kept) * (p.y * ka * kept);
                    height += a * s * kept;
                    crest += a * kept;
                    variance += 0.5 * ka * ka * (1 - kept * kept);
                }
                float determinant = jxx * jzz - jxz * jxz;
                float compression = saturate(1 - determinant);
                float2 slope = float2(jzz * sx - jxz * sz, jxx * sz - jxz * sx) / max(determinant, 0.05);

                // Gust patches drift downwind: rougher, foamier water in some places, glassier lanes between.
                float gust = lerp(1 - _OceanWind.w, 1 + _OceanWind.w, tex2D(_GustTex, rel * _GustUV.z + _GustUV.xy).r);

                // The game's ripple normal map in three scales; far out the ripples average out and become roughness. Faded at 48 pixels a
                // tile, the mid-distance sea turned to glass (live check 2026-10-19); the mips filter what stays.
                float fadeA = saturate(1 / (_RippleA.z * 10 * footprint));
                float fadeB = saturate(1 / (_RippleB.z * 10 * footprint));
                float fadeC = saturate(1 / (_RippleC.z * 10 * footprint));
                float2 wind = _OceanWind.xy, crosswind = float2(-wind.y, wind.x);
                // Rotate the UVs and slopes together. The second ripple train crosses the first by 0.7 radians, the third (a coarse
                // tile that keeps the sea textured from 0.5 to 5 km, where the fine two have mipped flat) by -0.5.
                float2 windB = float2(wind.x * 0.764842 + wind.y * 0.644218, wind.y * 0.764842 - wind.x * 0.644218);
                float2 crossB = float2(-windB.y, windB.x);
                float2 windC = float2(wind.x * 0.877583 - wind.y * 0.479426, wind.y * 0.877583 + wind.x * 0.479426);
                float2 crossC = float2(-windC.y, windC.x);
                float2 uvA = float2(dot(rel, wind), dot(rel, crosswind)) * _RippleA.z + _RippleA.xy;
                float2 uvB = float2(dot(rel, windB), dot(rel, crossB)) * _RippleB.z + _RippleB.xy;
                float2 uvC = float2(dot(rel, windC), dot(rel, crossC)) * _RippleC.z + _RippleC.xy;
                float3 na = UnpackNormal(tex2D(_RippleNormal, uvA));
                float3 nb = UnpackNormal(tex2D(_RippleNormal, uvB));
                float3 nc = UnpackNormal(tex2D(_RippleNormal, uvC));
                float ripple = _OceanWind.z * gust;
                slope -= ((na.x * wind + na.y * crosswind) * fadeA * _RippleA.w
                    + (nb.x * windB + nb.y * crossB) * fadeB * _RippleB.w
                    + (nc.x * windC + nc.y * crossC) * fadeC * _RippleC.w) * ripple;
                variance += ripple * ripple * 0.03 * (3 - fadeA - fadeB - fadeC);

                // Low aircraft: rings of downwash ripples and a foaming patch, stretched behind a fast jet.
                float wash = 0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float4 c = _OceanWash[k];
                    if (c.w <= 0) continue;
                    float2 d = rel - c.xy;
                    float2 dir = _OceanWashDir[k].xy;
                    float along = dot(d, dir), across = dot(d, float2(-dir.y, dir.x));
                    float rr = length(float2(along / _OceanWashDir[k].z, across)) / c.z;
                    float fall = saturate(1 - rr);
                    fall *= fall;
                    float ring = sin(rr * 26 - _OceanDisp.z * 13);
                    slope += normalize(d + 1e-3) * ring * fall * c.w * 0.35;
                    wash = max(wash, fall * c.w);
                }

                float3 n = normalize(float3(-slope.x, 1, -slope.y));
                float rough = clamp(sqrt(_OceanSea.x * _OceanSea.x + 2 * variance + wash * 0.05), 0.02, 0.65);

                float3 v = normalize(_WorldSpaceCameraPos - i.world);
                float3 l = normalize(_MainLightPosition.xyz);
                float3 sun = _MainLightColor.rgb * ShadowAt(i.world) * CookieAt(i.world);
                float nv = saturate(dot(n, v)) + 1e-4;
                float nl = saturate(dot(n, l));

                // Sky: URP's roughness-to-mip curve on the perceptual roughness. Mip = roughness x mips left a rough sea a mirror: bright
                // cloud bases on the horizon came back as sharp vertical columns (live check 2026-10-19).
                float3 r = reflect(-v, n);
                float perceptual = sqrt(rough);
                float mip = perceptual * (1.7 - 0.7 * perceptual) * max(_OceanSea2.z - 1, 0);
                // The reflection and its blur keep above the horizon: the probe's lower half is not the sea the eye expects, and a blurred
                // lookup near the horizon pulled it in as a grey band.
                r.y = max(r.y, 0.015 + 0.006 * exp2(mip));
                float3 sky = texCUBElod(_OceanSky, float4(normalize(r), mip)).rgb * _OceanSea2.w;
                float fresnel = 0.02 + 0.98 * pow(1 - nv, 5);
                fresnel *= lerp(1, 0.75, saturate(rough * 2));

                // Sun: GGX with height-correlated Smith visibility; rough water spreads the glitter path wide.
                float3 h = normalize(l + v);
                float nh = saturate(dot(n, h));
                float a2 = rough * rough;
                float dd = nh * nh * (a2 - 1) + 1;
                float ggx = a2 / (UNITY_PI * dd * dd);
                float vis = 0.5 / (nl * sqrt(nv * nv * (1 - a2) + a2) + nv * sqrt(nl * nl * (1 - a2) + a2) + 1e-5);
                float fh = 0.02 + 0.98 * pow(1 - saturate(dot(v, h)), 5);
                float3 spec = min(ggx * vis * fh, 80) * nl * sun;

                // The water body: deep colour in open sea, the map's own lagoon colour over the shallows, lit by sky and sun,
                // and sunlight through the thin backs of crests when looking toward the sun.
                float3 macro = tex2D(_macro_basecolor, i.map.xy).rgb;
                float3 bodyColour = lerp(_OceanDeep.rgb, macro, smoothstep(0.3, 0.9, shallow) * 0.8);
                float rise = saturate(height / max(crest, 0.05) * 0.5 + 0.5);
                // Crests catch more light than troughs: under an overcast, where the reflection is one grey, this is what shows the waves.
                float3 body = bodyColour * (_OceanAmbient.rgb + sun * saturate(l.y) * 0.55) * lerp(0.88, 1.12, rise);
                float through = pow(saturate(dot(v, -normalize(l + n * 0.35))), 4);
                body += _OceanScatter.rgb * sun * through * rise * rise * _OceanScatter.a;

                // Foam: whitecaps where crests compress, wind streaks in a gale, surf in the shallows, aircraft wash.
                float2 foamUV = rel * _FoamUV.z + _FoamUV.xy;
                float lace = saturate(tex2D(_FoamTex, foamUV).r * 0.7 + tex2D(_FoamTex, foamUV * 2.7 + 0.37).r * 0.6);
                // Whitecaps sit where the crossed waves squeeze a crest (the Jacobian above). The waves never repeat, so neither do the
                // whitecaps: a thresholded tiling texture stamped the same crescents every 38 m (live check 2026-10-19). The summed compression
                // is near normal, so a share of the sea above a threshold in sigmas follows from the normal quantile (SeaState.WhitecapSigma),
                // the sigma measured from the waves this pixel resolves, so the share holds near and far. Breaking cores (_OceanSea.y, a fifth
                // of the coverage) are solid white; the rest of the coverage (from _OceanSea.z) is the lace they leave. All solid, a gale's
                // 22 % drew cow spots (live check 2026-10-19). Gust patches break more crests.
                float sigmas = compression / max(sqrt(spread), 1e-4) + (gust - 1) * 1.5;
                float core = smoothstep(_OceanSea.y, _OceanSea.y + 0.3, sigmas);
                float trail = smoothstep(_OceanSea.z, _OceanSea.y + 1e-3, sigmas) * saturate(lace * 3 - 0.2) * 0.6;
                float crestFoam = max(core, trail) * saturate(_OceanSea.w * 400);
                float2 windwise = float2(dot(rel, wind), dot(rel, crosswind));
                float streak = tex2D(_StreakTex, float2(windwise.x * 0.2, windwise.y) * _StreakUV.z + _StreakUV.xy).r;
                float streakFoam = saturate((streak - 0.62) * 5) * _OceanSea2.x * (0.4 + lace);
                float surfBand = smoothstep(0.55, 0.8, shallow) * (1 - smoothstep(0.93, 0.99, shallow));
                float surf = surfBand * saturate(sin(shallow * 90 - _OceanDisp.z * 1.3) * 0.6 + 0.4) * lace * _OceanSea2.y;
                float foam = saturate(crestFoam + streakFoam + surf + wash * saturate(lace * 2.5 + wash * 0.3));
                // Far away whitecaps (a few metres across) shrink under a pixel: the foam becomes its coverage, a paler sea. Faded by the lace
                // tile instead, every whitecap stopped on one line ~1.5 km out (live check 2026-10-19).
                float foamSharp = 1 - smoothstep(2, 8, footprint);
                foam = lerp(saturate(_OceanSea.w * 0.3 + _OceanSea2.x * 0.05) * shore, foam, foamSharp);

                float alpha = lerp(1, _OceanShore.w, smoothstep(_OceanShore.z, 0.99, shallow));
                float3 rgb = body * (1 - fresnel) * alpha + sky * fresnel + spec;
                // Foam is lit by the sky the eye sees (the probe's blurriest mip straight up), never darker than the ambient probe: under a
                // storm the game dims the ambient probe far below its grey sky, and SH-lit foam came out as dark stains (live check 2026-10-19).
                float3 skyDiffuse = max(_OceanAmbient.rgb, texCUBElod(_OceanSky, float4(0, 1, 0, _OceanSea2.z)).rgb * _OceanSea2.w);
                float3 foamLit = _OceanFoamTint.rgb * (skyDiffuse + sun * saturate(dot(n, l) * 0.7 + 0.3) * 0.75);
                rgb = lerp(rgb, foamLit, foam);
                alpha = lerp(alpha, 1, foam);

                if (_OceanFog.z > 0.5)
                {
                    float fd = _OceanFog.x * i.map.w;
                    float fog = _OceanFog.y > 0.5 ? exp(-fd * fd) : exp(-fd);
                    rgb = lerp(_OceanFogColor.rgb * alpha, rgb, saturate(fog));
                }
                return float4(rgb, alpha);
            }
            ENDCG
        }
    }
}
