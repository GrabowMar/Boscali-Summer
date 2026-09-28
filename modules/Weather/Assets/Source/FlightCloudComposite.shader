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
    }
    Fallback Off
}
