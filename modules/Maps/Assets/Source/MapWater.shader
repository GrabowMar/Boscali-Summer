Shader "Boscali/MapWater"
{
    // The fresh water of a Boscali map: the level sheet of a river or a lake, closed in by the ground it lies in.
    // Per vertex the bake says how deep the water is (red: the bed shows through the shallows), how fast it falls
    // (green: white water) and where the sheet ends (alpha: it thins out in a dither). The uv is metres, so the
    // ripples stay put when the floating origin shifts the world; they run down the v axis at _Flow.
    // Opaque on purpose: a transparent sheet would draw over the game's clouds.
    Properties
    {
        _Shallow ("Water over the shallows", Color) = (.40, .47, .33, 1)
        _Deep ("Deep water", Color) = (.07, .24, .26, 1)
        _Flow ("Flow along v, m/s", Float) = 1.4
        _Ripple ("Ripple size, m", Float) = 6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+3" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Shallow;
            half4 _Deep;
            float _Flow, _Ripple;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : COLOR;
                float fog : TEXCOORD2;
            };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            // value noise and its slope: x the value, yz its derivative
            float3 Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f), du = 6 * f * (1 - f);
                float a = Hash(cell), b = Hash(cell + float2(1, 0)), c = Hash(cell + float2(0, 1)), d = Hash(cell + float2(1, 1));
                float k = a - b - c + d;
                return float3(a + (b - a) * u.x + (c - a) * u.y + k * u.x * u.y, du * float2(b - a + k * u.y, c - a + k * u.x));
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(i.color.a - frac(52.9829189 * frac(dot(i.positionCS.xy, float2(.06711056, .00583715)))) * .98 - .01);
                float t = _Time.y * _Flow;
                float3 fine = Noise((i.uv - float2(0, t)) / _Ripple);
                float3 mid = Noise((i.uv - float2(t * .13, t * .62)) / (_Ripple * 2.7) + 17.3);
                float3 broad = Noise((i.uv - float2(0, t * .8)) / (_Ripple * 9.0) + 5.1);
                float away = saturate(distance(i.positionWS, GetCameraPositionWS()) / 1800.0);   // small ripples only shimmer from afar
                float2 slope = ((fine.yz * .5 + mid.yz * .6) * (1 - away) + broad.yz * .5) * (.22 * (.5 + 1.5 * i.color.g));
                half3 normal = normalize(half3(-slope.x, 1, -slope.y));
                half3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 body = lerp(_Shallow.rgb, _Deep.rgb, smoothstep(0, 1, i.color.r));
                // the current: long streaks drawn out along the flow, seen from any height
                float streak = Noise(float2(i.uv.x / 4.0, (i.uv.y - t * 1.6) / 70.0)).x * .6 + Noise(float2(i.uv.x / 11.0 + 9.0, (i.uv.y - t) / 160.0)).x * .4;
                body *= .78 + .5 * streak * saturate(_Flow);
                half foam = smoothstep(.5, .8, fine.x * .6 + mid.x * .4 + i.color.g * .45 - .15) * saturate(i.color.g * 2.5);   // white water where it falls
                foam = max(foam, smoothstep(.12, 0, i.color.r) * smoothstep(.55, .75, mid.x) * .45);                             // lace along the banks
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = sun.shadowAttenuation * sun.distanceAttenuation;
                half3 ambient = max(half3(.03, .035, .04), SampleSH(normal));
                half3 lit = body * (sun.color * (saturate(dot(normal, sun.direction)) * shadow * .55) + ambient * .9);
                half3 sky = max(half3(.03, .035, .04), SampleSH(reflect(-view, normal)));
                half fresnel = .03 + .97 * pow(1 - saturate(dot(normal, view)), 5);
                half3 colour = lerp(lit, sky, saturate(fresnel * .85 + .08));
                colour += sun.color * (pow(saturate(dot(normal, normalize(sun.direction + view))), 380) * 2.2 * shadow);
                colour = lerp(colour, (sun.color * (shadow * .8) + ambient) * .85, foam);
                return half4(MixFog(colour, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
