using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain.Cyber;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// The two track-deception operations. Both rewrite what hostile factions believe about
    /// your aircraft: ghost freezes their tracks at stale positions, spoof feeds them a false
    /// formation. The host runs it for AI truth; every peer runs the same local pass from the
    /// replicated effect so enemy players' maps agree. Bounded: four live effects, 64
    /// aircraft, every hostile faction dictionary.
    /// </summary>
    internal sealed class CyberEffects
    {
        private const int MaximumActive = 4;
        private const int MaximumUnits = 64;
        private const float ApplyInterval = 0.25f;

        private sealed class Active
        {
            public HackKind Kind;
            public FactionHQ Attacker;
            public float X, Z, Until;
            public readonly Dictionary<PersistentID, GlobalPosition> Frozen =
                new Dictionary<PersistentID, GlobalPosition>();
        }

        private readonly List<Active> active = new List<Active>(MaximumActive);
        private float nextApply;

        public int ActiveCount => active.Count;

        public bool Begin(HackKind kind, FactionHQ attacker, float x, float z, float duration, float now)
        {
            if (attacker == null || active.Count >= MaximumActive) return false;
            active.Add(new Active
            {
                Kind = kind,
                Attacker = attacker,
                X = x,
                Z = z,
                Until = now + Mathf.Max(1f, duration)
            });
            Apply(now);
            return true;
        }

        public void Tick(float now)
        {
            for (int i = active.Count - 1; i >= 0; i--)
                if (now >= active[i].Until || active[i].Attacker == null)
                    active.RemoveAt(i);

            if (active.Count == 0 || now < nextApply) return;
            nextApply = now + ApplyInterval;
            Apply(now);
        }

        public void Clear() => active.Clear();

        private void Apply(float now)
        {
            for (int i = 0; i < active.Count; i++)
                ApplyEffect(active[i], now);
        }

        private static void ApplyEffect(Active effect, float now)
        {
            if (effect.Attacker == null) return;
            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) return;

            bool spoof = effect.Kind == HackKind.Spoof;
            int processed = 0;
            for (int i = 0; i < units.Count && processed < MaximumUnits; i++)
            {
                if (!(units[i] is Aircraft aircraft) || aircraft.disabled) continue;
                if (aircraft.NetworkHQ != effect.Attacker) continue;
                processed++;

                if (!effect.Frozen.TryGetValue(aircraft.persistentID, out GlobalPosition frozen))
                {
                    frozen = aircraft.GlobalPosition();
                    effect.Frozen[aircraft.persistentID] = frozen;
                }

                GlobalPosition shown = spoof ? Decoy(effect, aircraft, frozen) : frozen;
                foreach (FactionHQ victim in FactionRegistry.GetAllHQs())
                {
                    if (victim == null || victim == effect.Attacker) continue;
                    if (!victim.trackingDatabase.TryGetValue(aircraft.persistentID, out TrackingInfo info)) continue;
                    info.lastKnownPosition = shown;
                    info.lastSpottedTime = now - 10f;
                }
            }
        }

        /// <summary>False formation position: the targeted point with a stable per-aircraft offset.</summary>
        private static GlobalPosition Decoy(Active effect, Aircraft aircraft, GlobalPosition real)
        {
            int seed = aircraft.GetInstanceID();
            float jitterX = ((seed % 7) - 3) * 400f;
            float jitterZ = (((seed / 7) % 7) - 3) * 400f;
            return new GlobalPosition(effect.X + jitterX, real.y, effect.Z + jitterZ);
        }
    }
}
