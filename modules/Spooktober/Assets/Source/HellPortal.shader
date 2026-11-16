// A wound in the ground: opaque depth, a torn ash-dark lip and a few weak crimson stress seams.
// No rotating polar patterns or luminous outline. Plain CG works in the game's URP and the offline compiler.
Shader "Boscali/HellPortal"
{
    Properties
    {
        _Intensity ("Crimson seam strength", Range(0, 1)) = 0.7
        _Strength ("Opening strength", Range(0, 1)) = 1
        _GroundMesh ("Terrain envelope coordinates", Float) = 0
        _ExtentScale ("Envelope to opening radius", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "IgnoreProjector"="True" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Intensity, _Strength, _GroundMesh, _ExtentScale;
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 view:TEXCOORD1; };
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 cell=floor(p), f=frac(p); f=f*f*(3.0-2.0*f);
                return lerp(lerp(Hash(cell),Hash(cell+float2(1,0)),f.x),lerp(Hash(cell+float2(0,1)),Hash(cell+1.0),f.x),f.y);
            }
            v2f vert(appdata_base v)
            {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=(v.texcoord.xy*2.0-1.0)*_ExtentScale;
                float3 view=mul((float3x3)unity_WorldToObject, _WorldSpaceCameraPos-mul(unity_ObjectToWorld,v.vertex).xyz);
                o.view=lerp(view,view.xzy,_GroundMesh);
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float t=fmod(_Time.y,1000.0);
                float2 p=i.uv;
                // Slow breathing and local advection; nothing rotates around a center.
                float breath=sin(t*.19)*.006;
                float2 drift=float2(Noise(p*3.7+float2(t*.014,0)),Noise(p*4.1+float2(11,-t*.011)))-.5;
                float2 warped=p+drift*.012;
                float radius=length(warped);
                float2 direction=warped/max(radius,.001);
                // The fully opaque region always covers the reversible .78-radius physical terrain cut.
                float edge=max(.82,.865+(Noise(direction*3.2+5.7)-.5)*.19+
                    (Noise(direction*17.7+2.4)-.5)*.055+breath);
                float alpha=(1.0-smoothstep(edge-.003,edge+.018,radius))*saturate(_Strength);
                clip(alpha-.001);
                float normalized=radius/max(edge,.1);
                float lip=smoothstep(.70,.94,normalized);
                float rock=Noise(p*31.0+Noise(p*7.0)*1.6);
                // A displaced inner wall reveals the far side of a deep throat at grazing angles.
                // Cartesian tears and irregular ledges keep it from reading as a concentric ring.
                float2 view=clamp(i.view.xy/max(abs(i.view.z),.3),-1.0,1.0);
                float2 deep=warped+view*.23*(1.0-saturate(normalized));
                float depth=Noise(deep*8.0+float2(t*.009,-t*.006));
                float wall=smoothstep(.42,.82,length(warped+view*.19)+(depth-.5)*.13)*
                    (1.0-smoothstep(.91,1.0,normalized));
                float ledge=Noise(deep*12.0+float2(0,-t*.012));
                wall*=smoothstep(.19,.61,ledge)*(.35+.65*saturate(dot(direction,view)));
                float3 color=float3(.0002,.00014,.00012)+float3(.022,.014,.011)*lip*(.4+rock*.6);
                color+=float3(.016,.005,.003)*wall+float3(.0005,.00012,.00008)*depth*(1.0-lip);
                // Three broken hairline stress scars. Their field is linear, jagged and gated into short fragments.
                float jag=(Noise(p*19.0)-.5)*.105;
                float scarDistance=min(abs(p.x*.78+p.y*.31+jag-.34),abs(p.x*.23-p.y*.89-jag+.29));
                scarDistance=min(scarDistance,abs(p.x*.64-p.y*.62+jag+.46));
                float seam=(1.0-smoothstep(.0015,.0045,scarDistance))*smoothstep(.61,.86,normalized)*
                    (1.0-smoothstep(.97,1.02,normalized))*smoothstep(.48,.67,Noise(p*23.0+7.0));
                float pulse=.24+.76*pow(.5+.5*sin(t*.29+Noise(p*5.0)*5.0),4.0);
                color+=float3(.16,.0025,.0008)*seam*pulse*saturate(_Intensity);
                return float4(color*alpha,alpha);
            }
            ENDCG
        }
    }
}
