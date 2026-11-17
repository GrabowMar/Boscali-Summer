#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    // Sandbox-only exercises call native systems. Readbacks report actual instantiated components.
    public static class SkylarkAcceptance
    {
        public static object Inspect(Dictionary<string, object> args)
        {
            Aircraft aircraft = args.TryGetValue("idUnit", out object unit) ? unit as Aircraft : null;
            if (aircraft == null) return new { ok = false };
            Transform[] ducts = SkylarkTemplate.Get<Transform[]>(aircraft.GetComponent<SkylarkAnimation>(), "ducts");
            Bounds[] fanBounds = ducts.Select(t => SkylarkTemplate.LocalBounds(aircraft.transform, new[] { t.GetComponent<Renderer>() })).ToArray();
            var stores = aircraft.weaponStations.Where(s => !s.WeaponInfo.gun).SelectMany(s => s.Weapons).Select(w =>
            {
                Renderer[] renderers = w.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.gameObject.activeInHierarchy).Cast<Renderer>().ToArray();
                Bounds bounds = SkylarkTemplate.LocalBounds(aircraft.transform, renderers);
                float[] separations = fanBounds.Select(f => Mathf.Max(bounds.min.x - f.max.x, f.min.x - bounds.max.x,
                    bounds.min.y - f.max.y, f.min.y - bounds.max.y, bounds.min.z - f.max.z, f.min.z - bounds.max.z)).ToArray();
                return new { w.name, meshCount = renderers.Length, center = V(bounds.center), size = V(bounds.size),
                    minimumAxisSeparation = separations.Min(), fanBoundsDisjoint = renderers.Length > 0 && separations.All(s => s > 0f) };
            }).ToArray();
            var skin = aircraft.partLookup.SelectMany(part => part.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.GetComponentInParent<UnitPart>(true) == part && r.sharedMaterial != null && r.sharedMaterial.shader.name == "Shader Graphs/AircraftSkin"
                    && r.sharedMaterial.GetTexture("_Basecolor") != null && r.sharedMaterial.GetTexture("_Basecolor").name.StartsWith("BS_Skylark"))
                .Select(r => new { owner = part.name, renderer = r.name, hp = r.sharedMaterial.GetFloat("_HitPoints"),
                    damage = r.sharedMaterial.GetTexture("_BasecolorDmg")?.name })).ToArray();
            Canopy[] canopies = SkylarkTemplate.Get<Canopy[]>(aircraft, "canopies");
            var pilots = UnityEngine.Object.FindObjectsOfType<PilotDismounted>().Where(p => p.parentUnit.Equals(aircraft.persistentID))
                .Select(p => new { state = p.animationState.ToString(), onRail = p.IsOnEjectionRail, position = V(p.transform.position),
                    velocity = V(p.rb.velocity), separation = Vector3.Distance(p.transform.position, aircraft.transform.position) }).ToArray();
            SkylarkCollisionFilter filter = aircraft.GetComponent<SkylarkCollisionFilter>();
            Collider[] colliders = SkylarkTemplate.Get<Collider[]>(filter, "colliders");
            UnitPart[] owners = SkylarkTemplate.Get<UnitPart[]>(filter, "owners");
            int crossAssemblyPairs = 0, crossAssemblyIgnored = 0, detachedInternalPairs = 0, detachedInternalIgnored = 0;
            for (int i = 0; i < colliders.Length; i++)
                for (int j = i + 1; j < colliders.Length; j++)
                {
                    if (colliders[i] == null || colliders[j] == null || !colliders[i].enabled || !colliders[j].enabled
                        || owners[i] == null || owners[j] == null || owners[i] == owners[j]) continue;
                    bool ignored = Physics.GetIgnoreCollision(colliders[i], colliders[j]);
                    if (owners[i].IsDetached() != owners[j].IsDetached()) { crossAssemblyPairs++; if (ignored) crossAssemblyIgnored++; }
                    else if (owners[i].IsDetached()) { detachedInternalPairs++; if (ignored) detachedInternalIgnored++; }
                }
            return new { ok = true, status = SkylarkStatusDisplay.Inspect(aircraft), hangars = SkylarkHangars.Inspect(),
                stores, allStoresClear = stores.Length > 0 && stores.All(s => s.fanBoundsDisjoint), skin,
                damagedWingHp = skin.Where(s => s.owner == "wing1_L").Select(s => s.hp).DefaultIfEmpty(-1f).Min(),
                neighborWingHp = skin.Where(s => s.owner == "wing1_R").Select(s => s.hp).DefaultIfEmpty(-1f).Min(),
                ejected = aircraft.HasEjected(), canopyUpSpeed = canopies.Where(c => c != null).Select(c => SkylarkTemplate.Get<Rigidbody>(c, "rb"))
                    .Where(rb => rb != null).Select(rb => rb.velocity.y - aircraft.rb.velocity.y).DefaultIfEmpty(0f).Max(), // relative: the airframe may be sinking
                pilots, separatedPilots = pilots.Count(p => !p.onRail && p.separation > 3f),
                unparentedCanopies = canopies.Count(c => c != null && c.ejectionTransform != null && c.ejectionTransform.parent == null),
                collision = new { crossAssemblyPairs, crossAssemblyIgnored, detachedInternalPairs, detachedInternalIgnored },
                missiles = UnityEngine.Object.FindObjectsOfType<Missile>().Where(m => m.owner == aircraft)
                    .Select(m => new { m.name, target = m.targetID.ToString(), position = V(m.transform.position), speed = m.rb.velocity.magnitude }).ToArray(),
                canopy = canopies.Where(c => c != null).Select(c => new { open = SkylarkTemplate.Get<float>(c, "openAmount"),
                    ejectionForce = V(SkylarkTemplate.Get<Vector3>(c, "ejectionForce")),
                    position = V(c.ejectionTransform.position), unparented = c.ejectionTransform.parent == null,
                    velocity = SkylarkTemplate.Get<Rigidbody>(c, "rb") is Rigidbody rb ? V(rb.velocity) : null }).ToArray(),
                gear = aircraft.partLookup.SelectMany(p => p.GetComponentsInChildren<LandingGear>(true)).Distinct()
                    .Select(g => new { g.name, wheels = SkylarkTemplate.Get<Transform[]>(g, "wheels").Select(t => new { t.name, axis = V(aircraft.transform.InverseTransformDirection(t.right)) }) }) };
        }

        public static object Benchmark(Dictionary<string, object> args)
        {
            Aircraft aircraft = args.TryGetValue("idUnit", out object unit) ? unit as Aircraft : null;
            if (aircraft == null) return new { ok = false };
            return new { ok = true, key = aircraft.definition.jsonKey, name = aircraft.definition.unitName,
                speed = aircraft.speed, mass = aircraft.GetMass(), throttle = aircraft.GetInputs().throttle,
                altitude = aircraft.radarAlt, verticalSpeed = aircraft.rb.velocity.y, cost = aircraft.definition.value,
                radarSize = aircraft.definition.radarSize, nominalSpeed = aircraft.definition.aircraftInfo.maxSpeed,
                detached = aircraft.partLookup.Count(p => p.IsDetached()) };
        }

        public static object Exercise(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"))) return new { ok = false };
            Aircraft aircraft = args.TryGetValue("idUnit", out object unit) ? unit as Aircraft : null;
            if (aircraft == null || aircraft.definition.jsonKey != SkylarkTemplate.Key) return new { ok = false };
            string action = Convert.ToString(args["action"]);
            if (action == "canopy") aircraft.OpenCanopies();
            else if (action == "eject") aircraft.StartEjectionSequence();
            else
            {
                UnitPart part = aircraft.partLookup.First(p => p.name == Convert.ToString(args["part"]));
                if (action == "damage") part.ApplyDamage(Convert.ToSingle(args["amount"]), 0f, 0f, 0f);
                else if (action == "detach") aircraft.DetachPart(part.id, part.rb.GetPointVelocity(part.transform.position), Vector3.zero);
                else throw new ArgumentException("Unknown Skylark exercise " + action);
            }
            return new { ok = true, action };
        }

        private static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
    }
}
#endif
