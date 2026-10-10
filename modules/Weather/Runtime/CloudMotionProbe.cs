using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    /// <summary>ponytail: temporary dev probe (pans the orbit camera, saves every frame); delete after the check.</summary>
    public sealed class CloudMotionProbe : MonoBehaviour
    {
        private static readonly FieldInfo PanField = AccessTools.Field(typeof(CameraOrbitState), "panView");
        private static readonly FieldInfo TiltField = AccessTools.Field(typeof(CameraOrbitState), "tiltView");
        private static float? origin;
        private int frames, done;
        private float step, tilt;
        private string dir;
        private readonly List<Texture2D> shots = new List<Texture2D>();

        public static Dictionary<string, object> Run(Dictionary<string, object> args)
        {
            Shader.SetGlobalFloat("_CloudDebugTint", Bool(args, "tint") ? 1f : 0f);
            Shader.SetGlobalFloat("_CloudDebugMode", Number(args, "mode", 0f));
            var settings = AccessTools.Field(typeof(WeatherManager), "settings").GetValue(WeatherManager.Live) as Configuration.WeatherSettings;
            if (settings != null && args.ContainsKey("temporal")) settings.CloudTemporalUpdate.Value = Bool(args, "temporal");
            if (settings != null && args.ContainsKey("half")) settings.CloudHalfResolution.Value = Bool(args, "half");
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam != null)
            {
                if (origin == null) origin = (float)PanField.GetValue(cam.orbitState);
                PanField.SetValue(cam.orbitState, origin.Value);
            }
            string aa = "none";
            var data = cam != null && cam.mainCamera != null ? UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam.mainCamera) : null;
            if (data != null)
            {
                aa = data.antialiasing + "/" + data.antialiasingQuality;
                if (args.ContainsKey("aa")) data.antialiasing = (UnityEngine.Rendering.Universal.AntialiasingMode)(int)Number(args, "aa", 0f);
            }
            var probe = new GameObject("CloudMotionProbe").AddComponent<CloudMotionProbe>();
            probe.frames = Mathf.Clamp((int)Number(args, "frames", 24f), 2, 60);
            probe.step = Number(args, "step", 2f);
            probe.tilt = Number(args, "tilt", -12f);
            probe.dir = Path.Combine(Path.GetTempPath(), "nomodkit", "cloud-probe-" + (Text(args, "name") ?? "run"));
            Directory.CreateDirectory(probe.dir);
            probe.StartCoroutine(probe.Capture());
            return new Dictionary<string, object> { { "ok", true }, { "dir", probe.dir }, { "aa", aa }, { "points", Points(cam) }, { "mips", args.ContainsKey("mips") ? MipCheck() : "skip" } };
        }

        private static string MipCheck()
        {
            const int n = 64;
            byte[] raw = Visuals.CloudNoise3D.Generate(n, 47);
            var px = new Color32[n * n * n];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(raw[i * 4], raw[i * 4 + 1], raw[i * 4 + 2], 255);
            var tex = new Texture3D(n, n, n, TextureFormat.RGBA32, true);
            tex.SetPixels32(px);
            tex.Apply(true, false);
            Color32[] m1 = tex.GetPixels32(1);
            int h = n / 2;
            double err = 0, errX = 0, errY = 0, errZ = 0;
            for (int z = 0; z < h; z++) for (int y = 0; y < h; y++) for (int x = 0; x < h; x++)
            {
                double box = 0, bx = 0, by = 0, bz = 0;
                for (int k = 0; k < 2; k++) for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++)
                    box += px[(2 * z + k) * n * n + (2 * y + j) * n + 2 * x + i].r;
                for (int j = 0; j < 2; j++) for (int k = 0; k < 2; k++) { bx += px[(2 * z + k) * n * n + (2 * y + j) * n + 2 * x].r; }
                for (int i = 0; i < 2; i++) for (int k = 0; k < 2; k++) { by += px[(2 * z + k) * n * n + (2 * y) * n + 2 * x + i].r; }
                for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++) { bz += px[(2 * z) * n * n + (2 * y + j) * n + 2 * x + i].r; }
                double v = m1[z * h * h + y * h + x].r;
                err += System.Math.Abs(v - box / 8); errX += System.Math.Abs(v - bx / 4); errY += System.Math.Abs(v - by / 4); errZ += System.Math.Abs(v - bz / 4);
            }
            double c = h * h * h;
            Object.Destroy(tex);
            return $"mip1 vs box8 {err / c:F2} | vs 2D-without-x {errX / c:F2} without-y {errY / c:F2} without-z {errZ / c:F2}";
        }

        private static string Points(CameraStateManager cam)
        {
            Camera c = cam != null ? cam.mainCamera : null;
            Light sun = LevelInfo.i != null ? LevelInfo.i.sun : null;
            if (c == null || sun == null) return "n/a";
            Vector3 p = c.transform.position;
            Vector3 toSun = -sun.transform.forward;
            Vector3 sunFlat = new Vector3(toSun.x, 0f, toSun.z).normalized;
            string S(Vector3 d) { Vector3 v = c.WorldToScreenPoint(p + d * 100000f); return $"({v.x:F0},{v.y:F0},{v.z:F0})"; }
            return $"screen {c.pixelWidth}x{c.pixelHeight} sun{S(toSun)} sunHorizon{S(sunFlat)} antiSunHorizon{S(-sunFlat)} east{S(Vector3.right)} north{S(Vector3.forward)} west{S(Vector3.left)} south{S(Vector3.back)} sunDir{toSun}";
        }

        private void Update()
        {
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null || done >= frames) return;
            PanField.SetValue(cam.orbitState, (float)PanField.GetValue(cam.orbitState) + step);
            TiltField.SetValue(cam.orbitState, tilt);
        }

        private IEnumerator Capture()
        {
            while (done < frames)
            {
                yield return new WaitForEndOfFrame();
                var shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
                shot.Apply(false);
                shots.Add(shot);
                done++;
            }
            Shader.SetGlobalFloat("_CloudDebugTint", 0f);
            for (int i = 0; i < shots.Count; i++)
            {
                File.WriteAllBytes(Path.Combine(dir, i.ToString("D3") + ".png"), shots[i].EncodeToPNG());
                Destroy(shots[i]);
            }
            Destroy(gameObject);
        }
    }
}
