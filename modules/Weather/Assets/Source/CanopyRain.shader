// Boscali canopy rain render: a pure heightfield visualizer over the per-pane
// droplet simulation (Hidden/BoscaliCanopyDroplets). Every pane samples the same
// metric pattern density (1.5 tiles/m on a per-pane planar frame picked from mesh
// bounds), so split windscreens agree on drop size by construction. The render
// holds no time and no flow: all motion lives in the sim, which is what keeps
// panes from ever disagreeing frame to frame. Beads refract the opaque scene,
// runner trails add a faint wet sheen; dry glass clips to fully transparent.
Shader "Boscali/CanopyRain"
{
    Properties
    {
        _Intensity ("Glass Wetness", Range(0, 1)) = 0
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

            sampler2D _CameraOpaqueTexture;
            sampler2D _DropTex;
            float _Intensity, _LightLevel, _Refract, _Distortion, _MapAxis;
            float4 _Salt, _SunDir, _SunColor, _FogColor, _Tint;

            #define DENSITY 1.5 // pattern tiles per metre, identical on every pane
            #define TEXEL (1.0 / 512.0)

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
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.objNormal = v.normal;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float4 frag(Interpolated i) : SV_Target
            {
                clip(_Intensity - 0.001);

                float hC = tex2D(_DropTex, i.simUv).r;
                float hx = tex2D(_DropTex, i.simUv + float2(TEXEL, 0.0)).r
                    - tex2D(_DropTex, i.simUv - float2(TEXEL, 0.0)).r;
                float hy = tex2D(_DropTex, i.simUv + float2(0.0, TEXEL)).r
                    - tex2D(_DropTex, i.simUv - float2(0.0, TEXEL)).r;
                float trail = tex2D(_DropTex, i.simUv).g;

                // Pane basis in object space, flipped to the authored normal side.
                float3 exO = _MapAxis < 0.5 ? float3(1, 0, 0) : (_MapAxis < 1.5 ? float3(0, 0, 1) : float3(1, 0, 0));
                float3 eyO = _MapAxis < 0.5 ? float3(0, 1, 0) : (_MapAxis < 1.5 ? float3(0, 1, 0) : float3(0, 0, 1));
                float3 nO = _MapAxis < 0.5 ? float3(0, 0, 1) : (_MapAxis < 1.5 ? float3(1, 0, 0) : float3(0, 1, 0));
                float side = dot(normalize(i.objNormal), nO) >= 0.0 ? 1.0 : -1.0;
                float3x3 objToWorld = (float3x3)unity_ObjectToWorld;
                float3 nW = normalize(mul(objToWorld, nO) * side
                    + (mul(objToWorld, exO) * -hx + mul(objToWorld, eyO) * -hy) * 3.5 * side);

                float bead = smoothstep(0.05, 0.45, hC);
                float rim = saturate(length(float2(hx, hy)) * 2.0);
                float cover = saturate(bead + trail * 0.35);
                clip(cover - 0.004);

                float2 suv = i.screen.xy / max(i.screen.w, 0.0001);
                float2 screenDistortion = float2(hx, hy) * _Distortion
                    * float2(_ScreenParams.y / _ScreenParams.x, 1.0) * 2.0;
                float3 scene = tex2D(_CameraOpaqueTexture, saturate(suv + screenDistortion)).rgb;

                float3 tint = _FogColor.rgb * _LightLevel;
                float3 body = lerp(tint, scene, _Refract) * _Tint.rgb;

                float sunLen = length(_SunDir.xyz);
                float glint = 0.0;
                if (sunLen > 0.05)
                {
                    float3 sunDir = _SunDir.xyz / sunLen;
                    glint = pow(saturate(dot(nW, sunDir)), 28.0);
                }

                float sky = pow(saturate(nW.y), 3.0) * 0.14 * _LightLevel;
                float3 finalColor = body * (1.0 - rim * 0.18 * bead) + _SunColor.rgb * glint * 0.65 + _FogColor.rgb * (sky + bead * hC * 0.25 * _LightLevel);

                float alpha = saturate(cover * lerp(0.10 + bead * 0.25, 0.25 + bead * 0.55, _Refract)) * _Intensity;
                return float4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
