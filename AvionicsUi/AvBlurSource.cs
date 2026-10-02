using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace NOAvionics
{
    /// <summary>
    /// Opt-in frosted glass for floating windows. After the last camera renders (before overlay UI),
    /// copy the camera target at quarter resolution, at most 15 Hz, and run two dual-Kawase passes.
    /// Nothing is allocated or grabbed while no window holds a reference. If the grab fails the
    /// source reports unsupported and windows use the glass shader's frost mode instead (spec §7.3).
    /// </summary>
    public static class AvBlurSource
    {
        private const int MaxUsers = 16;
        private const float Interval = 1f / 15f;
        private static int users;
        private static bool hooked, grabOk, failed;
        private static float nextGrab;
        private static RenderTexture a, b;
        private static Material blur;
        private static int frames;
        private static Recorder recorder;

        private static bool enabled;

        /// <summary>The player's setting. Takes effect immediately, also while a window is open.</summary>
        public static bool Enabled
        {
            get => enabled;
            set
            {
                if (enabled == value) return;
                enabled = value;
                if (!value) Unhook();
                else if (users > 0) Hook();
            }
        }
        public static bool Supported => grabOk && !failed;
        public static RenderTexture Texture => grabOk ? a : null;

        public static void Acquire()
        {
            if (users < MaxUsers) users++;
            Hook();
        }

        public static void Release()
        {
            if (users > 0) users--;
            if (users == 0) Unhook();
        }

        private static void Hook()
        {
            if (hooked || failed || !Enabled) return;
            Shader s = AvBundle.Shader("NOA/UI/Blur");
            if (s == null) { failed = true; return; }
            if (blur == null) blur = new Material(s) { name = "NOA UI Blur" };
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            hooked = true;
        }

        private static void Unhook()
        {
            if (!hooked) return;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            hooked = false;
            if (a != null) { a.Release(); Object.Destroy(a); a = null; }
            if (b != null) { b.Release(); Object.Destroy(b); b = null; }
            grabOk = false;
        }

        private static void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!Enabled || users == 0 || cam == null || cam.targetTexture != null || cam.cameraType != CameraType.Game) return;
            if (cam != LastScreenCamera()) return;
            if (Time.unscaledTime < nextGrab) return;
            nextGrab = Time.unscaledTime + Interval;
            try
            {
                int w = Mathf.Max(16, Screen.width / 4), h = Mathf.Max(16, Screen.height / 4);
                if (a == null || a.width != w || a.height != h)
                {
                    if (a != null) { a.Release(); Object.Destroy(a); }
                    if (b != null) { b.Release(); Object.Destroy(b); }
                    a = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "NOA Blur A" };
                    b = new RenderTexture(w / 2, h / 2, 0, RenderTextureFormat.ARGB32) { name = "NOA Blur B" };
                }
                var cmd = new CommandBuffer { name = "NOA.UI.Blur" };
                cmd.BeginSample("NOA.UI.Blur");
                cmd.Blit(BuiltinRenderTextureType.CameraTarget, a);
                cmd.SetGlobalFloat("_Offset", 1.5f);
                cmd.Blit(a, b, blur, 0);
                cmd.Blit(b, a, blur, 1);
                cmd.EndSample("NOA.UI.Blur");
                ctx.ExecuteCommandBuffer(cmd);
                cmd.Release();
                grabOk = true;
                frames++;
            }
            catch (System.Exception)
            {
                failed = true;
                Unhook();
            }
        }

        private static Camera LastScreenCamera()
        {
            Camera best = null;
            foreach (Camera c in Camera.allCameras)
                if (c.enabled && c.targetTexture == null && (best == null || c.depth > best.depth)) best = c;
            return best;
        }

        public static Dictionary<string, object> Probe()
        {
            if (recorder == null) { recorder = Recorder.Get("NOA.UI.Blur"); recorder.enabled = true; }
            return new Dictionary<string, object>
            {
                { "grabOk", grabOk }, { "failed", failed }, { "frames", frames },
                { "blurGpuMs", recorder.isValid ? recorder.gpuElapsedNanoseconds / 1e6 : -1.0 },
            };
        }
    }
}
