#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;
using Mirage;
using Mirage.Serialization;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Production DLL and real Mirage handlers/serialization. Only the manager lookup and authenticated sender are fixtures.
public static class EventsNetUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Assembly Mod = Assembly.Load("BoscaliSummer");
    private static readonly List<string> Failures = new List<string>();
    private static readonly List<GameObject> Roots = new List<GameObject>();
    private static NetworkManagerNuclearOption native;
    private static int assertions;

    public static void Run()
    {
        var harmony = new Harmony("Boscali.EventsNetCheck");
        object oldMission = typeof(MissionManager).GetProperty("CurrentMission", All).GetValue(null);
        string result;
        try
        {
            MethodInfo paths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", All);
            ParameterInfo[] parameters = paths.GetParameters();
            var pathArgs = new object[parameters.Length];
            pathArgs[0] = Path.GetFullPath("EventsNetUnityCheck.exe");
            for (int i = 1; i < pathArgs.Length; i++) pathArgs[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            paths.Invoke(null, pathArgs);
            MethodInfo getter = Type("Core.Game.GameAccess").GetProperty("NetworkManagerOrNull", All).GetGetMethod(true);
            harmony.Patch(getter, prefix: new HarmonyMethod(typeof(EventsNetUnityCheck).GetMethod(nameof(NativeManager), All)));
            Cases();
        }
        catch (Exception error) { Failures.Add(error.ToString()); Debug.LogException(error); }
        finally
        {
            foreach (GameObject root in Roots) if (root != null) Object.DestroyImmediate(root);
            typeof(MissionManager).GetProperty("CurrentMission", All).SetValue(null, oldMission);
            harmony.UnpatchSelf();
        }
        result = (Failures.Count == 0 ? "PASS: " : "FAIL: ") + assertions +
            " production Events network lifecycle assertions; immediate configure/start binding, scene reset, first-state retention, native packet decode, disabled presentation price, replacement and teardown. " +
            "Offline Unity callbacks with substituted manager lookup/authenticated sender; no UDP or live-game claim.\n" + string.Join("\n", Failures);
        File.WriteAllText("result.txt", result);
        Debug.Log(result);
        EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
    }

    private static void Cases()
    {
        GameObject nativeRoot = Root("Native manager fixture", false);
        native = nativeRoot.AddComponent<EventsNativeManagerFixture>();
        NetworkClient first = Root("Client one", false).AddComponent<NetworkClient>();
        native.Client = first;
        MessageHandler handler = NewHandler(first);
        Started(first);
        GameObject owner = Root("Events owner", true);
        Component net = owner.AddComponent(Type("Modules.Events.Networking.EventsNet"));
        Component manager = owner.AddComponent(Type("Modules.Events.Runtime.EventsManager"));
        object settings = Activator.CreateInstance(Type("Modules.Events.Configuration.EventsSettings"),
            new object[] { new ConfigFile(Path.GetFullPath("events-check.cfg"), false) { SaveOnConfigSet = false } });
        Call(manager, "Configure", settings, net);
        Call(net, "Configure", manager);
        Type message = Type("Modules.Events.Networking.ActiveEventChanged");
        int id = MessagePacker.GetId(message);
        Check(Registered(handler, id), "Configure binds an already started client immediately");
        Poll(net); // Let the baseline reach its ordinary binding before isolating the reset regression.

        MessageHandler replaced = NewHandler(first);
        Started(first);
        Check(!Registered(handler, id) && Registered(replaced, id), "native Started moves handler before any Update");
        handler = replaced;
        Poll(net);
        Call(manager, "ResetForScene");
        Check(Registered(handler, id), "scene reset retains the live transport handler synchronously");
        if (Registered(handler, id))
        {
            Mission(new Mission());
            Packet(handler, message, PriceEvent(), 10f);
            Check(Current(manager) != null, "first native packet after reset is decoded and applied before polling");
        }

        // Isolate the valid-first-snapshot identity race even on a baseline whose reset dropped its handler.
        Poll(net);
        Mission(new Mission());
        Packet(handler, message, PriceEvent(), 20f);
        Check(Current(manager) != null, "a valid mission snapshot is applied");
        Call(manager, "Update");
        Check(Current(manager) != null && Math.Abs((float)Property(Current(manager), "StartedAtMissionTime") - 20f) < .001f,
            "the following manager Update does not clear the new mission's first snapshot");

        ((ConfigEntry<bool>)Property(settings, "Enabled")).Value = false;
        Poll(net);
        Packet(handler, message, PriceEvent(), 30f);
        Check((float)Call(manager, "SupportCostMultiplierFor", 0UL) < .99f,
            "client presentation disabled still uses the host's decoded price");

        Mission(new Mission());
        Call(manager, "ResetForScene");
        Check(Current(manager) == null && ((IList)Field(manager, "history")).Count == 0,
            "a scene reset clears the previous event/history without retaining an old snapshot");
        Check(Registered(handler, id), "another mission boundary keeps the same handler");

        NetworkClient second = Root("Client two", false).AddComponent<NetworkClient>();
        MessageHandler secondHandler = NewHandler(second);
        Started(second);
        native.Client = second;
        Call(net, "ResetScene");
        Check(!Registered(handler, id) && Registered(secondHandler, id), "client replacement releases old binding and binds the new transport");
        ((Behaviour)net).enabled = false;
        net.GetType().GetMethod("OnDisable", All)?.Invoke(net, null); // Explicit callback: no Play mode.
        Check(!Registered(secondHandler, id), "disabled module releases its handler");
        NewHandler(second);
        Started(second);
        Check(!Registered(second.MessageHandler, id), "a later native start cannot rebind a disabled module");
        Call(net, "ResetScene");
        Check(!Registered(second.MessageHandler, id), "teardown reset does not reinstall a disabled handler");
        ((Behaviour)net).enabled = true;
        MethodInfo onEnable = net.GetType().GetMethod("OnEnable", All);
        onEnable?.Invoke(net, null);
        Check(Registered(second.MessageHandler, id), "re-enable binds immediately through native late-event replay");
        Call(net, "OnDestroy"); // Explicit runtime callback, as with enable/disable above; this fixture does not enter Play mode.
        Check(!Registered(second.MessageHandler, id), "OnDestroy releases registration");
        Object.DestroyImmediate(net);
        Call(net, "ResetScene");
        Check(!Registered(second.MessageHandler, id), "late manager cleanup tolerates the destroyed network component");
    }

    private static bool NativeManager(ref NetworkManagerNuclearOption __result) { __result = native; return false; }
    private static GameObject Root(string name, bool active)
    {
        var root = new GameObject(name); root.SetActive(active); Roots.Add(root); return root;
    }
    private static void Mission(Mission mission) => typeof(MissionManager).GetProperty("CurrentMission", All).SetValue(null, mission);
    private static MessageHandler NewHandler(NetworkClient client)
    {
        var handler = new MessageHandler(new NetworkWorld(), false, true);
        typeof(NetworkClient).GetField("<MessageHandler>k__BackingField", All).SetValue(client, handler);
        return handler;
    }
    private static void Started(NetworkClient client) => Call(Field(client, "_started"), "Invoke");
    private static bool Registered(MessageHandler handler, int id) =>
        ((IDictionary)typeof(MessageHandler).GetField("_messageHandlers", All).GetValue(handler)).Contains(id);
    private static void Poll(Component net) { Set(net, "nextRegistration", 0f); Call(net, "Update"); }
    private static object Current(Component manager) => Property(manager, "Current");
    private static int PriceEvent()
    {
        Array all = (Array)Type("Modules.Events.Domain.EventCatalog").GetField("All", All).GetValue(null);
        for (int i = 0; i < all.Length; i++)
        {
            object item = all.GetValue(i);
            if (Property(item, "Tier").ToString() == "Minor" && Property(item, "Target").ToString() == "All" &&
                (float)Property(item, "SupportCostMultiplier") < .99f) return i;
        }
        throw new InvalidOperationException("No minor global price fixture");
    }
    private static void Packet(MessageHandler handler, Type type, int index, float start)
    {
        object message = Activator.CreateInstance(type);
        Set(message, "Protocol", (byte)6); Set(message, "CatalogIndex", (sbyte)index);
        Set(message, "TargetFactionHash", 0); Set(message, "StartedAtMissionTime", start);
        Set(message, "EndsAtMissionTime", start + 120f); Set(message, "EffectStrength", 1f);
        MethodInfo pack = typeof(MessagePacker).GetMethods(All).Single(m => m.Name == "Pack" && m.IsGenericMethod && m.GetParameters().Length == 1);
        byte[] bytes = (byte[])pack.MakeGenericMethod(type).Invoke(null, new[] { message });
        // No socket/auth substitute is claimed as transport evidence: only the real decoder and callback are under test here.
        var player = (Mirage.NetworkPlayer)FormatterServices.GetUninitializedObject(typeof(Mirage.NetworkPlayer));
        Type auth = typeof(Mirage.NetworkPlayer).GetProperty("Authentication", All).PropertyType;
        player.SetAuthentication((Mirage.Authentication.PlayerAuthentication)FormatterServices.GetUninitializedObject(auth), false);
        handler.HandleMessage(player, new ArraySegment<byte>(bytes));
    }
    private static Type Type(string suffix) => Mod.GetType("BoscaliSummer." + suffix, true);
    private static object Field(object owner, string name) => owner.GetType().GetField(name, All).GetValue(owner);
    private static object Property(object owner, string name) => owner.GetType().GetProperty(name, All).GetValue(owner);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, All).SetValue(owner, value);
    private static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, All).Invoke(owner, args);
    private static void Check(bool value, string name) { assertions++; if (!value) Failures.Add(name); }
}
#endif
