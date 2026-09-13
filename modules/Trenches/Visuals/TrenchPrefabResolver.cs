using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Visuals
{
    /// <summary>
    /// Manages the lifecycle, resolution, and instantiation of modular trench 3D prefabs.
    /// Prefers artist-authored prefabs from a compiled AssetBundle ('trenches.bundle').
    /// If no bundle is present, automatically generates clean procedural modular prefabs
    /// with native URP materials scavenged from game assets (pillbox, gabion bunker, emplacements).
    /// </summary>
    internal static class TrenchPrefabResolver
    {
        private static AssetBundle loadedBundle;
        private static bool attemptedBundleLoad;

        // Template prefabs (cached per scene)
        private static GameObject templateStraight;
        private static GameObject templateCorner;
        private static GameObject templateJunction;
        private static GameObject templateRifleBay;
        private static GameObject templateWeaponPit;
        private static GameObject templateBunker;
        private static GameObject templateEndCap;
        private static GameObject templateLOD1Straight;
        private static GameObject templateLOD1Node;

        private static readonly List<Mesh> generatedTemplateMeshes = new List<Mesh>();

        public static bool HasExternalBundle => loadedBundle != null;

        public static void ResetForScene()
        {
            DestroyTemplate(ref templateStraight);
            DestroyTemplate(ref templateCorner);
            DestroyTemplate(ref templateJunction);
            DestroyTemplate(ref templateRifleBay);
            DestroyTemplate(ref templateWeaponPit);
            DestroyTemplate(ref templateBunker);
            DestroyTemplate(ref templateEndCap);
            DestroyTemplate(ref templateLOD1Straight);
            DestroyTemplate(ref templateLOD1Node);

            for (int i = 0; i < generatedTemplateMeshes.Count; i++)
            {
                if (generatedTemplateMeshes[i] != null)
                {
                    UnityEngine.Object.Destroy(generatedTemplateMeshes[i]);
                }
            }
            generatedTemplateMeshes.Clear();

            if (loadedBundle != null)
            {
                try
                {
                    loadedBundle.Unload(true);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[TrenchPrefabResolver] Error unloading AssetBundle: " + ex.Message);
                }
                loadedBundle = null;
            }
            attemptedBundleLoad = false;
        }

        private static void DestroyTemplate(ref GameObject template)
        {
            if (template != null)
            {
                UnityEngine.Object.Destroy(template);
                template = null;
            }
        }

        public static void EnsureLoaded()
        {
            if (attemptedBundleLoad) return;
            attemptedBundleLoad = true;

            string[] candidatePaths = GetCandidateBundlePaths();
            foreach (string path in candidatePaths)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try
                    {
                        loadedBundle = AssetBundle.LoadFromFile(path);
                        if (loadedBundle != null)
                        {
                            Debug.Log("[TrenchPrefabResolver] Loaded modular trench AssetBundle from " + path);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[TrenchPrefabResolver] Failed loading bundle at " + path + ": " + ex.Message);
                    }
                }
            }
        }

        private static string[] GetCandidateBundlePaths()
        {
            var paths = new List<string>();

            string asmDir = null;
            try
            {
                asmDir = Path.GetDirectoryName(typeof(TrenchPrefabResolver).Assembly.Location);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    paths.Add(Path.Combine(asmDir, "Assets", "trenches.bundle"));
                    paths.Add(Path.Combine(asmDir, "trenches.bundle"));
                }
            }
            catch { }

            string dataDir = Application.dataPath;
            if (!string.IsNullOrEmpty(dataDir))
            {
                paths.Add(Path.Combine(dataDir, "..", "BepInEx", "plugins", "BoscaliSummer", "Assets", "trenches.bundle"));
                paths.Add(Path.Combine(dataDir, "..", "BepInEx", "plugins", "BoscaliSummer", "trenches.bundle"));
                paths.Add(Path.Combine(dataDir, "..", "plugins", "BoscaliSummer", "Assets", "trenches.bundle"));
            }

            return paths.ToArray();
        }

        public static GameObject InstantiateStraight(Vector3 position, Quaternion rotation, Transform parent)
        {
            EnsureLoaded();
            return InstantiateFromTemplate(GetStraightTemplate(), position, rotation, parent, "Trench_Straight");
        }

        public static GameObject InstantiateCorner(Vector3 position, Quaternion rotation, Transform parent)
        {
            EnsureLoaded();
            return InstantiateFromTemplate(GetCornerTemplate(), position, rotation, parent, "Trench_Corner");
        }

        public static GameObject InstantiateJunction(Vector3 position, Quaternion rotation, Transform parent)
        {
            EnsureLoaded();
            return InstantiateFromTemplate(GetJunctionTemplate(), position, rotation, parent, "Trench_Junction");
        }

        public static GameObject InstantiateRifleBay(Vector3 position, Quaternion rotation, Transform parent)
        {
            EnsureLoaded();
            return InstantiateFromTemplate(GetRifleBayTemplate(), position, rotation, parent, "Trench_RifleBay");
        }

        public static GameObject InstantiateWeaponPit(Vector3 position, Quaternion rotation, Transform parent)
        {
            EnsureLoaded();
            return InstantiateFromTemplate(GetWeaponPitTemplate(), position, rotation, parent, "Trench_WeaponPit");
        }

        public static GameObject InstantiateBunker(Vector3 position, Quaternion rotation, Transform parent)
        {
            EnsureLoaded();
            return InstantiateFromTemplate(GetBunkerTemplate(), position, rotation, parent, "Trench_Bunker");
        }

        public static GameObject InstantiateEndCap(Vector3 position, Quaternion rotation, Transform parent)
        {
            EnsureLoaded();
            return InstantiateFromTemplate(GetEndCapTemplate(), position, rotation, parent, "Trench_EndCap");
        }

        public static GameObject InstantiateLOD1Node(Vector3 position, Quaternion rotation, Transform parent)
        {
            EnsureLoaded();
            return InstantiateFromTemplate(GetLOD1NodeTemplate(), position, rotation, parent, "Trench_Node_LOD1");
        }

        private static GameObject InstantiateFromTemplate(GameObject template, Vector3 pos, Quaternion rot, Transform parent, string name)
        {
            if (template == null) return null;
            // Trench coordinates are Datum-origin local. The overload taking a position
            // interprets it as world-space and shifts close LOD modules away on origin moves.
            GameObject instance = UnityEngine.Object.Instantiate(template, parent, false);
            instance.transform.localPosition = pos;
            instance.transform.localRotation = rot;
            instance.name = name;
            instance.SetActive(true);
            return instance;
        }

        // ==================== Template Resolution & Fallback Generators ====================

        private static GameObject GetStraightTemplate()
        {
            if (templateStraight != null) return templateStraight;
            if (loadedBundle != null)
            {
                templateStraight = loadedBundle.LoadAsset<GameObject>("Trench_Straight");
                if (templateStraight != null) return templateStraight;
            }

            // Procedural Modular Straight: 6m long, 1.4m floor, revetments, firing step, parapet, downward skirts
            Mesh mesh = TrenchMeshBuilder.BuildModularStraightMesh(8.0f, 2.6f, 2.4f, 1.7f, 1.4f);
            generatedTemplateMeshes.Add(mesh);
            return templateStraight = CreateTemplateObject("Template_Straight", mesh, TrenchMaterialResolver.GetEarthBermMaterial());
        }

        private static GameObject GetCornerTemplate()
        {
            if (templateCorner != null) return templateCorner;
            if (loadedBundle != null)
            {
                templateCorner = loadedBundle.LoadAsset<GameObject>("Trench_Corner");
                if (templateCorner != null) return templateCorner;
            }

            Mesh mesh = TrenchMeshBuilder.BuildModularCornerMesh(2.6f, 2.4f, 1.4f);
            generatedTemplateMeshes.Add(mesh);
            return templateCorner = CreateTemplateObject("Template_Corner", mesh, TrenchMaterialResolver.GetEarthBermMaterial());
        }

        private static GameObject GetJunctionTemplate()
        {
            if (templateJunction != null) return templateJunction;
            if (loadedBundle != null)
            {
                templateJunction = loadedBundle.LoadAsset<GameObject>("Trench_Junction");
                if (templateJunction != null) return templateJunction;
            }

            Mesh mesh = TrenchMeshBuilder.BuildModularJunctionMesh(2.6f, 2.4f, 1.4f);
            generatedTemplateMeshes.Add(mesh);
            return templateJunction = CreateTemplateObject("Template_Junction", mesh, TrenchMaterialResolver.GetEarthBermMaterial());
        }

        private static GameObject GetRifleBayTemplate()
        {
            if (templateRifleBay != null) return templateRifleBay;
            if (loadedBundle != null)
            {
                templateRifleBay = loadedBundle.LoadAsset<GameObject>("Trench_RifleBay");
                if (templateRifleBay != null) return templateRifleBay;
            }

            Mesh mesh = TrenchMeshBuilder.BuildFightingBayMesh();
            generatedTemplateMeshes.Add(mesh);
            return templateRifleBay = CreateTemplateObject("Template_RifleBay", mesh, TrenchMaterialResolver.GetSandbagMaterial());
        }

        private static GameObject GetWeaponPitTemplate()
        {
            if (templateWeaponPit != null) return templateWeaponPit;
            if (loadedBundle != null)
            {
                templateWeaponPit = loadedBundle.LoadAsset<GameObject>("Trench_WeaponPit");
                if (templateWeaponPit != null) return templateWeaponPit;
            }

            Mesh mesh = TrenchMeshBuilder.BuildWeaponPitMesh();
            generatedTemplateMeshes.Add(mesh);
            return templateWeaponPit = CreateTemplateObject("Template_WeaponPit", mesh, TrenchMaterialResolver.GetConcreteMaterial());
        }

        private static GameObject GetBunkerTemplate()
        {
            if (templateBunker != null) return templateBunker;
            if (loadedBundle != null)
            {
                templateBunker = loadedBundle.LoadAsset<GameObject>("Trench_Bunker");
                if (templateBunker != null) return templateBunker;
            }

            Mesh mesh = TrenchMeshBuilder.BuildBunkerMesh();
            generatedTemplateMeshes.Add(mesh);
            return templateBunker = CreateTemplateObject("Template_Bunker", mesh, TrenchMaterialResolver.GetConcreteMaterial());
        }

        private static GameObject GetEndCapTemplate()
        {
            if (templateEndCap != null) return templateEndCap;
            if (loadedBundle != null)
            {
                templateEndCap = loadedBundle.LoadAsset<GameObject>("Trench_EndCap");
                if (templateEndCap != null) return templateEndCap;
            }

            Mesh mesh = TrenchMeshBuilder.BuildModularEndCapMesh(2.6f, 2.4f, 1.4f);
            generatedTemplateMeshes.Add(mesh);
            return templateEndCap = CreateTemplateObject("Template_EndCap", mesh, TrenchMaterialResolver.GetEarthBermMaterial());
        }

        private static GameObject GetLOD1NodeTemplate()
        {
            if (templateLOD1Node != null) return templateLOD1Node;
            Mesh mesh = TrenchMeshBuilder.BuildModularEndCapMesh(3.0f, 0.8f, 0.5f);
            generatedTemplateMeshes.Add(mesh);
            return templateLOD1Node = CreateTemplateObject("Template_LOD1Node", mesh, TrenchMaterialResolver.GetEarthBermMaterial());
        }

        private static GameObject CreateTemplateObject(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.sharedMaterial = material;
            return go;
        }
    }
}

