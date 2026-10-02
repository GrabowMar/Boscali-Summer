using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using UnityEngine;

namespace BoscaliSummer.Core.Fx
{
    /// <summary>
    /// The render-target ledger: every persistent client-effects RT is owned here, counted in
    /// bytes against one cap (24 MB: canopy 16 + imagers ~2 + headroom for FxPass scratch).
    /// Actual pooling stays Unity's GetTemporary; this only guarantees nothing grows unbounded
    /// and every byte shows up in Describe. Refused allocations return false — the caller
    /// destroys and skips, never half-renders.
    /// </summary>
    internal static class FxRtPool
    {
        internal const long CapBytes = 24L * 1024 * 1024;
        private const int MaxEntries = 48;

        private static readonly Dictionary<RenderTexture, long> owned =
            new Dictionary<RenderTexture, long>(MaxEntries);
        private static long usedBytes;

        public static long UsedBytes => usedBytes;

        /// <summary>
        /// Count an already-created RT. False when over cap or full: destroy it and skip.
        /// </summary>
        public static bool Own(RenderTexture target)
        {
            if (target == null) return false;
            long bytes = FxBudget.RtBytes(target.width, target.height,
                FxBudget.ColourBits(target.format.ToString()), target.depth);
            if (owned.ContainsKey(target)) return true;
            if (owned.Count >= MaxEntries || usedBytes + bytes > CapBytes) return false;
            owned[target] = bytes;
            usedBytes += bytes;
            return true;
        }

        public static void Disown(RenderTexture target)
        {
            if (target == null) return;
            if (owned.TryGetValue(target, out long bytes))
            {
                owned.Remove(target);
                usedBytes -= bytes;
                if (usedBytes < 0) usedBytes = 0;
            }
        }

        public static void Describe(IDictionary<string, object> state)
        {
            if (state == null) return;
            state["fxRtBytes"] = usedBytes;
            state["fxRtCount"] = owned.Count;
        }

        internal static void ReleaseAll()
        {
            // Owners normally disown and destroy their targets in ReleaseFx. Any target
            // still recorded here belongs to a failed owner, so tear it down on stop.
            foreach (RenderTexture target in owned.Keys)
                if (target != null) UnityEngine.Object.Destroy(target);
            owned.Clear();
            usedBytes = 0;
        }
    }
}
