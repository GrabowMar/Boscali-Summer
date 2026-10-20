// Spooktober rad ghost: a see-through, glowing redraw of a unit (the kaiju, the FALLEN ACE). Premultiplied glass (additive with
// _DstBlend = One), no depth write: the world shows through the body, a fresnel rim in the spectral blue ramp outlines it, value-noise heat crawls up it, faint scanlines
// roll up and the surface shimmers. Noise and scanlines run in pivot-relative world metres (the object's rotation and
// scale, not its position), so a floating-origin shift never jumps the pattern and it rides the body.
// Plain CG, no pipeline includes (same recipe as NvgHeat): URP draws an untagged pass as SRPDefaultUnlit. No
// RenderPipeline tag either: the bundle is compiled in a built-in project, where that tag would reject the SubShader.
Shader "Boscali/RadGhost"
{
    Properties
    {
        _Intensity ("Intensity (HDR multiplier on the ramp)", Float) = 1.8
        _BodyAlpha ("Body alpha (see-through floor)", Range(0, 1)) = 0.18
        _RimPower ("Rim power", Float) = 2.5
        _NoiseScale ("Noise scale (cells per metre)", Float) = 0.5
        _ScrollSpeed ("Heat rise (cells per second)", Float) = 0.6
        _ScanDensity ("Scanlines per metre", Float) = 2
        _ScanStrength ("Scanline heat", Float) = 0.15
        _ShimmerScale ("Shimmer (centimetres)", Float) = 4
        _FarDistance ("Distance where the whole body glows like the rim (m)", Float) = 4000
        _Strength ("Strength (fade)", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend (OneMinusSrcAlpha = tinted glass, One = additive)", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }
        Pass
        {
            // Premultiplied: the glow is added and the world behind dims by the alpha, so a bright sky or cloud deck shows
            // through tinted instead of the body blowing out to white, and stacked inner meshes converge on the ramp colour.
            Blend One [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Intensity, _BodyAlpha, _RimPower, _NoiseScale, _ScrollSpeed, _ScanDensity, _ScanStrength, _ShimmerScale,
                  _FarDistance, _Strength;

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

            // Spectral ramp: black, deep blue, cyan, hot cyan-white, dim blue (HDR, so bloom picks the rim up).
            float3 Ramp(float h)
            {
                float3 c = lerp(0.0, float3(0.05, 0.2, 1.0), saturate(h / 0.25));
                c = lerp(c, float3(0.3, 0.85, 1.5), saturate((h - 0.25) / 0.25));
                c = lerp(c, float3(1.2, 2.4, 3.6), saturate((h - 0.5) / 0.3));
                return lerp(c, float3(0.2, 0.5, 2.0), saturate((h - 0.8) / 0.2));
            }

            // normalize() of a zero vector (degenerate normal, camera on the vertex) is NaN, and NaN poisons bloom.
            float3 SafeNormalize(float3 v) { return v * rsqrt(max(dot(v, v), 1e-12)); }

            // ponytail: time wraps every 1000 s (one pattern jump per ~17 min) so the hash keeps its float precision.
            float Clock() { return fmod(_Time.y, 1000.0); }

            v2f vert(appdata_base v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 normal = SafeNormalize(mul(v.normal, (float3x3)unity_WorldToObject)); // UnityObjectToWorldNormal, NaN-safe
                float3 rel = world - unity_ObjectToWorld._m03_m13_m23;
                float wobble = Noise(rel * _NoiseScale * 3.0 + Clock() * 2.5) - 0.5;
                world += normal * (0.01 * _ShimmerScale * wobble);
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                o.normal = normal;
                o.view = _WorldSpaceCameraPos - world;
                o.rel = rel;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float t = Clock();
                float facing = saturate(abs(dot(SafeNormalize(i.normal), SafeNormalize(i.view))));
                float fresnel = pow(1.0 - facing, _RimPower);
                // A far unit is a few pixels with no room for a rim: light all of it so it still reads against the sky.
                float far = saturate(length(i.view) / _FarDistance);
                fresnel = lerp(fresnel, 1.0, far * far);
                float3 p = i.rel * _NoiseScale + float3(0.0, -t * _ScrollSpeed, 0.0);
                float n = Noise(p) * 0.65 + Noise(p * 2.3 + 7.1) * 0.35;
                float scan = step(0.5, frac(i.rel.y * _ScanDensity - t * 2.0));
                float heat = saturate(fresnel * 1.2 + n * 0.5 + scan * _ScanStrength);
                float alpha = saturate(_BodyAlpha + heat * fresnel) * _Strength;
                return float4(Ramp(heat) * _Intensity * alpha, alpha);
            }
            ENDCG
        }
    }
}
