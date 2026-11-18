Shader "Boscali/MapIce"
{
    // The ice of a Boscali map: a second skin over the ground's own mesh, drawn only where the vertex alpha says there
    // is ice. Ice is not lit like rock: light bleeds round the terminator, shade turns blue (red is absorbed along the
    // longer path through the ice), thin ice against the sun glows, and the surface glints.
    Properties
    {
        _Deep ("Colour of light inside the ice", Color) = (.10, .45, .62, 1)
        _Scatter ("Scattering", Range(0,2)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+2" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            Offset -1, -1
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
            half4 _Deep;
            half _Scatter;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 map : TEXCOORD2;
                half4 color : COLOR;
                float fog : TEXCOORD3;
            };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.map = v.uv * 81920.0;   // metres on the map: it stays put when the floating origin shifts the world
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }
            half4 frag(Varyings i) : SV_Target
            {
                half fine = Noise(i.map * .22), broad = Noise(i.map * .031 + 17);
                clip(i.color.a - .5 + (fine - .5) * .3);   // a ragged edge where the ice thins out
                half3 normal = normalize(i.normalWS + half3(fine - .5, 0, Noise(i.map.yx * .22 + 5) - .5) * .22);
                half3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = sun.shadowAttenuation * sun.distanceAttenuation;
                half wrap = saturate((dot(normal, sun.direction) + .45) / 1.45) * shadow;
                half3 base = i.color.rgb * (.94 + .12 * broad);
                half3 lit = base * sun.color * wrap;
                half3 shade = base * _Deep.rgb * (1 - wrap) * (.75 * _Scatter);
                half3 glow = _Deep.rgb * sun.color * (pow(saturate(dot(view, -normalize(sun.direction + normal * .35))), 3) * .8 * _Scatter);
                half3 sky = max(half3(.03, .035, .04), SampleSH(normal)) * base * half3(.85, .95, 1.1);
                half rim = pow(1 - saturate(dot(normal, view)), 4);
                half glint = pow(saturate(dot(normal, normalize(sun.direction + view))), 96) * step(.6, fine) * shadow;
                half3 colour = lit + shade + glow + sky * .65 + rim * half3(.5, .66, .82) * .3 + sun.color * glint * .7;
                return half4(MixFog(colour, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
