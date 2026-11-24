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
    /// <summary>KY-44 Groza flying carrier with systems inherited from the SFB-81 Darkreach.
    /// Motion comes from <see cref="MegaplaneFlight"/> (BOTE-style game-logic flight), not from the donor's aerodynamics.</summary>
    internal static class MegaplaneTemplate
    {
        internal const string Key = "BS_Megaplane";
        // Mirage uses these only for network instantiation. Mission saves use the definition key.
        // Different native damage tables must never receive one another's byte-indexed RPCs.
        private const int NativePartLayout = 2;
        internal const int PrefabHashFighters = 0x42534D50 + 4 * NativePartLayout;
        internal const int PrefabHashRailgun = 0x42534D52 + 4 * NativePartLayout;
        internal static int PrefabHash => MegaplaneSettings.Railgun ? PrefabHashRailgun : PrefabHashFighters;
        internal const float Mass = 6500000f;          // The existing gross mass controls collisions and carried momentum.
        internal static AircraftDefinition Definition { get; private set; }
        internal static string LastError { get; private set; }
        private static GameObject holder;
        private static bool building, failed;
        private static int failures;

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
                // a pack that has not loaded yet leaves no donor: retry on the next AfterLoad rather than latch
                // the failure for the whole session (three strikes, then it stays quiet)
                if (++failures >= 3) failed = true;
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
            definition.code = "KY-44";
            definition.unitName = "KY-44 Groza";
            definition.description = "A fission-powered flying carrier with a 342 m wingspan, eight engine pods and an internal cargo hold. It launches airborne. At low forward speed, lower the gear, add throttle and pull back to open the nose fans and assist the climb. Center the stick to hold altitude.";
            definition.value = 400f;
            definition.radarSize = 3f;
            definition.manpower = 12;
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
            p.StatusDisplay = MegaplaneStatusDisplay.Build(p.StatusDisplay, holder.transform);
            p.airfoils = donor.aircraftParameters.airfoils;   // nonserialized IDs index native charts
            p.name = Key + "_Parameters";
            p.aircraftName = definition.unitName;
            p.aircraftGLimit = 1.5f;
            p.maxSpeed = MegaplaneFlight.MaxSpeed;
            p.cornerSpeed = 120f;
            p.turningRadius = 4000f;
            p.minimumRadarAlt = 300f;
            ((Unit)aircraft).definition = definition;

            // The bomber has ~30 damage islands; the carrier's body and inboard pods need their own. Clones register in
            // Awake like the donor's (hierarchy order of the shared prefab: same ids on every peer).
            AddIsland(go.transform, "body_F", null);     // forward fuselage, y 20..46 design (assets/.../breaks.py)
            AddIsland(go.transform, "hump", null);       // dorsal plateau: silos and barbettes ride on it
            AddIsland(go.transform, "pod1_L", "engine_L");
            AddIsland(go.transform, "pod1_R", "engine_R");
            Transform model = MegaplaneArt.Build(go.transform);
            model.name = "MegaplaneVisuals";
            // BSMP stores the model in metres and bakes rotations/scales into vertices. Include
            // the gear and tip lights in the encyclopedia and formation-spacing dimensions.
            var envelope = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter mesh in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Bounds bounds = mesh.sharedMesh.bounds;
                Vector3 origin = model.InverseTransformPoint(mesh.transform.position);
                envelope.Encapsulate(origin + bounds.min);
                envelope.Encapsulate(origin + bounds.max);
            }
            definition.width = envelope.size.x;
            definition.height = envelope.size.y;
            definition.length = envelope.size.z;
            Dictionary<string, Transform> nodes = model.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            // Append the two optional native parts after the old hierarchy to preserve earlier part indices.
            foreach (string side in new[] { "L", "R" })
                if (nodes.ContainsKey("NoseAssist_" + side)) AddIsland(go.transform, "noseAssist_" + side, null);
            // New root siblings keep the network IDs of all previously shipped parts stable.
            foreach (string island in new[] { "pod4_L", "pod4_R", "reactor", "coolant_L", "coolant_R" })
                AddIsland(go.transform, island, null);
            // These outer skins and control surfaces previously formed part of their support.
            // Append siblings so existing native damage RPC indices stay stable.
            AeroPart center = go.GetComponent<AeroPart>();
            float plateMass = center.mass * .008f;
            foreach (string side in new[] { "L", "R" })
            {
                AeroPart inner = go.GetComponentsInChildren<AeroPart>(true).First(part => part.name == "gearbay_" + side);
                AeroPart flap = AddIsland(go.transform, "innerFlap_" + side, null);
                flap.mass = inner.mass * .09f;
                inner.mass -= flap.mass;
                AeroPart plate = AddIsland(go.transform, "hullPlate_" + side, null);
                plate.mass = plateMass;
                center.mass -= plateMass;
            }
            // These four pieces come out of the flight-deck shell's original mass budget.
            // Append root siblings so the new native hitboxes do not renumber donor parts.
            AeroPart bridge = go.GetComponentsInChildren<AeroPart>(true).First(part => part.name == "fuselage_F");
            float bridgeMass = bridge.mass;
            foreach (var section in MegaplaneForebodyPanel.Sections)
            {
                AeroPart skin = AddIsland(go.transform, section.name, null);
                skin.mass = bridgeMass * section.share;
                bridge.mass -= skin.mass;
                skin.gameObject.AddComponent<MegaplaneForebodyPanel>().SetFace(section.face);
            }
            RefitAirframe(aircraft, model, nodes);
            foreach (string side in new[] { "L", "R" })
                if (nodes.TryGetValue("NoseAssist_" + side, out Transform housing))
                    housing.gameObject.AddComponent<MegaplaneNoseFan>().Bind(nodes, side);
            foreach (var marker in nodes.Where(n => n.Key.StartsWith("Detail_Bay_WorkLight_")))
            {
                marker.Value.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Light lamp = marker.Value.gameObject.AddComponent<Light>();
                lamp.type = LightType.Spot;
                lamp.color = new Color(1f, .87f, .73f);
                lamp.intensity = 2f;
                lamp.range = 13f;
                lamp.spotAngle = 120f;
                lamp.shadows = LightShadows.None;
            }
            FitCockpit(aircraft, nodes);
            MegaplaneLod.Install(nodes);
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
            go.AddComponent<MegaplaneDamage>();
            go.AddComponent<MegaplaneCrash>();
            go.AddComponent<MegaplaneAirframeSeams>();
            go.AddComponent<MegaplaneFx>();
            go.AddComponent<MegaplaneFlightSmoke>();
            go.AddComponent<MegaplaneReactor>();
            go.AddComponent<MegaplaneFlight>();
            go.AddComponent<MegaplaneGear>();
            go.AddComponent<MegaplaneFans>();
            go.AddComponent<MegaplaneSurfaces>();
            go.AddComponent<MegaplaneLights>();
            return definition;
        }

        // Donor damage island -> art node it moves to (and whose art it carries). Donor chain per side:
        // engine > gearbay > {nacelle, wing1b > {flap, wing1a, wing2 > {wingtip, aileron}}}. Wing panels take the structural
        // links and pods take leaves, so a lost pod is only that pod (MegaplaneFlight counts surviving pods), while a lost
        // panel takes what it carries. Each of the eight pods has its own engine-health island.
        private static readonly Dictionary<string, string> Mapping = new Dictionary<string, string>
        {
            { "fuselage_F", "Fuselage_Cockpit" }, { "nose", "Fuselage_Cockpit" }, { "fuselage_R", "Fuselage_Aft" }, { "tail", "Fuselage_TailCone" },
            { "hstab", "Tailplane" }, { "intake_L", "Strongback_L" }, { "intake_R", "Strongback_R" },
            { "engine_L", "Wing_Root_L" }, { "engine_R", "Wing_Root_R" }, { "nacelle_L", "Pod_L2" }, { "nacelle_R", "Pod_R2" },
            { "gearbay_L", "Wing_Inner_L" }, { "gearbay_R", "Wing_Inner_R" }, { "wing1b_L", "Wing_Mid_L" }, { "wing1b_R", "Wing_Mid_R" },
            { "flap_L", "Pod_L3" }, { "flap_R", "Pod_R3" }, { "wing1a_L", "Flap_Outer_L" }, { "wing1a_R", "Flap_Outer_R" },
            { "wing2_L", "Wing_Outer_L" }, { "wing2_R", "Wing_Outer_R" }, { "wingtip_L", "Wing_Tip_L" }, { "wingtip_R", "Wing_Tip_R" },
            { "aileron_L", "Aileron_L" }, { "aileron_R", "Aileron_R" }, { "nozzle_L", "Boom_L" }, { "nozzle_R", "Boom_R" },
            { "vstab1_L", "Fin_L" }, { "vstab1_R", "Fin_R" }, { "vstab2_L", "Rudder_L" }, { "vstab2_R", "Rudder_R" },
            { "rudder_L", "Rudder_L" }, { "rudder_R", "Rudder_R" },
            { "body_F", "Fuselage_Forward" }, { "hump", "Fuselage_Hump" }, { "pod1_L", "Pod_L1" }, { "pod1_R", "Pod_R1" },
            { "noseAssist_L", "NoseAssist_L" }, { "noseAssist_R", "NoseAssist_R" },
            { "pod4_L", "Pod_L4" }, { "pod4_R", "Pod_R4" }, { "reactor", "ReactorCore" },
            { "coolant_L", "CoolantPack_L" }, { "coolant_R", "CoolantPack_R" },
            { "innerFlap_L", "Flap_Inner_L" }, { "innerFlap_R", "Flap_Inner_R" },
            { "hullPlate_L", "HullPlate_L" }, { "hullPlate_R", "HullPlate_R" },
            { "foreCheek_L", "ForeCheek_L" }, { "foreCheek_R", "ForeCheek_R" },
            { "bridgeCrown", "BridgeCrown" }, { "noseChin", "NoseChin" },
        };
        private static readonly Dictionary<string, string> ExtraArt = new Dictionary<string, string>();
        // Position-only islands share another island's art.
        private static readonly HashSet<string> NoArt = new HashSet<string> { "nose", "vstab2_L", "vstab2_R" };

        private static void FitCockpit(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            Transform pilot = nodes["pilot"];
            // The eye sits just forward of the modeled headrest and looks over the glare shield.
            aircraft.cockpitViewPoint.position = pilot.TransformPoint(new Vector3(0f, .26f, .30f));
            // The native camera normally re-enables the donor hull or interior when switching views.
            // Keep only the Groza glazing and cockpit in both views. Its live display is for the seated pilot.
            Set(aircraft, "exteriorRenderers", new Renderer[0]);
            Set(aircraft, "cockpitRenderers", new Renderer[0]);
            Renderer donor = aircraft.cockpit.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "tacScreen");
            if (donor == null || donor.sharedMaterial == null) return;
            var face = new GameObject("GrozaTacticalDisplay");
            face.layer = donor.gameObject.layer;
            bool hasLocator = nodes.TryGetValue("TacticalScreen", out Transform locator);
            face.transform.SetParent(hasLocator ? locator : pilot, false);
            face.transform.localPosition = hasLocator ? new Vector3(0f, 0f, -.003f) : new Vector3(0f, -.52f, 1.126f);
            // Sample the same live tactical-view region as the pilot's Darkreach display.
            var mesh = new Mesh
            {
                name = "GrozaTacticalDisplay",
                vertices = new[] { new Vector3(-.186f, -.18f), new Vector3(-.186f, .18f), new Vector3(.186f, .18f), new Vector3(.186f, -.18f) },
                uv = new[] { new Vector2(0f, .28f), new Vector2(0f, 1f), new Vector2(.574f, 1f), new Vector2(.574f, .28f) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            face.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer display = face.AddComponent<MeshRenderer>();
            display.sharedMaterial = donor.sharedMaterial;
            display.enabled = false;
            Set(aircraft, "cockpitRenderers", new[] { display });
        }

        /// <summary>A new damage island: a copy of the donor's "tail" island stripped to its AeroPart, under `parent`
        /// (an island name, or the airframe root).</summary>
        private static AeroPart AddIsland(Transform root, string name, string parent)
        {
            AeroPart[] parts = root.GetComponentsInChildren<AeroPart>(true);
            AeroPart donor = parts.First(p => p.name == "tail");
            Transform under = parent == null ? root : parts.First(p => p.name == parent).transform;
            GameObject go = Object.Instantiate(donor.gameObject, under);
            go.name = name;
            foreach (Transform child in go.transform.Cast<Transform>().ToList()) Object.DestroyImmediate(child.gameObject);
            foreach (Component c in go.GetComponents<Component>())
                if (c is Collider || c is Renderer || c is MeshFilter || c is FuelTank || c is AudioSource || c is LODGroup) Object.DestroyImmediate(c);
            AeroPart part = go.GetComponent<AeroPart>();
            part.Joints = new PartJoint[0];
            Set(part, "disintegrateObjects", new GameObject[0]);   // pointed at the donor's children, now gone
            // UnitPart applies liveries and damage to this list even when a section has no donor renderer.
            Set(part, "damageMaterial", new DamageMaterial());
            return part;
        }

        private static void RefitAirframe(Aircraft aircraft, Transform model, Dictionary<string, Transform> nodes)
        {
            Transform root = aircraft.transform;
            // Collect the art before it is handed to the islands: afterwards most of it is no longer under the model.
            MeshFilter[] art = model.GetComponentsInChildren<MeshFilter>(true);
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
                Set(part, "criticalPart", MegaplaneDamage.Critical.Contains(part.name));   // wings kill through MegaplaneDamage's lift rule
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
            foreach (MeshFilter mesh in art)
            {
                AeroPart owner = mesh.GetComponentInParent<AeroPart>(true);
                // The centre hull encloses two open compartments. Separate convex strips preserve
                // the upper cover opening and keep the fighter-bay volume clear beneath its deck.
                // The flight deck has backed cutouts under each removable cheek, crown and chin.
                // A single convex shell here would cover the openings and steal their hits.
                if (mesh.name == "Fuselage_Center" || mesh.name == "Fuselage_Cockpit") continue;
                if (mesh.name.StartsWith("ReactorHull_", StringComparison.Ordinal) ||
                    mesh.name.StartsWith("ForeHullProxy_", StringComparison.Ordinal))
                    Object.DestroyImmediate(mesh.GetComponent<MeshRenderer>());
                // Paint, lamps and interior fittings sit on an existing hull. A convex collider over
                // disconnected lamps or decals would invent solid surfaces between them.
                if (owner == null || mesh.name == "Antennas" || mesh.name == "Dorsal_ServiceTrunks" ||
                    mesh.name.Contains("Canopy") || mesh.name.StartsWith("Int_") || mesh.name.StartsWith("FlightDeck_") ||
                    mesh.name.StartsWith("Detail_") || mesh.name.StartsWith("Det_lights_")) continue;
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
        public void TakeDamage(float pierce, float blast, float affected, float fire, float impact, PersistentID dealer)
        {
            pierce /= HitPointScale;
            blast /= HitPointScale;
            // fire (burning wreckage, napalm, DamageParticles) arrives once per hitbox in reach: once per island per tick
            if (fire > 0f && pierce == 0f && blast == 0f && !Group.AcceptFire()) return;
            fire /= HitPointScale;
            if (pierce == 0f && blast > 0f) affected *= BlastShare;
            Owner.TakeDamage(pierce, blast, affected, fire, impact, dealer);
            // This path can complete synchronously after the native damage RPC has been sent.
            if (damage == null) damage = Owner.parentUnit != null ? Owner.parentUnit.GetComponent<MegaplaneDamage>() : null;
            if (damage != null) damage.AfterHit(Owner);
        }
        private MegaplaneDamage damage;
        public void ApplyDamage(float pierce, float blast, float fire, float impact) =>
            // the same toughness as TakeDamage: one entry point must not armour the island 20x weaker than the other
            Owner.ApplyDamage(pierce / HitPointScale, blast / HitPointScale, fire, impact);
        public void TakeShockwave(Vector3 origin, float overpressure, float blastPower)
        {
            if (Group.AcceptShockwave(origin)) Owner.TakeShockwave(origin, overpressure, blastPower);
        }
        public void Detach(Vector3 velocity, Vector3 relativePos) => Owner.Detach(velocity, relativePos);
        public ArmorProperties GetArmorProperties() => Owner.GetArmorProperties();
        public Unit GetUnit() => Owner.GetUnit();
        public float GetMass() => Owner.GetMass();
        public Transform GetTransform() => Owner.GetTransform();
        private const float HitPointScale = 8f;    // a carrier shrugs off what downs a bomber, not what downs a destroyer
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
        private int lastFireFrame = -1;
        // fire arrives once per hitbox in reach (DamageParticles overlap sphere): one hit per island per frame
        internal bool AcceptFire()
        {
            if (lastFireFrame == Time.frameCount) return false;
            lastFireFrame = Time.frameCount;
            return true;
        }
    }
}
