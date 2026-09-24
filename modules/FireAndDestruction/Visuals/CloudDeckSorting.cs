using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Per-pool copies of vanilla particle materials queued behind the cloud deck
    /// (<see cref="CloudDeckPolicy"/>). One copy per source material, destroyed on clear.
    /// </summary>
    internal sealed class CloudDeckSorting
    {
        private readonly Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
        private readonly Dictionary<Material, Material> sources = new Dictionary<Material, Material>();

        /// <summary>
        /// Re-points <paramref name="systems"/> when the deck starts or stops lying between the
        /// camera and <paramref name="plume"/>; returns the new state for the caller to keep.
        /// </summary>
        public bool Sync(ParticleSystem[] systems, Vector3 plume, bool behindDeck)
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (systems == null || level == null || cameras == null) return behindDeck;
            // Same plane height and camera the vanilla CloudLayer uses for its own occlusion.
            bool behind = CloudDeckPolicy.IsBehindDeck(
                cameras.transform.position.y, plume.y, Datum.LocalSeaY + level.cloudHeight);
            if (behind == behindDeck) return behind;

            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystemRenderer renderer = systems[i] != null
                    ? systems[i].GetComponent<ParticleSystemRenderer>()
                    : null;
                Material current = renderer != null ? renderer.sharedMaterial : null;
                if (current == null) continue;
                Material target = behind ? BehindDeck(current) : Vanilla(current);
                if (target != current) renderer.sharedMaterial = target;
            }
            return behind;
        }

        public void Clear()
        {
            foreach (Material copy in copies.Values)
                if (copy != null) UnityEngine.Object.Destroy(copy);
            copies.Clear();
            sources.Clear();
        }

        private Material BehindDeck(Material material)
        {
            if (sources.ContainsKey(material)) return material;
            Material copy;
            if (!copies.TryGetValue(material, out copy) || copy == null)
            {
                copy = new Material(material)
                {
                    name = material.name + " (behind cloud deck)",
                    renderQueue = CloudDeckPolicy.BehindDeckQueue
                };
                copies[material] = copy;
                sources[copy] = material;
            }
            return copy;
        }

        private Material Vanilla(Material material)
        {
            Material source;
            return sources.TryGetValue(material, out source) && source != null ? source : material;
        }
    }
}
