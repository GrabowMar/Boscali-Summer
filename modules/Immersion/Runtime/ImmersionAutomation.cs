using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Game;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Immersion.Runtime
{
    /// <summary>Explicit client-local hooks for nomodkit cockpit acceptance scenarios.</summary>
    public static class ImmersionAutomation
    {
        public static Dictionary<string, object> Readout(Dictionary<string, object> args)
        {
            ImmersionManager manager = ImmersionManager.Live;
            if (manager == null) return Failure("ImmersionAutomation", "Readout", "no immersion manager in this scene");
            Dictionary<string, object> state = Readout(manager);
            if (Arg(args, "materials") is bool materials && materials) DescribeMaterials(state);
            manager.LogAutomation("Readout: " + Describe(state));
            return state;
        }

        public static Dictionary<string, object> SetEnabled(Dictionary<string, object> args)
        {
            ImmersionManager manager = ImmersionManager.Live;
            if (manager == null) return Failure("ImmersionAutomation", "SetEnabled", "no immersion manager in this scene");
            if (!(Arg(args, "enabled") is bool enabled)) return Failure("ImmersionAutomation", "SetEnabled", "enabled must be a boolean");
            manager.IsEnabled = enabled;
            manager.LogAutomation("SetEnabled: " + manager.IsEnabled);
            return Readout(manager);
        }

        private static Dictionary<string, object> Readout(ImmersionManager manager)
        {
            var state = new Dictionary<string, object>();
            manager.Describe(state);
            FxBus.Describe(state);
            state["ok"] = true;
            return state;
        }

        // Expensive discovery is explicit automation work; no update/render callback calls this.
        private static void DescribeMaterials(Dictionary<string, object> state)
        {
            const int maxSlots = 32;
            var entries = new List<Dictionary<string, object>>(maxSlots);
            state["cockpitMaterials"] = entries;
            var cameras = SceneSingleton<CameraStateManager>.i;
            Aircraft aircraft = cameras != null ? cameras.followingUnit as Aircraft : null;
            Renderer[] renderers = GameAccess.GetCockpitRenderers(aircraft);
            state["materialRendererCount"] = renderers != null ? renderers.Length : 0;
            if (renderers == null) return;
            var shared = new List<Material>(8);
            var slotBlock = new MaterialPropertyBlock();
            var rendererBlock = new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Length && entries.Count < maxSlots; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                renderer.GetSharedMaterials(shared);
                renderer.GetPropertyBlock(rendererBlock);
                for (int slot = 0; slot < shared.Count && entries.Count < maxSlots; slot++)
                {
                    Material material = shared[slot];
                    renderer.GetPropertyBlock(slotBlock, slot);
                    var entry = new Dictionary<string, object>
                    {
                        ["renderer"] = renderer.name,
                        ["slot"] = slot,
                        ["visible"] = renderer.enabled && renderer.gameObject.activeInHierarchy,
                        ["material"] = material != null ? material.name : "null",
                        ["shader"] = material != null && material.shader != null ? material.shader.name : "null",
                        ["slotBlock"] = !slotBlock.isEmpty,
                        ["rendererBlock"] = !rendererBlock.isEmpty,
                    };
                    if (material != null)
                    {
                        entry["renderQueue"] = material.renderQueue;
                        entry["renderType"] = material.GetTag("RenderType", false);
                        entry["emissionKeyword"] = material.IsKeywordEnabled("_EMISSION");
                        entry["mainTexture"] = TextureKind(material.mainTexture);
                        DescribeTexture(entry, material, "_BaseMap");
                        DescribeTexture(entry, material, "_MainTex");
                        DescribeTexture(entry, material, "_EmissionMap");
                        DescribeColour(entry, material, slotBlock, rendererBlock, "_Color");
                        DescribeColour(entry, material, slotBlock, rendererBlock, "_BaseColor");
                        DescribeColour(entry, material, slotBlock, rendererBlock, "_EmissionColor");
                    }
                    entries.Add(entry);
                }
                shared.Clear();
            }
            state["materialSlotsTruncated"] = entries.Count == maxSlots;
        }

        private static void DescribeTexture(Dictionary<string, object> entry, Material material, string property)
        {
            entry[property] = material.HasProperty(property) ? TextureKind(material.GetTexture(property)) : "unsupported";
        }

        private static void DescribeColour(Dictionary<string, object> entry, Material material,
            MaterialPropertyBlock slot, MaterialPropertyBlock renderer, string property)
        {
            bool supported = material.HasProperty(property);
            entry[property + ".supported"] = supported;
            int id = Shader.PropertyToID(property);
            entry[property + ".slot"] = slot.HasProperty(id);
            entry[property + ".renderer"] = renderer.HasProperty(id);
            if (!supported) return;
            Color colour = material.GetColor(property);
            entry[property] = new[] { colour.r, colour.g, colour.b, colour.a };
        }

        private static string TextureKind(Texture texture) => texture == null ? "null" :
            texture is RenderTexture ? "RenderTexture" : texture is Texture2D ? "Texture2D" :
            texture is Texture3D ? "Texture3D" : texture is Cubemap ? "Cubemap" : "Texture";
    }
}
