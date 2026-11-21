using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Core.Damage
{
    /// <summary>
    /// Binds a structure's native <see cref="UnitPart"/> hit points into a deterministic multi-point
    /// model. Every peer observes the same replicated part health at 5 Hz and raises the same break
    /// events in the same order; no extra network messages, no new authority. Presentation subscribes
    /// to <see cref="PointChanged"/> / <see cref="PointBroken"/> / <see cref="AllBroken"/> and decides
    /// what geometry, fire and debris follow. A part is the damage carrier; this component is the
    /// reusable bookkeeping on top of it.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class MultiHitPoint : MonoBehaviour
    {
        private const float Tick = 0.2f;

        private readonly HitPointModel model = new HitPointModel();
        private readonly List<UnitPart> parts = new List<UnitPart>(64);
        private float next;
        private bool announced;

        public HitPointModel Model => model;
        public IReadOnlyList<HitPoint> Points => model.Points;
        public int Count => model.Count;
        public int BrokenCount => model.BrokenCount;
        public bool AllBroken => model.AllBroken;

        /// <summary>Health changed (or the point just broke); the index is into <see cref="Points"/>.</summary>
        public event Action<int> PointChanged;

        /// <summary>The observation that took the point from alive to broken, exactly once.</summary>
        public event Action<int> PointBroken;

        /// <summary>Every required point is broken, exactly once.</summary>
        public event Action AllBrokenReached;

        /// <summary>Add a point backed by a native part. The owner applies the planned capacity after all parts awake.</summary>
        public int Add(string name, UnitPart part, float capacity = 100f, int parent = -1, bool required = true)
        {
            int index = model.Add(name, capacity, parent, required);
            parts.Add(part);
            return index;
        }

        public HitPoint Point(int index) => index >= 0 && index < model.Count ? model.Points[index] : null;

        public UnitPart Part(int index) => index >= 0 && index < parts.Count ? parts[index] : null;

        public float Fraction(int index)
        {
            HitPoint point = Point(index);
            return point != null ? point.Fraction : 0f;
        }

        /// <summary>Force a break, used by the owner when a parent point takes its children with it.</summary>
        public void Break(int index)
        {
            if (!model.Break(index)) return;
            PointChanged?.Invoke(index);
            PointBroken?.Invoke(index);
            Announce();
        }

        private void Update()
        {
            if (model.Count == 0 || Time.time < next) return;
            next = Time.time + Tick;
            for (int i = 0; i < model.Count; i++)
            {
                HitPoint point = model.Points[i];
                UnitPart part = parts[i];
                float health = part != null ? part.hitPoints : 0f;
                bool changed = point.Health != health;
                bool broke = model.Observe(i, health);
                if (changed) PointChanged?.Invoke(i);
                if (!broke) continue;
                PointBroken?.Invoke(i);
                Announce();
            }
        }

        private void Announce()
        {
            if (announced || !model.AllBroken) return;
            announced = true;
            AllBrokenReached?.Invoke();
        }
    }
}
