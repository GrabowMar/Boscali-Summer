// Boscali/Ocean (Crest-inspired single-pass water shader with dynamic weather synergy):
// Replaces the original water shading on LevelInfo.ocean (WaterMat) in place, preserving _macro_basecolor,
// _macro_depth, _size, _OriginOffset, 16-wave Gerstner vertex/fragment math, ship calm rings, and wakes.
// Shading pipeline modeled after Wave Harmonic's Crest Ocean System (OceanEmission, OceanReflection, OceanFoam):
// - Grazing vs nadir scatter + shadow-tinted water body + Beer-Lambert RGB coastal absorption & shallow caustics
// - Multi-term crest subsurface scattering (view-against-sun + wave-face transmission + crest compression)
// - Two-layer foam: subsurface parallax bubbles under glossy Fresnel + normal-lit surface froth & dual-wave surf
// - Roughness-aware energy-conserving Fresnel + dual-lobe GGX sun glitter (broad glitter + near capillary sparkle)
// - Spatial weather coupling via WeatherMap/Profiles (local storm chop, squall whitecaps, turbidity), procedural
//   raindrop impact ripple rings, and real-time storm lightning reflections across waves.
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
        [HideInInspector] _OceanWeatherMap ("Weather map", 2D) = "black" {}
        [HideInInspector] _OceanWeatherProfile ("Weather profile", 2D) = "black" {}
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
            sampler2D _OceanWeatherMap, _OceanWeatherProfile;
            samplerCUBE _OceanSky;
            float4 _size, _OriginOffset;

            // xy direction (toward), z wavenumber, w amplitude / x phase at the anchor, y lean (Gerstner Q), z wavelength.
            float4 _Waves[WAVES];
            float4 _WavePhase[WAVES];
            // Global xz of the point every wave phase and texture offset is measured from (a 2 km grid point near the camera).
            float4 _OceanAnchor;
            // x vertex displacement fade start, y end (horizontal metres from the camera), z wrapped time (s), w enhanced interactions.
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
            float4 _OceanDeep, _OceanDeepGrazing, _OceanShadowTint, _OceanShallow;
            float4 _OceanScatter, _OceanFoamTint, _OceanAmbient, _OceanFogColor;
            // x density, y 1 = exponential squared, z 1 = fog on, w 1 = the main light carries a cookie.
            float4 _OceanFog;
            // Weather synergy: x liquid rain, y rain ripple strength, z local cover, w 1 + overcast shade (>= 0.5 marks live upload).
            float4 _OceanWeather;
            // Weather synergy 2: x turbidity, y sun elevation, z gust boost, w local storm core depth.
            float4 _OceanWeather2;
            float _OceanWeatherSpan;
            // Anchor-relative lightning flashes: xy/z = position (y height above sea), w = flash gain.
            float4 _OceanFlashA, _OceanFlashB;
            // Ships: xy centre (anchor-relative), z radius of flat water, w radius of full waves. w = 0 marks an unused slot.
            float4 _OceanCalm[8];
            // Shared aircraft/ship slots: xy centre (anchor-relative), z radius/half-width, w strength.
            // Dir: xy heading, z aircraft stretch or ship trail metres, w 1 = ship stern wake (0 = aircraft).
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
                float calm : TEXCOORD3;
            };

            float Calm(float2 rel)
            {
                float k = 1.0;
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
                float spacing = 4.0 + 0.016 * r;
                float calm = Calm(rel);
                float amp = (1.0 - smoothstep(_OceanShore.x, _OceanShore.y, shallow)) * calm
                    * (1.0 - smoothstep(_OceanDisp.x, _OceanDisp.y, r));
                float3 disp = 0;
                [unroll] for (int i = 0; i < WAVES; i++)
                {
                    float4 w = _Waves[i], p = _WavePhase[i];
                    float a = w.w * saturate(p.z / (4.0 * spacing) - 1.0);
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
                o.calm = calm;
                return o;
            }

            float CookieAt(float3 world)
            {
                if (_OceanFog.w < 0.5) return 1.0;
                float2 uv = mul(_MainLightWorldToLight, float4(world, 1.0)).xy * 0.5 + 0.5;
                float4 c = tex2Dlod(_MainLightCookieTexture, float4(uv, 0, 0));
                return _MainLightCookieTextureFormat < 0.5 ? dot(c.rgb, 1.0 / 3.0) : (_MainLightCookieTextureFormat < 1.5 ? c.a : c.r);
            }

            // URP's cascaded main-light shadow, one bilinear compare tap. Strength 0 (shadows off) is fully lit.
            float ShadowAt(float3 world)
            {
                if (_MainLightShadowParams.x <= 0.0) return 1.0;
                float3 c0 = world - _CascadeShadowSplitSpheres0.xyz, c1 = world - _CascadeShadowSplitSpheres1.xyz;
                float3 c2 = world - _CascadeShadowSplitSpheres2.xyz, c3 = world - _CascadeShadowSplitSpheres3.xyz;
                float4 weights = float4(float4(dot(c0, c0), dot(c1, c1), dot(c2, c2), dot(c3, c3)) < _CascadeShadowSplitSphereRadii);
                weights.yzw = saturate(weights.yzw - weights.xyz);
                int cascade = 4 - (int)dot(weights, float4(4, 3, 2, 1));
                float4 sc = mul(_MainLightWorldToShadow[cascade], float4(world, 1.0));
                if (sc.z <= 0.0 || sc.z >= 1.0) return 1.0;
                float s = _MainLightShadowmapTexture.SampleCmpLevelZero(sampler_LinearClampCompare, sc.xy, sc.z);
                s = lerp(1.0, s, _MainLightShadowParams.x);
                float3 fromCamera = world - _WorldSpaceCameraPos;
                return lerp(s, 1.0, saturate(dot(fromCamera, fromCamera) * _MainLightShadowParams.z + _MainLightShadowParams.w));
            }

            void SampleSpatialWeather(float2 rel, float localCover, float localRain, float baseRipple,
                out float pixCover, out float pixCell, out float pixRain, out float rainRipple)
            {
                pixCover = localCover;
                pixCell = _OceanWeather2.w;
                pixRain = localRain;
                rainRipple = baseRipple;
                if (_OceanWeatherSpan <= 1.0) return;

                float2 wuv = (rel + _OceanAnchor.xy) / _OceanWeatherSpan + 0.5;
                float edge = min(min(wuv.x, 1.0 - wuv.x), min(wuv.y, 1.0 - wuv.y));
                float mask = smoothstep(0.0, 0.04, edge);
                if (mask <= 0.001) return;

                float4 wm = tex2Dlod(_OceanWeatherMap, float4(saturate(wuv), 0, 0));
                float4 wp = tex2Dlod(_OceanWeatherProfile, float4(saturate(wuv), 0, 0));
                float mapCover = saturate(max(wm.r, wm.b));
                float mapRain = saturate(wp.a * 4.5);
                pixCover = lerp(localCover, mapCover, mask);
                pixCell = lerp(pixCell, wm.b, mask);
                pixRain = lerp(localRain, mapRain, mask);
                if (baseRipple > 0.001 && localRain > 0.001)
                {
                    float spatialScale = clamp(pixRain / max(localRain, 0.05), 0.2, 1.6);
                    rainRipple = saturate(baseRipple * lerp(1.0, spatialScale, mask));
                }
            }

            // Procedural staggered concentric raindrop impact rings (zero texture taps, floating-origin safe via rel).
            float2 RainRippleSlope(float2 rel, float time, float intensity, float footprint, out float varOut)
            {
                varOut = 0.0;
                float detail = (1.0 - smoothstep(0.06, 0.45, footprint)) * saturate(intensity);
                if (detail <= 0.001) return 0.0;

                float2 slopeSum = 0.0;
                [unroll] for (int layer = 0; layer < 2; layer++)
                {
                    float scale = layer == 0 ? 0.85 : 1.55;
                    float speed = layer == 0 ? 0.95 : 1.35;
                    float2 p = rel * scale + float2(layer * 17.3, layer * 9.1);
                    float2 cell = floor(p);
                    float2 f = frac(p);
                    float3 h3 = frac(float3(cell.xyx) * float3(0.1031, 0.1030, 0.0973));
                    h3 += dot(h3, h3.yzx + 33.33);
                    float3 rnd = frac((h3.xxy + h3.yzz) * h3.zyx);
                    float age = frac(time * speed * (0.75 + 0.5 * rnd.z) + rnd.x * 13.7);
                    float2 drop = 0.25 + 0.5 * rnd.xy;
                    float2 d = f - drop;
                    float r = length(d);
                    float maxR = 0.44;
                    float ring = (r - age * maxR) * 28.0;
                    float packet = exp(-ring * ring * 0.85);
                    float env = (1.0 - age) * (1.0 - age) * smoothstep(0.0, 0.07, age) * (1.0 - smoothstep(maxR * 0.8, maxR, r));
                    float wave = (cos(ring) - 0.55 * ring * sin(ring)) * packet * env;
                    float2 dir = d / max(r, 1e-4);
                    slopeSum += dir * wave * (layer == 0 ? 0.13 : 0.095);
                }
                varOut = 0.012 * detail;
                return slopeSum * detail;
            }

            float3 GGXTerm(float3 n, float3 v, float3 l, float nv, float nl, float rough, float3 lightCol)
            {
                float3 hv = l + v;
                float hvLen = length(hv);
                float3 h = hvLen > 1e-4 ? hv / hvLen : n;
                float nh = saturate(dot(n, h));
                float a2 = rough * rough;
                float dd = nh * nh * (a2 - 1.0) + 1.0;
                float ggx = a2 / (UNITY_PI * dd * dd);
                float vis = 0.5 / (nl * sqrt(nv * nv * (1.0 - a2) + a2) + nv * sqrt(nl * nl * (1.0 - a2) + a2) + 1e-5);
                float fh = 0.02 + 0.98 * pow(1.0 - saturate(dot(v, h)), 5.0);
                return min(ggx * vis * fh, 80.0) * nl * lightCol;
            }

            void ApplyLightning(float4 flash, float2 rel, float3 n, float3 v, float nv, float rough, inout float3 specAcc, inout float3 diffAcc)
            {
                if (flash.w <= 0.001) return;
                float3 toFlash = float3(flash.x - rel.x, max(flash.y, 120.0), flash.z - rel.y);
                float dist2 = dot(toFlash, toFlash);
                float3 lf = toFlash * rsqrt(max(dist2, 1.0));
                float atten = flash.w / (1.0 + dist2 * 1.2e-8);
                float3 boltCol = float3(0.72, 0.86, 1.0) * atten * 2.4;
                float nlf = saturate(dot(n, lf));
                specAcc += GGXTerm(n, v, lf, nv, nlf, clamp(rough * 0.85, 0.04, 0.55), boltCol);
                diffAcc += boltCol * (0.25 + 0.75 * nlf) * 0.18;
            }

            float3 ScatterColour(float shallow, float lightVis, float pixCover, float pixTurbidity, float rise, float compression,
                float crestRelief, float sssFade, float3 macro, float3 shallowCol, float3 n, float3 v, float3 l, float3 sun,
                float3 skyIrradiance, float3 causticSource, float footprint)
            {
                float3 grazingCol = _OceanDeepGrazing.a > 0.5 ? _OceanDeepGrazing.rgb : _OceanDeep.rgb * float3(1.15, 1.28, 1.18);
                float3 shadowCol = _OceanShadowTint.a > 0.5 ? _OceanShadowTint.rgb : _OceanDeep.rgb * 0.45;
                float vUp = abs(v.y);

                float3 baseCol = lerp(grazingCol, _OceanDeep.rgb, pow(saturate(vUp), 0.65));
                baseCol = lerp(shadowCol, baseCol, saturate(lightVis * (1.0 - pixCover * 0.35)));

                // Beer-Lambert depth absorption into coastal shallows.
                float opticalDepth = (1.0 - shallow) * lerp(18.0, 9.0, pixTurbidity);
                float3 sigma = lerp(float3(0.42, 0.14, 0.085), float3(0.55, 0.32, 0.28), pixTurbidity);
                float3 transmittance = exp(-sigma * opticalDepth);
                float3 seabed = lerp(shallowCol, macro, 0.65);
                baseCol = lerp(baseCol, seabed, saturate(transmittance * smoothstep(0.08, 0.85, shallow)));

                float3 body = baseCol * (skyIrradiance + sun * saturate(l.y) * 0.48) * lerp(1.0, lerp(0.88, 1.14, rise), crestRelief);

                // Subtle shallow-water caustics derived from existing ripple maps (zero extra taps).
                float causticFade = smoothstep(0.35, 0.85, shallow) * (1.0 - smoothstep(0.5, 3.0, footprint)) * saturate(l.y * 2.5);
                if (causticFade > 0.005)
                {
                    float caustic = pow(saturate(1.0 - abs(causticSource.x + causticSource.y) * 1.8), 4.0);
                    body += shallowCol * sun * (caustic * transmittance.g * causticFade * 0.45);
                }

                // Crest subsurface scattering: thin pinched crests backlit toward the sun and steep wave faces (gated by physical steepness & distance).
                float crestThin = saturate(rise * rise * (0.55 + 0.85 * compression)) * sssFade;
                float towardsSun = pow(saturate(dot(l, -v)), 3.5);
                float waveFace = pow(saturate(dot(v, -normalize(l + n * 0.45))), 3.0);
                float sss = (0.18 + 0.82 * max(towardsSun, waveFace)) * (1.0 - vUp * vUp) * crestThin;
                body += _OceanScatter.rgb * sun * sss * _OceanScatter.a;
                body += _OceanScatter.rgb * skyIrradiance * crestThin * (1.0 - vUp) * 0.12 * (1.0 - pixTurbidity * 0.65) * _OceanScatter.a;
                return body;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 rel = i.rel;
                float shallow = i.map.z;
                // Metres one pixel covers on the water: waves shorter than four of them would alias.
                float footprint = max(length(ddx(rel)), length(ddy(rel))) + 1e-3;
                float shore = 1.0 - smoothstep(_OceanShore.x, _OceanShore.y, shallow);
                // Reuse the vertex calm calculation: hull-adjacent normals follow the already-flattened geometry.
                float calm = lerp(1.0, saturate(i.calm), saturate(_OceanDisp.w));
                float waveScale = shore * calm;

                bool hasWeather = _OceanWeather.w > 0.5;
                float overcastShade = hasWeather ? saturate(_OceanWeather.w - 1.0) : 0.0;
                float pixCover, pixCell, pixRain, rainRipple;
                SampleSpatialWeather(rel, _OceanWeather.z, _OceanWeather.x, _OceanWeather.y, pixCover, pixCell, pixRain, rainRipple);
                float pixTurbidity = hasWeather ? saturate(max(_OceanWeather2.x, 0.35 * pixRain + 0.30 * pixCell)) : 0.0;

                float sx = 0, sz = 0, height = 0, crest = 0, variance = 0, spread = 0;
                float jxx = 1, jxz = 0, jzz = 1;
                [unroll] for (int w = 0; w < WAVES; w++)
                {
                    float4 wv = _Waves[w], p = _WavePhase[w];
                    float kept = saturate(p.z / (4.0 * footprint) - 1.0);
                    float a = wv.w * waveScale;
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
                    variance += 0.5 * ka * ka * (1.0 - kept * kept);
                }
                float determinant = jxx * jzz - jxz * jxz;
                float compression = saturate(1.0 - determinant);
                float2 slope = float2(jzz * sx - jxz * sz, jxx * sz - jxz * sx) / max(determinant, 0.05);
                float rise = saturate(height / max(crest, 0.05) * 0.5 + 0.5);

                float physicalAmp = smoothstep(0.35, 1.8, crest);
                float crestRelief = physicalAmp * (1.0 - smoothstep(2.0, 14.0, footprint));
                float slopeSteep = smoothstep(0.04, 0.22, length(slope));
                float sssFade = smoothstep(0.15, 1.2, max(height, 0.0)) * slopeSteep * (1.0 - smoothstep(1.0, 6.5, footprint));

                // Smoothly damp calm low-amplitude discrete swell normals from altitude into roughness variance so 4 sine waves never stripe.
                float calmAerial = smoothstep(2.0, 12.0, footprint) * (1.0 - smoothstep(0.6, 2.2, crest));
                float swellKeep = lerp(1.0, 0.22, calmAerial);
                variance += 0.5 * dot(slope, slope) * (1.0 - swellKeep * swellKeep);
                slope *= swellKeep;

                // Dual-scale rotated gust patches drift downwind: rougher, foamier water in some places, glassier wind slicks between.
                float gustA = tex2D(_GustTex, rel * _GustUV.z + _GustUV.xy).r;
                float2 relRot = float2(rel.x * 0.53 - rel.y * 0.85, rel.x * 0.85 + rel.y * 0.53);
                float gustB = tex2D(_GustTex, relRot * (_GustUV.z * 0.37) + _GustUV.xy * 0.41).r;
                float macroSlick = saturate(gustA * 0.62 + gustB * 0.38);
                float gust = lerp(1.0 - _OceanWind.w, 1.0 + _OceanWind.w, macroSlick);
                gust *= 1.0 + _OceanWeather2.z * 0.3 + pixCell * 0.45;

                float fadeA = saturate(1.0 / (_RippleA.z * 10.0 * footprint));
                float fadeB = saturate(1.0 / (_RippleB.z * 10.0 * footprint));
                float fadeC = saturate(1.0 / (_RippleC.z * 26.0 * footprint));
                float2 wind = _OceanWind.xy, crosswind = float2(-wind.y, wind.x);
                float2 windB = float2(wind.x * 0.764842 + wind.y * 0.644218, wind.y * 0.764842 - wind.x * 0.644218);
                float2 crossB = float2(-windB.y, windB.x);
                float2 windC = float2(wind.x * 0.877583 - wind.y * 0.479426, wind.y * 0.877583 + wind.x * 0.479426);
                float2 crossC = float2(-windC.y, windC.x);
                float slickWarp = (macroSlick - 0.5) * 0.16;
                float2 uvA = float2(dot(rel, wind), dot(rel, crosswind)) * _RippleA.z + _RippleA.xy;
                float2 uvB = float2(dot(rel, windB), dot(rel, crossB)) * _RippleB.z + _RippleB.xy + slickWarp;
                float2 uvC = float2(dot(rel, windC), dot(rel, crossC)) * _RippleC.z + _RippleC.xy - slickWarp * 0.7;
                float3 na = UnpackNormal(tex2D(_RippleNormal, uvA));
                float3 nb = UnpackNormal(tex2D(_RippleNormal, uvB));
                float3 nc = UnpackNormal(tex2D(_RippleNormal, uvC));

                // Crest-wind coupling: exposed crests catch stronger capillary ripples than sheltered troughs (gated by physical wave relief).
                float crestWind = lerp(1.0, lerp(0.78, 1.24, rise), crestRelief);
                float rainDamp = 1.0 - 0.25 * pixRain;
                float ripple = _OceanWind.z * gust * crestWind * rainDamp * lerp(0.35, 1.0, calm);
                float2 fineRippleSlope = ((na.x * wind + na.y * crosswind) * fadeA * _RippleA.w
                    + (nb.x * windB + nb.y * crossB) * fadeB * _RippleB.w) * ripple;
                slope -= fineRippleSlope + (nc.x * windC + nc.y * crossC) * fadeC * _RippleC.w * ripple;
                variance += ripple * ripple * 0.03 * (3.0 - fadeA - fadeB - fadeC) + pixRain * 0.008 + pixCell * 0.006;
                variance += max(macroSlick - 0.32, 0.0) * 0.0045 * smoothstep(1.0, 8.0, footprint);

                // Procedural raindrop impact rings (liquid rain only; masked under heavy chop and distance).
                float rainVar = 0.0;
                float2 rainSlope = RainRippleSlope(rel, _OceanDisp.z, rainRipple, footprint, rainVar);
                slope += rainSlope;
                variance += rainVar;

                // Four shared interaction slots, without new texture reads or a full-screen pass.
                float wash = 0.0, shipWake = 0.0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float4 c = _OceanWash[k];
                    if (c.w <= 0.0) continue;
                    float2 d = rel - c.xy;
                    float2 dir = _OceanWashDir[k].xy;
                    float along = dot(d, dir), across = dot(d, float2(-dir.y, dir.x));
                    if (_OceanWashDir[k].w > 0.5)
                    {
                        float back = -along, trailLength = max(_OceanWashDir[k].z, 1.0);
                        float age = back / trailLength;
                        float halfWidth = c.z + back * 0.28;
                        float band = max(c.z * (0.7 + age * 0.9), footprint * 1.5);
                        if (back <= 0.0 || back >= trailLength || abs(across) > halfWidth + band * 2.0) continue;
                        float fade = (1.0 - age) * (1.0 - age) * smoothstep(0.0, c.z * 1.5, back) * c.w;
                        float phase = back * 0.45 - _OceanDisp.z * (2.0 * UNITY_PI * 0.8);
                        float wave = sin(phase);
                        halfWidth += wave * c.z * 0.35;
                        float edge = 1.0 - smoothstep(0.0, band * 2.0, abs(abs(across) - halfWidth));
                        float churn = 1.0 - smoothstep(0.0, c.z * (2.2 + age * 3.0), abs(across - wave * c.z * 0.15));
                        float resolved = 1.0 - smoothstep(0.5, 2.0, footprint * 0.45);
                        slope += float2(-dir.y, dir.x) * sign(across) * wave * edge * fade * 0.12 * resolved;
                        shipWake = max(shipWake, fade * (churn * 0.65 + edge * 0.2));
                        continue;
                    }
                    float rr = length(float2(along / _OceanWashDir[k].z, across)) / c.z;
                    float fall = saturate(1.0 - rr);
                    fall *= fall;
                    float ring = sin(rr * 26.0 - _OceanDisp.z * 13.0);
                    ring *= lerp(1.0, 1.0 - smoothstep(0.5, 2.0, fwidth(rr) * 26.0), saturate(_OceanDisp.w));
                    slope += normalize(d + 1e-3) * ring * fall * c.w * 0.35;
                    wash = max(wash, fall * c.w);
                }

                float3 n = normalize(float3(-slope.x, 1.0, -slope.y));
                float rough = clamp(sqrt(_OceanSea.x * _OceanSea.x + 2.0 * variance + max(wash, shipWake) * 0.05), 0.02, 0.68);

                float3 v = normalize(_WorldSpaceCameraPos - i.world);
                float3 l = normalize(_MainLightPosition.xyz);
                float lightVis = ShadowAt(i.world) * CookieAt(i.world);
                float3 sun = _MainLightColor.rgb * lightVis;
                float nv = saturate(dot(n, v)) + 1e-4;
                float nl = saturate(dot(n, l));

                // Sky reflection using broader normal (retaining 35% of fine ripples + swell + rain rings + wakes).
                float2 reflectionSlope = slope + fineRippleSlope * 0.65;
                float3 reflectionNormal = normalize(float3(-reflectionSlope.x, 1.0, -reflectionSlope.y));
                float3 r = reflect(-v, reflectionNormal);
                float reflectionNv = saturate(dot(reflectionNormal, v));
                float grazing = (1.0 - smoothstep(0.0, 0.6, reflectionNv)) * (1.0 - smoothstep(0.15, 0.4, rough)) * (1.0 - pixRain * 0.5);
                float perceptual = sqrt(rough) * lerp(1.0, 0.65, grazing);
                float mip = perceptual * (1.7 - 0.7 * perceptual) * max(_OceanSea2.z - 1.0, 0.0);
                r.y = max(r.y, 0.015 + 0.006 * exp2(mip));
                float3 sky = texCUBElod(_OceanSky, float4(normalize(r), mip)).rgb * _OceanSea2.w;

                // Energy-conserving roughness-aware Schlick Fresnel.
                float f0 = 0.02;
                float fresnel = f0 + (max(1.0 - rough * 0.65, f0) - f0) * pow(1.0 - nv, 5.0);

                // Dual-lobe GGX specular: broad glitter highway + crisp near capillary sun sparkles.
                float3 spec = GGXTerm(n, v, l, nv, nl, rough, sun);
                float sparkleFade = (1.0 - smoothstep(0.3, 3.5, footprint)) * (1.0 - pixRain * 0.6) * (1.0 - overcastShade * 0.75);
                if (sparkleFade > 0.01)
                {
                    float roughSparkle = clamp(rough * 0.36, 0.014, 0.14);
                    spec += GGXTerm(n, v, l, nv, nl, roughSparkle, sun) * sparkleFade * 0.45;
                }

                // Storm lightning reflections & wave illumination from the two brightest active bolts.
                float3 lightningDiff = 0.0;
                ApplyLightning(_OceanFlashA, rel, n, v, nv, rough, spec, lightningDiff);
                ApplyLightning(_OceanFlashB, rel, n, v, nv, rough, spec, lightningDiff);

                float3 macro = tex2D(_macro_basecolor, i.map.xy).rgb;
                float3 shallowCol = hasWeather ? _OceanShallow.rgb : float3(0.04, 0.34, 0.36);
                float3 skyDiffuse = max(_OceanAmbient.rgb, texCUBElod(_OceanSky, float4(0, 1, 0, _OceanSea2.z)).rgb * _OceanSea2.w);
                float3 body = ScatterColour(shallow, lightVis, pixCover, pixTurbidity, rise, compression, crestRelief, sssFade,
                    macro, shallowCol, n, v, l, sun, max(_OceanAmbient.rgb, skyDiffuse * (0.14 + 0.18 * overcastShade)), float3(na.x, nb.y, 0.0), footprint);
                body += lightningDiff * max(_OceanDeep.rgb, float3(0.02, 0.05, 0.08)) * 2.5;

                // Foam masks: whitecaps under wave compression + gust + storm cell, gale streaks, dual-wave shore surf, wakes.
                float2 foamUV = rel * _FoamUV.z + _FoamUV.xy;
                float laceA = tex2D(_FoamTex, foamUV).r;
                float laceB = tex2D(_FoamTex, foamUV * 2.7 + 0.37).r;
                float lace = saturate(laceA * 0.7 + laceB * 0.6);
                float sigmas = compression / max(sqrt(spread), 1e-4) + (gust - 1.0) * 1.5 + pixCell * 0.75;
                float core = smoothstep(_OceanSea.y, _OceanSea.y + 0.3, sigmas);
                float trail = smoothstep(_OceanSea.z, _OceanSea.y + 1e-3, sigmas) * saturate(lace * 3.0 - 0.2) * 0.6;
                float crestFoam = max(core, trail) * saturate(_OceanSea.w * 400.0 + pixCell * 0.8) * calm;
                float2 windwise = float2(dot(rel, wind), dot(rel, crosswind));
                float streak = tex2D(_StreakTex, float2(windwise.x * 0.2, windwise.y) * _StreakUV.z + _StreakUV.xy).r;
                float streakFoam = saturate((streak - 0.62) * 5.0) * saturate(_OceanSea2.x + pixCell * 0.25) * (0.4 + lace) * calm;

                float surfBand = smoothstep(0.55, 0.78, shallow) * (1.0 - smoothstep(0.93, 0.99, shallow));
                float surfWaveA = saturate(sin(shallow * 92.0 - _OceanDisp.z * 1.35) * 0.6 + 0.4);
                float surfWaveB = saturate(sin(shallow * 48.0 - _OceanDisp.z * 0.85 + 2.1) * 0.55 + 0.35);
                float surf = surfBand * max(surfWaveA, surfWaveB * 0.75) * (0.45 + 0.75 * lace) * _OceanSea2.y;

                float brokenWake = smoothstep(0.25, 0.7, laceA) * smoothstep(0.15, 0.65, laceB);
                float foam = saturate(crestFoam + streakFoam + surf + wash * saturate(lace * 2.5 + wash * 0.3)
                    + shipWake * (0.1 + brokenWake * 0.65) * 0.5);

                // Crest subsurface parallax bubbles beneath the glossy Fresnel water surface.
                float bubbleMask = saturate(trail * 1.35 + core * 0.9 + surf * 0.95 + wash * 0.9 + shipWake * 0.85) * calm;
                if (bubbleMask > 0.01 && footprint < 4.0)
                {
                    float2 bubbleUV = (rel - v.xz / max(abs(v.y), 0.30) * 0.45) * _FoamUV.z * 1.35 + _FoamUV.xy;
                    float bubbleTex = tex2Dlod(_FoamTex, float4(bubbleUV, 0, clamp(log2(max(footprint * 2.0, 1.0)), 0.0, 4.0))).r;
                    float bubbles = bubbleMask * smoothstep(0.18, 0.72, bubbleTex) * (1.0 - smoothstep(1.5, 4.0, footprint));
                    float3 bubbleLit = lerp(shallowCol, _OceanFoamTint.rgb, 0.42) * (skyDiffuse + sun * 0.45 + lightningDiff);
                    body = lerp(body, bubbleLit, bubbles * 0.65);
                }

                float foamSharp = 1.0 - smoothstep(2.0, 8.0, footprint);
                foam = lerp(saturate(_OceanSea.w * 0.3 + _OceanSea2.x * 0.05) * waveScale, foam, foamSharp);

                float alpha = lerp(1.0, _OceanShore.w, smoothstep(_OceanShore.z, 0.99, shallow));
                float3 rgb = body * (1.0 - fresnel) * alpha + sky * fresnel + spec;

                // Normal-lit surface foam: slight ridge tilt toward lace gradient gives crisp 3D froth highlights.
                float3 foamN = normalize(n + float3(wind.x, 0.0, wind.y) * ((laceA - laceB) * 0.45));
                float3 foamLit = _OceanFoamTint.rgb * (skyDiffuse + sun * (0.25 + 0.75 * saturate(dot(foamN, l))) * 0.80 + lightningDiff);
                rgb = lerp(rgb, foamLit, foam);
                alpha = lerp(alpha, 1.0, foam);

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
