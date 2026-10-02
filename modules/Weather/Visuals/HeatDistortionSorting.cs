using System.Collections.Generic;
using UnityEngine;

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
        private const float RescanSeconds = 30f;
        private const int MaxMaterials = 16;

        private readonly Dictionary<Material, int> originals = new Dictionary<Material, int>();
        private float nextScan;

        /// <summary>Re-queues any not-yet-seen haze material; scans at most once per interval.</summary>
        public void Update(float now)
        {
            if (now < nextScan) return;
            nextScan = now + RescanSeconds;

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
        }
    }
}
