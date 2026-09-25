using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Visuals.Configuration;
using BoscaliSummer.Features.Visuals.Domain;
using BoscaliSummer.Framework.Lifecycle;
using HarmonyLib;
using NuclearOption.Effects;
using UnityEngine;

namespace BoscaliSummer.Features.Visuals.Runtime
{
    /// <summary>
    /// Drives vegetation from the map's synced wind. Trees: each <see cref="TreeRenderer"/> gets a
    /// swaying copy of its shared clump mesh (<see cref="TreeSwayMesh"/>). Grass: the vanilla
    /// instanced grass shader already has wind; its strength and speed are scaled with the wind.
    /// Client-only presentation — nothing here is networked.
    /// </summary>
    internal sealed class FoliageWindService : MonoBehaviour, ISceneService
    {
        private const float ScanInterval = 1f;
        private const int MaxTreeRenderers = 16;

        private static readonly int WindStrengthId = Shader.PropertyToID("_WindStrength");
        private static readonly int WindSpeedId = Shader.PropertyToID("_WindSpeed");

        private static readonly AccessTools.FieldRef<DetailRenderer, TreeRenderer[]> TreeRenderersRef =
            AccessTools.FieldRefAccess<DetailRenderer, TreeRenderer[]>("treeRenderers");
        private static readonly AccessTools.FieldRef<DetailRenderer, GrassRenderer[]> GrassRenderersRef =
            AccessTools.FieldRefAccess<DetailRenderer, GrassRenderer[]>("grassRenderers");
        private static readonly AccessTools.FieldRef<GrassRenderer, Material> GrassMaterialRef =
            AccessTools.FieldRefAccess<GrassRenderer, Material>("grassMaterial");

        public static FoliageWindService Live { get; private set; }

        private readonly List<TreeSwayMesh> trees = new List<TreeSwayMesh>(4);
        private readonly HashSet<TreeRenderer> refused = new HashSet<TreeRenderer>();
        private VisualsSettings settings;
        private ManualLogSource logger;
        private float nextScan;
        private bool grassTouched;

        public void Configure(VisualsSettings visualsSettings, ManualLogSource logSource)
        {
            settings = visualsSettings;
            logger = logSource;
            Live = this;
        }

        private bool Wanted => settings != null && settings.Enabled.Value && settings.FoliageDynamicsEnabled.Value;

        public void ResetForScene()
        {
            ReleaseTrees();
            refused.Clear();
            grassTouched = false;
            nextScan = 0f;
        }

        private void OnDestroy()
        {
            ReleaseTrees();
            RestoreGrass();
            if (Live == this) Live = null;
        }

        private void Update()
        {
            if (!Wanted)
            {
                if (trees.Count > 0) ReleaseTrees();
                if (grassTouched) RestoreGrass();
                return;
            }

            DetailRenderer detail = SceneSingleton<DetailRenderer>.i;
            if (detail == null) return;

            Vector3 wind = CurrentWind();
            float speed = new Vector2(wind.x, wind.z).magnitude;
            float windX = 0.7071f, windZ = 0.7071f;
            if (speed > 0.05f)
            {
                windX = wind.x / speed;
                windZ = wind.z / speed;
            }

            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + ScanInterval;
                AdoptTrees(detail);
                ApplyGrass(detail, speed);
            }

            float amplitude = VisualsMath.TreeSwayAmplitude(speed) * settings.FoliageSwayStrength.Value;
            float time = Time.time;
            for (int i = trees.Count - 1; i >= 0; i--)
            {
                TreeSwayMesh tree = trees[i];
                if (tree.Renderer == null)
                {
                    tree.Dispose();
                    trees.RemoveAt(i);
                    continue;
                }
                tree.Update(time, windX, windZ, amplitude);
            }
        }

        private void AdoptTrees(DetailRenderer detail)
        {
            TreeRenderer[] renderers = TreeRenderersRef(detail);
            if (renderers == null) return;
            foreach (TreeRenderer renderer in renderers)
            {
                if (renderer == null || refused.Contains(renderer) || Owns(renderer)) continue;
                if (trees.Count >= MaxTreeRenderers) return;

                TreeSwayMesh sway = TreeSwayMesh.TryCreate(renderer, out string failure);
                if (sway == null)
                {
                    // One attempt per renderer per scene: a mesh that cannot be read back will
                    // not start working on the next scan.
                    refused.Add(renderer);
                    logger?.LogWarning($"[Visuals] Tree sway unavailable for '{renderer.name}': {failure}.");
                    continue;
                }
                trees.Add(sway);
                logger?.LogInfo($"[Visuals] Tree sway active on '{renderer.name}' ({sway.VertexCount} vertices).");
            }
        }

        private bool Owns(TreeRenderer renderer)
        {
            foreach (TreeSwayMesh tree in trees)
                if (tree.Renderer == renderer) return true;
            return false;
        }

        private void ApplyGrass(DetailRenderer detail, float windSpeed)
        {
            GrassRenderer[] grass = GrassRenderersRef(detail);
            if (grass == null) return;
            float dial = settings.FoliageSwayStrength.Value;
            foreach (GrassRenderer renderer in grass)
            {
                if (renderer == null || renderer.grassMaterialProps == null) continue;
                Material material = GrassMaterialRef(renderer);
                if (material == null || !material.HasProperty(WindStrengthId)) continue;
                // The props block is rebuilt whenever the game re-creates the grass, so this
                // re-applies on every scan instead of once.
                renderer.grassMaterialProps.SetFloat(WindStrengthId,
                    VisualsMath.GrassWindStrength(material.GetFloat(WindStrengthId), windSpeed, dial));
                renderer.grassMaterialProps.SetFloat(WindSpeedId,
                    VisualsMath.GrassWindSpeed(material.GetFloat(WindSpeedId), windSpeed));
                grassTouched = true;
            }
        }

        private void RestoreGrass()
        {
            grassTouched = false;
            DetailRenderer detail = SceneSingleton<DetailRenderer>.i;
            if (detail == null) return;
            GrassRenderer[] grass = GrassRenderersRef(detail);
            if (grass == null) return;
            foreach (GrassRenderer renderer in grass)
            {
                if (renderer == null || renderer.grassMaterialProps == null) continue;
                Material material = GrassMaterialRef(renderer);
                if (material == null || !material.HasProperty(WindStrengthId)) continue;
                renderer.grassMaterialProps.SetFloat(WindStrengthId, material.GetFloat(WindStrengthId));
                renderer.grassMaterialProps.SetFloat(WindSpeedId, material.GetFloat(WindSpeedId));
            }
        }

        private void ReleaseTrees()
        {
            foreach (TreeSwayMesh tree in trees) tree.Dispose();
            trees.Clear();
        }

        private static Vector3 CurrentWind()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            return level != null ? level.windVelocity : Vector3.zero;
        }

        /// <summary>State for the automation readout.</summary>
        public void Describe(IDictionary<string, object> state)
        {
            int active = 0, frames = 0;
            foreach (TreeSwayMesh tree in trees)
            {
                if (tree.Active) active++;
                frames += tree.Frames;
            }
            state["treeRenderersSwaying"] = active;
            state["treeRenderersRefused"] = refused.Count;
            state["treeSwayFrames"] = frames;
            state["grassWindApplied"] = grassTouched;
            Vector3 wind = CurrentWind();
            state["windSpeed"] = new Vector2(wind.x, wind.z).magnitude;
        }
    }
}
