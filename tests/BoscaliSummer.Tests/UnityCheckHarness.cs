#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Setup, reflection and capture helpers shared by the *UnityCheck.cs fixtures. The Run-*UnityCheck.ps1 runners copy this
/// file next to the check into the scratch Unity project, so it may only use what every such project has: UnityEditor,
/// TextMeshPro and the NOAvionics kit. Checks pull it in with <c>using static UnityCheckHarness;</c>;
/// a check that needs different semantics keeps its own member of the same name.
/// </summary>
public static class UnityCheckHarness
{
    public const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    /// <summary>True when the TMP shaders exist. Otherwise imports them and calls <paramref name="rerun"/> afterwards (return false and stop).</summary>
    public static bool EnsureTmpEssentials(Action rerun)
    {
        if (Shader.Find("TextMeshPro/Distance Field") != null) return true;
        var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
        AssetDatabase.importPackageCompleted += _ => EditorApplication.delayCall += () => rerun();
        AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
        return false;
    }

    /// <summary>BepInEx.Paths.SetExecutablePath(fullPath(<paramref name="exeName"/>), defaults...), so config and data paths resolve in the editor.</summary>
    public static void SetExecutablePath(string exeName)
    {
        MethodInfo paths = Type.GetType("BepInEx.Paths, BepInEx", true).GetMethod("SetExecutablePath", All); // by name: not every project that uses this file references BepInEx
        ParameterInfo[] parameters = paths.GetParameters();
        var args = new object[parameters.Length];
        args[0] = Path.GetFullPath(exeName);
        for (int i = 1; i < args.Length; i++) args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
        paths.Invoke(null, args);
    }

    /// <summary>The standard kit bring-up: style host on the working directory, shipped fonts and icons from the bundle, FX driver at <paramref name="fx"/>.</summary>
    public static void InitAvionics(AvFxTier fx = AvFxTier.Off)
    {
        AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
        Shader.SetGlobalFloat("_NOA_Now", 1e6f);
        AvBundle.ResetForTests();
        AvBundle.Load(Debug.Log);
        if (!AvBundle.Available || !AvIcons.Available) throw new Exception("Production fonts and icons did not load.");
        AvFxDriver.Configure(fx, false);
    }

    /// <summary>Writes the result file and exits the editor (0 on pass, 1 on fail).</summary>
    public static void Finish(string result, bool pass, string file = "result.txt")
    {
        File.WriteAllText(file, result);
        EditorApplication.Exit(pass ? 0 : 1);
    }

    public static object Get(object o, string name) => o.GetType().GetField(name, All)?.GetValue(o) ?? o.GetType().GetProperty(name, All).GetValue(o);
    public static object Field(object o, string name) => o.GetType().GetField(name, All).GetValue(o);
    public static void Set(object o, string name, object value) => o.GetType().GetField(name, All).SetValue(o, value);
    public static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, All).Invoke(o, args);
    public static object CallStatic(Type type, string name, params object[] args) => type.GetMethod(name, All).Invoke(null, args);

    /// <summary>A new orthographic camera (on its own GameObject) with a solid background, looking down +Z from z = -10 unless placed.</summary>
    public static Camera OrthoCamera(string name, float orthoSize, Color background, Vector3? position = null)
    {
        Camera camera = new GameObject(name, typeof(Camera)).GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = orthoSize;
        camera.transform.position = position ?? new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = background;
        return camera;
    }

    /// <summary>Renders <paramref name="camera"/> into <paramref name="target"/> and writes the pixels as a PNG.</summary>
    public static void CapturePng(Camera camera, RenderTexture target, string path)
    {
        camera.targetTexture = target;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = previous;
        Object.DestroyImmediate(image);
    }

    /// <summary>Renders <paramref name="camera"/> into a fresh pixelWidth x pixelHeight target, writes the PNG and releases the target.</summary>
    public static void CapturePng(Camera camera, int pixelWidth, int pixelHeight, string path)
    {
        var target = new RenderTexture(pixelWidth, pixelHeight, 24);
        CapturePng(camera, target, path);
        camera.targetTexture = null;
        Object.DestroyImmediate(target);
    }
}
#endif
