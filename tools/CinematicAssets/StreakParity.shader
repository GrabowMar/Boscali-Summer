Shader "Hidden/Boscali/Tests/StreakParity"
{
    SubShader
    {
        Pass
        {
            Cull Off ZTest Always ZWrite Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            float4 Vertex(uint vertexID : SV_VertexID) : SV_POSITION
            {
                // Sloping edges produce different coverage in individual MSAA samples.
                float2 p = vertexID == 0 ? float2(-0.71, -0.53) :
                    vertexID == 1 ? float2(0.21, -0.37) : float2(-0.43, 0.67);
                return float4(p, 0, 1);
            }
            float4 Fragment() : SV_Target { return float4(20, 9, 5, 0.37); }
            ENDCG
        }
    }
}
