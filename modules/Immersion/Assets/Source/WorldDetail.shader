Shader "Boscali/WorldDetail"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Authored Detail Atlas", 2D) = "white" {}
        _Cutoff ("Leaf Cutoff", Range(0,1)) = .42
        _Cull ("Cull", Float) = 2
        _Wind ("Leaf Movement", Float) = .035
        _WindDatum ("Wind Origin Phase", Float) = 0
        _Fade ("Distance Fade", Vector) = (85,105,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull [_Cull]
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _LEAF_CUTOUT
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _Fade;
            float _Cutoff, _Wind, _WindDatum;
            CBUFFER_END
            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : COLOR;
                float2 fogDistance : TEXCOORD3;
            };
            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS);
                #if defined(_LEAF_CUTOUT)
                // Branch bases stay still; no interaction texture or additional pass.
                float phase = _Time.y * 1.3 + UNITY_MATRIX_M._m03 * .17 + UNITY_MATRIX_M._m23 * .11 - _WindDatum;
                float bend = sin(phase + v.positionOS.y * .9) * _Wind * saturate(v.positionOS.y);
                positionWS.xz += float2(bend, bend * .43);
                #endif
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.color = v.color;
                o.fogDistance = float2(ComputeFogFactor(o.positionCS.z), distance(_WorldSpaceCameraPos, positionWS));
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
                #if defined(_LEAF_CUTOUT)
                clip(albedo.a - _Cutoff);
                #endif
                // Stable screen-door fade avoids drawing overlapping LODs.
                float fade = saturate((_Fade.y - i.fogDistance.y) / max(1, _Fade.y - _Fade.x));
                float threshold = frac(dot(floor(i.positionCS.xy), float2(.754877666, .569840296)));
                clip(fade - threshold - .0001);
                half3 normal = normalize(i.normalWS);
                #if defined(_LEAF_CUTOUT)
                normal.y = abs(normal.y) * .65h + .25h;
                normal = normalize(normal);
                #endif
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse = saturate(dot(normal, sun.direction));
                half3 illumination = max(half3(.025,.03,.035), SampleSH(normal)) +
                    sun.color * (diffuse * sun.distanceAttenuation * sun.shadowAttenuation);
                half3 color = MixFog(albedo.rgb * illumination, i.fogDistance.x);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
