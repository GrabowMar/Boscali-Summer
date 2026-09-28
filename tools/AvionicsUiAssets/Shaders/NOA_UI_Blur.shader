Shader "NOA/UI/Blur"
{
    Properties { _MainTex ("Source", 2D) = "black" {} _Offset ("Offset", Float) = 1 }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex; float4 _MainTex_TexelSize; float _Offset;
        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
        ENDCG
        Pass // 0: dual-Kawase downsample
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target
            {
                float2 h = _MainTex_TexelSize.xy * 0.5 * _Offset;
                half4 s = tex2D(_MainTex, i.uv) * 4;
                s += tex2D(_MainTex, i.uv - h); s += tex2D(_MainTex, i.uv + h);
                s += tex2D(_MainTex, i.uv + float2(h.x, -h.y)); s += tex2D(_MainTex, i.uv - float2(h.x, -h.y));
                return s / 8;
            }
            ENDCG
        }
        Pass // 1: dual-Kawase upsample
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target
            {
                float2 h = _MainTex_TexelSize.xy * 0.5 * _Offset;
                half4 s = tex2D(_MainTex, i.uv + float2(-h.x * 2, 0));
                s += tex2D(_MainTex, i.uv + float2(-h.x, h.y)) * 2;
                s += tex2D(_MainTex, i.uv + float2(0, h.y * 2));
                s += tex2D(_MainTex, i.uv + float2(h.x, h.y)) * 2;
                s += tex2D(_MainTex, i.uv + float2(h.x * 2, 0));
                s += tex2D(_MainTex, i.uv + float2(h.x, -h.y)) * 2;
                s += tex2D(_MainTex, i.uv + float2(0, -h.y * 2));
                s += tex2D(_MainTex, i.uv + float2(-h.x, -h.y)) * 2;
                return s / 12;
            }
            ENDCG
        }
    }
}
