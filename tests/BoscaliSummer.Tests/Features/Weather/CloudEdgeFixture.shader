Shader "Hidden/Boscali/CloudEdgeFixture"
{
    SubShader
    {
        // CloudLowRes records production march pass 2. The first two slots stay unused.
        Pass { }
        Pass { }
        Pass
        {
            Blend Off ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _CameraDepthTexture;
            float4 _CloudLowResSize, _CloudChecker;
            float4x4 _CloudFrustum;
            float _CloudCheckerOn;
            float _CloudEdgeGap;
            float _CloudEdgeDistance;
            struct Varying { float4 position : SV_POSITION; };
            struct Output { float4 colour : SV_Target0; float4 depth : SV_Target1; };
            Varying vert(uint id : SV_VertexID)
            {
                Varying o;
                float2 p = float2((id << 1) & 2, id & 2);
                o.position = float4(p * 2.0 - 1.0, 0.5, 1.0);
                return o;
            }
            Output frag(Varying i)
            {
                float2 pixel = _CloudCheckerOn > 0.5 ? floor(i.position.xy) * 2.0 + _CloudChecker.xy + 0.5 : i.position.xy;
                float eye = LinearEyeDepth(tex2Dlod(_CameraDepthTexture, float4(pixel * _CloudLowResSize.zw, 0, 0)).r);
                Output o;
                bool gap = _CloudEdgeGap > 0.5 && pixel.x >= 34.0 && pixel.x < 36.0;
                o.colour = eye > 1000.0 && !gap ? float4(0.8, 0.8, 0.8, 0.9) : 0.0;
                float distance = _CloudEdgeDistance > 0.0 ? _CloudEdgeDistance : 10000.0;
                float2 uv = pixel * _CloudLowResSize.zw;
                float3 ray = lerp(lerp(_CloudFrustum[0].xyz, _CloudFrustum[1].xyz, uv.x),
                    lerp(_CloudFrustum[2].xyz, _CloudFrustum[3].xyz, uv.x), uv.y);
                float upsampleDepth = _CloudCheckerOn < 0.5 && o.colour.a > 0.002 ? min(eye, distance / length(ray)) : eye;
                o.depth = float4(upsampleDepth, distance, 0, 0);
                return o;
            }
            ENDHLSL
        }
    }
}
