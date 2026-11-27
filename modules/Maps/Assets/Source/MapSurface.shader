Shader "Boscali/MapSurface"
{
    // Terrain, runway and scenery of a Boscali map: the colour is baked into the vertices, so a map needs no textures.
    // On KitSway/terrain, vertex alpha is wind sway (0 for ground, up to 1 at a palm's crown).
    // On KitModel (_TEXTURED), vertex alpha selects PBR surface & normal map classes:
    //   a < 0.12        : painted/sheet metal (_TexB / _TexBN)
    //   0.12 <= a < 0.30: tinted reflective glazing (no albedo tex, glossy Fresnel + sun glint)
    //   0.30 <= a < 0.48: rock / stone / masonry (_TexD / _TexDN)
    //   0.48 <= a < 0.65: gabion / timber / canvas (_TexC / _TexCN)
    //   0.72 <= a <=0.78: cutout decal atlas (_DecalTex)
    //   a > 0.85        : weathered concrete / plaster (_TexA / _TexAN)
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
        _TexC ("Gabion (alpha .55)", 2D) = "white" {}
        _TexD ("Rock (alpha .4)", 2D) = "white" {}
        _TexAN ("Concrete Normal", 2D) = "bump" {}
        _TexBN ("Metal Normal", 2D) = "bump" {}
        _TexCN ("Gabion Normal", 2D) = "bump" {}
        _TexDN ("Rock Normal", 2D) = "bump" {}
        _TexTile ("Repeats per metre", Vector) = (.0625, .05, .16, .08)
        _TexGain ("Brightness to middle grey", Vector) = (1, 1, 1, 1)
        _NormalStrength ("Surface Normal Strength", Range(0,2)) = 0.75
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
        float _DetailSurfaces, _DetailScale, _DetailStrength, _DecalStrength, _NormalStrength;
        float4 _TexTile, _TexGain;
        CBUFFER_END
        float3 SwayedWS(float3 positionOS, half weight)
        {
            float3 positionWS = TransformObjectToWorld(positionOS);
            if (_Sway > 0.0001)
            {
                float phase = _Time.y * 1.1 + positionOS.x * .05 + positionOS.z * .07;
                positionWS.xz += float2(sin(phase), cos(phase * .8)) * (_Sway * weight);
            }
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
            TEXTURE2D(_TexA); TEXTURE2D(_TexB); TEXTURE2D(_TexC); TEXTURE2D(_TexD);
            TEXTURE2D(_TexAN); TEXTURE2D(_TexBN); TEXTURE2D(_TexCN); TEXTURE2D(_TexDN);
            SAMPLER(sampler_TexA);
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
                #ifdef _TEXTURED
                o.positionWS = TransformObjectToWorld(v.positionOS); // alpha picks a surface here, it is not sway
                #else
                o.positionWS = SwayedWS(v.positionOS, v.color.a);
                #endif
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS;
                o.color = v.color;
                o.uv = v.uv;
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
            half3 PerturbNormal(half3 normal, half3 t, half3 bt, float invmax, half3 tangentNormal, half strength)
            {
                half3 tn = half3(tangentNormal.xy * strength, tangentNormal.z);
                return normalize(t * tn.x * invmax + bt * tn.y * invmax + normal * tn.z);
            }
            half4 frag(Varyings i, half facing : VFACE) : SV_Target
            {
                half3 normal = normalize(i.normalWS) * (facing < 0 ? -1 : 1);
                half grain = Grain(i.positionOS.xz);
                half3 albedo = i.color.rgb * (1 - _Detail * .5 + _Detail * grain);
                float dist = distance(_WorldSpaceCameraPos, i.positionWS);
                float fade = saturate(300.0 / max(dist, 1.0));
                float nearFade = saturate(450.0 / max(dist, 1.0));
                half decal = step(.72, i.color.a) * step(i.color.a, .78);

                half smoothness = 0.08;
                half metallic = 0.0;

                #if defined(_DETAIL) || defined(_TEXTURED)
                half3 dp1 = ddx(i.positionWS), dp2 = ddy(i.positionWS);
                float2 duv1 = ddx(i.uv), duv2 = ddy(i.uv);
                half3 dp2perp = cross(dp2, normal), dp1perp = cross(normal, dp1);
                half3 t = dp2perp * duv1.x + dp1perp * duv2.x;
                half3 bt = dp2perp * duv1.y + dp1perp * duv2.y;
                float invmax = rsqrt(max(max(dot(t, t), dot(bt, bt)), 1e-8));
                #endif

                #ifdef _DETAIL
                half3 detail = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, i.uv * _DetailScale).rgb;
                albedo *= lerp(1, detail * 1.08, _DetailStrength * fade * (1 - decal));
                if (decal < .5)
                {
                    half3 dn = UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, i.uv * _DetailScale));
                    normal = PerturbNormal(normal, t, bt, invmax, dn, _DetailStrength * 0.7 * fade);
                }
                #endif

                #ifdef _TEXTURED
                half a = i.color.a;
                if (a > .85)
                {
                    // Concrete / plaster
                    albedo *= lerp(half3(1, 1, 1), SAMPLE_TEXTURE2D(_TexA, sampler_TexA, i.uv * _TexTile.x).rgb * _TexGain.x, 0.85);
                    half3 sn = UnpackNormal(SAMPLE_TEXTURE2D(_TexAN, sampler_TexA, i.uv * _TexTile.x));
                    normal = PerturbNormal(normal, t, bt, invmax, sn, _NormalStrength * nearFade);
                    smoothness = 0.14;
                }
                else if (a < .12)
                {
                    // Painted / sheet metal
                    albedo *= lerp(half3(1, 1, 1), SAMPLE_TEXTURE2D(_TexB, sampler_TexA, i.uv * _TexTile.y).rgb * _TexGain.y, 0.85);
                    half3 sn = UnpackNormal(SAMPLE_TEXTURE2D(_TexBN, sampler_TexA, i.uv * _TexTile.y));
                    normal = PerturbNormal(normal, t, bt, invmax, sn, _NormalStrength * nearFade);
                    smoothness = 0.38;
                    metallic = 0.28;
                }
                else if (a < .30)
                {
                    // Reflective dark glazing
                    albedo = i.color.rgb;
                    smoothness = 0.86;
                    metallic = 0.10;
                }
                else if (a < .48)
                {
                    // Rock / stone / masonry
                    albedo *= lerp(half3(1, 1, 1), SAMPLE_TEXTURE2D(_TexD, sampler_TexA, i.uv * _TexTile.w).rgb * _TexGain.w, 0.90);
                    half3 sn = UnpackNormal(SAMPLE_TEXTURE2D(_TexDN, sampler_TexA, i.uv * _TexTile.w));
                    normal = PerturbNormal(normal, t, bt, invmax, sn, _NormalStrength * 1.15 * nearFade);
                    smoothness = 0.10;
                }
                else if (a < .65)
                {
                    // Gabion / timber / sandbag
                    albedo *= lerp(half3(1, 1, 1), SAMPLE_TEXTURE2D(_TexC, sampler_TexA, i.uv * _TexTile.z).rgb * _TexGain.z, 0.88);
                    half3 sn = UnpackNormal(SAMPLE_TEXTURE2D(_TexCN, sampler_TexA, i.uv * _TexTile.z));
                    normal = PerturbNormal(normal, t, bt, invmax, sn, _NormalStrength * nearFade);
                    smoothness = 0.08;
                }
                #endif

                #ifdef _DETAIL
                if (decal > .5)
                {
                    half4 d = SAMPLE_TEXTURE2D(_DecalTex, sampler_DecalTex, i.uv);
                    clip(d.a - .5);
                    albedo = lerp(albedo, i.color.rgb * d.rgb, _DecalStrength);
                    smoothness = 0.12;
                    metallic = 0.0;
                }
                #endif

                float e = .6;
                float2 dg = float2(Grain(i.positionOS.xz + float2(e, 0)) - Grain(i.positionOS.xz - float2(e, 0)),
                                   Grain(i.positionOS.xz + float2(0, e)) - Grain(i.positionOS.xz - float2(0, e))) / (2.0 * e);
                half3 across = normalize(cross(normal, float3(0.04, 1, 0.03)));
                normal = normalize(normal - (across * dg.x + cross(normal, across) * dg.y) * (.45 * _Detail * fade));

                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse = saturate((dot(normal, sun.direction) + .3) / 1.3);
                half3 view = normalize(_WorldSpaceCameraPos - i.positionWS);
                half glow = pow(saturate(dot(-view, sun.direction)), 4) * .07;
                half sunVis = sun.distanceAttenuation * sun.shadowAttenuation;
                half3 ambient = max(half3(.03, .035, .04), SampleSH(normal));
                half3 light = ambient + sun.color * ((diffuse + glow) * sunVis);

                half3 spec = 0;
                #ifdef _TEXTURED
                half3 halfVec = normalize(sun.direction + view);
                half ndh = saturate(dot(normal, halfVec));
                half specPower = lerp(12.0, 140.0, smoothness * smoothness);
                half specTerm = pow(ndh, specPower) * lerp(0.03, 0.65, smoothness);
                half3 specTint = lerp(half3(1, 1, 1), albedo, metallic);
                spec = sun.color * (specTerm * sunVis * specTint);

                // Subtle sky reflection on glass & painted metal
                half fresnel = pow(1.0 - saturate(dot(normal, view)), 4.0);
                half3 skyRefl = max(half3(.04, .05, .07), SampleSH(reflect(-view, normal)));
                spec += skyRefl * (fresnel * lerp(0.02, 0.55, smoothness * smoothness));
                #endif

                return half4(MixFog(albedo * light + spec, i.fog), 1);
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
            #pragma shader_feature_local _TEXTURED
            float3 _LightDirection;
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            float4 vert(Attributes v) : SV_POSITION
            {
                if (v.color.a > .72 && v.color.a < .78) return float4(0, 0, -1, 0);
                #ifdef _TEXTURED
                float3 positionWS = TransformObjectToWorld(v.positionOS);
                #else
                float3 positionWS = SwayedWS(v.positionOS, v.color.a);
                #endif
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
            #pragma shader_feature_local _TEXTURED
            struct Attributes { float3 positionOS : POSITION; half4 color : COLOR; };
            float4 vert(Attributes v) : SV_POSITION
            {
                if (v.color.a > .72 && v.color.a < .78) return float4(0, 0, -1, 0);
                #ifdef _TEXTURED
                return TransformWorldToHClip(TransformObjectToWorld(v.positionOS));
                #else
                return TransformWorldToHClip(SwayedWS(v.positionOS, v.color.a));
                #endif
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
            #pragma shader_feature_local _TEXTURED
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
                #ifdef _TEXTURED
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(v.positionOS));
                #else
                o.positionCS = TransformWorldToHClip(SwayedWS(v.positionOS, v.color.a));
                #endif
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
    Fallback Off
}
