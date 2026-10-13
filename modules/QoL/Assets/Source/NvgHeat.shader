// Night vision, the thermal channel: redraws a hot unit's own mesh as an additive glow that is strongest on its
// silhouette, so it reads as an outline up close and as a hot speck far away. Depth-tested: terrain hides it.
// Plain CG, no pipeline includes.
Shader "Boscali/NvgHeat"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" }
        Pass
        {
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _NvgHeatColor; // rgb: colour times brightness, already divided by the tube gain
            float _NvgHeat;       // 0-1 heat of the unit being drawn

            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float3 view : TEXCOORD1; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = _WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float facing = saturate(abs(dot(normalize(i.normal), normalize(i.view))));
                float rim = (1.0 - facing) * (1.0 - facing);
                // Past about 800 m a unit is a few pixels: light all of it.
                float far = saturate(length(i.view) / 800.0);
                return float4(_NvgHeatColor.rgb * (_NvgHeat * lerp(rim + 0.22, 1.2, far)), 0.0);
            }
            ENDCG
        }
    }
}
