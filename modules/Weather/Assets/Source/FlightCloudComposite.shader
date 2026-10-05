Shader "Boscali/FlightCloudComposite"
{
    // Draws the reduced-resolution cloud target over the scene at the volume's render queue,
    // so water, cloud and vanilla smoke keep their order. A joint bilateral upsample: each
    // pixel blends the nine nearest cloud texels, weighted by how closely the scene depth
    // each one marched against matches this pixel's own, so terrain and aircraft edges get
    // no cloud halo and no gap.
    Properties { }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Pass
        {
            Name "CloudComposite"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _CameraDepthTexture;
            sampler2D _CloudLowResColour;
            sampler2D _CloudLowResDepth;
            float4 _CloudLowResSize;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 screen : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
                // The 3x3 cloud texels around this pixel: a Gaussian over distance (which also
                // melts the march's per-texel dither) times a depth match.
                float2 p = uv * _CloudLowResSize.xy - 0.5;
                float2 centre = floor(p + 0.5);
                float4 c = 0.0;
                float total = 0.0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 texel = centre + float2(x, y);
                        float2 tuv = (texel + 0.5) * _CloudLowResSize.zw;
                        float2 offset = texel - p;
                        float spatial = exp(-dot(offset, offset) * 1.1);
                        float texelDepth = tex2Dlod(_CloudLowResDepth, float4(tuv, 0, 0)).r;
                        // Relative depth difference: 2 % counts as the same surface at any range.
                        float w = spatial / (0.02 + abs(texelDepth - depth) / max(depth, 1.0));
                        c += tex2Dlod(_CloudLowResColour, float4(tuv, 0, 0)) * w;
                        total += w;
                    }
                }
                c /= max(1e-5, total);
                if (c.a < 0.002) discard;
                return c;
            }
            ENDHLSL
        }

        // Temporal resolve (drawn by the render pass only): the half-size sky from this frame's
        // quarter-size march. The three texels of each 2x2 block not marched this frame are
        // last frame's sky, reprojected along the cloud's distance and clamped to the range
        // of the fresh texels around them, so a cloud that moved or appeared never leaves a
        // ghost. The marched texel blends toward that carried value instead of replacing it,
        // so the per-frame march dither averages out instead of drawing a checkerboard grid
        // on thin cloud. Where last frame did not see the point, the fresh texels are
        // upsampled instead.
        Pass
        {
            Name "CloudResolve"
            Tags { "LightMode"="BoscaliCloudResolve" }
            Blend Off
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex resolveVert
            #pragma fragment resolveFrag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _CameraDepthTexture;
            sampler2D _CloudQuarterColour;
            sampler2D _CloudQuarterData;
            sampler2D _CloudHistoryTex;
            float4 _CloudLowResSize, _CloudQuarterSize;
            float4x4 _CloudFrustum, _CloudPrevMatrix;
            float4 _CloudCamDelta;
            float4 _CloudChecker;
            float _CloudHistoryValid;
            float _CloudFlashChange;

            struct rv2f { float4 pos : SV_POSITION; };
            struct ResolveOut
            {
                float4 colour : SV_Target0;
                float4 depth : SV_Target1;
            };

            rv2f resolveVert(uint id : SV_VertexID)
            {
                rv2f o;
                float2 p = float2((id << 1) & 2, id & 2);
                o.pos = float4(p * 2.0 - 1.0, 0.5, 1.0);
                return o;
            }

            ResolveOut resolveFrag(rv2f i)
            {
                float2 texel = floor(i.pos.xy);
                float2 uv = (texel + 0.5) * _CloudLowResSize.zw;
                float2 block = floor(texel * 0.5);
                float2 quv = (block + 0.5) * _CloudQuarterSize.zw;
                ResolveOut o;
                o.depth = float4(LinearEyeDepth(tex2Dlod(_CameraDepthTexture, float4(uv, 0, 0)).r), 0, 0, 0);

                bool fresh = all(texel - block * 2.0 == _CloudChecker.xy);
                float4 freshVal = tex2Dlod(_CloudQuarterColour, float4(quv, 0, 0));
                float4 upsampled = tex2Dlod(_CloudQuarterColour, float4(uv, 0, 0));
                // No history (first frame, a cut, a zoom): fresh texels are sharp, the rest
                // fall back to the upsampled march. Sharpness returns over the next frames.
                if (_CloudHistoryValid < 0.5 || _CloudChecker.w < 0.5) { o.colour = fresh ? freshVal : upsampled; return o; }

                // This texel's ray, out to the cloud distance its fresh neighbour measured,
                // seen from last frame's camera.
                float3 bottom = lerp(_CloudFrustum[0].xyz, _CloudFrustum[1].xyz, uv.x);
                float3 top = lerp(_CloudFrustum[2].xyz, _CloudFrustum[3].xyz, uv.x);
                float3 ray = normalize(lerp(bottom, top, uv.y));
                float distance = tex2Dlod(_CloudQuarterData, float4(quv, 0, 0)).g;
                float4 clip = mul(_CloudPrevMatrix, float4(ray * distance + _CloudCamDelta.xyz, 1.0));
                float2 previous = clip.xy / clip.w * 0.5 + 0.5;
                if (clip.w <= 0.0 || any(previous < 0.0) || any(previous > 1.0)) { o.colour = fresh ? freshVal : upsampled; return o; }
                float4 history = tex2Dlod(_CloudHistoryTex, float4(previous, 0, 0));

                // Neighbourhood clamp against the fresh texels around this one.
                float4 low = 1e5, high = -1e5;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float4 n = tex2Dlod(_CloudQuarterColour, float4(quv + float2(x, y) * _CloudQuarterSize.zw, 0, 0));
                        low = min(low, n);
                        high = max(high, n);
                    }
                }
                float4 carried = clamp(history, low, high);
                // A single representative depth cannot reproject all the material in a
                // nearby volume. Reduce history when translation is large relative to it.
                // Distant skies keep the same temporal savings and accumulation.
                float motion = max(saturate(length(_CloudCamDelta.xyz) / max(4.0, distance * 0.08)), _CloudCamDelta.w);
                // Fresh neighbours expose a local flash promptly without clearing sky history.
                motion = max(motion, saturate(_CloudFlashChange * 12.0));
                if (!fresh) { o.colour = lerp(carried, upsampled, motion); return o; }
                // The clamp already pulled the carried value into the fresh range, so new and
                // vanished cloud still converge within a few frames; dense texels track the
                // fresh march faster to keep their detail crisp.
                o.colour = lerp(carried, freshVal, max(0.10 + 0.40 * freshVal.a, motion));
                return o;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
