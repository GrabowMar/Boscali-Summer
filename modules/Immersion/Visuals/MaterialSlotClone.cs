using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    // Clone just a verified display or opaque damage slot when native MPBs are empty.
    // Native materials keep updating; renderer/slot property blocks stay authoritative.
    internal sealed class MaterialSlotClone
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int HitPointsId = Shader.PropertyToID("_HitPoints");
        private static readonly int LiveryId = Shader.PropertyToID("_Livery");
        private readonly Renderer renderer;
        private readonly Material native;
        private readonly int slot;
        private readonly bool display;
        private readonly List<Material> shared = new List<Material>(8);
        private Material clone;

        internal bool Active => renderer != null && clone != null;

        internal static MaterialSlotClone TryBindDisplay(Renderer target, Material source, int materialSlot) =>
            TryBind(target, source, materialSlot, true);

        internal static bool CanBindOpaque(Material source) => source != null && OpaqueColourProperty(source) >= 0;

        internal static MaterialSlotClone TryBindOpaque(Renderer target, Material source, int materialSlot) =>
            TryBind(target, source, materialSlot, false);

        private static MaterialSlotClone TryBind(Renderer target, Material source, int materialSlot, bool display)
        {
            if (target == null || source == null ||
                (display ? DisplayColourProperty(source) : OpaqueColourProperty(source)) < 0) return null;
            var owner = new MaterialSlotClone(target, source, materialSlot, display);
            target.GetSharedMaterials(owner.shared);
            if (materialSlot < 0 || materialSlot >= owner.shared.Count || owner.shared[materialSlot] != source) return null;
            owner.clone = new Material(source)
                { name = display ? "Boscali display glow" : "Boscali damage tint", hideFlags = HideFlags.DontSave };
            owner.shared[materialSlot] = owner.clone;
            target.SetSharedMaterials(owner.shared);
            return owner;
        }

        private MaterialSlotClone(Renderer target, Material source, int materialSlot, bool isDisplay)
        {
            renderer = target;
            native = source;
            slot = materialSlot;
            display = isDisplay;
        }

        internal bool Matches(Renderer target, int materialSlot) => renderer == target && slot == materialSlot;

        internal void Apply(float gain)
        {
            if (clone == null) return;
            if (renderer == null || native == null) { Restore(); return; }
            renderer.GetSharedMaterials(shared);
            if (slot >= shared.Count || shared[slot] != clone) { DestroyClone(); return; }
            bool shaderChanged = clone.shader != native.shader;
            int colourId = display ? DisplayColourProperty(native) : OpaqueColourProperty(native, shaderChanged);
            if (colourId < 0) { Restore(); return; }
            // Copy current native brightness, textures, keywords and render settings.
            // Native TacScreen adjusts emission continuously for day/night and options.
            if (shaderChanged) clone.shader = native.shader;
            clone.CopyPropertiesFromMaterial(native);
            gain = float.IsNaN(gain) || float.IsInfinity(gain) ? 1f :
                Mathf.Clamp(gain, display ? 1f : 0.88f, display ? 1.15f : 1f);
            Color colour = native.GetColor(colourId);
            clone.SetColor(colourId, new Color(colour.r * gain, colour.g * gain, colour.b * gain, colour.a));
        }

        internal void Restore()
        {
            if (clone == null) return;
            if (renderer != null)
            {
                renderer.GetSharedMaterials(shared);
                if (slot < shared.Count && shared[slot] == clone)
                {
                    shared[slot] = native;
                    renderer.SetSharedMaterials(shared);
                }
            }
            DestroyClone();
        }

        private void DestroyClone()
        {
            if (clone != null) Object.Destroy(clone);
            clone = null;
            shared.Clear();
        }

        // A render texture proves this is a display slot, including emission-only screens.
        // The selected property must already exist on its native shader.
        private static int DisplayColourProperty(Material source)
        {
            if (HasNativeMaterialWriter(source)) return -1;
            bool emissionDisplay = IsDisplayTexture(source, EmissionMapId);
            if (emissionDisplay && source.HasProperty(EmissionId)) return EmissionId;
            bool baseDisplay = IsDisplayTexture(source, BaseMapId);
            bool mainDisplay = IsDisplayTexture(source, MainTexId);
            if (baseDisplay && source.HasProperty(BaseColorId)) return BaseColorId;
            if (mainDisplay && source.HasProperty(ColorId)) return ColorId;
            if ((baseDisplay || mainDisplay) && source.HasProperty(BaseColorId)) return BaseColorId;
            if ((baseDisplay || mainDisplay) && source.HasProperty(ColorId)) return ColorId;
            return -1;
        }

        private static bool IsDisplayTexture(Material source, int property) =>
            source.HasProperty(property) && source.GetTexture(property) is RenderTexture;

        private static int OpaqueColourProperty(Material source, bool validateTag = true)
        {
            if (HasNativeMaterialWriter(source)) return -1;
            // GetTag marshals a managed string: validate it at binding/shader change only.
            if (source.renderQueue >= 2500 || (validateTag && source.GetTag("RenderType", false) != "Opaque") ||
                source.IsKeywordEnabled("_EMISSION") ||
                IsDisplayTexture(source, MainTexId) || IsDisplayTexture(source, BaseMapId) ||
                IsDisplayTexture(source, EmissionMapId)) return -1;
            if (source.HasProperty(EmissionId))
            {
                Color emission = source.GetColor(EmissionId);
                if (Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b)) > 0.01f) return -1;
            }
            int colour = source.HasProperty(BaseColorId) ? BaseColorId : source.HasProperty(ColorId) ? ColorId : -1;
            return colour >= 0 && source.GetColor(colour).a >= 0.99f ? colour : -1;
        }

        // UnitPart.ApplyDamage/SetLivery reacquire renderer.material before writing
        // these shader properties. A clone there would become the native writer's
        // target, so never replace such a slot or copy its state back to a shared asset.
        private static bool HasNativeMaterialWriter(Material source) =>
            source.HasProperty(HitPointsId) || source.HasProperty(LiveryId);
    }
}
