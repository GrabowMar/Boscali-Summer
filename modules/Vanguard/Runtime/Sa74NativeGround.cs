using System;
using System.Collections.Generic;
using HarmonyLib;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Presentation;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Scales the cloned vanilla soot spawner; its native Start/EffectManager still own projection and lifetime.</summary>
    internal static class Sa74NativeGround
    {
        private static readonly AccessTools.FieldRef<Shockwave, float> Yield = AccessTools.FieldRefAccess<Shockwave, float>("yieldKilotons");
        private static readonly AccessTools.FieldRef<DecalSpawner, float> Size = AccessTools.FieldRefAccess<DecalSpawner, float>("decalSize");
        private static readonly AccessTools.FieldRef<DecalSpawner, Material> MaterialRef = AccessTools.FieldRefAccess<DecalSpawner, Material>("decalMaterial");
        internal static int ConfiguredCount { get; private set; }
        internal static float ConfiguredDiameter { get; private set; }
        internal static float SourceYieldKilotons { get; private set; }
        private static Material soot;
        internal static Material SootMaterial
        {
            get
            {
                if (soot != null) return soot;
                foreach (DecalSpawner spawner in Resources.FindObjectsOfTypeAll<DecalSpawner>())
                {
                    Material material = MaterialRef(spawner);
                    if (material != null && material.name.IndexOf("scorch", StringComparison.OrdinalIgnoreCase) >= 0)
                        return soot = material;
                }
                return null;
            }
        }

        /// <summary>Call immediately after Instantiate, before SetOwner changes the native reference yield.</summary>
        internal static void Prepare(GameObject blast, float targetKilotons, bool groundBurst)
        {
            if (blast == null || targetKilotons <= 0f) return;
            Shockwave source = blast.GetComponentInChildren<Shockwave>(true);
            float originalKilotons = source != null ? Yield(source) : 0f;
            if (originalKilotons <= 0f) return;
            float scale = Mathf.Pow(targetKilotons / originalKilotons, 1f / 3f);
            foreach (DecalSpawner spawner in blast.GetComponentsInChildren<DecalSpawner>(true))
            {
                Material material = MaterialRef(spawner);
                // The shipped 250 kt effect carries decalSize=6000 and the native "scorchmark" material.
                if (material == null || material.name.IndexOf("scorch", StringComparison.OrdinalIgnoreCase) < 0) continue;
                soot = material;
                // A surface-water or airborne visual donor must not paint a dry scorch disk on a distant seabed.
                if (!groundBurst) { spawner.enabled = false; continue; }
                Size(spawner) *= scale;
                ConfiguredCount++;
                ConfiguredDiameter = Mathf.Max(ConfiguredDiameter, Size(spawner));
                SourceYieldKilotons = originalKilotons;
            }
        }

        internal static void Reset()
        {
            ConfiguredCount = 0;
            ConfiguredDiameter = SourceYieldKilotons = 0f;
            soot = null;
        }
    }

    /// <summary>Retires only cloned native presentation; Shockwave continues its authoritative damage updates.</summary>
    [DefaultExecutionOrder(2050)]
    internal sealed class Sa74NativeGroundLifetime : MonoBehaviour
    {
        private DecalProjector dust;
        private Renderer vapor;
        private Renderer[] mushroomVisuals;
        private ParticleSystem[] mushroomParticles;
        private Renderer[] rings;
        private bool[] ringsSuppressed, ringsWereVisible;
        private Renderer[] supports;
        private bool[] supportSuppressed, supportWasVisible;
        private ParticleSystemForceField[] updrafts;
        private bool[] updraftSuppressed, updraftWasEnabled;
        private GameObject dustObject;
        private uint siteId;
        private float created;
        private bool vaporSuppressed, vaporWasVisible;

        internal void Initialize(Shockwave shockwave, uint id)
        {
            siteId = id; created = Time.time;
            mushroomVisuals = GetComponentsInChildren<Renderer>(true);
            mushroomParticles = GetComponentsInChildren<ParticleSystem>(true);
            var support = new List<Renderer>();
            foreach (Renderer renderer in mushroomVisuals)
                if (renderer.name == "BaseCloud" || renderer.name == "stemParticles" || renderer.name == "stemTop") support.Add(renderer);
            updrafts = GetComponentsInChildren<ParticleSystemForceField>(true);
            updraftSuppressed = new bool[updrafts.Length]; updraftWasEnabled = new bool[updrafts.Length];
            var condensation = new List<Renderer>();
            foreach (MushroomCloud mushroom in GetComponentsInChildren<MushroomCloud>(true))
            {
                // The authored cap has both a MeshRenderer and a separate particle renderer. Hide only its actual serialized cloudParticles receiver.
                object cloudOwner = AccessTools.Field(typeof(MushroomCloud), "cloud")?.GetValue(mushroom);
                ParticleSystem capParticles = cloudOwner != null ? AccessTools.Field(cloudOwner.GetType(), "cloudParticles")?.GetValue(cloudOwner) as ParticleSystem : null;
                ParticleSystemRenderer capRenderer = capParticles != null ? capParticles.GetComponent<ParticleSystemRenderer>() : null;
                if (capRenderer != null && !support.Contains(capRenderer)) support.Add(capRenderer);
                if (!(AccessTools.Field(typeof(MushroomCloud), "cloudRings")?.GetValue(mushroom) is Array authored)) continue;
                foreach (object ring in authored)
                    if (ring != null && AccessTools.Field(ring.GetType(), "renderer")?.GetValue(ring) is Renderer renderer)
                        condensation.Add(renderer);
            }
            supports = support.ToArray();
            supportSuppressed = new bool[supports.Length]; supportWasVisible = new bool[supports.Length];
            rings = condensation.ToArray();
            ringsSuppressed = new bool[rings.Length]; ringsWereVisible = new bool[rings.Length];
            if (shockwave == null) return;
            dustObject = AccessTools.Field(typeof(Shockwave), "groundDecal")?.GetValue(shockwave) as GameObject;
            dust = dustObject != null ? dustObject.GetComponent<DecalProjector>() : null;
            GameObject cloud = AccessTools.Field(typeof(Shockwave), "vaporCloud")?.GetValue(shockwave) as GameObject;
            vapor = cloud != null ? cloud.GetComponent<Renderer>() : null;
        }

        private void LateUpdate()
        {
            float age = Time.time - created;
            foreach (Sa74Fallout.Site site in Sa74Fallout.Sites)
                if (site.Id == siteId) { age = Mathf.Max(0f, Time.time - site.Started); break; }
            bool retired = age > Sa74BlastShape.ArrivalSeconds(Sa74Fallout.ShockRadius) + 15f;
            if (retired && dust != null) dust.enabled = false;
            if (age >= Sa74BlastShape.MaximumVisualAge)
            {
                foreach (Renderer renderer in mushroomVisuals) if (renderer != null) renderer.enabled = false;
                foreach (ParticleSystem particles in mushroomParticles)
                    if (particles != null && particles.isPlaying) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            bool replaced = Sa74Mushroom.DrawsSite(siteId);
            bool supportHidden = replaced || age >= Sa74BlastShape.MaximumVisualAge;
            for (int i = 0; i < supports.Length; i++)
            {
                Renderer renderer = supports[i];
                if (renderer == null) continue;
                if (supportHidden && !supportSuppressed[i])
                {
                    supportWasVisible[i] = renderer.enabled; renderer.enabled = false; supportSuppressed[i] = true;
                }
                else if (!supportHidden && supportSuppressed[i])
                {
                    renderer.enabled = supportWasVisible[i]; supportSuppressed[i] = false;
                }
            }
            for (int i = 0; i < updrafts.Length; i++)
            {
                ParticleSystemForceField force = updrafts[i];
                if (force == null) continue;
                if (supportHidden && !updraftSuppressed[i])
                {
                    updraftWasEnabled[i] = force.enabled; force.enabled = false; updraftSuppressed[i] = true;
                }
                else if (!supportHidden && updraftSuppressed[i])
                {
                    force.enabled = updraftWasEnabled[i]; updraftSuppressed[i] = false;
                }
            }
            bool hidden = retired || replaced || Sa74Particles.DrawsPressure(siteId);
            // Stock condensation rings are pressure dressing too; their native clock must not leave young white disks in aged state.
            for (int i = 0; i < rings.Length; i++)
            {
                Renderer ring = rings[i];
                if (ring == null) continue;
                if (hidden && !ringsSuppressed[i])
                {
                    ringsWereVisible[i] = ring.enabled; ring.enabled = false; ringsSuppressed[i] = true;
                }
                else if (!hidden && ringsSuppressed[i])
                {
                    ring.enabled = ringsWereVisible[i]; ringsSuppressed[i] = false;
                }
            }
            if (vapor == null) return;
            if (hidden && !vaporSuppressed)
            {
                vaporWasVisible = vapor.enabled;
                vapor.enabled = false; vaporSuppressed = true;
            }
            else if (!hidden && vaporSuppressed)
            {
                vapor.enabled = vaporWasVisible; vaporSuppressed = false;
            }
        }

        private void OnDestroy()
        {
            // Native Start reparents this projector under Datum, outside the cloned explosion root.
            if (dustObject != null) Destroy(dustObject);
            if (mushroomParticles == null || Datum.origin == null) return;
            foreach (ParticleSystem particles in mushroomParticles)
            {
                if (particles == null) continue;
                Transform frame = particles.main.customSimulationSpace;
                if (frame != null && frame != Datum.origin && frame.parent == Datum.origin && frame.childCount == 0)
                    Destroy(frame.gameObject); // Empty stem simulation frame created by native MushroomCloud.Start.
            }
        }
    }
}
