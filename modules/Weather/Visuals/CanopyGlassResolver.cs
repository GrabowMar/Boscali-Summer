using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Weather.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Visuals
{
    internal readonly struct CanopySurface
    {
        internal readonly MeshRenderer Renderer;
        internal readonly Mesh Mesh;
        internal readonly int Submesh;
        internal readonly int Score;
        internal readonly LODGroup Lod;
        internal CanopySurface(MeshRenderer renderer, Mesh mesh, int submesh, int score)
        {
            Renderer = renderer; Mesh = mesh; Submesh = submesh; Score = score;
            Lod = renderer.GetComponentInParent<LODGroup>();
        }
    }

    // Search ownship near the cockpit eye, using metadata rather than native vertex reads.
    internal static class CanopyGlassResolver
    {
        internal const int MaxSurfaces = 8;
        private const int MaxNodes = 1024;
        private static readonly List<CanopySurface> surfaces = new List<CanopySurface>(MaxSurfaces);
        private static readonly List<Material> materials = new List<Material>(16);
        private static readonly Stack<Transform> pending = new Stack<Transform>(MaxNodes);
        private static int aircraftId, attempts, resolvedMask;
        private static float retryAt;

        internal static IReadOnlyList<CanopySurface> Resolve(Transform root, Vector3 eye, int layerMask = -1)
        {
            if (root == null) { ResetForScene(); return surfaces; }
            if (aircraftId != root.GetInstanceID() || resolvedMask != layerMask)
            {
                ResetForScene();
                aircraftId = root.GetInstanceID();
                resolvedMask = layerMask;
            }
            for (int i = surfaces.Count - 1; i >= 0; i--)
                if (surfaces[i].Renderer == null || surfaces[i].Mesh == null) surfaces.RemoveAt(i);
            if (surfaces.Count > 0 || attempts >= 3 || Time.unscaledTime < retryAt) return surfaces;
            attempts++;
            retryAt = Time.unscaledTime + 2f;
            pending.Push(root);
            int visited = 0;
            try
            {
                while (pending.Count > 0 && visited < MaxNodes)
                {
                    Transform node = pending.Pop();
                    visited++;
                    if (node == null) continue;
                    var renderer = node.GetComponent<MeshRenderer>();
                    var filter = renderer != null ? node.GetComponent<MeshFilter>() : null;
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh != null && renderer.gameObject.activeInHierarchy &&
                        (layerMask & (1 << renderer.gameObject.layer)) != 0)
                    {
                        Bounds bounds = renderer.bounds;
                        float distance = Vector3.Distance(bounds.ClosestPoint(eye), eye);
                        if (CanopyScoring.PlausibleBounds(distance, bounds.size.magnitude))
                        {
                            renderer.GetSharedMaterials(materials);
                            int count = Math.Min(16, Math.Min(materials.Count, mesh.subMeshCount));
                            for (int slot = 0; slot < count; slot++)
                            {
                                Material mat = materials[slot];
                                if (mat == null || mat.shader == null) continue;
                                bool transparent = mat.renderQueue >= 2501 ||
                                    mat.GetTag("RenderType", false) == "Transparent" ||
                                    (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f);
                                int score = CanopyScoring.ScoreSurface(node.name, mat.name, mat.shader.name,
                                    transparent, distance, bounds.size.magnitude);
                                if (score <= 0) continue;
                                int at = 0;
                                while (at < surfaces.Count && surfaces[at].Score >= score) at++;
                                if (at >= MaxSurfaces) continue;
                                if (surfaces.Count == MaxSurfaces) surfaces.RemoveAt(MaxSurfaces - 1);
                                surfaces.Insert(at, new CanopySurface(renderer, mesh, slot, score));
                            }
                        }
                    }
                    // Bound transforms too: empty hierarchies must not defeat the search cap.
                    int children = Math.Min(node.childCount, MaxNodes - visited - pending.Count);
                    for (int i = children - 1; i >= 0; i--) pending.Push(node.GetChild(i));
                }
            }
            catch (MissingReferenceException) { surfaces.Clear(); }
            finally { pending.Clear(); materials.Clear(); }
            return surfaces;
        }

        internal static void ResetForScene()
        {
            surfaces.Clear(); materials.Clear(); pending.Clear();
            aircraftId = attempts = 0; retryAt = 0f;
        }
    }
}
