#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

/// <summary>Offline production-marker geometry and owned smoke, with a synthetic HUD/minimap and ground scene.</summary>
public static class LandingZoneUnityCheck
{
    private static int assertions;
    private static Type ModType(string name) => typeof(AvBundle).Assembly.GetType(name, true);
    private static void Assert(bool pass, string text) { Check(pass, text); assertions++; }

    public static void Run()
    {
        try
        {
            if (!EnsureTmpEssentials(Run)) return;
            SetExecutablePath("LandingZoneCheck.exe");
            InitAvionics();
            Markers();
            Smoke();
            Finish("PASS: " + assertions + " production-marker/input/particle/origin/lifecycle assertions, three PNG renders. " +
                "Synthetic native HUD/minimap/ground and built-in smoke shader fallback. " +
                "No live camera/map adapter, native terrain, actual input, multiplayer or in-game acceptance.", true);
        }
        catch (Exception error) { Debug.LogException(error); Finish("FAIL: " + error, false); }
    }

    private static object Marker(RectTransform parent, string name, bool hud, Vector2 at, string text, bool pinned = false, float bearing = 0f)
    {
        object marker = Activator.CreateInstance(ModType("BoscaliSummer.Garrisons.LandingZoneMarker"), All, null,
            new object[] { parent, name, hud }, null);
        Call(marker, "Show", at, text, new Color(.5f, 1f, .6f), pinned, bearing);
        return marker;
    }

    private static void Markers()
    {
        var go = new GameObject("Synthetic HUD and minimap", typeof(RectTransform), typeof(Canvas));
        var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)go.transform; rect.sizeDelta = new Vector2(1280f, 720f);
        object front = Marker(rect, "Ground fix", true, new Vector2(-240f, 80f), "LZ  4.2 KM");
        object edge = Marker(rect, "Offscreen fix", true, new Vector2(480f, 150f), "LZ  12.8 KM", true, 25f);
        var map = AvLay.Child(rect, "Synthetic minimap viewport");
        map.anchorMin = map.anchorMax = map.pivot = new Vector2(.5f, .5f);
        map.sizeDelta = new Vector2(260f, 210f); map.anchoredPosition = new Vector2(410f, -180f);
        var back = AvLay.Solid(map, "Map backing", new Color(.04f, .1f, .09f)); back.raycastTarget = false;
        back.rectTransform.anchorMin = Vector2.zero; back.rectTransform.anchorMax = Vector2.one;
        back.rectTransform.offsetMin = back.rectTransform.offsetMax = Vector2.zero;
        object mapFix = Marker(map, "Minimap landing zone", false, new Vector2(-25f, 20f), "LZ");
        foreach (TMP_Text label in rect.GetComponentsInChildren<TMP_Text>(true))
            if (!label.name.StartsWith("Icon"))
            {
                label.font = AvBundle.Font("NOA JetBrains Mono Regular SDF");
                label.fontSharedMaterial = label.font.material;
            }
        Canvas.ForceUpdateCanvases();
        foreach (Graphic graphic in rect.GetComponentsInChildren<Graphic>(true))
            Assert(!graphic.raycastTarget, "Every landing-zone graphic yields native flight and map input.");
        Assert(((AvVector)Field(front, "brackets")).enabled && !((AvVector)Field(front, "pointer")).gameObject.activeSelf,
            "An on-screen fix draws ground brackets and no offscreen pointer.");
        Assert(!((AvVector)Field(edge, "brackets")).enabled && ((AvVector)Field(edge, "pointer")).gameObject.activeSelf,
            "An offscreen fix replaces ground brackets with a bearing pointer.");
        Assert(((TMP_Text)Field(mapFix, "label")).text == "LZ", "The minimap identifies the drop zone in text as well as by icon.");
        Camera camera = OrthoCamera("Marker render camera", 360f, new Color(.25f, .42f, .59f));
        CapturePng(camera, 1280, 720, "landing-zone-markers.png");
        Call(mapFix, "Show", new Vector2(98f, 40f), "LZ", new Color(.5f, 1f, .6f), true, 22f);
        Canvas.ForceUpdateCanvases();
        CapturePng(camera, 1280, 720, "landing-zone-minimap-edge.png");
        Assert(((AvVector)Field(mapFix, "pointer")).gameObject.activeSelf, "A distant minimap zone has an edge-bearing indication.");
        foreach (object marker in new[] { front, edge, mapFix })
        {
            Call(marker, "SetVisible", false);
            Assert(!((RectTransform)Field(marker, "Root")).gameObject.activeSelf, "Zone markers hide synchronously.");
        }
        Object.DestroyImmediate(go); Object.DestroyImmediate(camera.gameObject);
    }

    private static void Smoke()
    {
        var origin = new GameObject("Synthetic Datum origin").transform;
        typeof(Datum).GetProperty("origin", All).SetValue(null, origin);
        typeof(Datum).GetProperty("originPosition", All).SetValue(null, Vector3.zero);
        var smoke = (GameObject)CallStatic(ModType("BoscaliSummer.Core.Fx.LandingSmoke"), "Mark", Vector3.zero);
        Assert(smoke != null && smoke.transform.parent == origin, "Selected-zone smoke belongs to the floating origin.");
        var particles = smoke.GetComponent<ParticleSystem>();
        particles.useAutoRandomSeed = false; particles.randomSeed = 7;
        particles.Simulate(6f, true, true, true);
        Assert(particles.main.loop && particles.main.maxParticles == 96 && particles.particleCount > 0 && particles.particleCount <= 96,
            "Exactly one looping plume stays within its 96-particle budget.");
        Assert(particles.main.simulationSpace == ParticleSystemSimulationSpace.Local && !particles.collision.enabled && !particles.lights.enabled,
            "The plume follows the origin and adds no collisions or particle lights.");
        var samples = new ParticleSystem.Particle[96]; int count = particles.GetParticles(samples);
        Assert(samples.Take(count).Any(p => { Color32 colour = p.GetCurrentColor(particles); return colour.g > colour.r && colour.g > colour.b; }),
            "Evaluated particles carry green smoke colour.");
        Vector3 before = particles.transform.TransformPoint(samples[0].position), local = samples[0].position;
        Vector3 shift = new Vector3(1200f, -300f, 900f); origin.position += shift;
        particles.GetParticles(samples);
        Assert((samples[0].position - local).sqrMagnitude < .00001f &&
            (particles.transform.TransformPoint(samples[0].position) - before - shift).sqrMagnitude < .001f,
            "An origin shift carries the plume without moving particles in their simulation space.");
        origin.position = Vector3.zero;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.transform.localScale = Vector3.one * 6f;
        ground.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = new Color(.16f, .22f, .16f) };
        Camera camera = OrthoCamera("Smoke render camera", 10f, new Color(.34f, .47f, .56f), new Vector3(24f, 14f, -28f));
        camera.transform.LookAt(new Vector3(0f, 5f, 0f));
        CapturePng(camera, 1280, 720, "landing-zone-smoke.png");
        Object.DestroyImmediate(smoke);
        Assert(origin.GetComponentsInChildren<ParticleSystem>().Length == 0, "Removing the owned plume leaves no emitter behind.");
        Object.DestroyImmediate(origin.gameObject); Object.DestroyImmediate(ground); Object.DestroyImmediate(camera.gameObject);
        typeof(Datum).GetProperty("origin", All).SetValue(null, null);
        typeof(Datum).GetProperty("originPosition", All).SetValue(null, Vector3.zero);
    }
}
#endif
