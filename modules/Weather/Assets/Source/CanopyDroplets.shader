// Boscali canopy droplet simulation: one persistent water layer per glass pane.
// R = bead water, G = runner trail memory. A fullscreen blit advances the state:
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
            float _Rain, _SpeedN, _Dt, _SimTime;

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
                float mob = smoothstep(0.12, 0.55, prev.r + _SpeedN * 0.25);
                float water = lerp(prev.r, traced.r, mob);

                // Spawn once per cell per 6 Hz epoch. The prior gate stayed open
                // for several render frames and painted the same bead repeatedly.
                float2 grid = i.uv * 24.0;
                float2 cell = floor(grid);
                float clock = _SimTime * 6.0 + hash12(cell + 9.2);
                float tick = floor(clock);
                float seed = tick * 17.0;
                float gate = step(frac(clock), dt * 6.0)
                    * step(hash12(cell + seed), _Rain * 0.12);
                float2 center = float2(hash12(cell + seed + 3.1), hash12(cell + seed + 7.7)) - 0.5;
                float d = length(frac(grid) - 0.5 - center * 0.5);
                float sizeMul = 0.7 + 0.6 * hash12(cell + seed + 13.7);
                // Paraboloid profile: a flat-topped bump renders as a ring with a dead
                // centre; a dome keeps a slope everywhere so beads read as lenses.
                float r = saturate(d / (0.34 * sizeMul));
                water += gate * (1.0 - r * r) * 0.8;

                // Dry air and slipstream evaporate the film.
                water *= exp(-dt * (0.10 + 0.8 * (1.0 - _Rain) + 1.5 * _SpeedN));

                // Trails mark where running water has been.
                float trail = max(traced.g * exp(-dt * 1.2), traced.r * 0.22 * mob);

                return float4(saturate(water), saturate(trail), 0.0, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
