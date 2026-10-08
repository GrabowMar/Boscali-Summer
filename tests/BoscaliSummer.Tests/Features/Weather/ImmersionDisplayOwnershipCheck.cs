#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Actual candidate DLL, native Unity materials/MPBs, and deferred destruction in play mode.
// The wrapper starts this isolated editor with -nographics; no camera or texture is rendered.
public static class ImmersionDisplayOwnershipCheck
{
    private const string Pending = "Boscali.DisplayOwnership.Pending";
    private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static bool started;
    private static int assertions;
    private static readonly List<Material> disposed = new List<Material>();
    private static readonly List<UnityEngine.Object> fixtures = new List<UnityEngine.Object>();
    private static Type ownerType;
    private static MethodInfo bind, bindOpaque, apply, restore;
    private static PropertyInfo active;
    private static Exception failure;
    private static int releaseFrame;

    public static void Run()
    {
        SessionState.SetBool(Pending, true);
        Register();
        if (EditorApplication.isPlaying) Begin();
        else EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        Register();
        if (EditorApplication.isPlaying) EditorApplication.delayCall += Begin;
    }

    private static void Register()
    {
        EditorApplication.playModeStateChanged -= Changed;
        EditorApplication.playModeStateChanged += Changed;
    }

    private static void Changed(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) Begin();
    }

    private static void Begin()
    {
        if (started || !SessionState.GetBool(Pending, false)) return;
        started = true;
        CheckLifecycle();
    }

    private static void CheckLifecycle()
    {
        try
        {
            Assembly candidate = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(Application.dataPath), "Candidate", "BoscaliSummer.dll"));
            ownerType = candidate.GetType("BoscaliSummer.Modules.Immersion.Visuals.MaterialSlotClone", true);
            bind = ownerType.GetMethod("TryBindDisplay", All); bindOpaque = ownerType.GetMethod("TryBindOpaque", All);
            apply = ownerType.GetMethod("Apply", All);
            restore = ownerType.GetMethod("Restore", All); active = ownerType.GetProperty("Active", All);
            CheckTexturelessMaterial(); CheckMainDisplay(); CheckEmissionDisplay(); CheckBaseDisplay(); CheckOpaqueSurface(); CheckNativeDamageOwnership();
        }
        catch (Exception e) { failure = e; }
        // Real deferred Unity destruction, rather than manually destroying the owner's clones.
        releaseFrame = Time.frameCount + 2;
        EditorApplication.update += AfterUnityFrames;
    }

    private static void AfterUnityFrames()
    {
        if (Time.frameCount < releaseFrame) return;
        EditorApplication.update -= AfterUnityFrames;
        if (failure == null)
        {
            try { foreach (Material clone in disposed) Check(clone == null, "Released display clone is destroyed after Unity frames"); }
            catch (Exception e) { failure = e; }
        }
        foreach (UnityEngine.Object fixture in fixtures) if (fixture != null) UnityEngine.Object.Destroy(fixture);
        SessionState.SetBool(Pending, false);
        File.WriteAllText("display-result.txt", failure == null ?
            "PASS: " + assertions + " actual-DLL native display/surface material/MPB/deferred cleanup assertions" : "FAIL " + failure);
        EditorApplication.Exit(failure == null ? 0 : 1);
    }

    private static void CheckMainDisplay()
    {
        Shader shader = Shader.Find("Standard"); Check(shader != null, "Native Standard display shader available");
        Material native = Fixture(new Material(shader)), other = Fixture(new Material(shader)), replacement = Fixture(new Material(shader));
        RenderTexture screen = Fixture(new RenderTexture(8, 8, 0)), updateScreen = Fixture(new RenderTexture(16, 8, 0));
        native.SetTexture("_MainTex", screen); native.SetColor("_Color", new Color(.4f, .3f, .2f, .6f));
        native.SetColor("_EmissionColor", Color.black); native.DisableKeyword("_EMISSION"); native.SetFloat("_Glossiness", .37f);
        MeshRenderer renderer = Renderer(native, other);
        Check(Empty(renderer, -1) && Empty(renderer, 0), "Native display begins with empty renderer and slot MPBs");
        object owner = Bind(renderer, native, 0); Check(owner != null, "Verified main RenderTexture slot binds without MPB");
        Material clone = renderer.sharedMaterials[0];
        Check(clone != native && renderer.sharedMaterials[1] == other, "Only verified display slot is cloned");
        Apply(owner, 1.15f);
        Check(Same(clone.GetColor("_Color"), new Color(.46f, .345f, .23f, .6f)), "Main display tint gains brightness and preserves alpha");
        Check(Same(native.GetColor("_Color"), new Color(.4f, .3f, .2f, .6f)), "Native display material remains unmodified");
        Check(Empty(renderer, -1) && Empty(renderer, 0), "Material glow introduces no renderer or slot MPB");

        native.SetColor("_Color", new Color(.6f, .2f, .1f, .73f)); native.SetTexture("_MainTex", updateScreen);
        native.SetFloat("_Glossiness", .75f); native.renderQueue = 3012; native.EnableKeyword("_EMISSION");
        for (int i = 0; i < 1000; i++) Apply(owner, 1000f);
        Check(renderer.sharedMaterials[0] == clone, "Repeated glow ticks reuse one owned clone");
        Check(Same(clone.GetColor("_Color"), new Color(.69f, .23f, .115f, .73f)), "Current native tint copied before bounded non-ratcheting gain");
        Check(clone.GetTexture("_MainTex") == updateScreen && clone.GetFloat("_Glossiness") == .75f &&
            clone.renderQueue == 3012 && clone.IsKeywordEnabled("_EMISSION"), "Native texture/properties/render state updates copied");
        Apply(owner, -10f); Check(Same(clone.GetColor("_Color"), native.GetColor("_Color")), "Negative gain clamps to neutral");
        Apply(owner, float.NaN); Check(Same(clone.GetColor("_Color"), native.GetColor("_Color")), "Nonfinite gain is neutral");

        var wide = new MaterialPropertyBlock(); wide.SetFloat("_ForeignWide", 31f); wide.SetColor("_Color", new Color(.1f, .8f, .4f, .9f));
        var slot = new MaterialPropertyBlock(); slot.SetFloat("_ForeignSlot", 62f); slot.SetColor("_Color", new Color(.8f, .4f, .2f, .7f));
        renderer.SetPropertyBlock(wide); renderer.SetPropertyBlock(slot, 0);
        Apply(owner, 1.15f); CheckBlocks(renderer, wide, slot);
        Restore(owner); Restore(owner); disposed.Add(clone);
        Check(renderer.sharedMaterials[0] == native && renderer.sharedMaterials[1] == other && !Active(owner), "Repeated release restores exact native slots and clears ownership");
        CheckBlocks(renderer, wide, slot);

        owner = Bind(renderer, native, 0); clone = renderer.sharedMaterials[0];
        renderer.sharedMaterials = new[] { replacement, other };
        Apply(owner, 1.15f); Restore(owner); Restore(owner); disposed.Add(clone);
        Check(renderer.sharedMaterials[0] == replacement && !Active(owner), "Foreign display slot replacement survives tick and repeated release");
        CheckBlocks(renderer, wide, slot);
        Check(Bind(renderer, native, 0) == null && Bind(renderer, native, 99) == null && Bind(renderer, replacement, 0) == null,
            "Unsupported texture, mismatched material and invalid slot are rejected");

        MeshRenderer emptyRenderer = Renderer(native, other); owner = Bind(emptyRenderer, native, 0); clone = emptyRenderer.sharedMaterials[0];
        Apply(owner, 1.1f); Restore(owner); Restore(owner); disposed.Add(clone);
        Check(Empty(emptyRenderer, -1) && Empty(emptyRenderer, 0), "Originally empty MPBs remain empty after glow cleanup");
    }

    private static void CheckTexturelessMaterial()
    {
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        Check(shader != null, "Textureless shader fixture exists");
        Material native = Fixture(new Material(shader));
        Check(!native.HasProperty("_MainTex"), "Fixture has no default main texture property");
        MeshRenderer renderer = Renderer(native);
        int errors = 0;
        Application.LogCallback observe = (message, trace, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception) errors++;
        };
        Application.logMessageReceived += observe;
        try
        {
            Check(Bind(renderer, native, 0) == null, "Textureless canopy is rejected as a display");
            object owner = BindOpaque(renderer, native, 0);
            if (owner != null) Restore(owner);
            Check(errors == 0, "Inspecting a textureless material emits no Unity errors");
        }
        finally { Application.logMessageReceived -= observe; }
    }

    private static void CheckEmissionDisplay()
    {
        Material native = Fixture(new Material(Shader.Find("Standard")));
        RenderTexture screen = Fixture(new RenderTexture(8, 8, 0));
        native.SetTexture("_MainTex", null); native.SetTexture("_EmissionMap", screen);
        native.SetColor("_Color", new Color(.8f, .7f, .6f, .9f)); native.SetColor("_EmissionColor", new Color(.2f, .4f, .6f, .8f));
        MeshRenderer renderer = Renderer(native); object owner = Bind(renderer, native, 0);
        Check(owner != null, "Emission-only RenderTexture display binds"); Material clone = renderer.sharedMaterials[0];
        Apply(owner, 1.15f);
        Check(Same(clone.GetColor("_EmissionColor"), new Color(.23f, .46f, .69f, .8f)) &&
            Same(clone.GetColor("_Color"), native.GetColor("_Color")), "Emission display boosts emission only and preserves tint/alpha");
        native.SetColor("_EmissionColor", new Color(.1f, .5f, .9f, .55f)); Apply(owner, 1.15f);
        Check(Same(clone.GetColor("_EmissionColor"), new Color(.115f, .575f, 1.035f, .55f)), "Native emission updates copy before gain");
        Restore(owner); Restore(owner); disposed.Add(clone);
        Check(renderer.sharedMaterials[0] == native && Empty(renderer, 0), "Emission display teardown restores native material without MPB");
    }

    private static void CheckBaseDisplay()
    {
        Shader shader = Shader.Find("Hidden/Boscali/DisplayOwnershipFixture"); Check(shader != null, "Base-map display fixture shader available");
        Material native = Fixture(new Material(shader)); RenderTexture screen = Fixture(new RenderTexture(8, 8, 0));
        native.SetTexture("_BaseMap", screen); native.SetColor("_BaseColor", new Color(.3f, .5f, .7f, .45f));
        native.SetColor("_Color", new Color(.9f, .8f, .7f, .65f)); native.SetColor("_EmissionColor", new Color(2, 3, 4, .7f));
        MeshRenderer renderer = Renderer(native); object owner = Bind(renderer, native, 0);
        Check(owner != null, "Base-map RenderTexture display binds"); Material clone = renderer.sharedMaterials[0]; Apply(owner, 1.15f);
        Check(Same(clone.GetColor("_BaseColor"), new Color(.345f, .575f, .805f, .45f)) &&
            Same(clone.GetColor("_Color"), native.GetColor("_Color")) && Same(clone.GetColor("_EmissionColor"), native.GetColor("_EmissionColor")),
            "Base display RT chooses base tint despite unrelated emission brightness");
        Restore(owner); disposed.Add(clone); Check(renderer.sharedMaterials[0] == native, "Base display restores native slot");
    }

    private static void CheckOpaqueSurface()
    {
        Material native = Fixture(new Material(Shader.Find("Standard")));
        native.SetColor("_Color", new Color(.8f, .6f, .4f, 1f)); native.SetColor("_EmissionColor", new Color(0f, 0f, 0f, 1.5f));
        native.DisableKeyword("_EMISSION"); native.renderQueue = 2000;
        MeshRenderer renderer = Renderer(native); object owner = BindOpaque(renderer, native, 0);
        Check(owner != null, "Verified opaque native surface binds for bounded damage presentation");
        Material clone = renderer.sharedMaterials[0]; Apply(owner, .1f);
        Check(Same(clone.GetColor("_Color"), new Color(.704f, .528f, .352f, 1f)), "Opaque surface gain clamps to .88 and preserves alpha");
        native.SetColor("_Color", new Color(.6f, .4f, .2f, .995f)); native.SetFloat("_Glossiness", .19f);
        Apply(owner, .94f);
        Check(Same(clone.GetColor("_Color"), new Color(.564f, .376f, .188f, .995f)) && clone.GetFloat("_Glossiness") == .19f,
            "Opaque surface follows current native material before damage gain");
        Apply(owner, 1000f); Check(Same(clone.GetColor("_Color"), native.GetColor("_Color")), "Opaque surface gain cannot brighten beyond native material");
        Restore(owner); Restore(owner); disposed.Add(clone);
        Check(renderer.sharedMaterials[0] == native && !Active(owner) && Empty(renderer, -1) && Empty(renderer, 0),
            "Repair-equivalent repeated opaque restore returns native material and leaves empty MPBs");

        native.renderQueue = 3000;
        Check(BindOpaque(renderer, native, 0) == null, "Transparent surface queue is excluded from damage cloning");
        native.renderQueue = 2000; native.SetColor("_Color", new Color(.6f, .4f, .2f, .5f));
        Check(BindOpaque(renderer, native, 0) == null, "Nonopaque native alpha is excluded from damage cloning");
        native.SetColor("_Color", Color.white); native.SetColor("_EmissionColor", Color.white); native.EnableKeyword("_EMISSION");
        Check(BindOpaque(renderer, native, 0) == null, "Emissive native surface is excluded from damage cloning");
        native.SetColor("_EmissionColor", Color.black); native.DisableKeyword("_EMISSION");
        RenderTexture screen = Fixture(new RenderTexture(8, 8, 0)); native.SetTexture("_MainTex", screen);
        Check(BindOpaque(renderer, native, 0) == null, "Verified display RT cannot enter the opaque damage path");

        native = Fixture(new Material(Shader.Find("Hidden/Boscali/DisplayOwnershipFixture")));
        native.SetColor("_BaseColor", new Color(.7f, .5f, .3f, 1f)); native.SetColor("_Color", Color.white);
        native.SetColor("_EmissionColor", Color.black); native.renderQueue = 2000; renderer = Renderer(native);
        owner = BindOpaque(renderer, native, 0); Check(owner != null, "Opaque BaseColor surface binds"); clone = renderer.sharedMaterials[0]; Apply(owner, .88f);
        Check(Same(clone.GetColor("_BaseColor"), new Color(.616f, .44f, .264f, 1f)) && Same(clone.GetColor("_Color"), Color.white),
            "Opaque BaseColor path changes only the intended tint");
        Restore(owner); disposed.Add(clone);
    }

    private static void CheckNativeDamageOwnership()
    {
        foreach (string kind in new[] { "Damage", "Livery" })
        {
            Shader shader = Shader.Find("Hidden/Boscali/Native" + kind + "OwnershipFixture");
            Check(shader != null, "Native " + kind + " shader fixture exists");
            Material native = Fixture(new Material(shader)); native.renderQueue = 2000;
            MeshRenderer renderer = Renderer(native);
            Check(BindOpaque(renderer, native, 0) == null && renderer.sharedMaterials[0] == native,
                "Native-owned " + kind + " shader is rejected before cloning");
            // Match UnitPart.ApplyDamage/Aircraft.SetLivery's material access: writes must
            // reach the renderer's native instance and remain present after a rejected bind.
            Material runtimeNative = Fixture(renderer.material);
            string property = kind == "Damage" ? "_HitPoints" : "_Livery";
            runtimeNative.SetFloat(property, .35f); runtimeNative.SetFloat("_Glossiness", .52f);
            Check(renderer.sharedMaterials[0] == runtimeNative && renderer.sharedMaterials[0].GetFloat(property) == .35f &&
                renderer.sharedMaterials[0].GetFloat("_Glossiness") == .52f, "Native " + kind + " renderer.material updates remain reachable");
            Check(BindOpaque(renderer, runtimeNative, 0) == null, "Updated native " + kind + " instance remains excluded");
            RenderTexture screen = Fixture(new RenderTexture(8, 8, 0)); runtimeNative.SetTexture("_MainTex", screen);
            Check(Bind(renderer, runtimeNative, 0) == null && renderer.sharedMaterials[0] == runtimeNative,
                "Native-owned " + kind + " shader cannot enter display cloning through a RenderTexture");
            runtimeNative.SetFloat(property, .65f);
            Check(renderer.sharedMaterials[0].GetFloat(property) == .65f, "Native " + kind + " updates remain reachable after display rejection");
        }
    }

    private static T Fixture<T>(T value) where T : UnityEngine.Object { fixtures.Add(value); return value; }
    private static MeshRenderer Renderer(params Material[] materials)
    {
        GameObject root = Fixture(new GameObject("Native display slots fixture"));
        MeshRenderer renderer = root.AddComponent<MeshRenderer>(); renderer.enabled = false; renderer.sharedMaterials = materials; return renderer;
    }
    private static object Bind(Renderer renderer, Material native, int slot) => bind.Invoke(null, new object[] { renderer, native, slot });
    private static object BindOpaque(Renderer renderer, Material native, int slot) => bindOpaque.Invoke(null, new object[] { renderer, native, slot });
    private static void Apply(object owner, float gain) => apply.Invoke(owner, new object[] { gain });
    private static void Restore(object owner) => restore.Invoke(owner, null);
    private static bool Active(object owner) => (bool)active.GetValue(owner);
    private static bool Empty(Renderer renderer, int slot)
    {
        var block = new MaterialPropertyBlock(); if (slot < 0) renderer.GetPropertyBlock(block); else renderer.GetPropertyBlock(block, slot); return block.isEmpty;
    }
    private static void CheckBlocks(Renderer renderer, MaterialPropertyBlock wide, MaterialPropertyBlock slot)
    {
        var actual = new MaterialPropertyBlock(); renderer.GetPropertyBlock(actual);
        Check(actual.GetFloat("_ForeignWide") == wide.GetFloat("_ForeignWide") && Same(actual.GetColor("_Color"), wide.GetColor("_Color")), "Foreign renderer-wide MPB remains authoritative");
        renderer.GetPropertyBlock(actual, 0);
        Check(actual.GetFloat("_ForeignSlot") == slot.GetFloat("_ForeignSlot") && Same(actual.GetColor("_Color"), slot.GetColor("_Color")), "Foreign slot MPB remains authoritative");
    }
    private static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) < .0001f && Mathf.Abs(a.g - b.g) < .0001f &&
        Mathf.Abs(a.b - b.b) < .0001f && Mathf.Abs(a.a - b.a) < .0001f;
    private static void Check(bool value, string behavior)
    {
        if (!value) throw new InvalidOperationException(behavior); assertions++; Debug.Log("PASS " + behavior);
    }
}
#endif
