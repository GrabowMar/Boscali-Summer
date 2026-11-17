#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoscaliSummer.Core.Diagnostics
{
    /// <summary>Explicit, bounded nomodkit attribution probe. It never skips or changes a physics call.</summary>
    public static class PhysicsWarningProbe
    {
        private const int Limit = 32;
        private const string Warning = "Physics.ClosestPoint can only be used";
        private static readonly object Gate = new object();
        private static readonly List<Dictionary<string, object>> Queries = new List<Dictionary<string, object>>(Limit);
        private static readonly List<Dictionary<string, object>> Warnings = new List<Dictionary<string, object>>(Limit);
        private static readonly HashSet<string> Seen = new HashSet<string>();
        private static Harmony harmony;
        private static volatile bool installed;
        private static int mainThread, calls, unsupported, warningCount, canaryWarningCount;
        private static StackTraceLogType oldError, oldWarning;
        private static bool stackSettingsSaved;
        private sealed class QueryState
        {
            internal string Key, Api, Pose;
            internal Collider Collider;
            internal Vector3 Point;
        }
        [ThreadStatic] private static QueryState currentQuery;
        [ThreadStatic] private static bool canary;
        [ThreadStatic] private static string explicitScope;
        internal static string CaptureScope { get => explicitScope; set => explicitScope = value; }

        public static Dictionary<string, object> Install(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return AutomationArgs.Failed("requires an active hosted single-player nomodkit scenario");
            if (installed) return Read(args);
            lock (Gate) { Queries.Clear(); Warnings.Clear(); Seen.Clear(); }
            calls = unsupported = warningCount = canaryWarningCount = 0;
            mainThread = Thread.CurrentThread.ManagedThreadId;
            harmony = new Harmony(Plugin.PluginGuid + ".physics-warning-probe");
            bool ready = false;
            try
            {
                Patch(typeof(Physics).GetMethod(nameof(Physics.ClosestPoint), new[] {
                    typeof(Vector3), typeof(Collider), typeof(Vector3), typeof(Quaternion) }), nameof(StaticQuery));
                Patch(typeof(Collider).GetMethod(nameof(Collider.ClosestPoint), new[] { typeof(Vector3) }), nameof(InstanceQuery));
                oldError = Application.GetStackTraceLogType(LogType.Error);
                oldWarning = Application.GetStackTraceLogType(LogType.Warning);
                stackSettingsSaved = true;
                Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.Full);
                Application.logMessageReceivedThreaded += NativeWarning;
                Application.quitting += OnQuit;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                installed = ready = true;
            }
            finally { if (!ready) Remove(); }
            return Read(args);
        }

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            lock (Gate)
                return new Dictionary<string, object> {
                    { "ok", true }, { "installed", installed }, { "calls", Volatile.Read(ref calls) },
                    { "onMainThread", Thread.CurrentThread.ManagedThreadId == mainThread },
                    { "unsupportedCandidates", Volatile.Read(ref unsupported) }, { "nativeWarnings", Volatile.Read(ref warningCount) },
                    { "intentionalCanaryWarnings", Volatile.Read(ref canaryWarningCount) },
                    { "queries", Queries.ToArray() }, { "warnings", Warnings.ToArray() }, { "limit", Limit },
                };
        }

        public static Dictionary<string, object> Stop(Dictionary<string, object> args)
        {
            Remove();
            return Read(args);
        }

        public static Dictionary<string, object> SelfTest(Dictionary<string, object> args)
        {
            if (!installed || Thread.CurrentThread.ManagedThreadId != mainThread)
                return AutomationArgs.Failed("install the scenario probe on the main thread first");
            GameObject shape = null;
            Mesh mesh = null;
            try
            {
                shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shape.name = "PhysicsWarningProbe intentional canary";
                shape.hideFlags = HideFlags.HideAndDontSave;
                shape.transform.position = new Vector3(0f, Datum.LocalSeaY + 10000f, 0f);
                UnityEngine.Object.DestroyImmediate(shape.GetComponent<BoxCollider>());
                mesh = UnityEngine.Object.Instantiate(shape.GetComponent<MeshFilter>().sharedMesh);
                mesh.name = "PhysicsWarningProbe canary mesh";
                mesh.hideFlags = HideFlags.HideAndDontSave;
                var collider = shape.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = false;
                Physics.SyncTransforms();
                Vector3 point = collider.bounds.center + new Vector3(2f, 2f, 2f);
                canary = true;
                int before = Volatile.Read(ref canaryWarningCount);
                Vector3 instance = collider.ClosestPoint(point);
                int afterInstance = Volatile.Read(ref canaryWarningCount);
                Vector3 direct = Physics.ClosestPoint(point, collider, shape.transform.position, shape.transform.rotation);
                return new Dictionary<string, object> {
                    { "ok", true }, { "engine", Application.unityVersion }, { "scope", "intentional disposable canary; not a gameplay fault" },
                    { "instanceWarnings", afterInstance - before },
                    { "staticWarnings", Volatile.Read(ref canaryWarningCount) - afterInstance },
                    { "instancePoint", instance.ToString() }, { "staticPoint", direct.ToString() },
                };
            }
            finally
            {
                canary = false;
                if (shape != null) UnityEngine.Object.DestroyImmediate(shape);
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static void Patch(MethodInfo method, string prefix)
        {
            if (method == null || method.GetMethodBody() == null)
                throw new MissingMethodException("managed Unity ClosestPoint wrapper unavailable");
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PhysicsWarningProbe), prefix),
                finalizer: new HarmonyMethod(typeof(PhysicsWarningProbe), nameof(FinishQuery)));
        }

        private static void StaticQuery(Vector3 __0, Collider __1, Vector3 __2, Quaternion __3, out QueryState __state)
        {
            __state = currentQuery;
            string pose = " | pose=" + __2 + "/" + __3;
            currentQuery = new QueryState { Key = Capture("Physics.ClosestPoint", __1, __0, pose),
                Api = "Physics.ClosestPoint", Collider = __1, Point = __0, Pose = pose };
        }

        private static void InstanceQuery(Collider __instance, Vector3 __0, out QueryState __state)
        {
            __state = currentQuery;
            currentQuery = new QueryState { Key = Capture("Collider.ClosestPoint", __instance, __0, ""),
                Api = "Collider.ClosestPoint", Collider = __instance, Point = __0, Pose = "" };
        }

        private static Exception FinishQuery(Exception __exception, QueryState __state)
        {
            currentQuery = __state;
            return __exception;
        }

        private static string Capture(string api, Collider collider, Vector3 point, string pose)
        {
            Interlocked.Increment(ref calls);
            if (!installed || ReferenceEquals(collider, null)) return null;
            bool main = Thread.CurrentThread.ManagedThreadId == mainThread;
            if (main && (collider == null || collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider ||
                collider is MeshCollider convex && convex.convex)) return null;
            Interlocked.Increment(ref unsupported);
            string key = api + ":" + (main ? collider.GetInstanceID().ToString() : collider.GetType().FullName);
            lock (Gate)
            {
                if (Seen.Contains(key)) return key;
                if (Queries.Count >= Limit) return key;
                Seen.Add(key);
                Queries.Add(new Dictionary<string, object> {
                    { "key", key }, { "api", api }, { "thread", Thread.CurrentThread.ManagedThreadId },
                    { "shape", Describe(collider, main) }, { "point", point.ToString() + pose },
                    { "stack", Clip(new StackTrace(2, true).ToString()) },
                });
            }
            return key;
        }

        private static void NativeWarning(string message, string stack, LogType type)
        {
            if (!installed || !message.StartsWith(Warning, StringComparison.Ordinal)) return;
            if (canary) Interlocked.Increment(ref canaryWarningCount);
            else Interlocked.Increment(ref warningCount);
            lock (Gate)
            {
                if (Warnings.Count >= Limit) return;
                QueryState query = currentQuery;
                bool main = Thread.CurrentThread.ManagedThreadId == mainThread;
                Warnings.Add(new Dictionary<string, object> {
                    { "message", message }, { "query", query?.Key ?? "no recorded candidate on this thread" },
                    { "scope", explicitScope ?? (canary ? "intentional canary" : "ordinary gameplay") },
                    { "api", query?.Api ?? "unknown" }, { "point", query == null ? "" : query.Point + query.Pose },
                    // Actual warnings own their metadata budget, even after harmless candidates filled Queries.
                    { "shape", query == null || ReferenceEquals(query.Collider, null) ? "unknown" : Describe(query.Collider, main) },
                    { "thread", Thread.CurrentThread.ManagedThreadId }, { "stack", Clip(stack) },
                });
            }
        }

        private static string Describe(Collider collider, bool main)
        {
            string shape = collider.GetType().FullName;
            if (!main || collider == null) return shape;
            Rigidbody body = collider.attachedRigidbody;
            Unit owner = collider.GetComponentInParent<Unit>();
            shape += " | path=" + Path(collider.transform) + " | bounds=" + collider.bounds +
                " | body=" + (body == null ? "none" : body.name + "/kinematic=" + body.isKinematic) +
                " | unit=" + (owner == null ? "none" : owner.name + "/" + owner.definition?.name);
            if (collider is MeshCollider mesh)
                shape += " | convex=" + mesh.convex + " | mesh=" + (mesh.sharedMesh == null ? "none" : mesh.sharedMesh.name);
            return shape;
        }

        private static string Path(Transform transform)
        {
            string path = transform.name;
            for (int n = 0; transform.parent != null && n < 10; n++)
            { transform = transform.parent; path = transform.name + "/" + path; }
            return path;
        }

        private static string Clip(string text) => text == null ? "" : text.Length <= 8000 ? text : text.Substring(0, 8000);
        private static void OnQuit() => Remove();
        private static void OnSceneUnloaded(Scene scene) => Remove();
        private static void Remove()
        {
            installed = false;
            Application.logMessageReceivedThreaded -= NativeWarning;
            Application.quitting -= OnQuit;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            harmony?.UnpatchSelf();
            harmony = null;
            if (stackSettingsSaved)
            {
                Application.SetStackTraceLogType(LogType.Error, oldError);
                Application.SetStackTraceLogType(LogType.Warning, oldWarning);
                stackSettingsSaved = false;
            }
            currentQuery = null;
        }
    }
}
#endif
