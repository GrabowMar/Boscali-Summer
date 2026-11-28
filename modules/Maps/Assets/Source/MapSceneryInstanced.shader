Shader "Boscali/MapSceneryInstanced"
{
    Properties
    {
        _Sway ("Wind Sway", Float) = 0
        _Cull ("Cull", Float) = 2
        _Textured ("Surface classes", Float) = 0
        _DetailSurfaces ("Decals", Float) = 0
        _DecalTex ("Decal atlas", 2D) = "white" {}
        _DecalStrength ("Decal strength", Range(0,1)) = 1
        _TexA ("Concrete", 2D) = "white" {}
        _TexB ("Sheet metal", 2D) = "white" {}
        _TexC ("Gabion", 2D) = "white" {}
        _TexD ("Rock", 2D) = "white" {}
        _TexAN ("Concrete normal", 2D) = "bump" {}
        _TexBN ("Metal normal", 2D) = "bump" {}
        _TexCN ("Gabion normal", 2D) = "bump" {}
        _TexDN ("Rock normal", 2D) = "bump" {}
        _TexTile ("Repeats per metre", Vector) = (.0625, .05, .16, .08)
        _TexGain ("Brightness to middle grey", Vector) = (1, 1, 1, 1)
        _NormalStrength ("Surface normal strength", Float) = .75
        _SceneryIce ("Ice surface", Float) = 0
        _IceColor ("Ice colour", 2D) = "white" {}
        _Snow ("Snow colour", 2D) = "white" {}
        _TileScale ("Ice repeats per metre", Float) = .055
        _SnowScale ("Snow repeats per metre", Float) = .08
        _FoliageMap ("Foliage atlas", 2D) = "white" {}
        _FoliageCutout ("Foliage cutout", Float) = 0
        _FoliageCutoff ("Foliage threshold", Range(0,1)) = .42
        _NativeTreeSurface ("Use stock tree atlas", Float) = 0
        _TreeSurface ("Stock tree atlas", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        StructuredBuffer<float3> _SceneryPositions;
        StructuredBuffer<float4> _SceneryPoses;
        StructuredBuffer<uint> _SceneryVisible;
        float3 _SceneryWorldOffset;
        float3 _LightDirection;
        CBUFFER_START(UnityPerMaterial)
        float _Sway, _Textured, _DetailSurfaces, _DecalStrength, _NormalStrength;
        float _SceneryIce, _TileScale, _SnowScale, _FoliageCutout, _FoliageCutoff;
        float _NativeTreeSurface;
        float4 _TexTile, _TexGain;
        CBUFFER_END

        TEXTURE2D(_DecalTex); SAMPLER(sampler_DecalTex);
        TEXTURE2D(_FoliageMap); SAMPLER(sampler_FoliageMap);
        TEXTURE2D(_TreeSurface); SAMPLER(sampler_TreeSurface);
        TEXTURE2D(_TexA); TEXTURE2D(_TexB); TEXTURE2D(_TexC); TEXTURE2D(_TexD);
        TEXTURE2D(_TexAN); TEXTURE2D(_TexBN); TEXTURE2D(_TexCN); TEXTURE2D(_TexDN);
        SAMPLER(sampler_TexA);
        TEXTURE2D(_IceColor); SAMPLER(sampler_IceColor);
        TEXTURE2D(_Snow); SAMPLER(sampler_Snow);

        struct Attributes
        {
            float3 position : POSITION;
            float3 normal : NORMAL;
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
            uint instance : SV_InstanceID;
        };

        struct Geometry
        {
            float4 positionCS : SV_POSITION;
            half3 normalWS : TEXCOORD0;
            float2 uv : TEXCOORD1;
            half4 color : COLOR;
        };

        float3 RotateYaw(float3 direction, float2 yaw)
        {
            return float3(direction.x * yaw.x + direction.z * yaw.y, direction.y,
                          direction.z * yaw.x - direction.x * yaw.y);
        }

        float3 InstancePosition(Attributes v, out half3 normalWS)
        {
            // Each indirect draw starts at instance zero; GPU culling preserves indices into the baked records.
            uint index = _SceneryVisible[v.instance];
            float3 origin = _SceneryPositions[index];
            float4 pose = _SceneryPoses[index];
            float3 relative = RotateYaw(v.position, pose.yz) * pose.x;
            if (_Sway > .001)
            {
                float phase = _Time.y * 1.1 + (origin.x + relative.x) * .05 + (origin.z + relative.z) * .07;
                relative.xz += float2(sin(phase), cos(phase * .8)) * (_Sway * v.color.a);
            }
            normalWS = RotateYaw(v.normal, pose.yz);
            return _SceneryWorldOffset + origin + relative;
        }

        bool IsDecal(half alpha) { return _DetailSurfaces > .5 && alpha >= .72 && alpha <= .78; }
        bool IsFoliage(float2 uv) { return _FoliageCutout > .5 && uv.x >= 0; } // The trunk's UV.x is negative.

        void AppearanceClip(float2 uv, half vertexAlpha)
        {
            if (IsFoliage(uv)) clip(SAMPLE_TEXTURE2D(_FoliageMap, sampler_FoliageMap, uv).a - _FoliageCutoff);
            if (IsDecal(vertexAlpha)) clip(SAMPLE_TEXTURE2D(_DecalTex, sampler_DecalTex, uv).a - .5);
        }

        Geometry DepthVertex(Attributes v)
        {
            Geometry o;
            o.positionCS = TransformWorldToHClip(InstancePosition(v, o.normalWS));
            o.uv = v.uv;
            o.color = v.color;
            return o;
        }

        Geometry ShadowVertex(Attributes v)
        {
            Geometry o;
            float3 world = InstancePosition(v, o.normalWS);
            o.positionCS = TransformWorldToHClip(ApplyShadowBias(world, o.normalWS, _LightDirection));
            #if UNITY_REVERSED_Z
            o.positionCS.z = min(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
            #else
            o.positionCS.z = max(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
            #endif
            o.uv = v.uv;
            o.color = v.color;
            return o;
        }

        half4 DepthFragment(Geometry i) : SV_Target
        {
            AppearanceClip(i.uv, i.color.a);
            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            AlphaToMask [_FoliageCutout]
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                half fog : TEXCOORD4;
                half4 color : COLOR;
            };

            Varyings Vertex(Attributes v)
            {
                Varyings o;
                o.positionWS = InstancePosition(v, o.normalWS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.positionOS = v.position;
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half3 Surface(Varyings i, inout half3 normal)
            {
                float2 uv = i.uv;
                // Only nearby props sample the native normal atlas. Distant detail comes from colours and mipmapped albedo.
                half strength = saturate((550 - distance(i.positionWS, GetCameraPositionWS())) / 300) * _NormalStrength;
                half3 albedo = 1;
                half3 bump = half3(0, 0, 1);
                if (i.color.a > .85)
                {
                    uv *= _TexTile.x;
                    albedo = SAMPLE_TEXTURE2D(_TexA, sampler_TexA, uv).rgb * _TexGain.x;
                    if (strength > 0) bump = UnpackNormal(SAMPLE_TEXTURE2D(_TexAN, sampler_TexA, uv));
                }
                else if (i.color.a < .12)
                {
                    uv *= _TexTile.y;
                    albedo = SAMPLE_TEXTURE2D(_TexB, sampler_TexA, uv).rgb * _TexGain.y;
                    if (strength > 0) bump = UnpackNormal(SAMPLE_TEXTURE2D(_TexBN, sampler_TexA, uv));
                }
                else if (i.color.a >= .30 && i.color.a < .48)
                {
                    uv *= _TexTile.w;
                    albedo = SAMPLE_TEXTURE2D(_TexD, sampler_TexA, uv).rgb * _TexGain.w;
                    if (strength > 0) bump = UnpackNormal(SAMPLE_TEXTURE2D(_TexDN, sampler_TexA, uv));
                }
                else if (i.color.a >= .48 && i.color.a < .65)
                {
                    uv *= _TexTile.z;
                    albedo = SAMPLE_TEXTURE2D(_TexC, sampler_TexA, uv).rgb * _TexGain.z;
                    if (strength > 0) bump = UnpackNormal(SAMPLE_TEXTURE2D(_TexCN, sampler_TexA, uv));
                }

                float3 dpdx = ddx(i.positionWS), dpdy = ddy(i.positionWS);
                float2 duvdx = ddx(i.uv), duvdy = ddy(i.uv);
                if (strength > 0)
                {
                    float3 t1 = cross(dpdy, normal), t2 = cross(normal, dpdx);
                    float3 t = t1 * duvdx.x + t2 * duvdy.x, b = t1 * duvdx.y + t2 * duvdy.y;
                    float tangentScale = rsqrt(max(max(dot(t, t), dot(b, b)), 1e-8));
                    normal = normalize(normal * bump.z + (t * bump.x + b * bump.y) * (strength * tangentScale));
                }
                return i.color.rgb * lerp(half3(1, 1, 1), albedo, .88);
            }

            half3 Ice(Varyings i, half3 normal)
            {
                float3 p = i.positionOS * _TileScale;
                half3 axis = abs(normal);
                float2 sampleUV = axis.y > .60 ? p.xz : (axis.x > axis.z ? p.zy : p.xy);
                half3 ice = SAMPLE_TEXTURE2D(_IceColor, sampler_IceColor, sampleUV).rgb;
                half snow = saturate(i.color.a * .82 + smoothstep(.32, .82, normal.y) * smoothstep(2.6, 5.2, i.positionOS.y) * .65);
                half3 iceTint = lerp(half3(.02, .16, .30), ice * 1.95 * lerp(i.color.rgb, half3(.64, .82, .94), .42), .76);
                half3 snowTint = (.88 + .45 * SAMPLE_TEXTURE2D(_Snow, sampler_Snow, i.positionOS.xz * _SnowScale).rgb) * lerp(i.color.rgb, half3(.93, .95, .98), .58);
                return lerp(iceTint, snowTint, snow);
            }

            half4 Fragment(Varyings i, FRONT_FACE_TYPE side : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half3 normal = normalize(i.normalWS) * IS_FRONT_VFACE(side, 1, -1);
                half3 albedo = i.color.rgb;
                half alpha = 1;
                if (_SceneryIce > .5) albedo = Ice(i, normal);
                else if (IsDecal(i.color.a))
                {
                    half4 decal = SAMPLE_TEXTURE2D(_DecalTex, sampler_DecalTex, i.uv);
                    clip(decal.a - .5);
                    albedo *= lerp(half3(1, 1, 1), decal.rgb, _DecalStrength);
                }
                else if (_Textured > .5) albedo = Surface(i, normal);
                if (IsFoliage(i.uv))
                {
                    half4 leaf = SAMPLE_TEXTURE2D(_FoliageMap, sampler_FoliageMap, i.uv);
                    clip(leaf.a - _FoliageCutoff);
                    albedo *= lerp(half3(1, 1, 1), leaf.rgb, .35);
                    alpha = leaf.a;
                }
                if (_NativeTreeSurface > .5)
                {
                    bool wood = i.uv.x < 0;
                    // Use interior texels of the game's canopy and bark, with species coverage from our own mask.
                    float2 atlasUV = wood
                        ? float2(.543, .073) + saturate(float2(-i.uv.x - 2, i.uv.y - 17)) * float2(.009, .087)
                        : float2(.33, .46) + i.uv * float2(.33, .31);
                    half4 surface = SAMPLE_TEXTURE2D(_TreeSurface, sampler_TreeSurface, atlasUV);
                    albedo = lerp(albedo, surface.rgb, (wood ? .78 : .90) * saturate(surface.a * 1.3));
                }

                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(i.positionWS));
                #else
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                #endif
                Light sun = GetMainLight(shadowCoord);
                half diffuse = saturate((dot(normal, sun.direction) + .25) * .8);
                half3 lit = max(half3(.03, .035, .04), SampleSH(normal));
                lit += sun.color * (diffuse * sun.distanceAttenuation * sun.shadowAttenuation);
                return half4(MixFog(albedo * lit, i.fog), alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment DepthFragment
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex DepthVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 DepthNormalsFragment(Geometry i, FRONT_FACE_TYPE side : FRONT_FACE_SEMANTIC) : SV_Target
            {
                AppearanceClip(i.uv, i.color.a);
                half3 normal = normalize(i.normalWS) * IS_FRONT_VFACE(side, 1, -1);
                #ifdef _GBUFFER_NORMALS_OCT
                float2 oct = PackNormalOctQuadEncode(normal);
                return half4(PackFloat2To888(saturate(oct * .5 + .5)), 0);
                #else
                return half4(normal, 0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
