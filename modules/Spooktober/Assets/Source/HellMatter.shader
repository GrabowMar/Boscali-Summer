// Pipeline-independent obsidian armour and molten interiors. Most of the silhouette stays dark; light lives in cavities.
Shader "Boscali/HellMatter"
{
    Properties
    {
        _Char ("Obsidian", Color) = (.035,.038,.045,1)
        _Layer ("Armour / ribs / furnace", Float) = 0
        _Intensity ("Emission", Float) = .18
        _NoiseScale ("Surface scale", Float) = .65
        _Strength ("Strength", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
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
            float4 _Char;
            float _Layer, _Intensity, _NoiseScale, _Strength;
            struct v2f { float4 pos:SV_POSITION; float3 normal:TEXCOORD0; float3 view:TEXCOORD1; float3 p:TEXCOORD2; };
            float Hash(float3 p) { p=frac(p*.3183099+.1)*17; return frac(p.x*p.y*p.z*(p.x+p.y+p.z)); }
            float Noise(float3 p)
            {
                float3 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(lerp(Hash(i),Hash(i+float3(1,0,0)),f.x),lerp(Hash(i+float3(0,1,0)),Hash(i+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(Hash(i+float3(0,0,1)),Hash(i+float3(1,0,1)),f.x),lerp(Hash(i+float3(0,1,1)),Hash(i+1),f.x),f.y),f.z);
            }
            v2f vert(appdata_base v)
            {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex);
                float3 world=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.normal=UnityObjectToWorldNormal(v.normal);
                o.view=_WorldSpaceCameraPos-world;
                o.p=world-unity_ObjectToWorld._m03_m13_m23;
                return o;
            }
            float4 frag(v2f i):SV_Target
            {
                float3 n=normalize(i.normal), view=normalize(i.view), p=i.p*_NoiseScale;
                float grain=Noise(p*7), broad=Noise(p*.8);
                float lamination=sin((p.y+p.x*.22+p.z*.14)*27+Noise(p*2)*4);
                float key=saturate(dot(n,normalize(float3(-.4,.85,.25))));
                float rim=pow(1-saturate(abs(dot(n,view))),3);
                float3 reflected=reflect(-normalize(float3(-.4,.85,.25)),n);
                float spec=pow(saturate(dot(reflected,view)),26)*(.08+grain*.09);
                float3 rock=_Char.rgb*(.48+key*2.1)*(1+(broad-.5)*.5+lamination*.08)+spec+rim*.045;
                float vein=pow(saturate(1-abs(Noise(p*1.7)*2-1)),32);
                float pulse=.78+.22*sin(_Time.y*1.9+p.y*.12+Noise(p)*3);
                float3 fire=lerp(float3(.65,.012,.001),float3(2.4,.45,.025),saturate(grain*.8+broad*.4));
                float furnace=step(1.5,_Layer);
                float cracks=lerp(vein*.16,.48+grain*.52,furnace);
                return float4((lerp(rock,rock*.22,furnace)+fire*cracks*_Intensity*pulse)*_Strength,1);
            }
            ENDCG
        }
    }
}
