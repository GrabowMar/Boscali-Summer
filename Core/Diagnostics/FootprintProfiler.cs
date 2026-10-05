using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Profiling;
using UnityEngine;

namespace BoscaliSummer.Core.Diagnostics
{
    /// <summary>
    /// On-demand measurement of what Boscali itself costs per frame. Off by default and free
    /// when off: nothing is patched until <see cref="Start"/>. While on, every Boscali
    /// Update/LateUpdate/FixedUpdate and every Boscali Harmony prefix/postfix/finalizer is
    /// wrapped in a timing prefix + finalizer, and the time is attributed per module and per
    /// method. Totals count only outermost scopes, so a Boscali patch that runs inside a
    /// Boscali Update is not counted twice. Results survive <see cref="Stop"/> so a tool can
    /// stop first and read afterwards.
    /// </summary>
    internal static class FootprintProfiler
    {
        private const string HarmonyId = Plugin.PluginGuid + ".footprint";
        private const string ModulePatchOwnerPrefix = Plugin.PluginGuid + ".module.";
        private const int RingSize = 1024;
        private const int TopMethods = 40;
        private const int TopSpikes = 20;

        internal struct Mark
        {
            public long Ticks;
            public long Alloc;
        }

        private sealed class Slot
        {
            public string Name;
            public string Module;
            public string Kind;
            public long Ticks;
            public long FrameTicks;
            public long MaxFrameTicks;
            public long Alloc;
            public int Calls;
            public bool Touched;
        }

        // Keyed by method handle: reflection objects for one method need not compare equal.
        private static readonly Dictionary<IntPtr, Slot> slots = new Dictionary<IntPtr, Slot>();
        private static readonly List<Slot> touched = new List<Slot>(64);
        private static readonly long[] frameRing = new long[RingSize];
        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        private static ManualLogSource log;
        private static Harmony harmony;
        private static int mainThread;
        private static int depth;
        private static int lastFrame = -1;
        private static long frameTotal;
        private static long outerTotalTicks;
        private static int ringCount;
        private static int ringNext;
        private static bool allocSupported;
        private static ProfilerRecorder gcAllocRecorder;
        private static double gcAllocSum;
        private static int gcAllocFrames;
        private static double unscaledSum;
        private static int unscaledFrames;
        private static int startFrame;
        private static int stopFrame;
        private static float startTime;
        private static float stopTime;
        private static int instrumented;
        private static int failed;
        private static string mode = "off";

        public static bool Running { get; private set; }

        /// <summary>
        /// Extra Boscali methods to wrap on the next <see cref="Start"/>: "Type.Method" (every
        /// overload) or "Type.*" (every declared method), separated by ';' or ','. Type is the
        /// simple or nested name, matched in the Boscali assembly only.
        /// </summary>
        public static string Watch { get; set; } = "";

        /// <summary>"updates" wraps Unity callbacks and patches; "deep" also wraps Tick methods.</summary>
        public static string Start(bool deep)
        {
            if (Running) Stop();
            log ??= BepInEx.Logging.Logger.CreateLogSource("Boscali Footprint");
            mainThread = Thread.CurrentThread.ManagedThreadId;
            ResetCounters();
            var targets = CollectTargets(deep);
            harmony = new Harmony(HarmonyId);
            var prefix = new HarmonyMethod(typeof(FootprintProfiler).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static));
            var finalizer = new HarmonyMethod(typeof(FootprintProfiler).GetMethod(nameof(Finalizer), BindingFlags.NonPublic | BindingFlags.Static));
            instrumented = 0;
            failed = 0;
            foreach (var pair in targets)
            {
                try
                {
                    harmony.Patch(pair.Key, prefix: prefix, finalizer: finalizer);
                    slots[pair.Key.MethodHandle.Value] = pair.Value;
                    instrumented++;
                }
                catch (Exception ex)
                {
                    failed++;
                    if (failed <= 3) log.LogWarning("Footprint: cannot wrap " + pair.Value.Name + ": " + ex.Message);
                }
            }
            try
            {
                gcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            }
            catch (Exception)
            {
                gcAllocRecorder = default;
            }
            mode = deep ? "deep" : "updates";
            startFrame = Time.frameCount;
            startTime = Time.realtimeSinceStartup;
            Running = true;
            log.LogInfo("Footprint: profiling " + instrumented + " Boscali methods (" + mode + ", " + failed +
                " skipped, per-method alloc " + (allocSupported ? "on" : "unsupported") + ").");
            return mode;
        }

        public static void Stop()
        {
            if (!Running) return;
            Running = false;
            RollFrame(int.MaxValue);
            stopFrame = Time.frameCount;
            stopTime = Time.realtimeSinceStartup;
            try { harmony?.UnpatchSelf(); }
            catch (Exception ex) { log?.LogWarning("Footprint: unpatch failed: " + ex.Message); }
            harmony = null;
            if (gcAllocRecorder.Valid) gcAllocRecorder.Dispose();
            gcAllocRecorder = default;
            depth = 0;
            LogSummary();
        }

        /// <summary>A few log lines so the measurement is readable without the bridge.</summary>
        private static void LogSummary()
        {
            if (log == null) return;
            int frames = System.Math.Max(1, stopFrame - startFrame);
            var modules = new Dictionary<string, long>();
            var methods = new List<Slot>();
            foreach (var slot in slots.Values)
            {
                if (slot.Calls == 0) continue;
                methods.Add(slot);
                modules.TryGetValue(slot.Module, out long ticks);
                modules[slot.Module] = ticks + slot.Ticks;
            }
            var ordered = new List<KeyValuePair<string, long>>(modules);
            ordered.Sort((a, b) => b.Value.CompareTo(a.Value));
            methods.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));
            var line = new StringBuilder("Footprint: Boscali ");
            line.Append((outerTotalTicks * TicksToMs / frames).ToString("0.000", CultureInfo.InvariantCulture))
                .Append(" ms/frame over ").Append(frames).Append(" frames; by module:");
            for (int i = 0; i < ordered.Count && i < 12; i++)
                line.Append(' ').Append(ordered[i].Key).Append('=')
                    .Append((ordered[i].Value * TicksToMs / frames).ToString("0.000", CultureInfo.InvariantCulture));
            log.LogInfo(line.ToString());
            for (int i = 0; i < methods.Count && i < 10; i++)
                log.LogInfo("Footprint:   " + (methods[i].Ticks * TicksToMs / frames).ToString("0.000", CultureInfo.InvariantCulture) +
                    " ms/frame, max " + (methods[i].MaxFrameTicks * TicksToMs).ToString("0.00", CultureInfo.InvariantCulture) +
                    " ms  " + methods[i].Name);
        }

        /// <summary>Zeroes the counters without re-patching (a fresh window).</summary>
        public static void ResetWindow()
        {
            foreach (var slot in slots.Values)
            {
                slot.Ticks = slot.FrameTicks = slot.MaxFrameTicks = slot.Alloc = 0;
                slot.Calls = 0;
                slot.Touched = false;
            }
            touched.Clear();
            frameTotal = outerTotalTicks = 0;
            ringCount = ringNext = 0;
            gcAllocSum = unscaledSum = 0;
            gcAllocFrames = unscaledFrames = 0;
            lastFrame = -1;
            startFrame = Time.frameCount;
            startTime = Time.realtimeSinceStartup;
        }

        private static void ResetCounters()
        {
            slots.Clear();
            ResetWindow();
            depth = 0;
            allocSupported = ProbeAllocCounter();
        }

        private static bool ProbeAllocCounter()
        {
            try
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                var probe = new byte[4096];
                long after = GC.GetAllocatedBytesForCurrentThread();
                return probe.Length > 0 && after - before >= 4096;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Prefix(out Mark __state)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread)
            {
                __state = default;
                return;
            }
            depth++;
            __state.Alloc = allocSupported ? GC.GetAllocatedBytesForCurrentThread() : 0;
            __state.Ticks = Stopwatch.GetTimestamp();
        }

        private static void Finalizer(MethodBase __originalMethod, Mark __state)
        {
            if (__state.Ticks == 0) return;
            long elapsed = Stopwatch.GetTimestamp() - __state.Ticks;
            long alloc = allocSupported ? GC.GetAllocatedBytesForCurrentThread() - __state.Alloc : 0;
            depth--;
            if (!Running || __originalMethod == null || !slots.TryGetValue(__originalMethod.MethodHandle.Value, out var slot)) return;
            int frame = Time.frameCount;
            if (frame != lastFrame) RollFrame(frame);
            slot.Ticks += elapsed;
            slot.FrameTicks += elapsed;
            slot.Alloc += alloc;
            slot.Calls++;
            if (!slot.Touched)
            {
                slot.Touched = true;
                touched.Add(slot);
            }
            if (depth <= 0)
            {
                depth = 0;
                frameTotal += elapsed;
                outerTotalTicks += elapsed;
            }
        }

        private static void RollFrame(int frame)
        {
            if (lastFrame >= 0)
            {
                frameRing[ringNext] = frameTotal;
                ringNext = (ringNext + 1) % RingSize;
                if (ringCount < RingSize) ringCount++;
                for (int i = 0; i < touched.Count; i++)
                {
                    var slot = touched[i];
                    if (slot.FrameTicks > slot.MaxFrameTicks) slot.MaxFrameTicks = slot.FrameTicks;
                    slot.FrameTicks = 0;
                    slot.Touched = false;
                }
                touched.Clear();
                unscaledSum += Time.unscaledDeltaTime;
                unscaledFrames++;
                if (gcAllocRecorder.Valid)
                {
                    gcAllocSum += gcAllocRecorder.LastValue;
                    gcAllocFrames++;
                }
            }
            frameTotal = 0;
            lastFrame = frame;
        }

        private static Dictionary<MethodBase, Slot> CollectTargets(bool deep)
        {
            var found = new Dictionary<MethodBase, Slot>();
            var assembly = typeof(FootprintProfiler).Assembly;
            const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var type in LoadableTypes(assembly))
            {
                if (type == null || type.ContainsGenericParameters || type == typeof(FootprintProfiler)) continue;
                try
                {
                    bool behaviour = typeof(MonoBehaviour).IsAssignableFrom(type);
                    foreach (var method in type.GetMethods(declared))
                    {
                        string name = method.Name;
                        bool unity = behaviour && !method.IsStatic &&
                            (name == "Update" || name == "LateUpdate" || name == "FixedUpdate");
                        bool tick = deep && (name == "Tick" || name == "FixedTick" || name == "LateTick");
                        if (!unity && !tick) continue;
                        if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                        if (unity && method.GetParameters().Length != 0) continue;
                        found[method] = NewSlot(type, type.Name + "." + name, unity ? "update" : "tick");
                    }
                }
                catch (Exception)
                {
                    // A type that references an absent optional assembly cannot be inspected; skip it.
                }
            }
            AddWatched(found, assembly);
            foreach (var original in Harmony.GetAllPatchedMethods())
            {
                var info = Harmony.GetPatchInfo(original);
                if (info == null) continue;
                AddPatches(found, info.Prefixes, original, assembly);
                AddPatches(found, info.Postfixes, original, assembly);
                AddPatches(found, info.Finalizers, original, assembly);
            }
            return found;
        }

        private static void AddWatched(Dictionary<MethodBase, Slot> found, Assembly assembly)
        {
            if (string.IsNullOrWhiteSpace(Watch)) return;
            const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (string raw in Watch.Split(';', ','))
            {
                string spec = raw.Trim();
                int dot = spec.LastIndexOf('.');
                if (dot <= 0 || dot == spec.Length - 1) continue;
                string typeName = spec.Substring(0, dot);
                string methodName = spec.Substring(dot + 1);
                foreach (var type in LoadableTypes(assembly))
                {
                    if (type == null || type.ContainsGenericParameters || type == typeof(FootprintProfiler)) continue;
                    if (type.Name != typeName && type.FullName != typeName) continue;
                    try
                    {
                        foreach (var method in type.GetMethods(declared))
                        {
                            if (methodName != "*" && method.Name != methodName) continue;
                            if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                            if (!found.ContainsKey(method))
                                found[method] = NewSlot(type, type.Name + "." + method.Name, "watch");
                        }
                    }
                    catch (Exception)
                    {
                        // Uninspectable type: nothing to watch.
                    }
                }
            }
        }

        private static void AddPatches(Dictionary<MethodBase, Slot> found, IEnumerable<Patch> patches,
            MethodBase original, Assembly assembly)
        {
            if (patches == null) return;
            foreach (var patch in patches)
            {
                var method = patch.PatchMethod;
                if (method == null || method.DeclaringType == null || method.DeclaringType.Assembly != assembly) continue;
                if (patch.owner == null || !patch.owner.StartsWith(ModulePatchOwnerPrefix, StringComparison.Ordinal)) continue;
                if (found.ContainsKey(method) || method.DeclaringType == typeof(FootprintProfiler)) continue;
                string target = (original.DeclaringType != null ? original.DeclaringType.Name + "." : "") + original.Name;
                found[method] = NewSlot(method.DeclaringType, method.DeclaringType.Name + "." + method.Name + " @ " + target, "patch");
            }
        }

        private static Slot NewSlot(Type type, string name, string kind) =>
            new Slot { Name = name, Module = ModuleOf(type), Kind = kind };

        /// <summary>BoscaliSummer.Modules.Weather.Runtime → "weather"; BoscaliSummer.Core.* → "core".</summary>
        internal static string ModuleOf(Type type)
        {
            string ns = type?.Namespace ?? "";
            string[] parts = ns.Split('.');
            if (parts.Length >= 3 && parts[0] == "BoscaliSummer" && parts[1] == "Modules") return parts[2].ToLowerInvariant();
            if (parts.Length >= 2 && parts[0] == "BoscaliSummer") return parts[1].ToLowerInvariant();
            return parts.Length > 0 && parts[0].Length > 0 ? parts[0].ToLowerInvariant() : "unknown";
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types; }
        }

        /// <summary>The measurement as one JSON object (schema 1, documented in docs/PERFORMANCE.md).</summary>
        public static string SnapshotJson()
        {
            int frames = System.Math.Max(0, (Running ? Time.frameCount : stopFrame) - startFrame);
            double seconds = System.Math.Max(0.0, (Running ? Time.realtimeSinceStartup : stopTime) - startTime);
            var byModule = new Dictionary<string, double[]>();
            var methods = new List<Slot>();
            foreach (var slot in slots.Values)
            {
                if (slot.Calls == 0) continue;
                methods.Add(slot);
                if (!byModule.TryGetValue(slot.Module, out var acc)) byModule[slot.Module] = acc = new double[4];
                acc[0] += slot.Ticks;
                acc[1] = System.Math.Max(acc[1], slot.MaxFrameTicks);
                acc[2] += slot.Calls;
                acc[3] += slot.Alloc;
            }
            methods.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));
            var ring = new long[ringCount];
            Array.Copy(frameRing, ring, ringCount);
            Array.Sort(ring);
            double perFrame = System.Math.Max(1, frames);

            var sb = new StringBuilder(4096);
            sb.Append("{\"schema\":1");
            Field(sb, "profiling", Running);
            Str(sb, "mode", Running ? mode : (frames > 0 ? mode + "-stopped" : "off"));
            Field(sb, "window_s", seconds);
            Field(sb, "frames", frames);
            Field(sb, "instrumented", instrumented);
            Field(sb, "skipped", failed);
            Field(sb, "frame_ms_avg", unscaledFrames > 0 ? unscaledSum * 1000.0 / unscaledFrames : 0);
            Field(sb, "boscali_ms_per_frame", outerTotalTicks * TicksToMs / perFrame);
            Field(sb, "boscali_ms_p50", Percentile(ring, 0.50) * TicksToMs);
            Field(sb, "boscali_ms_p95", Percentile(ring, 0.95) * TicksToMs);
            Field(sb, "boscali_ms_max", ring.Length > 0 ? ring[ring.Length - 1] * TicksToMs : 0);
            Field(sb, "gc_alloc_supported", allocSupported);
            Field(sb, "game_gc_alloc_bytes_per_frame", gcAllocFrames > 0 ? gcAllocSum / gcAllocFrames : 0);
            sb.Append(",\"adaptive\":{");
            sb.Append("\"enabled\":").Append(AdaptiveBudgetReport.Enabled ? "true" : "false");
            Field(sb, "reduced", AdaptiveBudgetReport.Reduced);
            Field(sb, "last_average_ms", AdaptiveBudgetReport.LastAverageMs);
            sb.Append('}');

            var modules = new List<KeyValuePair<string, double[]>>(byModule);
            modules.Sort((a, b) => b.Value[0].CompareTo(a.Value[0]));
            sb.Append(",\"modules\":[");
            for (int i = 0; i < modules.Count; i++)
            {
                var acc = modules[i].Value;
                if (i > 0) sb.Append(',');
                sb.Append('{');
                Str(sb, "module", modules[i].Key, first: true);
                Field(sb, "ms_per_frame", acc[0] * TicksToMs / perFrame);
                Field(sb, "max_frame_ms", acc[1] * TicksToMs);
                Field(sb, "calls_per_frame", acc[2] / perFrame);
                Field(sb, "alloc_bytes_per_frame", acc[3] / perFrame);
                sb.Append('}');
            }
            sb.Append("],\"methods\":[");
            // Top by average cost, plus the worst single frames: a rare hitch has a small average.
            var listed = new List<Slot>(methods.GetRange(0, System.Math.Min(TopMethods, methods.Count)));
            var bySpike = new List<Slot>(methods);
            bySpike.Sort((a, b) => b.MaxFrameTicks.CompareTo(a.MaxFrameTicks));
            for (int i = 0; i < bySpike.Count && i < TopSpikes; i++)
                if (!listed.Contains(bySpike[i])) listed.Add(bySpike[i]);
            for (int i = 0; i < listed.Count; i++)
            {
                var slot = listed[i];
                if (i > 0) sb.Append(',');
                sb.Append('{');
                Str(sb, "method", slot.Name, first: true);
                Str(sb, "module", slot.Module);
                Str(sb, "kind", slot.Kind);
                Field(sb, "ms_per_frame", slot.Ticks * TicksToMs / perFrame);
                Field(sb, "max_frame_ms", slot.MaxFrameTicks * TicksToMs);
                Field(sb, "calls_per_frame", slot.Calls / perFrame);
                Field(sb, "alloc_bytes_per_frame", slot.Alloc / perFrame);
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static double Percentile(long[] sorted, double q)
        {
            if (sorted.Length == 0) return 0;
            int index = (int)System.Math.Ceiling(q * sorted.Length) - 1;
            return sorted[System.Math.Max(0, System.Math.Min(sorted.Length - 1, index))];
        }

        private static void Field(StringBuilder sb, string key, bool value) =>
            sb.Append(",\"").Append(key).Append("\":").Append(value ? "true" : "false");

        private static void Field(StringBuilder sb, string key, int value) =>
            sb.Append(",\"").Append(key).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));

        private static void Field(StringBuilder sb, string key, double value) =>
            sb.Append(",\"").Append(key).Append("\":")
                .Append(double.IsNaN(value) || double.IsInfinity(value) ? "0" : value.ToString("0.####", CultureInfo.InvariantCulture));

        private static void Str(StringBuilder sb, string key, string value, bool first = false)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(key).Append("\":\"");
            foreach (char c in value ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
