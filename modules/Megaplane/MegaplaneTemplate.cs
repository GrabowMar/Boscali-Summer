using System;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using Mirage;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>G-240 BREAKWATER: a native SFB-81 Darkreach systems clone carrying the Blender blockout at true scale (designed at 240 m, built past the CL-1201 envelope to 380 x 190 m).
    /// Motion comes from <see cref="MegaplaneFlight"/> (BOTE-style game-logic flight), not from the donor's aerodynamics.</summary>
    internal static class MegaplaneTemplate
    {
        internal const string Key = "BS_Megaplane";
        internal const int PrefabHash = 0x42534D50;
        internal const float Mass = 6500000f;          // 380 m, past CL-1201 (study gross 5,375 t at 340 m); flight is acceleration-driven, so this only weighs collisions
        internal static AircraftDefinition Definition { get; private set; }
        internal static string LastError { get; private set; }
        private static GameObject holder;
        private static bool building, failed;

        private static void Set(object o, string field, object value) => AccessTools.Field(o.GetType(), field).SetValue(o, value);

        internal static void Register(Encyclopedia encyclopedia)
        {
            if (building || failed || encyclopedia == null || Encyclopedia.Lookup == null || encyclopedia.IndexLookup == null) return;
            if (Encyclopedia.Lookup.ContainsKey(Key)) return;
            building = true;
            try
            {
                if (Definition == null) Definition = Build(encyclopedia);
                if (!encyclopedia.aircraft.Contains(Definition)) encyclopedia.aircraft.Add(Definition);
                ((INetworkDefinition)Definition).LookupIndex = encyclopedia.IndexLookup.Count;
                encyclopedia.IndexLookup.Add(Definition);
                Encyclopedia.Lookup.Add(Key, Definition);
                Set(encyclopedia, "aircraftAndVehiclesCache", null);
                var manager = GameAccess.NetworkManagerOrNull;
                if (manager != null && manager.ClientObjectManager != null)
                    manager.ClientObjectManager.RegisterPrefab(Definition.unitPrefab.GetComponent<NetworkIdentity>());
                Debug.Log("[Megaplane] Registered " + Key + ", mass " + Definition.mass + " kg, " + Definition.width + " x " + Definition.length + " m");
            }
            catch (Exception e)
            {
                failed = true;
                LastError = e.ToString();
                Debug.LogError("[Megaplane] Registration failed: " + e);
            }
            finally { building = false; }
        }

        private static AircraftDefinition Build(Encyclopedia encyclopedia)
        {
            AircraftDefinition donor = encyclopedia.aircraft.First(d => d != null && (d.jsonKey == "Darkreach" || d.unitName == "SFB-81 Darkreach"));
            holder = new GameObject("MegaplaneTemplates");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            GameObject go = Object.Instantiate(donor.unitPrefab, holder.transform);
            go.name = Key;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.GetComponent<NetworkIdentity>().PrefabHash = PrefabHash;
            Aircraft aircraft = go.GetComponent<Aircraft>();
            AircraftDefinition definition = Object.Instantiate(donor);
            definition.name = Key;
            definition.jsonKey = Key;
            definition.code = "G-240";
            definition.unitName = "G-240 Breakwater";
            definition.description = "A 380 m fission-powered flying carrier and invasion hub. It never lands on the map: it arrives in the air, holds its altitude by itself and turns slowly. Point it with the stick, set speed with the throttle.";
            definition.value = 400f;
            definition.radarSize = 3f;
            definition.manpower = 12;
            definition.width = 380f;
            definition.length = 190f;
            definition.height = 53f;
            definition.spawnOffset = new Vector3(0, 18f, 0);   // belly to datum: an air start is always far above it
            definition.restRotation = Vector3.zero;
            // Turret target scores are multiplied by the OWNER's role: the Darkreach bomber role scores missiles 0, so the
            // ship CIWS and RAM clones would never pick one. A carrier with a ship's point defence has a ship's role.
            Debug.Log("[Megaplane] donor role antiAir " + donor.roleIdentity.antiAir + " antiMissile " + donor.roleIdentity.antiMissile);
            definition.roleIdentity.antiMissile = 1f;
            definition.roleIdentity.antiAir = Mathf.Max(definition.roleIdentity.antiAir, .5f);
            definition.unitPrefab = go;
            definition.aircraftInfo = new AircraftInfo { emptyWeight = Mass * .8f, maxWeight = Mass, maxSpeed = MegaplaneFlight.MaxSpeed * 3.6f, stallSpeed = MegaplaneFlight.MinSpeed * 3.6f, maneuverability = 1f };
            definition.aircraftParameters = Object.Instantiate(donor.aircraftParameters);
            AircraftParameters p = definition.aircraftParameters;
            p.airfoils = donor.aircraftParameters.airfoils;   // nonserialized IDs index native charts
            p.name = Key + "_Parameters";
            p.aircraftName = definition.unitName;
            p.aircraftGLimit = 1.5f;
            p.maxSpeed = MegaplaneFlight.MaxSpeed;
            p.cornerSpeed = 120f;
            p.turningRadius = 4000f;
            p.minimumRadarAlt = 300f;
            ((Unit)aircraft).definition = definition;

            Transform model = MegaplaneArt.Build(go.transform);
            model.name = "MegaplaneVisuals";
            Dictionary<string, Transform> nodes = model.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            RefitAirframe(aircraft, model, nodes);
            bool railgun = MegaplaneSettings.Railgun && nodes.ContainsKey("Railgun_Pitch");
            MegaplaneGuns.Graft(encyclopedia, aircraft, nodes, railgun);
            // The bay holds one payload: the other's art is switched off (its hitboxes go with it).
            foreach (KeyValuePair<string, Transform> n in nodes)
                if (railgun ? n.Key.StartsWith("Trapeze") || n.Key.StartsWith("FighterSlot_") : n.Key.StartsWith("Railgun")) n.Value.gameObject.SetActive(false);
            if (!railgun) MegaplaneBayTemplate.Build(encyclopedia, aircraft, nodes);
            MegaplaneStores.Build(encyclopedia, aircraft, p, nodes);
            go.AddComponent<MegaplaneDefence>();
            definition.mass = Mass;
            go.GetComponent<Rigidbody>().mass = Mass;
            go.AddComponent<MegaplaneFlight>();
            go.AddComponent<MegaplaneGear>();
            return definition;
        }

        // Donor damage island -> art node it moves to (and whose art it carries). Donor chain per side:
        // engine > gearbay > {nacelle, wing1b > {flap, wing1a, wing2 > {wingtip, aileron}}}. Wing panels take the structural
        // links and pods take leaves, so a lost pod is only that pod (MegaplaneFlight counts surviving pods), while a lost
        // panel takes what it carries. The inboard pair shares one island: one nuclear engine group per side.
        private static readonly Dictionary<string, string> Mapping = new Dictionary<string, string>
        {
            { "fuselage_F", "Fuselage_Cockpit" }, { "nose", "Fuselage_Cockpit" }, { "fuselage_R", "Fuselage_Aft" }, { "tail", "BayDoor_L" },
            { "hstab", "Tailplane" }, { "intake_L", "Strongback_L" }, { "intake_R", "Strongback_R" },
            { "engine_L", "Wing_Inner_L" }, { "engine_R", "Wing_Inner_R" }, { "nacelle_L", "Pod_L1" }, { "nacelle_R", "Pod_R1" },
            { "gearbay_L", "Wing_Mid_L" }, { "gearbay_R", "Wing_Mid_R" }, { "wing1b_L", "Wing_Outer_L" }, { "wing1b_R", "Wing_Outer_R" },
            { "flap_L", "Pod_L3" }, { "flap_R", "Pod_R3" }, { "wing1a_L", "Flap_Outer_L" }, { "wing1a_R", "Flap_Outer_R" },
            { "wing2_L", "Aileron_L" }, { "wing2_R", "Aileron_R" }, { "wingtip_L", "Pod_L4" }, { "wingtip_R", "Pod_R4" },
            { "aileron_L", "Aileron_L" }, { "aileron_R", "Aileron_R" }, { "nozzle_L", "Boom_L" }, { "nozzle_R", "Boom_R" },
            { "vstab1_L", "Fin_L" }, { "vstab1_R", "Fin_R" }, { "vstab2_L", "Rudder_L" }, { "vstab2_R", "Rudder_R" },
            { "rudder_L", "Rudder_L" }, { "rudder_R", "Rudder_R" },
        };
        private static readonly Dictionary<string, string> ExtraArt = new Dictionary<string, string> { { "nacelle_L", "Pod_L2" }, { "nacelle_R", "Pod_R2" } };
        // Position-only islands share another island's art.
        private static readonly HashSet<string> NoArt = new HashSet<string> { "nose", "wing2_L", "wing2_R", "vstab2_L", "vstab2_R" };

        private static void RefitAirframe(Aircraft aircraft, Transform model, Dictionary<string, Transform> nodes)
        {
            Transform root = aircraft.transform;
            AeroPart[] parts = root.GetComponentsInChildren<AeroPart>(true);
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(model))) r.enabled = false;
            foreach (LODGroup lod in root.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            int Depth(Transform t) { int d = 0; for (; t != root; t = t.parent) d++; return d; }
            AeroPart[] ordered = parts.Where(p => p.gameObject != aircraft.gameObject).OrderBy(p => Depth(p.transform)).ToArray();
            Transform pilot = root.GetComponentsInChildren<Pilot>(true).Select(x => x.transform).FirstOrDefault();
            Transform cockpit = parts.First(p => p.name == "cockpit").transform;
            for (int pass = 0; pass < 2; pass++)   // pass 2 re-seats the crew on the art's pilot anchor
                foreach (AeroPart part in ordered)
                {
                    part.transform.rotation = root.rotation;
                    if (Mapping.TryGetValue(part.name, out string node)) part.transform.position = nodes[node].position;
                    else if (part.transform == cockpit && pilot != null) part.transform.position = nodes["pilot"].position - (pilot.position - cockpit.position);
                }
            foreach (AeroPart part in parts)
            {
                if (Mapping.TryGetValue(part.name, out string node) && !NoArt.Contains(part.name)) nodes[node].SetParent(part.transform, true);
                if (ExtraArt.TryGetValue(part.name, out string extra)) nodes[extra].SetParent(part.transform, true);
                Set(part, "centerOfLift", Vector3.zero);
                Set(part, "centerOfMass", null);
                part.SetWingArea(0f);   // game-logic flight: no native lift to fight the controller
                if (part.Joints != null)
                    foreach (PartJoint joint in part.Joints)
                        if (joint.anchor != null) joint.anchor.position = part.transform.position;
            }
            float scale = Mass / parts.Sum(part => part.mass);
            foreach (AeroPart part in parts)
            {
                part.mass *= scale;
                Set(part, "baseMass", part.mass);   // parts reset to baseMass on spawn
            }
            foreach (MeshFilter mesh in model.GetComponentsInChildren<MeshFilter>(true))
            {
                AeroPart owner = mesh.GetComponentInParent<AeroPart>(true);
                if (owner == null || mesh.name.Contains("Canopy")) continue;   // glazing: no hull of its own
                var collider = mesh.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh.sharedMesh;
                collider.convex = true;
                mesh.gameObject.layer = owner.gameObject.layer;
                mesh.gameObject.AddComponent<MegaplaneHitbox>().Owner = owner;
            }
            foreach (IGrouping<AeroPart, MegaplaneHitbox> group in aircraft.GetComponentsInChildren<MegaplaneHitbox>(true).GroupBy(h => h.Owner))
            {
                MegaplaneDamageGroup shared = group.Key.gameObject.AddComponent<MegaplaneDamageGroup>();
                foreach (MegaplaneHitbox hitbox in group) { hitbox.Group = shared; hitbox.BlastShare = 1f / group.Count(); }
            }
        }
    }

    /// <summary>Native bullets resolve IDamageable on the hit collider itself: forward to the owning damage island.</summary>
    internal sealed class MegaplaneHitbox : MonoBehaviour, IDamageable
    {
        [SerializeField] internal AeroPart Owner;
        [SerializeField] internal MegaplaneDamageGroup Group;
        [SerializeField] internal float BlastShare = 1f;   // one blast reaches every mesh of an island: split it, as the Skylark does
        // Island hit points are a fixed 0..100 scale (UnitPart.Awake), so toughness divides what arrives instead.
        public void TakeDamage(float pierce, float blast, float affected, float fire, float impact, PersistentID dealer) =>
            Owner.TakeDamage(pierce / HitPointScale, blast / HitPointScale, pierce == 0f && blast > 0f ? affected * BlastShare : affected, fire, impact, dealer);
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
        private const float HitPointScale = 20f;   // a carrier shrugs off what downs a bomber
    }

    // Several art meshes share an island: one shockwave hits it once.
    internal sealed class MegaplaneDamageGroup : MonoBehaviour
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
