using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoscaliSummer.Core.Lifecycle
{
    internal sealed class SceneLifecycle : MonoBehaviour
    {
        private sealed class Entry
        {
            public string ModuleId;
            public ISceneService Service;
            public int Order;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private bool sorted;

        private void OnEnable() => SceneManager.sceneLoaded += SceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= SceneLoaded;

        internal void Register(string moduleId, ISceneService service, int order)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            entries.Add(new Entry { ModuleId = moduleId, Service = service, Order = order });
            sorted = false;
        }

        internal void Unregister(string moduleId)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
                if (string.Equals(entries[i].ModuleId, moduleId, StringComparison.Ordinal))
                    entries.RemoveAt(i);
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // An additive load joins the running mission; resetting there would wipe host world state.
            if (mode == LoadSceneMode.Additive) return;
            ResetAll();
        }

        internal void ResetAll()
        {
            if (!sorted)
            {
                entries.Sort(CompareEntries);
                sorted = true;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                try
                {
                    entry.Service.ResetForScene();
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError(
                        "Scene reset failed for module '" + entry.ModuleId + "': " + e);
                }
            }
        }

        private static int CompareEntries(Entry left, Entry right)
        {
            int byOrder = left.Order.CompareTo(right.Order);
            return byOrder != 0
                ? byOrder
                : string.Compare(left.ModuleId, right.ModuleId, StringComparison.Ordinal);
        }
    }
}
