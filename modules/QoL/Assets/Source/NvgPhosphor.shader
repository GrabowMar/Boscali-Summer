// Original white-phosphor response, before native exposure and cockpit composition.
// Work in exposed light for a stable noise floor and highlight shoulder, then undo
// that exposure so the game's existing NVG volume still applies it exactly once.
Shader "Boscali/NvgPhosphor"
{
    Properties { _MainTex ("Frame", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        sampler2D _BoscaliNvgHeatMask;
        float4 _MainTex_TexelSize;
        float4 _NvgHeatColor;
        float4 _NvgTint;
        float4 _NvgNoise;    // grain, dark noise amplitude, frame seed, meter mip
        float4 _NvgResponse; // exposure multiplier
        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        v2f vert(appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            return o;
        }
        float hash(float2 p)
        {
            p = frac(p * float2(443.897, 441.423));
            p += dot(p, p.yx + 19.19);
            return frac((p.x + p.y) * p.x);
        }
        float luma(float3 c) { return max(0.0, dot(c, float3(0.2126, 0.7152, 0.0722))); }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float4 c = tex2Dlod(_MainTex, float4(i.uv, 0, 0));
                float l = luma(c.rgb);
                float2 d = abs(_MainTex_TexelSize.xy) * 1.5;
                float surround = (luma(tex2D(_MainTex, i.uv + float2(d.x, 0)).rgb)
                    + luma(tex2D(_MainTex, i.uv - float2(d.x, 0)).rgb)
                    + luma(tex2D(_MainTex, i.uv + float2(0, d.y)).rgb)
                    + luma(tex2D(_MainTex, i.uv - float2(0, d.y)).rgb)) * 0.25;
                // Bounded detail enhancement: preserve fine terrain without ringing
                // around navigation lights or inventing detail in an empty black sky.
                l += clamp((l - surround) * 0.18, -l * 0.12, l * 0.12);
                float exposure = max(_NvgResponse.x, 0.0001);
                float signal = l * exposure;
                signal *= 1.0 + 0.12 / (1.0 + signal * 8.0);
                signal /= 1.0 + signal / 2.5;
                float n = hash(floor(i.pos.xy) + _NvgNoise.z)
                    + hash(floor(i.pos.yx) + _NvgNoise.z * 1.7 + 31.0) - 1.0;
                // Fine, zero-mean shot noise. No gain-amplified grey veil.
                signal = max(0.0, signal + n * _NvgNoise.x
                    * sqrt(signal * 0.015 + _NvgNoise.y * _NvgNoise.y));
                float3 tint = lerp(_NvgTint.rgb, float3(1, 1, 1), smoothstep(0.08, 1.1, signal) * 0.9);
                // A depth-tested mask provides real silhouettes rather than Fresnel-lit
                // faces. A faint interior cue keeps distant subpixel targets readable.
                float contour = 0.0;
                [branch] if (_NvgHeatColor.r > 0.0)
                {
                    float hot = tex2D(_BoscaliNvgHeatMask, i.uv).r;
                    float left = tex2D(_BoscaliNvgHeatMask, i.uv - float2(d.x, 0)).r;
                    float right = tex2D(_BoscaliNvgHeatMask, i.uv + float2(d.x, 0)).r;
                    float down = tex2D(_BoscaliNvgHeatMask, i.uv - float2(0, d.y)).r;
                    float up = tex2D(_BoscaliNvgHeatMask, i.uv + float2(0, d.y)).r;
                    float edge = max(0.0, hot - min(min(left, right), min(up, down)));
                    contour = saturate(edge * 1.8) + hot * 0.025;
                }
                return float4(signal * tint / exposure + _NvgHeatColor.rgb * contour, c.a);
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            // Meter the unprocessed frame: the response and grain never feed back into AUTO gain.
            float4 frag(v2f i) : SV_Target { return tex2Dlod(_MainTex, float4(i.uv, 0, _NvgNoise.w)); }
            ENDCG
        }
    }
}
