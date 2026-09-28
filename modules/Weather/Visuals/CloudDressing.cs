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
        private static readonly FieldInfo DistantSystemField = typeof(CloudLayer).GetField("distantCloudSystem", PrivateInstance);
        private static readonly FieldInfo FlyThroughField = typeof(CloudLayer).GetField("flyThroughSystem", PrivateInstance);
        private static readonly FieldInfo RendererField = typeof(CloudLayer).GetField("cloudRenderer", PrivateInstance);
        private static readonly FieldInfo LightningField = typeof(CloudLayer).GetField("lightning", PrivateInstance);
        private static readonly FieldInfo LightningSystemField = typeof(Lightning).GetField("lightningSystem", PrivateInstance);
        private static readonly FieldInfo LightningFlashField = typeof(Lightning).GetField("flashLight", PrivateInstance);
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
        private readonly Renderer[] hiddenRenderers = new Renderer[5];
        private readonly bool[] rendererWasEnabled = new bool[5];
        private CloudLayer hiddenLayer;
        private LevelInfo hiddenOwner;
        private Behaviour hiddenLightning;
        private Light hiddenFlash;
        private bool nativeHidden, layerWasEnabled, lightningWasEnabled;
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

        internal bool NativeHidden => nativeHidden && hiddenLayer != null;

        internal void SetNativeHidden(LevelInfo level, bool hidden)
        {
            if (nativeHidden && (!hidden || hiddenOwner != level || hiddenLayer == null))
                RestoreNativeVisibility();
            if (!hidden || level == null) return;
            if (!nativeHidden)
            {
                if (LayerField == null || SystemField == null || DistantSystemField == null ||
                    FlyThroughField == null || RendererField == null) return;
                hiddenLayer = LayerField.GetValue(level) as CloudLayer;
                if (hiddenLayer == null) return;
                hiddenOwner = level;
                layerWasEnabled = hiddenLayer.enabled;
                RememberRenderer(0, ParticleRenderer(SystemField, hiddenLayer));
                RememberRenderer(1, ParticleRenderer(DistantSystemField, hiddenLayer));
                RememberRenderer(2, ParticleRenderer(FlyThroughField, hiddenLayer));
                RememberRenderer(3, RendererField.GetValue(hiddenLayer) as Renderer);
                hiddenLightning = LightningField?.GetValue(hiddenLayer) as Behaviour;
                if (hiddenLightning != null)
                {
                    lightningWasEnabled = hiddenLightning.enabled;
                    RememberRenderer(4, ParticleRenderer(LightningSystemField, hiddenLightning));
                    hiddenFlash = LightningFlashField?.GetValue(hiddenLightning) as Light;
                }
                nativeHidden = true;
            }

            // Stop native particle generation and its independent sun/moon cookie writer.
            // LevelInfo still reads this altitude when applying atmospheric lighting.
            hiddenLayer.enabled = false;
            Vector3 position = hiddenLayer.transform.position;
            position.y = Datum.LocalSeaY + level.cloudHeight;
            hiddenLayer.transform.position = position;
            for (int i = 0; i < hiddenRenderers.Length; i++)
                if (hiddenRenderers[i] != null) hiddenRenderers[i].enabled = false;

            // The native async weather loop survives component disable and reactivates
            // distant-cloud/lightning objects. Component suppression survives those toggles.
            if (hiddenLightning != null) hiddenLightning.enabled = false;
            if (hiddenFlash != null) hiddenFlash.enabled = false;
        }

        private void RememberRenderer(int index, Renderer renderer)
        {
            hiddenRenderers[index] = renderer;
            rendererWasEnabled[index] = renderer != null && renderer.enabled;
        }

        private static Renderer ParticleRenderer(FieldInfo field, object owner)
        {
            var particles = field?.GetValue(owner) as ParticleSystem;
            return particles != null ? particles.GetComponent<ParticleSystemRenderer>() : null;
        }

        private void RestoreNativeVisibility()
        {
            for (int i = 0; i < hiddenRenderers.Length; i++)
            {
                if (hiddenRenderers[i] != null) hiddenRenderers[i].enabled = rendererWasEnabled[i];
                hiddenRenderers[i] = null;
            }
            // A flash is a transient event; do not resurrect a flash from before takeover.
            // Re-enabling Lightning gives its own OnEnable/Update control of the light.
            if (hiddenLightning != null) hiddenLightning.enabled = lightningWasEnabled;
            if (hiddenLayer != null) hiddenLayer.enabled = layerWasEnabled;
            hiddenLayer = null;
            hiddenOwner = null;
            hiddenLightning = null;
            hiddenFlash = null;
            nativeHidden = false;
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
