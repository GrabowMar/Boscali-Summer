Shader "Boscali/FlightCloud"
{
    Properties { _CloudDensity ("Density", Range(0,2)) = 1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Front
        Pass
        {
            Name "CloudBody"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _CameraDepthTexture;
            sampler3D _CloudNoiseTex;
            float3 _CloudWorldOffset, _CloudCameraForward, _CloudSunDirection;
            float3 _CloudSunColor, _CloudAmbientColor, _CloudFogColor;
            float2 _CloudWindOffset;
            float _CloudDensity, _CloudType, _CloudSteps, _CloudStorm;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 screen : TEXCOORD0;
                float3 world : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.pos);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float Density(float3 local, float3 world)
            {
                float3 p = local * 2.0;
                float h = local.y + 0.5;
                float3 g = world + _CloudWorldOffset;
                float3 n1 = float3((g.xz - _CloudWindOffset) / 7100.0, g.y / 3500.0);
                float3 n2 = float3((g.xz - _CloudWindOffset * 1.7) / 1900.0, g.y / 1500.0);
                float2 broad = tex3Dlod(_CloudNoiseTex, float4(n1, 0)).rg;
                float2 detail = tex3Dlod(_CloudNoiseTex, float4(n2, 0)).rg;
                float deckShape = smoothstep(1.0, 0.82,
                    max(abs(p.x), abs(p.z)) + (0.5 - broad.r) * 0.13) *
                    smoothstep(0.0, 0.07, h) * (1.0 - smoothstep(0.81, 0.98, h));
                float core = length(float3(p.x * 1.12, (p.y - 0.04) * 0.95, p.z * 1.12));
                float left = length(float3((p.x + 0.46) / 0.72,
                    (p.y + 0.35) / 0.76, (p.z - 0.10) / 0.80));
                float right = length(float3((p.x - 0.43) / 0.69,
                    (p.y + 0.27) / 0.77, (p.z + 0.18) / 0.76));
                float lobe = min(core, min(left, right)) +
                    (0.5 - broad.r) * 0.27 + (0.5 - detail.r) * 0.08;
                float volumeEdge = max(max(abs(p.x), abs(p.z)), p.y);
                float cumulusShape = smoothstep(1.10, 0.78, lobe) *
                    smoothstep(0.0, 0.08, h) * smoothstep(1.0, 0.82, volumeEdge);
                float shape = broad.r * 0.68 + broad.g * 0.15 + detail.r * 0.17;
                float erosion = saturate((shape - (_CloudType > 0.5 ? 0.40 : 0.40)) * 5.4);
                erosion *= lerp(0.62, 1.0, detail.g);
                return (_CloudType > 0.5 ? deckShape : cumulusShape) * erosion * _CloudDensity;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float3 origin = _WorldSpaceCameraPos.xyz;
                float3 ray = normalize(i.world - origin);
                float3 ro = mul(unity_WorldToObject, float4(origin, 1)).xyz;
                float3 rd = mul((float3x3)unity_WorldToObject, ray);
                float3 safeDir = rd + (1.0 - abs(sign(rd))) * 1e-6;
                float3 inv = 1.0 / safeDir;
                float3 lo = (-0.5 - ro) * inv;
                float3 hi = ( 0.5 - ro) * inv;
                float3 a = min(lo, hi), b = max(lo, hi);
                float start = max(0.0, max(a.x, max(a.y, a.z)));
                float finish = min(b.x, min(b.y, b.z));
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                float sceneDistance = LinearEyeDepth(rawDepth) /
                    max(0.025, dot(ray, _CloudCameraForward));
                finish = min(finish, min(sceneDistance, 18000.0));
                if (finish <= start) discard;

                float span = finish - start;
                float steps = _CloudSteps;
                if (start > 17000.0) steps *= 0.58;
                float stepLength = span / max(1.0, steps);
                float t = start + 0.5 * stepLength;
                float transmittance = 1.0;
                float3 colour = 0.0;
                float phase = 0.72 + 0.8 * pow(saturate(dot(ray, _CloudSunDirection)), 10.0);
                [loop]
                for (int n = 0; n < 24; n++)
                {
                    if (n >= steps || t >= finish || transmittance < 0.025) break;
                    float3 world = origin + ray * t;
                    float3 local = ro + rd * t;
                    float d = Density(local, world);
                    if (d > 0.003)
                    {
                        float3 lightWorld = world + _CloudSunDirection * 850.0;
                        float3 lightLocal = local + mul((float3x3)unity_WorldToObject,
                            _CloudSunDirection * 850.0);
                        float shadow = Density(lightLocal, lightWorld);
                        float h = saturate(local.y + 0.5);
                        float3 light = _CloudAmbientColor * lerp(0.23, 0.82, h) +
                            _CloudSunColor * exp(-shadow * 4.0) * phase *
                            lerp(0.25, 0.92, h);
                        light *= lerp(1.0, 0.65, _CloudStorm);
                        light = min(light, 1.0);
                        float fog = saturate((t - 15000.0) / 30000.0);
                        light = lerp(light, _CloudFogColor, fog * 0.65);
                        float absorb = exp(-d * stepLength * 0.003);
                        colour += transmittance * (1.0 - absorb) * light;
                        transmittance *= absorb;
                    }
                    t += stepLength;
                }
                return float4(colour, 1.0 - transmittance);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
