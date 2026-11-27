using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace NOAvionics
{
    /// <summary>
    /// Opt-in frosted glass for floating windows. After the last camera renders (before overlay UI),
    /// copy the camera target at quarter resolution, at most 30 Hz, and run four dual-Kawase passes (down to 1/16, back up).
    /// Nothing is allocated or grabbed while no window holds a reference. If the grab fails the
    /// source reports unsupported and windows use the glass shader's frost mode instead (spec §7.3).
    /// </summary>
    public static class AvBlurSource
    {
        private const float Interval = 1f / 30f;
        private static int users;
        private static bool hooked, grabOk, failed;
        private static float nextGrab;
        private static RenderTexture a, b, c;
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
        public static RenderTexture Texture => grabOk ? a : null;

        public static void Acquire()
        {
            users++;
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
            if (c != null) { c.Release(); Object.Destroy(c); c = null; }
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
                    if (c != null) { c.Release(); Object.Destroy(c); }
                    a = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "NOA Blur A" };
                    b = new RenderTexture(Mathf.Max(8, w / 2), Mathf.Max(8, h / 2), 0, RenderTextureFormat.ARGB32) { name = "NOA Blur B" };
                    c = new RenderTexture(Mathf.Max(4, w / 4), Mathf.Max(4, h / 4), 0, RenderTextureFormat.ARGB32) { name = "NOA Blur C" };
                }
                var cmd = new CommandBuffer { name = "NOA.UI.Blur" };
                cmd.BeginSample("NOA.UI.Blur");
                cmd.Blit(BuiltinRenderTextureType.CameraTarget, a);
                // Frosted glass wants a wide, smooth spread: two octaves down, two up. No frost noise: grain reads as static in game.
                blur.SetFloat("_Offset", 2f);   // the material's own _Offset property hides a global
                cmd.Blit(a, b, blur, 0);
                cmd.Blit(b, c, blur, 0);
                cmd.Blit(c, b, blur, 1);
                cmd.Blit(b, a, blur, 1);
                cmd.EndSample("NOA.UI.Blur");
                // Immediate: URP has already submitted ctx when endCameraRendering fires, so a queued buffer grabbed black.
                Graphics.ExecuteCommandBuffer(cmd);
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
                if (c.enabled && c.cameraType == CameraType.Game && c.targetTexture == null && (best == null || c.depth > best.depth)) best = c;
            return best;
        }

        public static Dictionary<string, object> Probe()
        {
            if (recorder == null) { recorder = Recorder.Get("NOA.UI.Blur"); recorder.enabled = true; }
            return new Dictionary<string, object>
            {
                { "grabOk", grabOk }, { "failed", failed }, { "frames", frames },
                { "blurGpuMs", recorder.isValid ? recorder.gpuElapsedNanoseconds / 1e6 : -1.0 },
                { "meanLuma", MeanLuma(c) }, { "lumaA", MeanLuma(a) },
                { "frostLayers", CountFrost() },
            };
        }

        // Diagnostics only: is the grabbed picture black (grab reads the wrong target) or real?
        private static double MeanLuma(RenderTexture c)
        {
            if (!grabOk || c == null) return -1.0;
            RenderTexture prev = RenderTexture.active;
            var t = new Texture2D(c.width, c.height, TextureFormat.RGBA32, false);
            RenderTexture.active = c;
            t.ReadPixels(new Rect(0, 0, c.width, c.height), 0, 0);
            RenderTexture.active = prev;
            double sum = 0;
            Color32[] px = t.GetPixels32();
            foreach (Color32 p in px) sum += (p.r + p.g + p.b) / 3.0;
            Object.Destroy(t);
            return px.Length > 0 ? sum / px.Length : -1.0;
        }

        private static string CountFrost()
        {
            int all = 0, shown = 0;
            foreach (AvFrost f in Object.FindObjectsOfType<AvFrost>())
            {
                all++;
                var img = f.GetComponent<UnityEngine.UI.RawImage>();
                if (img != null && img.enabled && img.texture != null) shown++;
            }
            return shown + "/" + all;
        }
    }
}
