// One-time CPU trail topology supplies arrival time and final coverage; the shader only reveals it.
// The soot colour follows the existing wasteland. No textures, emission, trail loops or moving final pattern.
Shader "Boscali/HellInfection"
{
    Properties
    {
        _GrowthStart ("Growth start (game seconds)", Float) = 0
        _GrowthDuration ("Growth duration (seconds)", Float) = 40
    }
    SubShader
    {
        Tags { "Queue"="Transparent-20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Offset -1,-1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            float _GrowthStart, _GrowthDuration;
            struct A { float4 p:POSITION; float2 world:TEXCOORD0; float2 growth:TEXCOORD1; };
            struct V { float4 p:SV_POSITION; float2 world:TEXCOORD0; float2 growth:TEXCOORD1; UNITY_FOG_COORDS(2) };
            V vert(A v)
            {
                V o; o.p=UnityObjectToClipPos(v.p); o.world=v.world; o.growth=v.growth;
                UNITY_TRANSFER_FOG(o,o.p); return o;
            }
            float hash(float2 p) { return frac(sin(dot(p,float2(12.9898,78.233)))*43758.5453); }
            float noise(float2 p)
            {
                float2 cell=floor(p), t=frac(p); t=t*t*(3.0-2.0*t);
                float value=lerp(lerp(hash(cell),hash(cell+float2(1,0)),t.x),
                    lerp(hash(cell+float2(0,1)),hash(cell+float2(1,1)),t.x),t.y);
                // Unresolved grain becomes its mean instead of shimmering into a bright confetti field.
                float footprint=max(length(ddx(p)),length(ddy(p)));
                return lerp(value,.5,smoothstep(.5,2.0,footprint));
            }
            float4 frag(V i):SV_Target
            {
                float coverage=saturate(i.growth.y);
                clip(coverage-.0001);
                float age=clamp(_Time.y-_GrowthStart,0.0,max(.001,_GrowthDuration));
                float elapsed=age-i.growth.x;
                clip(elapsed-.00001);
                float front=smoothstep(0.0,2.0,elapsed);
                float2 p=float2(i.world.x*.83+i.world.y*.56,i.world.y*.83-i.world.x*.56);
                float broad=noise(p*.11), grain=noise(p*.72), drift=noise(p*.35+17.0);
                // Connectivity comes entirely from the authored ant network; noise only roughens its soft boundary.
                float edge=smoothstep(.025,.32,coverage+(broad-.5)*.14+(grain-.5)*.04);
                float alpha=front*edge;
                clip(alpha-.001);
                float ash=.016+broad*.03+grain*.008;
                ash+=smoothstep(.72,.9,drift)*.032;
                float3 ambient=clamp(ShadeSH9(float4(0,1,0,1))*1.4,.25,1.6);
                float4 color=float4(float3(ash,ash*.94,ash*.88)*ambient,alpha);
                UNITY_APPLY_FOG(i.fogCoord,color);
                return float4(color.rgb*alpha,alpha);
            }
            ENDCG
        }
    }
}
