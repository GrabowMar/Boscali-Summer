Shader "Boscali/FlightCloudComposite"
{
    // Draws the reduced-resolution cloud target over the scene at the volume's render queue,
    // so water, cloud and vanilla smoke keep their order. A joint bilateral upsample: each
    // pixel blends the nine nearest cloud texels against the nearest relevant depth, so
    // opaque foreground stays clear and terrain behind visible cloud cannot cut holes in it.
    Properties
    {
        [HideInInspector] _CloudLowResColour ("Cloud colour", 2D) = "black" { }
        [HideInInspector] _CloudLowResDepth ("Cloud depth", 2D) = "black" { }
        [HideInInspector] _CloudQuarterColour ("Fresh colour", 2D) = "black" { }
        [HideInInspector] _CloudQuarterData ("Fresh depth", 2D) = "black" { }
        [HideInInspector] _CloudHistoryTex ("History colour", 2D) = "black" { }
        [HideInInspector] _CloudHistoryDepth ("History depth", 2D) = "black" { }
    }
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
                        float4 sample = tex2Dlod(_CloudLowResColour, float4(tuv, 0, 0));
                        // Visible clouds only need both opaque surfaces behind the cloud.
                        // Empty foreground samples retain their scene depth and must still
                        // be rejected completely, or they dilute the sky into a pale outline.
                        float pixelDepth = sample.a > 0.002 ? min(depth, texelDepth) : depth;
                        float difference = abs(texelDepth - pixelDepth) / max(1.0, min(texelDepth, pixelDepth));
                        float w = difference < 0.1 ? spatial / (0.02 + difference) : 0.0;
                        c += sample * w;
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
            sampler2D _CloudHistoryDepth;
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

            float3 ViewRay(float2 uv)
            {
                return lerp(lerp(_CloudFrustum[0].xyz, _CloudFrustum[1].xyz, uv.x),
                    lerp(_CloudFrustum[2].xyz, _CloudFrustum[3].xyz, uv.x), uv.y);
            }

            ResolveOut Resolved(float4 colour, float sceneDepth, float cloudEye)
            {
                ResolveOut o;
                o.colour = colour;
                // Same RFloat budget: the nearest relevant depth, raw scene depth for
                // empty rays and visible cloud depth for rays with cloud in front.
                o.depth = float4(colour.a > 0.002 ? min(sceneDepth, cloudEye) : sceneDepth, 0, 0, 0);
                return o;
            }

            ResolveOut resolveFrag(rv2f i)
            {
                float2 texel = floor(i.pos.xy);
                float2 uv = (texel + 0.5) * _CloudLowResSize.zw;
                float2 block = floor(texel * 0.5);
                float2 quv = (block + 0.5) * _CloudQuarterSize.zw;
                float sceneDepth = LinearEyeDepth(tex2Dlod(_CameraDepthTexture, float4(uv, 0, 0)).r);
                float3 view = ViewRay(uv);
                float viewLength = length(view);

                bool fresh = all(texel - block * 2.0 == _CloudChecker.xy);
                float4 freshVal = tex2Dlod(_CloudQuarterColour, float4(quv, 0, 0));
                float2 freshData = tex2Dlod(_CloudQuarterData, float4(quv, 0, 0)).rg;
                // A checker sample may hit the aircraft while this half-resolution pixel
                // sees sky. Reconstruct only from fresh samples on the same side of depth.
                float4 upsampled = 0.0, low = 1e5, high = -1e5;
                float total = 0.0, distanceSum = 0.0, distanceWeight = 0.0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 sampleBlock = block + float2(x, y);
                        float2 sampleUv = (sampleBlock + 0.5) * _CloudQuarterSize.zw;
                        float2 data = tex2Dlod(_CloudQuarterData, float4(sampleUv, 0, 0)).rg;
                        float4 n = tex2Dlod(_CloudQuarterColour, float4(sampleUv, 0, 0));
                        float2 marchUv = (sampleBlock * 2.0 + _CloudChecker.xy + 0.5) * _CloudLowResSize.zw;
                        float cloudEye = n.a > 0.002 ? data.g / length(ViewRay(marchUv)) : 1e20;
                        float sampleDepth = min(data.r, cloudEye), pixelDepth = min(sceneDepth, cloudEye);
                        float difference = abs(sampleDepth - pixelDepth) / max(1.0, min(sampleDepth, pixelDepth));
                        if (difference >= 0.1) continue;
                        float2 offset = sampleBlock * 2.0 + _CloudChecker.xy - texel;
                        float w = exp(-dot(offset, offset) * 0.275) / (0.02 + difference);
                        upsampled += n * w;
                        // Empty rays carry a far fallback, not a cloud point. Weight
                        // depth by visible opacity so sky cannot drag edge parallax away.
                        float cloudWeight = n.a > 0.002 ? n.a * w : 0.0;
                        distanceSum += data.g * cloudWeight;
                        distanceWeight += cloudWeight;
                        total += w;
                        low = min(low, n);
                        high = max(high, n);
                    }
                }
                upsampled /= max(1e-5, total);
                if (total <= 1e-5) return Resolved(0.0, sceneDepth, sceneDepth);
                float distance = distanceWeight > 1e-5 ? distanceSum / distanceWeight : freshData.g;
                if (fresh) { upsampled = freshVal; distance = freshData.g; }
                float cloudDepth = distance / viewLength;
                // No history (first frame, a cut, a zoom): fresh texels are sharp, the rest
                // fall back to the upsampled march. Sharpness returns over the next frames.
                if (_CloudHistoryValid < 0.5 || _CloudChecker.w < 0.5) return Resolved(upsampled, sceneDepth, cloudDepth);

                // This texel's ray, out to the cloud distance its fresh neighbour measured,
                // seen from last frame's camera.
                float3 ray = view / viewLength;
                float4 clip = mul(_CloudPrevMatrix, float4(ray * distance + _CloudCamDelta.xyz, 1.0));
                float2 previous = clip.xy / clip.w * 0.5 + 0.5;
                if (clip.w <= 0.0 || any(previous < 0.0) || any(previous > 1.0)) return Resolved(upsampled, sceneDepth, cloudDepth);
                // Bilinear history must also reject foreground neighbours; testing only one
                // depth texel would still let its empty colour bleed through interpolation.
                float2 historyPixel = previous * _CloudLowResSize.xy - 0.5;
                float2 historyBase = floor(historyPixel), fraction = frac(historyPixel);
                float4 history = 0.0;
                float historyWeight = 0.0;
                [unroll]
                for (int y = 0; y <= 1; y++)
                {
                    [unroll]
                    for (int x = 0; x <= 1; x++)
                    {
                        float2 huv = (historyBase + float2(x, y) + 0.5) * _CloudLowResSize.zw;
                        float depth = tex2Dlod(_CloudHistoryDepth, float4(huv, 0, 0)).r;
                        // clip.w is this cloud point's previous eye depth. A changed
                        // terrain distance behind it cannot invalidate the visible cloud.
                        float pointDepth = upsampled.a > 0.002 ? clip.w : sceneDepth;
                        depth = min(depth, pointDepth);
                        float difference = abs(depth - pointDepth) / max(1.0, min(depth, pointDepth));
                        float w = (x == 0 ? 1.0 - fraction.x : fraction.x) *
                            (y == 0 ? 1.0 - fraction.y : fraction.y);
                        if (difference >= 0.1) continue;
                        history += tex2Dlod(_CloudHistoryTex, float4(huv, 0, 0)) * w;
                        historyWeight += w;
                    }
                }
                if (historyWeight <= 1e-5) return Resolved(upsampled, sceneDepth, cloudDepth);
                history /= historyWeight;
                float4 carried = clamp(history, low, high);
                // A single representative depth cannot reproject all the material in a
                // nearby volume. Reduce history when translation is large relative to it.
                // Distant skies keep the same temporal savings and accumulation.
                float motion = max(saturate(length(_CloudCamDelta.xyz) / max(4.0, distance * 0.08)), _CloudCamDelta.w);
                // Repeated bilinear history resampling softens moving features. Refresh in
                // proportion to their movement on this target, retaining accumulation at rest.
                motion = max(motion, 0.5 * saturate(length((previous - uv) * _CloudLowResSize.xy)));
                // Fresh neighbours expose a local flash promptly without clearing sky history.
                motion = max(motion, saturate(_CloudFlashChange * 12.0));
                if (!fresh) return Resolved(lerp(carried, upsampled, motion), sceneDepth, cloudDepth);
                // The clamp already pulled the carried value into the fresh range, so new and
                // vanished cloud still converge within a few frames; dense texels track the
                // fresh march faster to keep their detail crisp.
                return Resolved(lerp(carried, freshVal, max(0.10 + 0.40 * freshVal.a, motion)), sceneDepth, cloudDepth);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
