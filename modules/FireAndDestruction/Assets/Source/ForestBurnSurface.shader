Shader "Boscali/ForestBurnSurface"
{
    SubShader
    {
        Tags { "Queue"="Geometry+10" "RenderType"="Opaque" }
        ZWrite Off Cull Off Offset -1,-1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            struct A { float4 p:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 p:SV_POSITION; float2 world:TEXCOORD0; UNITY_FOG_COORDS(1) };
            V vert(A v) { V o; o.p=UnityObjectToClipPos(v.p); o.world=v.uv; UNITY_TRANSFER_FOG(o,o.p); return o; }
            float hash(float2 p) { return frac(sin(dot(p,float2(12.9898,78.233)))*43758.5453); }
            float noise(float2 p)
            {
                float2 cell=floor(p), t=frac(p); t=t*t*(3.0-2.0*t);
                return lerp(lerp(hash(cell),hash(cell+float2(1,0)),t.x),
                    lerp(hash(cell+float2(0,1)),hash(cell+float2(1,1)),t.x),t.y);
            }
            float4 frag(V i):SV_Target
            {
                // Global UVs keep the mottling continuous across shared cell edges and
                // stationary through floating-origin shifts; interpolation hides the grid.
                float2 p=float2(i.world.x*0.83+i.world.y*0.56,i.world.y*0.83-i.world.x*0.56);
                float ash=0.055+noise(p*0.11)*0.065+noise(p*0.72)*0.018;
                float4 color=float4(ash,ash*0.93,ash*0.82,1);
                UNITY_APPLY_FOG(i.fogCoord,color); return color;
            }
            ENDCG
        }
    }
}
