using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    // Cosmetic damage cues only. Weather is the sole owner of water and cold moisture.
    internal sealed class SurfaceImmersion
    {
        private const int MaxSurfaces = 16;
        private readonly struct SurfaceSlot
        {
            internal readonly Renderer Renderer;
            internal readonly Material Native;
            internal readonly int Slot;
            internal SurfaceSlot(Renderer renderer, Material native, int slot)
            { Renderer = renderer; Native = native; Slot = slot; }
        }
        private readonly List<SurfaceSlot> candidates = new List<SurfaceSlot>(MaxSurfaces);
        private readonly List<MaterialSlotClone> surfaces = new List<MaterialSlotClone>(MaxSurfaces);
        private readonly List<Material> materials = new List<Material>(8);
        private Aircraft bound;
        private float initialHp;
        private float damage;
        private float nextSample;
        private bool tintBound;
        internal ManualLogSource Logger { get; set; }
        public float Wetness => 0f;
        public float Frost => 0f;
        public float Scorch => damage;
        public float Dirt => 0f;
        internal int CandidateCount => candidates.Count;
        internal int SurfaceCount => surfaces.Count;

        public void Reset()
        {
            RestoreSurfaces();
            candidates.Clear();
            materials.Clear();
            bound = null;
            damage = initialHp = 0f;
            nextSample = 0f;
        }

        public void Tick(Aircraft aircraft, bool cockpitView, LevelInfo level, bool enabled, float dt)
        {
            if (!enabled || !cockpitView || aircraft == null) { Reset(); return; }
            if (aircraft != bound) Bind(aircraft);
            if (Time.unscaledTime >= nextSample)
            {
                nextSample = Time.unscaledTime + 0.5f;
                float hp = 0f;
                List<UnitPart> parts = aircraft.partLookup;
                if (parts != null) for (int i = 0; i < parts.Count; i++) if (parts[i] != null) hp += Mathf.Max(0f, parts[i].hitPoints);
                if (initialHp <= 0f) initialHp = hp;
                damage = initialHp > 0f ? Mathf.Clamp01(1f - hp / initialHp) : 0f;
            }
            if (damage <= 0.001f)
            {
                RestoreSurfaces();
                return;
            }
            if (!tintBound)
            {
                tintBound = true;
                for (int i = 0; i < candidates.Count; i++)
                {
                    SurfaceSlot slot = candidates[i];
                    if (!NativeMaterialGuard.CanBindRenderer(aircraft, slot.Renderer)) continue;
                    MaterialSlotClone surface = MaterialSlotClone.TryBindOpaque(slot.Renderer, slot.Native, slot.Slot);
                    if (surface != null) surfaces.Add(surface);
                }
            }
            for (int i = surfaces.Count - 1; i >= 0; i--)
            {
                surfaces[i].Apply(1f - damage * 0.12f);
                if (!surfaces[i].Active) surfaces.RemoveAt(i);
            }
        }

        private void RestoreSurfaces()
        {
            for (int i = 0; i < surfaces.Count; i++) surfaces[i].Restore();
            surfaces.Clear();
            tintBound = false;
        }

        private void Bind(Aircraft aircraft)
        {
            Reset();
            bound = aircraft;
            Renderer[] cockpit = GameAccess.GetCockpitRenderers(aircraft);
            if (cockpit == null) return;
            for (int i = 0; i < cockpit.Length && candidates.Count < MaxSurfaces; i++)
            {
                Renderer renderer = cockpit[i];
                if (!NativeMaterialGuard.CanBindRenderer(aircraft, renderer)) continue;
                renderer.GetSharedMaterials(materials);
                for (int slot = 0; slot < materials.Count && candidates.Count < MaxSurfaces; slot++)
                {
                    Material material = materials[slot];
                    if (MaterialSlotClone.CanBindOpaque(material)) candidates.Add(new SurfaceSlot(renderer, material, slot));
                }
            }
            materials.Clear();
            if (Logger != null && candidates.Count > 0)
                Logger.LogInfo("[Immersion] Damage cues found " + candidates.Count + " verified opaque slots.");
        }
    }
}
