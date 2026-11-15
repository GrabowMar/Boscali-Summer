Shader "Hidden/Boscali/AgxLutComposite"
{
    Properties
    {
        _MainTex ("Native HDR grading LUT", 2D) = "white" {}
        _AgxLut ("AgX transform", 3D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler3D _AgxLut;

            float4 Frag(v2f_img input) : SV_Target
            {
                float3 hdrColor = max(tex2D(_MainTex, input.uv).rgb, 0.0);
                // The connected nodes in meenphie's URP/FullScreen LUT.shadergraph.
                // This is Alexa LogC, not AgX's internal log2 encoding. The supplied
                // EXRs already contain the view transform and selected contrast look.
                float3 uvw = saturate(log10(hdrColor * 5.555555820465088 + 0.047995999455451965)
                    * 0.244160994887352 + 0.3860360085964203);
                // Preserve the upstream importer's colour-space handling. No second
                // sRGB encode/decode: URP's final output pass handles display encoding.
                return float4(tex3Dlod(_AgxLut, float4(uvw, 0.0)).rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
