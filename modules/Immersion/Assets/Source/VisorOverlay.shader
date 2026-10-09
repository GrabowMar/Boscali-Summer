Shader "Boscali/VisorOverlay"
{
    // One full-screen overlay quad for the glass-only helmet visor. Layers come from one
    // generated mask (R smudges, G breath fog, B helmet brow) and four scalar uniforms.
    Properties
    {
        [PerRendererData] _MainTex ("Visor layers (R smudge, G fog, B brow)", 2D) = "black" {}
        _TintColor ("Sun visor colour", Color) = (.30,.26,.20,1)
        _Tint ("Sun visor", Range(0,1)) = 0
        _SunLit ("Sun on smudges", Range(0,1)) = 0
        _Fog ("Breath fog", Range(0,1)) = 0
        _Brow ("Helmet brow", Range(0,1)) = 0
        _Light ("Ambient light", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _TintColor;
            float _Tint, _SunLit, _Fog, _Brow, _Light;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            Output vert(Input v)
            {
                Output o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            float4 frag(Output i) : SV_Target
            {
                float4 m = tex2D(_MainTex, i.uv);
                // Premultiplied "over" compositing, bottom to top: visor tint, fog, brow; sun glints add light.
                float tintA = _Tint * .12;
                float4 c = float4(_TintColor.rgb * tintA, tintA);
                float fogA = saturate(m.g * _Fog * .38);
                c = c * (1 - fogA) + float4(float3(.82, .85, .88) * lerp(.25, 1, _Light), 1) * fogA;
                float browA = saturate(m.b * _Brow * .85);
                c = c * (1 - browA) + float4(0, 0, 0, 1) * browA;
                c.rgb += float3(1, .93, .82) * saturate(m.r * _SunLit) * .35;
                return c;
            }
            ENDHLSL
        }
    }
}
