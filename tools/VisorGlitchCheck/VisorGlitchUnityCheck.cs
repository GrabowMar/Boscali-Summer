#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Runs the compiled production material scan with real Unity/TMP components, including an uninitialized fallback submesh.
public static class VisorGlitchUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly Type Visor = Type.GetType("BoscaliSummer.Modules.Hud.Presentation.VisorGlitch, BoscaliSummer", true);
    private static int assertions;

    public static void Run()
    {
        bool passed = false;
        string result;
        try
        {
            Cases();
            passed = true;
            result = "PASS: " + assertions + " production visor material assertions; default/custom images, null TMP label and fallback submesh, repeat scans, and shader disable restoration. Offline Unity material ownership check; no rendered-quality claim.\n";
        }
        catch (Exception error) { result = "FAIL: " + error + "\n"; Debug.LogException(error); }
        File.WriteAllText("result.txt", result);
        Debug.Log(result);
        EditorApplication.Exit(passed ? 0 : 1);
    }

    private static void Cases()
    {
        var root = new GameObject("Visor material ownership", typeof(RectTransform));
        root.SetActive(false); // The production scan includes inactive descendants, including pooled TMP submeshes.
        Material stock = Graphic.defaultGraphicMaterial;
        var glitch = new Material(stock) { name = "Fixture visor material" };
        var custom = new Material(stock) { name = "Fixture custom material" };
        FieldInfo ui = Visor.GetField("ui", All), nextScan = Visor.GetField("nextScan", All);
        PropertyInfo disabled = Visor.GetProperty("Disabled", All);
        var roots = (IList)Visor.GetField("roots", All).GetValue(null);
        try
        {
            Image defaultImage = Child<Image>(root.transform, "Default image");
            Image customImage = Child<Image>(root.transform, "Custom image");
            customImage.material = custom;
            TextMeshProUGUI label = Child<TextMeshProUGUI>(root.transform, "TMP label");
            TMP_SubMeshUI submesh = Child<TMP_SubMeshUI>(label.transform, "Uninitialized fallback submesh");
            Check(label.fontSharedMaterial == null && submesh.sharedMaterial == null, "fixture has uninitialized text materials");
            Check(defaultImage.material == stock, "default image starts on the stock material");

            // A ready line-art material isolates ownership from optional asset-bundle loading; the real Scan/SetDisabled bodies run.
            ui.SetValue(null, glitch);
            nextScan.SetValue(null, 0f);
            disabled.SetValue(null, false);
            Call("Attach", root.transform);
            Call("Scan", 1f);
            Check(defaultImage.material == glitch, "default image takes the visor material");
            Check(customImage.material == custom, "custom image keeps its own material");
            Check(label.fontSharedMaterial == null && submesh.sharedMaterial == null, "TMP material getters were not invoked by the scan");
            Call("Scan", 2f);
            Check(defaultImage.material == glitch && customImage.material == custom, "repeat scan preserves both owners");

            Call("SetDisabled", true);
            Check(defaultImage.material == stock, "disabling restores the default image");
            Check(customImage.material == custom, "disabling preserves the custom image");
            Check(label.fontSharedMaterial == null && submesh.sharedMaterial == null, "disabling leaves TMP materials alone");
            Call("SetDisabled", true);
            Check(defaultImage.material == stock && customImage.material == custom, "repeated disable is stable");
        }
        finally
        {
            roots.Clear();
            ui.SetValue(null, null);
            nextScan.SetValue(null, 0f);
            disabled.SetValue(null, false);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(glitch);
            Object.DestroyImmediate(custom);
        }
    }

    private static T Child<T>(Transform parent, string name) where T : Component
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return child.AddComponent<T>();
    }

    private static void Call(string method, params object[] args) => Visor.GetMethod(method, All).Invoke(null, args);
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
