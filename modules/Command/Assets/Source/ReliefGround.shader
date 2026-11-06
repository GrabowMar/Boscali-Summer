// The relief map's ground (MfdTerrainRelief, 2026-10-29): the land-cover style texture times the baked hillshade (the old URP
// particle-unlit look), plus, all from the height the vertex already carries, AA contour lines (minor every _ContourStep m,
// a lighter index line every fifth), a slight lift with altitude, water coloured by depth and by distance to the coast
// (TEXCOORD1.x, in heightfield samples) with a fine coastline, and a distance haze in tilted views (_HazeRange set per draw).
// Contours are etched (darker), every fifth one deeper.
// One pass, one texture read, no lighting, no animation: the relief redraws only when its view moves or once a second.
// Plain CG (no pipeline includes): drawn by a command buffer, or through the URP camera as its default unlit pass.
Shader "Boscali/ReliefGround"
{
    Properties
    {
        _MainTex ("Style", 2D) = "white" {}
        _Color ("Gain", Color) = (1, 1, 1, 1)
        _MetersPerUnit ("Metres per model unit", Float) = 25
        _SeaLevel ("Heightfield offset (m)", Float) = -200
        _PeakMeters ("Highest point (m)", Float) = 2000
        _ContourStep ("Contour step (m)", Float) = 100
        _ContourInk ("Minor contour darkening", Range(0, 1)) = 0.12
        _IndexInk ("Index contour darkening", Range(0, 1)) = 0.24
        _Deep ("Deep water", Color) = (0.05, 0.10, 0.16, 1)
        _Shallow ("Shallow water", Color) = (0.13, 0.31, 0.36, 1)
        _Coast ("Coastline", Color) = (0.62, 0.80, 0.84, 1)
        _ShallowMeters ("Shallow depth (m)", Float) = 45
        _CoastCells ("Shallow reach (samples)", Float) = 4
        _Haze ("Haze", Color) = (0.07, 0.11, 0.14, 1)
        _HazeRange ("Haze start, end (view depth), amount", Vector) = (0, 1, 0, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color, _Deep, _Shallow, _Coast, _Haze;
            float _MetersPerUnit, _SeaLevel, _PeakMeters, _ContourStep, _ContourInk, _IndexInk, _ShallowMeters, _CoastCells;
            float4 _HazeRange;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 coast : TEXCOORD1;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 data : TEXCOORD1;   // height above the sea (m), coast distance (samples), view depth
                fixed shade : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.data = float3(v.vertex.y * _MetersPerUnit + _SeaLevel, v.coast.x, -UnityObjectToViewPos(v.vertex).z);
                o.shade = v.color.r;
                return o;
            }

            // A one-pixel line where x crosses a whole number; it fades out before lines crowd closer than a few pixels.
            float Contour(float x)
            {
                float w = max(fwidth(x), 1e-4);   // a perfectly flat patch: no 0/0 that would etch it whole
                float d = abs(frac(x + 0.5) - 0.5);
                return (1.0 - smoothstep(w * 0.5, w * 1.5, d)) * saturate(1.6 - w * 5.0);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float h = i.data.x;
                // Water below half a metre: land encodes at 1 m or more, and a version-2 heightfield (Archipelago) keeps its
                // whole sea flat at exactly 0 m, which h < 0 painted as land with a coastline everywhere (2026-11-05).
                float s = h - 0.5;
                fixed3 col = tex2D(_MainTex, i.uv).rgb * i.shade * _Color.rgb;
                if (s < 0.0)
                {
                    // Real depth or a near shore makes the shallows; a sea with no depth is deep away from the coast.
                    float shallow = max(h < 0.0 ? saturate(1.0 + h / _ShallowMeters) : 0.0, saturate(1.0 - i.data.y / _CoastCells));
                    col = lerp(_Deep.rgb, _Shallow.rgb, shallow) * lerp(0.75, 1.2, i.shade);
                }
                else
                {
                    col *= lerp(0.94, 1.1, saturate(h / _PeakMeters));
                    // Etched, not drawn: pale lines read as roads and grid (2026-10-29 captures).
                    float minor = Contour(h / _ContourStep);
                    float index = Contour(h / (_ContourStep * 5.0));
                    col *= 1.0 - max(_ContourInk * minor, _IndexInk * index);
                }
                float coast = 1.0 - smoothstep(0.0, fwidth(h) + 0.001, abs(s));
                col = lerp(col, _Coast.rgb, coast * 0.28);
                float haze = saturate((i.data.z - _HazeRange.x) / max(_HazeRange.y - _HazeRange.x, 0.001)) * _HazeRange.z;
                col = lerp(col, _Haze.rgb, haze);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
