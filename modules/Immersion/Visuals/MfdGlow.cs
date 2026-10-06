using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    internal sealed class MfdGlow
    {
        private const int MaxPanels = 8;
        private readonly List<MaterialSlotClone> panels = new List<MaterialSlotClone>(MaxPanels);
        private readonly List<Material> materials = new List<Material>(8);
        private Aircraft bound;
        private int bindAttempts;
        private float nextBind;
        public int PanelCount => panels.Count;
        public float Boost { get; private set; } = 1f;

        public void Tick(Aircraft aircraft, bool cockpitView, float ambient01, bool enabled)
        {
            if (!enabled || !cockpitView || aircraft == null) { Release(); return; }
            if (aircraft != bound) Bind(aircraft);
            else if (bindAttempts > 0 && Time.unscaledTime >= nextBind) BindSlots(aircraft);
            Boost = panels.Count > 0 ? 1f + 0.15f * (1f - Mathf.Clamp01(ambient01)) : 1f;
            for (int i = panels.Count - 1; i >= 0; i--)
            {
                panels[i].Apply(Boost);
                if (!panels[i].Active) panels.RemoveAt(i);
            }
            if (panels.Count == 0) Boost = 1f;
        }

        public void Release()
        {
            for (int i = 0; i < panels.Count; i++) panels[i].Restore();
            panels.Clear();
            materials.Clear();
            bound = null;
            bindAttempts = 0;
            nextBind = 0f;
            Boost = 1f;
        }

        private void Bind(Aircraft aircraft)
        {
            Release();
            bound = aircraft;
            bindAttempts = 3;
            BindSlots(aircraft);
        }

        private void BindSlots(Aircraft aircraft)
        {
            bindAttempts--;
            nextBind = Time.unscaledTime + 1f;
            Renderer[] cockpit = GameAccess.GetCockpitRenderers(aircraft);
            if (cockpit == null) return; // Unsupported aircraft skip safely; no whole-assembly name guesses.
            for (int i = 0; i < cockpit.Length && panels.Count < MaxPanels; i++)
            {
                Renderer renderer = cockpit[i];
                if (!NativeMaterialGuard.CanBindRenderer(aircraft, renderer)) continue;
                renderer.GetSharedMaterials(materials);
                for (int slot = 0; slot < materials.Count && panels.Count < MaxPanels; slot++)
                {
                    Material material = materials[slot];
                    bool boundSlot = false;
                    for (int p = 0; p < panels.Count; p++)
                        if (panels[p].Matches(renderer, slot)) { boundSlot = true; break; }
                    if (boundSlot) continue;
                    MaterialSlotClone panel = MaterialSlotClone.TryBindDisplay(renderer, material, slot);
                    if (panel != null) panels.Add(panel);
                }
            }
            materials.Clear();
            if (Plugin.Logger != null && panels.Count > 0 && bindAttempts == 2)
                Plugin.Logger.LogInfo("[Immersion] Night glow bound " + panels.Count + " verified display slots.");
        }
    }
}
