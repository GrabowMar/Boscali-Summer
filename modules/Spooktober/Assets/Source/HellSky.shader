Shader "Boscali/HellSky"
{
    Properties { _Intensity ("Storm intensity", Float) = 1 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background+10" }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back
            CGPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #include "UnityCG.cginc"
            float4 vertDepth(appdata_base v):SV_POSITION { return UnityObjectToClipPos(v.vertex); }
            float4 fragDepth():SV_Target { return 0; }
            ENDCG
        }
        Pass
        {
            Cull Back
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Intensity;
            struct v2f { float4 pos:SV_POSITION; float3 dir:TEXCOORD0; };
            float H(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float N(float2 p)
            {
                float2 i=floor(p),f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(H(i),H(i+float2(1,0)),f.x),lerp(H(i+float2(0,1)),H(i+1),f.x),f.y);
            }
            v2f vert(appdata_base v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.dir=v.vertex.xyz; return o; }
            float4 frag(v2f i):SV_Target
            {
                float3 d=normalize(i.dir);
                float2 p=float2(atan2(d.z,d.x)*2,d.y*5);
                float t=_Time.y*.008;
                float cloud=N(p*2+float2(t,0))*.6+N(p*5-float2(t,t))*.27+N(p*11+t)*.13;
                float horizon=pow(saturate(1-abs(d.y)),4);
                float3 sky=lerp(float3(.022,.007,.021),float3(.23,.035,.044),horizon);
                sky=lerp(sky*.4,sky*1.6,smoothstep(.23,.8,cloud));
                sky+=float3(.34,.055,.018)*pow(horizon,5)*pow(cloud,2);
                float cycle=floor(_Time.y/9);
                float flash=pow(saturate(1-frac(_Time.y/9)*24),3);
                float fork=sin(p.y*17+cycle)*.018+sin(p.y*41+cycle*3)*.008;
                float x=H(float2(cycle,3))*5-2.5;
                float bolt=pow(saturate(1-abs(p.x-x-fork)*170),3)*smoothstep(-.1,.45,d.y);
                sky+=float3(.55,.28,.6)*(bolt*flash+flash*.035)*_Intensity;
                return float4(sky,1);
            }
            ENDCG
        }
    }
}
