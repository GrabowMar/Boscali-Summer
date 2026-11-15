Shader "Hidden/Boscali/CinematicStreak"
{
    Properties { _MainTex ("Scene", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        sampler2D _BoscaliStreakTex;
        // x: raw HDR threshold, y: extracted HDR ceiling, z: strength, w: tap spacing in low-res pixels.
        float4 _StreakParams;

        float3 Highlight(float2 uv)
        {
            float3 hdrColor = max(tex2D(_MainTex, uv).rgb, 0.0);
            float peak = max(max(hdrColor.r, hdrColor.g), hdrColor.b);
            float bright = min(max(peak - _StreakParams.x, 0.0), _StreakParams.y);
            return hdrColor * (bright / max(peak, 0.00001));
        }

        float4 Extract(v2f_img input) : SV_Target
        {
            float2 pixel = abs(_MainTex_TexelSize.xy);
            float3 light = Highlight(input.uv + pixel * float2(-1, -1));
            light += Highlight(input.uv + pixel * float2(1, -1));
            light += Highlight(input.uv + pixel * float2(-1, 1));
            light += Highlight(input.uv + pixel * float2(1, 1));
            return float4(light * 0.25, 1.0);
        }

        float3 Tap(float2 uv)
        {
            // Reject taps outside the frame: clamping would duplicate an edge light.
            return all(uv >= 0.0) && all(uv <= 1.0) ? tex2D(_MainTex, uv).rgb : 0.0;
        }

        float4 Blur(v2f_img input) : SV_Target
        {
            float2 stepUv = float2(abs(_MainTex_TexelSize.x) * _StreakParams.w, 0.0);
            float3 light = Tap(input.uv) * 0.28;
            light += (Tap(input.uv - stepUv) + Tap(input.uv + stepUv)) * 0.12;
            light += (Tap(input.uv - stepUv * 2.0) + Tap(input.uv + stepUv * 2.0)) * 0.09;
            light += (Tap(input.uv - stepUv * 3.0) + Tap(input.uv + stepUv * 3.0)) * 0.065;
            light += (Tap(input.uv - stepUv * 4.0) + Tap(input.uv + stepUv * 4.0)) * 0.045;
            light += (Tap(input.uv - stepUv * 5.0) + Tap(input.uv + stepUv * 5.0)) * 0.025;
            return float4(light / 0.97, 1.0);
        }

        float4 Compose(v2f_img input) : SV_Target
        {
            float4 scene = tex2D(_MainTex, input.uv);
            scene.rgb += tex2D(_BoscaliStreakTex, input.uv).rgb * _StreakParams.z;
            return scene;
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Extract
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Blur
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Compose
            ENDCG
        }
    }
    Fallback Off
}
