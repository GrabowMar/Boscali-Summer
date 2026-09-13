#if UNITY_EDITOR
using System;
using System.IO;
using System.Runtime.CompilerServices;
using BoscaliSummer.Features.Support.Patches;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

// Production blast and patch run unchanged with real Unity physics. The small Missile
// stand-in models the installed TakeDamage -> Detonate -> Networkdisabled ordering.
public static class RodBlastUnityCheck
{
    private static bool engineError;
    public static void Run()
    {
        Application.logMessageReceived += (text, stack, type) => {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) engineError = true;
        };
        var harmony = new Harmony("boscalisummer.tests.rod-blast");
        try
        {
            harmony.PatchAll(typeof(SupportMissileAuthorityPatch));
            var source = Make<Missile>("Source rod", 0f);
            var chain = Make<Missile>("Second rod", 300f);
            var near = Make<Building>("Outer blast target", -200f);
            var far = Make<Building>("Chained blast target", 650f);
            var meshTarget = Make<Building>("Nonconvex damageable", -220f, false);
            var outside = Make<Building>("Outside both blasts", 1200f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.position = new Vector3(0, -3, 0);
            ground.transform.localScale = Vector3.one * 80f;
            Physics.SyncTransforms();
            source.Detonate(Vector3.up, false, true);
            Require(source.Detonations == 1 && source.DamageCalls == 0, "source self-damage or repeated detonation");
            Require(chain.Detonations == 1, "chain reaction did not detonate exactly once");
            Require(near.DamageCalls == 1 && far.DamageCalls == 1 && meshTarget.DamageCalls == 1,
                "nested blast lost, duplicated or corrupted a target");
            Require(outside.DamageCalls == 0, "damage escaped the blast radius");
            source.Detonate(Vector3.up, false, true);
            Require(near.DamageCalls == 1, "repeat detonation applied a second blast");
            BoscaliSummer.Runtime.GameAccess.Server = false;
            var client = Make<Missile>("Client rod", -200f);
            Physics.SyncTransforms();
            client.Detonate(Vector3.up, false, true);
            Require(near.DamageCalls == 1, "client applied authoritative damage");
            Require(!engineError, "Unity engine error (including invalid ClosestPoint)");
            File.WriteAllText("result.txt", "PASS: actual Harmony patch + Unity physics: no self-damage/re-entry, chained target isolation, nonconvex collider handling, repeat suppression, radius and client authority.");
            harmony.UnpatchSelf();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            File.WriteAllText("result.txt", "FAIL: " + e);
            harmony.UnpatchSelf();
            EditorApplication.Exit(1);
        }
    }
    static T Make<T>(string name, float x, bool sphere = true) where T : Unit
    {
        var go = GameObject.CreatePrimitive(sphere ? PrimitiveType.Sphere : PrimitiveType.Plane);
        go.name = name; go.transform.position = new Vector3(x, 0, 0);
        var value = go.AddComponent<T>();
        if (value is Missile missile)
        {
            missile.UniqueName = "BoscaliSummer:Support:Rod:" + name;
            go.AddComponent<Rigidbody>().isKinematic = true;
        }
        else if (sphere) go.AddComponent<BoxCollider>(); // Multiple colliders, one damageable.
        return value;
    }
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}

public struct PersistentID { }
public interface IDamageable
{
    Unit GetUnit();
    void TakeDamage(float pierce, float blast, float affected, float fire, float impact, PersistentID dealer);
}
public class Unit : MonoBehaviour, IDamageable
{
    public bool disabled;
    public int DamageCalls;
    public Unit GetUnit() => this;
    public virtual void TakeDamage(float p, float b, float a, float f, float i, PersistentID d) { DamageCalls++; }
}
public class Building : Unit { public void RegisterRecentExplosion(Vector3 p, float yield) {} }
public class Missile : Unit
{
    public string UniqueName;
    public PersistentID ownerID;
    public int Detonations;
    private int depth;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Detonate(Vector3 normal, bool armor, bool terrain)
    {
        if (disabled) return;
        disabled = true; // Vanilla sets Networkdisabled here, before its RPC.
        Detonations++;
    }
    public override void TakeDamage(float p, float b, float a, float f, float impact, PersistentID d)
    {
        if (disabled) return;
        DamageCalls++;
        if (++depth > 8) throw new Exception("REGRESSION: recursive Missile.TakeDamage -> Detonate before disabled; stopped before stack overflow");
        try { if (impact > 0f) Detonate(Vector3.up, false, false); }
        finally { depth--; }
    }
}
public static class Datum
{
    public static float LocalSeaY;
    public static Transform origin = new GameObject("Datum").transform;
}
public static class PhysicsLayers { public const int StaticsMask = 1; }
public static class RodCheckPosition { public static Vector3 ToGlobalPosition(this Vector3 p) => p; }
namespace BoscaliSummer.Runtime { public static class GameAccess { public static bool Server = true; public static bool IsServer() => Server; } }
namespace BoscaliSummer.Features.Support.Visuals
{
    static class KineticRodStrikeVisuals { public static void Track(Missile m, Vector3 p) {} public static void TriggerImpact(Vector3 p, PersistentID id) {} }
    class KineticRodDescentEffect : MonoBehaviour { public void MarkDetonated() {} }
    static class EmpVisualEffect { public static void Trigger(Vector3 p, float r) {} }
    static class CockpitEmpDisruption { public static void CheckLocalDisruption(Vector3 p, float r) {} }
    static class FlareMissileBurstVisuals { public static void TriggerBarrage(Vector3 p, float r, float d, int c) {} }
}
namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    class FlareMissileFlightTracker : MonoBehaviour { public float Radius, Duration; public int FlareCount; public void MarkDetonated() {} }
}
#endif
