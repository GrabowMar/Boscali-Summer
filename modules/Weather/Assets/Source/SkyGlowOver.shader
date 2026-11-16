// The alpha-over twin of Boscali/SkyGlow, for sky glows that hide what lies behind them: the EMP fireball by day (added to a blue sky, its orange
// washed out to white, 2026-10-28). Premultiplied: colour x texture x alpha, alpha = texture alpha x colour alpha. No fog, like SkyGlow.
// The material sets its queue (2996); the EMP draws it as the first submesh of its sky mesh, so the additive glows land on top.
Shader "Boscali/SkyGlowOver"
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
            Blend One OneMinusSrcAlpha, Zero One
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
                float a = saturate(t.a * i.color.a);
                return float4(i.color.rgb * t.rgb * (a * _Intensity), a);
            }
            ENDCG
        }
    }
}
