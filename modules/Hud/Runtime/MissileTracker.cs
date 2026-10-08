using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Own-missile tracker: every 10 Hz tick, the board asks for our missiles in flight and
    /// their two legs (ownship → missile, missile → target). Reads <c>UnitRegistry.allUnits</c>
    /// and each missile's private <c>target</c> (the same field the flare barrage misguides);
    /// never writes to a missile. Distances are local-space (one floating origin).
    /// </summary>
    internal sealed class MissileTracker
    {
        private static readonly FieldInfo TargetField = AccessTools.Field(typeof(Missile), "target");

        private readonly List<Missile> scratch = new List<Missile>(16);

        /// <summary>Own live missiles, nearest first. Empty (never null) without an aircraft.</summary>
        public IReadOnlyList<Missile> Missiles => scratch;

        public int Read(Aircraft own, List<MissileTrack> into)
        {
            scratch.Clear();
            into.Clear();
            if (own == null || own.disabled) return 0;
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null) return 0;
            Vector3 ownPos = own.transform.position;

            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is Missile m) || m == null || m.disabled) continue;
                if (m.owner != own) continue;
                scratch.Add(m);
            }
            scratch.Sort((a, b) =>
            {
                float da = (a.transform.position - ownPos).sqrMagnitude;
                float db = (b.transform.position - ownPos).sqrMagnitude;
                return da.CompareTo(db);
            });

            int shown = 0;
            for (int i = 0; i < scratch.Count && shown < MissileTracks.MaxShown; i++)
            {
                Missile m = scratch[i];
                Vector3 mPos = m.transform.position;
                Unit target = TargetField?.GetValue(m) as Unit;
                bool hasTarget = target != null && !target.disabled;
                WeaponInfo info = null;
                try { info = m.GetWeaponInfo(); } catch { info = null; }
                string name = info != null && !string.IsNullOrEmpty(info.shortName) ? info.shortName
                    : info != null && !string.IsNullOrEmpty(info.weaponName) ? info.weaponName : "MSL";
                string targetName = hasTarget && target.definition != null && !string.IsNullOrEmpty(target.definition.unitName)
                    ? target.definition.unitName : hasTarget ? "TGT" : string.Empty;
                into.Add(new MissileTrack
                {
                    Id = m.GetInstanceID(),
                    Name = name,
                    OwnM = Vector3.Distance(ownPos, mPos),
                    TargetM = hasTarget ? Vector3.Distance(mPos, target.transform.position) : float.NaN,
                    HasTarget = hasTarget,
                    TargetName = targetName,
                    AgeS = m.timeSinceSpawn,
                });
                shown++;
            }
            return shown;
        }
    }
}
