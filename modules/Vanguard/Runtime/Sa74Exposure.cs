using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    internal static class Sa74Exposure
    {
        // One gameplay pulse on the authoritative detonation. Snapshot visible parts before damage can remove cover.
        internal static int Apply(Vector3 burst, float radius, PersistentID owner)
        {
            if (!GameAccess.IsServer()) return 0;
            Vector3 eye = burst + Vector3.up * 4f;
            var exposed = new Dictionary<IDamageable, float>();
            foreach (Collider collider in Physics.OverlapSphere(eye, radius, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                var part = collider.GetComponentInParent<IDamageable>();
                if (part == null || exposed.ContainsKey(part)) continue;
                Unit unit = part.GetUnit();
                if (unit != null && (unit.disabled || !unit.IsServer)) continue;
                if (part is UnitPart candidate && (unit == null || candidate.hitPoints <= 0f)) continue;
                Vector3 target = collider.bounds.center;
                float fraction = Vector3.Distance(eye, target) / radius;
                if (fraction >= 1f) continue;
                if (Physics.Linecast(eye, target, out RaycastHit cover, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)
                    && cover.collider != collider && cover.collider.GetComponentInParent<IDamageable>() != part) continue;
                // A fixed fraction of the part's health gives aircraft, ships and scenery a consistent game rule.
                float health = part is UnitPart nativePart ? Mathf.Max(1f, nativePart.hitPoints) : 100f;
                exposed.Add(part, Mathf.Max(1f, health * Mathf.Lerp(.78f, .14f, fraction)));
            }
            int affected = 0;
            System.Exception failure = null;
            foreach (var entry in exposed)
                if (entry.Key is Component component && component != null)
                {
                    // A component can be detached or despawned by an earlier hit on its parent.
                    try { entry.Key.TakeDamage(0f, 0f, 1f, 0f, entry.Value, owner); affected++; }
                    catch (System.Exception error) { failure = error; }
                }
            if (failure != null) Plugin.Logger?.LogWarning("[Vanguard] SA-74N exposure skipped a damage receiver: " + failure.Message);
            return affected;
        }
    }
}
