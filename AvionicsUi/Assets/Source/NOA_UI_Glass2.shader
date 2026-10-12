// Second-generation full-display finish: a cover glass over the whole maximized MFD.
//   Reflection - sky light pooled at the top, the canopy bow as a soft diagonal band, and the sun as a glare
//                spot that slides across the glass as the aircraft turns (_NOA_Glare, set by AvDisplayGlass).
//   Wear       - wipe smudges and dust that only show where the glass catches light, plus grime in the corners.
//   Edge       - the shadow the bezel lip casts onto the recessed glass.
//   Live       - a power-on raster, a refresh line, grain, and block corruption under jamming or a hit.
// One premultiplied overlay quad: alpha darkens what is under it, rgb adds light. Built into avionics-fx.bundle
// by tools/Compile-AvionicsFxAssets.ps1.
Shader "NOA/UI/Glass2"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _NoiseTex ("Noise", 2D) = "gray" {}
        _Reflection ("Reflection", Range(0,1)) = 0.6
        _Wear ("Surface wear", Range(0,1)) = 0
        _Edge ("Bezel shadow", Range(0,1)) = 0
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
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 worldPosition : TEXCOORD1; float4 screen : TEXCOORD2; };

            sampler2D _NoiseTex; float4 _ClipRect;
            float _Reflection, _Wear, _Edge, _Light, _Frost, _Life;
            float _NOA_Now, _NOA_Glitch, _NOA_FxTier, _NOA_Boot;
            // xy: where the sun's reflection sits (uv, may be off the glass), z: its strength, w: roll (-1..1).
            float4 _NOA_Glare;

            float hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }
            float hash21(float2 p) { float3 q = frac(float3(p.xyx) * 0.1031); q += dot(q, q.yzx + 33.33); return frac((q.x + q.y) * q.z); }
            // Smooth value noise: the smudge field must not depend on how the bundle's noise texture is filtered.
            float vnoise(float2 p)
            {
                float2 c = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(c), hash21(c + float2(1, 0)), f.x),
                            lerp(hash21(c + float2(0, 1)), hash21(c + float2(1, 1)), f.x), f.y);
            }

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
                float2 q = float2(uv.x * aspect, uv.y);          // square units, one = the glass height
                half3 rgb = 0; half a = 0;
                // The moving parts need the Full tier; the C# side sends _Life = 0 under reduced motion.
                float life = _Life * step(1.5, _NOA_FxTier);
                float roll = _NOA_Glare.w;

                // Wear field: broad wipe arcs (stretched noise along a shallow curve) times blotchy smudges.
                float2 w = q + float2(0.0, 0.18 * sin(q.x * 2.3 + 0.7));
                float wipe = vnoise(float2(w.x * 1.3, w.y * 15.0));
                float blot = vnoise(q * 3.1 + 7.3) * 0.65 + vnoise(q * 8.7 + 2.1) * 0.35;
                float smudge = saturate(wipe * 0.9 + blot * 0.8 - 0.55);
                float dust = step(0.9965, hash21(floor(sp * 0.5)));

                // Reflection. Sky light pools along the top and tips over with roll; the canopy bow is a soft
                // diagonal band; the sun is a spot with a wide halo.
                float down = (1.0 - uv.y) + (uv.x - 0.5) * roll * 0.7;
                float sky = exp(-max(down, 0.0) * 3.4) * 0.085;
                float bandAt = uv.x * 0.50 + (1.0 - uv.y) * 0.86 - 0.52 - roll * 0.18;
                float bow = smoothstep(0.34, 0.0, abs(bandAt)); bow = bow * bow * 0.055;
                float2 g = (uv - _NOA_Glare.xy) * float2(aspect, 1.0);
                float gd = dot(g, g);
                float sun = (exp(-gd * 7.0) * 0.20 + exp(-gd * 70.0) * 0.30) * _NOA_Glare.z;
                float lit = (sky + bow) * lerp(0.30, 1.0, _Light) + sun;
                // Clean glass reflects evenly; worn glass scatters: dimmer between the smudges, brighter on them.
                float scatter = lerp(1.0, 0.45 + smudge * 2.6, _Wear);
                float refl = lit * scatter * _Reflection;
                rgb += refl * half3(0.93, 0.97, 1.0) + sun * _Reflection * half3(0.10, 0.06, 0.0);
                a += refl * 0.75;
                // Dust only shows where light rakes the glass.
                float speck = dust * _Wear * saturate(0.03 + lit * 2.2);
                rgb += speck; a += speck;

                // Bezel shadow: a tight contact shadow along every edge, a longer one under the top lip, and a
                // little corner falloff. Wear adds grime where a cloth never reaches.
                float2 e = min(uv, 1.0 - uv) * float2(aspect, 1.0);
                float contact = exp(-e.x * 46.0) + exp(-e.y * 46.0);
                float lip = exp(-(1.0 - uv.y) * 11.0) * 0.55 + exp(-uv.x * aspect * 14.0) * 0.25;
                float2 d = uv - 0.5; float corner = saturate(dot(d, d) * 2.2); corner *= corner;
                a += saturate(contact * 0.55 + lip * 0.45 + corner * 0.45) * 0.62 * _Edge;
                a += corner * (0.25 + blot * 0.5) * 0.28 * _Wear;

                // Refresh: every nine seconds a thin line runs down the glass in under a second, with a short tail.
                float cycle = frac(_NOA_Now / 9.0) * 9.0;
                float behind = cycle / 0.8 - (1.0 - uv.y);
                float sweep = step(0.0, behind) * step(behind, 1.0) * exp(-behind * 60.0) * 0.07 * life;
                rgb += sweep * half3(0.8, 0.95, 1.0); a += sweep;

                // Grain (full tier only) and frost (blur fallback).
                if (_NOA_FxTier > 1.5) { float n = tex2D(_NoiseTex, sp / 64.0 + frac(_NOA_Now * 7.3)).r; a += (n - 0.5) * 0.03 * (0.4 + life); }
                if (_Frost > 0.001) { float f = tex2D(_NoiseTex, sp / 96.0).r; rgb += f * 0.05 * _Frost; a += (0.18 + f * 0.08) * _Frost; }

                // EMP / jam / hit: the picture corrupts in blocks. Some rows of cells go bad twelve times a second;
                // in a bad row cells light up cyan or magenta or drop out, a dark hum bar climbs the glass, static
                // veils it, and now and then the whole frame dips.
                if (_NOA_Glitch > 0.001)
                {
                    float G = _NOA_Glitch;
                    float beat = floor(_NOA_Now * 12.0);
                    float2 cell = floor(sp / float2(56.0, 14.0));
                    float rowBad = step(1.0 - 0.30 * G, hash11(cell.y * 1.7 + beat * 5.3));
                    float span = hash11(cell.y * 3.1 + beat);                      // each bad row tears a different stretch
                    float inSpan = step(abs(uv.x - span), 0.12 + 0.30 * hash11(cell.y + beat * 2.3));
                    float pick = hash21(cell + beat);
                    float block = rowBad * inSpan * step(0.30, pick);
                    float bright = step(0.62, pick);
                    half3 ink = lerp(half3(0.25, 0.90, 1.0), half3(1.0, 0.25, 0.60), step(0.90, pick));
                    rgb += block * bright * ink * 0.30 * G;
                    a += block * lerp(0.80 * max(life, 0.25), 0.25, bright) * G;
                    float hum = smoothstep(0.16, 0.0, abs(frac(uv.y - _NOA_Now * 0.55) - 0.5));
                    a += hum * 0.30 * G * life;
                    float st = tex2D(_NoiseTex, sp / 48.0 + _NOA_Now * 3.1).r;
                    a += (st - 0.5) * 0.16 * G; rgb += saturate(st - 0.82) * 0.9 * G;
                    a += step(0.90, hash11(beat + 19.0)) * 0.22 * G * life;
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
