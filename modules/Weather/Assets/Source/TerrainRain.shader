// Original texture-free wet-ground pass drawn over exact native terrain submeshes.
// Blend One SrcAlpha: rgb adds sun glints and sky sheen, alpha multiplies the terrain
// underneath for damp darkening. Noise runs in tile-local metres so origin shifts never
// move the mottling. No native material, mesh or vertex is touched.
Shader "Boscali/TerrainRain"
{
    Properties
    {
        _Wetness ("Wetness", Range(0,1)) = 0
        _Rain ("Current Rain", Range(0,1)) = 0
        _SunDir ("Sun Direction WS", Vector) = (0,1,0,0)
        _SunColor ("Sun Color", Color) = (0,0,0,0)
        _SkyColor ("Sky Sheen Color", Color) = (0,0,0,0)
        _FogDensity ("Fog Density", Float) = 0
        _RippleTime ("Ripple Time", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Geometry+10" "RenderType"="Opaque" }
        Pass
        {
            Blend One SrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float _Wetness, _Rain, _FogDensity, _RippleTime;
            float4 _SunDir, _SunColor, _SkyColor;

            struct Varyings
            {
                float4 pos:SV_POSITION;
                float3 world:TEXCOORD0;
                float3 normal:TEXCOORD1;
                float2 local:TEXCOORD2;
            };

            Varyings vert(appdata_base v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.local = v.vertex.xz;
                return o;
            }

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3 - 2 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1)), d = hash21(i + 1);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag(Varyings i):SV_Target
            {
                float3 N = normalize(i.normal);
                float3 toCam = _WorldSpaceCameraPos - i.world;
                float dist = length(toCam);
                float3 V = toCam / max(dist, 0.001);
                float fade = 1 - smoothstep(700, 1200, dist);
                float wet = saturate(_Wetness) * fade;

                // Organic damp mottling and flat-ground puddles, in tile-local metres.
                float2 p = i.local;
                float mottle = 0.55 + 0.45 * (vnoise(p * 0.02) * 0.6 + vnoise(p * 0.07 + 5.3) * 0.4);
                float slope = smoothstep(0.2, 0.85, N.y);
                float flat = smoothstep(0.93, 0.995, N.y);
                float puddle = smoothstep(0.60, 0.70, vnoise(p * 0.045 + 13.7)) * flat *
                    smoothstep(0.35, 0.8, _Wetness);
                float dark = 0.15 * mottle * slope + 0.07 * puddle;
                float keep = 1 - dark * wet;

                // Puddles ripple; damp ground only sheens.
                // Small expanding impact rings instead of synchronized diagonal waves.
                // One analytic cell, no texture reads; fade before subpixel rings shimmer.
                float2 cell = floor(p * 3);
                float seed = hash21(cell);
                float age = frac(_RippleTime * (0.7 + seed * 0.6) + seed * 17);
                float2 delta = frac(p * 3) - (0.3 + 0.4 * float2(seed, hash21(cell + 19)));
                float radius = length(delta);
                float ring = (radius - age * 0.28) * 65;
                float rip = ring * exp(-ring * ring) * (1 - age) * smoothstep(0, 0.08, age);
                float detail = (1 - smoothstep(15, 65, dist)) / max(1, fwidth(radius) * 65);
                float2 rippleNormal = delta / max(radius, 0.001) * rip * detail * saturate(_Rain);
                float3 Np = normalize(N + float3(rippleNormal.x, 0, rippleNormal.y) * 0.10 * puddle);
                float3 L = normalize(_SunDir.xyz + float3(0, 0.0001, 0));
                float3 H = normalize(L + V);
                float ndh = saturate(dot(Np, H));
                float spec = pow(ndh, 96) * 1.2 * puddle + pow(ndh, 18) * 0.10 * slope * mottle;
                float fres = pow(1 - saturate(dot(Np, V)), 4);
                float sheen = fres * (0.35 * puddle + 0.06 * slope);
                float fogAtt = exp2(-dist * dist * _FogDensity * _FogDensity * 1.4427);
                float3 add = (_SunColor.rgb * spec + _SkyColor.rgb * sheen) * wet * fogAtt;
                return half4(add, keep);
            }
            ENDHLSL
        }
    }
}
