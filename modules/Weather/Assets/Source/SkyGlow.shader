// Additive unlit glow for the night-sky particles (aurora curtains, red sprites, blue jets; SkyPhenomena).
// No fog: they hang far above the haze. Particle colour times the texture times _Intensity (HDR, so bloom picks them up).
// The material sets its queue (2996: after the stars at 2995, before the cloud composite at 2997, which then hides them behind cloud).
Shader "Boscali/SkyGlow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-4" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off ZTest LEqual
            Blend One One, Zero One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Intensity;
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv);
                return float4(i.color.rgb * t.rgb * (t.a * i.color.a * _Intensity), 0.0);
            }
            ENDCG
        }
    }
}
