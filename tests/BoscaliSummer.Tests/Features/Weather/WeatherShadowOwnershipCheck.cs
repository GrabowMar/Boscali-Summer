#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using NOAvionics;
using UnityEngine;

// No rendering: invoke the final production LightState against actual Unity Light/URP data.
public static class WeatherShadowOwnershipCheck
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int assertions;
    public static void Run()
    {
        try
        {
            Type owner = typeof(AvConsole).Assembly.GetType("BoscaliSummer.Modules.Weather.Visuals.WeatherCloudShadows", true);
            Type stateType = owner.GetNestedType("LightState", BindingFlags.NonPublic);
            ConstructorInfo constructor = stateType.GetConstructors(All)[0];
            Type dataType = constructor.GetParameters()[1].ParameterType;
            PropertyInfo size = dataType.GetProperty("lightCookieSize"), offset = dataType.GetProperty("lightCookieOffset");
            MethodInfo before = stateType.GetMethod("BeforeWrite", All), wrote = stateType.GetMethod("Wrote", All), restore = stateType.GetMethod("Restore", All);
            for (int foreign = 0; foreign < 5; foreign++)
            {
                var root = new GameObject("Shadow native ownership " + foreign);
                Light light = root.AddComponent<Light>(); light.type = LightType.Directional; light.enabled = false;
                Component data = root.AddComponent(dataType);
                Texture2D baselineCookie = new Texture2D(2, 2), ownedCookie = new Texture2D(2, 2), foreignCookie = new Texture2D(2, 2);
                light.cookie = baselineCookie; size.SetValue(data, new Vector2(96, 48)); offset.SetValue(data, new Vector2(3, 4));
                light.transform.rotation = Quaternion.Euler(35, 22, 17);
                Quaternion baselineRotation = light.transform.rotation;
                object state = constructor.Invoke(new object[] { light, data });
                before.Invoke(state, new object[] { true });
                light.cookie = ownedCookie; size.SetValue(data, new Vector2(48000, 48000)); offset.SetValue(data, new Vector2(600, 700));
                light.transform.rotation = Quaternion.Euler(35, 22, 0); wrote.Invoke(state, new object[] { true });
                Quaternion replacement = Quaternion.Euler(-8, 37, 21);
                if (foreign == 1) light.cookie = foreignCookie;
                if (foreign == 2) size.SetValue(data, new Vector2(120, 140));
                if (foreign == 3) offset.SetValue(data, new Vector2(-3, -4));
                if (foreign == 4) light.transform.rotation = replacement;
                restore.Invoke(state, null); restore.Invoke(state, null);
                Check(light.cookie == (foreign == 1 ? foreignCookie : baselineCookie), "Cookie ownership case " + foreign);
                Check(((Vector2)size.GetValue(data)).Equals(foreign == 2 ? new Vector2(120, 140) : new Vector2(96, 48)), "Size ownership case " + foreign);
                Check(((Vector2)offset.GetValue(data)).Equals(foreign == 3 ? new Vector2(-3, -4) : new Vector2(3, 4)), "Offset ownership case " + foreign);
                Check(Same(light.transform.rotation, foreign == 4 ? replacement : baselineRotation), "Native roll ownership case " + foreign);

                // Native changes before the next write become the baseline for that write.
                before.Invoke(state, new object[] { true });
                light.cookie = ownedCookie; size.SetValue(data, new Vector2(48000, 48000)); offset.SetValue(data, new Vector2(1, 2));
                light.transform.rotation = Quaternion.identity; wrote.Invoke(state, new object[] { true });
                light.cookie = foreignCookie; size.SetValue(data, new Vector2(333, 444)); offset.SetValue(data, new Vector2(-8, 9));
                light.transform.rotation = replacement;
                before.Invoke(state, new object[] { true });
                light.cookie = ownedCookie; size.SetValue(data, new Vector2(48000, 48000)); offset.SetValue(data, Vector2.zero);
                light.transform.rotation = Quaternion.identity; wrote.Invoke(state, new object[] { true });
                restore.Invoke(state, null);
                Check(light.cookie == foreignCookie && ((Vector2)size.GetValue(data)).Equals(new Vector2(333, 444)) &&
                    ((Vector2)offset.GetValue(data)).Equals(new Vector2(-8, 9)) && Same(light.transform.rotation, replacement),
                    "Native replacements rebase individually before another shadow write " + foreign);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(baselineCookie); UnityEngine.Object.DestroyImmediate(ownedCookie); UnityEngine.Object.DestroyImmediate(foreignCookie);
            }
            CheckShelterOwnership();
            CheckNativeFilterOwnership();
            File.WriteAllText("shadow-result.txt", "PASS: " + assertions + " current-DLL native LightState/filter/shelter assertions");
            UnityEditor.EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText("shadow-result.txt", "FAIL " + e); UnityEditor.EditorApplication.Exit(1); }
    }
    private static void Check(bool value, string behavior)
    {
        if (!value) throw new InvalidOperationException(behavior);
        assertions++; Debug.Log("PASS " + behavior);
    }
    private static bool Same(Quaternion a, Quaternion b) => Math.Abs(Quaternion.Dot(a, b)) > .99999f;

    private static void CheckNativeFilterOwnership()
    {
        Type type = typeof(AvConsole).Assembly.GetType("BoscaliSummer.Modules.Immersion.Audio.CockpitAudioFilter", true);
        MethodInfo tick = type.GetMethod("Tick", All), release = type.GetMethod("Release", All);
        for (int scenario = 0; scenario < 3; scenario++)
        {
            var root = new GameObject("Native borrowed filter ownership " + scenario);
            Camera camera = root.AddComponent<Camera>(); camera.enabled = false;
            root.AddComponent<AudioListener>();
            var native = root.AddComponent<AudioLowPassFilter>();
            native.enabled = scenario == 2; native.cutoffFrequency = 7777f; native.lowpassResonanceQ = 1.5f;
            object owner = Activator.CreateInstance(type, true);
            object[] arguments = { camera, 1f, 0f, true, true, 1f };
            tick.Invoke(owner, arguments); tick.Invoke(owner, arguments);
            Check(native.enabled && native.cutoffFrequency < 7777f, "High exposure borrows native filter " + scenario);
            native.lowpassResonanceQ = 2.5f;
            if (scenario < 2) native.cutoffFrequency = 5555f;
            else native.enabled = false;
            if (scenario == 1) tick.Invoke(owner, arguments);
            release.Invoke(owner, null); release.Invoke(owner, null);
            Check(!native.enabled, "Native/foreign disabled state survives release " + scenario);
            Check(native.cutoffFrequency == (scenario < 2 ? 5555f : 7777f),
                "Foreign cutoff and independent original cutoff restore " + scenario);
            Check(native.lowpassResonanceQ == 2.5f, "Foreign resonance remains unowned " + scenario);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void CheckShelterOwnership()
    {
        Type manager = typeof(AvConsole).Assembly.GetType("BoscaliSummer.Modules.Weather.Runtime.WeatherManager", true);
        MethodInfo hasShelter = manager.GetMethod("HasShelter", BindingFlags.Static | BindingFlags.NonPublic);
        Check(hasShelter != null, "Production shelter helper exists");
        Vector3 origin = new Vector3(12345f, 500f, 54321f);
        var aircraft = new GameObject("Ownship shelter physics fixture"); aircraft.transform.position = origin;
        var cockpit = new GameObject("Detached cockpit shelter physics fixture"); cockpit.transform.position = origin;
        for (int i = 0; i < 12; i++)
        {
            var part = new GameObject("Own collider " + i); part.transform.SetParent(i % 2 == 0 ? aircraft.transform : cockpit.transform, false);
            part.transform.localPosition = new Vector3(0, 1f + i * .5f, 0);
            part.AddComponent<BoxCollider>().size = new Vector3(1, .1f, 1);
        }
        Physics.SyncTransforms();
        var hits = new RaycastHit[8];
        int count = Physics.RaycastNonAlloc(origin, Vector3.up, hits, 20f, ~0, QueryTriggerInteraction.Ignore);
        Check(count == hits.Length, "Real ownship/cockpit colliders saturate eight-hit buffer");
        Check(!(bool)hasShelter.Invoke(null, new object[] { hits, count, aircraft.transform, cockpit.transform }),
            "Ownship/cockpit overflow cannot invent foreign shelter");
        aircraft.SetActive(false); cockpit.SetActive(false);
        var roof = new GameObject("Foreign roof physics fixture"); roof.transform.position = origin + Vector3.up * 10f;
        roof.AddComponent<BoxCollider>().size = new Vector3(10, .3f, 10);
        Physics.SyncTransforms();
        count = Physics.RaycastNonAlloc(origin, Vector3.up, hits, 20f, ~0, QueryTriggerInteraction.Ignore);
        Check(count == 1 && (bool)hasShelter.Invoke(null, new object[] { hits, count, aircraft.transform, cockpit.transform }),
            "A real foreign roof provides shelter");
        UnityEngine.Object.DestroyImmediate(aircraft); UnityEngine.Object.DestroyImmediate(cockpit); UnityEngine.Object.DestroyImmediate(roof);
    }
}
#endif
