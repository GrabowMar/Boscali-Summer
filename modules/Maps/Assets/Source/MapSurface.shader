Shader "Boscali/MapSurface"
{
    // Terrain, runway and scenery of a Boscali map: the colour is baked into the vertices, so a map needs no textures.
    // Vertex alpha is how much a vertex sways in the wind (0 for ground, up to 1 at a palm's crown).
    Properties
    {
        _Sway ("Wind Sway (m)", Float) = 0
        _Cull ("Cull", Float) = 2
        _Detail ("Detail Contrast", Range(0,1)) = .35
        [Toggle(_DETAIL)] _DetailSurfaces ("Generated detail and decals", Float) = 0
        _DetailTex ("Detail", 2D) = "grey" {}
        _DetailNormal ("Detail normal", 2D) = "bump" {}
        _DetailScale ("Detail repeats per metre", Float) = .35
        _DetailStrength ("Detail strength", Range(0,1)) = .6
        _DecalTex ("Decal atlas", 2D) = "white" {}
        _DecalStrength ("Decal strength", Range(0,1)) = 1
        [Toggle(_TEXTURED)] _Textured ("Surfaces by vertex alpha", Float) = 0
        _TexA ("Concrete (alpha 1)", 2D) = "white" {}
        _TexB ("Sheet metal (alpha 0)", 2D) = "white" {}
        _TexC ("Gabion (alpha .5)", 2D) = "white" {}
        _TexTile ("Repeats per metre", Vector) = (.0625, .05, .16, 0)
        _TexGain ("Brightness to middle grey", Vector) = (1, 1, 1, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float _Sway, _Cull, _Detail, _Textured;
        float _DetailSurfaces, _DetailScale, _DetailStrength, _DecalStrength;
        float4 _TexTile, _TexGain;
        CBUFFER_END
        float3 SwayedWS(float3 positionOS, half weight)
        {
            float3 positionWS = TransformObjectToWorld(positionOS);
            float phase = _Time.y * 1.1 + positionOS.x * .05 + positionOS.z * .07;
            positionWS.xz += float2(sin(phase), cos(phase * .8)) * (_Sway * weight);
            return positionWS;
        }
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma shader_feature_local _TEXTURED
            #pragma shader_feature_local _DETAIL
            TEXTURE2D(_TexA); TEXTURE2D(_TexB); TEXTURE2D(_TexC); SAMPLER(sampler_TexA);
            TEXTURE2D(_DetailTex); TEXTURE2D(_DetailNormal); TEXTURE2D(_DecalTex);
            SAMPLER(sampler_DetailTex); SAMPLER(sampler_DetailNormal); SAMPLER(sampler_DecalTex);
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half4 color : COLOR;
                float fog : TEXCOORD3;
                float2 uv : TEXCOORD4;
            };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = SwayedWS(v.positionOS, v.color.a);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS;
                o.color = v.color;
                o.uv = v.uv;
                #ifdef _TEXTURED
                o.positionWS = TransformObjectToWorld(v.positionOS); // alpha picks a surface here, it is not sway
                o.positionCS = TransformWorldToHClip(o.positionWS);
                #endif
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }
            float Grain(float2 p) { return Noise(p * .9) * .6 + Noise(p * .11) * .4; }
            half4 frag(Varyings i, half facing : VFACE) : SV_Target
            {
                half3 normal = normalize(i.normalWS) * (facing < 0 ? -1 : 1);
                // Object-space grain at two scales: it stays put when the floating origin shifts the map. Its relief
                // as well as its colour, faded with distance (a frequency below a pixel only shimmers) — the same
                // field perturbs the normal so kit rock and markings catch the light at close range.
                half grain = Grain(i.positionOS.xz);
                half3 albedo = i.color.rgb * (1 - _Detail * .5 + _Detail * grain);
                float fade = saturate(300.0 / distance(_WorldSpaceCameraPos, i.positionWS));
                half decal = step(.72, i.color.a) * step(i.color.a, .78);
                #ifdef _DETAIL
                half3 detail = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, i.uv * _DetailScale).rgb;
                albedo *= lerp(1, detail * 1.08, _DetailStrength * fade * (1 - decal));
                if (decal < .5)
                {
                    half3 dn = SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, i.uv * _DetailScale).xyz * 2 - 1;
                    half3 dp1 = ddx(i.positionWS), dp2 = ddy(i.positionWS);
                    float2 duv1 = ddx(i.uv), duv2 = ddy(i.uv);
                    half3 dp2perp = cross(dp2, normal), dp1perp = cross(normal, dp1);
                    half3 t = dp2perp * duv1.x + dp1perp * duv2.x;
                    half3 bt = dp2perp * duv1.y + dp1perp * duv2.y;
                    float invmax = rsqrt(max(dot(t, t), dot(bt, bt)));
                    normal = normalize(t * dn.x * invmax + bt * dn.y * invmax + normal * dn.z);
                }
                #endif
                #ifdef _TEXTURED
                half3 concrete = SAMPLE_TEXTURE2D(_TexA, sampler_TexA, i.uv * _TexTile.x).rgb * _TexGain.x;
                half3 metal = SAMPLE_TEXTURE2D(_TexB, sampler_TexA, i.uv * _TexTile.y).rgb * _TexGain.y;
                half3 gabion = SAMPLE_TEXTURE2D(_TexC, sampler_TexA, i.uv * _TexTile.z).rgb * _TexGain.z;
                albedo *= i.color.a > .85 ? concrete : i.color.a < .25 ? metal : i.color.a > .4 && i.color.a < .6 ? gabion : half3(1, 1, 1);
                #endif
                #ifdef _DETAIL
                if (decal > .5)
                {
                    half4 d = SAMPLE_TEXTURE2D(_DecalTex, sampler_DecalTex, i.uv);
                    clip(d.a - .5);
                    albedo = lerp(albedo, i.color.rgb * d.rgb, _DecalStrength);
                }
                #endif
                float e = .6;
                float2 dg = float2(Grain(i.positionOS.xz + float2(e, 0)) - Grain(i.positionOS.xz - float2(e, 0)),
                                   Grain(i.positionOS.xz + float2(0, e)) - Grain(i.positionOS.xz - float2(0, e))) / (2.0 * e);
                half3 across = normalize(cross(normal, float3(0.04, 1, 0.03)));
                normal = normalize(normal - (across * dg.x + cross(normal, across) * dg.y) * (.45 * _Detail * fade));
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                // Wrap the diffuse a little (after Journey's sand: matte ground does not go black in its own shade),
                // and a faint forward-scatter glow when the sun is behind what you look at.
                half diffuse = saturate((dot(normal, sun.direction) + .3) / 1.3);
                half3 view = normalize(_WorldSpaceCameraPos - i.positionWS);
                half glow = pow(saturate(dot(-view, sun.direction)), 4) * .07;
                half3 light = max(half3(.03, .035, .04), SampleSH(normal)) + sun.color * ((diffuse + glow) * sun.distanceAttenuation * sun.shadowAttenuation);
                return half4(MixFog(albedo * light, i.fog), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull [_Cull]
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float3 _LightDirection;
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            float4 vert(Attributes v) : SV_POSITION
            {
                if (v.color.a > .72 && v.color.a < .78) return float4(0, 0, -1, 0);
                float3 positionWS = SwayedWS(v.positionOS, v.color.a);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, TransformObjectToWorldNormal(v.normalOS), _LightDirection));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull [_Cull]
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float3 positionOS : POSITION; half4 color : COLOR; };
            float4 vert(Attributes v) : SV_POSITION
            {
                if (v.color.a > .72 && v.color.a < .78) return float4(0, 0, -1, 0);
                return TransformWorldToHClip(SwayedWS(v.positionOS, v.color.a));
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                if (v.color.a > .72 && v.color.a < .78)
                {
                    o.positionCS = float4(0, 0, -1, 0);
                    o.normalWS = half3(0, 1, 0);
                    return o;
                }
                o.positionCS = TransformWorldToHClip(SwayedWS(v.positionOS, v.color.a));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
    Fallback Off
}
