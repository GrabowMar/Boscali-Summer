using System;
using System.Collections.Generic;
using BepInEx.Logging;
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

        public ManualLogSource Logger { get; }
        public ModConfiguration Settings { get; }
        public ServiceRegistry Services { get; }

        internal FeatureContext(
            string featureId,
            GameObject runtimeRoot,
            SceneLifecycle sceneLifecycle,
            ManualLogSource logger,
            ModConfiguration settings,
            ServiceRegistry services)
        {
            this.featureId = featureId;
            this.runtimeRoot = runtimeRoot;
            this.sceneLifecycle = sceneLifecycle;
            Logger = logger;
            Settings = settings;
            Services = services;
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

        internal void Rollback()
        {
            sceneLifecycle.Unregister(featureId);
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
