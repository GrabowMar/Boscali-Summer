// Sun shafts: a glow around the sun, cut by everything in front of it, smeared away from the sun at quarter
// resolution and added over the frame. Anything with depth (terrain, aircraft) and the cloud cover block the
// glow, which draws the beams. Pass 0 is the blocked glow, pass 1 smears it along the line to the sun,
// pass 2 adds it. Plain CG, no pipeline includes.
Shader "Boscali/SunShafts"
{
    Properties { _MainTex ("Frame", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        sampler2D _CameraDepthTexture;
        sampler2D _BoscaliCloudCover; // a: cloud cover over the sky, from the half-resolution cloud pass
        float4 _ShaftSun;    // xy: the sun in the viewport, z: width over height, w: glow radius in screen heights
        float _BoscaliCloudCoverFrame;
        float4 _ShaftParams; // x: use resolved cover, y: fallback light transmission, z: smear reach, w: strength
        float4 _ShaftTint;   // rgb: the sun's colour through the air

        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        v2f vert(appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            return o;
        }

        float InViewport(float2 uv)
        {
            return step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
        }

        float3 Source(float2 uv)
        {
            float valid = InViewport(uv);
            #if UNITY_UV_STARTS_AT_TOP
            if (_MainTex_TexelSize.y < 0.0) uv.y = 1.0 - uv.y;
            #endif
            // A clamped edge replicated bright texels outside the view and drew long border streaks.
            return tex2Dlod(_MainTex, float4(uv, 0, 0)).rgb * valid;
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float sky = step(0.9999, Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv)));
                float cover = tex2Dlod(_BoscaliCloudCover, float4(i.uv, 0, 0)).a;
                float open = lerp(_ShaftParams.y, saturate(1.0 - cover), _ShaftParams.x * _BoscaliCloudCoverFrame);
                float2 d = (i.uv - _ShaftSun.xy) * float2(_ShaftSun.z, 1.0);
                float radial = length(d) / max(_ShaftSun.w, 0.001);
                float corona = exp2(-6.0 * radial * radial) * saturate(1.0 - radial);
                return float4(_ShaftTint.rgb * (corona * sky * open * open), 1.0);
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define TAPS 16
            float4 frag(v2f i) : SV_Target
            {
                float2 stride = (_ShaftSun.xy - i.uv) * (_ShaftParams.z / TAPS);
                // Interleaved gradient noise: the tap pattern shifts per pixel, so the steps do not show as rings.
                float jitter = frac(52.9829189 * frac(dot(i.pos.xy, float2(0.06711056, 0.00583715))));
                float2 uv = i.uv + stride * jitter;
                float3 sum = 0.0;
                float weight = 1.0, total = 0.0;
                for (int s = 0; s < TAPS; s++)
                {
                    sum += Source(uv) * weight;
                    total += weight;
                    weight *= 0.94;
                    uv += stride;
                }
                return float4(sum / total, 1.0);
            }
            ENDCG
        }

        Pass
        {
            Blend One One, Zero One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return float4(Source(i.uv) * _ShaftParams.w, 0.0); }
            ENDCG
        }
    }
}
