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
    /// so the host broadcasts only on change. Last writer wins; the setter is whoever the
    /// host derived from the intent's sender, never a client-sent name.
    /// </summary>
    internal sealed class InfluenceState
    {
        internal const int MaximumAxes = 4;
        internal const int MaximumSetterLength = 24;
        internal const float DefaultStance = 0.6f;
        internal const float DefaultMaxEscrow = 15f;
        internal const float MaximumEscrowCap = 100f;

        private readonly List<AxisWeight> axes = new List<AxisWeight>(MaximumAxes);

        public float Stance { get; private set; } = DefaultStance;
        public bool HoldOffense { get; private set; }
        public float MaxEscrowPerPlan { get; private set; } = DefaultMaxEscrow;
        public float ReserveFloor { get; private set; }
        public string Setter { get; private set; } = "";
        public IReadOnlyList<AxisWeight> Axes => axes;

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
            float escrow = Finite(maxEscrow, DefaultMaxEscrow);
            if (escrow < 0f) escrow = 0f;
            if (escrow > MaximumEscrowCap) escrow = MaximumEscrowCap;
            float floor = Finite(reserve, 0f);
            if (floor < 0f) floor = 0f;
            if (Math.Abs(escrow - MaxEscrowPerPlan) < 0.001f && Math.Abs(floor - ReserveFloor) < 0.001f)
                return false;
            MaxEscrowPerPlan = escrow;
            ReserveFloor = floor;
            Setter = Bound(setter);
            return true;
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

        public void Reset()
        {
            Stance = DefaultStance;
            HoldOffense = false;
            MaxEscrowPerPlan = DefaultMaxEscrow;
            ReserveFloor = 0f;
            Setter = "";
            axes.Clear();
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
