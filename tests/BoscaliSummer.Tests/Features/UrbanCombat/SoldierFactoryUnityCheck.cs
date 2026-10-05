#if UNITY_5_3_OR_NEWER
// Run with Run-SoldierFactoryUnityCheck.ps1. Exercises the production visual-soldier
// strip against a stub pilot prefab: network/seat parts gone, body and animator kept,
// the template shared across a stick and rebuilt when the prefab changes.
using System;
using System.Reflection;
using BoscaliSummer.Garrisons;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace Mirage
{
    public class NetworkBehaviour : MonoBehaviour { }
    public class NetworkIdentity : MonoBehaviour { }
}

public class EjectionSeat : MonoBehaviour { }

public class SlingloadHook : MonoBehaviour { }

public class GameAssets : MonoBehaviour
{
    public static GameAssets i;
    public GameObject pilotDismounted;
}

public class Encyclopedia : MonoBehaviour
{
    public static Encyclopedia i;
    public System.Collections.Generic.List<BuildingDefinition> buildings;
}

public class BuildingDefinition : MonoBehaviour
{
    public string jsonKey;
    public GameObject unitPrefab;
}

public static class SoldierFactoryUnityCheck
{
    private static int checks;

    public static void Run()
    {
#if UNITY_EDITOR
        // The factory parks its staging root with DontDestroyOnLoad, which only runs
        // in Play Mode. Enter it; the hook below restarts the body once play begins,
        // with or without a domain reload.
        if (!EditorApplication.isPlaying)
        {
            SessionState.SetBool("SoldierFactoryUnityCheck.Run", true);
            EditorApplication.EnterPlaymode();
            return;
        }
#endif
        RunInner();
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void HookPlayMode()
    {
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        if (!SessionState.GetBool("SoldierFactoryUnityCheck.Run", false)) return;
        SessionState.EraseBool("SoldierFactoryUnityCheck.Run");
        EditorApplication.delayCall += RunInner;
    }
#endif

    private static void RunInner()
    {
        var assets = new GameObject("GameAssets").AddComponent<GameAssets>();
        GameAssets.i = assets;
        var encyclopedia = new GameObject("Encyclopedia").AddComponent<Encyclopedia>();
        Encyclopedia.i = encyclopedia;
        encyclopedia.buildings = new System.Collections.Generic.List<BuildingDefinition>();

        assets.pilotDismounted = BuildPilotPrefab("PilotA", 1f);
        var parent = new GameObject("Stick").transform;

        GameObject first = VanillaSoldierFactory.CreateVisualSoldier(Vector3.zero, Quaternion.identity, parent);
        Check(first != null, "first stick clones a soldier");
        AssertStripped(first, "first clone");
        Check(first.transform.parent == parent, "clone parents to the stick");

        GameObject second = VanillaSoldierFactory.CreateVisualSoldier(Vector3.right, Quaternion.identity, parent);
        Check(second != null && second != first, "second stick clones independently");
        AssertStripped(second, "second clone");
        object templateA = Template();
        Check(templateA != null, "one strip builds the shared template");

        assets.pilotDismounted = BuildPilotPrefab("PilotB", 1.35f);
        GameObject third = VanillaSoldierFactory.CreateVisualSoldier(Vector3.zero, Quaternion.identity, parent);
        Check(third != null, "a changed prefab rebuilds the template");
        AssertStripped(third, "rebuilt clone");
        Check(Template() != null && !ReferenceEquals(Template(), templateA),
            "the template follows the prefab");
        Transform body = third.transform.Find("Body");
        Check(body != null && Math.Abs(body.localScale.x - 1.35f) < 0.01f,
            "rebuilt clones carry the new prefab");

        Debug.Log($"[SoldierFactoryCheck] {checks} assertions passed.");
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    private static object Template()
    {
        FieldInfo field = typeof(VanillaSoldierFactory).GetField("template",
            BindingFlags.Static | BindingFlags.NonPublic);
        return field?.GetValue(null);
    }

    private static GameObject BuildPilotPrefab(string name, float scale)
    {
        var root = new GameObject(name);
        root.AddComponent<Mirage.NetworkBehaviour>();
        root.AddComponent<Mirage.NetworkIdentity>();
        root.AddComponent<Rigidbody>();
        root.AddComponent<Animator>();
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = Vector3.one * scale;
        var seat = new GameObject("EjectSeat");
        seat.transform.SetParent(root.transform, false);
        seat.AddComponent<EjectionSeat>();
        var chair = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chair.name = "ChairPad";
        chair.transform.SetParent(root.transform, false);
        return root;
    }

    private static void AssertStripped(GameObject soldier, string label)
    {
        Check(soldier.GetComponentsInChildren<Mirage.NetworkBehaviour>(true).Length == 0,
            label + " strips network behaviours");
        Check(soldier.GetComponentsInChildren<Mirage.NetworkIdentity>(true).Length == 0,
            label + " strips network identity");
        Check(soldier.GetComponentInChildren<EjectionSeat>(true) == null,
            label + " strips the ejection seat");
        Check(soldier.transform.Find("EjectSeat") == null && soldier.transform.Find("ChairPad") == null,
            label + " removes seat and chair objects");
        Transform body = soldier.transform.Find("Body");
        Renderer renderer = body != null ? body.GetComponent<Renderer>() : null;
        Check(renderer != null && renderer.enabled, label + " keeps an enabled body renderer");
        Rigidbody rb = soldier.GetComponent<Rigidbody>();
        Check(rb != null && rb.isKinematic && !rb.detectCollisions, label + " parks physics");
        Collider[] cols = soldier.GetComponentsInChildren<Collider>(true);
        bool anyEnabled = false;
        for (int i = 0; i < cols.Length; i++)
            if (cols[i] != null && cols[i].enabled) anyEnabled = true;
        Check(!anyEnabled, label + " disables colliders");
        Animator anim = soldier.GetComponentInChildren<Animator>();
        Check(anim != null && anim.enabled, label + " keeps an enabled animator");
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition)
        {
            Debug.LogError("[SoldierFactoryCheck] FAIL: " + message);
#if UNITY_EDITOR
            EditorApplication.Exit(1);
#endif
            throw new InvalidOperationException(message);
        }
    }
}
#endif
