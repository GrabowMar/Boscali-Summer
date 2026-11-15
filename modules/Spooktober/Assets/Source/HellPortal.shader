// A wound in the ground: opaque depth, a torn ash-dark lip and a few weak crimson stress seams.
// No rotating polar patterns or luminous outline. Plain CG works in the game's URP and the offline compiler.
Shader "Boscali/HellPortal"
{
    Properties
    {
        _Intensity ("Crimson seam strength", Range(0, 1)) = 0.7
        _Strength ("Opening strength", Range(0, 1)) = 1
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
            float _Intensity, _Strength;
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float3 view:TEXCOORD1; };
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 cell=floor(p), f=frac(p); f=f*f*(3.0-2.0*f);
                return lerp(lerp(Hash(cell),Hash(cell+float2(1,0)),f.x),lerp(Hash(cell+float2(0,1)),Hash(cell+1.0),f.x),f.y);
            }
            v2f vert(appdata_base v)
            {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord.xy*2.0-1.0;
                o.view=mul((float3x3)unity_WorldToObject, _WorldSpaceCameraPos-mul(unity_ObjectToWorld,v.vertex).xyz);
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float t=fmod(_Time.y,1000.0);
                float2 p=i.uv;
                // Slow breathing and local advection; nothing rotates around a center.
                float breath=sin(t*.19)*.003;
                float2 drift=float2(Noise(p*3.7+float2(t*.014,0)),Noise(p*4.1+float2(11,-t*.011)))-.5;
                float2 warped=p+drift*.012;
                float radius=length(warped);
                float2 direction=warped/max(radius,.001);
                // The fully opaque region always covers the reversible .78-radius physical terrain cut.
                float edge=max(.82,.865+(Noise(direction*3.2+5.7)-.5)*.19+
                    (Noise(direction*9.1+2.4)-.5)*.035+breath);
                float alpha=(1.0-smoothstep(edge-.003,edge+.018,radius))*saturate(_Strength);
                clip(alpha-.001);
                float normalized=radius/max(edge,.1);
                float lip=smoothstep(.64,.92,normalized);
                float rock=Noise(p*31.0+Noise(p*7.0)*1.6);
                // The center is opaque and almost unlit. Parallax only moves faint obscured matter in its throat.
                float2 view=clamp(i.view.xy/max(abs(i.view.z),.3),-1.0,1.0);
                float2 deep=warped+view*.075*(1.0-saturate(normalized));
                float depth=Noise(deep*8.0+float2(t*.009,-t*.006));
                float3 color=float3(.0003,.00022,.0002)+float3(.009,.007,.006)*lip*(.55+rock*.45);
                color+=float3(.0005,.00012,.00008)*depth*(1.0-lip);
                // Three broken hairline stress scars. Their field is linear, jagged and gated into short fragments.
                float jag=(Noise(p*19.0)-.5)*.075;
                float scarDistance=min(abs(p.x*.78+p.y*.31+jag-.34),abs(p.x*.23-p.y*.89-jag+.29));
                scarDistance=min(scarDistance,abs(p.x*.64-p.y*.62+jag+.46));
                float seam=(1.0-smoothstep(.0015,.0045,scarDistance))*smoothstep(.72,.88,normalized)*
                    (1.0-smoothstep(.97,1.02,normalized))*smoothstep(.48,.67,Noise(p*23.0+7.0));
                float pulse=.24+.76*pow(.5+.5*sin(t*.29+Noise(p*5.0)*5.0),4.0);
                color+=float3(.105,.0018,.0006)*seam*pulse*saturate(_Intensity);
                return float4(color*alpha,alpha);
            }
            ENDCG
        }
    }
}
