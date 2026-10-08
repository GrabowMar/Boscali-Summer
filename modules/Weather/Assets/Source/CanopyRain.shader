// Boscali canopy rain render: a pure heightfield visualizer over the per-pane
// droplet simulation (Hidden/BoscaliCanopyDroplets). Every pane samples the same
// metric pattern density (3 tiles/m on a per-pane planar frame picked from mesh
// bounds), so split windscreens agree on drop size by construction. The render
// holds no time and no flow: all motion lives in the sim, which is what keeps
// panes from ever disagreeing frame to frame. Beads refract the finished world frame
// (CanopySceneCopy), film blurs it; dry glass clips to fully transparent.
Shader "Boscali/CanopyRain"
{
    Properties
    {
        _Intensity ("Glass Wetness", Range(0, 1)) = 0
        _Frost ("Cold Moisture Edge", Range(0, 1)) = 0
        _PaneBounds ("Pane Bounds", Vector) = (-1,-1,1,1)
        _DropTex ("Droplet State", 2D) = "black" {}
        _Salt ("Pane Salt", Vector) = (0, 0, 0, 0)
        _MapAxis ("Planar Axis (0=z 1=x 2=y)", Float) = 0
        _LightLevel ("Ambient Light", Range(0, 1)) = 1
        _SunDir ("World Sun Direction", Vector) = (0, 0, 0, 0)
        _SunColor ("Sun Color", Color) = (0, 0, 0, 0)
        _FogColor ("Fog Color", Color) = (0.55, 0.6, 0.68, 1)
        _Refract ("Scene Refraction", Range(0, 1)) = 0
        _Distortion ("Distortion", Float) = 0.010
        _Tint ("Tint", Color) = (0.92, 0.95, 0.96, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" }
        ZWrite Off
        ZTest LEqual
        Cull Off
        Offset -1, -1
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            // Untagged passes use SRPDefaultUnlit in URP and also render in the standalone fixture.
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _SceneTex; // finished world frame, half size, mipmapped (CanopySceneCopy)
            sampler2D _DropTex;
            float _Intensity, _LightLevel, _Refract, _Distortion, _MapAxis, _Frost;
            float4 _PaneBounds;
            float4 _Salt, _SunDir, _SunColor, _FogColor, _Tint;

            #define DENSITY 3.0 // pattern tiles per metre, identical on every pane
            #define TEXEL (1.0 / 256.0)

            struct Input
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct Interpolated
            {
                float4 pos : SV_POSITION;
                float2 simUv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float4 screen : TEXCOORD2;
                float3 objNormal : TEXCOORD3;
                float2 paneUv : TEXCOORD4;
                float3 worldPos : TEXCOORD5;
            };

            Interpolated vert(Input v)
            {
                Interpolated o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 scale = float3(
                    length(unity_ObjectToWorld._m00_m10_m20),
                    length(unity_ObjectToWorld._m01_m11_m21),
                    length(unity_ObjectToWorld._m02_m12_m22));
                float3 meters = v.vertex.xyz * scale;
                float2 planar = _MapAxis < 0.5 ? meters.xy : (_MapAxis < 1.5 ? meters.zy : meters.xz);
                o.simUv = planar * DENSITY + _Salt.xy;
                float2 pane = _MapAxis < 0.5 ? v.vertex.xy : (_MapAxis < 1.5 ? v.vertex.zy : v.vertex.xz);
                o.paneUv = (pane - _PaneBounds.xy) / max(float2(0.001,0.001), _PaneBounds.zw - _PaneBounds.xy);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.objNormal = v.normal;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float4 frag(Interpolated i) : SV_Target
            {
                clip(max(_Intensity, _Frost) - 0.001);

                float4 drop = tex2D(_DropTex, i.simUv);
                float hC = drop.r;
                // Thick water is a bead; the thin layer and runner memory are sheet film.
                float bead = smoothstep(0.08, 0.32, hC);
                float film = saturate(smoothstep(0.015, 0.1, hC) * 0.6 + drop.g * 0.8) * (1.0 - bead);
                float cover = saturate(bead + drop.g * 0.25);
                float paneEdge = max(abs(i.paneUv.x * 2.0 - 1.0), abs(i.paneUv.y * 2.0 - 1.0));
                float grain = frac(sin(dot(floor(i.simUv * 24.0), float2(12.9898,78.233))) * 43758.5453);
                float cold = _Frost * smoothstep(0.68, 0.98, paneEdge) * (0.5 + grain * 0.5);
                // Dry glass needs no gradient taps, lighting or scene refraction.
                clip(max(max(cover, film * _Refract), cold) - 0.004);
                float hx = tex2D(_DropTex, i.simUv + float2(TEXEL, 0.0)).r
                    - tex2D(_DropTex, i.simUv - float2(TEXEL, 0.0)).r;
                float hy = tex2D(_DropTex, i.simUv + float2(0.0, TEXEL)).r
                    - tex2D(_DropTex, i.simUv - float2(0.0, TEXEL)).r;

                // Pane basis in object space, flipped to the authored normal side.
                float3 exO = _MapAxis < 0.5 ? float3(1, 0, 0) : (_MapAxis < 1.5 ? float3(0, 0, 1) : float3(1, 0, 0));
                float3 eyO = _MapAxis < 0.5 ? float3(0, 1, 0) : (_MapAxis < 1.5 ? float3(0, 1, 0) : float3(0, 0, 1));
                float3 nO = _MapAxis < 0.5 ? float3(0, 0, 1) : (_MapAxis < 1.5 ? float3(1, 0, 0) : float3(0, 1, 0));
                float side = dot(normalize(i.objNormal), nO) >= 0.0 ? 1.0 : -1.0;
                float3x3 objToWorld = (float3x3)unity_ObjectToWorld;
                float3 exW = normalize(mul(objToWorld, exO));
                float3 eyW = normalize(mul(objToWorld, eyO));
                float3 baseW = normalize(mul(objToWorld, nO)) * side;

                // Lens normal (RainyGlass style): the slope's tangent part of a unit normal,
                // so steep bead edges saturate instead of exploding. Paraboloid beads give a
                // smooth radial ramp: a clear centre and a steep, dark rim.
                float2 slope = float2(hx, hy) * side;
                float2 t = -slope * 1.8;
                float2 nT = t * rsqrt(1.0 + dot(t, t));
                float tilt = length(nT);
                float3 nW = normalize(baseW * sqrt(saturate(1.0 - tilt * tilt)) + exW * nT.x + eyW * nT.y);

                float3 toEye = normalize(_WorldSpaceCameraPos - i.worldPos);
                float fresnel = pow(1.0 - saturate(abs(dot(nW, toEye))), 4.0);
                // Pane-space "up": light from the sky lands on the upper face, the lens
                // focuses it into a bright crescent on the lower inside edge.
                float2 upT = float2(dot(exW, float3(0, 1, 0)), dot(eyW, float3(0, 1, 0)));
                upT = upT / max(length(upT), 0.001);
                float lit = dot(nT, upT);

                float sunLen = length(_SunDir.xyz);
                float glint = 0.0;
                if (sunLen > 0.05)
                {
                    float3 sunDir = _SunDir.xyz / sunLen;
                    float3 h = normalize(sunDir + toEye);
                    glint = pow(saturate(dot(nW, h)), 220.0) * 4.0;
                }
                // Overcast still gives each bead a tiny catch-light near its top.
                float catchLight = smoothstep(0.55, 0.8, lit) * smoothstep(0.95, 0.7, tilt);
                float rimDark = smoothstep(0.45, 0.92, tilt);           // total internal reflection band
                float caustic = smoothstep(0.15, 0.6, -lit) * (1.0 - rimDark);

                float2 suv = i.screen.xy / max(i.screen.w, 0.0001);
                float3 finalColor;
                float alpha;
                if (_Refract > 0.5)
                {
                    // librain-style: the glass shows only the displaced, darkened scene, never
                    // an added colour. A bead is a convex lens (small inverted image of its
                    // surroundings); film smears slightly along its slope and blurs through the
                    // copy's mips, like sheet water in DCS / MSFS.
                    float2 lensView = mul((float3x3)UNITY_MATRIX_V, exW * nT.x + eyW * nT.y).xy;
                    float2 lensUv = -lensView * _Distortion * (3.0 * bead + 0.6 * film)
                        * float2(_ScreenParams.y / _ScreenParams.x, 1.0);
                    float3 body = tex2Dlod(_SceneTex, float4(saturate(suv + lensUv), 0.0, film * 2.5 + bead * 0.4)).rgb;
                    // What a bead mirrors is the blurred scene above it, not a fixed sky colour.
                    float3 env = tex2Dlod(_SceneTex, float4(saturate(suv + float2(0.0, 0.12)), 0.0, 5.0)).rgb;
                    float3 water = body * (1.0 - rimDark * 0.45 + caustic * 0.4)
                        + env * (fresnel * 0.6 + catchLight * 0.5) + _SunColor.rgb * glint;
                    finalColor = lerp(body * 0.94, water, bead);
                    alpha = saturate(bead * 0.97 + film * 0.55);
                }
                else
                {
                    float3 body = _FogColor.rgb * _LightLevel * _Tint.rgb;
                    float3 sky = lerp(_FogColor.rgb, float3(1, 1, 1), 0.35) * _LightLevel;
                    finalColor = body * (1.0 - rimDark * 0.38 * bead + caustic * 0.5 * bead)
                        + sky * (fresnel * 0.5 + catchLight * 0.75) * bead
                        + _SunColor.rgb * glint * bead;
                    alpha = cover * (0.15 + bead * 0.32);
                }
                // The simulation already controls how much water rain deposits. Multiplying
                // opacity by rain again made individual light-shower beads disappear.
                alpha *= smoothstep(0.0, 0.12, _Intensity);
                finalColor = lerp(finalColor, float3(0.70,0.78,0.82) * max(0.08,_LightLevel), saturate(cold * 3.0));
                alpha = max(alpha, cold * 0.65);
                return float4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
