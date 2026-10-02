using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Core.Game;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
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
        internal const float ProtectionRadius = 3000f;

        /// <summary>One host pulse breaks up to four ordinary active-radar missile target links.
        /// Native networking and seekers own the resulting coast/reacquisition; never steer,
        /// transfer ownership, detonate, or rewrite nuclear weapons.</summary>
        internal static int BreakSeekerLocks(FactionHQ owner, GlobalPosition target)
        {
            if (!GameAccess.IsServer() || owner == null) return 0;
            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) return 0;
            Vector3 point = target.ToLocalPosition();
            int broken = 0;
            for (int i = 0; i < units.Count && i < 512 && broken < 4; i++)
            {
                // Fresh missiles are appended to the registry; inspect the newest 512 first.
                if (!(units[units.Count - 1 - i] is Missile missile) || missile == null || missile.disabled ||
                    missile.NetworkHQ == null || missile.NetworkHQ == owner || missile.targetID.NotValid ||
                    missile.seekerMode != Missile.SeekerMode.activeLock) continue;
                WeaponInfo info = missile.GetWeaponInfo();
                if (info == null || info.nuclear || missile.GetComponent<ARHSeeker>() == null ||
                    (missile.transform.position - point).sqrMagnitude > ProtectionRadius * ProtectionRadius) continue;
                missile.SetTarget(null);
                broken++;
            }
            return broken;
        }

        private sealed class Active
        {
            public HackKind Kind;
            public FactionHQ Attacker;
            public float X, Z, Until;
            public int Cursor;
            public readonly Dictionary<PersistentID, GlobalPosition> Frozen =
                new Dictionary<PersistentID, GlobalPosition>();
        }

        private readonly List<Active> active = new List<Active>(MaximumActive);
        private float nextApply;

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
            for (int examined = 0; examined < Mathf.Min(128, units.Count) && processed < MaximumUnits; examined++)
            {
                if (effect.Cursor >= units.Count) effect.Cursor = 0;
                if (!(units[effect.Cursor++] is Aircraft aircraft) || aircraft.disabled) continue;
                if (aircraft.NetworkHQ != effect.Attacker) continue;
                GlobalPosition position = aircraft.GlobalPosition();
                float dx = position.x - effect.X, dz = position.z - effect.Z;
                if (dx * dx + dz * dz > ProtectionRadius * ProtectionRadius) continue;
                processed++;

                if (!effect.Frozen.TryGetValue(aircraft.persistentID, out GlobalPosition frozen))
                {
                    if (effect.Frozen.Count >= MaximumUnits) continue;
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
            uint seed = unchecked((uint)aircraft.persistentID.Id);
            float jitterX = ((int)(seed % 7) - 3) * 400f;
            float jitterZ = ((int)((seed / 7) % 7) - 3) * 400f;
            return new GlobalPosition(effect.X + jitterX, real.y, effect.Z + jitterZ);
        }
    }
}
