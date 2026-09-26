// Boscali canopy rain shader adapting toadstorm's RainyGlassShader (LGPL-2.1).
// High-fidelity raindrops with strike/erosion physics and dual-rest-field flowing rivulets.
// Refracts URP scene color via _CameraOpaqueTexture with dynamic airflow alignment.
Shader "Boscali/CanopyRain"
{
    Properties
    {
        _Intensity ("Glass Wetness", Range(0, 1)) = 0
        _FlowPhase ("Flow Phase", Float) = 0
        _Flow ("Local Airflow", Vector) = (0, -1, 0, 0)
        _LightLevel ("Ambient Light", Range(0, 1)) = 1
        _Speed ("Airspeed Norm", Range(0, 1)) = 0
        _SunDir ("Local Sun Direction", Vector) = (0, 0, 0, 0)
        _SunColor ("Sun Color", Color) = (0, 0, 0, 0)
        _FogColor ("Fog Color", Color) = (0.55, 0.6, 0.68, 1)
        _Refract ("Scene Refraction", Range(0, 1)) = 0

        _DropletMask ("Droplet Mask", 2D) = "black" {}
        _RivuletMask ("Rivulet Mask", 2D) = "black" {}
        _Tiling ("Tiling", Vector) = (3, 3, 0, 0)
        _Distortion ("Distortion", Float) = 0.010
        _Droplets_Strength ("Droplets Strength", Range(0, 1)) = 0.98
        _DropletsStrikeSpeed ("Droplets Strike Speed", Range(0, 2)) = 0.20
        _RivuletSpeed ("Rivulet Speed", Range(0, 2)) = 0.025
        _RivuletsStrength ("Rivulets Strength", Range(0, 3)) = 0.8
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

            sampler2D _CameraOpaqueTexture;
            sampler2D _DropletMask;
            sampler2D _RivuletMask;
            float4 _DropletMask_TexelSize;

            float _Intensity, _FlowPhase, _LightLevel, _Speed, _Refract;
            float4 _Flow, _SunDir, _SunColor, _FogColor;
            float4 _Tiling, _Tint;
            float _Distortion, _Droplets_Strength, _DropletsStrikeSpeed, _RivuletSpeed, _RivuletsStrength;

            struct Input
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 texcoord : TEXCOORD0;
            };

            struct Interpolated
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float4 screen : TEXCOORD2;
                float3 worldNormal : TEXCOORD3;
                float3 local : TEXCOORD4;
            };

            Interpolated vert(Input v)
            {
                Interpolated o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = v.normal;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.screen = ComputeScreenPos(o.pos);

                float3 scale = float3(
                    length(unity_ObjectToWorld._m00_m10_m20),
                    length(unity_ObjectToWorld._m01_m11_m21),
                    length(unity_ObjectToWorld._m02_m12_m22));
                o.local = v.vertex.xyz * scale;

                // Authored mesh UVs if provided; planar projection of local space if unmapped.
                float2 meshUv = v.texcoord.xy;
                if (dot(meshUv, meshUv) > 0.00001)
                {
                    o.uv = meshUv;
                }
                else
                {
                    float3 n3 = abs(v.normal);
                    if (n3.z >= n3.x && n3.z >= n3.y) o.uv = o.local.xy * 0.5 + 0.5;
                    else if (n3.x >= n3.y) o.uv = o.local.zy * 0.5 + 0.5;
                    else o.uv = o.local.xz * 0.5 + 0.5;
                }
                return o;
            }

            float4 frag(Interpolated i) : SV_Target
            {
                clip(_Intensity - 0.001);

                // Local airflow direction projected onto the dominant surface plane
                float3 n3 = abs(i.normal);
                float2 flow2D;
                if (n3.z >= n3.x && n3.z >= n3.y) flow2D = _Flow.xy;
                else if (n3.x >= n3.y) flow2D = _Flow.zy;
                else flow2D = _Flow.xz;

                float flowLen = length(flow2D);
                float2 flowDir = flowLen > 0.001 ? flow2D / flowLen : float2(0.0, -1.0);

                // Rotate coordinates so stream lines follow airflow; reference downward flow (0, -1) has angle 0
                float cosRot = -flowDir.y;
                float sinRot = flowDir.x;
                float2x2 flowRot = float2x2(cosRot, -sinRot, sinRot, cosRot);

                float2 tiling = _Tiling.xy > 0.001 ? _Tiling.xy : float2(3.0, 3.0);
                float2 baseUv = mul(i.uv * tiling, flowRot);

                float time = _FlowPhase;

                // Fade bead detail as drops go subpixel (distance, grazing side panes):
                // a 2px popping refractor reads as shimmer, not rain. Rivulets keep full body.
                float footprint = fwidth(baseUv.x) + fwidth(baseUv.y);
                float beadFade = saturate(0.05 / max(footprint, 1e-4));

                float2 finalDropNorm = float2(0.0, 0.0);
                float dropCoverage = 0.0;
                float2 finalRivNorm = float2(0.0, 0.0);
                float rivuletCoverage = 0.0;
                float3 normW = normalize(i.worldNormal);

                if (_DropletMask_TexelSize.z > 16.0)
                {
                    // --- Toadstorm RainyGlass: Droplets with Strike & Erosion ---
                    float dropGravity = lerp(0.0, 0.03, _Speed);
                    float2 dropUv = baseUv + float2(0.0, time * dropGravity);
                    float4 dropTex = tex2D(_DropletMask, dropUv);

                    float2 dropNorm = dropTex.rg * 2.0 - 1.0;
                    float dropTime = time * _DropletsStrikeSpeed;
                    float dropCycle = frac((dropTex.b * 2.0 - 1.0) + dropTime);

                    float dropStrength = _Droplets_Strength * saturate(_Intensity * 1.25);
                    float dropErosion = (dropTex.a - dropCycle) - (1.0 - dropStrength);
                    // Soft fade-out plus a short fade-in at cycle wrap: a hard pop-in
                    // flips pixels between background and full-contrast refraction.
                    float strikeMask = saturate(dropErosion / 0.22) * smoothstep(0.0, 0.08, dropCycle);

                    finalDropNorm = dropNorm * strikeMask * beadFade;
                    dropCoverage = strikeMask * dropTex.a * beadFade;

                    // --- Toadstorm RainyGlass: Rivulets with Dual Rest Fields ---
                    float4 rivuletTex = tex2D(_RivuletMask, baseUv);
                    float2 flowVector = float2(rivuletTex.b, rivuletTex.a);
                    float2 flowDisp = float2(-0.1, 0.0) + flowVector * float2(0.2, 3.0);

                    float flowClock = time * 0.23;
                    float rest1 = frac(flowClock);
                    float rest2 = frac(flowClock + 0.5);
                    float triWave = abs(frac(flowClock) * 2.0 - 1.0);
                    float blendBias = triWave * triWave;

                    float rivSpeed = lerp(_RivuletSpeed, _RivuletSpeed * 3.2, _Speed);
                    float2 scroll = float2(0.0, time * rivSpeed);
                    float2 uv1 = flowDisp * rest1 + baseUv + scroll;
                    float2 uv2 = flowDisp * rest2 + baseUv + scroll;

                    float4 sample1 = tex2D(_RivuletMask, uv1);
                    float4 sample2 = tex2D(_RivuletMask, uv2);
                    float4 rivuletBlended = lerp(sample1, sample2, blendBias);

                    float2 rivuletNorm = rivuletBlended.rg * 2.0 - 1.0;
                    float slopeFactor = 1.0 - smoothstep(0.85, 1.0, abs(dot(normW, float3(0.0, 1.0, 0.0))));
                    float rivStrength = _RivuletsStrength * saturate((_Intensity - 0.22) * 1.6) * slopeFactor;
                    finalRivNorm = rivuletNorm * rivStrength;
                    rivuletCoverage = saturate(length(rivuletNorm) * 2.5) * rivStrength;
                }
                else
                {
                    // Procedural fallback when mask textures are not bound
                    float2 grid = baseUv * 7.0;
                    float2 id = floor(grid);
                    float2 f = frac(grid) - 0.5;
                    float2 h = frac(sin(float2(dot(id, float2(127.1, 311.7)), dot(id, float2(269.5, 183.3)))) * 43758.5453);
                    float dropCycle = frac(time * 0.25 + h.x);
                    float alive = smoothstep(0.0, 0.1, dropCycle) * (1.0 - smoothstep(0.5, 1.0, dropCycle));
                    float r = length(f);
                    float dMask = (1.0 - smoothstep(0.10, 0.20, r)) * alive * step(1.0 - _Intensity * 0.5, h.y);
                    finalDropNorm = (f / 0.20) * dMask * beadFade;
                    dropCoverage = dMask * beadFade;
                }

                // Total water distortion & coverage
                float2 totalNorm = finalDropNorm + finalRivNorm;
                float waterCoverage = saturate(dropCoverage + rivuletCoverage);

                // Early clip to guarantee unwatered glass stays 100% transparent and clear
                clip(waterCoverage - 0.002);

                // Screen refraction via URP opaque texture
                float2 suv = i.screen.xy / max(i.screen.w, 0.0001);
                float2 screenDistortion = totalNorm * _Distortion * float2(_ScreenParams.y / _ScreenParams.x, 1.0);
                float3 scene = tex2D(_CameraOpaqueTexture, saturate(suv + screenDistortion)).rgb;

                float3 tint = _FogColor.rgb * _LightLevel;
                float3 body = lerp(tint, scene, _Refract) * _Tint.rgb;

                // Sun specular glints on water droplets and streams
                float3 perturbedNormal = normalize(normW + float3(totalNorm.x, totalNorm.y, 0.0) * 0.45);
                float sunLen = length(_SunDir.xyz);
                float glint = 0.0;
                if (sunLen > 0.05)
                {
                    float3 sunDir = _SunDir.xyz / sunLen;
                    glint = pow(saturate(dot(perturbedNormal, sunDir)), 28.0);
                }

                // Ambient sky sheen
                float sky = pow(saturate(perturbedNormal.y), 3.0) * 0.14 * _LightLevel;

                float3 finalColor = body + _SunColor.rgb * glint * 0.65 + _FogColor.rgb * sky;

                // Alpha blending: subtle translucent center, refractive meniscus edge
                float nl = length(totalNorm);
                float edge = smoothstep(0.08, 0.65, nl);
                float alpha = saturate(waterCoverage * lerp(0.06 + edge * 0.18, 0.20 + edge * 0.50, _Refract));

                return float4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
