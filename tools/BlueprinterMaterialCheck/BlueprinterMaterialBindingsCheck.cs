#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Calls the installed Blueprinter setter, not a reimplementation of its parser.
public static class BlueprinterMaterialBindingsCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private const string Owner = "com.marci.boscalisummer.blueprinter-material-bindings";
    private static int assertions;
    private static MethodInfo setter, install, dispose;

    private sealed class Legacy
    {
        public Texture Field;
        public Texture Property { get; set; }
        public List<Texture> Items = new List<Texture> { null };
    }

    public static void Run()
    {
        string result;
        try
        {
            Assembly mod = Assembly.Load("BoscaliSummer");
            Type adapter = mod.GetType("BoscaliSummer.Core.Game.BlueprinterMaterialBindings", true);
            install = adapter.GetMethod("Install", All);
            dispose = adapter.GetMethod("Dispose", All);
            dispose.Invoke(null, null);
            Assembly blueprinter = Assembly.LoadFrom(Path.GetFullPath("Assets/Blueprinter_2.0.1.dll"));
            Check(blueprinter.GetName().Name == "com.nikkorap.blueprinter" &&
                  blueprinter.GetName().Version == new Version(2, 0, 1, 0), "uses the installed Blueprinter 2.0.1 assembly");
            setter = blueprinter.GetType("Blueprinter.MemberPathSetter", true).GetMethod("TryApply", All);
            Cases();
            result = "PASS: " + assertions + " actual Blueprinter 2.0.1 material binding assertions; pre-install failure, installed texture identity/render, legacy and invalid controls, idempotency and uninstall. Offline Unity editor; no game asset, multiplayer or performance claim.\n";
        }
        catch (Exception error) { result = "FAIL: " + error + "\n"; Debug.LogException(error); }
        finally { dispose?.Invoke(null, null); }
        File.WriteAllText("result.txt", result);
        Debug.Log(result);
        EditorApplication.Exit(result.StartsWith("PASS:", StringComparison.Ordinal) ? 0 : 1);
    }

    private static void Cases()
    {
        Shader shader = Shader.Find("Hidden/Boscali/BlueprinterMaterialCheck");
        Check(shader != null && shader.isSupported, "fixture texture shader is available");
        var material = new Material(shader);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        texture.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red });
        texture.Apply();
        try
        {
            const string path = "materialTexture::_Normal_Crinkle";
            Check(!Apply(material, path, texture) && material.GetTexture("_Normal_Crinkle") == null,
                "unpatched actual runtime rejects the editor material texture path");
            install.Invoke(null, new object[] { null });
            install.Invoke(null, new object[] { null });
            Check(Harmony.GetPatchInfo(setter).Postfixes.Count(p => p.owner == Owner) == 1,
                "repeated installation adds exactly one adapter");
            Check(Apply(material, path, texture) && material.GetTexture("_Normal_Crinkle") == texture,
                "actual runtime applies the texture with adapter installed");
            Check(RenderedRed(material), "the bound crinkle texture reaches the material's rendered output");

            Check(Apply(material, "mainTexture", texture) && material.mainTexture == texture,
                "successful native material property assignment is preserved");
            var legacy = new Legacy();
            Check(Apply(legacy, "Field", texture) && legacy.Field == texture, "legacy field path still works");
            Check(Apply(legacy, "Property", texture) && legacy.Property == texture, "legacy property path still works");
            Check(Apply(legacy, "Items[0]", texture) && legacy.Items[0] == texture, "legacy indexed path still works");
            Check(!Apply(material, "materialTexture::_Missing", texture), "unknown shader property remains rejected");
            Check(!Apply(material, "materialTexture::_Amount", texture) && material.GetFloat("_Amount") == .5f,
                "a non-texture shader property remains rejected and unchanged");
            Check(!Apply(material, path, new object()) && material.GetTexture("_Normal_Crinkle") == texture,
                "non-texture assets remain rejected without replacing the binding");
            Check(!Apply(legacy, path, texture), "a non-material target remains rejected");
            Check(!Apply(material, "materialTexture::", texture), "an empty texture property remains rejected");
            Check(!Apply(material, "materialtexture::_Normal_Crinkle", texture), "other path syntax remains rejected");
            Check(!Apply(null, path, texture), "null target remains rejected");

            dispose.Invoke(null, null);
            dispose.Invoke(null, null);
            material.SetTexture("_Normal_Crinkle", null);
            Check(!Apply(material, path, texture) && material.GetTexture("_Normal_Crinkle") == null,
                "uninstall restores the actual runtime's baseline failure");
            Check(Harmony.GetPatchInfo(setter)?.Postfixes.All(p => p.owner != Owner) != false,
                "uninstall releases the adapter patch");
        }
        finally { Object.DestroyImmediate(material); Object.DestroyImmediate(texture); }
    }

    private static bool RenderedRed(Material material)
    {
        var target = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var pixels = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            target.Create();
            Graphics.Blit(null, target, material);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
            pixels.Apply();
            Color pixel = pixels.GetPixel(2, 2);
            return pixel.r > .95f && pixel.g < .05f && pixel.b < .05f;
        }
        finally { RenderTexture.active = previous; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); }
    }

    private static bool Apply(object target, string path, object asset) => (bool)setter.Invoke(null, new[] { target, path, asset });
    private static void Check(bool value, string description) { assertions++; if (!value) throw new InvalidOperationException(description); }
}
#endif
