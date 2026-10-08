using System;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>
    /// One geostationary station-keeping satellite over the theatre (map u,v in 0..1). It sits parked at a point and covers a circular footprint around it.
    /// A burn moves it: it slews to a new parked point at a fixed speed and the fuel is spent up front, in proportion to the distance (plus a small fixed burn cost).
    /// Pure value type: the position at any mission time is a function of the burn that started it, so every client agrees with no per-frame state.
    /// </summary>
    internal readonly struct GeoBird
    {
        /// <summary>Theatre widths per second: a cross-theatre reposition (~1.0) takes about 90 s.</summary>
        public const float Slew = 0.011f;
        public const float BurnBase = 1.5f, BurnPerUnit = 36f;

        public readonly float FromU, FromV, ToU, ToV;
        /// <summary>Mission clock at which the burn started.</summary>
        public readonly float DepartAt;
        /// <summary>Fuel left after the burn that is under way (0..100).</summary>
        public readonly float Fuel;

        public GeoBird(float u, float v, float fuel) : this(u, v, u, v, 0f, fuel) { }

        public GeoBird(float fromU, float fromV, float toU, float toV, float departAt, float fuel)
        {
            FromU = fromU; FromV = fromV; ToU = toU; ToV = toV; DepartAt = departAt; Fuel = Math.Max(0f, Math.Min(100f, fuel));
        }

        public static float Distance(float u0, float v0, float u1, float v1)
        {
            float du = u1 - u0, dv = v1 - v0;
            return (float)Math.Sqrt(du * du + dv * dv);
        }

        /// <summary>Fuel percent a burn over <paramref name="distance"/> costs (0 for no move).</summary>
        public static float Cost(float distance) => distance < 0.005f ? 0f : BurnBase + BurnPerUnit * distance;

        public float Length => Distance(FromU, FromV, ToU, ToV);
        public float Duration => Length / Slew;
        public bool Moving(float now) => Length > 0f && now - DepartAt < Duration;
        public float Eta(float now) => Moving(now) ? Math.Max(0f, Duration - (now - DepartAt)) : 0f;
        /// <summary>Progress of the burn, 0..1 (1 once parked).</summary>
        public float Progress(float now) => Length <= 0f ? 1f : Math.Max(0f, Math.Min(1f, (now - DepartAt) / Duration));

        public float U(float now) => FromU + (ToU - FromU) * Progress(now);
        public float V(float now) => FromV + (ToV - FromV) * Progress(now);

        /// <summary>The fuel that the burn under way cost (shown while it runs).</summary>
        public float BurnCost => Cost(Length);

        public bool CanBurn(float now, float u, float v) => !Moving(now) && Fuel >= Cost(Distance(U(now), V(now), Clamp(u), Clamp(v))) && Cost(Distance(U(now), V(now), Clamp(u), Clamp(v))) > 0f;

        /// <summary>Starts a burn to (u,v) and returns the new state; the same state back when it is refused (already moving, no move, not enough fuel).</summary>
        public GeoBird Relocate(float now, float u, float v)
        {
            u = Clamp(u); v = Clamp(v);
            if (!CanBurn(now, u, v)) return this;
            float fu = U(now), fv = V(now);
            return new GeoBird(fu, fv, u, v, now, Fuel - Cost(Distance(fu, fv, u, v)));
        }

        /// <summary>The footprint covers (u,v): the gate for a perk or a reveal that needs a satellite over the target.</summary>
        public bool Covers(float now, float u, float v, float radius) => Distance(U(now), V(now), u, v) <= radius;

        private static float Clamp(float x) => x < 0.02f ? 0.02f : x > 0.98f ? 0.98f : x;
    }
}
