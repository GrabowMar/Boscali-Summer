using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Immersion.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    // ponytail: one front-view pilot plane approximates depth; use geometry only if live parallax requires it.
    internal sealed class PilotReflection
    {
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly Plane[] frustum = new Plane[6];
        private readonly CanopyPane[] surfaces = new CanopyPane[8];
        private ICanopyGlassView canopy;
        private static readonly int CaptureId = Shader.PropertyToID("_CaptureVP");
        private static readonly int TextureId = Shader.PropertyToID("_PilotTex");
        private static readonly int WorldToPilotId = Shader.PropertyToID("_WorldToPilot");
        private static readonly int RectId = Shader.PropertyToID("_CaptureRect");
        private static readonly int EyeId = Shader.PropertyToID("_EyeWorld");
        private RenderTexture target;
        private CommandBuffer capture;
        private Material glass;
        private CockpitPilotRig boundRig;
        private Vector3 localCenter;
        private Vector4 captureRect;
        private double capturedAt = double.NegativeInfinity;
        private bool contentValid;
        private float retryAt;
        private int captures, draws;
        internal int CaptureCount => captures;
        internal int GlassDraws => draws;
        internal RenderTexture Target => target;
        internal void SetGlassView(ICanopyGlassView value) => canopy = value;

        internal void Tick(CockpitPilotRig rig, Transform cockpit, Camera eye, Camera renderCamera, float ambient, bool enabled)
        {
            draws = 0;
            if (!enabled || canopy == null || rig == null || !rig.Valid || cockpit == null || eye == null || renderCamera == null)
            { Release(); return; }
            if (boundRig != rig)
            {
                Release(); boundRig = rig;
                localCenter = rig.Frame.InverseTransformPoint(rig.Head.position - rig.Frame.up * .35f);
            }
            int surfaceCount = canopy.Resolve(cockpit, eye.transform.position, renderCamera.cullingMask, surfaces);
            GeometryUtility.CalculateFrustumPlanes(renderCamera, frustum);
            bool paneVisible = false;
            for (int i = 0; i < surfaceCount; i++) if (Visible(surfaces[i], renderCamera)) { paneVisible = true; break; }
            if (!paneVisible) { contentValid = false; return; }
            int size = FxBus.EffectiveQuality == FxQuality.Low ? 128 : 256;
            if (target != null && target.width != size) DropTarget();
            if (!EnsureTarget(size)) return;
            glass.SetFloat("_ReflectionStrength", .085f + (1f - Mathf.Clamp01(ambient)) * .025f);
            Vector3 center = rig.Frame.TransformPoint(localCenter);
            Matrix4x4 planeToWorld = Matrix4x4.TRS(center, rig.Frame.rotation, Vector3.one);
            double now = Time.unscaledTimeAsDouble;
            if (now < capturedAt) { capturedAt = double.NegativeInfinity; contentValid = false; }
            if (PilotPoseMath.CaptureDue(now, capturedAt)) Capture(rig, planeToWorld, now);
            if (!contentValid || now - capturedAt > .25) return;
            properties.SetTexture(TextureId, target);
            properties.SetMatrix(WorldToPilotId, planeToWorld.inverse);
            properties.SetVector(RectId, captureRect);
            Vector3 eyePoint = eye.transform.position;
            properties.SetVector(EyeId, new Vector4(eyePoint.x, eyePoint.y, eyePoint.z, 1));
            for (int i = 0; i < surfaceCount && draws < 8; i++)
            {
                CanopyPane pane = surfaces[i];
                if (!Visible(pane, renderCamera)) continue;
                Graphics.DrawMesh(pane.Mesh, pane.Renderer.localToWorldMatrix, glass, pane.Renderer.gameObject.layer,
                    renderCamera, pane.Submesh, properties, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
                draws++;
            }
        }

        private bool Visible(CanopyPane surface, Camera camera) => surface.Renderer != null && surface.Mesh != null &&
            surface.Renderer.enabled && !surface.Renderer.forceRenderingOff && surface.Renderer.gameObject.activeInHierarchy &&
            (surface.Lod == null || surface.Renderer.isVisible) && (camera.cullingMask & (1 << surface.Renderer.gameObject.layer)) != 0 &&
            GeometryUtility.TestPlanesAABB(frustum, surface.Renderer.bounds);

        private bool EnsureTarget(int size)
        {
            if (target != null) return true;
            if (Time.unscaledTime < retryAt) return false;
            retryAt = Time.unscaledTime + 2f;
            Shader shader = PilotShaderBundle.GetReflection();
            if (shader == null) return false;
            if (glass == null) glass = new Material(shader) { name = "Boscali.PilotReflection", hideFlags = HideFlags.HideAndDontSave };
            var candidate = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
            { name = "Boscali.PilotCapture", hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1,
                useMipMap = false, autoGenerateMips = false, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!FxRtPool.Own(candidate)) { Object.Destroy(candidate); return false; }
            if (!candidate.Create()) { FxRtPool.Disown(candidate); Object.Destroy(candidate); return false; }
            target = candidate;
            if (capture == null) capture = new CommandBuffer { name = "Boscali pilot-only capture" };
            contentValid = false;
            return true;
        }

        private void Capture(CockpitPilotRig rig, Matrix4x4 planeToWorld, double now)
        {
            Bounds bounds = rig.Renderer.bounds;
            Matrix4x4 worldToPlane = planeToWorld.inverse;
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1,
                    (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = worldToPlane.MultiplyPoint3x4(corner);
                min = Vector2.Min(min, new Vector2(point.x, point.y)); max = Vector2.Max(max, new Vector2(point.x, point.y));
            }
            Vector2 size = max - min + new Vector2(.08f, .08f);
            if (!PilotPoseMath.Finite(size.x) || !PilotPoseMath.Finite(size.y) || size.x <= .01f || size.y <= .01f) return;
            captureRect = new Vector4(min.x - .04f, min.y - .04f, size.x, size.y);
            Vector3 position = planeToWorld.MultiplyPoint3x4(new Vector3((min.x + max.x) * .5f, (min.y + max.y) * .5f, 3f));
            Quaternion rotation = Quaternion.LookRotation(-rig.Frame.forward, rig.Frame.up);
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-size.x * .5f, size.x * .5f,
                -size.y * .5f, size.y * .5f, .01f, 6f), true);
            rig.Material.SetMatrix(CaptureId, projection * view);
            RenderTexture previous = RenderTexture.active;
            capture.Clear();
            capture.SetRenderTarget(target);
            capture.ClearRenderTarget(true, true, Color.clear);
            capture.DrawRenderer(rig.Renderer, rig.Material, 0, 1);
            if (previous != null) capture.SetRenderTarget(previous);
            else capture.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            capture.SetViewport(new Rect(0, 0, previous != null ? previous.width : Screen.width,
                previous != null ? previous.height : Screen.height));
            Graphics.ExecuteCommandBuffer(capture);
            capturedAt = now; contentValid = true; captures++;
        }

        private void DropTarget()
        {
            if (target != null) { FxRtPool.Disown(target); target.Release(); Object.Destroy(target); }
            // Preserve submission time through visibility, quality and toggle changes: still at most 10 Hz.
            target = null; contentValid = false;
        }

        internal void Release()
        {
            DropTarget();
            if (glass != null) Object.Destroy(glass);
            glass = null;
            if (capture != null) capture.Release();
            capture = null; boundRig = null; draws = captures = 0; retryAt = 0f; properties.Clear();
            for (int i = 0; i < surfaces.Length; i++) surfaces[i] = default;
        }

        internal void Describe(IDictionary<string, object> state)
        {
            state["pilotCaptures"] = captures; state["pilotGlassDraws"] = draws;
            state["pilotRtBytes"] = target != null ? target.width * target.height * 6 : 0;
        }
    }
}
