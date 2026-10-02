using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    // Owns one persistent droplet layer per canopy pane (ping-pong 256x256 RGBA32,
    // hard-capped at MaxPanes pairs = 4 MiB). Keyed by renderer+submesh so a lost
    // pane never donates its puddles to a sibling. All panes step with the same dt
    // and parameters; only the projected flow differs, which is the physically
    // correct per-pane difference (librain-style shared model, auto-calibrated).
    internal sealed class CanopyDropletSim
    {
        internal const int MaxPanes = 8;
        internal const int StateSize = 256;
        private const float StepSeconds = 1f / 30f;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int FlowTilesId = Shader.PropertyToID("_FlowTiles");
        private static readonly int RainId = Shader.PropertyToID("_Rain");
        private static readonly int SpeedId = Shader.PropertyToID("_SpeedN");
        private static readonly int DtId = Shader.PropertyToID("_Dt");
        private static readonly int SimTimeId = Shader.PropertyToID("_SimTime");

        private sealed class Pane
        {
            public RenderTexture Read;
            public RenderTexture Write;
            public Vector2 Salt;
            public bool Disabled;
        }

        private readonly Dictionary<int, Pane> panes = new Dictionary<int, Pane>(MaxPanes);

        private float simTime;
        private float pendingTime;

        internal static int KeyFor(int rendererId, int submesh) => rendererId * 31 + submesh;

        internal bool Update(Material updateMaterial, IReadOnlyList<CanopySurface> surfaces,
            Vector2[] flowsTiles, float rain, float speedNorm, float dt)
        {
            if (updateMaterial == null || surfaces == null) return false;

            pendingTime = Mathf.Min(pendingTime + Mathf.Max(0f, dt), 0.1f);
            if (pendingTime + 0.000001f < StepSeconds) return false;
            float step = Mathf.Min(pendingTime, 0.05f);
            pendingTime -= step;
            simTime += step;
            bool stepped = false;
            for (int i = 0; i < surfaces.Count && i < flowsTiles.Length; i++)
            {
                CanopySurface surface = surfaces[i];
                if (surface.Renderer == null || surface.Mesh == null) continue;
                int key = KeyFor(surface.Renderer.GetInstanceID(), surface.Submesh);
                if (!panes.TryGetValue(key, out Pane pane))
                {
                    if (panes.Count >= MaxPanes) continue;
                    pane = new Pane { Salt = SaltFor(key) };
                    panes[key] = pane;
                }
                if (pane.Disabled) continue;
                EnsureTargets(pane);
                if (pane.Read == null || pane.Write == null)
                {
                    ReleaseTargets(pane);
                    pane.Disabled = true; // a refused allocation must not retry each tick
                    continue;
                }
                updateMaterial.SetTexture(MainTexId, pane.Read);
                updateMaterial.SetVector(FlowTilesId, new Vector4(flowsTiles[i].x, flowsTiles[i].y, 0f, 0f));
                updateMaterial.SetFloat(RainId, Mathf.Clamp01(rain));
                updateMaterial.SetFloat(SpeedId, Mathf.Clamp01(speedNorm));
                updateMaterial.SetFloat(DtId, step);
                updateMaterial.SetFloat(SimTimeId, simTime);
                Graphics.Blit(pane.Read, pane.Write, updateMaterial);
                RenderTexture tmp = pane.Read;
                pane.Read = pane.Write;
                pane.Write = tmp;
                stepped = true;
            }
            return stepped;
        }

        internal RenderTexture StateFor(int key)
        {
            if (panes.TryGetValue(key, out Pane pane)) return pane.Read;
            return null;
        }

        internal Vector2 SaltFor(int key)
        {
            if (panes.TryGetValue(key, out Pane pane)) return pane.Salt;
            uint h = (uint)key * 2654435761u;
            return new Vector2(((h >> 8) & 1023) / 1023f * 8f, (h & 1023) / 1023f * 8f);
        }

        internal int PaneCount => panes.Count;

        internal void Release()
        {
            foreach (KeyValuePair<int, Pane> pair in panes)
            {
                ReleaseTargets(pair.Value);
            }
            panes.Clear();

            simTime = 0f;
            pendingTime = 0f;
        }

        private static void ReleaseTargets(Pane pane)
        {
            if (pane.Read != null) { FxRtPool.Disown(pane.Read); Object.Destroy(pane.Read); pane.Read = null; }
            if (pane.Write != null) { FxRtPool.Disown(pane.Write); Object.Destroy(pane.Write); pane.Write = null; }
        }

        private static void EnsureTargets(Pane pane)
        {
            if (pane.Read == null) pane.Read = AllocTarget();
            if (pane.Write == null) pane.Write = AllocTarget();
        }

        private static RenderTexture AllocTarget()
        {
            // ponytail: ARGB32 is universal and lean; go half-float if banding shows.
            var target = new RenderTexture(StateSize, StateSize, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!target.Create()) { Object.Destroy(target); return null; }
            if (!FxRtPool.Own(target)) { Object.Destroy(target); return null; }
            Graphics.SetRenderTarget(target);
            GL.Clear(false, true, Color.black);
            Graphics.SetRenderTarget(null);
            return target;
        }
    }
}
