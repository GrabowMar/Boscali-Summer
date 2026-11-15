// Spooktober hellfire: Hell's Army units stay solid (normal vehicles) but read as charred obsidian with lava cracking through
// it. A dim fake top light keeps the shape, ridged value noise opens glowing cracks that pulse and crawl up, and a fire-red
// fresnel rim outlines it. Noise runs in pivot-relative world metres, like RadGhost, so a floating-origin shift never jumps it.
// Plain CG, no pipeline includes, no RenderPipeline tag (same recipe as RadGhost.shader): URP draws it as SRPDefaultUnlit.
Shader "Boscali/Hellfire"
{
    Properties
    {
        _Intensity ("Intensity (HDR multiplier on the fire ramp)", Float) = 2.2
        _Char ("Char (body colour)", Color) = (0.07, 0.035, 0.03, 1)
        _RimPower ("Rim power", Float) = 3
        _NoiseScale ("Crack cells per metre", Float) = 0.35
        _CrackWidth ("Crack width (0-1)", Range(0.01, 0.5)) = 0.12
        _ScrollSpeed ("Crack crawl (cells per second)", Float) = 0.15
        _PulseSpeed ("Pulse (radians per second)", Float) = 2.5
        _FarDistance ("Distance where the whole body glows like the rim (m)", Float) = 3000
        _Strength ("Strength (fade)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            ZWrite On
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Char;
            float _Intensity, _RimPower, _NoiseScale, _CrackWidth, _ScrollSpeed, _PulseSpeed, _FarDistance, _Strength;

            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float3 view : TEXCOORD1; float3 rel : TEXCOORD2; };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1) * 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + 1.0), f.x), f.y), f.z);
            }

            // Fire ramp: black, deep red, orange, hot yellow-white (HDR, so bloom picks the cracks up).
            float3 Ramp(float h)
            {
                float3 c = lerp(0.0, float3(0.5, 0.02, 0.0), saturate(h / 0.3));
                c = lerp(c, float3(2.0, 0.45, 0.03), saturate((h - 0.3) / 0.35));
                return lerp(c, float3(3.5, 2.2, 0.8), saturate((h - 0.65) / 0.35));
            }

            float3 SafeNormalize(float3 v) { return v * rsqrt(max(dot(v, v), 1e-12)); }

            // ponytail: time wraps every 1000 s (one pattern jump per ~17 min) so the hash keeps its float precision.
            float Clock() { return fmod(_Time.y, 1000.0); }

            v2f vert(appdata_base v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                o.normal = SafeNormalize(mul(v.normal, (float3x3)unity_WorldToObject));
                o.view = _WorldSpaceCameraPos - world;
                o.rel = world - unity_ObjectToWorld._m03_m13_m23;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float t = Clock();
                float3 n = SafeNormalize(i.normal);
                float facing = saturate(abs(dot(n, SafeNormalize(i.view))));
                float rim = pow(1.0 - facing, _RimPower);
                float far = saturate(length(i.view) / _FarDistance);
                rim = lerp(rim, 0.7, far * far);
                // Ridged noise: 1 on the zero-crossings, so the cracks are thin connected veins.
                float3 p = i.rel * _NoiseScale + float3(0.0, -t * _ScrollSpeed, 0.0);
                float ridge = 1.0 - abs(Noise(p) * 2.0 - 1.0);
                ridge = max(ridge, 1.0 - abs(Noise(p * 2.7 + 5.3) * 2.0 - 1.0) * 1.3);
                float cracks = smoothstep(1.0 - _CrackWidth, 1.0, ridge);
                float pulse = 0.7 + 0.3 * sin(t * _PulseSpeed + Noise(p * 0.4) * 6.283);
                float heat = saturate((cracks * pulse + rim * 0.9) * _Strength);
                float lit = saturate(dot(n, float3(0.27, 0.92, 0.27))) * 0.7 + 0.3;
                float3 body = _Char.rgb * lit * (1.0 + ridge * 0.5); // the char shows the vein relief faintly
                return float4(body + Ramp(heat) * _Intensity * heat, 1.0);
            }
            ENDCG
        }
    }
}
