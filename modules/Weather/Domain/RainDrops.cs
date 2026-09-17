using System;
using BoscaliSummer.Core;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// Rain running on cockpit glass, as a bounded pool of droplets in pane space: (0,0) is the
    /// pane's top-left, (1,1) its bottom-right, and one pane unit is the pane's longer side.
    ///
    /// <para>Pure, deterministic and allocation-free after construction — the same seed and the
    /// same sequence of steps always produce the same pane — so the glass renderer can rebuild
    /// its normal sheet from this state and the behaviour is testable without Unity. Nothing here
    /// is networked: it is one player's windscreen, not the weather.</para>
    ///
    /// <para>Droplets nucleate at a rate the precipitation kind and intensity set, grow by
    /// coalescing with neighbours and by condensation, run under gravity, trail a tail along
    /// whatever is dragging them — a crosswind, or the airflow of a fast aeroplane — and dry off,
    /// run off the bottom or blow off the side. The pool never exceeds <see cref="MaxDrops"/>,
    /// whatever the sky does.</para>
    /// </summary>
    internal sealed class RainDrops
    {
        /// <summary>Hard pool ceiling. Drizzle fills a fraction of it, a hailstorm the whole thing.</summary>
        public const int MaxDrops = 256;

        /// <summary>Wetness field resolution; a coarse film, not a water simulation.</summary>
        public const int WetColumns = 16;
        public const int WetRows = 32;

        private const int SpawnSalt = 0x53504152;

        /// <summary>A hitch must not teleport every droplet across the pane.</summary>
        private const float MaxStep = 0.2f;

        /// <summary>Radii are in pane units and are optic, not physical: the glass is ~1 m across.</summary>
        private const float MinRadius = 0.006f;
        private const float MaxRadius = 0.06f;

        /// <summary>Below this a droplet beads and creeps instead of running.</summary>
        private const float StickRadius = 0.014f;
        private const float StickCreep = 0.25f;

        private const float MaxAge = 25f;
        private const float BottomLimit = 1.15f;
        private const float SideLimit = 0.3f;

        private const float MaxTail = 0.4f;

        /// <summary>Under this the run has stopped, so the trail behind it drains rather than grows.</summary>
        private const float TailFloor = 0.01f;
        private const float TailDecay = 0.35f;

        /// <summary>The trail stretches faster than the droplet slides, which is what reads as a streak.</summary>
        private const float TailStretch = 2.2f;

        private const float Condensation = 0.0015f;
        private const float DryRate = 0.004f;

        /// <summary>Heavy rain keeps the glass wet, so drying nearly stops at full intensity.</summary>
        private const float DryAtFullIntensity = 0.9f;

        /// <summary>Pane-space drift a full-speed blow-through adds, beside whatever the crosswind does.</summary>
        private const float BlowDrag = 1.6f;

        /// <summary>
        /// Coalescing checks each droplet against the next few only. O(n·k) instead of O(n²) is
        /// invisible at this pool size; a spatial grid is the upgrade if the pool ever grows.
        /// </summary>
        private const int CoalesceNeighbours = 6;

        private const float WetGain = 2.5f;
        private const float WetDry = 0.12f;

        private struct Drop
        {
            public float X;
            public float Y;
            public float Radius;

            /// <summary>Trail tip relative to the droplet: behind it and up-wind of it.</summary>
            public float TailX;
            public float TailY;

            public float Age;
        }

        /// <summary>What one precipitation kind is worth on the glass.</summary>
        private readonly struct KindProfile
        {
            public readonly float Rate;
            public readonly float RadiusMin;
            public readonly float RadiusMax;
            public readonly float Fall;

            public KindProfile(float rate, float radiusMin, float radiusMax, float fall)
            {
                Rate = rate;
                RadiusMin = radiusMin;
                RadiusMax = radiusMax;
                Fall = fall;
            }
        }

        private readonly Drop[] drops = new Drop[MaxDrops];
        private readonly float[] wetness = new float[WetColumns * WetRows];
        private readonly int seed;
        private int count;
        private int step;
        private float debt;

        public RainDrops(int seed)
        {
            this.seed = seed;
        }

        /// <summary>Droplets currently on the pane. Never above <see cref="MaxDrops"/>.</summary>
        public int Count => count;

        public void Reset()
        {
            Array.Clear(drops, 0, drops.Length);
            Array.Clear(wetness, 0, wetness.Length);
            count = 0;
            step = 0;
            debt = 0f;
        }

        /// <summary>
        /// One step of the pane. <paramref name="crossWind"/> is sideways drift in pane units a
        /// second applied to a full-size droplet; <paramref name="airspeed"/> is 0..1 blow-through,
        /// where 1 is fast enough that the airflow strips the glass.
        /// </summary>
        public void Step(float deltaTime, float intensity, PrecipitationKind kind, float crossWind, float airspeed)
        {
            if (!(deltaTime > 0f)) return;

            float dt = deltaTime > MaxStep ? MaxStep : deltaTime;
            intensity = Unit(intensity);
            crossWind = Finite(crossWind);
            airspeed = Unit(airspeed);

            step++;
            DryWetness(dt);
            Nucleate(dt, intensity, kind);
            Move(dt, intensity, kind, crossWind, airspeed);
            Coalesce();
        }

        public float X(int index) => drops[index].X;

        public float Y(int index) => drops[index].Y;

        public float Radius(int index) => drops[index].Radius;

        public float TailX(int index) => drops[index].TailX;

        public float TailY(int index) => drops[index].TailY;

        public float Age(int index) => drops[index].Age;

        /// <summary>Wet film over one cell of the pane, 0..1. The renderer reads it as a sheen.</summary>
        public float Wetness(int column, int row) => wetness[row * WetColumns + column];

        // ---- Simulation ----------------------------------------------------------------------

        private void Nucleate(float dt, float intensity, PrecipitationKind kind)
        {
            debt += Profile(kind).Rate * intensity * dt;
            while (debt >= 1f)
            {
                if (count >= MaxDrops)
                {
                    // The pane is full: no backlog is carried into the next step, so the rate
                    // resumes honestly the moment a droplet leaves.
                    debt = 0f;
                    return;
                }
                debt -= 1f;
                Spawn(kind);
            }
        }

        private void Spawn(PrecipitationKind kind)
        {
            KindProfile profile = Profile(kind);
            uint x = Deterministic.Hash(seed, step, count, SpawnSalt);
            uint y = Deterministic.Hash(seed, step, count, SpawnSalt + 1);
            uint size = Deterministic.Hash(seed, step, count, SpawnSalt + 2);
            drops[count++] = new Drop
            {
                X = Deterministic.UnitFloat(x),
                Y = Deterministic.UnitFloat(y),
                Radius = profile.RadiusMin +
                         (profile.RadiusMax - profile.RadiusMin) * Deterministic.UnitFloat(size)
            };
        }

        private void Move(float dt, float intensity, PrecipitationKind kind, float crossWind, float airspeed)
        {
            float drag = crossWind + airspeed * airspeed * BlowDrag;
            float fall = Profile(kind).Fall;
            float dry = DryRate * (1f - intensity * DryAtFullIntensity);
            float grow = Condensation * intensity;

            for (int i = count - 1; i >= 0; i--)
            {
                Drop drop = drops[i];
                float size = SizeFactor(drop.Radius);
                float vx = drag * size;
                float vy = fall * size * size;
                if (drop.Radius < StickRadius) vy *= StickCreep;

                drop.X += vx * dt;
                drop.Y += vy * dt;

                float speed = vx * vx + vy * vy;
                if (speed > TailFloor * TailFloor)
                {
                    drop.TailX -= vx * dt * TailStretch;
                    drop.TailY -= vy * dt * TailStretch;
                    LimitTail(ref drop);
                }
                else
                {
                    drop.TailX -= drop.TailX * TailDecay * dt;
                    drop.TailY -= drop.TailY * TailDecay * dt;
                }

                MarkWet(drop.X, drop.Y, dt);
                drop.Radius += (grow - dry) * dt;
                drop.Age += dt;

                drops[i] = drop;
                if (drop.Radius >= MinRadius && drop.Y <= BottomLimit &&
                    drop.X >= -SideLimit && drop.X <= 1f + SideLimit && drop.Age <= MaxAge)
                {
                    continue;
                }
                drops[i] = drops[count - 1];
                count--;
            }
        }

        private void Coalesce()
        {
            for (int i = 0; i < count; i++)
            {
                for (int k = 1; k <= CoalesceNeighbours && i + k < count; k++)
                {
                    int j = i + k;
                    float dx = drops[j].X - drops[i].X;
                    float dy = drops[j].Y - drops[i].Y;
                    float reach = drops[i].Radius + drops[j].Radius;
                    if (dx * dx + dy * dy > reach * reach) continue;

                    float merged = drops[i].Radius * drops[i].Radius + drops[j].Radius * drops[j].Radius;
                    drops[i].Radius = Math.Min((float)Math.Sqrt(merged), MaxRadius);
                    drops[j] = drops[count - 1];
                    count--;
                    k--;
                }
            }
        }

        private void MarkWet(float x, float y, float dt)
        {
            int column = (int)(x * WetColumns);
            int row = (int)(y * WetRows);
            if (column < 0 || column >= WetColumns || row < 0 || row >= WetRows) return;
            int index = row * WetColumns + column;
            float value = wetness[index] + WetGain * dt;
            wetness[index] = value > 1f ? 1f : value;
        }

        private void DryWetness(float dt)
        {
            float dry = WetDry * dt;
            for (int i = 0; i < wetness.Length; i++)
            {
                float value = wetness[i] - dry;
                wetness[i] = value > 0f ? value : 0f;
            }
        }

        private static void LimitTail(ref Drop drop)
        {
            float length = (float)Math.Sqrt(drop.TailX * drop.TailX + drop.TailY * drop.TailY);
            if (length <= MaxTail) return;
            float scale = MaxTail / length;
            drop.TailX *= scale;
            drop.TailY *= scale;
        }

        /// <summary>Radius as a fraction of the largest droplet any kind makes, 0..1.</summary>
        private static float SizeFactor(float radius)
        {
            float size = radius / 0.045f;
            return size > 1f ? 1f : size;
        }

        private static KindProfile Profile(PrecipitationKind kind)
        {
            switch (kind)
            {
                case PrecipitationKind.Drizzle: return new KindProfile(4f, 0.010f, 0.020f, 0.06f);
                case PrecipitationKind.Rain: return new KindProfile(20f, 0.014f, 0.030f, 0.12f);
                case PrecipitationKind.Showers: return new KindProfile(40f, 0.018f, 0.038f, 0.18f);
                case PrecipitationKind.Hail: return new KindProfile(52f, 0.020f, 0.045f, 0.26f);
                default: return new KindProfile(0f, 0.010f, 0.020f, 0.12f);
            }
        }

        /// <summary>NaN-safe 0..1 clamp: a broken reading must never poison the pool.</summary>
        private static float Unit(float value)
        {
            if (float.IsNaN(value)) return 0f;
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        private static float Finite(float value) => float.IsNaN(value) ? 0f : value;
    }
}
