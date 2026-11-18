using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>A real density volume: rising stem, turbulent cap and rolling torus. Shared weather noise, bounded half-size march.</summary>
    internal sealed class Sa74Mushroom : MonoBehaviour, ISceneService
    {
        private const string Resource = "BoscaliSummer.Vanguard.sa74-mushroom.bundle";
        private static AssetBundle bundle;
        private static Shader shader;
        private static bool loaded;
        private static Sa74Mushroom instance;
        private static readonly uint[] renderedSiteIds = new uint[2];
        private static int renderedSiteCount;
        private readonly Vector4[] sites = new Vector4[2];
        private readonly uint[] siteIds = new uint[2];
        private readonly MushroomPass pass = new MushroomPass();
        private static readonly FieldInfo NativeCloud = typeof(MushroomCloud).GetField("cloud", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NativeCloudMesh = NativeCloud?.FieldType.GetField("meshRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NativeFireball = typeof(MushroomCloud).GetField("fireball", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly List<NativeCloudView> nativeClouds = new List<NativeCloudView>();
        private int observedEvents = -1;
        private Material material;
        private Texture3D noise;
        private readonly struct NativeCloudView
        {
            internal readonly Renderer Renderer;
            internal readonly bool WasForcedOff;
            internal readonly uint SiteId;
            internal NativeCloudView(Renderer renderer, uint siteId) { Renderer = renderer; WasForcedOff = renderer.forceRenderingOff; SiteId = siteId; }
        }
        internal static bool Ready { get; private set; }
        internal static int RenderedFrame { get; private set; } = -1;
        internal static int VisibleSites { get; private set; }
        internal static int TargetWidth { get; private set; }
        internal static int TargetHeight { get; private set; }
        internal static int NativeFallbackRenderers { get; private set; }
        internal static int NativeSuppressedRenderers { get; private set; }
        internal static int DispersedSites { get; private set; }
        internal static float RemainingSeconds { get; private set; }
        internal static bool CompositedAfterWeather { get; private set; }
        internal static long TargetBytes => (long)TargetWidth * TargetHeight * 12L;
        internal static bool DrawsSite(uint siteId)
        {
            if (instance == null || RenderedFrame < Time.frameCount - 1) return false;
            for (int i = 0; i < renderedSiteCount; i++) if (renderedSiteIds[i] == siteId) return true;
            return false;
        }
        private static void MarkRendered()
        {
            RenderedFrame = Time.frameCount;
            renderedSiteCount = instance != null ? VisibleSites : 0;
            for (int i = 0; i < renderedSiteCount; i++) renderedSiteIds[i] = instance.siteIds[i];
        }

        private void Awake()
        {
            if (Application.isBatchMode) return;
            instance = this;
            if (ModuleServices.TryGet(out ICloudVolumeView clouds)) clouds.GetNoise(out _);
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }

        private bool Prepare()
        {
            if (!loaded)
            {
                loaded = true;
                byte[] bytes = EmbeddedResources.ReadAll(typeof(Sa74Mushroom).Assembly, Resource, 4 * 1024 * 1024);
                bundle = bytes != null ? AssetBundle.LoadFromMemory(bytes) : null;
                if (bundle != null)
                    foreach (Shader candidate in bundle.LoadAllAssets<Shader>()) if (candidate.name == "Boscali/SA74Mushroom") shader = candidate;
                if (shader == null) Debug.LogWarning("[Vanguard] SA-74N volumetric mushroom shader bundle is missing.");
            }
            if (shader == null || !shader.isSupported) return false;
            if (noise == null)
            {
                if (!ModuleServices.TryGet(out ICloudVolumeView clouds)) return false;
                byte[] data = clouds.GetNoise(out int size);
                if (data == null) return false;
                noise = new Texture3D(size, size, size,
                    TextureFormat.RGBA32, true) { name = "SA-74N shared cloud billows", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
                noise.SetPixelData(data, 0);
                noise.Apply(true, true);
            }
            if (material == null)
            {
                material = new Material(shader) { name = "SA-74N volumetric mushroom", hideFlags = HideFlags.DontUnloadUnusedAsset };
                material.SetTexture("_Sa74Noise", noise);
            }
            Ready = true;
            return true;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!Sa74Fallout.Active)
            {
                NativeCloudFallback(false);
                VisibleSites = NativeFallbackRenderers = NativeSuppressedRenderers = 0;
                DispersedSites = 0; RemainingSeconds = 0f;
                pass.ReleaseTargets(); pass.Bind(null, null);
                RenderedFrame = -1;
                return;
            }
            if (Application.isBatchMode || camera == null || camera.cameraType != CameraType.Game ||
                (ModuleServices.TryGet(out IWeatherCameraExclusion exclusion) && exclusion.Contains(camera))) return;
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            if (cameraData == null || cameraData.renderType != CameraRenderType.Base) return;
            NativeCloudFallback(false);
            int count = 0;
            Vector4 groundClearance = Vector4.zero;
            DispersedSites = 0; RemainingSeconds = 0f;
            // ponytail: two newest concurrent volumes bound fragment work; omitted sites keep their native cloud fallback.
            for (int i = Sa74Fallout.Sites.Count - 1; i >= 0; i--)
            {
                Sa74Fallout.Site site = Sa74Fallout.Sites[i];
                float age = Mathf.Max(0f, Time.time - site.Started);
                if (age >= Sa74BlastShape.MaximumVisualAge) { DispersedSites++; continue; }
                if (count >= sites.Length) continue;
                Vector3 at = site.Position.ToLocalPosition();
                if ((at - camera.transform.position).sqrMagnitude > 250000f * 250000f) continue;
                siteIds[count] = site.Id;
                groundClearance[count] = site.GroundClearance;
                sites[count++] = new Vector4(at.x, Mathf.Max(at.y, Datum.LocalSeaY + 8f), at.z, age);
                RemainingSeconds = Mathf.Max(RemainingSeconds, Sa74BlastShape.MaximumVisualAge - age);
            }
            VisibleSites = count;
            if (count == 0)
            {
                pass.ReleaseTargets(); pass.Bind(null, null); RenderedFrame = -1;
                return;
            }
            if (cameraData.scriptableRenderer == null || !Prepare()) return;
            NativeCloudFallback(RenderedFrame >= Time.frameCount - 1);
            material.SetInt("_Sa74Count", count);
            material.SetVectorArray("_Sa74Sites", sites);
            material.SetVector("_Sa74Clearance", groundClearance);
            material.SetFloat("_Sa74ShockRadius", Sa74Fallout.ShockRadius);
            Vector3 sun = RenderSettings.sun != null ? -RenderSettings.sun.transform.forward : new Vector3(.45f, .65f, -.40f).normalized;
            material.SetVector("_Sa74Sun", sun);
            material.SetColor("_Sa74SunColor", RenderSettings.sun != null ? RenderSettings.sun.color.linear * Mathf.Clamp(RenderSettings.sun.intensity, .2f, 1.5f) : Color.white);
            material.SetColor("_Sa74Fog", RenderSettings.fogColor.linear);
            pass.Bind(camera, material);
            cameraData.scriptableRenderer.EnqueuePass(pass);
            cameraData.scriptableRenderer.EnqueuePass(pass.DeferredComposite);
        }

        private void NativeCloudFallback(bool hide, bool retireDispersed = true)
        {
            if (observedEvents != Sa74Fallout.Events)
            {
                observedEvents = Sa74Fallout.Events;
                foreach (MushroomCloud cloud in UnityEngine.Object.FindObjectsOfType<MushroomCloud>())
                {
                    Sa74BlastTag tag = cloud.GetComponentInParent<Sa74BlastTag>();
                    if (tag == null) continue;
                    object owner = NativeCloud?.GetValue(cloud);
                    Track(owner != null ? NativeCloudMesh?.GetValue(owner) as Renderer : null, tag.SiteId);
                    object optional = NativeFireball?.GetValue(cloud);
                    object fireball = optional?.GetType().GetProperty("Value")?.GetValue(optional, null);
                    Track(fireball?.GetType().GetField("renderer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(fireball) as Renderer, tag.SiteId);
                }
            }
            int fallbackRenderers = 0, suppressedRenderers = 0;
            foreach (NativeCloudView view in nativeClouds)
            {
                if (view.Renderer == null) continue;
                bool replaced = false;
                if (retireDispersed)
                    foreach (Sa74Fallout.Site site in Sa74Fallout.Sites)
                        if (site.Id == view.SiteId && Time.time - site.Started >= Sa74BlastShape.MaximumVisualAge) { replaced = true; break; }
                if (hide)
                    for (int i = 0; i < VisibleSites; i++) replaced |= siteIds[i] == view.SiteId;
                view.Renderer.forceRenderingOff = view.WasForcedOff || replaced;
                if (replaced) suppressedRenderers++;
                else if (!view.WasForcedOff) fallbackRenderers++;
            }
            NativeFallbackRenderers = fallbackRenderers; NativeSuppressedRenderers = suppressedRenderers;

            void Track(Renderer renderer, uint siteId)
            {
                if (renderer != null && !nativeClouds.Exists(view => view.Renderer == renderer))
                    nativeClouds.Add(new NativeCloudView(renderer, siteId));
            }
        }

        public void ResetForScene()
        {
            NativeCloudFallback(false, false); nativeClouds.Clear(); observedEvents = -1;
            NativeFallbackRenderers = NativeSuppressedRenderers = 0;
            DispersedSites = 0; RemainingSeconds = 0f;
            pass.ReleaseTargets(); RenderedFrame = -1; VisibleSites = 0;
        }
        private void OnDestroy()
        {
            if (instance == this) { instance = null; renderedSiteCount = 0; }
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            NativeCloudFallback(false, false); nativeClouds.Clear();
            NativeFallbackRenderers = NativeSuppressedRenderers = 0;
            pass.ReleaseTargets();
            if (material != null) Destroy(material);
            if (noise != null) Destroy(noise);
            Ready = false; RenderedFrame = -1; VisibleSites = 0;
            DispersedSites = 0; RemainingSeconds = 0f;
        }

        private sealed class MushroomPass : ScriptableRenderPass
        {
            private readonly RenderTargetIdentifier[] targets = new RenderTargetIdentifier[2];
            private Camera camera;
            private Material material;
            private RenderTexture color, depth;
            private int requestedWidth, requestedHeight;
            private float retryAt;
            private long refusedBytes = -1;
            private int marchedFrame = -1;
            private bool deferComposite;
            internal readonly ScriptableRenderPass DeferredComposite;
            internal MushroomPass()
            {
                // Native transparents stay in front if Weather is disabled, full-size or unavailable.
                renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 1);
                ConfigureInput(ScriptableRenderPassInput.Depth);
                DeferredComposite = new CompositePass(this);
            }
            internal void Bind(Camera view, Material shaderMaterial) { camera = view; material = shaderMaterial; }
            private bool Ensure(int width, int height)
            {
                float scale = Mathf.Min(.5f, Mathf.Min(960f / Mathf.Max(1, width), 540f / Mathf.Max(1, height)));
                if (color != null && requestedWidth == width && requestedHeight == height && color.IsCreated() && depth.IsCreated()) return true;
                if (refusedBytes == FxRtPool.UsedBytes && requestedWidth == width && requestedHeight == height && Time.unscaledTime < retryAt) return false;
                ReleaseTargets();
                requestedWidth = width; requestedHeight = height;
                for (int attempt = 0; attempt < 3; attempt++, scale *= .7f)
                {
                    int w = Mathf.Max(2, Mathf.CeilToInt(width * scale)), h = Mathf.Max(2, Mathf.CeilToInt(height * scale));
                    color = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf) { name = "SA-74N half-size density", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    depth = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat) { name = "SA-74N scene-depth resolve", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    if (color.Create() && depth.Create() && FxRtPool.Own(color) && FxRtPool.Own(depth))
                    {
                        TargetWidth = w; TargetHeight = h; refusedBytes = -1;
                        material.SetTexture("_Sa74Color", color); material.SetTexture("_Sa74Depth", depth);
                        material.SetVector("_Sa74Size", new Vector4(w, h, 1f / w, 1f / h));
                        return true;
                    }
                    ReleaseTargets();
                }
                refusedBytes = FxRtPool.UsedBytes; retryAt = Time.unscaledTime + .5f;
                return false;
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (camera == null || renderingData.cameraData.camera != camera || material == null) return;
                RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
                if (!Ensure(descriptor.width, descriptor.height)) return;
                Matrix4x4 inverseView = camera.worldToCameraMatrix.inverse;
                Matrix4x4 inverseProjection = renderingData.cameraData.GetProjectionMatrix().inverse;
                material.SetMatrix("_Sa74Frustum", VolumeFrustum.Rays(inverseView, inverseProjection));
                material.SetVector("_Sa74Camera", inverseView.GetColumn(3));
                CommandBuffer cmd = CommandBufferPool.Get("SA-74N volumetric mushroom");
                try
                {
                    cmd.DisableScissorRect();
                    targets[0] = color; targets[1] = depth;
                    cmd.SetRenderTarget(targets, color);
                    cmd.SetViewport(new Rect(0f, 0f, color.width, color.height));
                    cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                    deferComposite = ModuleServices.TryGet(out ICloudVolumeView clouds) && clouds.HasResolved(camera);
                    CompositedAfterWeather = deferComposite;
                    RestoreTarget(cmd, ref renderingData);
                    if (!deferComposite) cmd.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);
                    context.ExecuteCommandBuffer(cmd);
                    marchedFrame = Time.frameCount;
                    if (!deferComposite) MarkRendered();
                }
                finally { CommandBufferPool.Release(cmd); }
            }
            private static void RestoreTarget(CommandBuffer cmd, ref RenderingData renderingData)
            {
                ScriptableRenderer renderer = renderingData.cameraData.renderer;
                cmd.SetRenderTarget(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
                // pixelRect is in display pixels; URP's intermediate may be scaled (DLSS/renderScale).
                RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
                cmd.SetViewport(new Rect(0f, 0f, descriptor.width, descriptor.height));
            }
            private sealed class CompositePass : ScriptableRenderPass
            {
                private readonly MushroomPass owner;
                internal CompositePass(MushroomPass owner) { this.owner = owner; renderPassEvent = RenderPassEvent.AfterRenderingTransparents; }
                public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
                {
                    if (!owner.deferComposite || owner.marchedFrame != Time.frameCount || owner.camera != renderingData.cameraData.camera || owner.material == null) return;
                    CommandBuffer cmd = CommandBufferPool.Get("SA-74N weather-ordered composite");
                    try
                    {
                        cmd.DisableScissorRect();
                        RestoreTarget(cmd, ref renderingData);
                        cmd.DrawProcedural(Matrix4x4.identity, owner.material, 1, MeshTopology.Triangles, 3);
                        context.ExecuteCommandBuffer(cmd);
                        MarkRendered();
                    }
                    finally { CommandBufferPool.Release(cmd); }
                }
            }
            internal void ReleaseTargets()
            {
                marchedFrame = -1; deferComposite = false; CompositedAfterWeather = false;
                if (color != null) { FxRtPool.Disown(color); color.Release(); UnityEngine.Object.Destroy(color); }
                if (depth != null) { FxRtPool.Disown(depth); depth.Release(); UnityEngine.Object.Destroy(depth); }
                color = depth = null;
                TargetWidth = TargetHeight = 0;
            }
        }
    }
}
