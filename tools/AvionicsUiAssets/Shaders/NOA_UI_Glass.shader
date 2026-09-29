Shader "NOA/UI/Glass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _NoiseTex ("Noise", 2D) = "gray" {}
        _Reflection ("Reflection", Range(0,1)) = 0.6
        _Scan ("Scanlines", Range(0,1)) = 0
        _Edge ("Vignette", Range(0,1)) = 0
        _TintColor ("Tint", Color) = (1,1,1,1)
        _TintStrength ("Tint Strength", Range(0,1)) = 0
        _Light ("Ambient light", Range(0,1)) = 1
        _Frost ("Frost", Range(0,1)) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 worldPosition : TEXCOORD1; float4 screen : TEXCOORD2; };

            sampler2D _NoiseTex; float4 _ClipRect;
            float _Reflection, _Scan, _Edge, _TintStrength, _Light, _Frost; fixed4 _TintColor;
            float _NOA_Now, _NOA_Glitch, _NOA_FxTier;

            float hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }

            v2f vert(appdata_t v)
            {
                v2f o; o.worldPosition = v.vertex; o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord; o.color = v.color; o.screen = ComputeScreenPos(o.vertex); return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float2 sp = i.screen.xy / max(i.screen.w, 0.0001) * _ScreenParams.xy;
                half3 rgb = 0; half a = 0;

                // Reflection: a soft diagonal sheen, dimmer in low ambient light.
                float sheen = smoothstep(0.55, 0.0, abs(uv.x * 0.8 + (1 - uv.y) * 0.6 - 0.55)) * 0.07 * _Reflection * lerp(0.35, 1, _Light);
                rgb += sheen; a += sheen;

                // Scanlines at device pixels: one dark line every 3 px.
                float scanRow = step(2.0, fmod(sp.y, 3.0));
                a += scanRow * 0.10 * _Scan;

                // Vignette.
                float2 d = uv - 0.5; float vig = saturate(dot(d, d) * 2.2);
                a += vig * vig * 0.55 * _Edge;

                // Grain (full tier only) and frost (blur fallback).
                if (_NOA_FxTier > 1.5) { float n = tex2D(_NoiseTex, sp / 64.0 + frac(_NOA_Now * 7.3)).r; a += (n - 0.5) * 0.025; }
                if (_Frost > 0.001) { float f = tex2D(_NoiseTex, sp / 96.0).r; rgb += f * 0.05 * _Frost; a += (0.18 + f * 0.08) * _Frost; }

                // Tint wash.
                rgb += _TintColor.rgb * 0.06 * _TintStrength; a += 0.06 * _TintStrength;

                // EMP/jam: bright tear bands + a noise veil.
                if (_NOA_Glitch > 0.001)
                {
                    float band = floor(sp.y / 6.0 + _NOA_Now * 31.0);
                    float on = step(0.86, hash11(band)) * _NOA_Glitch;
                    rgb += on * half3(0.55, 0.9, 1.0) * 0.25; a += on * 0.18;
                    a += (tex2D(_NoiseTex, sp / 48.0 + _NOA_Now).r - 0.5) * 0.12 * _NOA_Glitch;
                }

                a = saturate(a) * i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                // Premultiplied: the dark part of the overlay is alpha with no colour.
                return fixed4(saturate(rgb) * i.color.a, a);
            }
            ENDCG
        }
    }
}
