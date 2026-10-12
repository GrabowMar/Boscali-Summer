using BoscaliSummer.Core.Math;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// Owns the glow of the native sun disc. LevelInfo rewrites it once a second as white x 1,000,000 x sin(elevation):
    /// the bloom turns that into a white ball a quarter of the screen wide, white at sunset, gone before the horizon,
    /// and bright enough to burn through any cloud the composite leaves a thousandth open. Here the disc is a compact
    /// sun, the air reddens and dims it and cloud on the line of sight puts it out. Written every LateUpdate, after
    /// the native write, so there is nothing to restore: the game takes it back within a second.
    /// </summary>
    internal sealed class SunDisc
    {
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        // Tuned in game against the native bloom (threshold 1.3, intensity 0.5): at 1,000,000 the glow is about 25
        // degrees wide, at 20,000 about 6, at 300 the bare disc. 12,000 leaves a dazzling sun with a tight halo.
        private const float Peak = 12000f;

        private GameObject owner;
        private Material material;
        private float open = 1f;

        /// <summary>Automation: the glow written this frame.</summary>
        internal Color Shown { get; private set; }

        internal void Tick(LevelInfo level, Camera camera, float cloudTransmission, float dt)
        {
            GameObject disc = level != null ? level.sunObject : null;
            if (disc == null || level.sun == null || camera == null) return;
            if (disc != owner)
            {
                owner = disc;
                var renderer = disc.GetComponent<MeshRenderer>();
                material = renderer != null ? renderer.sharedMaterial : null;
                open = Mathf.Clamp01(cloudTransmission);
            }
            if (material == null || !disc.activeInHierarchy) return;

            float elevation = Mathf.Asin(Mathf.Clamp(-level.sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            SunDiscMath.Transmission(elevation, camera.transform.GlobalPosition().y, out float r, out float g, out float b);
            // The transmission is tuned for lighting (half a kilometre of cloud passes a third). A source thousands of
            // times brighter than the sky needs the real optical depth, about eight times that. Eased: the line of sight
            // is re-measured about ten times a second.
            float through = Mathf.Clamp01(cloudTransmission);
            through *= through; through *= through; through *= through;
            open = Mathf.MoveTowards(open, through, 6f * dt);
            float scale = Peak * SunDiscMath.AboveHorizon(elevation) * open;
            Shown = new Color(r * scale, g * scale, b * scale, 1f);
            material.SetColor(EmissionId, Shown);
        }
    }
}
