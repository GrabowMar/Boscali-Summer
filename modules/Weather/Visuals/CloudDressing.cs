using System.Reflection;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Visuals
{
    /// <summary>
    /// Client-local tuning of the game's cloud layer. Also keeps it as a visual fallback
    /// when the optional volumetric render pass is unavailable.
    /// </summary>
    internal sealed class CloudDressing
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo LayerField = typeof(LevelInfo).GetField("cloudLayer", PrivateInstance);
        private static readonly FieldInfo SystemField = typeof(CloudLayer).GetField("cloudSystem", PrivateInstance);
        private static readonly FieldInfo FlyThroughField = typeof(CloudLayer).GetField("flyThroughSystem", PrivateInstance);
        private static readonly FieldInfo SizeMinField = typeof(CloudLayer).GetField("cloudSizeMin", PrivateInstance);
        private static readonly FieldInfo SizeMaxField = typeof(CloudLayer).GetField("cloudSizeMax", PrivateInstance);
        private static readonly FieldInfo MapScaleField = typeof(CloudLayer).GetField("densityMapScale", PrivateInstance);
        private static readonly FieldInfo ThicknessField = typeof(CloudLayer).GetField("layerThickness", PrivateInstance);
        private static readonly FieldInfo ParticleLimitField = typeof(CloudLayer).GetField("maxParticles", PrivateInstance);

        private CloudLayer layer;
        private LevelInfo owner;
        private ParticleSystem system;
        private LevelInfo observedOwner;
        private ParticleSystem flyThrough;
        private ParticleSystemRenderer hiddenRenderer;
        private LevelInfo hiddenOwner;
        private bool rendererWasEnabled;
        private float sizeMin, sizeMax, mapScale, thickness;
        private float writtenSizeMin, writtenSizeMax, writtenMapScale, writtenThickness;
        private int particleLimit;
        private bool applied;

        internal bool Applied => applied;

        internal void Apply(LevelInfo level) => Apply(level, 0f, 0f);

        internal void Apply(LevelInfo level, float frontCover, float cellCore)
        {
            if (applied && owner == level && layer != null)
            {
                UpdateShape(frontCover, cellCore);
                return;
            }
            Restore();
            if (level == null || LayerField == null || SystemField == null || SizeMinField == null ||
                SizeMaxField == null || MapScaleField == null || ThicknessField == null || ParticleLimitField == null)
                return;

            layer = LayerField.GetValue(level) as CloudLayer;
            if (layer == null) return;
            system = SystemField.GetValue(layer) as ParticleSystem;
            if (system == null) { layer = null; return; }

            sizeMin = (float)SizeMinField.GetValue(layer);
            sizeMax = (float)SizeMaxField.GetValue(layer);
            mapScale = (float)MapScaleField.GetValue(layer);
            thickness = (float)ThicknessField.GetValue(layer);
            particleLimit = (int)ParticleLimitField.GetValue(layer);
            if (sizeMin <= 0f || sizeMax < sizeMin || mapScale <= 0f || thickness <= 0f || particleLimit <= 0)
            {
                layer = null;
                system = null;
                return;
            }

            // Broader fair-weather shapes; fronts and cell cores build deeper cloud masses.
            UpdateShape(frontCover, cellCore);
            ParticleLimitField.SetValue(layer, Mathf.Max(1, Mathf.RoundToInt(particleLimit * 0.6f)));
            owner = level;
            applied = true;
            // CloudLayer recalculates this only when the graphics cloud-detail setting changes.
            var main = system.main;
            main.maxParticles = ScaledLimit((int)ParticleLimitField.GetValue(layer));
        }

        internal void Restore()
        {
            SetNativeHidden(null, false);
            if (!applied) return;
            applied = false;
            if (layer != null)
            {
                RestoreFloat(SizeMinField, writtenSizeMin, sizeMin);
                RestoreFloat(SizeMaxField, writtenSizeMax, sizeMax);
                RestoreFloat(MapScaleField, writtenMapScale, mapScale);
                RestoreFloat(ThicknessField, writtenThickness, thickness);
                int reduced = Mathf.Max(1, Mathf.RoundToInt(particleLimit * 0.6f));
                if ((int)ParticleLimitField.GetValue(layer) == reduced)
                {
                    ParticleLimitField.SetValue(layer, particleLimit);
                    if (system != null)
                    {
                        var main = system.main;
                        if (main.maxParticles == ScaledLimit(reduced))
                            main.maxParticles = ScaledLimit(particleLimit);
                    }
                }
            }
            layer = null;
            owner = null;
            system = null;
            writtenSizeMin = writtenSizeMax = writtenMapScale = writtenThickness = 0f;
        }

        internal bool NativeHidden => hiddenRenderer != null;

        internal void SetNativeHidden(LevelInfo level, bool hidden)
        {
            if (hiddenRenderer != null && (!hidden || hiddenOwner != level))
            {
                if (hiddenRenderer != null) hiddenRenderer.enabled = rendererWasEnabled;
                hiddenRenderer = null;
                hiddenOwner = null;
            }
            if (!hidden || hiddenRenderer != null || level == null || LayerField == null || SystemField == null)
                return;
            CloudLayer cloudLayer = LayerField.GetValue(level) as CloudLayer;
            ParticleSystem nativeSystem = cloudLayer != null ? SystemField.GetValue(cloudLayer) as ParticleSystem : null;
            var renderer = nativeSystem != null ? nativeSystem.GetComponent<ParticleSystemRenderer>() : null;
            if (renderer == null) return;
            rendererWasEnabled = renderer.enabled;
            renderer.enabled = false;
            hiddenRenderer = renderer;
            hiddenOwner = level;
        }

        // Native CloudLayer already samples its cloud mask and altitude for this emitter.
        // Reuse that result for cockpit moisture instead of sampling textures again.
        internal bool InCloud(LevelInfo level)
        {
            if (level == null || LayerField == null || FlyThroughField == null) return false;
            if (observedOwner != level || flyThrough == null)
            {
                observedOwner = level;
                CloudLayer observed = LayerField.GetValue(level) as CloudLayer;
                flyThrough = observed != null ? FlyThroughField.GetValue(observed) as ParticleSystem : null;
            }
            return flyThrough != null && flyThrough.isPlaying;
        }

        private void UpdateShape(float frontCover, float cellCore)
        {
            float front = Mathf.Clamp01(frontCover);
            float core = Mathf.Clamp01(cellCore);
            WriteFloat(SizeMinField, ref writtenSizeMin, sizeMin * (1.8f + 0.3f * front + 0.4f * core));
            WriteFloat(SizeMaxField, ref writtenSizeMax, sizeMax * (1.8f + 0.5f * front + 0.7f * core));
            WriteFloat(MapScaleField, ref writtenMapScale, mapScale * (1.65f + 0.15f * front));
            WriteFloat(ThicknessField, ref writtenThickness, thickness * (1.35f + 0.35f * front + 0.55f * core));
        }

        private void WriteFloat(FieldInfo field, ref float written, float value)
        {
            if (Mathf.Abs(written - value) < 0.01f) return;
            field.SetValue(layer, value);
            written = value;
        }

        private void RestoreFloat(FieldInfo field, float written, float original)
        {
            if (Mathf.Approximately((float)field.GetValue(layer), written)) field.SetValue(layer, original);
        }

        private static int ScaledLimit(int limit) => Mathf.Max(1,
            (int)(limit * Mathf.Sqrt(Mathf.Clamp(PlayerSettings.graphics.CloudDetail, 0.1f, 1f))));
    }
}
