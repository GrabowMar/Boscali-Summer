using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// Emissive tint for the lamps of one SKYWELL joint: every renderer under the joint's DIRECT children named Glow*
    /// (LOD siblings hang beneath them). A MaterialPropertyBlock, never the shared Glow material; updated only on change.
    /// </summary>
    internal sealed class SkywellGlow
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private readonly Renderer[] renderers;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private Color lastColor;
        private float lastIntensity;
        private bool tinted;

        public SkywellGlow(Transform joint)
        {
            var found = new List<Renderer>();
            if (joint != null)
                foreach (Transform child in joint)
                    if (child.name.StartsWith("Glow", StringComparison.Ordinal)) found.AddRange(child.GetComponentsInChildren<Renderer>(true));
            renderers = found.ToArray();
        }

        public void Set(Color color, float intensity)
        {
            if (renderers.Length == 0) return;
            if (tinted && Mathf.Abs(intensity - lastIntensity) <= 0.02f &&
                Mathf.Abs(color.r - lastColor.r) + Mathf.Abs(color.g - lastColor.g) + Mathf.Abs(color.b - lastColor.b) <= 0.02f) return;
            lastColor = color;
            lastIntensity = intensity;
            tinted = true;
            block.Clear();
            block.SetColor(BaseColor, new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f, 1f));
            block.SetColor(EmissionColor, color * intensity);
            foreach (Renderer r in renderers)
                if (r != null) r.SetPropertyBlock(block);
        }

        /// <summary>Back to the material's own colour.</summary>
        public void Clear()
        {
            if (!tinted) return;
            tinted = false;
            foreach (Renderer r in renderers)
                if (r != null) r.SetPropertyBlock(null);
        }
    }
}
