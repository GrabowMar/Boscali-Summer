Shader "Hidden/Boscali/BlueprinterMaterialCheck"
{
    Properties
    {
        _MainTex ("Legacy texture", 2D) = "black" {}
        _Normal_Crinkle ("Crinkle texture", 2D) = "black" {}
        _Amount ("Non-texture control", Float) = 0.5
    }
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _Normal_Crinkle;
            fixed4 frag(v2f_img input) : SV_Target { return tex2D(_Normal_Crinkle, input.uv); }
            ENDCG
        }
    }
}
