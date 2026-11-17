Shader "Boscali/MapSurface"
{
    // Terrain, runway and scenery of a Boscali map: the colour is baked into the vertices, so a map needs no textures.
    // Vertex alpha is how much a vertex sways in the wind (0 for ground, up to 1 at a palm's crown).
    Properties
    {
        _Sway ("Wind Sway (m)", Float) = 0
        _Cull ("Cull", Float) = 2
        _Detail ("Detail Contrast", Range(0,1)) = .35
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float _Sway, _Cull, _Detail;
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
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half4 color : COLOR;
                float fog : TEXCOORD3;
            };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = SwayedWS(v.positionOS, v.color.a);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS;
                o.color = v.color;
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
            half4 frag(Varyings i, half facing : VFACE) : SV_Target
            {
                half3 normal = normalize(i.normalWS) * (facing < 0 ? -1 : 1);
                // Object-space grain at two scales: it stays put when the floating origin shifts the map.
                half grain = Noise(i.positionOS.xz * .9) * .6 + Noise(i.positionOS.xz * .11) * .4;
                half3 albedo = i.color.rgb * (1 - _Detail * .5 + _Detail * grain);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse = saturate(dot(normal, sun.direction));
                half3 light = max(half3(.03, .035, .04), SampleSH(normal)) + sun.color * (diffuse * sun.distanceAttenuation * sun.shadowAttenuation);
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
            float4 vert(Attributes v) : SV_POSITION { return TransformWorldToHClip(SwayedWS(v.positionOS, v.color.a)); }
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
