// One packed, mipmapped texture sample over nearby native terrain. R fine colour,
// GB height slopes centred on 0.5, A broad colour. Multiplication preserves the
// native material's shadows and fog; this pass never adds light or writes depth.
Shader "Boscali/TerrainDetail"
{
    Properties
    {
        [NoScaleOffset] _TerrainDetail ("Packed Ground Detail", 2D) = "gray" {}
        _DetailStrength ("Detail Strength", Range(0,1)) = 0
        _DetailFade ("Distance / Footprint Fade Metres", Vector) = (550,1000,0.25,1.5)
        _DetailOrigin ("Wrapped Global Tile Origin", Vector) = (0,0,0,0)
        _SunDir ("Sun Direction WS", Vector) = (0,1,0,0)
        _SunColor ("Sun Color", Color) = (0,0,0,0)
        _FogDensity ("Fog Density", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Geometry+9" "RenderType"="Opaque" }
        Pass
        {
            Blend DstColor Zero
            ColorMask RGB
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _TerrainDetail;
            float _DetailStrength, _FogDensity;
            float4 _DetailFade, _DetailOrigin, _SunDir, _SunColor;

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(appdata_base v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                // Strip floating-origin translation; the CPU supplies the renderer's
                // global origin modulo 64 m. Adjacent objects share one stationary tile.
                float3 relative = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
                o.uv = relative.xz / 64.0 + _DetailOrigin.xy;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 dx = ddx(i.uv), dy = ddy(i.uv);
                float footprint = 64.0 * max(length(dx), length(dy));
                float dist = length(_WorldSpaceCameraPos - i.world);
                float3 N = normalize(i.normal + float3(0, 0.0001, 0));
                // Planar detail fades off near-vertical walls instead of stretching.
                float fade = saturate(_DetailStrength) * smoothstep(0.15, 0.65, abs(N.y));
                fade *= 1 - smoothstep(_DetailFade.x, _DetailFade.y, dist);
                fade *= 1 - smoothstep(_DetailFade.z, _DetailFade.w, footprint);
                fade *= exp(-dist * max(0, _FogDensity));
                [branch] if (fade <= 0.0001) return half4(1, 1, 1, 1);

                // Explicit gradients keep mip selection defined across the fade branch.
                float4 detail = tex2Dgrad(_TerrainDetail, i.uv, dx, dy);
                float tone = (detail.r - 0.5) * 0.12 + (detail.a - 0.5) * 0.16;
                float3 bump = float3(0.5 - detail.g, 0, 0.5 - detail.b) * 0.4;
                bump -= N * dot(N, bump);
                float3 raised = normalize(N + bump);
                float3 L = normalize(_SunDir.xyz + float3(0, 0.0001, 0));
                float direct = saturate(dot(_SunColor.rgb, float3(0.2126, 0.7152, 0.0722)));
                float relief = clamp(dot(raised - N, L), -0.2, 0.2) * direct * 0.35;
                float keep = 1 + (tone + relief) * fade;
                return half4(keep, keep, keep, 1);
            }
            ENDHLSL
        }
    }
}
