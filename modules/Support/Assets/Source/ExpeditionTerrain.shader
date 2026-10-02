Shader "BoscaliSummer/OPS/TerrainRelief"
{
    Properties
    {
        [PerRendererData] _MainTex ("Terrain", 2D) = "white" {}
        _Low ("Shadow", Color) = (0.14,0.18,0.16,1)
        _High ("Relief", Color) = (0.68,0.65,0.50,1)
        _Sea ("Water", Color) = (0.045,0.085,0.11,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 world:TEXCOORD1; };
            sampler2D _MainTex;
            float4 _Low, _High, _Sea, _ClipRect;
            v2f vert(appdata v)
            {
                v2f o; o.world=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 native = tex2D(_MainTex, i.uv);
                float luminance = dot(native.rgb, float3(0.2126,0.7152,0.0722));
                // Contrast grade of the source imagery only: no invented elevation, roads or contacts.
                float relief = pow(saturate(luminance), 0.65);
                float3 shadow = lerp(_Sea.rgb, _Low.rgb, 0.35);
                fixed4 c = fixed4(lerp(shadow, _High.rgb, relief), native.a) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
