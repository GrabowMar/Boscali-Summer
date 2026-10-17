Shader "Boscali/VisorOverlay"
{
    // Near-future HMD visor glass, drawn under the HUD over one soft mask (G breath fog, B brow):
    // electrochromic tint, breath fog, the brow shade above 6 G, a faint coating that shifts hue
    // toward the rim, and hairline scratches that catch light only where the sun's direction
    // matches (drawn here at screen resolution, never baked). No helmet frame or outline.
    Properties
    {
        [PerRendererData] _MainTex ("Visor layers (G fog, B brow)", 2D) = "black" {}
        _TintColor ("Sun visor colour", Color) = (.30,.26,.20,1)
        _Tint ("Sun visor", Range(0,1)) = 0
        _SunLit ("Sun on scratches", Range(0,1)) = 0
        _Fog ("Breath fog", Range(0,1)) = 0
        _Brow ("Helmet brow", Range(0,1)) = 0
        _Light ("Ambient light", Range(0,1)) = 1
        _Curve ("Visor curvature", Range(0,1)) = .55
        _Reflect ("Reflection strength", Range(0,2)) = 1
        _SunView ("Sun position on the view (viewport xy, z in front)", Vector) = (.5,.5,-1,0)
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _TintColor;
            float _Tint, _SunLit, _Fog, _Brow, _Light;
            float _Curve, _Reflect;
            float4 _SunView;
            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            Output vert(Input v)
            {
                Output o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            // Spherical warp, twin of HelmetMath.Warp: identity at boresight, periphery pulled out.
            float2 Warp(float2 uv)
            {
                float2 c = uv * 2 - 1;
                float scale = 1 + _Curve * .18 * dot(c, c);
                return c * scale * .5 + .5;
            }
            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            // Hairline scratches on a jittered grid, one short segment in about a third of the cells.
            // A scratch lights up when its normal points at the sun (the glint ring a point light draws
            // on scored glass) and dies out with distance from the sun; with no sun nothing is drawn.
            float Scratches(float2 pix, float2 sunPix)
            {
                float scale = _ScreenParams.y / 1080;
                float cell = 140 * scale;
                float2 base = floor(pix / cell);
                float reach = _ScreenParams.y * .6;
                float sum = 0;
                [unroll] for (int j = -1; j <= 1; j++)
                {
                    [unroll] for (int i = -1; i <= 1; i++)
                    {
                        float2 id = base + float2(i, j);
                        if (Hash(id) < .36)
                        {
                            float2 centre = (id + float2(Hash(id + 1.7), Hash(id + 3.1))) * cell;
                            float angle = Hash(id + 5.3) * 3.14159;
                            float2 dir = float2(cos(angle), sin(angle));
                            float half = lerp(14, 64, Hash(id + 7.9)) * scale;
                            float2 d = pix - centre;
                            float along = dot(d, dir);
                            float dist = length(d - dir * clamp(along, -half, half));
                            float stroke = 1 - smoothstep(.3, 1.1, dist);
                            float taper = saturate(1 - abs(along) / half);
                            float2 toLight = centre - sunPix;
                            float2 n = float2(-dir.y, dir.x);
                            float align = pow(abs(dot(n, toLight / max(length(toLight), 1))), 4);
                            float falloff = exp(-length(toLight) / reach);
                            sum += stroke * taper * align * falloff * (.35 + .65 * Hash(id + 9.1));
                        }
                    }
                }
                return saturate(sum);
            }
            float4 frag(Output i) : SV_Target
            {
                float2 uv = i.uv;
                float2 c = uv * 2 - 1;
                float radial = length(c);
                float vignette = 1 - _Curve * .38 * pow(smoothstep(.55, 1.45, radial), 2);
                // Schlick Fresnel proxy from the edge of the quad (grazing view at the periphery).
                float grazing = smoothstep(.35, 1.42, radial) * .92;
                float fres = .04 + .96 * pow(grazing, 5);
                // Dichroic coating: hue drifts with radius and angle (gold centre, teal-blue rim).
                float angle = atan2(c.y, c.x);
                float3 coat = lerp(float3(1, .82, .55), float3(.45, .75, 1), saturate(radial * .7 + .18 * sin(angle * 2 + radial * 4)));
                float4 m = tex2D(_MainTex, Warp(uv));
                float tintA = _Tint * .12;
                float4 col = float4(_TintColor.rgb * tintA, tintA);
                float fogA = saturate(m.g * _Fog * .38);
                col = col * (1 - fogA) + float4(float3(.82, .85, .88) * lerp(.25, 1, _Light), 1) * fogA;
                float browA = saturate(m.b * _Brow * .85);
                col = col * (1 - browA) + float4(0, 0, 0, 1) * browA;
                col.rgb += coat * fres * .05 * (.35 + .65 * _Light) * _Reflect;
                if (_SunLit > .01)
                {
                    float2 sunPix = _SunView.xy * _ScreenParams.xy;
                    col.rgb += float3(1, .94, .84) * Scratches(uv * _ScreenParams.xy, sunPix) * _SunLit * .55;
                }
                col.rgb *= vignette;
                col.a = saturate(col.a);
                return col;
            }
            ENDHLSL
        }
    }
}
