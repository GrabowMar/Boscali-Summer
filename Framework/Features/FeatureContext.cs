using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Framework.Features
{
    internal sealed class FeatureContext
    {
        private readonly string featureId;
        private readonly GameObject runtimeRoot;
        private readonly SceneLifecycle sceneLifecycle;
        private readonly List<Component> installedComponents = new List<Component>();
        private readonly List<Type> registeredServices = new List<Type>();
        private readonly List<IHostSettingsView> registeredHostSettings = new List<IHostSettingsView>();

        public ManualLogSource Logger { get; }
        public ModConfiguration Settings { get; }
        public ServiceRegistry Services { get; }

        /// <summary>Where this feature publishes host-authoritative settings for SET SERVER.</summary>
        public HostSettingsBoard HostSettings { get; }

        internal FeatureContext(
            string featureId,
            GameObject runtimeRoot,
            SceneLifecycle sceneLifecycle,
            ManualLogSource logger,
            ModConfiguration settings,
            ServiceRegistry services,
            HostSettingsBoard hostSettings)
        {
            this.featureId = featureId;
            this.runtimeRoot = runtimeRoot;
            this.sceneLifecycle = sceneLifecycle;
            Logger = logger;
            Settings = settings;
            Services = services;
            HostSettings = hostSettings;
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
            sceneLifecycle.Register(featureId, component, resetOrder);
            return component;
        }

        public void AddService<T>(T service) where T : class
        {
            Services.Add(service);
            registeredServices.Add(typeof(T));
        }

        /// <summary>Publish this feature's host-authoritative settings to the SET SERVER page.</summary>
        public void AddHostSettings(IHostSettingsView view)
        {
            if (view == null) return;
            HostSettings.Add(view);
            registeredHostSettings.Add(view);
        }

        internal void Rollback()
        {
            sceneLifecycle.Unregister(featureId);
            for (int i = registeredHostSettings.Count - 1; i >= 0; i--)
                HostSettings.Remove(registeredHostSettings[i]);
            registeredHostSettings.Clear();
            for (int i = registeredServices.Count - 1; i >= 0; i--)
                Services.Remove(registeredServices[i]);
            registeredServices.Clear();
            for (int i = installedComponents.Count - 1; i >= 0; i--)
                if (installedComponents[i] != null)
                    UnityEngine.Object.Destroy(installedComponents[i]);
            installedComponents.Clear();
        }
    }
}
