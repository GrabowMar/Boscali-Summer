using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Visuals
{
    // Owns one persistent droplet layer per canopy pane (ping-pong 512x512 RGBA32,
    // hard-capped at MaxPanes pairs = 16 MB). Keyed by renderer+submesh so a lost
    // pane never donates its puddles to a sibling. All panes step with the same dt
    // and parameters; only the projected flow differs, which is the physically
    // correct per-pane difference (librain-style shared model, auto-calibrated).
    internal sealed class CanopyDropletSim
    {
        internal const int MaxPanes = 8;
        internal const int StateSize = 512;

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
        }

        private readonly Dictionary<int, Pane> panes = new Dictionary<int, Pane>(MaxPanes);

        private float simTime;

        internal static int KeyFor(int rendererId, int submesh) => rendererId * 31 + submesh;

        internal bool Update(Material updateMaterial, IReadOnlyList<CanopySurface> surfaces,
            Vector2[] flowsTiles, float rain, float speedNorm, float dt)
        {
            if (updateMaterial == null || surfaces == null) return false;

            simTime += Mathf.Max(0f, dt);
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
                EnsureTargets(pane);
                if (pane.Read == null || pane.Write == null) continue;
                updateMaterial.SetTexture(MainTexId, pane.Read);
                updateMaterial.SetVector(FlowTilesId, new Vector4(flowsTiles[i].x, flowsTiles[i].y, 0f, 0f));
                updateMaterial.SetFloat(RainId, Mathf.Clamp01(rain));
                updateMaterial.SetFloat(SpeedId, Mathf.Clamp01(speedNorm));
                updateMaterial.SetFloat(DtId, dt);
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

        internal void Release()
        {
            foreach (KeyValuePair<int, Pane> pair in panes)
            {
                if (pair.Value.Read != null) Object.Destroy(pair.Value.Read);
                if (pair.Value.Write != null) Object.Destroy(pair.Value.Write);
            }
            panes.Clear();

            simTime = 0f;
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
            Graphics.SetRenderTarget(target);
            GL.Clear(false, true, Color.black);
            Graphics.SetRenderTarget(null);
            return target;
        }
    }
}
