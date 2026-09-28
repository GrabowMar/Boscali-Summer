Shader "NOA/UI/Fx"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _NoiseTex ("Noise", 2D) = "gray" {}
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
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; float4 uv1 : TEXCOORD1; float4 uv2 : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 texcoord : TEXCOORD0; float4 worldPosition : TEXCOORD1; float4 rectUv : TEXCOORD2; float4 fx : TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO };

            sampler2D _MainTex; sampler2D _NoiseTex;
            fixed4 _Color; fixed4 _TextureSampleAdd; float4 _ClipRect; float4 _MainTex_ST;
            float _NOA_Now; float _NOA_Glitch; float _NOA_FxTier;

            float hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }

            v2f vert(appdata_t v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 pos = v.vertex;
                // EMP/jam line tear: horizontal jitter in bands, only at full tier.
                if (_NOA_Glitch > 0.001 && _NOA_FxTier > 1.5)
                {
                    float band = floor(pos.y * 0.08 + _NOA_Now * 23.0);
                    pos.x += (hash11(band) - 0.5) * 14.0 * _NOA_Glitch * step(0.7, hash11(band + 7.0));
                }
                o.worldPosition = pos;
                o.vertex = UnityObjectToClipPos(pos);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                o.rectUv = v.uv1;
                o.fx = v.uv2;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half4 color = (tex2D(_MainTex, i.texcoord) + _TextureSampleAdd) * i.color;
                float id = round(i.fx.x);
                float t = _NOA_Now - i.fx.y;
                float k = i.fx.z;
                float2 uv = i.rectUv.xy;
                float aspect = max(i.rectUv.z, 0.0001);

                if (_NOA_FxTier > 0.5)
                {
                    if (id == 1 && _NOA_FxTier > 1.5 && t >= 0 && t < 0.4) // shine
                    {
                        float p = uv.x + uv.y * 0.35;
                        float c = lerp(-0.35, 1.35, saturate(t / 0.4));
                        color.rgb += smoothstep(0.12, 0.0, abs(p - c)) * k * color.a;
                    }
                    else if (id == 2) // static edge glow (lite + full)
                    {
                        float e = min(min(uv.x, 1 - uv.x) * aspect, min(uv.y, 1 - uv.y));
                        color.rgb += exp(-e * max(i.fx.w, 1.0)) * k * i.color.rgb;
                        color.a = max(color.a, exp(-e * max(i.fx.w, 1.0)) * k * i.color.a);
                    }
                    else if (id == 3 && _NOA_FxTier > 1.5) // dissolve-in, 350 ms
                    {
                        float thr = saturate(t / 0.35);
                        float n = tex2D(_NoiseTex, uv * max(i.fx.w, 1.0)).r;
                        color.a *= step(n, thr);
                        color.rgb += smoothstep(thr - 0.08, thr, n) * step(n, thr) * k * (thr < 0.999); // no edge once fully in
                    }
                    else if (id == 4 && _NOA_FxTier > 1.5) // scan wipe, 250 ms, top-down
                    {
                        // param < 0.5: the graphic itself is revealed top-first.
                        // param >= 0.5 (cover): the graphic is an overlay that recedes downward, revealing what is under it.
                        float front = saturate(t / 0.25);
                        float edge = 1 - front;
                        float keep = i.fx.w > 0.5 ? step(uv.y, edge) : step(edge, uv.y);
                        float scan = smoothstep(0.035, 0.0, abs(uv.y - edge)) * k * (front < 1);
                        color *= keep;
                        color.rgb += scan * i.color.rgb;
                        color.a = max(color.a, scan * i.color.a);
                    }
                    else if (id == 5) // 1 Hz pulse
                    {
                        color.rgb *= 1 + k * (0.5 + 0.5 * sin(_NOA_Now * 6.2831853));
                    }
                }

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
