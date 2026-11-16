// BROKEN HELMET (2026-11-16): the visor's glitch for the mod's HUD line art and sprites. UI/Default plus a tear, a chromatic split, dropout blocks,
// scanline shimmer, rolling bars and a flicker, all driven by the globals in NOA_VisorGlitch.cginc (set once a frame; the material carries none).
// With _BH_Fx.x = 0 it draws what UI/Default draws. Three passes: a red ghost left, a cyan ghost right (both vanish unless a split is asked for), then the picture.
// One shared material; rect clip and stencil as the stock UI shader, so RectMask2D and Mask keep working. Built into avionics-fx.bundle by tools/Compile-AvionicsFxAssets.ps1.
Shader "NOA/UI/VisorGlitch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        ColorMask [_ColorMask]

        Pass
        {
            Name "GhostRed"
            Blend SrcAlpha One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #define BH_PASS 1
            #include "NOA_UI_VisorGlitchCore.cginc"
            ENDCG
        }
        Pass
        {
            Name "GhostCyan"
            Blend SrcAlpha One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #define BH_PASS 2
            #include "NOA_UI_VisorGlitchCore.cginc"
            ENDCG
        }
        Pass
        {
            Name "Main"
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #define BH_PASS 0
            #include "NOA_UI_VisorGlitchCore.cginc"
            ENDCG
        }
    }
}
