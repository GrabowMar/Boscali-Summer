using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Visuals.Runtime
{
    /// <summary>
    /// Phase-0 spike: injects <see cref="FxSpikeFeature"/> into every live URP renderer.
    /// Decompiled mechanism (URP 14): the runtime <c>ScriptableRenderer</c> copies the data's
    /// features into its own private <c>m_RendererFeatures</c> list at creation and iterates it
    /// every frame, so injection is Create + list.Add on the LIVE renderer — no asset edit, no
    /// SetDirty, no renderer rebuild. Proven in-game: the game runs 4 UniversalRenderers and the
    /// pass executes on real cameras. The injector reconciles every second: renderers are plain
    /// C# objects that get recreated (quality change, asset swap, scene load), so entries missing
    /// from the current set are detached to avoid leaking lists and features. Fail-closed:
    /// any failure logs once and retries. Removed or productized into FxPass in Phase 1.
    /// </summary>
    internal sealed class FxSpikeInjector
    {
        private const int MaxRenderers = 4;

        private readonly List<ScriptableRenderer> injected = new List<ScriptableRenderer>(MaxRenderers);
        private readonly List<FxSpikeFeature> features = new List<FxSpikeFeature>(MaxRenderers);
        private readonly List<ScriptableRenderer> current = new List<ScriptableRenderer>(MaxRenderers);
        private ManualLogSource logger;
        private FieldInfo rendererFeaturesField;
        private FieldInfo rendererDataListField;
        private bool fieldsResolved;
        private float nextAttempt;
        private string lastError = "";

        internal void SetLogger(ManualLogSource logSource)
        {
            logger = logSource;
            FxSpikeFeature.Logger = logSource;
        }

        /// <summary>How many live renderers currently carry the spike (automation readout).</summary>
        internal int InjectedCount => injected.Count;

        internal void Tick(bool enabled)
        {
            if (!enabled)
            {
                RemoveAll("disabled");
                return;
            }
            if (Time.unscaledTime < nextAttempt) return;
            nextAttempt = Time.unscaledTime + 1f;

            try
            {
                var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (pipeline == null) { NoteError("no URP pipeline asset"); return; }
                if (!fieldsResolved)
                {
                    fieldsResolved = true;
                    rendererFeaturesField = typeof(ScriptableRenderer).GetField("m_RendererFeatures",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    rendererDataListField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (rendererFeaturesField == null) { NoteError("m_RendererFeatures not found"); return; }

                current.Clear();
                int rendererCount = RendererCount(pipeline);
                for (int i = 0; i < Math.Min(rendererCount, MaxRenderers); i++)
                {
                    ScriptableRenderer renderer = pipeline.GetRenderer(i);
                    if (renderer != null && !current.Contains(renderer)) current.Add(renderer);
                }
                if (current.Count == 0) { NoteError("no live renderer"); return; }

                // Detach entries whose renderer was recreated since injection.
                for (int i = injected.Count - 1; i >= 0; i--)
                {
                    if (!current.Contains(injected[i])) RemoveAt(i, "renderer recreated");
                }

                for (int i = 0; i < current.Count; i++)
                {
                    if (!injected.Contains(current[i])) Inject(current[i]);
                }
                if (injected.Count > 0 && lastError.Length > 0)
                {
                    lastError = "";
                    logger?.LogInfo("[Visuals] FxSpike: injection healthy on " + injected.Count + " renderer(s).");
                }
            }
            catch (Exception e)
            {
                NoteError(e.GetType().Name + ": " + e.Message);
            }
        }

        internal void Release()
        {
            RemoveAll("released");
        }

        private int RendererCount(UniversalRenderPipelineAsset pipeline)
        {
            if (rendererDataListField == null) return 1;
            try
            {
                var list = rendererDataListField.GetValue(pipeline) as Array;
                return list != null ? list.Length : 1;
            }
            catch (Exception)
            {
                return 1;
            }
        }

        private void Inject(ScriptableRenderer renderer)
        {
            var list = rendererFeaturesField.GetValue(renderer) as List<ScriptableRendererFeature>;
            if (list == null) { NoteError("feature list unreadable"); return; }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is FxSpikeFeature existing && existing != null)
                {
                    // Survived a missed detach (or a hot reload): adopt instead of duplicating.
                    injected.Add(renderer);
                    features.Add(existing);
                    logger?.LogInfo("[Visuals] FxSpike: adopted existing feature on renderer #" +
                        renderer.GetHashCode() + ".");
                    return;
                }
            }
            var feature = ScriptableObject.CreateInstance<FxSpikeFeature>();
            feature.name = "BoscaliFxSpike";
            feature.hideFlags = HideFlags.HideAndDontSave;
            feature.Create();
            list.Add(feature);
            injected.Add(renderer);
            features.Add(feature);
            logger?.LogInfo("[Visuals] FxSpike: injected into renderer #" + renderer.GetHashCode() + ".");
        }

        private void RemoveAt(int index, string reason)
        {
            ScriptableRenderer renderer = injected[index];
            FxSpikeFeature feature = features[index];
            injected.RemoveAt(index);
            features.RemoveAt(index);
            try
            {
                if (renderer != null && rendererFeaturesField != null)
                {
                    var list = rendererFeaturesField.GetValue(renderer) as List<ScriptableRendererFeature>;
                    list?.Remove(feature);
                }
            }
            catch (Exception) { /* Renderer is gone; nothing to detach from. */ }
            try
            {
                if (feature != null)
                {
                    feature.Dispose();
                    UnityEngine.Object.Destroy(feature);
                }
            }
            catch (Exception) { /* Best effort teardown. */ }
            logger?.LogInfo("[Visuals] FxSpike: removed (" + reason + ").");
        }

        private void RemoveAll(string reason)
        {
            for (int i = injected.Count - 1; i >= 0; i--) RemoveAt(i, reason);
        }

        private void NoteError(string error)
        {
            if (error == lastError) return;
            lastError = error;
            logger?.LogWarning("[Visuals] FxSpike: " + error + ".");
        }
    }
}
