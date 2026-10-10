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

        /// <summary>Hold an extra cockpit look for captures (args: pitch down-positive, yaw, seconds; degrees).</summary>
        public static Dictionary<string, object> Look(Dictionary<string, object> args)
        {
            ImmersionManager manager = ImmersionManager.Live;
            if (manager == null) return Failure("ImmersionAutomation", "Look", "no immersion manager in this scene");
            float pitch, yaw, seconds;
            try
            {
                pitch = System.Convert.ToSingle(Arg(args, "pitch") ?? 0f, System.Globalization.CultureInfo.InvariantCulture);
                yaw = System.Convert.ToSingle(Arg(args, "yaw") ?? 0f, System.Globalization.CultureInfo.InvariantCulture);
                seconds = System.Convert.ToSingle(Arg(args, "seconds") ?? 5f, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (System.Exception) { return Failure("ImmersionAutomation", "Look", "pitch, yaw and seconds must be numbers"); }
            manager.DebugLook(Mathf.Clamp(pitch, -80f, 80f), Mathf.Clamp(yaw, -160f, 160f), Mathf.Clamp(seconds, 0f, 60f));
            return Readout(manager);
        }

        /// <summary>Per-feature A/B switches (args: body, reflection, visor, breathing - each optional bool).</summary>
        public static Dictionary<string, object> Configure(Dictionary<string, object> args)
        {
            ImmersionManager manager = ImmersionManager.Live;
            if (manager == null || manager.Settings == null) return Failure("ImmersionAutomation", "Configure", "no immersion manager in this scene");
            if (Arg(args, "body") is bool body) manager.Settings.PilotBodyEnabled.Value = body;
            if (Arg(args, "reflection") is bool reflection) manager.Settings.PilotReflectionEnabled.Value = reflection;
            if (Arg(args, "visor") is bool visor) manager.Settings.HelmetVisorEnabled.Value = visor;
            if (Arg(args, "breathing") is bool breathing) manager.Settings.PilotBreathingEnabled.Value = breathing;
            if (Arg(args, "visorIgnoreHud") is bool ignoreHud) manager.DebugVisorIgnoresHud(ignoreHud);
            if (Arg(args, "kneeboardRefresh") is bool refresh && refresh) CockpitPilot.Live?.DebugRefreshKneeboard();
            object card = Arg(args, "kneeboardCard");
            if (card != null)
            {
                try { manager.Settings.KneeboardCard.Value = System.Math.Max(0, System.Convert.ToInt32(card, System.Globalization.CultureInfo.InvariantCulture)); }
                catch (System.Exception) { return Failure("ImmersionAutomation", "Configure", "kneeboardCard must be a number"); }
                CockpitPilot.Live?.DebugRefreshKneeboard();
            }
            manager.LogAutomation("Configure: body=" + manager.Settings.PilotBodyEnabled.Value + " reflection=" + manager.Settings.PilotReflectionEnabled.Value +
                " visor=" + manager.Settings.HelmetVisorEnabled.Value + " breathing=" + manager.Settings.PilotBreathingEnabled.Value);
            return Readout(manager);
        }

        /// <summary>Drive pilot cues from a fixed G (args: g -5..12, seconds 0..30) through the vanilla-equation fallback.</summary>
        public static Dictionary<string, object> InjectG(Dictionary<string, object> args)
        {
            ImmersionManager manager = ImmersionManager.Live;
            if (manager == null) return Failure("ImmersionAutomation", "InjectG", "no immersion manager in this scene");
            float g, seconds;
            try
            {
                g = System.Convert.ToSingle(Arg(args, "g") ?? 1f, System.Globalization.CultureInfo.InvariantCulture);
                seconds = System.Convert.ToSingle(Arg(args, "seconds") ?? 5f, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (System.Exception) { return Failure("ImmersionAutomation", "InjectG", "g and seconds must be numbers"); }
            if (float.IsNaN(g) || g < -5f || g > 12f || float.IsNaN(seconds) || seconds < 0f || seconds > 30f)
                return Failure("ImmersionAutomation", "InjectG", "g must be -5..12 and seconds 0..30");
            manager.InjectG(g, seconds);
            manager.LogAutomation("InjectG: " + g + " G for " + seconds + " s");
            return Readout(manager);
        }

        /// <summary>
        /// Deal real blast damage to the followed aircraft (args: blast per part, parts counted from the end of its part
        /// list) so the hit hook, the warhead test and the crippled check run end to end. Host only; it does break the aircraft.
        /// </summary>
        public static Dictionary<string, object> InjectHit(Dictionary<string, object> args)
        {
            ImmersionManager manager = ImmersionManager.Live;
            var cameras = SceneSingleton<CameraStateManager>.i;
            Aircraft aircraft = cameras != null ? cameras.followingUnit as Aircraft : null;
            if (manager == null || aircraft == null || aircraft.partLookup == null)
                return Failure("ImmersionAutomation", "InjectHit", "no followed aircraft in this scene");
            float blast;
            int count;
            try
            {
                blast = System.Convert.ToSingle(Arg(args, "blast") ?? 50f, System.Globalization.CultureInfo.InvariantCulture);
                count = System.Convert.ToInt32(Arg(args, "parts") ?? 1, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (System.Exception) { return Failure("ImmersionAutomation", "InjectHit", "blast and parts must be numbers"); }
            if (float.IsNaN(blast) || blast < 0f || blast > 5000f || count < 1 || count > 64)
                return Failure("ImmersionAutomation", "InjectHit", "blast must be 0..5000 and parts 1..64");
            int struck = 0;
            for (int i = aircraft.partLookup.Count - 1; i >= 0 && struck < count; i--)
            {
                UnitPart part = aircraft.partLookup[i];
                if (part == null || part == aircraft.cockpit) continue;
                part.TakeDamage(0f, blast, 1f, 0f, 0f, PersistentID.None);
                struck++;
            }
            manager.LogAutomation("InjectHit: blast " + blast + " on " + struck + " parts");
            Dictionary<string, object> state = Readout(manager);
            state["partsStruck"] = struck;
            return state;
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
                        entry["mainTexture"] = TextureKind(material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null);
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
