using System;
using System.Collections.Generic;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Framework.Fx
{
    /// <summary>
    /// Bounded, main-thread registry. Modules retain effect ownership and ticking; this
    /// registry provides stable identity, diagnostics and reliable reverse-order teardown.
    /// </summary>
    internal sealed class FxRegistry
    {
        private readonly int capacity;
        private readonly Dictionary<string, IClientEffect> byId;
        private readonly List<IClientEffect> order;

        internal FxRegistry(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
            byId = new Dictionary<string, IClientEffect>(capacity, StringComparer.Ordinal);
            order = new List<IClientEffect>(capacity);
        }

        internal int Count => order.Count;

        internal bool Contains(string id) => id != null && byId.ContainsKey(id);

        internal bool TryRegister(IClientEffect effect)
        {
            if (effect == null || !ValidId(effect.EffectId)) return false;
            if (byId.TryGetValue(effect.EffectId, out IClientEffect owner))
                return ReferenceEquals(owner, effect);
            if (order.Count >= capacity) return false;
            byId.Add(effect.EffectId, effect);
            order.Add(effect);
            return true;
        }

        internal bool Unregister(IClientEffect effect)
        {
            if (effect == null || !byId.TryGetValue(effect.EffectId, out IClientEffect owner) ||
                !ReferenceEquals(owner, effect)) return false;
            byId.Remove(effect.EffectId);
            order.Remove(effect);
            return true;
        }

        internal void Describe(IDictionary<string, object> state)
        {
            if (state == null) return;
            for (int i = 0; i < order.Count; i++)
            {
                IClientEffect effect = order[i];
                try { effect.DescribeFx(state); }
                catch (Exception error) { state["fxFault." + effect.EffectId] = error.GetType().Name; }
            }
        }

        /// <summary>Release every effect even if one fails. Returns the number of failures.</summary>
        internal int ReleaseAll()
        {
            IClientEffect[] snapshot = order.ToArray();
            byId.Clear();
            order.Clear();
            int failures = 0;
            for (int i = snapshot.Length - 1; i >= 0; i--)
            {
                try { snapshot[i].ReleaseFx(); }
                catch (Exception) { failures++; }
            }
            return failures;
        }

        private static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 48 || id[0] == '-' || id[id.Length - 1] == '-')
                return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (c == '-' && i > 0 && id[i - 1] != '-') continue;
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9') continue;
                return false;
            }
            return true;
        }
    }
}
