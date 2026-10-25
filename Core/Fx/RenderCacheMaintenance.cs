using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Core.Fx
{
    /// <summary>Only URP's unused-stack cache is drained; stacks assigned to cameras and the main stack stay owned.</summary>
    internal static class RenderCacheMaintenance
    {
        private static readonly FieldInfo CachedStacks = typeof(UniversalAdditionalCameraData).GetField("s_CachedVolumeStacks", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo DefaultStack = typeof(VolumeManager).GetField("m_DefaultStack", BindingFlags.Instance | BindingFlags.NonPublic);

        public static int ClearUnusedStacks(out string error)
        {
            error = null;
            int cleared = 0;
            try
            {
                if (CachedStacks == null || DefaultStack == null)
                {
                    error = "Unused render cache is unavailable in this renderer version.";
                    return 0;
                }
                var cache = CachedStacks.GetValue(null) as List<VolumeStack>;
                if (cache == null || cache.Count == 0) return 0;
                VolumeManager manager = VolumeManager.instance;
                var protectedStacks = new HashSet<VolumeStack> { (VolumeStack)DefaultStack.GetValue(manager) };
                foreach (UniversalAdditionalCameraData camera in Resources.FindObjectsOfTypeAll<UniversalAdditionalCameraData>())
                    if (camera != null && camera.volumeStack != null) protectedStacks.Add(camera.volumeStack);
                VolumeStack[] unused = cache.ToArray();
                cache.Clear(); // Remove ownership before Dispose: no future camera can pick up a disposed entry.
                var seen = new HashSet<VolumeStack>();
                foreach (VolumeStack stack in unused)
                {
                    if (stack == null || protectedStacks.Contains(stack) || !seen.Add(stack)) continue;
                    try
                    {
                        if (ReferenceEquals(manager.stack, stack)) manager.ResetMainStack();
                        stack.Dispose();
                        cleared++;
                    }
                    catch (Exception failure) { error = "An unused render stack could not be released: " + failure.Message; }
                }
            }
            catch (Exception failure) { error = "Unused render cache could not be read: " + failure.Message; }
            return cleared;
        }
    }
}
