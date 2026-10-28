using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core.Config;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using UnityEngine;

namespace BoscaliSummer.Core.Modules
{
    internal sealed class ModuleContext
    {
        private readonly string moduleId;
        private readonly GameObject runtimeRoot;
        private readonly SceneLifecycle sceneLifecycle;
        private readonly List<Component> installedComponents = new List<Component>();
        private readonly List<Type> registeredServices = new List<Type>();
        private readonly List<IHostSettingsView> registeredHostSettings = new List<IHostSettingsView>();
        private readonly List<ClientSetting> registeredClientSettings = new List<ClientSetting>();
        private readonly List<ClientKeySetting> registeredClientKeys = new List<ClientKeySetting>();
        private readonly List<IClientEffect> registeredEffects = new List<IClientEffect>();
        private readonly List<Action> teardown = new List<Action>();

        public ManualLogSource Logger { get; }
        public ModConfiguration Settings { get; }
        /// <summary>Read services here; register new ones via AddService so rollback tracks them.</summary>
        public ServiceRegistry Services { get; }

        /// <summary>Where this module publishes host-authoritative settings for SET SERVER.</summary>
        public HostSettingsBoard HostSettings { get; }
        public ClientSettingsBoard ClientSettings { get; }

        internal ModuleContext(
            string moduleId,
            GameObject runtimeRoot,
            SceneLifecycle sceneLifecycle,
            ManualLogSource logger,
            ModConfiguration settings,
            ServiceRegistry services,
            HostSettingsBoard hostSettings, ClientSettingsBoard clientSettings)
        {
            this.moduleId = moduleId;
            this.runtimeRoot = runtimeRoot;
            this.sceneLifecycle = sceneLifecycle;
            Logger = logger;
            Settings = settings;
            Services = services;
            HostSettings = hostSettings;
            ClientSettings = clientSettings;
        }

        public T AddComponent<T>() where T : MonoBehaviour
        {
            T component = runtimeRoot.AddComponent<T>();
            installedComponents.Add(component);
            return component;
        }

        public T AddSceneService<T>(int resetOrder) where T : MonoBehaviour, ISceneService
        {
            T component = AddComponent<T>();
            sceneLifecycle.Register(moduleId, component, resetOrder);
            return component;
        }

        public void AddService<T>(T service) where T : class
        {
            Services.Add(service);
            registeredServices.Add(typeof(T));
        }

        /// <summary>Publish this module's host-authoritative settings to the SET SERVER page.</summary>
        public void AddHostSettings(IHostSettingsView view)
        {
            if (view == null) return;
            HostSettings.Add(view);
            registeredHostSettings.Add(view);
        }

        /// <summary>Publish an immediately applied client-local toggle to SET; the section picks its SET tab.</summary>
        public ClientSettingToggle AddClientSetting(string section, string label, string help,
            BepInEx.Configuration.ConfigEntry<bool> entry, bool[] presets = null, ClientSettingToggle requires = null) =>
            entry == null ? null : Track(new ClientSettingToggle(section, label, help, entry, presets, requires));

        /// <summary>Publish a client-local level to SET as a dial; <paramref name="text"/> prints it (a percentage when null).</summary>
        public void AddClientLevel(string section, string label, string help, BepInEx.Configuration.ConfigEntry<float> entry,
            float min, float max, float step, Func<float, string> text = null, ClientSettingToggle requires = null)
        {
            if (entry != null) Track(new ClientSettingLevel(section, label, help, entry, min, max, step, text, requires));
        }

        /// <summary>Publish a client-local enum to SET as a segment strip; <paramref name="names"/> follow the enum's order.</summary>
        public void AddClientChoice<T>(string section, string label, string help, BepInEx.Configuration.ConfigEntry<T> entry,
            string[] names, ClientSettingToggle requires = null) where T : Enum
        {
            if (entry != null) Track(new ClientSettingChoice(section, label, help, entry, names, requires));
        }

        private T Track<T>(T row) where T : ClientSetting
        {
            registeredClientSettings.Add(ClientSettings.Add(row));
            return row;
        }

        /// <summary>Publish a client-local key to SET (and, unless <paramref name="onPrompt"/> is false, the mission prompt), where the player rebinds it.</summary>
        public void AddClientKey(string label, string help, BepInEx.Configuration.ConfigEntry<KeyCode> entry, bool onPrompt = true)
        {
            if (entry == null) return;
            registeredClientKeys.Add(ClientSettings.AddKey(label, help, entry, onPrompt));
        }

        /// <summary>
        /// Opt a client-local effect into the shared resource/diagnostic bus. A rejected
        /// registration fails only this module's installation, so the host rolls it back.
        /// The owning module still creates, ticks and configures the effect.
        /// </summary>
        public void AddClientEffect(IClientEffect effect)
        {
            if (!FxBus.Register(effect))
                throw new InvalidOperationException("Client effect registration refused: " +
                    (effect?.EffectId ?? "<null>"));
            registeredEffects.Add(effect);
        }

        public void OnTeardown(Action action)
        {
            if (action != null) teardown.Add(action);
        }

        internal void Rollback()
        {
            sceneLifecycle.Unregister(moduleId);
            for (int i = teardown.Count - 1; i >= 0; i--)
            {
                try { teardown[i](); }
                catch (Exception error) { Logger.LogWarning("Module subscription teardown failed: " + error); }
            }
            teardown.Clear();
            for (int i = registeredEffects.Count - 1; i >= 0; i--)
            {
                IClientEffect effect = registeredEffects[i];
                FxBus.Unregister(effect);
                try { effect.ReleaseFx(); }
                catch (Exception error) { Logger.LogWarning("Client effect teardown failed: " + error); }
            }
            registeredEffects.Clear();
            for (int i = registeredHostSettings.Count - 1; i >= 0; i--)
                HostSettings.Remove(registeredHostSettings[i]);
            registeredHostSettings.Clear();
            for (int i = registeredClientSettings.Count - 1; i >= 0; i--)
                ClientSettings.Remove(registeredClientSettings[i]);
            registeredClientSettings.Clear();
            for (int i = registeredClientKeys.Count - 1; i >= 0; i--)
                ClientSettings.Remove(registeredClientKeys[i]);
            registeredClientKeys.Clear();
            for (int i = registeredServices.Count - 1; i >= 0; i--)
                Services.Remove(registeredServices[i]);
            registeredServices.Clear();
            for (int i = installedComponents.Count - 1; i >= 0; i--)
                if (installedComponents[i] is MonoBehaviour behaviour && behaviour != null)
                {
                    // Profile changes happen at the native mission-loading boundary. End
                    // callbacks and network handlers now, before the next mission can spawn.
                    behaviour.enabled = false;
                    behaviour.StopAllCoroutines();
                }
            // Reset while sibling components still exist; managers often reset their networks.
            for (int i = installedComponents.Count - 1; i >= 0; i--)
                if (installedComponents[i] != null && installedComponents[i] is ISceneService sceneService)
                {
                    try { sceneService.ResetForScene(); }
                    catch (Exception error) { Logger.LogWarning("Module teardown reset failed: " + error); }
                }
            for (int i = installedComponents.Count - 1; i >= 0; i--)
                if (installedComponents[i] != null)
                    UnityEngine.Object.DestroyImmediate(installedComponents[i]);
            installedComponents.Clear();
        }
    }
}
