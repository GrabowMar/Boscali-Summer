#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Reproduces a shot expiring between Poll and Paint, before the same frame's strip is painted.
public static class ShotFeedUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly Assembly Mod = Assembly.Load("BoscaliSummer");
    private static int assertions;

    private sealed class Feed<T> where T : struct
    {
        public T Value;
        public bool Live = true;
        public bool Read(out T data) { data = Value; return Live; }
    }

    public static void Run()
    {
        bool passed = false;
        string result;
        try
        {
            Cases();
            passed = true;
            result = "PASS: " + assertions + " production SHOT feed lifecycle assertions; expiry and provider removal between Poll/Paint, empty strip, gauges and headline, and subsequent live poll. Offline Unity check; no live-game or rendered-quality claim.\n";
        }
        catch (Exception error) { result = "FAIL: " + error + "\n"; Debug.LogException(error); }
        File.WriteAllText("result.txt", result);
        Debug.Log(result);
        EditorApplication.Exit(passed ? 0 : 1);
    }

    private static void Cases()
    {
        Type dataType = Find("Modules.Hud.Domain.ShotScope"), missileType = Find("Modules.Hud.Domain.ShotMissile");
        object data = Activator.CreateInstance(dataType), missile = Activator.CreateInstance(missileType);
        Set(missile, "Id", 1);
        Set(missile, "Target", -1);
        Set(missile, "TtiS", 4f);
        Array missiles = Array.CreateInstance(missileType, 1);
        missiles.SetValue(missile, 0);
        Set(data, "MissileCount", 1);
        Set(data, "Missiles", missiles);
        Type heldType = typeof(Feed<>).MakeGenericType(dataType);
        object held = Activator.CreateInstance(heldType);
        Set(held, "Value", data);
        Delegate provider = Delegate.CreateDelegate(Find("Core.Contracts.ScopeProvider`1").MakeGenericType(dataType), held, heldType.GetMethod("Read"));
        Type feeds = Find("Core.Contracts.ScopeFeeds");
        MethodInfo register = feeds.GetMethod("Register", All).MakeGenericMethod(dataType), unregister = feeds.GetMethod("Unregister", All).MakeGenericMethod(dataType);
        object page = Activator.CreateInstance(Find("Modules.Hud.Presentation.ShotPage"), true);
        object env = Activator.CreateInstance(Find("Modules.Hud.Presentation.ScopeEnv")), paint = Activator.CreateInstance(Find("Modules.Hud.Presentation.PagePaint"));
        try
        {
            foreach (bool remove in new[] { false, true })
            {
                Set(held, "Live", true);
                register.Invoke(null, new object[] { provider });
                Check((bool)Call(page, "Poll", env), "shot starts live");
                Check((bool)Property(page, "HasStrip"), "live shot has a strip");
                if (remove) unregister.Invoke(null, new object[] { provider });
                else Set(held, "Live", false);
                Call(page, "Paint", null, paint);
                Check(!(bool)Property(page, "Fast") && !(bool)Property(page, "HasStrip"), "lost feed immediately clears live state");
                Call(page, "PaintStrip", null, paint); // Must return without accessing a canvas or the default feed's null arrays.
                Check((int)Call(page, "Gauges", Array.CreateInstance(Find("Core.Contracts.GaugeSpec"), 3)) == 0, "lost feed has no stale gauges");
                Check(Call(page, "Headline").GetType().GetField("Text").GetValue(Call(page, "Headline")) == null, "lost feed has no stale headline");
            }
        }
        finally { unregister.Invoke(null, new object[] { provider }); }
    }

    private static Type Find(string name) => Mod.GetType("BoscaliSummer." + name, true);
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, All).SetValue(owner, value);
    private static object Property(object owner, string name) => owner.GetType().GetProperty(name, All).GetValue(owner);
    private static object Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, All).Invoke(owner, args);
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
