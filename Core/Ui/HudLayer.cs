using System;
using System.Collections.Generic;
using NOAvionics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// THE AV LAYER (2026-11-17): every mod HUD block lives inside the game's own HUD canvas (vanilla keeps visibility: HUD toggle, map, death, cinematics) on two roots, and every
    /// world-locked or per-frame element is placed at one late point. <see cref="Under"/> is a nested canvas below the native symbols (world ink); the Over host
    /// (<see cref="Static"/>/<see cref="Data"/>/<see cref="Motion"/>) sits above them. Both stretch over the canvas at identity scale, so moving a full-screen holder in is coordinate neutral.
    /// The late point is <see cref="Canvas.preWillRenderCanvases"/>: after every LateUpdate (camera pose, shake, head, FOV, origin shift) and before the UGUI rebuild, so what is
    /// written lands in this frame's batch. It fires <see cref="Repair"/> (native reprojection) then <see cref="Paint"/> (mod ink), each subscriber isolated. A main-camera
    /// pose change after that point redoes both and forces a canvas rebuild (<see cref="ForcedRebuilds"/>). FAIL OPEN: <see cref="RepairOkFrame"/> only advances when every Repair
    /// subscriber returned, so a native suppression keyed on it hands placement back to the game the next frame. <see cref="Enabled"/> false (the A/B switch) runs neither phase.
    /// </summary>
    internal static class HudLayer
    {
        /// <summary>The A/B switch: false restores the old placement (no late pass, so <see cref="RepairOkFrame"/> goes stale and natives project themselves; subscribers run their old path while it is off).</summary>
        public static bool Enabled = true;

        public static RectTransform Under, Static, Data, Motion;
        /// <summary>The Over host (its <see cref="AvHudHost.Group"/>/<see cref="AvHudHost.Reveal"/> fade every Over block together); null until <see cref="Ensure"/> found the HUD canvas.</summary>
        public static AvHudHost Over;
        public static Canvas HudCanvas;

        /// <summary>The last frame the Repair phase ran with at least one subscriber and none threw.</summary>
        public static int RepairOkFrame = -1;
        /// <summary>Frames the late pass ran; frames a late main-camera pose change forced a second pass and a canvas rebuild.</summary>
        public static int LateFrames, ForcedRebuilds;
        /// <summary>Set by the native marker patch while <c>CombatHUD.UpdateMarkers</c> runs.</summary>
        public static bool InNativeMarkers;

        private static readonly List<Action<HudView>> repair = new List<Action<HudView>>(), paint = new List<Action<HudView>>();

        /// <summary>Native reprojection, first. Subscribers are called in order and each is isolated: one throwing never stops the next.</summary>
        public static event Action<HudView> Repair { add => Add(repair, value); remove => repair.Remove(value); }
        /// <summary>Mod world-locked and per-frame ink, after Repair.</summary>
        public static event Action<HudView> Paint { add => Add(paint, value); remove => paint.Remove(value); }

        /// <summary>The view of the last late pass (Valid false before the first).</summary>
        public static HudView View { get; private set; }

        private static bool hooked, running;
        private static int lastPassFrame = -1, errors;

        private static void Add(List<Action<HudView>> list, Action<HudView> handler)
        {
            if (handler != null && !list.Contains(handler)) list.Add(handler);
            Hook();
        }

        private static void Hook()
        {
            if (hooked) return;
            hooked = true;
            Canvas.preWillRenderCanvases += OnLate;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        /// <summary>Builds the two roots in the game's HUD canvas, or re-finds them; false while there is no HUD canvas. Cheap when built.</summary>
        public static bool Ensure()
        {
            Hook();
            if (Under != null && Static != null && HudCanvas != null) return true;
            Canvas canvas = FindHudCanvas();
            if (canvas == null) return false;
            var root = (RectTransform)canvas.transform;
            if (Under != null) UnityEngine.Object.Destroy(Under.gameObject);      // half a pair (the other died): rebuild both
            Over?.Destroy();

            var under = new GameObject("BoscaliHudUnder", typeof(RectTransform), typeof(Canvas));
            Under = (RectTransform)under.transform;
            Under.SetParent(root, false);
            Under.anchorMin = Vector2.zero;
            Under.anchorMax = Vector2.one;
            Under.pivot = new Vector2(0.5f, 0.5f);
            Under.offsetMin = Under.offsetMax = Vector2.zero;
            Under.localScale = Vector3.one;
            under.GetComponent<Canvas>().overrideSorting = false;
            Under.SetAsFirstSibling();      // the world is behind the HUD

            Over = AvHudHost.Nested(root, "BoscaliHudOver");
            Over.Root.transform.SetAsLastSibling();      // the visor glitch overlay re-asserts itself last after this
            Static = Over.Static;
            Data = Over.Data;
            Motion = Over.Motion;
            HudCanvas = canvas;
            return true;
        }

        /// <summary>The game's HUD canvas: the nearest canvas above <c>CombatHUD.iconLayer</c> (the same one <c>NativeRefs.HudCanvas</c> finds), found even while the HUD is hidden.</summary>
        private static Canvas FindHudCanvas()
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || hud.iconLayer == null) return null;
            for (Transform t = hud.iconLayer; t != null; t = t.parent)
                if (t.TryGetComponent(out Canvas c)) return c;
            return null;
        }

        private static void OnLate()
        {
            // A forced rebuild of our own (OnBeginCamera) re-enters here: that pass already ran.
            if (running || !Enabled) return;
            if (!Ensure() || !HudCanvas.isActiveAndEnabled) return;
            Pass();
        }

        private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (running || !Enabled || lastPassFrame != Time.frameCount) return;
            HudView v = View;
            // Auxiliary cameras (target, relief, cockpit overlay, previews) never consume the main view's pass.
            if (!v.Valid || camera != v.Cam) return;
            if (camera.projectionMatrix * camera.worldToCameraMatrix == v.Vp && camera.pixelRect == v.PixelRect) return;
            if (CanvasUpdateRegistry.IsRebuildingGraphics() || CanvasUpdateRegistry.IsRebuildingLayout()) return;
            if (!Pass()) return;
            ForcedRebuilds++;
            running = true;
            try { Canvas.ForceUpdateCanvases(); }
            finally { running = false; }
        }

        private static bool Pass()
        {
            CameraStateManager state = SceneSingleton<CameraStateManager>.i;
            Camera cam = state != null ? state.mainCamera : null;
            if (cam == null) return false;
            var canvasRect = (RectTransform)HudCanvas.transform;
            HudView view = new HudView(cam, canvasRect.rect.size, HudCanvas.scaleFactor);
            View = view;
            if (!view.Valid) return false;

            int frame = view.Frame;
            if (lastPassFrame != frame) LateFrames++;
            lastPassFrame = frame;
            running = true;
            try
            {
                bool ok = repair.Count > 0;
                for (int i = 0; i < repair.Count; i++) ok &= Run(repair[i], view);
                if (ok) RepairOkFrame = frame;
                for (int i = 0; i < paint.Count; i++) Run(paint[i], view);
            }
            finally { running = false; }
            return true;
        }

        private static bool Run(Action<HudView> handler, HudView view)
        {
            try
            {
                handler(view);
                return true;
            }
            catch (Exception e)
            {
                // A handful logged, never a spam: a throwing handler throws every frame.
                if (errors++ < 5) Plugin.Logger?.LogWarning("HUD layer: " + handler.Method.DeclaringType?.Name + "." + handler.Method.Name + " threw: " + e);
                return false;
            }
        }
    }

    /// <summary>
    /// One frame's rendered view, snapshot at the late point: the main camera's view-projection, its pixel rect, the HUD canvas size (canvas units) and scale, the eye.
    /// <see cref="Project"/> maps a world point (local, this frame's origin) to canvas-local units round the canvas centre, the coordinates of a centre-pivoted child of a layer root.
    /// </summary>
    public readonly struct HudView
    {
        /// <summary>The near cut, metres of view depth.</summary>
        public const float Near = 0.5f;

        public readonly Camera Cam;
        public readonly Matrix4x4 Vp;
        public readonly Rect PixelRect;
        public readonly Vector2 CanvasSize;
        public readonly float Scale;
        public readonly Vector3 Eye;
        public readonly int Frame;
        public readonly bool Valid;

        public HudView(Camera cam, Vector2 canvasSize, float scale)
        {
            Cam = cam;
            Vp = cam.projectionMatrix * cam.worldToCameraMatrix;
            PixelRect = cam.pixelRect;
            CanvasSize = canvasSize;
            Scale = scale;
            Eye = cam.transform.position;
            Frame = Time.frameCount;
            Valid = scale > 1e-4f && canvasSize.x > 1f && canvasSize.y > 1f && PixelRect.width > 1f && PixelRect.height > 1f;
        }

        /// <summary>A world point to canvas-local units; false behind the near cut (it may still be off screen: the caller bounds it).</summary>
        public bool Project(Vector3 worldLocal, out Vector2 canvasLocal)
        {
            Vector4 c = Vp * new Vector4(worldLocal.x, worldLocal.y, worldLocal.z, 1f);
            if (!Valid || c.w < Near) { canvasLocal = default; return false; }
            canvasLocal = FromClip(c.x / c.w, c.y / c.w);
            return true;
        }

        /// <summary>A screen pixel (a native overlay object's <c>transform.position</c>) to canvas-local units.</summary>
        public Vector2 FromScreen(Vector2 px) => new Vector2(px.x / Scale - CanvasSize.x * 0.5f, px.y / Scale - CanvasSize.y * 0.5f);

        private Vector2 FromClip(float nx, float ny) =>
            FromScreen(new Vector2(PixelRect.x + (nx * 0.5f + 0.5f) * PixelRect.width, PixelRect.y + (ny * 0.5f + 0.5f) * PixelRect.height));

        /// <summary>
        /// A world segment in canvas-local units, cut at the near plane and to the canvas (5% margin, Liang-Barsky): a line running far off screen costs only what shows;
        /// false when nothing of it shows.
        /// </summary>
        public bool Clip(Vector3 a, Vector3 b, out Vector2 ca, out Vector2 cb)
        {
            ca = cb = default;
            if (!Valid) return false;
            Vector4 p = Vp * new Vector4(a.x, a.y, a.z, 1f), q = Vp * new Vector4(b.x, b.y, b.z, 1f);
            if (p.w < Near && q.w < Near) return false;
            if (p.w < Near) p = Vector4.Lerp(p, q, (Near - p.w) / (q.w - p.w));
            else if (q.w < Near) q = Vector4.Lerp(q, p, (Near - q.w) / (p.w - q.w));
            Vector2 s = FromClip(p.x / p.w, p.y / p.w), e = FromClip(q.x / q.w, q.y / q.w);
            float lx = CanvasSize.x * 0.5f * 1.05f, ly = CanvasSize.y * 0.5f * 1.05f, dx = e.x - s.x, dy = e.y - s.y, t0 = 0f, t1 = 1f;
            if (!Edge(-dx, s.x + lx, ref t0, ref t1) || !Edge(dx, lx - s.x, ref t0, ref t1) || !Edge(-dy, s.y + ly, ref t0, ref t1) || !Edge(dy, ly - s.y, ref t0, ref t1))
                return false;
            ca = new Vector2(s.x + dx * t0, s.y + dy * t0);
            cb = new Vector2(s.x + dx * t1, s.y + dy * t1);
            return true;
        }

        /// <summary>One edge of the Liang-Barsky clip: false when the line lies wholly outside it.</summary>
        private static bool Edge(float p, float q, ref float t0, ref float t1)
        {
            if (p == 0f) return q >= 0f;
            float r = q / p;
            if (p < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }
    }
}
