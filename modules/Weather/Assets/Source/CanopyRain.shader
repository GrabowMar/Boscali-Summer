// Original Boscali glass-only rain layer. Native glass is drawn unchanged underneath.
// Object-space projection works without authored UVs. Drops live in a flow-aligned
// glass frame: runners slide along the airflow leaving trails, beads sit and evaporate.
// The combined drop normal refracts the base camera's opaque texture when _Refract is 1,
// otherwise the drops carry a fog-coloured body so the look degrades gracefully.
Shader "Boscali/CanopyRain"
{
    Properties
    {
        _Intensity ("Glass Wetness", Range(0, 1)) = 0
        _FlowPhase ("Flow Phase", Float) = 0
        _Flow ("Local Airflow", Vector) = (0,-1,0,0)
        _LightLevel ("Ambient Light", Range(0,1)) = 1
        _Speed ("Airspeed Norm", Range(0,1)) = 0
        _SunDir ("Local Sun Direction", Vector) = (0,0,0,0)
        _SunColor ("Sun Color", Color) = (0,0,0,0)
        _FogColor ("Fog Color", Color) = (0.55,0.6,0.68,1)
        _Refract ("Scene Refraction", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" }
        ZWrite Off
        ZTest LEqual
        Cull Off
        Offset -1, -1
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            // Untagged passes use SRPDefaultUnlit in URP and also render in the standalone fixture.
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _CameraOpaqueTexture;
            float _Intensity, _FlowPhase, _LightLevel, _Speed, _Refract;
            float4 _Flow, _SunDir, _SunColor, _FogColor;

            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct Interpolated
            {
                float4 pos:SV_POSITION;
                float3 local:TEXCOORD0;
                float3 normal:TEXCOORD1;
                float4 screen:TEXCOORD2;
            };

            Interpolated vert(Input v)
            {
                Interpolated o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 scale = float3(length(unity_ObjectToWorld._m00_m10_m20),
                    length(unity_ObjectToWorld._m01_m11_m21), length(unity_ObjectToWorld._m02_m12_m22));
                o.local = v.vertex.xyz * scale;
                o.normal = v.normal;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float2 hash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            // Soft disc coverage with screen-space anti-aliasing; d is in drop radii.
            float Disc(float2 d)
            {
                float r = length(d);
                float aa = max(fwidth(r), 0.06);
                return (1 - smoothstep(1 - aa, 1 + aa, r)) / max(1, aa * aa);
            }

            // Runners: one drop per tall cell sliding along +y (the flow), wobbling across,
            // leaving a beaded trail behind it. Returns (coverage, normal.xy).
            float3 Runners(float2 coord, float2 cell, float t)
            {
                float aspect = cell.y / cell.x;
                float2 grid = coord / cell;
                float2 id = floor(grid);
                float2 f = frac(grid) - 0.5;
                float2 h = hash22(id + 2.7);
                float2 h2 = hash22(id + 41.3);
                float presence = step(1 - _Intensity * 0.38, h2.y);
                float rate = (0.12 + 0.18 * h.y) * (1 + 4 * _Speed);
                float y = frac(t * rate + h2.x) - 0.5;
                float wobble = 0.08 + 0.06 * (1 - _Speed);
                float px = (h.x - 0.5) * 0.45 + sin(y * 12.566 + h.x * 6.283) * wobble;
                float2 d = (f - float2(px, y)) * float2(1, aspect);
                float radius = 0.12 + 0.09 * h2.x;
                float2 shape = d / float2(radius, radius * (1 + 2 * _Speed));
                float drop = Disc(shape);

                // Trail: shrinking beads left on the path behind the drop.
                float dy = (y - f.y) * aspect;
                float trailX = (h.x - 0.5) * 0.45 + sin(f.y * 12.566 + h.x * 6.283) * wobble;
                float period = 0.34;
                float ty = frac(dy / period) - 0.5;
                float trailRadius = 0.11 * saturate(1 - dy * (0.9 + 2 * _Speed));
                float2 td = float2(f.x - trailX, ty * period) / max(trailRadius, 0.001);
                float trail = Disc(td) * smoothstep(0.15, 0.3, dy) * (1 - smoothstep(0.5, 1.0, dy));

                float cover = max(drop, trail * 0.25) * presence * (1 - smoothstep(0.35, 0.5, abs(f.y)));
                float2 n = drop > trail ? shape : td;
                return float3(cover, n * cover);
            }

            // Beads: static drops that appear, sit and evaporate; stretched by airspeed.
            float3 Beads(float2 coord, float cell, float salt, float t)
            {
                float2 grid = coord / cell;
                float2 id = floor(grid);
                float2 f = frac(grid) - 0.5;
                float2 h = hash22(id + salt);
                float2 h2 = hash22(id + salt * 1.7 + 19.1);
                float clock = t * (0.05 + 0.12 * h2.x) + h.y;
                // Reseed only while invisible between impacts; no repeating stamped pattern.
                h = hash22(id + salt + floor(clock) * 17.31);
                float presence = step(1 - _Intensity * 0.48, h2.y);
                float life = frac(clock);
                float alive = smoothstep(0, 0.12, life) * (1 - smoothstep(0.5, 1, life));
                float2 center = (h - 0.5) * 0.55;
                float radius = 0.07 + 0.12 * h.x;
                // Stay within the cell at high speed rather than clipping elongated beads flat.
                float height = min(radius * (1 + 3 * _Speed), 0.48 - abs(center.y));
                float2 shape = (f - center) / float2(radius, height);
                float cover = Disc(shape) * alive * presence;
                return float3(cover, shape * cover);
            }

            float4 frag(Interpolated i):SV_Target
            {
                clip(_Intensity - 0.001);
                float3 n3 = abs(i.normal);
                float2 uv, flow, sun;
                if (n3.z >= n3.x && n3.z >= n3.y) { uv = i.local.xy; flow = _Flow.xy; sun = _SunDir.xy; }
                else if (n3.x >= n3.y) { uv = i.local.zy; flow = _Flow.zy; sun = _SunDir.zy; }
                else { uv = i.local.xz; flow = _Flow.xz; sun = _SunDir.xz; }
                float len = length(flow);
                flow = len > 0.001 ? flow / len : float2(0, -1);
                float2 across = float2(-flow.y, flow.x);
                float2 coord = float2(dot(uv, across), dot(uv, flow));
                float2 sunc = float2(dot(sun, across), dot(sun, flow));

                float t = _FlowPhase;
                float3 a = Runners(coord, float2(0.032, 0.085), t);
                float3 b = Beads(coord, 0.017, 3.1, t);
                float3 c = Beads(coord * 1.6 + 7.3, 0.017, 9.4, t * 1.3);
                float cover = saturate(a.x + b.x * 0.8 + c.x * 0.6);
                float2 n = a.yz + b.yz * 0.8 + c.yz * 0.6;
                n /= max(cover, 0.001);
                float nl = length(n);
                float edge = smoothstep(0.45, 0.88, nl);

                // Water body: refracted scene, or a fog-coloured bead when the opaque texture is off.
                float2 suv = i.screen.xy / max(i.screen.w, 0.0001);
                float2 offset = n * 0.0025 * float2(_ScreenParams.y / _ScreenParams.x, 1);
                float3 scene = tex2D(_CameraOpaqueTexture, saturate(suv + offset)).rgb;
                float3 tint = _FogColor.rgb * _LightLevel;
                float3 body = lerp(tint, scene, _Refract);
                body *= 1 - 0.12 * edge;

                // Highlights: a soft sky lip against the flow, a sharp sun glint toward the sun.
                float2 nd = n / max(nl, 0.0001);
                float sky = pow(saturate(dot(nd, float2(0, -1))), 4) * 0.18 * _LightLevel * edge;
                float sunLen = length(sunc);
                float glint = sunLen > 0.05 ? pow(saturate(dot(nd, sunc / sunLen)), 14) : 0;
                body += _FogColor.rgb * sky + _SunColor.rgb * glint * edge * 0.45;

                // Clear centers and faint menisci, including when scene colour is unavailable.
                float alpha = cover * lerp(0.025 + edge * 0.20, 0.10 + edge * 0.28, _Refract);
                return float4(body, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
