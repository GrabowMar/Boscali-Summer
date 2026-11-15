Shader "Boscali/KneeboardInk"
{
    Properties
    {
        _MainTex ("Font distance atlas", 2D) = "white" {}
        _Color ("Ink", Color) = (.02,.022,.02,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _Color;
            struct Input { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Output vert(Input i)
            {
                Output o; o.position = UnityObjectToClipPos(i.position); o.uv = i.uv; return o;
            }
            fixed4 frag(Output i) : SV_Target
            {
                // The card is a pixel-space immediate draw. It has no TMP camera or
                // canvas scale: derive coverage from the atlas at the actual raster size.
                float distance = tex2D(_MainTex, i.uv).a;
                float edge = max(fwidth(distance) * .6, .002);
                float alpha = smoothstep(.46 - edge, .46 + edge, distance) * _Color.a;
                return fixed4(_Color.rgb * alpha, alpha);
            }
            ENDCG
        }
    }
}
