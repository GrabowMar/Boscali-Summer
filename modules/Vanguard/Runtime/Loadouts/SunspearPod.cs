using System;
using System.Collections.Generic;
using System.Text;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Vanguard.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// HEL-60 SUNSPEAR: a runtime clone of vanilla's 120 kW laser mount (laser_EW1) on the ECM / flare / ARM pylons. It stays an
    /// ordinary selectable weapon station, so vanilla replicates the beam, draws the aircraft's PowerSupply and lets the AI fire
    /// it (UseEnergyWeapon). Only the cloned Laser's numbers and body change, never the vanilla prefab.
    /// </summary>
    internal static class SunspearPod
    {
        // No patches: vanilla Laser does everything. ponytail: Laser.AttachToUnit has no PowerSupply null check (vanilla); every
        // fixed-wing and helo prefab has one, so the pod is not guarded. Add a prefix if a PowerSupply-less carrier ever appears.
        public static readonly Type[] PatchTypes = new Type[0];

        private const string Donor = "laser_EW1";
        private static readonly string[] Shells = { "JammingPod1", "ECMPod1" };

        public static void Register(ModuleContext context) => RuntimeStores.Add(new StoreRecipe
        {
            Key = VanguardKeys.SunspearMount,
            Donor = Donor,
            Carriers = new[] { "SpecialFlarePod", "ECMPod1", "ARM1_single", Donor },
            Build = Build,
            AiPicks = true // a vanilla laser to the AI (AIPilotCombatModes.UseEnergyWeapon)
        });

        private static class Fields
        {
            public static readonly AccessTools.FieldRef<Laser, float> Power = AccessTools.FieldRefAccess<Laser, float>("power");
            public static readonly AccessTools.FieldRef<Laser, float> MaxAngle = AccessTools.FieldRefAccess<Laser, float>("maxAngle");
            public static readonly AccessTools.FieldRef<Laser, float> FireDamage = AccessTools.FieldRefAccess<Laser, float>("fireDamage");
            public static readonly AccessTools.FieldRef<Laser, AnimationCurve> Curve = AccessTools.FieldRefAccess<Laser, AnimationCurve>("damageAtRange");
            public static readonly AccessTools.FieldRef<Laser, Renderer> Beam = AccessTools.FieldRefAccess<Laser, Renderer>("beamRenderer");
            public static readonly AccessTools.FieldRef<Laser, Transform> Direction = AccessTools.FieldRefAccess<Laser, Transform>("directionTransform");
            public static readonly AccessTools.FieldRef<Laser, ParticleSystem[]> Muzzle = AccessTools.FieldRefAccess<Laser, ParticleSystem[]>("muzzleParticles");
        }

        private static void Build(WeaponMount mount)
        {
            GameObject root = mount.prefab;
            Laser laser = root.GetComponentInChildren<Laser>(true);
            if (laser == null) throw new InvalidOperationException("laser_EW1 prefab has no Laser");
            var log = new StringBuilder("[Vanguard] SUNSPEAR template before: ").Append(Describe(root));

            // The EW1 prefab may carry a battery and a jammer of its own: this pod only fires, so it must add no capacitance.
            foreach (Countermeasure c in root.GetComponentsInChildren<Countermeasure>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (PowerSupply p in root.GetComponentsInChildren<PowerSupply>(true)) UnityEngine.Object.DestroyImmediate(p);
            mount.countermeasure = mount.radar = false;

            WeaponInfo info = UnityEngine.Object.Instantiate(mount.info);
            info.name = SunspearProfile.Info;
            info.weaponName = SunspearProfile.Name;
            info.shortName = SunspearProfile.ShortName;
            info.description = "Pylon laser pod: a wide turret cone and a 160 kW draw on the aircraft's own power. Burns inbound missiles and light aircraft out to 12 km.";
            info.costPerRound = 0f; // the price is the mount's emptyCost
            info.massPerRound = 0f; // the weight is the mount's emptyMass
            RoleIdentity role = info.effectiveness;
            role.antiMissile = SunspearProfile.AntiMissile;
            role.antiAir = SunspearProfile.AntiAir;
            info.effectiveness = role;
            TargetRequirements need = info.targetRequirements;
            need.maxRange = SunspearProfile.RangeM;
            need.minAlignment = SunspearProfile.MaxAngleDeg;
            info.targetRequirements = need;

            laser.info = info;
            Fields.Power(laser) = SunspearProfile.Power;
            Fields.MaxAngle(laser) = SunspearProfile.MaxAngleDeg;
            Fields.FireDamage(laser) = SunspearProfile.FireDamage;
            Fields.Curve(laser) = AnimationCurve.Linear(0f, 1f, SunspearProfile.RangeM, 0f);

            // The laser's own forward is its cone axis (Laser.FixedUpdate turns directionTransform at most maxAngle off it): face
            // the pylon's forward, or targets ahead fall outside the cone and the station never fires (sim 2026-10-26).
            if (laser.transform != root.transform) laser.transform.rotation = root.transform.rotation;
            Renderer beam = Fields.Beam(laser);
            GameObject art = StoreArt.Replace(root.transform, StoreArt.Sunspear, f => beam != null && f.gameObject == beam.gameObject);
            Transform aperture = art != null ? StoreArt.Joint(art.transform, "Aperture") : null;
            if (aperture != null)
            {
                // Beam, aim and muzzle flash leave the turret's aperture; the ball follows the beam (SunspearTurret, every peer).
                Transform dir = Fields.Direction(laser);
                if (dir != null && dir != root.transform) dir.position = aperture.position;
                if (beam != null && beam.transform != root.transform) beam.transform.position = aperture.position;
                ParticleSystem[] flash = Fields.Muzzle(laser);
                if (flash != null) foreach (ParticleSystem p in flash) if (p != null && p.transform != root.transform) p.transform.position = aperture.position;
                root.AddComponent<SunspearTurret>();
                log.Append("; baked pod, beam at the aperture");
            }
            // Body without the bundle: the pod shell of a vanilla jamming pod when the laser prefab has no body of its own.
            bool hasBody = BodyBounds(root.transform, beam, out Bounds body);
            if (art == null && SunspearProfile.NeedsShell(hasBody, body.size.z))
            {
                WeaponMount shell = null;
                foreach (string key in Shells) if (shell == null) shell = RuntimeStores.FindMount(key);
                if (shell != null && shell.prefab != null) AddShell(root.transform, shell.prefab.transform);
                hasBody = BodyBounds(root.transform, beam, out body);
                log.Append("; pod shell from ").Append(shell != null ? shell.jsonKey : "none");
            }
            if (art == null && hasBody)
            {
                // Beam origin, aim origin and muzzle flash at the pod's nose.
                Vector3 nose = root.transform.TransformPoint(new Vector3(body.center.x, body.center.y, body.max.z));
                Transform dir = Fields.Direction(laser);
                if (dir != null && dir != root.transform) dir.position = nose;
                if (beam != null && beam.transform != root.transform) beam.transform.position = nose;
                ParticleSystem[] muzzle = Fields.Muzzle(laser);
                if (muzzle != null) foreach (ParticleSystem p in muzzle) if (p != null && p.transform != root.transform) p.transform.position = nose;
            }

            WeaponMount pod = RuntimeStores.FindMount("ECMPod1") ?? RuntimeStores.FindMount("JammingPod1");
            if (pod != null) { mount.drag = pod.drag; mount.emptyDrag = pod.emptyDrag; mount.RCS = pod.RCS; mount.emptyRCS = pod.emptyRCS; }
            mount.info = info;
            mount.mountName = SunspearProfile.Name;
            mount.ammo = Mathf.Max(1, mount.ammo);
            mount.emptyMass = mount.mass = SunspearProfile.MassKg;
            mount.emptyCost = SunspearProfile.Cost;

            log.Append("\n  after: ").Append(Describe(root)).Append("\n  body ").Append(hasBody ? body.size.ToString("F2") : "none")
                .Append(", drag/RCS from ").Append(pod != null ? pod.jsonKey : "donor (no pod mount found)")
                .Append(", ammo ").Append(mount.ammo);
            Debug.Log(log.ToString());
        }

        /// <summary>Bounds of the template's mesh renderers (the beam excluded) in the root's space.</summary>
        private static bool BodyBounds(Transform root, Renderer beam, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || (beam != null && f.gameObject == beam.gameObject)) continue;
                Matrix4x4 m = root.worldToLocalMatrix * f.transform.localToWorldMatrix;
                Bounds b = f.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1) * 2f - Vector3.one));
                    if (any) bounds.Encapsulate(corner); else { bounds = new Bounds(corner, Vector3.zero); any = true; }
                }
            }
            return any;
        }

        /// <summary>Copies a prefab's meshes and materials (nothing else: no weapons, joints or colliders) under the template root.</summary>
        private static void AddShell(Transform root, Transform source)
        {
            foreach (MeshFilter f in source.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer r = f.GetComponent<MeshRenderer>();
                if (f.sharedMesh == null || r == null) continue;
                Matrix4x4 m = source.worldToLocalMatrix * f.transform.localToWorldMatrix;
                var part = new GameObject("SunspearShell_" + f.name) { layer = f.gameObject.layer };
                part.transform.SetParent(root, false);
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = f.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
            }
        }

        private static string Describe(GameObject root)
        {
            var counts = new SortedDictionary<string, int>();
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                string name = c == null ? "missing" : c.GetType().Name;
                counts[name] = counts.TryGetValue(name, out int n) ? n + 1 : 1;
            }
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, int> pair in counts) sb.Append(pair.Key).Append('x').Append(pair.Value).Append(' ');
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>Every peer, visual only: the HEL-60's turret ball looks where the beam points, and the beam keeps leaving its aperture.</summary>
    internal sealed class SunspearTurret : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<Laser, Transform> Direction = AccessTools.FieldRefAccess<Laser, Transform>("directionTransform");
        private Transform turret, aperture, dir;
        private Quaternion rest;

        private void Awake()
        {
            Laser laser = GetComponentInChildren<Laser>(true);
            Transform art = transform.Find("StoreArt_" + StoreArt.Sunspear);
            turret = art != null ? StoreArt.Joint(art, "Turret") : null;
            aperture = art != null ? StoreArt.Joint(art, "Aperture") : null;
            dir = laser != null ? Direction(laser) : null;
            if (turret == null || dir == null) { enabled = false; return; }
            rest = Quaternion.Inverse(transform.rotation) * turret.rotation; // the ball's pose in pod space, aperture to the nose
        }

        private void LateUpdate()
        {
            // The laser turns directionTransform toward its target in FixedUpdate; idle, it faces the pod's nose.
            turret.rotation = Quaternion.FromToRotation(transform.forward, dir.forward) * transform.rotation * rest;
            if (aperture != null) dir.position = aperture.position;
        }
    }
}
