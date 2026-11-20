using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

namespace BoscaliSummer.Core.Diagnostics
{
    /// <summary>
    /// Sim-only window over Unity's own counters (draw calls, batches, SetPass, triangles, main/render thread time ...),
    /// the native side the footprint profiler cannot see. <c>list</c> names every counter this release player exposes;
    /// <c>start</c> records the wanted ones for up to 2400 frames; <c>read</c> returns average and max per counter.
    /// </summary>
    public static class RenderCounters
    {
        private static readonly string[] Wanted =
        {
            "Main Thread", "Render Thread", "CPU Main Thread Frame Time", "CPU Render Thread Frame Time", "GPU Frame Time",
            "Batches Count", "Draw Calls Count", "SetPass Calls Count", "Triangles Count", "Vertices Count",
            "Shadow Casters Count", "Visible Skinned Meshes Count", "Render Textures Count", "Render Textures Bytes",
            "Used Textures Bytes", "Used Buffers Bytes", "Total Used Memory", "Total Reserved Memory", "GC Used Memory",
            "System Used Memory", "Gfx Used Memory", "Texture Memory", "Mesh Memory", "Game Object Count", "Object Count",
            "Physics.Processing", "Physics.Simulate", "Canvas.SendWillRenderCanvases", "Canvas.BuildBatch",
            "UIEvents.WillRenderCanvases", "PlayerLoop", "Camera.Render", "Culling", "ParticleSystem.Update",
            "Animators.Update", "Audio.Update",
        };
        private static readonly Dictionary<string, ProfilerRecorder> live = new Dictionary<string, ProfilerRecorder>();

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            string action = args != null && args.TryGetValue("do", out object d) ? d as string : "read";
            if (action == "list")
            {
                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                var names = handles.Select(h => ProfilerRecorderHandle.GetDescription(h))
                    .Select(x => x.Category.Name + "/" + x.Name).OrderBy(x => x).ToArray();
                return new Dictionary<string, object> { ["count"] = names.Length, ["names"] = string.Join("|", names) };
            }
            if (action == "rts")
            {
                // every live render texture, biggest first: name WxH format (MB)
                var all = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.RenderTexture>()
                    .Where(t => t != null && t.IsCreated())
                    .Select(t => (t, mb: UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t) / 1048576.0))
                    .OrderByDescending(x => x.mb).ToArray();
                return new Dictionary<string, object>
                {
                    ["count"] = all.Length, ["totalMB"] = System.Math.Round(all.Sum(x => x.mb), 1),
                    ["list"] = string.Join("|", all.Take(60).Select(x => $"{x.t.name} {x.t.width}x{x.t.height} {x.t.format} {x.mb:F1}MB")),
                };
            }
            if (action == "lights")
            {
                // every live directional light: path, intensity, cookie
                var lights = UnityEngine.Object.FindObjectsOfType<UnityEngine.Light>()
                    .Where(l => l.type == UnityEngine.LightType.Directional)
                    .Select(l => $"{l.transform.root.name}/{l.name} on={l.isActiveAndEnabled} i={l.intensity:F2} cookie={(l.cookie != null ? l.cookie.name : "-")}");
                return new Dictionary<string, object> { ["sun"] = UnityEngine.RenderSettings.sun?.name ?? "-", ["list"] = string.Join("|", lights) };
            }
            if (action == "start")
            {
                Stop();
                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                foreach (ProfilerRecorderHandle h in handles)
                {
                    ProfilerRecorderDescription desc = ProfilerRecorderHandle.GetDescription(h);
                    if (!Wanted.Contains(desc.Name) || live.ContainsKey(desc.Name)) continue;
                    var recorder = new ProfilerRecorder(h, 2400, ProfilerRecorderOptions.Default);
                    recorder.Start();
                    live[desc.Name] = recorder;
                }
                return new Dictionary<string, object> { ["recording"] = live.Count, ["names"] = string.Join("|", live.Keys) };
            }
            var result = new Dictionary<string, object>();
            foreach (KeyValuePair<string, ProfilerRecorder> pair in live)
            {
                ProfilerRecorder r = pair.Value;
                int n = r.Count;
                if (!r.Valid || n == 0) { result[pair.Key] = "invalid"; continue; }
                double sum = 0, max = double.MinValue;
                for (int i = 0; i < n; i++) { double v = r.GetSample(i).Value; sum += v; if (v > max) max = v; }
                // time counters are nanoseconds: report ms; everything else as recorded
                bool time = r.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds;
                result[pair.Key] = time ? $"avg {sum / n / 1e6:F3} ms max {max / 1e6:F2} ms n {n}" : $"avg {sum / n:F0} max {max:F0} n {n} {r.UnitType}";
            }
            if (action == "stop") Stop();
            return result;
        }

        private static void Stop()
        {
            foreach (ProfilerRecorder r in live.Values) r.Dispose();
            live.Clear();
        }
    }
}
