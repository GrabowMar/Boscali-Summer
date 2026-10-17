Shader "Boscali/PilotCanopyReflection"
{
    // Remade canopy reflection: the pilot seen in coated glass, not a flat additive ghost.
    // Schlick Fresnel (HelmetMath twin), a sun glint where the reflected ray meets the sun,
    // a four-tap softening of the low-res capture, chromatic splitting along the reflection,
    // and a faint green MFD bounce from below at night. Additive only: a reflection adds light.
    Properties
    {
        _PilotTex ("Pilot capture", 2D) = "black" {}
        _CaptureRect ("Pilot plane min XY and size", Vector) = (-.6,-.8,1.2,1.6)
        _EyeWorld ("Final cockpit eye", Vector) = (0,0,0,1)
        _ReflectionStrength ("Coated glass reflection", Range(0,2)) = .5
        _SunDirWorld ("Direction to sun", Vector) = (0,1,0,0)
        _SunColor ("Sun colour", Color) = (0,0,0,1)
        _Ambient ("Ambient light", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+90" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Offset -1,-1
        Blend One One
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _PilotTex;
            float4 _PilotTex_TexelSize;
            float4 _CaptureRect, _EyeWorld, _SunDirWorld;
            float4 _SunColor;
            float4x4 _WorldToPilot;
            float _ReflectionStrength, _Ambient;
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
                // Canopy curvature proxy: bend the normal a little across the pane so the
                // reflection swims as the head moves instead of sliding like a decal.
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
                // Softening: four taps of the 128/256 capture, split chromatically outward.
                float2 texel = _PilotTex_TexelSize.xy * 1.5;
                float2 split = direction.xy * .006;
                float3 pilot;
                pilot.r = tex2D(_PilotTex, uv + split + texel * float2(.5, .5)).r;
                pilot.g = tex2D(_PilotTex, uv).g;
                pilot.b = tex2D(_PilotTex, uv - split + texel * float2(-.5, -.5)).b;
                pilot += tex2D(_PilotTex, uv + texel * float2(-.5, .5)).rgb;
                pilot += tex2D(_PilotTex, uv + texel * float2(.5, -.5)).rgb;
                pilot *= .3333;
                float edge = saturate(min(min(uv.x,1-uv.x), min(uv.y,1-uv.y)) * 24);
                // Schlick Fresnel on coated glass: faint head-on, strong at a grazing view.
                float cosTheta = saturate(dot(normal, -incoming));
                float fres = .04 + .96 * pow(1 - cosTheta, 5);
                // One front-view plane only stands in for the pilot where the glass mirrors the
                // pilot head-on; seen edge-on it smears into streaks, so side panes fade out.
                float facing = saturate((abs(direction.z) - .3) * 2.5);
                float darkBoost = .75 + (1 - _Ambient) * .5;
                float opacity = _ReflectionStrength * fres * 3.2 * darkBoost * edge * facing;
                float3 col = pilot * opacity * 1.4;
                // Sun glint: the pane flashes where the pilot's reflection meets the sun's.
                float glint = pow(saturate(dot(ray, normalize(_SunDirWorld.xyz))), 60);
                col += _SunColor.rgb * glint * _ReflectionStrength * .35 * edge;
                // MFD bounce at night: instrument glow lifts the lower reflection, green as glass displays are.
                float bounce = (1 - uv.y) * (1 - _Ambient) * _ReflectionStrength;
                col += float3(.15, .5, .28) * pilot * bounce * .18 * edge * facing;
                return float4(saturate(col), 0);
            }
            ENDHLSL
        }
    }
}
