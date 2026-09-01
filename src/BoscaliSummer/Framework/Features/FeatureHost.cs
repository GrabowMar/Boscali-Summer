using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using BoscaliSummer.Framework.Lifecycle;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Framework.Features
{
    internal sealed class FeatureHost : IDisposable
    {
        private sealed class LoadedFeature
        {
            public IModFeature Feature;
            public FeatureMetadata Metadata;
            public Harmony Harmony;
            public FeatureContext Context;
        }

        private readonly ManualLogSource logger;
        private readonly ModConfiguration settings;
        private readonly GameObject runtimeRoot;
        private readonly SceneLifecycle sceneLifecycle;
        private readonly ServiceRegistry services = new ServiceRegistry();
        private readonly List<LoadedFeature> loadedFeatures = new List<LoadedFeature>();
        private bool disposed;
        private bool loadAttempted;

        public ServiceRegistry Services => services;

        public FeatureHost(ManualLogSource logger, ModConfiguration settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            runtimeRoot = new GameObject("BoscaliSummer.Runtime");
            runtimeRoot.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(runtimeRoot);
            sceneLifecycle = runtimeRoot.AddComponent<SceneLifecycle>();
        }

        public void Load(IReadOnlyList<IModFeature> features)
        {
            if (features == null) throw new ArgumentNullException(nameof(features));
            if (loadAttempted)
                throw new InvalidOperationException("Features have already been loaded.");
            loadAttempted = true;

            var metadata = new FeatureMetadata[features.Count];
            for (int i = 0; i < features.Count; i++)
                metadata[i] = features[i]?.Metadata ??
                    throw new ArgumentException("Feature entries cannot be null.", nameof(features));

            int[] order = FeatureGraph.Sort(metadata);
            ValidatePatchOwnership(features);
            var installed = new Dictionary<string, bool>(StringComparer.Ordinal);
            var candidates = new List<LoadedFeature>(features.Count);

            for (int orderIndex = 0; orderIndex < order.Length; orderIndex++)
            {
                IModFeature feature = features[order[orderIndex]];
                FeatureMetadata featureMetadata = feature.Metadata;
                if (!DependenciesStarted(featureMetadata, installed))
                {
                    installed[featureMetadata.Id] = false;
                    logger.LogWarning("Skipped feature '" + featureMetadata.DisplayName +
                        "' because a required feature did not start.");
                    continue;
                }

                FeatureContext context = null;
                try
                {
                    context = new FeatureContext(
                        featureMetadata.Id, runtimeRoot, sceneLifecycle, logger, settings, services);
                    feature.Install(context);
                    candidates.Add(new LoadedFeature
                    {
                        Feature = feature,
                        Metadata = featureMetadata,
                        Harmony = new Harmony(Plugin.PluginGuid + ".feature." + featureMetadata.Id),
                        Context = context
                    });
                    installed[featureMetadata.Id] = true;
                }
                catch (Exception e)
                {
                    context?.Rollback();
                    installed[featureMetadata.Id] = false;
                    logger.LogError("Feature failed to start: " + featureMetadata.DisplayName + ": " + e);
                }
            }

            sceneLifecycle.ResetAll();
            var started = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                LoadedFeature candidate = candidates[i];
                if (!DependenciesStarted(candidate.Metadata, started))
                {
                    candidate.Context.Rollback();
                    started[candidate.Metadata.Id] = false;
                    logger.LogWarning("Skipped feature '" + candidate.Metadata.DisplayName +
                        "' because a required feature did not finish patching.");
                    continue;
                }

                try
                {
                    InstallPatches(candidate.Feature, candidate.Harmony);
                    loadedFeatures.Add(candidate);
                    started[candidate.Metadata.Id] = true;
                    logger.LogInfo("Feature loaded: " + candidate.Metadata.DisplayName + ".");
                }
                catch (Exception e)
                {
                    candidate.Harmony.UnpatchSelf();
                    candidate.Context.Rollback();
                    started[candidate.Metadata.Id] = false;
                    logger.LogError("Feature failed to patch: " + candidate.Metadata.DisplayName + ": " + e);
                }
            }

            ReportPatches();
        }

        private static void ValidatePatchOwnership(IReadOnlyList<IModFeature> features)
        {
            var claimedPatchTypes = new HashSet<Type>();
            for (int featureIndex = 0; featureIndex < features.Count; featureIndex++)
            {
                IModFeature feature = features[featureIndex];
                Type[] patchTypes = feature.PatchTypes ?? Array.Empty<Type>();
                for (int patchIndex = 0; patchIndex < patchTypes.Length; patchIndex++)
                {
                    Type patchType = patchTypes[patchIndex];
                    if (patchType == null)
                        throw new InvalidOperationException(
                            "Feature '" + feature.Metadata.Id + "' contains a null patch type.");
                    if (!claimedPatchTypes.Add(patchType))
                        throw new InvalidOperationException(
                            "Harmony patch class is owned by more than one feature: " + patchType.FullName);
                }
            }
        }

        private static void InstallPatches(IModFeature feature, Harmony harmony)
        {
            Type[] patchTypes = feature.PatchTypes ?? Array.Empty<Type>();
            for (int i = 0; i < patchTypes.Length; i++)
            {
                Type patchType = patchTypes[i];
                try
                {
                    harmony.PatchAll(patchType);
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException(
                        "Patch installation failed for " + patchType.FullName + ".", e);
                }
            }
        }

        private static bool DependenciesStarted(
            FeatureMetadata metadata, Dictionary<string, bool> started)
        {
            for (int i = 0; i < metadata.Dependencies.Length; i++)
                if (!started.TryGetValue(metadata.Dependencies[i], out bool ready) || !ready)
                    return false;
            return true;
        }

        private void ReportPatches()
        {
            var names = new List<string>();
            for (int i = 0; i < loadedFeatures.Count; i++)
            {
                foreach (MethodBase method in loadedFeatures[i].Harmony.GetPatchedMethods())
                    names.Add(method.DeclaringType?.Name + "." + method.Name);
            }
            names.Sort(StringComparer.Ordinal);
            logger.LogInfo("Harmony patched " + names.Count + " method(s): " +
                string.Join(", ", names.ToArray()));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = loadedFeatures.Count - 1; i >= 0; i--)
                loadedFeatures[i].Harmony.UnpatchSelf();
            loadedFeatures.Clear();
            if (sceneLifecycle != null) sceneLifecycle.enabled = false;
            services.Clear();
            if (runtimeRoot != null) UnityEngine.Object.Destroy(runtimeRoot);
        }
    }
}
