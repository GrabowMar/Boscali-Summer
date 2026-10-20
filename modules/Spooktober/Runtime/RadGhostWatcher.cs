using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>
    /// Keeps the rad look on the Spooktober targets, four times a second (a spawn shows opaque for at most 0.25 s) on every
    /// peer (local, no network): the kaiju's skin whenever
    /// Spooktober is installed (spec §5) and whatever <see cref="ExtraTargets"/> yields, at aircraft scale. A target that goes
    /// away or drops out of the list loses the look; a scene reset drops them all.
    /// </summary>
    internal sealed class RadGhostWatcher : MonoBehaviour, ISceneService
    {
        /// <summary>More roots to wear the look at aircraft scale (the FALLEN ACE). Null entries are skipped.</summary>
        public static Func<IEnumerable<GameObject>> ExtraTargets;

        private List<RadGhostLook.Handle> worn = new List<RadGhostLook.Handle>(), kept = new List<RadGhostLook.Handle>();
        private float next;

        public void ResetForScene() => RemoveAll();

        private void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.25f;

            kept.Clear();
            GameObject body = ModuleServices.TryGet(out IKaijuBoss boss) ? boss.Body : null;
            // Only the skin under "Model"; Profile.Kaiju leaves the weak spots (eyes, blisters, heart) their own glow.
            Transform model = body != null ? body.transform.Find("Model") : null;
            if (model != null) Wear(model.gameObject, RadGhostLook.Profile.Kaiju);
            IEnumerable<GameObject> extra = ExtraTargets?.Invoke();
            if (extra != null)
                foreach (GameObject go in extra)
                    if (go != null) Wear(go, RadGhostLook.Profile.Aircraft);

            foreach (RadGhostLook.Handle handle in worn)
                if (!kept.Contains(handle)) handle.Remove();
            (worn, kept) = (kept, worn);
        }

        /// <summary>Puts the look on once; a strength someone else set (RadGhostLook.Apply on the same root, e.g. a blink fade) stays.</summary>
        private void Wear(GameObject root, RadGhostLook.Profile profile)
        {
            RadGhostLook.Handle handle = worn.Find(h => h.Alive && h.Root == root) ?? RadGhostLook.Apply(root, profile);
            if (handle != null && !kept.Contains(handle)) kept.Add(handle);
        }

        private void RemoveAll()
        {
            foreach (RadGhostLook.Handle handle in worn) handle.Remove();
            worn.Clear();
        }

        /// <summary>Plugin teardown: every look comes off (also ones applied elsewhere) before the shader's bundle unloads, so
        /// no renderer is left on a destroyed shader; a reload then loads the bundle afresh.</summary>
        private void OnDestroy()
        {
            worn.Clear();
            RadGhostLook.RemoveAll();
            SpookAssets.Release();
        }
    }
}
