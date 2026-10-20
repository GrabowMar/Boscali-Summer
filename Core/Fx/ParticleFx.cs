using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Core.Fx
{
    /// <summary>
    /// Authored particle prefabs (fx-authoring project, baked by tools/Compile-ParticleFxAssets.ps1 into the embedded
    /// particles.bundle). <see cref="Spawn"/> drops one under <c>Datum.origin</c> at a world point, so floating-origin
    /// shifts carry it like the code-built effects, and removes it when its longest system ends. Callers keep their own
    /// effect as the fallback: no bundle or no prefab of that id simply returns null. Cosmetic, local to this peer.
    /// </summary>
    internal static class ParticleFx
    {
        private const string Resource = "BoscaliSummer.Fx.particles.bundle";
        private static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private static bool attempted;

        /// <summary>Spawn prefab <paramref name="id"/> at a world (origin-relative) point; <paramref name="scale"/> scales the whole effect.
        /// A <paramref name="parent"/> (e.g. the aircraft) carries the effect with it instead of leaving it on the ground.</summary>
        public static GameObject Spawn(string id, Vector3 world, Quaternion rotation, float scale = 1f, Transform parent = null)
        {
            GameObject prefab = Prefab(id);
            if (prefab == null || Datum.origin == null) return null;
            GameObject go = UnityEngine.Object.Instantiate(prefab, parent != null ? parent : Datum.origin, false);
            go.transform.SetPositionAndRotation(world, rotation);
            go.transform.localScale = prefab.transform.localScale * scale;
            float life = 0f;
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                if (main.simulationSpace == ParticleSystemSimulationSpace.World)
                {
                    // World space would leave particles behind on an origin shift: simulate relative to the origin instead.
                    main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                    main.customSimulationSpace = Datum.origin;
                }
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                life = Mathf.Max(life, main.duration + main.startDelay.constantMax + main.startLifetime.constantMax);
            }
            if (life > 0f) UnityEngine.Object.Destroy(go, life + 0.5f);
            return go;
        }

        public static bool Has(string id) => Prefab(id) != null;

        private static GameObject Prefab(string id)
        {
            if (prefabs.TryGetValue(id, out GameObject found)) return found;
            if (attempted) return null;
            attempted = true;
            try
            {
                byte[] bytes = EmbeddedResources.ReadAll(typeof(ParticleFx).Assembly, Resource, 64 * 1024 * 1024);
                AssetBundle bundle = bytes != null ? AssetBundle.LoadFromMemory(bytes) : null;
                if (bundle == null) return null;
                foreach (GameObject go in bundle.LoadAllAssets<GameObject>())
                {
                    // Bundled materials point at the game's own URP particle shaders by name: the build's copy may not match its variants.
                    // Assigning a shader resets the queue to the shader's 2000; keep the authored 3000/3001 or the quads draw
                    // opaque: invisible against the sky and cut holes in the weather clouds.
                    foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                        foreach (Material m in r.sharedMaterials)
                            if (m != null && m.shader != null && Shader.Find(m.shader.name) is Shader native)
                            {
                                int queue = m.renderQueue;
                                m.shader = native;
                                m.renderQueue = queue;
                            }
                    prefabs[go.name] = go;
                }
                Debug.Log("[Fx] particles.bundle: " + prefabs.Count + " prefab(s)");
            }
            catch (Exception error) { Debug.LogError("[Fx] particles.bundle: " + error.Message); }
            prefabs.TryGetValue(id, out found);
            return found;
        }
    }
}
