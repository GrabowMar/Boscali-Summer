using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Immersion.Domain;
using BoscaliSummer.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Immersion.Runtime
{
    /// <summary>
    /// Gives the native cockpit MFD glass a little glow at night. Binds the exact first-person
    /// cockpit renderers (the prefab array <c>Aircraft.SetCockpitRenderers</c> toggles, read even
    /// while disabled) and brightens screen-like ones through a <see cref="MaterialPropertyBlock"/>,
    /// so shared materials and assets are never touched. A screen is an emissive material (the
    /// vanilla night-glow path), a live RenderTexture feed, or a screen-like name; everything else
    /// under the panel is left alone so the cockpit never washes out.
    /// </summary>
    internal sealed class MfdGlow
    {
        private const int MaxPanels = 8;
        private const int MaxVisited = 256;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissiveMapId = Shader.PropertyToID("_EmissiveMap");
        private static readonly string[] ScreenWords = { "mfd", "screen", "display", "monitor", "lcd", "ddi", "crt", "hud" };

        private readonly List<Renderer> panels = new List<Renderer>(MaxPanels);
        private readonly List<Color> baseColors = new List<Color>(MaxPanels);
        private readonly List<Material> materials = new List<Material>(8);
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private Aircraft bound;
        private float applied = -1f;

        /// <summary>Set once from the manager so binds are diagnosed in the log.</summary>
        internal ManualLogSource Logger { get; set; }

        /// <summary>Screens currently brightened (0 when nothing matched or the view left the cockpit).</summary>
        public int PanelCount => panels.Count;

        /// <summary>Brightness multiplier currently applied (1 when off).</summary>
        public float Boost { get; private set; } = 1f;

        public void Tick(Aircraft aircraft, bool cockpitView, float ambient01, bool enabled)
        {
            if (aircraft != bound) Bind(aircraft);
            float target = enabled && cockpitView && panels.Count > 0 ? ImmersionMath.MfdBoost(ambient01) : 1f;
            Boost = target;
            if (Mathf.Abs(target - applied) < 0.005f) return;
            applied = target;
            for (int i = 0; i < panels.Count; i++)
            {
                Renderer r = panels[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(ColorId, baseColors[i] * target);
                r.SetPropertyBlock(block);
            }
        }

        public void Release()
        {
            if (bound != null)
            {
                block.Clear();
                for (int i = 0; i < panels.Count; i++)
                    if (panels[i] != null) panels[i].SetPropertyBlock(block);
            }
            bound = null;
            panels.Clear();
            baseColors.Clear();
            materials.Clear();
            applied = -1f;
            Boost = 1f;
        }

        private void Bind(Aircraft aircraft)
        {
            Release();
            bound = aircraft;
            if (aircraft == null) return;
            int candidates = 0;
            Renderer[] cockpit = GameAccess.GetCockpitRenderers(aircraft);
            if (cockpit != null)
            {
                for (int i = 0; i < cockpit.Length && panels.Count < MaxPanels; i++)
                {
                    if (cockpit[i] == null) continue;
                    candidates++;
                    Consider(cockpit[i]);
                }
            }
            else if (aircraft.cockpit != null)
            {
                // Seam unavailable (renamed field, old game build): bounded hierarchy fallback.
                var queue = new Queue<Transform>(64);
                queue.Enqueue(aircraft.cockpit.transform);
                int visited = 0;
                while (queue.Count > 0 && panels.Count < MaxPanels && visited < MaxVisited)
                {
                    Transform t = queue.Dequeue();
                    visited++;
                    for (int c = 0; c < t.childCount; c++) queue.Enqueue(t.GetChild(c));
                    var renderer = t.GetComponent<Renderer>();
                    if (renderer == null) continue;
                    candidates++;
                    Consider(renderer);
                }
            }
            if (Logger != null)
            {
                if (panels.Count > 0)
                    Logger.LogInfo("[Immersion] MFD glow bound " + panels.Count + " of " +
                        candidates + " cockpit renderers on " + aircraft.name + ".");
                else
                    Logger.LogWarning("[Immersion] MFD glow found no screens among " +
                        candidates + " cockpit renderers on " + aircraft.name + ".");
            }
        }

        private void Consider(Renderer renderer)
        {
            Material shared = renderer.sharedMaterial;
            if (shared == null || !shared.HasProperty(ColorId)) return;
            renderer.GetSharedMaterials(materials);
            bool screen = false;
            for (int i = 0; i < materials.Count; i++)
            {
                Material m = materials[i];
                if (m != null && (IsEmissive(m) || m.mainTexture is RenderTexture)) { screen = true; break; }
            }
            materials.Clear();
            if (!screen)
            {
                string name = renderer.name.ToLowerInvariant();
                for (int i = 0; i < ScreenWords.Length; i++)
                    if (name.Contains(ScreenWords[i])) { screen = true; break; }
            }
            if (!screen) return;
            panels.Add(renderer);
            baseColors.Add(shared.GetColor(ColorId));
        }

        private static bool IsEmissive(Material m)
        {
            if (m.IsKeywordEnabled("_EMISSION")) return true;
            if (m.HasProperty(EmissionColorId) && MaxComponent(m.GetColor(EmissionColorId)) > 0.01f) return true;
            if (m.HasProperty(EmissiveColorId) && MaxComponent(m.GetColor(EmissiveColorId)) > 0.01f) return true;
            if (m.HasProperty(EmissionMapId) && m.GetTexture(EmissionMapId) != null) return true;
            if (m.HasProperty(EmissiveMapId) && m.GetTexture(EmissiveMapId) != null) return true;
            return false;
        }

        private static float MaxComponent(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));
    }
}
