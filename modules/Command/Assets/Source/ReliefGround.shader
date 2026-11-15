// The relief map's ground (MfdTerrainRelief, 2026-10-29): the land-cover style texture times the baked hillshade (the old URP
// particle-unlit look), plus, all from the height the vertex already carries, AA contour lines (minor every _ContourStep m,
// a lighter index line every fifth), a slight lift with altitude, water coloured by depth and by distance to the coast
// (TEXCOORD1.x, in heightfield samples) with a fine coastline, and a distance haze in tilted views (_HazeRange set per draw).
// Contours are etched (darker), every fifth one deeper.
// One pass, one texture read on land (none at sea), baked tonal shading, no animation or real-time lighting.
// Plain CG (no pipeline includes): drawn by a command buffer, or through the URP camera as its default unlit pass.
Shader "Boscali/ReliefGround"
{
    Properties
    {
        _MainTex ("Style", 2D) = "white" {}
        _Color ("Gain", Color) = (1, 1, 1, 1)
        _ShadowTint ("Cool shadow", Color) = (0.88, 0.94, 1.02, 1)
        _SunTint ("Warm highlight", Color) = (1.08, 1.04, 0.93, 1)
        _ToneStrength ("Tonal shading", Range(0, 1)) = 0.4
        _RidgeTint ("Ridge highlight", Color) = (0.90, 1.0, 0.96, 1)
        _RidgeStrength ("Ridge highlight strength", Range(0, 0.15)) = 0.055
        _MetersPerUnit ("Metres per model unit", Float) = 25
        _SeaLevel ("Heightfield offset (m)", Float) = -200
        _PeakMeters ("Highest point (m)", Float) = 2000
        _ContourStep ("Contour step (m)", Float) = 100
        _ContourInk ("Minor contour darkening", Range(0, 1)) = 0.09
        _IndexInk ("Index contour darkening", Range(0, 1)) = 0.18
        _Deep ("Deep water", Color) = (0.055, 0.115, 0.15, 1)
        _Shallow ("Shallow water", Color) = (0.13, 0.28, 0.30, 1)
        _Coast ("Coastline", Color) = (0.42, 0.62, 0.65, 1)
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
            fixed4 _ShadowTint, _SunTint, _RidgeTint;
            float _ToneStrength, _RidgeStrength;
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
                float3 data : TEXCOORD1;   // height above the sea (m), coast distance (samples), unbounded haze coordinate
                fixed4 shade : COLOR;     // tonal land shade (rgb), original grey for water (a)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                float viewDepth = -UnityObjectToViewPos(v.vertex).z;
                // This coordinate is affine: normalize at vertices, but clamp after interpolation to keep the haze boundary exact.
                float haze = (viewDepth - _HazeRange.x) / max(_HazeRange.y - _HazeRange.x, 0.001);
                o.data = float3(v.vertex.y * _MetersPerUnit + _SeaLevel, v.coast.x, haze);
                // Reuse the baked light: warm sun-facing slopes, cooler valleys. This stays per vertex and leaves water unchanged.
                float facing = saturate((v.color.r - 0.42) / 0.53);
                fixed3 tint = lerp(_ShadowTint.rgb, _SunTint.rgb, facing);
                o.shade = fixed4(v.color.r * lerp(fixed3(1, 1, 1), tint, _ToneStrength), v.color.r);
                // Curvature is baked in uv2.y: accent only lit crests, with no extra sample or water tint.
                float crest = saturate(v.coast.y) * _RidgeStrength * saturate((v.color.r - 0.58) / 0.32);
                o.shade.rgb += min(_RidgeTint.rgb * crest, max(fixed3(0, 0, 0), 1.0 - o.shade.rgb));
                return o;
            }

            // A 1.3-pixel line where x crosses a whole number, filtered over the pixel footprint.
            float Contour(float x, float width)
            {
                float w = max(width, 1e-4);
                float d = abs(frac(x + 0.5) - 0.5);
                // The linear coverage keeps total ink stable through fractional-pixel pans. Fade crowded lines and flat plateaus.
                return saturate(1.15 - d / w) * saturate(1.6 - w * 5.0) * saturate(width * 1e4);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float h = i.data.x;
                // Water below half a metre: land encodes at 1 m or more, and a version-2 heightfield (Archipelago) keeps its
                // whole sea flat at exactly 0 m, which h < 0 painted as land with a coastline everywhere (2026-11-05).
                float s = h - 0.5;
                // Gradients must be taken before the land/water branch. Reuse the height gradient for both contour levels and
                // the coast; explicit UV gradients let coherent sea pixels skip the style texture without mip seams at shore.
                float heightWidth = fwidth(h);
                float2 uvDx = ddx(i.uv), uvDy = ddy(i.uv);
                fixed3 col;
                UNITY_BRANCH
                if (s < 0.0)
                {
                    // Real depth or a near shore makes the shallows; a sea with no depth is deep away from the coast.
                    float shallow = max(h < 0.0 ? saturate(1.0 + h / _ShallowMeters) : 0.0, saturate(1.0 - i.data.y / _CoastCells));
                    col = lerp(_Deep.rgb, _Shallow.rgb, shallow) * lerp(0.75, 1.2, i.shade.a);
                }
                else
                {
                    col = tex2Dgrad(_MainTex, i.uv, uvDx, uvDy).rgb * i.shade.rgb * _Color.rgb;
                    col *= lerp(0.99, 1.08, saturate(h / _PeakMeters));
                    // Etched, not drawn: pale lines read as roads and grid (2026-10-29 captures).
                    float contourHeight = h / _ContourStep;
                    float contourWidth = heightWidth / _ContourStep;
                    float minor = Contour(contourHeight, contourWidth);
                    float index = Contour(contourHeight * 0.2, contourWidth * 0.2);
                    col *= 1.0 - max(_ContourInk * minor, _IndexInk * index);
                }
                float coast = 1.0 - smoothstep(0.0, heightWidth + 0.001, abs(s));
                col = lerp(col, _Coast.rgb, coast * 0.18);
                float haze = saturate(i.data.z) * _HazeRange.z;
                col = lerp(col, _Haze.rgb, haze);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
