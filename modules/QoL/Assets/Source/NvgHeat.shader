// Original depth-tested heat mask. The phosphor composite extracts its silhouette.
Shader "Boscali/NvgHeat"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" }
        Pass
        {
            Blend One One
            BlendOp Max
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _NvgHeat;
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                return float4(_NvgHeat, 0.0, 0.0, 0.0);
            }
            ENDCG
        }
    }
}
