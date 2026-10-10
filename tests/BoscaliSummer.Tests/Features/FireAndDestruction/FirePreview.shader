Shader "Boscali/OfflineFirePreview"
{
    Properties { _MainTex ("Native opacity", 2D) = "white" {} _EmissionTex ("Native emission", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _EmissionTex;
            struct Input { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Output { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            Output vert(Input v) { Output o; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o; }
            float4 frag(Output i):SV_Target
            {
                float4 opacity=tex2D(_MainTex,i.uv);
                float3 emission=tex2D(_EmissionTex,i.uv).rgb;
                // Native fire_small1 uses this HDR emission multiplier. A simple
                // tone map substitutes for the game's URP exposure/bloom chain.
                float3 hdr=emission*i.color.rgb*67.793518;
                return float4(hdr/(1.0+hdr),opacity.a*i.color.a);
            }
            ENDCG
        }
    }
}
