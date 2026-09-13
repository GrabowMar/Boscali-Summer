using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    // VirtualMFD may live beside the map canvas, or exist before the canvas is assigned.
    internal static class MapMfdLookup
    {
        private const int MaxAttempts = 30;
        private static Canvas owner;
        private static VirtualMFD cached;
        private static float nextProbe;
        private static int attempts;

        public static VirtualMFD Resolve(Canvas canvas)
        {
            if (owner != canvas)
            {
                ClearCache();
                owner = canvas;
            }
            if (cached != null) return cached;
            if (attempts >= MaxAttempts || Time.unscaledTime < nextProbe) return null;
            attempts++;
            nextProbe = Time.unscaledTime + 1f;
            if (canvas != null)
                cached = canvas.GetComponentInChildren<VirtualMFD>(true);
            if (cached == null)
                cached = Object.FindObjectOfType<VirtualMFD>(true);
            return cached;
        }

        public static void Reset()
        {
            owner = null;
            ClearCache();
        }

        private static void ClearCache()
        {
            cached = null;
            nextProbe = 0f;
            attempts = 0;
        }
    }
}
