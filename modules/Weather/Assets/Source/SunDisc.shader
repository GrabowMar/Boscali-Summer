// Unlit native sun sphere, before the cloud composite (queue 2997). The composite contributes
// one final power of transmission; these seven suppress bloom leaking through dense cloud.
Shader "Boscali/SunDisc"
{
    Properties
    {
        [HDR] _EmissionColor ("Sun radiance", Color) = (1,1,1,1)
        _SunCloudMask ("Resolved clouds", Float) = 0
        _SunFallbackTransmission ("Native cloud transmission", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-50" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back ZWrite Off ZTest LEqual
            Blend One One, Zero One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _BoscaliCloudCover;
            float4 _EmissionColor;
            float _SunCloudMask;
            float _SunFallbackTransmission;
            float _BoscaliCloudCoverFrame;
            struct v2f { float4 pos : SV_POSITION; float4 screen : TEXCOORD0; };
            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float open = saturate(1.0 - tex2Dproj(_BoscaliCloudCover, UNITY_PROJ_COORD(i.screen)).a);
                float extinction = open * open * open;
                extinction *= extinction * open;
                extinction = lerp(_SunFallbackTransmission, extinction, _SunCloudMask * _BoscaliCloudCoverFrame);
                return float4(_EmissionColor.rgb * extinction, 0.0);
            }
            ENDCG
        }
    }
}
