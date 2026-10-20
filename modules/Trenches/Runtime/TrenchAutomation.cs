#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Trenches.Visuals;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Trenches.Runtime
{
    /// <summary>
    /// Explicit nomodkit single-player test hook; inert outside a simulation run. "status" lists the
    /// live positions with their chunk LOD and tint, "view" parks the observer camera on one,
    /// "cutoff" marks one cut off so the relief path can be watched while the field still reads it
    /// as owned.
    /// </summary>
    public static class TrenchAutomation
    {
        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("TrenchAutomation", "Step", "requires a hosted single-player nomodkit scenario");
            // The manager is a scene service on the module host, published as IFieldworksReadiness.
            TrenchManager manager = ModuleServices.TryGet(out IFieldworksReadiness fieldworks) ? fieldworks as TrenchManager : null;
            if (manager == null) return Failure("TrenchAutomation", "Step", "no trench manager");
            IReadOnlyList<TrenchLine> lines = manager.Lines;
            switch (Text(args, "action"))
            {
                case "status": return Status(lines);
                case "view": return View(lines, (int)Number(args, "line", 0f), Number(args, "along", 0.5f),
                    Number(args, "distance", 14f), Number(args, "height", 4f));
                case "cutoff":
                {
                    int index = (int)Number(args, "line", 0f);
                    if (index < 0 || index >= lines.Count) return Failure("TrenchAutomation", "cutoff", "no line " + index);
                    lines[index].Overrun = true;
                    lines[index].RetireAt = Time.time + 300f;
                    return new Dictionary<string, object> { ["ok"] = true, ["line"] = lines[index].Name };
                }
                default: return Failure("TrenchAutomation", "Step", "unknown action");
            }
        }

        private static Dictionary<string, object> Status(IReadOnlyList<TrenchLine> lines)
        {
            var chunks = new Dictionary<string, TrenchVisualChunk>();
            foreach (TrenchVisualChunk chunk in UnityEngine.Object.FindObjectsOfType<TrenchVisualChunk>())
                chunks[chunk.name] = chunk;
            var list = new List<object>(lines.Count);
            foreach (TrenchLine line in lines)
            {
                chunks.TryGetValue($"TrenchChunk_{line.Id}_{line.Name}", out TrenchVisualChunk chunk);
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = line.Name,
                    ["stage"] = line.Stage.ToString(),
                    ["defenders"] = line.DefenderCount,
                    ["overrun"] = line.Overrun,
                    ["stations"] = line.Curve?.Length ?? 0,
                    ["centre"] = new[] { line.Center.x, line.Center.y, line.Center.z },
                    ["lod"] = chunk != null ? chunk.ActiveLod : -2,
                    ["cameraDistance"] = chunk != null ? chunk.CameraDistance : -1f,
                    ["tint"] = chunk != null ? new[] { chunk.EarthTint.r, chunk.EarthTint.g, chunk.EarthTint.b } : null,
                    ["meshes"] = chunk != null ? chunk.MeshCount : 0
                });
            }
            return new Dictionary<string, object> { ["ok"] = true, ["count"] = lines.Count, ["lines"] = list };
        }

        /// <summary>Camera on the owned side of a station, looking across the ditch toward the threat.</summary>
        private static Dictionary<string, object> View(IReadOnlyList<TrenchLine> lines, int index, float along,
            float distance, float height)
        {
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (camera == null) return Failure("TrenchAutomation", "view", "no camera");
            if (index < 0 || index >= lines.Count || lines[index].Curve == null || lines[index].Curve.Length < 2)
                return Failure("TrenchAutomation", "view", "no line " + index);
            TrenchLine line = lines[index];
            int station = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(along) * (line.Curve.Length - 1)), 0, line.Curve.Length - 1);
            Vector3 point = line.Curve[station];
            Vector3 threat = line.ThreatAt(point);
            threat.y = 0f;
            if (threat.sqrMagnitude < 0.0001f) threat = Vector3.forward;
            threat.Normalize();
            Vector3 tangent = (line.Curve[Mathf.Min(station + 1, line.Curve.Length - 1)] -
                line.Curve[Mathf.Max(station - 1, 0)]).normalized;
            // Curve stations are global, so the camera position is too.
            Vector3 at = point - threat * distance * 0.7f + tangent * distance * 0.7f + Vector3.up * height;
            Vector3 look = point + Vector3.up * 0.8f - at;
            camera.SetCameraPosition(new GlobalPosition(at.x, at.y, at.z), Quaternion.LookRotation(look));
            camera.cameraVelocity = Vector3.zero;
            camera.allowInputs = false;
            camera.SetDesiredFoV(55f, 55f);
            return new Dictionary<string, object> { ["ok"] = true, ["line"] = line.Name, ["station"] = station,
                ["point"] = new[] { point.x, point.y, point.z } };
        }
    }
}
#endif
