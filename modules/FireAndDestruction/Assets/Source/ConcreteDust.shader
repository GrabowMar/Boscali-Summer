Shader "Boscali/ConcreteDust"
{
    Properties {_MainTex("Dust",2D)="white"{} _Softness("Soft depth",Float)=1.2}
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            Tags {"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); float _Softness;
            struct A {float4 p:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR;};
            struct V {float4 p:SV_POSITION; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; half4 color:COLOR;};
            V Vert(A i){V o;o.world=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.world);o.uv=i.uv;o.color=i.color;return o;}
            half4 Frag(V i):SV_Target
            {
                half4 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv)*i.color;
                float2 uv=GetNormalizedScreenSpaceUV(i.p);
                float rawDepth=SampleSceneDepth(uv);
                float scene=LinearEyeDepth(rawDepth,_ZBufferParams);
                float particle=-TransformWorldToView(i.world).z;
                float soft=saturate((scene-particle)/max(_Softness,0.01));
                #if UNITY_REVERSED_Z
                if(rawDepth>0.999999)soft=1;
                #else
                if(rawDepth<0.000001)soft=1;
                #endif
                c.a*=soft;
                Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
                c.rgb*=max(SampleSH(half3(0,1,0))+light.color*(0.35+0.45*light.shadowAttenuation),0.12);
                c.rgb=MixFog(c.rgb,ComputeFogFactor(i.p.z));return c;
            }
            ENDHLSL
        }
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent"}
        Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct A {float4 p:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
            struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
            V Vert(A i){V o;o.p=UnityObjectToClipPos(i.p);o.uv=i.uv;o.color=i.color;return o;}
            fixed4 Frag(V i):SV_Target{return tex2D(_MainTex,i.uv)*i.color;}
            ENDCG
        }
    }
}
