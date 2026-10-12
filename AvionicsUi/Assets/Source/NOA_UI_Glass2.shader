// Second-generation full-display finish. Everything NOA/UI/Glass did (sheen, scan texture, vignette, grain,
// frost, tint, jam tear) plus a live signal: a power-on raster, a slow refresh sweep and sync loss under jamming.
// One premultiplied overlay quad: alpha darkens what is under it, rgb adds light. Built into avionics-fx.bundle
// by tools/Compile-AvionicsFxAssets.ps1.
Shader "NOA/UI/Glass2"
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
        _Life ("Live signal", Range(0,1)) = 0.6
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
            float _Reflection, _Scan, _Edge, _TintStrength, _Light, _Frost, _Life; fixed4 _TintColor;
            float _NOA_Now, _NOA_Glitch, _NOA_FxTier, _NOA_Boot;

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
                // The moving parts need the Full tier; the C# side sends _Life = 0 under reduced motion.
                float life = _Life * step(1.5, _NOA_FxTier);

                // Reflection: a soft diagonal sheen, dimmer in low ambient light.
                float sheen = smoothstep(0.55, 0.0, abs(uv.x * 0.8 + (1 - uv.y) * 0.6 - 0.55)) * 0.07 * _Reflection * lerp(0.35, 1, _Light);
                rgb += sheen; a += sheen;

                // Scan texture at device pixels: one dark row in three, and a fainter column triad (shadow mask).
                a += step(2.0, fmod(sp.y, 3.0)) * 0.10 * _Scan;
                a += step(2.0, fmod(sp.x, 3.0)) * 0.035 * _Scan;

                // Refresh sweep: a soft band rolls down the tube every seven seconds with a short afterglow above it.
                float head = frac(_NOA_Now / 7.0);
                float behind = frac(head - (1.0 - uv.y));
                float sweep = exp(-behind * 26.0) * 0.020 * life;
                rgb += sweep; a += sweep;

                // Vignette.
                float2 d = uv - 0.5; float vig = saturate(dot(d, d) * 2.2);
                a += vig * vig * 0.55 * _Edge;

                // Grain (full tier only) and frost (blur fallback).
                if (_NOA_FxTier > 1.5) { float n = tex2D(_NoiseTex, sp / 64.0 + frac(_NOA_Now * 7.3)).r; a += (n - 0.5) * 0.025; }
                if (_Frost > 0.001) { float f = tex2D(_NoiseTex, sp / 96.0).r; rgb += f * 0.05 * _Frost; a += (0.18 + f * 0.08) * _Frost; }

                // Tint wash.
                rgb += _TintColor.rgb * 0.06 * _TintStrength; a += 0.06 * _TintStrength;

                // EMP / jam / hit: bright tear bands and a noise veil; with a live signal, blanking slabs that jump
                // nine times a second and a whole-frame dip, as if the picture were losing sync.
                if (_NOA_Glitch > 0.001)
                {
                    float band = floor(sp.y / 6.0 + _NOA_Now * 31.0);
                    float on = step(0.86, hash11(band)) * _NOA_Glitch;
                    rgb += on * half3(0.55, 0.9, 1.0) * 0.25; a += on * 0.18;
                    a += (tex2D(_NoiseTex, sp / 48.0 + _NOA_Now).r - 0.5) * 0.12 * _NOA_Glitch;
                    float beat = floor(_NOA_Now * 9.0);
                    float slab = step(0.88, hash11(floor(uv.y * 11.0) + beat * 3.7));
                    float dip = step(0.90, hash11(beat + 19.0));
                    a += (slab * 0.50 + dip * 0.20) * _NOA_Glitch * life;
                }

                // Power-on: a bright line widens, opens into the raster, and its bloom dies away. Outside the raster
                // the tube is still dark.
                float bt = (_NOA_Now - _NOA_Boot) / 0.34;
                if (life > 0.001 && bt >= 0.0 && bt < 1.0)
                {
                    float halfH = 0.5 * smoothstep(0.12, 0.80, bt) + 0.003;
                    float halfW = 0.5 * smoothstep(0.0, 0.22, bt);
                    float dy = abs(uv.y - 0.5), dx = abs(uv.x - 0.5);
                    float inside = step(dy, halfH) * step(dx, halfW);
                    float fade = (1.0 - bt) * (1.0 - bt);
                    float rim = smoothstep(0.02, 0.0, abs(dy - halfH)) * step(dx, halfW);
                    float glow = (inside * 0.16 + rim * 0.55) * fade;
                    rgb = rgb * inside + glow;
                    a = lerp(1.0, a, inside) + glow;
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
