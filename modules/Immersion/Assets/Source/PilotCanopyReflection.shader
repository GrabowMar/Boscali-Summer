Shader "Boscali/PilotCanopyReflection"
{
    Properties
    {
        _PilotTex ("Pilot capture", 2D) = "black" {}
        _CaptureRect ("Pilot plane min XY and size", Vector) = (-.6,-.8,1.2,1.6)
        _EyeWorld ("Final cockpit eye", Vector) = (0,0,0,1)
        _ReflectionStrength ("Coated glass reflection", Range(0,.12)) = .085
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+90" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Offset -1,-1
        Blend One OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _PilotTex;
            float4 _CaptureRect, _EyeWorld;
            float4x4 _WorldToPilot;
            float _ReflectionStrength;
            struct Input { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct Output { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; };
            Output vert(Input v)
            {
                Output o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            float4 frag(Output i) : SV_Target
            {
                float3 incoming = normalize(i.world - _EyeWorld.xyz);
                float3 normal = normalize(i.normal);
                float3 ray = reflect(incoming, normal);
                float3 origin = mul(_WorldToPilot, float4(i.world,1)).xyz;
                float3 direction = mul((float3x3)_WorldToPilot, ray);
                clip(abs(direction.z) - .0001);
                float distance = -origin.z / direction.z;
                clip(distance);
                float2 planePoint = origin.xy + direction.xy * distance;
                float2 uv = (planePoint - _CaptureRect.xy) / _CaptureRect.zw;
                // Captured from the front: camera-right is opposite pilot-right.
                uv.x = 1 - uv.x;
                clip(uv.x); clip(uv.y); clip(1 - uv.x); clip(1 - uv.y);
                float4 pilot = tex2D(_PilotTex, uv);
                float edge = saturate(min(min(uv.x,1-uv.x), min(uv.y,1-uv.y)) * 24);
                float grazing = 1 - saturate(abs(dot(normal, -incoming)));
                float opacity = min(.12, _ReflectionStrength) * (.35 + .65 * grazing * grazing) * edge;
                // Filtering an opaque pilot over a transparent clear already premultiplies RGB by coverage.
                return float4(pilot.rgb * opacity, pilot.a * opacity);
            }
            ENDHLSL
        }
    }
}
