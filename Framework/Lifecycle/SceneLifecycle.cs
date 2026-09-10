using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoscaliSummer.Framework.Lifecycle
{
    internal sealed class SceneLifecycle : MonoBehaviour
    {
        private sealed class Entry
        {
            public string FeatureId;
            public ISceneService Service;
            public int Order;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private bool sorted;

        private void OnEnable() => SceneManager.sceneLoaded += SceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= SceneLoaded;

        internal void Register(string featureId, ISceneService service, int order)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            entries.Add(new Entry { FeatureId = featureId, Service = service, Order = order });
            sorted = false;
        }

        internal void Unregister(string featureId)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
                if (string.Equals(entries[i].FeatureId, featureId, StringComparison.Ordinal))
                    entries.RemoveAt(i);
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
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
                        "Scene reset failed for feature '" + entry.FeatureId + "': " + e);
                }
            }
        }

        private static int CompareEntries(Entry left, Entry right)
        {
            int byOrder = left.Order.CompareTo(right.Order);
            return byOrder != 0
                ? byOrder
                : string.Compare(left.FeatureId, right.FeatureId, StringComparison.Ordinal);
        }
    }
}
