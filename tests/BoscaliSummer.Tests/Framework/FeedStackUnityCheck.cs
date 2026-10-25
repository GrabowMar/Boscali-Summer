#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Production FeedCamera.Release and installed game URP. Synthetic cameras isolate stack ownership/reuse;
// native destruction is driven explicitly in edit mode, without live target selection or mission restart.
public static class FeedStackUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Type Feed = Type.GetType("BoscaliSummer.Core.Fx.FeedCamera, BoscaliSummer", true);
    private static readonly MethodInfo UpdateFramework = typeof(UniversalRenderPipeline).GetMethod("UpdateVolumeFramework", All);
    private static readonly MethodInfo DepthCheck = typeof(UniversalRenderPipeline).GetMethod("CheckPostProcessForDepth", All, null, Type.EmptyTypes, null);
    private static int assertions;

    public static void Run()
    {
        try
        {
            ReproduceNativeFaults();
            Cases();
            Finish("PASS: " + assertions + " assertions; disposed-cache control reproduces ReplaceData and CheckPostProcessForDepth NREs; fixed release preserves native volume update/depth checks before and after cache reuse. All feed grades, both destruction orders, repeated release, current/other stack, default-grade reset. Production DLL and game URP; synthetic cameras in offline edit mode.", true);
        }
        catch (Exception e) { Debug.LogException(e); Finish("FAIL: " + e, false); }
    }

    private static void ReproduceNativeFaults()
    {
        VolumeManager manager = VolumeManager.instance;
        Camera camera = new GameObject("Old feed", typeof(Camera)).GetComponent<Camera>();
        camera.enabled = false;
        VolumeStack disposed = manager.CreateStack();
        camera.GetUniversalAdditionalCameraData().volumeStack = disposed;
        disposed.Dispose(); // Original bug: URP caches this dead object on native destruction.
        Object.DestroyImmediate(camera.gameObject);
        Camera target = NewTarget(out UniversalAdditionalCameraData targetData);
        ExpectNullReference(() => UpdateFramework.Invoke(null, new object[] { target, targetData }), "ReplaceData");
        Check(ReferenceEquals(targetData.volumeStack, disposed), "control reaches the disposed cache entry through native URP");
        manager.stack = disposed;
        ExpectNullReference(() => DepthCheck.Invoke(null, null), "CheckPostProcessForDepth");
        manager.ResetMainStack();
        // Replace the consumed dead entry without putting it back in the cache.
        targetData.volumeStack = manager.CreateStack();
        Object.DestroyImmediate(target.gameObject);
    }

    private static void Cases()
    {
        VolumeManager manager = VolumeManager.instance;
        VolumeStack main = manager.stack;
        VolumeStack other = manager.CreateStack();
        Type lookType = Feed.Assembly.GetType("BoscaliSummer.Core.Fx.FeedLook", true);
        for (int cycle = 0; cycle < 6; cycle++)
        {
            object look = Enum.ToObject(lookType, cycle % 3);
            object feed = Activator.CreateInstance(Feed, All, null, new[] { (object)"StackCheck", 16, 9, look, false }, null);
            Camera camera = new GameObject("Feed " + cycle, typeof(Camera)).GetComponent<Camera>();
            camera.enabled = false;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            VolumeStack stack = manager.CreateStack();
            data.volumeStack = stack;
            Feed.GetMethod("Grade", All).Invoke(null, new[] { (object)stack, look });
            // Keep the feed's camera reference empty so Release does not call edit-mode Object.Destroy.
            // Drive real URP OnDestroy ourselves, on either side of the production stack-release logic.
            Feed.GetField("stack", All).SetValue(feed, stack);
            Feed.GetField("ours", All).SetValue(feed, stack.GetComponent<ColorAdjustments>());
            manager.stack = cycle % 2 == 0 ? stack : other;

            // A scene unload may destroy the native camera before the module gets its reset callback.
            if (cycle >= 3) Object.DestroyImmediate(camera.gameObject);
            Feed.GetMethod("Release", All).Invoke(feed, null);
            Check(ReferenceEquals(manager.stack, cycle % 2 == 0 ? main : other), "release restores the main stack or preserves another camera's stack");
            Check(!(bool)DepthCheck.Invoke(null, null), "native depth check survives release before the next camera updates volumes");
            Check(Feed.GetField("stack", All).GetValue(feed) == null && Feed.GetField("ours", All).GetValue(feed) == null,
                "feed drops its stack/component references");
            Feed.GetMethod("Release", All).Invoke(feed, null);
            if (camera != null) Object.DestroyImmediate(camera.gameObject);

            Camera target = NewTarget(out UniversalAdditionalCameraData targetData);
            // Do not short-circuit on a missing-component assertion: the old DLL must fail in native ReplaceData.
            UpdateFramework.Invoke(null, new object[] { target, targetData });
            Check(ReferenceEquals(targetData.volumeStack, stack), "new target camera reuses the released feed stack");
            Check(ReferenceEquals(manager.stack, stack), "native volume framework makes the recycled stack current");
            Check(!(bool)DepthCheck.Invoke(null, null), "native CheckPostProcessForDepth survives the recycled target stack");
            ColorAdjustments color = targetData.volumeStack.GetComponent<ColorAdjustments>();
            Check(color != null && color.saturation.value == 0f && color.contrast.value == 0f && color.postExposure.value == 0f,
                "native volume update resets the feed grade before target-camera reuse");
            Check(targetData.volumeStack.GetComponent<DepthOfField>() != null, "target depth post-processing components survive reuse");
            Object.DestroyImmediate(target.gameObject);
        }
        manager.stack = main;
        manager.DestroyStack(other);
    }

    private static Camera NewTarget(out UniversalAdditionalCameraData data)
    {
        Camera target = new GameObject("TargetCam", typeof(Camera)).GetComponent<Camera>();
        target.enabled = false;
        data = target.GetUniversalAdditionalCameraData();
        typeof(UniversalAdditionalCameraData).GetProperty("volumeFrameworkUpdateMode", All)
            .SetValue(data, VolumeFrameworkUpdateMode.ViaScripting);
        data.volumeLayerMask = 0;
        return target;
    }

    private static void ExpectNullReference(Action call, string method)
    {
        try { call(); }
        catch (TargetInvocationException e)
        {
            Exception fault = e.InnerException;
            Check(fault is NullReferenceException && fault.StackTrace.Contains(method), "control reproduces native " + method + " NRE");
            return;
        }
        throw new Exception("control did not reproduce native " + method + " NRE");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }

    private static void Finish(string result, bool passed)
    {
        File.WriteAllText("result.txt", result + "\n");
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
#endif
