using System;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>
    /// Owns the effect globals (_NOA_Now, _NOA_Glitch, _NOA_FxTier) and the shared materials. One hidden
    /// updater sets three floats per frame; no effected Graphic has an Update of its own.
    /// </summary>
    public static class AvFxDriver
    {
        private static readonly int NowId = Shader.PropertyToID("_NOA_Now");
        private static readonly int GlitchId = Shader.PropertyToID("_NOA_Glitch");
        private static readonly int TierId = Shader.PropertyToID("_NOA_FxTier");
        private static readonly int NoiseId = Shader.PropertyToID("_NoiseTex");
        private static Material fx, glass;
        private static Updater updater;

        public static AvFxTier Tier { get; private set; } = AvFxTier.Full;
        public static bool ReducedMotion { get; private set; }
        public static Func<float> GlitchSource { get; set; }
        public static int Revision { get; private set; }

        public static void Configure(AvFxTier tier, bool reducedMotion)
        {
            Tier = tier;
            ReducedMotion = reducedMotion;
            AvReveal.Snap = reducedMotion || tier == AvFxTier.Off;
            Shader.SetGlobalFloat(TierId, (float)tier);
            Revision++;
        }

        public static Material FxMaterial
        {
            get
            {
                if (Tier == AvFxTier.Off) return null;
                if (fx != null && fx.shader == null) fx = null; // bundle unloaded (tests); never happens in game
                if (fx == null)
                {
                    Shader s = AvBundle.Shader("NOA/UI/Fx");
                    if (s == null) return null;
                    fx = new Material(s) { name = "NOA UI Fx (shared)" };
                    if (AvBundle.Noise != null) fx.SetTexture(NoiseId, AvBundle.Noise);
                }
                return fx;
            }
        }

        public static Material GlassMaterial
        {
            get
            {
                if (glass != null && glass.shader == null) glass = null;
                if (glass == null)
                {
                    Shader s = AvBundle.Shader("NOA/UI/Glass");
                    if (s == null) return null;
                    glass = new Material(s) { name = "NOA UI Glass (shared)" };
                    if (AvBundle.Noise != null) glass.SetTexture(NoiseId, AvBundle.Noise);
                }
                return glass;
            }
        }

        public static void EnsureRunning()
        {
            if (updater != null) return;
            var go = new GameObject("NOA Fx Driver") { hideFlags = HideFlags.HideAndDontSave };
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(go); // throws outside play mode (offline checks)
            updater = go.AddComponent<Updater>();
            Shader.SetGlobalFloat(TierId, (float)Tier);
        }

        private sealed class Updater : MonoBehaviour
        {
            private float glitch;

            private void Update()
            {
                Shader.SetGlobalFloat(NowId, Time.unscaledTime);
                float target = 0f;
                if (Tier == AvFxTier.Full && GlitchSource != null)
                {
                    try { target = Mathf.Clamp01(GlitchSource()); } catch (Exception) { GlitchSource = null; }
                }
                glitch = Mathf.MoveTowards(glitch, target, Time.unscaledDeltaTime * 3f);
                Shader.SetGlobalFloat(GlitchId, ReducedMotion ? 0f : glitch);
            }
        }
    }
}
