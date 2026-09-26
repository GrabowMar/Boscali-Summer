using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>One water drop on the canopy, in the canopy's unrolled surface frame (metres).</summary>
    internal struct Drop
    {
        /// <summary>Across the canopy (around the fuselage axis), 0 at the top, +right.</summary>
        public float X;

        /// <summary>Along the canopy, 0 at the front of the windscreen, + toward the rear.</summary>
        public float Y;

        public float VX;
        public float VY;

        /// <summary>Radius in metres (0.15–4 mm).</summary>
        public float Radius;

        /// <summary>Distance slid since the last trail bead was left.</summary>
        public float Travel;

        public bool Moving;
    }

    /// <summary>Everything the simulation needs to know about the airframe this frame.</summary>
    internal struct CanopyForces
    {
        /// <summary>True airspeed, m/s.</summary>
        public float Airspeed;

        /// <summary>Apparent gravity (gravity minus the airframe's acceleration) in the airframe's
        /// frame, m/s²: right, up, forward.</summary>
        public float GravityRight;
        public float GravityUp;
        public float GravityForward;

        /// <summary>Rain rate at the aircraft, mm/h.</summary>
        public float RainRate;

        /// <summary>0..1 how deep in cloud the aircraft is (adds mist beads).</summary>
        public float CloudDepth;
    }

    /// <summary>
    /// Rain on the canopy, simulated as a bounded pool of drops on the unrolled glass.
    ///
    /// <para>Physics that gives the look: contact-line pinning holds a drop with a force ∝ its
    /// perimeter, so the sticking acceleration goes as 1/r² while gravity is size-independent and
    /// airflow drag (∝ r² over mass ∝ r³) goes as V²/r. Big drops therefore slide first under
    /// gravity, the airflow starts moving everything at roughly 30–45 m/s, and in fast flight the
    /// glass sheds water within a fraction of a second. Sliding drops leave trail beads, touching
    /// drops merge (volume conserved), and drops leaving the glass are gone.</para>
    ///
    /// <para>The surface is modelled as a canopy whose slope runs from a steep windscreen at the
    /// front (drops there fight gravity against the airflow) to a flat top and a rear that falls
    /// away. Visual only: never networked, seeded so tests are repeatable.</para>
    /// </summary>
    internal sealed class DropSim
    {
        public const int MaxDropsCeiling = 1024;

        /// <summary>Sticking: a_stick = StickK / r² (m/s²).</summary>
        public const float StickK = 1.1e-5f;

        /// <summary>Air drag: a_air = AirK · V² / r (m/s²).</summary>
        public const float AirK = 1.2e-5f;

        /// <summary>Sliding damping (1/s): terminal slide speed is a / Damping.</summary>
        public const float Damping = 20f;

        public const float MinRadius = 0.00015f;
        public const float MaxRadius = 0.004f;
        public const float BeadSpacing = 0.015f;

        private const float WindscreenSlopeDeg = 40f;
        private const float RearSlopeDeg = -20f;
        private const int GridSize = 24;

        private readonly Drop[] drops;
        private readonly int[] gridHead;
        private readonly int[] gridNext;
        private uint rng;
        private float spawnCarry;

        public DropSim(int capacity, float width, float length, uint seed = 12345u)
        {
            Capacity = Math.Max(16, Math.Min(capacity, MaxDropsCeiling));
            drops = new Drop[Capacity];
            gridHead = new int[GridSize * GridSize];
            gridNext = new int[Capacity];
            Width = Math.Max(width, 0.2f);
            Length = Math.Max(length, 0.2f);
            rng = seed | 1u;
        }

        public int Capacity { get; }
        public int Count { get; private set; }

        /// <summary>Unrolled glass size in metres: across (arc length) and along.</summary>
        public float Width { get; }
        public float Length { get; }

        /// <summary>Drops that hit the glass during the last step (drives the impact sound).</summary>
        public int ImpactsLastStep { get; private set; }

        /// <summary>0..1 film of water on the glass, rising with rain and falling as it clears.</summary>
        public float Wetness { get; private set; }

        public Drop Get(int index) => drops[index];

        /// <summary>Puts a drop on the glass directly (tests, and water left from a previous airframe).</summary>
        public void Place(Drop drop) => Add(drop);

        public void Clear()
        {
            Count = 0;
            Wetness = 0f;
            spawnCarry = 0f;
        }

        /// <summary>Slope of the glass along its length at <paramref name="y"/>, degrees (rising rearward is positive).</summary>
        public float SlopeAt(float y)
        {
            float s = WeatherMath.Clamp01(y / Length);
            return WeatherMath.Lerp(WindscreenSlopeDeg, RearSlopeDeg, WeatherMath.Smoothstep(0.1f, 1f, s));
        }

        /// <summary>Surface accelerations (across, along) for a drop at (x, y), before sticking.</summary>
        public void Accelerations(in CanopyForces f, float x, float y, float radius, out float ax, out float ay)
        {
            // Across: the angle around the fuselage axis; the tangent goes right and down.
            float theta = x / (Width * 0.5f) * 1.2f;
            float ct = (float)Math.Cos(theta), st = (float)Math.Sin(theta);
            float gAcross = f.GravityRight * ct - f.GravityUp * st;

            // Along: toward the rear, climbing the windscreen then over the top.
            float slope = SlopeAt(y) * WeatherMath.Deg2Rad;
            float gAlong = f.GravityUp * (float)Math.Sin(slope) - f.GravityForward * (float)Math.Cos(slope);

            float air = f.Airspeed > 1f ? AirK * f.Airspeed * f.Airspeed / Math.Max(radius, MinRadius) : 0f;
            ax = gAcross;
            ay = gAlong + air;
        }

        public void Step(in CanopyForces forces, float dt)
        {
            if (dt <= 0f) return;
            dt = Math.Min(dt, 0.05f);

            Spawn(forces, dt);
            Move(forces, dt);
            Merge();
            Evaporate(forces, dt);

            float rainTarget = WeatherMath.Smoothstep(0f, 20f, forces.RainRate) * (1f - WeatherMath.Smoothstep(60f, 200f, forces.Airspeed));
            float rate = rainTarget > Wetness ? 0.5f : 0.08f;
            Wetness += (rainTarget - Wetness) * Math.Min(1f, rate * dt);
        }

        private void Spawn(in CanopyForces f, float dt)
        {
            ImpactsLastStep = 0;
            float rain = Math.Max(f.RainRate, 0f);
            float speedFactor = 1f + Math.Max(f.Airspeed, 0f) / 25f;
            float area = Width * Length;
            float rate = 8f * (float)Math.Pow(rain, 0.8) * speedFactor * area;
            rate += 40f * WeatherMath.Clamp01(f.CloudDepth) * WeatherMath.Smoothstep(10f, 60f, f.Airspeed) * area;

            spawnCarry += rate * dt;
            int n = (int)spawnCarry;
            spawnCarry -= n;
            n = Math.Min(n, 64);

            float frontBias = WeatherMath.Smoothstep(20f, 120f, f.Airspeed);
            for (int i = 0; i < n; i++)
            {
                float mist = f.CloudDepth > 0f && Next() < f.CloudDepth * 0.5f ? 1f : 0f;
                float r = mist > 0f
                    ? WeatherMath.Lerp(0.00015f, 0.0003f, Next())
                    : WeatherMath.Lerp(0.0003f, 0.0013f + 0.0003f * WeatherMath.Smoothstep(10f, 50f, rain), Next() * Next());
                float x = (Next() - 0.5f) * Width;
                // In flight the windscreen catches most of the water.
                float y = (float)Math.Pow(Next(), 1f + 2f * frontBias) * Length;

                var drop = new Drop { X = x, Y = y, Radius = r };
                if (f.Airspeed > 120f)
                {
                    // Impacts at speed smear aft immediately.
                    drop.VY = f.Airspeed * 0.05f;
                    drop.Moving = true;
                }
                Add(drop);
                ImpactsLastStep++;
            }
        }

        private void Move(in CanopyForces f, float dt)
        {
            int beadsToAdd = 0;
            for (int i = 0; i < Count; i++)
            {
                ref Drop d = ref drops[i];
                Accelerations(f, d.X, d.Y, d.Radius, out float ax, out float ay);
                float net = (float)Math.Sqrt(ax * ax + ay * ay);
                float stick = StickK / (d.Radius * d.Radius);

                if (!d.Moving && net > stick) d.Moving = true;
                if (d.Moving)
                {
                    // Once sliding, the drop keeps going while the drive exceeds half the pinning.
                    if (net < stick * 0.5f && d.VX * d.VX + d.VY * d.VY < 0.0004f)
                    {
                        d.Moving = false;
                        d.VX = 0f;
                        d.VY = 0f;
                        continue;
                    }
                    float fx = ax * (1f - stick * 0.5f / Math.Max(net, 1e-4f));
                    float fy = ay * (1f - stick * 0.5f / Math.Max(net, 1e-4f));
                    d.VX += (fx - Damping * d.VX) * dt;
                    d.VY += (fy - Damping * d.VY) * dt;
                    float sx = d.VX * dt, sy = d.VY * dt;
                    d.X += sx;
                    d.Y += sy;
                    d.Travel += (float)Math.Sqrt(sx * sx + sy * sy);

                    if (d.Travel > BeadSpacing && d.Radius > 0.0006f)
                    {
                        d.Travel = 0f;
                        beadsToAdd++;
                        float bead = d.Radius * 0.3f;
                        float remaining = d.Radius * d.Radius * d.Radius - bead * bead * bead;
                        d.Radius = (float)Math.Pow(Math.Max(remaining, 0f), 1.0 / 3.0);
                        if (Count + beadsToAdd <= Capacity)
                        {
                            AddDeferred(new Drop { X = d.X - sx * 2f, Y = d.Y - sy * 2f, Radius = bead }, beadsToAdd);
                        }
                    }
                }
            }
            Count = Math.Min(Count + beadsToAdd, Capacity);

            // Drops that left the glass are gone.
            for (int i = Count - 1; i >= 0; i--)
            {
                Drop d = drops[i];
                if (d.Y > Length || d.Y < -0.02f || Math.Abs(d.X) > Width * 0.5f || d.Radius < MinRadius) RemoveAt(i);
            }
        }

        private void Merge()
        {
            Array.Fill(gridHead, -1);
            float cellW = Width / GridSize, cellL = Length / GridSize;
            for (int i = 0; i < Count; i++)
            {
                int c = CellOf(drops[i].X, drops[i].Y, cellW, cellL);
                gridNext[i] = gridHead[c];
                gridHead[c] = i;
            }

            for (int i = 0; i < Count; i++)
            {
                ref Drop a = ref drops[i];
                if (a.Radius <= 0f) continue;
                int cx = Clamp((int)((a.X + Width * 0.5f) / cellW), 0, GridSize - 1);
                int cy = Clamp((int)(a.Y / cellL), 0, GridSize - 1);
                for (int oy = -1; oy <= 1; oy++)
                {
                    int y = cy + oy;
                    if (y < 0 || y >= GridSize) continue;
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int x = cx + ox;
                        if (x < 0 || x >= GridSize) continue;
                        for (int j = gridHead[y * GridSize + x]; j >= 0; j = gridNext[j])
                        {
                            if (j <= i) continue;
                            ref Drop b = ref drops[j];
                            if (b.Radius <= 0f) continue;
                            float dx = a.X - b.X, dy = a.Y - b.Y;
                            float reach = (a.Radius + b.Radius) * 0.8f;
                            if (dx * dx + dy * dy > reach * reach) continue;

                            float va = a.Radius * a.Radius * a.Radius;
                            float vb = b.Radius * b.Radius * b.Radius;
                            float v = va + vb;
                            a.X = (a.X * va + b.X * vb) / v;
                            a.Y = (a.Y * va + b.Y * vb) / v;
                            a.VX = (a.VX * va + b.VX * vb) / v;
                            a.VY = (a.VY * va + b.VY * vb) / v;
                            a.Radius = Math.Min((float)Math.Pow(v, 1.0 / 3.0), MaxRadius);
                            a.Moving |= b.Moving;
                            b.Radius = 0f;
                        }
                    }
                }
            }

            for (int i = Count - 1; i >= 0; i--)
            {
                if (drops[i].Radius <= 0f) RemoveAt(i);
            }
        }

        private void Evaporate(in CanopyForces f, float dt)
        {
            // Slow drying out of rain; fast shedding of the smallest beads in fast dry air.
            float dry = f.RainRate > 0.1f ? 0.0000005f : 0.000002f;
            float blow = WeatherMath.Smoothstep(150f, 300f, f.Airspeed) * 0.00002f;
            for (int i = Count - 1; i >= 0; i--)
            {
                ref Drop d = ref drops[i];
                d.Radius -= (dry + blow) * dt;
                if (d.Radius < MinRadius) RemoveAt(i);
            }
        }

        private void Add(in Drop drop)
        {
            if (Count < Capacity)
            {
                drops[Count++] = drop;
                return;
            }
            // Full: the new water joins a random existing drop instead of being lost.
            int target = (int)(Next() * Count) % Count;
            ref Drop d = ref drops[target];
            float v = d.Radius * d.Radius * d.Radius + drop.Radius * drop.Radius * drop.Radius;
            d.Radius = Math.Min((float)Math.Pow(v, 1.0 / 3.0), MaxRadius);
        }

        private void AddDeferred(in Drop drop, int offset)
        {
            int index = Count + offset - 1;
            if (index < Capacity) drops[index] = drop;
        }

        private void RemoveAt(int index)
        {
            Count--;
            if (index < Count) drops[index] = drops[Count];
        }

        private int CellOf(float x, float y, float cellW, float cellL)
        {
            int cx = Clamp((int)((x + Width * 0.5f) / cellW), 0, GridSize - 1);
            int cy = Clamp((int)(y / cellL), 0, GridSize - 1);
            return cy * GridSize + cx;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

        private float Next()
        {
            unchecked
            {
                rng ^= rng << 13;
                rng ^= rng >> 17;
                rng ^= rng << 5;
                return (rng & 0x00ffffffu) / 16777216f;
            }
        }
    }
}
