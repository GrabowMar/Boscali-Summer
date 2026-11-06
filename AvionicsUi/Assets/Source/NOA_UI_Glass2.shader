// Full-display screen finish (third generation, 2026-11-06): one premultiplied overlay quad over the whole maximized MFD.
// alpha darkens what is under it, rgb adds light. Every term is smooth and resolution-honest: no noise, smudges or grain.
//   _Reflection  coated cover glass: sky light pooled at the top, a soft canopy-bow band, a thin rim where glass meets bezel.
//   _Scan        phosphor raster: fine scanlines on physical pixels (1 dark row in 3) and a slow, faint refresh band.
//   _Edge        inset bezel shadow: contact shadow on every edge plus a rounded-corner falloff (tube or recessed glass).
//   _Light       ambient level (dims reflections at night).   _Life  power-on raster + refresh band + EMP motion.
//   _Wear        retired (kept so older settings and the build check stay valid).   _Frost  frosted fallback when blur is off.
// Technique after mob-sakai/UIEffect (one shared material, parameters per draw); no UIEffect code is copied.
// Built into avionics-fx.bundle by nomodkit tools/Compile-AvionicsFxAssets.ps1.
Shader "NOA/UI/Glass2"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _NoiseTex ("Noise", 2D) = "gray" {}
        _Reflection ("Reflection", Range(0,1)) = 0.5
        _Scan ("Scanlines", Range(0,1)) = 0
        _Wear ("Surface wear (retired)", Range(0,1)) = 0
        _Edge ("Bezel shadow", Range(0,1)) = 0
        _Light ("Ambient light", Range(0,1)) = 1
        _Frost ("Frost", Range(0,1)) = 0
        _Life ("Live signal", Range(0,1)) = 0.5
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
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 worldPosition : TEXCOORD1; float4 screen : TEXCOORD2; };

            sampler2D _NoiseTex; float4 _ClipRect;
            float _Reflection, _Scan, _Wear, _Edge, _Light, _Frost, _Life;
            float _NOA_Now, _NOA_Glitch, _NOA_FxTier, _NOA_Boot;

            float hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }
            float hash21(float2 p) { float3 q = frac(float3(p.xyx) * 0.1031); q += dot(q, q.yzx + 33.33); return frac((q.x + q.y) * q.z); }

            v2f vert(appdata_t v)
            {
                v2f o; o.worldPosition = v.vertex; o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord; o.color = v.color; o.screen = ComputeScreenPos(o.vertex); return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float2 sp = i.screen.xy / max(i.screen.w, 0.0001) * _ScreenParams.xy;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                half3 rgb = 0; half a = 0;
                float life = _Life * step(1.5, _NOA_FxTier);   // motion needs the Full tier; C# sends 0 under reduced motion
                float2 e = min(uv, 1.0 - uv) * float2(aspect, 1.0);   // distance to the nearest edge, square units

                // Glass: sky pooled at the top, the canopy bow as one soft diagonal band, and a hairline rim of light just
                // inside the top and left edges (the coating catching the cockpit). Smooth gradients only.
                float sky = exp(-(1.0 - uv.y) * 4.0) * 0.07;
                float band = uv.x * 0.55 + (1.0 - uv.y) * 0.85 - 0.55;
                float bow = smoothstep(0.30, 0.0, abs(band)); bow *= bow * 0.04;
                float rim = (exp(-(1.0 - uv.y) * 900.0) + exp(-uv.x * aspect * 900.0)) * 0.10;
                float lit = (sky + bow + rim) * lerp(0.35, 1.0, _Light) * _Reflection;
                rgb += lit * half3(0.90, 0.96, 1.0); a += lit * 0.6;

                // Bezel: a tight contact shadow on every edge and a rounded falloff into the corners.
                float contact = exp(-e.x * 60.0) + exp(-e.y * 60.0);
                float2 d = (uv - 0.5) * 2.0;                                             // -1..1 across the glass
                float corner = saturate(pow(abs(d.x), 6.0) + pow(abs(d.y), 6.0));       // superellipse: rounded rectangle
                a += saturate(contact * 0.45 + corner * 0.55) * 0.55 * _Edge;

                // Raster: one dark row in three physical pixels (crisp at any canvas scale) and a soft refresh band that
                // drifts down every twelve seconds.
                if (_Scan > 0.001)
                {
                    float row = step(fmod(floor(sp.y), 3.0), 0.5);
                    a += row * 0.20 * _Scan;
                    float drift = frac(_NOA_Now / 12.0);
                    float bandY = abs(frac(uv.y + drift) - 0.5);
                    float refresh = smoothstep(0.06, 0.0, bandY) * 0.035 * _Scan * life;
                    rgb += refresh * half3(0.75, 0.95, 1.0); a += refresh * 0.5;
                }

                // Frosted fallback (blur behind is off): an even milky veil, no texture.
                a += 0.16 * _Frost; rgb += 0.035 * _Frost;

                // EMP / jam / hit: rows of the picture drop out or tear, a dark hum bar climbs, the frame dips now and then.
                // Monochrome, so it reads as signal loss, not a colour party.
                if (_NOA_Glitch > 0.001)
                {
                    float G = _NOA_Glitch;
                    float beat = floor(_NOA_Now * 12.0);
                    float rowId = floor(sp.y / 14.0);
                    float bad = step(1.0 - 0.28 * G, hash11(rowId * 1.7 + beat * 5.3));
                    float span = hash11(rowId * 3.1 + beat);
                    float inSpan = step(abs(uv.x - span), 0.10 + 0.30 * hash11(rowId + beat * 2.3));
                    float bright = step(0.75, hash21(float2(rowId, beat)));
                    rgb += bad * inSpan * bright * 0.18 * G;
                    a += bad * inSpan * lerp(0.75, 0.15, bright) * G;
                    float hum = smoothstep(0.14, 0.0, abs(frac(uv.y - _NOA_Now * 0.55) - 0.5));
                    a += hum * 0.25 * G * max(life, 0.3);
                    a += step(0.92, hash11(beat + 19.0)) * 0.18 * G;
                }

                // Power-on: a line opens into the raster; outside it the screen is still dark.
                float bt = (_NOA_Now - _NOA_Boot) / 0.34;
                if (life > 0.001 && bt >= 0.0 && bt < 1.0)
                {
                    float halfH = 0.5 * smoothstep(0.12, 0.80, bt) + 0.003;
                    float halfW = 0.5 * smoothstep(0.0, 0.22, bt);
                    float dy = abs(uv.y - 0.5), dx = abs(uv.x - 0.5);
                    float inside = step(dy, halfH) * step(dx, halfW);
                    float fade = (1.0 - bt) * (1.0 - bt);
                    float edgeGlow = smoothstep(0.02, 0.0, abs(dy - halfH)) * step(dx, halfW);
                    float glow = (inside * 0.12 + edgeGlow * 0.45) * fade;
                    rgb = rgb * inside + glow;
                    a = lerp(1.0, a, inside) + glow;
                }

                a = saturate(a) * i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                return fixed4(saturate(rgb) * i.color.a, a);
            }
            ENDCG
        }
    }
}
