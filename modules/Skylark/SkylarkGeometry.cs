using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    internal static class SkylarkGeometry
    {
        internal static void Refit(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            // Main collision skins follow their own visible mesh, without a box spanning an entire wing/fan assembly.
            string[] solidPrefixes = { "Fuselage_", "Wing_", "LERX_", "Sidepod_", "Intake_", "Canard_", "Arm_", "Fin_", "Ruddervator_", "Elevon_", "Flap_", "Endplate_" };
            foreach (Transform t in nodes.Values)
            {
                if (t.name.EndsWith("_LOD") || (t.name != "SkylarkVisuals" && !solidPrefixes.Any(p => t.name.StartsWith(p)))) continue;
                MeshFilter mesh = t.GetComponent<MeshFilter>();
                AeroPart owner = t.GetComponentInParent<AeroPart>(true);
                if (mesh == null || owner == null) continue;
                MeshCollider collider = t.gameObject.AddComponent<MeshCollider>();
                // The cockpit render bevels exceed PhysX's convex polygon budget. Its authored
                // identity-frame LOD is a validated 56-triangle collision surrogate (bounds within 2.3 cm).
                collider.sharedMesh = t.name == "Fuselage_Cockpit" ? nodes["Fuselage_Cockpit_LOD"].GetComponent<MeshFilter>().sharedMesh : mesh.sharedMesh;
                collider.convex = true;
                t.gameObject.layer = owner.gameObject.layer;
                t.gameObject.AddComponent<SkylarkHitbox>().Owner = owner;
            }
            foreach (string suffix in new[] { "FL", "FR", "RL", "RR" })
            {
                Transform duct = nodes["Duct_" + suffix];
                AeroPart owner = duct.GetComponentInParent<AeroPart>(true);
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8f;
                    float sector = Mathf.Repeat(angle + Mathf.PI / 8f, Mathf.PI / 4f) - Mathf.PI / 8f;
                    float k = SkylarkTemplate.Scale, outside = .93f * k / Mathf.Cos(sector), radius = (.86f * k + outside) * .5f;
                    GameObject segment = new GameObject("ShroudHitbox_" + suffix + "_" + i);
                    segment.layer = owner.gameObject.layer;
                    segment.transform.SetParent(duct, false);
                    segment.transform.localPosition = new Vector3(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle), 0);
                    segment.transform.localRotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.forward);
                    BoxCollider box = segment.AddComponent<BoxCollider>();
                    box.size = new Vector3(outside - .86f * k, 2f * radius * Mathf.Tan(Mathf.PI / 16f), .32f * k);
                    segment.AddComponent<SkylarkHitbox>().Owner = owner;
                }
            }
            foreach (IGrouping<AeroPart, SkylarkHitbox> group in aircraft.GetComponentsInChildren<SkylarkHitbox>(true).GroupBy(h => h.Owner))
            {
                SkylarkDamageGroup shared = group.Key.gameObject.AddComponent<SkylarkDamageGroup>();
                foreach (SkylarkHitbox hitbox in group) { hitbox.Group = shared; hitbox.BlastShare = 1f / group.Count(); }
            }
            RestoreLods(aircraft, nodes);
            aircraft.gameObject.AddComponent<SkylarkCollisionFilter>().Configure(aircraft);
        }

        private static void RestoreLods(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            var authored = new HashSet<Renderer>(nodes.Values.Select(t => t.GetComponent<Renderer>()).Where(r => r != null));
            bool Excluded(Renderer r) => r.name.StartsWith("Cockpit") || r.name.StartsWith("Canopy") || r.name.StartsWith("Rotor")
                || r.name.StartsWith("Internal_") || r.name == "Tacscreen" || r.name == "Stick" || r.name == "Throttle" || r.name.StartsWith("MissileWarning");
            foreach (AeroPart part in aircraft.GetComponentsInChildren<AeroPart>(true))
            {
                Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true)
                    .Where(r => authored.Contains(r) && r.GetComponentInParent<AeroPart>(true) == part && !Excluded(r)).ToArray();
                if (renderers.Length == 0) continue;
                Renderer[] near = renderers.Where(r => !r.name.EndsWith("_LOD")).ToArray();
                Renderer[] distant = renderers.Where(r => r.name.EndsWith("_LOD") ||
                    (r.name != "SkylarkVisuals" && !nodes.ContainsKey(r.name + "_LOD") && !r.name.StartsWith("Fittings_") && !r.name.StartsWith("GearBrace") && !r.name.StartsWith("ExitVane"))).ToArray();
                foreach (Renderer renderer in renderers) renderer.enabled = true;
                LODGroup lod = part.gameObject.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(.06f, near), new LOD(.0015f, distant) });
                lod.RecalculateBounds();
                float sizeRatio = Mathf.Max(.001f, lod.size / (10.116f * SkylarkTemplate.Scale));
                lod.SetLODs(new[] { new LOD(.04f * sizeRatio, near), new LOD(.001f * sizeRatio, distant) });
            }
            // Interior structural hardware does not need to draw once its openings are sub-pixel.
            foreach (Transform t in nodes.Values.Where(t => t.name.StartsWith("Internal_")))
            {
                Renderer renderer = t.GetComponent<Renderer>();
                if (renderer == null) continue;
                LODGroup lod = t.gameObject.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(.08f, new[] { renderer }) });
                lod.RecalculateBounds();
            }
        }
    }

    // Native FixedJoint.enableCollision excludes only directly connected bodies. The compact quad-fan
    // shell also overlaps other structural islands, so exclude contacts within this intact airframe.
    internal sealed class SkylarkCollisionFilter : MonoBehaviour
    {
        [SerializeField] private Aircraft aircraft;
        [SerializeField] private Collider[] colliders;
        [SerializeField] private UnitPart[] owners;
        private Coroutine restoreContacts;

        internal void Configure(Aircraft source)
        {
            aircraft = source;
            colliders = source.GetComponentsInChildren<Collider>(true);
            owners = colliders.Select(c => c.GetComponentInParent<UnitPart>(true)).ToArray();
        }

        private void Awake()
        {
            aircraft.onInitialize += IgnoreInternalContacts;
            foreach (UnitPart part in owners.Where(p => p != null).Distinct()) part.onParentDetached += Detached;
        }

        private void IgnoreInternalContacts()
        {
            for (int i = 0; i < colliders.Length; i++)
                for (int j = i + 1; j < colliders.Length; j++)
                    if (colliders[i] != null && colliders[j] != null && colliders[i].enabled && colliders[j].enabled && owners[i] != owners[j])
                        Physics.IgnoreCollision(colliders[i], colliders[j]);
        }

        private void Detached(UnitPart part)
        {
            if (restoreContacts != null) StopCoroutine(restoreContacts);
            restoreContacts = StartCoroutine(RestoreDebrisContacts());
        }

        private System.Collections.IEnumerator RestoreDebrisContacts()
        {
            // Allow a torn island to clear its original interlocking seams before it can hit the airframe again.
            yield return new WaitForSeconds(1f);
            UnitPart[] assemblies = owners.Select(AssemblyRoot).ToArray();
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null && owners[i] != null)
                    for (int j = i + 1; j < colliders.Length; j++)
                        if (colliders[j] != null && owners[j] != null && owners[i] != owners[j])
                            Physics.IgnoreCollision(colliders[i], colliders[j], assemblies[i] == assemblies[j]);
            restoreContacts = null;
        }

        private static UnitPart AssemblyRoot(UnitPart part)
        {
            while (part != null)
            {
                object attachment = SkylarkTemplate.Get<object>(part, "attachInfo");
                if (attachment == null || SkylarkTemplate.Get<bool>(attachment, "detachedFromParentPart")) return part;
                UnitPart parent = SkylarkTemplate.Get<UnitPart>(attachment, "parentPart");
                if (parent == null) return part;
                part = parent;
            }
            return null;
        }

        private void OnDestroy()
        {
            if (aircraft != null) aircraft.onInitialize -= IgnoreInternalContacts;
            foreach (UnitPart part in owners.Where(p => p != null).Distinct()) part.onParentDetached -= Detached;
        }
    }

    /// <summary>Native bullets resolve IDamageable on the hit collider itself. Forward to the owning native damage island.</summary>
    internal sealed class SkylarkHitbox : MonoBehaviour, IDamageable
    {
        [SerializeField] internal AeroPart Owner;
        [SerializeField] internal SkylarkDamageGroup Group;
        [SerializeField] internal float BlastShare = 1f;
        public void TakeDamage(float pierce, float blast, float affected, float fire, float impact, PersistentID dealer) =>
            Owner.TakeDamage(pierce, blast, pierce == 0f && blast > 0f ? affected * BlastShare : affected, fire, impact, dealer);
        public void ApplyDamage(float pierce, float blast, float fire, float impact) => Owner.ApplyDamage(pierce, blast, fire, impact);
        public void TakeShockwave(Vector3 origin, float overpressure, float blastPower)
        {
            if (Group.AcceptShockwave(origin)) Owner.TakeShockwave(origin, overpressure, blastPower);
        }
        public void Detach(Vector3 velocity, Vector3 relativePos) => Owner.Detach(velocity, relativePos);
        public ArmorProperties GetArmorProperties() => Owner.GetArmorProperties();
        public Unit GetUnit() => Owner.GetUnit();
        public float GetMass() => Owner.GetMass();
        public Transform GetTransform() => Owner.GetTransform();
    }

    // A compound hitbox still represents one native damage island, not sixteen independent full-mass blast targets.
    internal sealed class SkylarkDamageGroup : MonoBehaviour
    {
        private int lastFrame = -1;
        private Vector3 lastOrigin;
        internal bool AcceptShockwave(Vector3 origin)
        {
            if (lastFrame == Time.frameCount && lastOrigin == origin) return false;
            lastFrame = Time.frameCount;
            lastOrigin = origin;
            return true;
        }
    }
}
