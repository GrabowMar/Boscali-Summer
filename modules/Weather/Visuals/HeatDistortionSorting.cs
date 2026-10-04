using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// Keeps vanilla jet-exhaust heat haze from erasing the cloud deck. The <c>HeatDistortion</c>
    /// material sits at queue 2975 and refracts the camera's opaque copy, which holds no clouds,
    /// so the haze paints cloud-free ground over the deck plane (2958) as a hole behind every
    /// engine. Queued at 2957 it refracts first and the deck draws over it. Originals are put
    /// back on <see cref="Restore"/>.
    /// </summary>
    internal sealed class HeatDistortionSorting
    {
        private const string MaterialName = "HeatDistortion";
        private const int BeforeDeckQueue = 2957;
        // The haze material is a shared asset that loads with an airframe prefab, so new ones
        // show up when a mission starts or a new aircraft type first spawns, not at random.
        // Scan on a short ladder after each scene load (the haze spawns with the first
        // aircraft), then settle to a slow sweep; FindObjectsOfTypeAll allocates and hitches.
        private static readonly float[] LadderSeconds = { 0f, 4f, 12f, 30f, 60f };
        private const float RescanSeconds = 120f;
        private const int MaxMaterials = 16;

        private readonly Dictionary<Material, int> originals = new Dictionary<Material, int>();
        private float nextScan;
        private int ladderStep;
        private float ladderStart = -1f;
        private int sceneHandle = int.MinValue;

        /// <summary>Re-queues any not-yet-seen haze material; scans at most once per interval.</summary>
        public void Update(float now)
        {
            int handle = SceneManager.GetActiveScene().handle;
            if (handle != sceneHandle)
            {
                sceneHandle = handle;
                ladderStep = 0;
                ladderStart = now;
                nextScan = now;
            }
            if (now < nextScan || originals.Count >= MaxMaterials) return;
            nextScan = ladderStep < LadderSeconds.Length - 1
                ? ladderStart + LadderSeconds[++ladderStep]
                : now + RescanSeconds;

            Material[] all = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < all.Length && originals.Count < MaxMaterials; i++)
            {
                Material material = all[i];
                if (material == null || material.name != MaterialName || originals.ContainsKey(material)) continue;
                originals[material] = material.renderQueue;
                material.renderQueue = BeforeDeckQueue;
            }
        }

        public void Restore()
        {
            foreach (KeyValuePair<Material, int> entry in originals)
                if (entry.Key != null) entry.Key.renderQueue = entry.Value;
            originals.Clear();
            nextScan = 0f;
            sceneHandle = int.MinValue;
        }
    }
}
