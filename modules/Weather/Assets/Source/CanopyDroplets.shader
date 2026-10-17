// Boscali canopy droplet simulation: one persistent water layer per glass pane.
// R = bead water, G = runner trail memory, B = fine cloud moisture film.
// A fullscreen blit advances the state:
// previously deposited water advects with the pane-projected flow (semi-Lagrangian
// backtrace), big beads run while small ones stick (librain-style static drag),
// fresh drops spawn with the rain rate, and dry air plus slipstream evaporate.
// Motion is integrated state, so panes can never pop or disagree frame to frame.
Shader "Hidden/BoscaliCanopyDroplets"
{
    Properties
    {
        _MainTex ("Prev State", 2D) = "black" {}
        _FlowTiles ("Flow, tiles/s", Vector) = (0, -0.25, 0, 0)
        _Rain ("Rain Rate", Range(0, 1)) = 0
        _SpeedN ("Airspeed Norm", Range(0, 1)) = 0
        _Condensation ("Cloud Moisture", Range(0, 1)) = 0
        _Evaporation ("Air Drying", Range(0.2, 1.4)) = 1
        _Dt ("Step, s", Float) = 0.016
        _SimTime ("Sim Time, s", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _FlowTiles;
            float _Rain, _SpeedN, _Dt, _SimTime, _Condensation, _Evaporation;

            struct Appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Interpolated
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Interpolated vert(Appdata v)
            {
                Interpolated o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Birth(float2 grid, float2 cell, float2 dir, float smear, float dt)
            {
                // Cells only find nearby events. Births use the full cell and their
                // footprints cross its edges; size must never pull centres onto a lattice.
                float2 site = cell - floor(cell / 24.0) * 24.0;
                float clock = _SimTime * 6.0 + hash12(site + 9.2);
                float seed = floor(clock) * 17.0;
                float gate = step(frac(clock), dt * 6.0)
                    * step(hash12(site + seed), _Rain * 0.24 * (0.2 + 0.35 * _Rain))
                    * step(0.0001, _Rain);
                [branch] if (gate < 0.5) return 0.0;

                float pick = hash12(site + seed + 13.7);
                float sizeMul = 0.35 + 0.65 * pick * pick;
                float rad = 0.30 * sizeMul;
                float2 center = cell + float2(hash12(site + seed + 3.1), hash12(site + seed + 7.7));
                float2 off = grid - center;
                off -= dir * dot(off, dir) * (1.0 - 1.0 / smear);
                float r = length(off) / rad;
                [branch] if (r >= 1.0) return 0.0;
                // Periodic site IDs and texture wrap preserve whole drops at the state seam.
                float free = step(tex2Dlod(_MainTex, float4(center / 24.0, 0.0, 0.0)).r, 0.06);
                return free * (1.0 - r * r) * (0.22 + 0.58 * sizeMul);
            }

            float4 frag(Interpolated i) : SV_Target
            {
                float dt = clamp(_Dt, 0.0, 0.05);
                // Capped visual advection: physical slipstream rates would white-out
                // the layer in a frame. 1.5 tiles/s reads as fast runoff.
                float2 flow = _FlowTiles.xy;
                float speed = length(flow);
                if (speed > 1.5) flow *= 1.5 / speed;

                float4 prev = tex2D(_MainTex, i.uv);
                // A fixed pane texture has no geometry readback. Tiny, stationary
                // variations in the glass path keep runners from forming ruler lines.
                float bend = (hash12(floor(i.uv * 48.0)) - 0.5) * 0.12;
                float2 back = i.uv - (flow + float2(-flow.y, flow.x) * bend) * dt;
                float4 traced = tex2D(_MainTex, back);

                // Mobility: big beads run even parked, small ones stick until the
                // slipstream rips them loose.
                // Judged on the taller of the water here and the water arriving: judged
                // on this texel alone, a bead's centre drained into dry glass and was
                // lost, leaving a hollow ring.
                float mob = smoothstep(0.45, 0.8, max(prev.r, traced.r) + _SpeedN * 0.5);
                float water = lerp(prev.r, traced.r, mob);

                // Full jitter needs neighbouring events: radius * smear <= 0.48 cells,
                // so the nearest 2x2 cells contain every footprint touching this texel.
                // Existing water stays in place when the next random arrival is chosen.
                float2 grid = i.uv * 24.0;
                float2 cell = floor(grid - 0.5);
                float smear = 1.0 + 0.6 * _SpeedN;
                float2 dir = speed > 0.001 ? _FlowTiles.xy / speed : float2(0.0, -1.0);
                [unroll] for (int y = 0; y < 2; y++)
                    [unroll] for (int x = 0; x < 2; x++)
                        water += Birth(grid, cell + float2(x, y), dir, smear, dt);
                // Standing beads swell under rain until they are heavy enough to run.
                // Dithered: the 8-bit state would round a per-step swell away.
                water += step(hash12(i.uv * 977.0 + _SimTime), _Rain * dt * 15.0) * 0.004
                    * step(0.15, water);

                // Humid air holds residual water after a shower; warm, dry air and
                // slipstream clear it. Rainfall controls arrivals, never cloud fogging.
                float drying = 0.06 + 0.32 * _Evaporation * (1.0 - _Rain) + 1.5 * _SpeedN;
                water *= exp(-dt * drying);

                // Trails mark where running water has been.
                float trail = max(traced.g * exp(-dt * (0.4 + _Evaporation + _SpeedN)), traced.r * 0.22 * mob);
                trail = floor(saturate(trail) * 255.0 + hash12(i.uv * 457.0 + _SimTime)) / 255.0;

                // Microscopic cloud droplets form a thin optical film without seeding
                // large rain beads. Airflow strips it promptly on leaving cloud.
                float film = prev.b + dt * (_Condensation * 0.18 * (1.0 - prev.b)
                    - prev.b * (0.035 + 0.13 * _Evaporation + 0.45 * _SpeedN));
                // Stochastic rounding keeps sub-byte evaporation alive in the 8-bit
                // state; otherwise a faint wet layer can survive forever in dry air.
                float2 moisture = saturate(float2(water, film));
                moisture = floor(moisture * 255.0 + float2(
                    hash12(i.uv * 977.0 + _SimTime), hash12(i.uv * 631.0 - _SimTime))) / 255.0;

                return float4(moisture.x, saturate(trail), moisture.y, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
