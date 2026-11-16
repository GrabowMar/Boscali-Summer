// BROKEN HELMET (2026-11-16): the visor's glitch for the HUD's words. A modified copy of TextMeshPro's "Distance Field" shader (see the core include for what
// changed and PROVENANCE.txt for the source and licence): the same face, outline and underlay halo, plus the tear, ghost split, dropout blocks, shimmer, bars and
// flicker of NOA_VisorGlitch.cginc. Premultiplied like the stock shader; the ghosts add (Blend One One). Built into avionics-fx.bundle by tools/Compile-AvionicsFxAssets.ps1.
// VanillaHudStyle.Glow makes its material copies on this shader when the bundle has it, and on the stock shader when not.
Shader "NOA/TMP/VisorGlitchSDF"
{
    Properties
    {
        _FaceTex ("Face Texture", 2D) = "white" {}
        _FaceUVSpeedX ("Face UV Speed X", Range(-5, 5)) = 0.0
        _FaceUVSpeedY ("Face UV Speed Y", Range(-5, 5)) = 0.0
        [HDR]_FaceColor ("Face Color", Color) = (1,1,1,1)
        _FaceDilate ("Face Dilate", Range(-1,1)) = 0

        [HDR]_OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineTex ("Outline Texture", 2D) = "white" {}
        _OutlineUVSpeedX ("Outline UV Speed X", Range(-5, 5)) = 0.0
        _OutlineUVSpeedY ("Outline UV Speed Y", Range(-5, 5)) = 0.0
        _OutlineWidth ("Outline Thickness", Range(0, 1)) = 0
        _OutlineSoftness ("Outline Softness", Range(0,1)) = 0

        [HDR]_UnderlayColor ("Border Color", Color) = (0,0,0, 0.5)
        _UnderlayOffsetX ("Border OffsetX", Range(-1,1)) = 0
        _UnderlayOffsetY ("Border OffsetY", Range(-1,1)) = 0
        _UnderlayDilate ("Border Dilate", Range(-1,1)) = 0
        _UnderlaySoftness ("Border Softness", Range(0,1)) = 0

        _WeightNormal ("Weight Normal", float) = 0
        _WeightBold ("Weight Bold", float) = 0.5

        _ShaderFlags ("Flags", float) = 0
        _ScaleRatioA ("Scale RatioA", float) = 1
        _ScaleRatioB ("Scale RatioB", float) = 1
        _ScaleRatioC ("Scale RatioC", float) = 1

        _MainTex ("Font Atlas", 2D) = "white" {}
        _TextureWidth ("Texture Width", float) = 512
        _TextureHeight ("Texture Height", float) = 512
        _GradientScale ("Gradient Scale", float) = 5.0
        _ScaleX ("Scale X", float) = 1.0
        _ScaleY ("Scale Y", float) = 1.0
        _PerspectiveFilter ("Perspective Correction", Range(0, 1)) = 0.875
        _Sharpness ("Sharpness", Range(-1,1)) = 0

        _VertexOffsetX ("Vertex OffsetX", float) = 0
        _VertexOffsetY ("Vertex OffsetY", float) = 0

        _MaskCoord ("Mask Coordinates", vector) = (0, 0, 32767, 32767)
        _ClipRect ("Clip Rect", vector) = (-32767, -32767, 32767, 32767)
        _MaskSoftnessX ("Mask SoftnessX", float) = 0
        _MaskSoftnessY ("Mask SoftnessY", float) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _CullMode ("Cull Mode", Float) = 0
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull [_CullMode]
        ZWrite Off
        Lighting Off
        Fog { Mode Off }
        ZTest [unity_GUIZTestMode]
        ColorMask [_ColorMask]

        Pass
        {
            Name "GhostRed"
            Blend One One
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex VertShader
            #pragma fragment PixShader
            #pragma multi_compile_local __ UNDERLAY_ON
            #pragma multi_compile_local __ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local __ UNITY_UI_ALPHACLIP
            #define BH_PASS 1
            #include "NOA_TMP_VisorGlitchSDFCore.cginc"
            ENDCG
        }
        Pass
        {
            Name "GhostCyan"
            Blend One One
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex VertShader
            #pragma fragment PixShader
            #pragma multi_compile_local __ UNDERLAY_ON
            #pragma multi_compile_local __ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local __ UNITY_UI_ALPHACLIP
            #define BH_PASS 2
            #include "NOA_TMP_VisorGlitchSDFCore.cginc"
            ENDCG
        }
        Pass
        {
            Name "Main"
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex VertShader
            #pragma fragment PixShader
            #pragma multi_compile_local __ UNDERLAY_ON
            #pragma multi_compile_local __ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local __ UNITY_UI_ALPHACLIP
            #define BH_PASS 0
            #include "NOA_TMP_VisorGlitchSDFCore.cginc"
            ENDCG
        }
    }
    Fallback "TextMeshPro/Mobile/Distance Field"
}
