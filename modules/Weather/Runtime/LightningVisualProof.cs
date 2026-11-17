#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    /// <summary>Explicit rendered-sandbox fixture: holds one production bolt without audio or cloud flashes.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class LightningVisualProof : MonoBehaviour
    {
        private static readonly FieldInfo Lines = typeof(LightningDirector).GetField("bolts", BindingFlags.Instance | BindingFlags.NonPublic);
        private static LightningVisualProof instance;
        private readonly LightningDirector director = new LightningDirector();
        private LightningEvent strike;
        private float gain;

        public static Dictionary<string, object> Capture(Dictionary<string, object> args)
        {
            if (Application.isBatchMode || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                return Failed("Lightning visual proof requires a rendered nomodkit scenario.");
            string action = Text(args, "action") ?? "show";
            if (action == "disable")
            {
                LineRenderer[] previous = instance != null ? (LineRenderer[])instance.Renderers().Clone() : Array.Empty<LineRenderer>();
                if (instance != null) { instance.director.Reset(); instance.enabled = false; Destroy(instance.gameObject); instance = null; }
                return new Dictionary<string, object> { ["ok"] = true, ["activeLines"] = CountEnabled(previous) };
            }
            if (action != "show" && action != "read") return Failed("Use show, read or disable.");
            if (action == "show")
            {
                float x = Number(args, "x", 0f), z = Number(args, "z", 8000f);
                float top = Number(args, "base", 1300f), bottom = Number(args, "bottom", 40f), brightness = Number(args, "gain", 1f);
                float seed = Number(args, "seed", 73f);
                foreach (float number in new[] { x, z, top, bottom, brightness, seed })
                    if (float.IsNaN(number) || float.IsInfinity(number)) return Failed("Fixture values must be finite.");
                if (top <= bottom) return Failed("Cloud base must be above the channel foot.");
                if (instance == null) instance = new GameObject("BoscaliLightningVisualProof").AddComponent<LightningVisualProof>();
                instance.strike = new LightningEvent { X = x, Z = z, Y = top + 1800f, Base = top,
                    BottomY = bottom, Top = top + 6000f, Seed = (uint)Mathf.Clamp(seed, 1f, 16000000f) };
                instance.gain = Mathf.Clamp01(brightness);
            }
            if (instance == null) return Failed("No lightning fixture is active.");
            instance.Draw();
            return instance.Readout();
        }

        private LineRenderer[] Renderers() => Lines?.GetValue(director) as LineRenderer[] ?? Array.Empty<LineRenderer>();
        private void LateUpdate() => Draw(); // Refresh local vertices after the camera's floating-origin shift.
        private void Draw()
        {
            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
            if (camera == null) return;
            float pixelAngle = 2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, camera.pixelHeight);
            float distance = Vector3.Distance(camera.transform.position, new GlobalPosition(strike.X, strike.Y, strike.Z).ToLocalPosition());
            director.DrawBolt(0, strike, gain, distance * pixelAngle);
        }

        private Dictionary<string, object> Readout()
        {
            LineRenderer[] lines = Renderers();
            int active = CountEnabled(lines), points = 0;
            bool finite = true, bounds = true, supported = active > 0, deterministic = true;
            float width = 0f; int queue = 0;
            foreach (LineRenderer line in lines)
            {
                if (line == null || !line.enabled) continue;
                points += line.positionCount; width = Mathf.Max(width, line.startWidth);
                supported &= line.sharedMaterial != null && line.sharedMaterial.shader != null && line.sharedMaterial.shader.isSupported;
                queue = line.sharedMaterial != null ? line.sharedMaterial.renderQueue : 0;
                var before = new Vector3[line.positionCount]; line.GetPositions(before);
                Draw();
                for (int i = 0; i < before.Length; i++)
                {
                    Vector3 p = before[i];
                    finite &= !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));
                    float height = p.GlobalY();
                    bounds &= height >= strike.BottomY - 0.1f && height <= strike.Base + 0.1f;
                    deterministic &= p == line.GetPosition(i);
                }
            }
            return new Dictionary<string, object> { ["ok"] = true, ["activeLines"] = active, ["pointCount"] = points,
                ["shaderSupported"] = supported, ["queue"] = queue, ["width"] = width,
                ["finite"] = finite, ["branchBounds"] = bounds, ["deterministic"] = deterministic, ["seed"] = strike.Seed };
        }

        private static int CountEnabled(LineRenderer[] lines)
        {
            int count = 0; foreach (LineRenderer line in lines) if (line != null && line.enabled) count++; return count;
        }
        private void OnDestroy() { director.Reset(); if (instance == this) instance = null; }
    }
}
#endif
