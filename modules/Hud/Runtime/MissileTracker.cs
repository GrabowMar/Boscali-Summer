using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Own-missile tracker, event-driven: the game raises <c>Unit.onRegisterMissile</c> on a missile's owner from
    /// <c>Missile.StartMissile</c> and <c>onDeregisterMissile</c> when it is disabled, so the live set is exact and nothing
    /// scans <c>UnitRegistry.allUnits</c>. <see cref="Missiles"/> keeps LAUNCH order (the follow view indexes into it);
    /// the 10 Hz <see cref="Read"/> picks the nearest few for the board's lines and reads each missile's private
    /// <c>target</c> (the field the flare barrage misguides). Never writes to a missile. Distances are local-space.
    /// </summary>
    internal sealed class MissileTracker
    {
        /// <summary>More than this in the air at once is not tracked (a 64-bit mask picks the nearest lines).</summary>
        private const int MaxLive = 32;

        private static readonly AccessTools.FieldRef<Missile, Unit> TargetRef = NativeRefs.Field<Missile, Unit>("target");

        private readonly List<Missile> live = new List<Missile>(8);
        private Aircraft subscribed;

        /// <summary>Raised the moment the game registers one of our missiles: the shot tracker's launch hook.</summary>
        public event Action<Missile> Launched;

        /// <summary>Own live missiles in launch order. Empty (never null) without an aircraft.</summary>
        public IReadOnlyList<Missile> Missiles => live;

        /// <summary>True while one of our missiles with this instance id is still in the air.</summary>
        public bool IsLive(int instanceId)
        {
            for (int i = 0; i < live.Count; i++)
                if (live[i] != null && !live[i].disabled && live[i].GetInstanceID() == instanceId) return true;
            return false;
        }

        public int Read(Aircraft own, List<MissileTrack> into)
        {
            into.Clear();
            if (own != subscribed) Swap(own);
            for (int i = live.Count - 1; i >= 0; i--)
                if (live[i] == null || live[i].disabled) live.RemoveAt(i);
            if (own == null || own.disabled) return 0;
            Vector3 ownPos = own.transform.position;

            // The lines shown are the nearest few, picked by repeated minimum: `live` keeps launch order and nothing allocates.
            ulong taken = 0;
            int shown = 0;
            while (shown < MissileTracks.MaxShown && shown < live.Count)
            {
                int best = -1;
                float bestSqr = float.MaxValue;
                for (int i = 0; i < live.Count; i++)
                {
                    if ((taken & (1UL << i)) != 0) continue;
                    float d = (live[i].transform.position - ownPos).sqrMagnitude;
                    if (d < bestSqr) { bestSqr = d; best = i; }
                }
                if (best < 0) break;
                taken |= 1UL << best;
                into.Add(TrackOf(live[best], ownPos));
                shown++;
            }
            return shown;
        }

        /// <summary>Drop the subscription (scene reset, feature teardown). Idempotent.</summary>
        public void Release() => Swap(null);

        private void Swap(Aircraft own)
        {
            if (subscribed != null)
            {
                subscribed.onRegisterMissile -= OnRegister;
                subscribed.onDeregisterMissile -= OnDeregister;
            }
            live.Clear();
            subscribed = own;
            if (own == null) return;
            own.onRegisterMissile += OnRegister;
            own.onDeregisterMissile += OnDeregister;
            // Missiles already in the air (fired while the HUD was hidden, or before a re-enable) are live too; no Launched for them.
            List<Unit> all = UnitRegistry.allUnits;
            for (int i = 0; all != null && i < all.Count; i++)
                if (all[i] is Missile m && m.owner == own && !m.disabled && live.Count < MaxLive) live.Add(m);
        }

        private void OnRegister(Missile missile)
        {
            if (missile == null) return;
            if (live.Count < MaxLive && !live.Contains(missile)) live.Add(missile);
            // Raised inside the game's own multicast: a throwing subscriber must not abort Missile.StartMissile.
            try { Launched?.Invoke(missile); }
            catch (Exception e) { BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Hud.MissileLaunched", e); }
        }

        private void OnDeregister(Missile missile) => live.Remove(missile);

        private static MissileTrack TrackOf(Missile m, Vector3 ownPos)
        {
            Vector3 mPos = m.transform.position;
            Unit target = NativeRefs.Get(TargetRef, m);
            bool hasTarget = target != null && !target.disabled;
            WeaponInfo info = null;
            try { info = m.GetWeaponInfo(); } catch { info = null; }
            string name = info != null && !string.IsNullOrEmpty(info.shortName) ? info.shortName
                : info != null && !string.IsNullOrEmpty(info.weaponName) ? info.weaponName : "MSL";
            string targetName = hasTarget && target.definition != null && !string.IsNullOrEmpty(target.definition.unitName)
                ? target.definition.unitName : hasTarget ? "TGT" : string.Empty;
            return new MissileTrack
            {
                Id = m.GetInstanceID(),
                Name = name,
                OwnM = Vector3.Distance(ownPos, mPos),
                TargetM = hasTarget ? Vector3.Distance(mPos, target.transform.position) : float.NaN,
                HasTarget = hasTarget,
                TargetName = targetName,
                AgeS = m.timeSinceSpawn,
            };
        }
    }
}
