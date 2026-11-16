using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Immersion.Domain;
using HarmonyLib;
using NuclearOption.Effects;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>Restyles the native forest/shrub paths, without another renderer or scatter grid.</summary>
    internal sealed class VegetationDressing
    {
        private static readonly AccessTools.FieldRef<TreeRenderer, Material> TreeMaterial = Field<TreeRenderer, Material>("material");
        private static readonly AccessTools.FieldRef<TreeRenderer, RenderParams> TreeParams = Field<TreeRenderer, RenderParams>("renderParams");
        private static readonly AccessTools.FieldRef<TreeRenderer, Mesh> TreeMesh = Field<TreeRenderer, Mesh>("mesh");
        private static readonly AccessTools.FieldRef<GrassRenderer, Material> GrassMaterial = Field<GrassRenderer, Material>("grassMaterial");
        private static readonly AccessTools.FieldRef<GrassRenderer, Mesh> GrassMesh = Field<GrassRenderer, Mesh>("grassMesh");
        private static readonly AccessTools.FieldRef<GrassRenderer, Texture2D[]> GrassTextures = Field<GrassRenderer, Texture2D[]>("mainTextures");
        private static readonly AccessTools.FieldRef<GrassRenderer, GraphicsBuffer> GrassConfigBuffer = Field<GrassRenderer, GraphicsBuffer>("configBuffer");
        private static readonly int LushColor = Shader.PropertyToID("_ColorLush"), AltColor = Shader.PropertyToID("_ColorAltitude"),
            Hue = Shader.PropertyToID("_HueVariation"), Value = Shader.PropertyToID("_ValueVariation");

        private sealed class TreeStyle
        {
            internal TreeRenderer Renderer;
            internal Material Original, Owned;
            internal Mesh OriginalMesh, OwnedMesh;
            internal float Alt, Lush, Large, Small;
            internal float AppliedAlt, AppliedLush, AppliedLarge, AppliedSmall;
        }

        private sealed class GrassStyle
        {
            internal GrassRenderer Renderer;
            internal GrassRenderer.GrassConfig[] Original, Owned;
        }

        private readonly List<TreeStyle> trees = new List<TreeStyle>(2);
        private readonly List<GrassStyle> grass = new List<GrassStyle>(2);
        private Transform map;
        private bool forestEnabled, brushEnabled;

        internal int StyledForests => trees.Count;
        internal int StyledGrass => grass.Count;

        internal void Tick(Camera camera, bool forest, bool underbrush)
        {
            if (!forest && !underbrush && trees.Count == 0 && grass.Count == 0) return;
            Transform current = LevelInfo.i?.LoadedMapSettings?.transform;
            if (Application.isBatchMode || GameManager.IsHeadless || camera == null || current == null ||
                GameManager.gameState == GameState.Encyclopedia)
            {
                Reset();
                return;
            }
            if (map != current) { Reset(); map = current; }
            if (forest != forestEnabled)
            {
                if (forest) StyleForests(); else RestoreForests();
                forestEnabled = forest;
            }
            if (underbrush != brushEnabled)
            {
                if (underbrush) StyleGrass(); else RestoreGrass();
                brushEnabled = underbrush;
            }
        }

        private void StyleForests()
        {
            if (TreeMaterial == null || TreeParams == null || TreeMesh == null) return;
            foreach (TreeRenderer renderer in Resources.FindObjectsOfTypeAll<TreeRenderer>())
            {
                if (!InMap(renderer)) continue;
                Material original = TreeMaterial(renderer);
                if (original == null || original.shader == null || original.shader.name != "Shader Graphs/TreeShader_Instanced" ||
                    !original.HasProperty(LushColor) || !original.HasProperty(AltColor) ||
                    !original.HasProperty(Hue) || !original.HasProperty(Value)) continue;
                RenderParams draw = TreeParams(renderer);
                if (draw.material != null && draw.material != original) continue;
                Mesh originalMesh = TreeMesh(renderer);
                Mesh ownedMesh = ForestClumpArt.Create(originalMesh);
                if (ownedMesh == null) continue;
                var style = new TreeStyle
                {
                    Renderer = renderer, Original = original,
                    OriginalMesh = originalMesh, OwnedMesh = ownedMesh,
                    Alt = renderer.AltIntensity, Lush = renderer.LushIntensity,
                    Large = renderer.LargeNoiseScale, Small = renderer.NoiseSmallScale
                };
                // Authored mixed crowns use the same native atlas, shader, indirect arguments,
                // bounds and shadows. The native position/clear buffers remain untouched.
                style.Owned = new Material(original) { name = "Boscali Forest Palette", hideFlags = HideFlags.HideAndDontSave };
                style.Owned.SetColor(LushColor, Color.Lerp(original.GetColor(LushColor), new Color(.18f, .56f, .12f, 1f), .45f));
                style.Owned.SetColor(AltColor, Color.Lerp(original.GetColor(AltColor), new Color(.22f, .44f, .16f, 1f), .35f));
                style.Owned.SetVector(Hue, Vector4.Lerp(original.GetVector(Hue), new Vector4(-.055f, .04f, 0f, 0f), .7f));
                style.Owned.SetVector(Value, Vector4.Lerp(original.GetVector(Value), new Vector4(.72f, 1.15f, 0f, 0f), .7f));
                style.AppliedAlt = renderer.AltIntensity = style.Alt * 1.15f;
                style.AppliedLush = renderer.LushIntensity = style.Lush * .9f;
                style.AppliedLarge = renderer.LargeNoiseScale = style.Large * .65f;
                style.AppliedSmall = renderer.NoiseSmallScale = style.Small * .25f;
                TreeMaterial(renderer) = style.Owned;
                TreeMesh(renderer) = style.OwnedMesh;
                if (draw.material == original) { draw.material = style.Owned; TreeParams(renderer) = draw; }
                trees.Add(style);
            }
        }

        private void StyleGrass()
        {
            if (GrassMaterial == null || GrassMesh == null || GrassTextures == null || GrassConfigBuffer == null) return;
            foreach (GrassRenderer renderer in Resources.FindObjectsOfTypeAll<GrassRenderer>())
            {
                if (!InMap(renderer)) continue;
                Material material = GrassMaterial(renderer);
                Mesh mesh = GrassMesh(renderer);
                Texture2D[] textures = GrassTextures(renderer);
                GrassRenderer.GrassConfig[] original = renderer.config;
                if (material == null || material.shader == null || material.shader.name != "Instanced/GrassInstancedShader" ||
                    mesh == null || mesh.name != "grassclump1_onesided" || original == null || original.Length != 3 ||
                    textures == null || textures.Length != 2 || textures[0] == null || textures[1] == null ||
                    textures[0].name != "grass1_b" || textures[1].name != "shrub1_b") continue;
                var dominant = original[0];
                var shrub = original[2];
                if (dominant.MainTextureIndex != 0 || shrub.MainTextureIndex != 1 ||
                    dominant.LushTextureIndex != shrub.LushTextureIndex ||
                    !VegetationRules.CanRedistribute(dominant.LushMultiplier, dominant.LushExponent, dominant.LushThreshold,
                        shrub.LushMultiplier, shrub.LushExponent, shrub.LushThreshold) ||
                    !float.IsFinite(dominant.ViewRadius) || dominant.ViewRadius <= 0f ||
                    !float.IsFinite(shrub.ViewRadius) || shrub.ViewRadius < dominant.ViewRadius ||
                    !float.IsFinite(original[1].ViewRadius) || original[1].ViewRadius < dominant.ViewRadius ||
                    !float.IsFinite(shrub.ScaleMin) || !float.IsFinite(shrub.ScaleMax) || shrub.ScaleMin <= 0f || shrub.ScaleMax < shrub.ScaleMin ||
                    !float.IsFinite(shrub.Y_Scale) || shrub.Y_Scale <= 0f) continue;
                var owned = (GrassRenderer.GrassConfig[])original.Clone();
                // Native generator accepts MAX(weight), then picks species with weighted roulette.
                // The unchanged dominant grass weight bounds the shrub for every lush value [0,1].
                // Cap every type at the unchanged dominant radius too: roulette can move an
                // individual old grass-0 choice into grass-1, so capping only shrubs is insufficient.
                // Near candidates stay identical; distant subpixel candidates can only disappear.
                shrub.LushMultiplier = VegetationRules.ShrubMultiplier;
                shrub.ViewRadius = Mathf.Min(shrub.ViewRadius, dominant.ViewRadius);
                shrub.ScaleMin = Mathf.Min(shrub.ScaleMin, VegetationRules.ShrubMinScale);
                shrub.Y_Scale *= VegetationRules.ShrubHeightFactor;
                owned[2] = shrub;
                var middle = owned[1];
                middle.ViewRadius = dominant.ViewRadius;
                owned[1] = middle;
                renderer.config = owned;
                if (!Upload(renderer, owned)) { renderer.config = original; continue; }
                grass.Add(new GrassStyle { Renderer = renderer, Original = original, Owned = owned });
            }
            if (grass.Count != 0) SceneSingleton<TerrainHeightMap>.i?.SetDirty();
        }

        private bool InMap(Component component) => component != null && component.gameObject.scene.IsValid() &&
            component.transform.IsChildOf(map);

        private static bool Upload(GrassRenderer renderer, GrassRenderer.GrassConfig[] values)
        {
            // Inactive native grass picks up the array in its own CommandSetup; never enable it here.
            if (!renderer.IsActive) return true;
            GraphicsBuffer buffer = GrassConfigBuffer(renderer);
            if (buffer == null || !buffer.IsValid() || buffer.count != values.Length || buffer.stride != GrassRenderer.GrassConfig.Size)
                return false;
            buffer.SetData(values); // Same 3-element buffer. No release/recreate or texture-array bake.
            return true;
        }

        private void RestoreForests()
        {
            foreach (TreeStyle style in trees)
            {
                TreeRenderer renderer = style.Renderer;
                if (renderer != null)
                {
                    if (TreeMaterial(renderer) == style.Owned) TreeMaterial(renderer) = style.Original;
                    if (TreeMesh(renderer) == style.OwnedMesh) TreeMesh(renderer) = style.OriginalMesh;
                    RenderParams draw = TreeParams(renderer);
                    if (draw.material == style.Owned) { draw.material = style.Original; TreeParams(renderer) = draw; }
                    // Only restore values still owned by this effect; leave another mod's edits alone.
                    if (renderer.AltIntensity == style.AppliedAlt) renderer.AltIntensity = style.Alt;
                    if (renderer.LushIntensity == style.AppliedLush) renderer.LushIntensity = style.Lush;
                    if (renderer.LargeNoiseScale == style.AppliedLarge) renderer.LargeNoiseScale = style.Large;
                    if (renderer.NoiseSmallScale == style.AppliedSmall) renderer.NoiseSmallScale = style.Small;
                }
                if (style.Owned != null) UnityEngine.Object.Destroy(style.Owned);
                if (style.OwnedMesh != null) UnityEngine.Object.Destroy(style.OwnedMesh);
            }
            trees.Clear();
        }

        private void RestoreGrass()
        {
            bool changed = false;
            foreach (GrassStyle style in grass)
            {
                GrassRenderer renderer = style.Renderer;
                if (renderer == null || !ReferenceEquals(renderer.config, style.Owned)) continue;
                renderer.config = style.Original;
                Upload(renderer, style.Original);
                changed = true;
            }
            grass.Clear();
            if (changed) SceneSingleton<TerrainHeightMap>.i?.SetDirty();
        }

        internal void Reset()
        {
            RestoreForests();
            RestoreGrass();
            map = null;
            forestEnabled = brushEnabled = false;
        }

        private static AccessTools.FieldRef<T, F> Field<T, F>(string name) where T : class
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception) { return null; } // An unknown game layout leaves native vegetation intact.
        }
    }
}
