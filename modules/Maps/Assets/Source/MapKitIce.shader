Shader "Boscali/MapKitIce"
{
    // Polar icebergs and sea-ice floes for the scenery kit: object-space triplanar glacier ice and snow
    // matching MapIce.shader's subsurface terminator scatter, crisp specular glint, and polar palette.
    Properties
    {
        _Deep ("Colour of light inside the ice", Color) = (.10, .45, .62, 1)
        _DepthColor ("Colour of the depths", Color) = (.02, .16, .30, 1)
        _Scatter ("Scattering", Range(0,2)) = 0.9
        _IceColor ("Ice colour", 2D) = "white" {}
        _IceNormal ("Ice normal", 2D) = "bump" {}
        _Snow ("Snow", 2D) = "white" {}
        _SnowNormal ("Snow normal", 2D) = "bump" {}
        _TileScale ("Ice repeats per metre", Float) = 0.055
        _SnowScale ("Snow repeats per metre", Float) = 0.08
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _Deep, _DepthColor;
        half _Scatter;
        float _TileScale, _SnowScale;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            TEXTURE2D(_IceColor); SAMPLER(sampler_IceColor);
            TEXTURE2D(_IceNormal); SAMPLER(sampler_IceNormal);
            TEXTURE2D(_Snow); SAMPLER(sampler_Snow);
            TEXTURE2D(_SnowNormal); SAMPLER(sampler_SnowNormal);
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half3 normalOS : TEXCOORD3;
                half4 color : COLOR;
                float fog : TEXCOORD4;
            };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS;
                o.normalOS = v.normalOS;
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half3 nWS = normalize(i.normalWS);
                half3 nOS = normalize(i.normalOS);
                half3 w = abs(nOS);
                w = w * w;
                w /= max(w.x + w.y + w.z, 0.001);

                float3 p = i.positionOS * _TileScale;
                half3 iceX = SAMPLE_TEXTURE2D(_IceColor, sampler_IceColor, p.zy).rgb;
                half3 iceY = SAMPLE_TEXTURE2D(_IceColor, sampler_IceColor, p.xz).rgb;
                half3 iceZ = SAMPLE_TEXTURE2D(_IceColor, sampler_IceColor, p.xy).rgb;
                half3 iceTex = iceX * w.x + iceY * w.y + iceZ * w.z;

                half3 bnX = UnpackNormal(SAMPLE_TEXTURE2D(_IceNormal, sampler_IceNormal, p.zy));
                half3 bnY = UnpackNormal(SAMPLE_TEXTURE2D(_IceNormal, sampler_IceNormal, p.xz));
                half3 bnZ = UnpackNormal(SAMPLE_TEXTURE2D(_IceNormal, sampler_IceNormal, p.xy));
                half3 iceBumpOS = half3(0, bnX.y, bnX.x) * w.x + half3(bnY.x, 0, bnY.y) * w.y + half3(bnZ.x, bnZ.y, 0) * w.z;

                float2 sp = i.positionOS.xz * _SnowScale;
                half3 snowTex = .88 + .45 * SAMPLE_TEXTURE2D(_Snow, sampler_Snow, sp).rgb;
                half3 snowBN = UnpackNormal(SAMPLE_TEXTURE2D(_SnowNormal, sampler_SnowNormal, sp));
                half3 snowBumpOS = half3(snowBN.x, 0, snowBN.y);

                // Snow collects above the wave-wash zone on upward slopes and baked snow-cap vertices.
                half aboveWave = smoothstep(2.6, 5.2, i.positionOS.y);
                half snowNoise = (iceTex.r - 0.45) * 0.32;
                half snowMask = saturate(i.color.a * 0.82 + smoothstep(0.32, 0.82, nWS.y + snowNoise) * 0.65 * aboveWave);
                half3 iceCol = lerp(_DepthColor.rgb, iceTex * 1.95 * lerp(i.color.rgb, half3(.64, .82, .94), .42), .76);
                half3 snowCol = snowTex * lerp(i.color.rgb, half3(.93, .95, .98), .58);
                half3 base = lerp(iceCol, snowCol, snowMask);

                float away = saturate((distance(i.positionWS, GetCameraPositionWS()) - 600.0) / 3000.0);
                half3 bumpWS = TransformObjectToWorldDir(lerp(iceBumpOS, snowBumpOS, snowMask)) * (1.0 - 0.75 * away);
                half3 normal = normalize(nWS + bumpWS * 0.45);

                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = sun.shadowAttenuation * sun.distanceAttenuation;
                half wrap = saturate((dot(normal, sun.direction) + .42) / 1.42) * shadow;
                half3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 lit = base * sun.color * wrap;
                half3 shade = base * _Deep.rgb * (1 - wrap) * (.68 * _Scatter);
                half3 glow = _Deep.rgb * sun.color * (pow(saturate(dot(view, -normalize(sun.direction + normal * .35))), 3) * .55 * _Scatter) * (1 - .6 * snowMask);
                half3 sky = max(half3(.04, .05, .06), SampleSH(normal)) * base * half3(.88, .96, 1.08);
                half rim = pow(1 - saturate(dot(normal, view)), 4);
                half gloss = lerp(96, 28, snowMask);
                half glint = pow(saturate(dot(normal, normalize(sun.direction + view))), gloss) * lerp(0.55, .14, snowMask) * shadow;
                half3 colour = lit + shade + glow + sky * .65 + rim * half3(.5, .66, .82) * lerp(.35, .16, snowMask) + sun.color * glint * .5;
                return half4(MixFog(colour, i.fog), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Back
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float3 _LightDirection;
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(Attributes v) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS);
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
            Cull Back
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float3 positionOS : POSITION; };
            float4 vert(Attributes v) : SV_POSITION { return TransformWorldToHClip(TransformObjectToWorld(v.positionOS)); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(v.positionOS));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
    Fallback Off
}
