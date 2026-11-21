using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Damage
{
    /// <summary>
    /// One independently damageable point on a structure. The health itself lives on the game's own
    /// <see cref="UnitPart"/>; this is the deterministic state every peer derives from it. A point may
    /// stand on another point (a section spine); required points are the ones whose loss finishes the
    /// structure. The root wreck hull is normally a point that is not required.
    /// </summary>
    internal sealed class HitPoint
    {
        internal HitPoint(string name, float capacity, int parent, bool required)
        {
            Name = name ?? string.Empty;
            Capacity = capacity > 0f ? capacity : 100f;
            Parent = parent;
            Required = required;
            Health = Capacity;
        }

        public string Name { get; }
        public float Capacity { get; }
        public int Parent { get; }
        public bool Required { get; }
        public float Health { get; internal set; }
        public bool Broken { get; internal set; }

        /// <summary>1 intact, 0 destroyed; clamped for drawing and stage decisions.</summary>
        public float Fraction => Capacity > 0f ? System.Math.Clamp(Health / Capacity, 0f, 1f) : 0f;

        /// <summary>0 intact, 1 destroyed; the value presentation uses for soot and sag.</summary>
        public float Damage => 1f - Fraction;
    }

    /// <summary>
    /// The deterministic multi-hitpoint bookkeeping shared by every peer: N points, independent
    /// health, one-shot breaks, and an aggregate that only finishes the structure when every required
    /// point is gone. Pure C# (no UnityEngine), pinned by the offline tests.
    /// </summary>
    internal sealed class HitPointModel
    {
        private readonly List<HitPoint> points = new List<HitPoint>(64);

        public IReadOnlyList<HitPoint> Points => points;
        public int Count => points.Count;

        /// <summary>Add a point; a parent must already exist and precede its children.</summary>
        public int Add(string name, float capacity = 100f, int parent = -1, bool required = true)
        {
            if (parent < -1 || parent >= points.Count)
                throw new ArgumentOutOfRangeException(nameof(parent), "a point's parent must precede it");
            points.Add(new HitPoint(name, capacity, parent, required));
            return points.Count - 1;
        }

        /// <summary>
        /// True exactly once, on the observation that takes the point from alive to broken. Health is
        /// recorded as observed (the game may drive it negative) so fractions clamp the same everywhere.
        /// </summary>
        public bool Observe(int index, float health)
        {
            if (index < 0 || index >= points.Count) return false;
            HitPoint point = points[index];
            point.Health = health;
            if (point.Broken || health > 0f) return false;
            point.Broken = true;
            return true;
        }

        /// <summary>Force a point broken (host propagation, or a child of a collapsed parent).</summary>
        public bool Break(int index)
        {
            if (index < 0 || index >= points.Count) return false;
            HitPoint point = points[index];
            if (point.Broken) return false;
            point.Health = 0f;
            point.Broken = true;
            return true;
        }

        public int BrokenCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < points.Count; i++) if (points[i].Broken) n++;
                return n;
            }
        }

        /// <summary>A structure with no required points is never considered finished by this model.</summary>
        public bool AllBroken
        {
            get
            {
                bool any = false;
                for (int i = 0; i < points.Count; i++)
                {
                    if (!points[i].Required) continue;
                    any = true;
                    if (!points[i].Broken) return false;
                }
                return any;
            }
        }

        /// <summary>The lowest fraction among required points that are still alive (1 when all are gone).</summary>
        public float WeakestRequired
        {
            get
            {
                float worst = 1f;
                for (int i = 0; i < points.Count; i++)
                    if (points[i].Required && !points[i].Broken) worst = System.Math.Min(worst, points[i].Fraction);
                return worst;
            }
        }
    }
}
