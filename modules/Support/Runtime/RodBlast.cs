using System.Collections.Generic;
using BoscaliSummer.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    internal static class RodBlast
    {
        // Called after the authoritative Missile.Detonate disables its source, including headless.
        public static void Apply(Vector3 point, PersistentID owner)
        {
            if (!GameAccess.IsServer()) return;
            // Damage can synchronously detonate another rod. Each blast owns its query/dedup
            // storage so the nested call cannot overwrite or clear the outer blast's targets.
            var hits = new Collider[512];
            var damaged = new HashSet<IDamageable>();
            var pushed = new HashSet<Rigidbody>();
            int count = Physics.OverlapSphereNonAlloc(point, SupportEffectPolicy.RodBlastRadius, hits);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Collider hit = hits[i];
                    if (hit == null) continue;
                    var damageable = hit.GetComponentInParent<IDamageable>();
                    Unit unit = damageable?.GetUnit();
                    if (unit != null && unit.disabled) continue;
                    var body = hit.attachedRigidbody;
                    if (damageable == null && (body == null || body.isKinematic)) continue;
                    // Unity rejects ClosestPoint on terrain and non-convex mesh colliders.
                    Vector3 closest = hit is BoxCollider || hit is SphereCollider ||
                        hit is CapsuleCollider || (hit is MeshCollider mesh && mesh.convex)
                        ? hit.ClosestPoint(point) : hit.bounds.ClosestPoint(point);
                    float distance = Vector3.Distance(closest, point);
                    float damage = SupportEffectPolicy.RodDamage(distance);
                    if (damage <= 0f) continue;
                    if (damageable != null && damaged.Add(damageable))
                    {
                        if (unit is Building building)
                            building.RegisterRecentExplosion(point.ToGlobalPosition(), damage / 12000f * 25000f);
                        damageable.TakeDamage(damage, damage * 0.2f, 0.9f, 1000f, damage, owner);
                    }
                    if (body != null && !body.isKinematic && pushed.Add(body))
                        body.AddExplosionForce(Mathf.Min(150000f, damage * 12f), point,
                            SupportEffectPolicy.RodBlastRadius, 8f, ForceMode.Impulse);
                }
            }
            finally
            {
                System.Array.Clear(hits, 0, count);
                damaged.Clear(); pushed.Clear();
            }
        }
    }
}
