using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using BoscaliSummer.Core.Config;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.AirSurvival;
using BoscaliSummer.Modules.Autopilot;
using BoscaliSummer.Modules.Command;
using BoscaliSummer.Modules.Cinematography;
using BoscaliSummer.Modules.Comms;
using BoscaliSummer.Modules.DynamicOperations;
using BoscaliSummer.Modules.Events;
using BoscaliSummer.Modules.FireAndDestruction;
using BoscaliSummer.Modules.HighCommand;
using BoscaliSummer.Modules.Hud;
using BoscaliSummer.Modules.Intel;
using BoscaliSummer.Modules.Immersion;
using BoscaliSummer.Modules.Performance;
using BoscaliSummer.Modules.PlayerSpawnPriority;
using BoscaliSummer.Modules.Progression;
using BoscaliSummer.Modules.QoL;
using BoscaliSummer.Modules.Radio;
using BoscaliSummer.Modules.Session;
using BoscaliSummer.Modules.Wing;
using BoscaliSummer.Modules.Squad;
using BoscaliSummer.Modules.Support;
using BoscaliSummer.Modules.TheaterOps;
using BoscaliSummer.Modules.Trenches;
using BoscaliSummer.Modules.UrbanCombat;
using BoscaliSummer.Modules.Weather;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Core
{
    /// <summary>
    /// The mod's composition root and module host in one place. <see cref="Start"/> builds
    /// the module list from the master switches, installs every module, then applies its
    /// Harmony patches; <see cref="Dispose"/> undoes all of it in reverse. Modules never
    /// touch each other directly: they publish services and settings boards here, and
    /// resolve each other late through <see cref="ModuleServices"/>.
    /// </summary>
    internal sealed class BoscaliMod : IDisposable
    {
        private sealed class LoadedModule
        {
            public IModule Module;
            public ModuleMetadata Metadata;
            public Harmony Harmony;
            public ModuleContext Context;
        }

        private readonly ManualLogSource logger;
        private readonly ModConfiguration settings;
        private readonly GameObject runtimeRoot;
        private readonly SceneLifecycle sceneLifecycle;
        private readonly ServiceRegistry services = new ServiceRegistry();
        private readonly HostSettingsBoard hostSettings = new HostSettingsBoard();
        private readonly ClientSettingsBoard clientSettings = new ClientSettingsBoard();
        private readonly List<LoadedModule> loadedModules = new List<LoadedModule>();
        private bool disposed;

        private BoscaliMod(ManualLogSource logger, ModConfiguration settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            runtimeRoot = new GameObject("BoscaliSummer.Core");
            runtimeRoot.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(runtimeRoot);
            sceneLifecycle = runtimeRoot.AddComponent<SceneLifecycle>();
            // Pilot reflection needs native glass even when dynamic Weather is disabled.
            if (!Application.isBatchMode)
                services.Add<ICanopyGlassView>(new BoscaliSummer.Modules.Weather.Visuals.CanopyGlassView());
            ModuleServices.Active = services;
            MissionProfile.Changed += OnMissionProfileChanged;
            MissionProfile.Preparing += ResetForMission;
        }

        public static BoscaliMod Start(ManualLogSource logger, ModConfiguration settings)
        {
            GameAccess.Initialise();
            // Outside a mission the last confirmed selection applies; each mission start re-selects.
            MissionProfile.SetDisabled(MissionProfile.Parse(settings.MissionDisabledModules.Value));
            var mod = new BoscaliMod(logger, settings);
            try
            {
                mod.Load(mod.ModulesForProfile());
                if (!Application.isBatchMode)
                {
                    var offered = new List<ModuleMetadata>();
                    foreach (IModule module in Compose(settings))
                        if (!MissionProfile.IsCore(module.Metadata.Id)) offered.Add(module.Metadata);
                    mod.runtimeRoot.AddComponent<MissionPrompt>().Offered = offered.ToArray();
                }
                CapabilityReport.Log();
                return mod;
            }
            catch
            {
                mod.Dispose();
                throw;
            }
        }

        /// <summary>
        /// The startup roster. Registration order is display order on the SET pages.
        /// </summary>
        private static IModule[] Compose(ModConfiguration settings)
        {
            // Session always installs: the multiplayer handshake every other module leans on.
            // The common HUD element installs before every presentation module below, which
            // each resolve it late and work without it.
            var modules = new List<IModule>
            {
                new MissionProfileModule(),
                // Mission scripts: the scripted missions in the picker, their dialogue and cutscenes.
                // The third side scripted missions can seat the pilot on.
                new BoscaliSummer.Modules.Pmc.PmcModule(),
                new BoscaliSummer.Modules.Scenario.ScenarioModule(),
                new SessionModule(),
                new FireAndDestructionModule(),
                new UrbanCombatModule(),
                new HudModule(),
                new PlayerSpawnPriorityModule(),
                new RadioModule()
            };
            // The inexpensive monitor is always installed so SET can switch it live.
            if (!Application.isBatchMode)
            {
                modules.Add(new PerformanceModule());
                // Keep the client service available so SET can enable the master live.
                modules.Add(new ImmersionModule());
                // Default off; installed so SET can switch the display grade live.
                modules.Add(new BoscaliSummer.Modules.Effects.EffectsModule());
            }
            if (settings.QoL.Enabled.Value && !Application.isBatchMode)
                modules.Add(new QoLModule());
            if (settings.Autopilot.Enabled.Value && !Application.isBatchMode)
                modules.Add(new AutopilotModule());
            // The radio installs before its publishers (Wing, Support), which resolve it late and stay silent without it.
            modules.Add(new BoscaliSummer.Modules.Chatter.ChatterModule());
            // Wing publishes IWingSquad, which Squad and the WMC page resolve.
            modules.Add(new WingModule());
            if (settings.Progression.Enabled.Value)
            {
                modules.Add(new SquadModule());
                modules.Add(new ProgressionModule());
            }
            // Support needs progression; the ordinary avionics remain available without it.
            if (settings.Support.Enabled.Value) modules.Add(new SupportModule());
            if (settings.Command.Enabled.Value) modules.Add(new CommandModule());
            if (settings.Command.Enabled.Value) modules.Add(new StrategicCommandModule());
            if (settings.DynamicOperations.Enabled.Value) modules.Add(new DynamicOperationsModule());
            if (settings.HighCommand.Enabled.Value) modules.Add(new HighCommandModule());
            // Intel publishes IThreatPicture ahead of TheaterOps, whose director reads it
            // late; neither depends on the other.
            if (settings.Intel.Enabled.Value) modules.Add(new IntelModule());
            if (settings.TheaterOps.Enabled.Value)
            {
                modules.Add(new TheaterOpsModule());
                modules.Add(new AirSurvivalModule());
            }
            if (settings.Trenches.Enabled.Value) modules.Add(new TrenchesModule());
            if (settings.Events.Enabled.Value) modules.Add(new EventsModule());
            if (settings.Comms.Enabled.Value) modules.Add(new CommsModule());
            if (settings.Weather.Enabled.Value) modules.Add(new WeatherModule());
            // Always installed for the cutscene camera mission scripts play; its private production tools keep their own switch.
            if (!Application.isBatchMode) modules.Add(new CinematographyModule());
            if (BoscaliSummer.Modules.Vanguard.VanguardModule.Available) modules.Add(new BoscaliSummer.Modules.Vanguard.VanguardModule());
            return modules.ToArray();
        }

        private IModule[] ModulesForProfile()
        {
            var modules = new List<IModule>();
            foreach (IModule module in Compose(settings))
                if (MissionProfile.AllowsModule(module.Metadata.Id))
                    modules.Add(module);
            return modules.ToArray();
        }

        private void OnMissionProfileChanged()
        {
            if (disposed) return;
            // A module goes when the player switched it off, or when a module it needs went.
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (LoadedModule loaded in loadedModules)
                if (MissionProfile.AllowsModule(loaded.Metadata.Id)) keep.Add(loaded.Metadata.Id);
            for (bool changed = true; changed;)
            {
                changed = false;
                foreach (LoadedModule loaded in loadedModules)
                    if (keep.Contains(loaded.Metadata.Id) && !keep.IsSupersetOf(loaded.Metadata.Dependencies))
                        changed = keep.Remove(loaded.Metadata.Id);
            }
            // Remove all gameplay patches before tearing down any of their services.
            for (int i = loadedModules.Count - 1; i >= 0; i--)
                if (!keep.Contains(loadedModules[i].Metadata.Id)) loadedModules[i].Harmony.UnpatchSelf();
            bool removed = false;
            for (int i = loadedModules.Count - 1; i >= 0; i--)
            {
                LoadedModule loaded = loadedModules[i];
                if (keep.Contains(loaded.Metadata.Id)) continue;
                loaded.Context.Rollback();
                loadedModules.RemoveAt(i);
                removed = true;
                logger.LogInfo("Module switched off for this mission: " + loaded.Metadata.DisplayName + ".");
            }
            var additions = new List<IModule>();
            foreach (IModule module in ModulesForProfile())
                if (!keep.Contains(module.Metadata.Id)) additions.Add(module);
            if (additions.Count > 0) Load(additions);
            else if (removed) sceneLifecycle.ResetAll();
        }

        private void ResetForMission()
        {
            if (!disposed) sceneLifecycle.ResetAll();
        }

        private void Load(IReadOnlyList<IModule> modules)
        {
            if (modules == null) throw new ArgumentNullException(nameof(modules));

            var requested = new ModuleMetadata[modules.Count];
            for (int i = 0; i < modules.Count; i++)
                requested[i] = modules[i]?.Metadata ??
                    throw new ArgumentException("Module entries cannot be null.", nameof(modules));

            // A module whose dependency is switched off in settings is left out, not fatal.
            var existing = new HashSet<string>(StringComparer.Ordinal);
            foreach (LoadedModule loaded in loadedModules) existing.Add(loaded.Metadata.Id);
            string[] missing = ModuleGraph.MissingDependencies(requested, existing);
            var loadable = new List<IModule>(modules.Count);
            for (int i = 0; i < modules.Count; i++)
            {
                if (missing[i] == null) loadable.Add(modules[i]);
                else logger.LogWarning("Skipped module '" + requested[i].DisplayName +
                    "' because it needs module '" + missing[i] + "', which is turned off or skipped.");
            }
            modules = loadable;
            var metadata = new ModuleMetadata[modules.Count];
            for (int i = 0; i < modules.Count; i++) metadata[i] = modules[i].Metadata;

            int[] order = ModuleGraph.Sort(metadata, existing);
            var allModules = new List<IModule>(modules);
            foreach (LoadedModule loaded in loadedModules) allModules.Add(loaded.Module);
            ValidatePatchOwnership(allModules);
            var installed = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string id in existing) installed[id] = true;
            var candidates = new List<LoadedModule>(modules.Count);

            for (int orderIndex = 0; orderIndex < order.Length; orderIndex++)
            {
                IModule module = modules[order[orderIndex]];
                ModuleMetadata moduleMetadata = module.Metadata;
                if (!DependenciesStarted(moduleMetadata, installed))
                {
                    installed[moduleMetadata.Id] = false;
                    logger.LogWarning("Skipped module '" + moduleMetadata.DisplayName +
                        "' because a required module did not start.");
                    continue;
                }

                ModuleContext context = null;
                try
                {
                    context = new ModuleContext(
                        moduleMetadata.Id, runtimeRoot, sceneLifecycle, logger, settings, services,
                        hostSettings, clientSettings);
                    module.Install(context);
                    candidates.Add(new LoadedModule
                    {
                        Module = module,
                        Metadata = moduleMetadata,
                        Harmony = new Harmony(Plugin.PluginGuid + ".module." + moduleMetadata.Id),
                        Context = context
                    });
                    installed[moduleMetadata.Id] = true;
                }
                catch (Exception e)
                {
                    context?.Rollback();
                    installed[moduleMetadata.Id] = false;
                    logger.LogError("Module failed to start: " + moduleMetadata.DisplayName + ": " + e);
                }
            }

            sceneLifecycle.ResetAll();
            var started = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string id in existing) started[id] = true;
            for (int i = 0; i < candidates.Count; i++)
            {
                LoadedModule candidate = candidates[i];
                if (!DependenciesStarted(candidate.Metadata, started))
                {
                    candidate.Context.Rollback();
                    started[candidate.Metadata.Id] = false;
                    logger.LogWarning("Skipped module '" + candidate.Metadata.DisplayName +
                        "' because a required module did not finish patching.");
                    continue;
                }

                try
                {
                    InstallPatches(candidate.Module, candidate.Harmony);
                    loadedModules.Add(candidate);
                    started[candidate.Metadata.Id] = true;
                    logger.LogInfo("Module loaded: " + candidate.Metadata.DisplayName + ".");
                }
                catch (Exception e)
                {
                    candidate.Harmony.UnpatchSelf();
                    candidate.Context.Rollback();
                    started[candidate.Metadata.Id] = false;
                    logger.LogError("Module failed to patch: " + candidate.Metadata.DisplayName + ": " + e);
                }
            }

            ReportPatches();
        }

        private static void ValidatePatchOwnership(IReadOnlyList<IModule> modules)
        {
            var claimedPatchTypes = new HashSet<Type>();
            for (int moduleIndex = 0; moduleIndex < modules.Count; moduleIndex++)
            {
                IModule module = modules[moduleIndex];
                Type[] patchTypes = module.PatchTypes ?? Array.Empty<Type>();
                for (int patchIndex = 0; patchIndex < patchTypes.Length; patchIndex++)
                {
                    Type patchType = patchTypes[patchIndex];
                    if (patchType == null)
                        throw new InvalidOperationException(
                            "Module '" + module.Metadata.Id + "' contains a null patch type.");
                    if (!claimedPatchTypes.Add(patchType))
                        throw new InvalidOperationException(
                            "Harmony patch class is owned by more than one module: " + patchType.FullName);
                }
            }
        }

        private static void InstallPatches(IModule module, Harmony harmony)
        {
            Type[] patchTypes = module.PatchTypes ?? Array.Empty<Type>();
            for (int i = 0; i < patchTypes.Length; i++)
            {
                Type patchType = patchTypes[i];
                try
                {
                    // A patch class's static initializers otherwise run on the first call of the patched
                    // native method, where a throw becomes a TypeInitializationException inside game code
                    // on every call (issue #9: no gun pipper). Run them here so the module rolls back instead.
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(patchType.TypeHandle);
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
            ModuleMetadata metadata, Dictionary<string, bool> started)
        {
            for (int i = 0; i < metadata.Dependencies.Length; i++)
                if (!started.TryGetValue(metadata.Dependencies[i], out bool ready) || !ready)
                    return false;
            return true;
        }

        private void ReportPatches()
        {
            var names = new List<string>();
            for (int i = 0; i < loadedModules.Count; i++)
            {
                foreach (MethodBase method in loadedModules[i].Harmony.GetPatchedMethods())
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
            MissionProfile.Changed -= OnMissionProfileChanged;
            MissionProfile.Preparing -= ResetForMission;
            for (int i = loadedModules.Count - 1; i >= 0; i--)
                loadedModules[i].Harmony.UnpatchSelf();
            for (int i = loadedModules.Count - 1; i >= 0; i--)
                loadedModules[i].Context.Rollback();
            int fxFailures = FxBus.Shutdown();
            if (fxFailures > 0) logger.LogWarning("Client FX teardown failures: " + fxFailures);
            loadedModules.Clear();
            if (sceneLifecycle != null) sceneLifecycle.enabled = false;
            services.Clear();
            if (ReferenceEquals(ModuleServices.Active, services)) ModuleServices.Active = null;
            if (runtimeRoot != null) UnityEngine.Object.Destroy(runtimeRoot);
            BoscaliMission.Unregister();
            MissionProfile.SetDisabled(null);
        }
    }
}
