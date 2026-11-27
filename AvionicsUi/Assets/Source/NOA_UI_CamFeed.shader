// Lightweight single-pass camera feed look for TARGET CAM, MISSILE CAM, and DRONE CAM (2026-11-27).
// One texture sample, pure ALU, zero extra textures or passes. Built into avionics-fx.bundle.
Shader "NOA/UI/CamFeed"
{
    Properties
    {
        [PerRendererData] _MainTex ("Feed Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Mode ("Mode (0 Target, 1 Missile, 2 Drone EO, 3 Drone IR)", Float) = 0
        _Thermal ("Thermal Blend", Range(0,1)) = 0
        _Signal ("Signal Quality", Range(0,1)) = 1
        _UvRect ("UV Crop Rect (x, y, invW, invH)", Vector) = (0,0,1,1)
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
        Blend SrcAlpha OneMinusSrcAlpha
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
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 screen : TEXCOORD2;
                float2 cardUv : TEXCOORD3;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _UvRect;
            float _Mode, _Thermal, _Signal;

            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float hash21(float2 p)
            {
                float3 q = frac(float3(p.xyx) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.vertex);
                o.uv = v.texcoord;
                o.cardUv = (v.texcoord - _UvRect.xy) * _UvRect.zw;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 sp = i.screen.xy / max(i.screen.w, 0.0001) * _ScreenParams.xy;
                float t15 = floor(_Time.y * 15.0);
                float loss = saturate(1.0 - _Signal);

                float2 sampleUv = i.uv;
                if (loss > 0.01)
                {
                    float row = floor(sp.y * 0.5);
                    float tearSeed = hash11(row * 1.73 + t15 * 7.13);
                    float tearOn = step(1.0 - loss * 0.35, tearSeed);
                    float jitter = (hash11(row * 3.11 + t15 * 19.3) - 0.5) * 0.05 * loss * tearOn;
                    float minU = _UvRect.x;
                    float maxU = _UvRect.x + 1.0 / max(_UvRect.z, 0.0001);
                    sampleUv.x = clamp(sampleUv.x + jitter, minU, maxU);
                }

                fixed4 tex = tex2D(_MainTex, sampleUv);
                float3 col = tex.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                col = sqrt(max(col, 0.0));
                #endif

                float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
                float scanBit = step(0.5, frac(sp.y * 0.5));
                float n = hash21(floor(sp * 0.5) + t15 * 13.7);
                float2 d = abs(i.cardUv - 0.5) * 2.0;
                float2 cd = (i.cardUv - 0.5) * float2(1.45, 1.15);

                if (_Mode < 0.5)
                {
                    // Target Cam (TGP): EO optical feed blended cleanly with FLIR white-hot by _Thermal.
                    float3 eo = lerp(col, lum.xxx, 0.18);
                    eo = lerp(eo, smoothstep(0.03, 0.97, eo), 0.30) * float3(0.96, 1.0, 0.98);
                    float hot = smoothstep(0.04, 0.92, lum);
                    hot = saturate(hot * 1.06 + hot * hot * hot * 0.14);
                    float3 ir = hot.xxx * float3(0.92, 0.98, 0.96);
                    col = lerp(eo, ir, _Thermal);
                    col *= 1.0 - 0.07 * scanBit;
                    float rectEdge = max(d.x, d.y);
                    col *= 1.0 - 0.22 * (rectEdge * rectEdge * rectEdge);
                    col += (n - 0.5) * lerp(0.03, 0.045, _Thermal);
                }
                else if (_Mode < 1.5)
                {
                    // Missile Cam: crisp tactical optical seeker datalink with sync sweep and dome vignette.
                    float3 sk = lerp(col, lum.xxx, 0.45);
                    sk = ((sk - 0.5) * 1.18 + 0.5) * float3(0.90, 1.02, 0.96);
                    float syncBand = smoothstep(0.07, 0.0, abs(frac(i.cardUv.y + _Time.y * 0.28) - 0.5)) * 0.045;
                    col = sk * (1.0 - 0.10 * scanBit + syncBand);
                    col *= 1.0 - 0.34 * saturate(dot(cd, cd));
                    col += (n - 0.5) * 0.055;
                }
                else if (_Mode < 2.5)
                {
                    // Drone Cam EO DAY: clean daylight reconnaissance optics.
                    float3 eo = lerp(col, lum.xxx, 0.22);
                    eo = lerp(eo, smoothstep(0.02, 0.98, eo), 0.22) * float3(0.96, 0.99, 1.0);
                    col = eo * (1.0 - 0.06 * scanBit);
                    col *= 1.0 - 0.18 * saturate(dot(d * d, float2(0.5, 0.5)));
                    col += (n - 0.5) * 0.028;
                }
                else
                {
                    // Drone Cam IR WHOT: microbolometer white-hot FLIR.
                    float th = smoothstep(0.05, 0.90, lum);
                    th = saturate(th * 1.05 + th * th * th * 0.16);
                    col = th.xxx * float3(0.95, 0.98, 1.02);
                    col *= 1.0 - 0.075 * scanBit;
                    col *= 1.0 - 0.26 * saturate(dot(cd, cd));
                    col += (n - 0.5) * 0.045;
                }

                if (loss > 0.01)
                {
                    float roll = smoothstep(0.08, 0.0, abs(frac(i.cardUv.y - _Time.y * 0.75) - 0.5));
                    col *= lerp(1.0, 0.42, loss);
                    col = lerp(col, (n * 0.85 + 0.08).xxx, loss * 0.55) + roll * loss * 0.12;
                }

                col = saturate(col);
                #ifndef UNITY_COLORSPACE_GAMMA
                col *= col;
                #endif

                fixed4 outCol = fixed4(col, tex.a) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                outCol.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(outCol.a - 0.001);
                #endif
                return outCol;
            }
            ENDCG
        }
    }
}
