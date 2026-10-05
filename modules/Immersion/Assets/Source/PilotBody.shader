Shader "Boscali/PilotBody"
{
    Properties
    {
        _BaseMap ("Native pilot texture", 2D) = "white" {}
        _BaseColor ("Native pilot tint", Color) = (1,1,1,1)
        _BumpMap ("Native pilot normal", 2D) = "bump" {}
        _BumpScale ("Native normal strength", Range(0,2)) = 1
        _MetallicGlossMap ("Native metallic and smoothness", 2D) = "white" {}
        _Metallic ("Native metallic", Range(0,1)) = 0
        _Smoothness ("Native smoothness", Range(0,1)) = .25
        _SmoothnessTextureChannel ("Native smoothness channel", Float) = 0
        _SpecularHighlights ("Native specular highlights", Float) = 1
        _OcclusionMap ("Native pilot occlusion", 2D) = "white" {}
        _OcclusionStrength ("Native occlusion strength", Range(0,1)) = 1
        _HasNormalMap ("Borrowed normal map", Float) = 0
        _HasMetallicMap ("Borrowed metallic map", Float) = 0
        _HasOcclusionMap ("Borrowed occlusion map", Float) = 0
        _BodyVisible ("Bound cockpit camera", Float) = 0
        _HeadCenter ("Head mask world center and radius", Vector) = (0,0,0,.22)
        _HeadMaskMode ("Head mask: none, fallback sphere, oriented box", Float) = 1
        _LightLevel ("Cockpit light", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
        TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST, _BumpMap_ST, _MetallicGlossMap_ST, _OcclusionMap_ST;
        half4 _BaseColor;
        float4 _HeadCenter;
        float4x4 _CaptureVP, _HeadWorldToMask;
        half _BumpScale, _Metallic, _Smoothness, _OcclusionStrength;
        half _SmoothnessTextureChannel, _SpecularHighlights;
        half _HasNormalMap, _HasMetallicMap, _HasOcclusionMap;
        half _BodyVisible, _HeadMaskMode, _LightLevel;
        CBUFFER_END
        struct Input { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; float2 uv : TEXCOORD0; };
        struct Output
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 world : TEXCOORD1;
            half3 normal : TEXCOORD2;
            half4 tangent : TEXCOORD3;
        };
        Output PilotVertex(Input v, bool capture)
        {
            Output o;
            o.world = TransformObjectToWorld(v.vertex.xyz);
            o.pos = capture ? mul(_CaptureVP, float4(o.world,1)) : TransformWorldToHClip(o.world);
            o.normal = TransformObjectToWorldNormal(v.normal);
            o.tangent = half4(TransformObjectToWorldDir(v.tangent.xyz), v.tangent.w * GetOddNegativeScale());
            o.uv = v.uv;
            return o;
        }
        half4 PilotAlbedo(Output i)
        {
            half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv * _BaseMap_ST.xy + _BaseMap_ST.zw);
            albedo.rgb *= _BaseColor.rgb;
            return albedo;
        }
        half PilotOcclusion(Output i)
        {
            if (_HasOcclusionMap < .5h) return 1;
            half occlusion = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap,
                i.uv * _OcclusionMap_ST.xy + _OcclusionMap_ST.zw).g;
            return lerp(1, occlusion, saturate(_OcclusionStrength));
        }
        half3 PilotNormal(Output i)
        {
            half3 normal = normalize(i.normal);
            if (_HasNormalMap < .5h) return normal;
            half3 tangent = i.tangent.xyz - normal * dot(normal, i.tangent.xyz);
            half tangentLengthSq = dot(tangent, tangent);
            if (tangentLengthSq < .01h) return normal;
            tangent *= rsqrt(tangentLengthSq);
            half3 bitangent = cross(normal, tangent) * i.tangent.w;
            half3 detail = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap,
                i.uv * _BumpMap_ST.xy + _BumpMap_ST.zw), _BumpScale);
            return normalize(detail.x * tangent + detail.y * bitangent + detail.z * normal);
        }
        half4 PilotBodyColour(Output i)
        {
            half4 albedo = PilotAlbedo(i);
            half metallic = _Metallic, smoothness = _Smoothness;
            if (_HasMetallicMap > .5h)
            {
                half4 surface = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap,
                    i.uv * _MetallicGlossMap_ST.xy + _MetallicGlossMap_ST.zw);
                // URP metallic maps replace the scalar; native pilot's scalar is zero.
                metallic = surface.r;
                smoothness *= surface.a;
            }
            if (_SmoothnessTextureChannel > .5h) smoothness = _Smoothness * albedo.a;
            half3 normal = PilotNormal(i);
            half alpha = 1;
            BRDFData brdf;
            InitializeBRDFData(albedo.rgb, saturate(metallic), half3(0,0,0), saturate(smoothness), alpha, brdf);
            Light mainLight = GetMainLight();
            half3 diffuseAmbient = SampleSH(normal) * brdf.diffuse * PilotOcclusion(i);
            // ponytail: one native main light + ambient; no additional-light loops, shadows, or reflection probes.
            half3 direct = LightingPhysicallyBased(brdf, mainLight, normal,
                GetWorldSpaceNormalizeViewDir(i.world), _SpecularHighlights < .5h);
            return half4((diffuseAmbient + direct) * max(.06h, _LightLevel), 1);
        }
        half4 PilotCaptureColour(Output i)
        {
            // At 128/256 pixels the reflection needs silhouette, suit colour and AO, not micro-normal/specular detail.
            half shading = .7h + .3h * saturate(dot(normalize(i.normal), normalize(half3(.3,.7,.5))));
            return half4(PilotAlbedo(i).rgb * PilotOcclusion(i) * shading * max(.06h, _LightLevel), 1);
        }
        ENDHLSL
        Pass
        {
            Name "BODY"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            Output vert(Input v) { return PilotVertex(v, false); }
            half4 frag(Output i) : SV_Target
            {
                clip(_BodyVisible - .5);
                if (_HeadMaskMode > 1.5h)
                {
                    float3 head = abs(mul(_HeadWorldToMask, float4(i.world,1)).xyz);
                    clip(max(max(head.x, head.y), head.z) - 1);
                }
                else if (_HeadMaskMode > .5h)
                    clip(dot(i.world - _HeadCenter.xyz, i.world - _HeadCenter.xyz) - _HeadCenter.w * _HeadCenter.w);
                return PilotBodyColour(i);
            }
            ENDHLSL
        }
        Pass
        {
            Name "CAPTURE"
            Tags { "LightMode"="PilotCapture" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            Output vert(Input v) { return PilotVertex(v, true); }
            half4 frag(Output i) : SV_Target { return PilotCaptureColour(i); }
            ENDHLSL
        }
    }
}
