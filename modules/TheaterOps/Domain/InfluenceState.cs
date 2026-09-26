using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.TheaterOps.Domain
{
    /// <summary>
    /// One axis weight: how the faction leans on one objective. +1 favors it as an attack
    /// target, -1 steers the director away from it, 0 (or absent) leaves it to the staff.
    /// A lean, never an order: the director still scores every objective every review.
    /// </summary>
    internal readonly struct AxisWeight
    {
        public string Key { get; }
        public float Weight { get; }

        public AxisWeight(string key, float weight)
        {
            Key = key;
            Weight = weight;
        }
    }

    /// <summary>
    /// One faction's standing orders to the theater director: the stance it fights with,
    /// whether new offensives are held, the chest it may spend from, and the axes it leans
    /// on. Pure state: every mutation clamps, bounds and reports whether anything moved,
    /// so the host broadcasts only on change. The setter is whoever the host derived from
    /// the order's origin (a passed vote, or the sender when votes are off), never a
    /// client-sent name.
    ///
    /// <para>The chest is priced in waves, not in raw millions: the default escrow opens a
    /// plan with two waves, the cap funds every wave a plan may hold, and the reserve stops
    /// at ten waves, so no single order can freeze the pool. An unset chest follows the
    /// host's costs; a set one is only re-clamped when they change.</para>
    /// </summary>
    internal sealed class InfluenceState
    {
        internal const int MaximumAxes = 4;
        internal const int MaximumSetterLength = 24;
        internal const float DefaultStance = 0.6f;

        /// <summary>The settings' own default costs; the settings bind with these.</summary>
        internal const float DefaultOverheadCost = 25f;
        internal const float DefaultWaveBudget = 45f;

        internal const int DefaultEscrowWaves = 2;
        internal const int MaximumReserveWaves = 10;

        private readonly List<AxisWeight> axes = new List<AxisWeight>(MaximumAxes);
        private bool chestSet;

        public float Stance { get; private set; } = DefaultStance;
        public bool HoldOffense { get; private set; }
        public float MaxEscrowPerPlan { get; private set; }
        public float ReserveFloor { get; private set; }
        public string Setter { get; private set; } = "";
        public IReadOnlyList<AxisWeight> Axes => axes;

        /// <summary>Escrow of an unset chest: the overhead plus two waves.</summary>
        public float DefaultMaxEscrow { get; private set; }

        /// <summary>Most one plan may escrow: the overhead plus every wave a plan may hold.</summary>
        public float EscrowCap { get; private set; }

        /// <summary>Highest reserve floor: ten waves.</summary>
        public float ReserveCap { get; private set; }

        public InfluenceState() => Price(DefaultOverheadCost, DefaultWaveBudget);

        /// <summary>
        /// Prices the chest from the offensive costs. Reports whether a figure the board
        /// shows moved, so the host re-broadcasts only when a cost change moved it.
        /// </summary>
        public bool Price(float overheadCost, float waveBudget)
        {
            float overhead = Math.Max(0f, Finite(overheadCost, DefaultOverheadCost));
            float wave = Math.Max(0f, Finite(waveBudget, DefaultWaveBudget));
            DefaultMaxEscrow = overhead + DefaultEscrowWaves * wave;
            EscrowCap = overhead + OffensivePlan.MaximumWaves * wave;
            ReserveCap = MaximumReserveWaves * wave;

            float escrow = chestSet ? Math.Min(MaxEscrowPerPlan, EscrowCap) : DefaultMaxEscrow;
            float floor = Math.Min(ReserveFloor, ReserveCap);
            if (Math.Abs(escrow - MaxEscrowPerPlan) < 0.001f && Math.Abs(floor - ReserveFloor) < 0.001f)
                return false;
            MaxEscrowPerPlan = escrow;
            ReserveFloor = floor;
            return true;
        }

        public bool SetStance(float stance, string setter)
        {
            float clamped = Clamp01(stance);
            if (Math.Abs(clamped - Stance) < 0.001f) return false;
            Stance = clamped;
            Setter = Bound(setter);
            return true;
        }

        public bool SetHold(bool hold, string setter)
        {
            if (hold == HoldOffense) return false;
            HoldOffense = hold;
            Setter = Bound(setter);
            return true;
        }

        public bool SetChest(float maxEscrow, float reserve, string setter)
        {
            ShapeChest(ref maxEscrow, ref reserve);
            if (Math.Abs(maxEscrow - MaxEscrowPerPlan) < 0.001f && Math.Abs(reserve - ReserveFloor) < 0.001f)
                return false;
            MaxEscrowPerPlan = maxEscrow;
            ReserveFloor = reserve;
            chestSet = true;
            Setter = Bound(setter);
            return true;
        }

        /// <summary>The chest an order would actually land: finite and inside both caps.</summary>
        public void ShapeChest(ref float maxEscrow, ref float reserve)
        {
            maxEscrow = Finite(maxEscrow, DefaultMaxEscrow);
            if (maxEscrow < 0f) maxEscrow = 0f;
            if (maxEscrow > EscrowCap) maxEscrow = EscrowCap;
            reserve = Finite(reserve, 0f);
            if (reserve < 0f) reserve = 0f;
            if (reserve > ReserveCap) reserve = ReserveCap;
        }

        /// <summary>
        /// Leans on one objective. A weight near zero clears the axis instead of storing it.
        /// Unknown keys are the service's problem (it checks the live objective list); here
        /// only the shape is enforced.
        /// </summary>
        public bool SetAxis(string key, float weight, string setter)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string bounded = key.Length > PriorityDirective.MaximumKeyLength
                ? key.Substring(0, PriorityDirective.MaximumKeyLength)
                : key;
            float clamped = Finite(weight, 0f);
            if (clamped < -1f) clamped = -1f;
            if (clamped > 1f) clamped = 1f;

            for (int i = 0; i < axes.Count; i++)
            {
                if (!string.Equals(axes[i].Key, bounded, StringComparison.Ordinal)) continue;
                if (Math.Abs(clamped) < 0.001f)
                {
                    axes.RemoveAt(i);
                    Setter = Bound(setter);
                    return true;
                }
                if (Math.Abs(clamped - axes[i].Weight) < 0.001f) return false;
                axes[i] = new AxisWeight(bounded, clamped);
                Setter = Bound(setter);
                return true;
            }

            if (Math.Abs(clamped) < 0.001f || axes.Count >= MaximumAxes) return false;
            axes.Add(new AxisWeight(bounded, clamped));
            Setter = Bound(setter);
            return true;
        }

        public float WeightOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0f;
            for (int i = 0; i < axes.Count; i++)
                if (string.Equals(axes[i].Key, key, StringComparison.Ordinal)) return axes[i].Weight;
            return 0f;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return DefaultStance;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static float Finite(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

        private static string Bound(string setter) =>
            string.IsNullOrEmpty(setter) ? ""
            : setter.Length > MaximumSetterLength ? setter.Substring(0, MaximumSetterLength) : setter;
    }
}
