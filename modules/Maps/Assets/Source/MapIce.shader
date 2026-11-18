Shader "Boscali/MapIce"
{
    // The ice of a Boscali map: a second skin over the ground's own mesh, drawn only where the vertex alpha says there
    // is ice. The surface is the cracked ice of brogli's Unity-URP-ShaderGraph-Ice-Shader (MIT; its textures and its
    // parallax loop, ported from the graph because a graph cannot take this mesh's per-vertex ice mask): crack layers
    // seen at depth inside bare ice, at two scales (underfoot, and the great fractures seen from the air), snow lying
    // over it where the map painted the ice white. Lit as ice: light bleeds round the terminator, shade turns blue,
    // thin ice against the sun glows, the surface glints.
    Properties
    {
        _Deep ("Colour of light inside the ice", Color) = (.10, .45, .62, 1)
        _Scatter ("Scattering", Range(0,2)) = 1
        _IceColor ("Ice colour", 2D) = "white" {}
        _IceMask ("Crack mask", 2D) = "white" {}
        _IceNormal ("Ice normal", 2D) = "bump" {}
        _Noise ("Noise", 2D) = "gray" {}
        _Snow ("Snow", 2D) = "white" {}
        _SnowNormal ("Snow normal", 2D) = "bump" {}
        _Near ("Metres per tile, close", Float) = 48
        _Far ("Metres per tile, away", Float) = 900
        _ParallaxOffset ("Parallax offset", Float) = 1.17
        _NoiseScale ("Noise scale", Float) = 0.2
        _NoiseDepth ("Noise depth", Float) = 1
        _DepthColor ("Colour of the depths", Color) = (.02, .16, .30, 1)
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
            TEXTURE2D(_IceColor); SAMPLER(sampler_IceColor);
            TEXTURE2D(_IceMask); SAMPLER(sampler_IceMask);
            TEXTURE2D(_IceNormal); SAMPLER(sampler_IceNormal);
            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            TEXTURE2D(_Snow); SAMPLER(sampler_Snow);
            TEXTURE2D(_SnowNormal); SAMPLER(sampler_SnowNormal);
            CBUFFER_START(UnityPerMaterial)
            half4 _Deep;
            half4 _DepthColor;
            half _Scatter;
            float _Near, _Far, _ParallaxOffset, _NoiseScale, _NoiseDepth;
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
            // The graph's ParallaxLoop: the crack mask sampled again and again, each time pushed further along the view
            // ray, so cracks show as sheets going down into the ice.
            half Cracks(float2 uv, half2 ray, int steps)
            {
                half result = 1;
                float total = 0;
                for (int k = 0; k < steps; k++)
                {
                    total -= _ParallaxOffset * 0.01;
                    float2 at = uv + ray * total;
                    half mask = SAMPLE_TEXTURE2D(_IceMask, sampler_IceMask, at).r + SAMPLE_TEXTURE2D(_Noise, sampler_Noise, at * _NoiseScale).r * _NoiseDepth;
                    result *= saturate(mask + (half)k / steps);
                }
                return result;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half fine = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, i.map / 37.0).r, broad = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, i.map / 2300.0 + .37).r;
                clip(i.color.a - .5 + (fine - .5) * .3);   // a ragged edge where the ice thins out
                half3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                half2 ray = view.xz / max(view.y, .25);     // the ground is the tangent plane: x east, z north
                float away = saturate((distance(i.positionWS, GetCameraPositionWS()) - 500.0) / 2500.0);
                float2 close = i.map / _Near, wide = i.map / _Far;
                half cracks = lerp(Cracks(close, ray, 6), 1, away) * lerp(1, Cracks(wide, ray, 4), .5 - .3 * away);   // the great fractures fade with distance: from the air the sheet is white
                // how much snow lies on the ice here: the map paints snow white and bare ice blue
                half snowy = smoothstep(.28, .7, i.color.r + (fine - .5) * .25) * smoothstep(.25, .6, broad + .35 * i.color.r);
                half3 ice = SAMPLE_TEXTURE2D(_IceColor, sampler_IceColor, close).rgb * 2.2 * lerp(i.color.rgb, half3(.55, .78, .92), .45);
                ice = lerp(lerp(_DepthColor.rgb, ice, cracks), half3(.6, .8, .93), .45);   // blue ice is pale from the air, not sea blue
                half3 snow = (.88 + .5 * SAMPLE_TEXTURE2D(_Snow, sampler_Snow, i.map / 23.0).rgb) * lerp(i.color.rgb, half3(1, 1, 1), .5) * (.92 + .14 * broad);   // the snow texture is its sparkle only, nearly black
                half3 base = lerp(ice, snow, snowy);
                half3 bumpIce = UnpackNormal(SAMPLE_TEXTURE2D(_IceNormal, sampler_IceNormal, close));
                half3 bumpSnow = UnpackNormal(SAMPLE_TEXTURE2D(_SnowNormal, sampler_SnowNormal, i.map / 23.0));
                half3 bump = lerp(bumpIce, bumpSnow, snowy) * (1 - .7 * away);
                half3 normal = normalize(i.normalWS + half3(bump.x, 0, bump.y) * .55);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half shadow = sun.shadowAttenuation * sun.distanceAttenuation;
                half wrap = saturate((dot(normal, sun.direction) + .45) / 1.45) * shadow;
                half3 lit = base * sun.color * wrap;
                half3 shade = base * _Deep.rgb * (1 - wrap) * (.75 * _Scatter);
                half3 glow = _Deep.rgb * sun.color * (pow(saturate(dot(view, -normalize(sun.direction + normal * .35))), 3) * .8 * _Scatter) * (1 - .6 * snowy);
                half3 sky = max(half3(.03, .035, .04), SampleSH(normal)) * base * half3(.85, .95, 1.1);
                half rim = pow(1 - saturate(dot(normal, view)), 4);
                half gloss = lerp(260, 40, snowy);          // bare ice is a mirror, snow a soft sheen
                half glint = pow(saturate(dot(normal, normalize(sun.direction + view))), gloss) * lerp(1.6, .25, snowy) * shadow;
                half3 colour = lit + shade + glow + sky * .65 + rim * half3(.5, .66, .82) * lerp(.45, .2, snowy) + sun.color * glint * .7;
                return half4(MixFog(colour, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
