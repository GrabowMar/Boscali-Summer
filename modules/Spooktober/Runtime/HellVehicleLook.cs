using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>Original alien hulls over native combat vehicles. Physics, weapons, joints and damage stay native.
    /// Only mesh rendering is concealed; particles, muzzle flashes, trails and native damage state are preserved.</summary>
    internal static class HellVehicleLook
    {
        private sealed class Native
        {
            internal Renderer Renderer;
            internal bool ForcedOff;
        }

        private sealed class Shell
        {
            internal readonly Unit Unit;
            private readonly List<Native> native = new List<Native>();
            private readonly HashSet<Renderer> known = new HashSet<Renderer>();
            private readonly List<Renderer> scan = new List<Renderer>(), partScan = new List<Renderer>();
            private GameObject model;
            private MeshRenderer shellRenderer;
            private bool extinguished;

            internal Shell(Unit unit) { Unit = unit; }

            internal int Hidden
            {
                get
                {
                    int count = 0;
                    foreach (Native item in native)
                        if (item.Renderer != null && item.Renderer.forceRenderingOff) count++;
                    return count;
                }
            }

            internal void Build(Mesh mesh, Material[] materials, string key)
            {
                Gather();
                Bounds body = BodyBounds();
                Bounds hull = mesh.bounds;
                // Preserve the authored silhouette and match its horizontal area. Independent XYZ fitting flattens the furnace.
                float scale = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(.01f, body.size.x * body.size.z) /
                    Mathf.Max(.01f, hull.size.x * hull.size.z)), .08f, 6f);
                model = new GameObject("Hell Alien Hull: " + key, typeof(MeshFilter), typeof(MeshRenderer));
                model.transform.SetParent(Unit.transform, false);
                model.transform.localScale = Vector3.one * scale;
                Vector3 center = body.center - hull.center * scale;
                if (!(Unit is Aircraft)) center.y = body.min.y + .2f - hull.min.y * scale;
                model.transform.localPosition = center;
                model.GetComponent<MeshFilter>().sharedMesh = mesh;
                shellRenderer = model.GetComponent<MeshRenderer>();
                shellRenderer.sharedMaterials = materials;
                shellRenderer.shadowCastingMode = ShadowCastingMode.On;
                shellRenderer.receiveShadows = true;
                Conceal();
            }

            internal void Extinguish()
            {
                if (extinguished || shellRenderer == null) return;
                if (coldCore == null) coldCore = new MaterialPropertyBlock();
                coldCore.Clear();
                coldCore.SetFloat(IntensityId, 0f);
                shellRenderer.SetPropertyBlock(coldCore);
                extinguished = true;
            }

            internal void Refresh()
            {
                Gather(); // new damage meshes and mass-bearing aircraft parts can appear after the spawn callback
                Conceal();
            }

            private void Gather()
            {
                Unit.GetComponentsInChildren(true, scan);
                if (Unit.partLookup == null) return;
                foreach (UnitPart part in Unit.partLookup)
                {
                    if (part == null || part.transform.IsChildOf(Unit.transform)) continue;
                    part.GetComponentsInChildren(true, partScan);
                    foreach (Renderer renderer in partScan)
                        if (!scan.Contains(renderer)) scan.Add(renderer);
                }
            }

            private Bounds BodyBounds()
            {
                bool found = false;
                Bounds bounds = default;
                Matrix4x4 toUnit = Unit.transform.worldToLocalMatrix;
                foreach (Renderer renderer in scan)
                {
                    // Include culled LOD geometry: the gate often spawns kilometres from the camera. Debug meshes never size the hull.
                    if (!Geometry(renderer) || Effect(renderer) || DebugGeometry(renderer.transform)) continue;
                    Mesh mesh = MeshOf(renderer);
                    if (mesh == null) continue;
                    Bounds local = mesh.bounds;
                    if (!Finite(local.center) || !Finite(local.size) || local.size.sqrMagnitude < .0001f || local.size.sqrMagnitude > 40000f) continue;
                    Matrix4x4 matrix = toUnit * renderer.localToWorldMatrix;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                            new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f)));
                        if (!Finite(point) || point.sqrMagnitude > 40000f) continue;
                        if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                        else bounds.Encapsulate(point);
                    }
                }
                if (found && bounds.size.x > .1f && bounds.size.z > .1f) return bounds;
                Vector3 size = Unit is Aircraft ? new Vector3(18f, 4f, 16f) : new Vector3(3f, 3f, 7f);
                float offset = Unit.definition != null ? Unit.definition.spawnOffset.y : size.y * .5f;
                return new Bounds(new Vector3(0f, size.y * .5f - offset, 0f), size);
            }

            private void Conceal()
            {
                foreach (Renderer renderer in scan)
                {
                    if (renderer == null || known.Contains(renderer) || !Geometry(renderer) ||
                        model != null && renderer.transform.IsChildOf(model.transform) || Effect(renderer)) continue;
                    known.Add(renderer);
                    native.Add(new Native { Renderer = renderer, ForcedOff = renderer.forceRenderingOff });
                }
                foreach (Native item in native)
                    if (item.Renderer != null) item.Renderer.forceRenderingOff = true;
            }

            internal void Remove()
            {
                if (model != null) Object.Destroy(model);
                foreach (Native item in native)
                    if (item.Renderer != null)
                    {
                        item.Renderer.forceRenderingOff = item.ForcedOff;
                        // Enabled/materials never changed: keep genuine damage and LOD changes made while hidden.
                    }
                native.Clear();
                known.Clear();
                scan.Clear();
                partScan.Clear();
            }
        }

        private static readonly Dictionary<Unit, Shell> live = new Dictionary<Unit, Shell>();
        private static readonly List<Unit> released = new List<Unit>();
        private static readonly string[] effectWords = { "muzzle", "flash", "flame", "exhaust", "trail", "tracer", "smoke", "spark", "beam" };
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static MaterialPropertyBlock coldCore;
        private static bool warned;

        internal static int Count => live.Count;
        internal static int HiddenRenderers
        {
            get { int count = 0; foreach (Shell shell in live.Values) count += shell.Hidden; return count; }
        }

        internal static bool Apply(Unit unit)
        {
            if (unit == null || unit.disabled || !(unit is GroundVehicle || unit is Aircraft)) return false;
            if (live.TryGetValue(unit, out Shell present)) { present.Refresh(); return true; }
            string key = ModelKey(unit);
            Mesh mesh = HellMeshes.Mesh(key);
            Material[] materials = HellMaterials.Shared;
            if (mesh == null || materials == null || materials.Length != 3 || materials[0] == null || materials[1] == null || materials[2] == null)
                return false;
            var shell = new Shell(unit);
            try
            {
                shell.Build(mesh, materials, key);
                live.Add(unit, shell);
                return true;
            }
            catch (Exception e)
            {
                shell.Remove();
                if (!warned) Plugin.Logger.LogWarning("[Spooktober] Alien vehicle hull: " + e.Message);
                warned = true;
                return false;
            }
        }

        internal static void Refresh()
        {
            released.Clear();
            foreach (KeyValuePair<Unit, Shell> pair in live)
                if (pair.Key == null || !HellArmy.IsHell(pair.Key)) released.Add(pair.Key);
                else if (pair.Key.disabled) { pair.Value.Refresh(); pair.Value.Extinguish(); }
            foreach (Unit unit in released) { live[unit].Remove(); live.Remove(unit); }
            released.Clear();
        }

        internal static void Clear()
        {
            foreach (Shell shell in live.Values) shell.Remove();
            live.Clear();
            released.Clear();
            warned = false;
            coldCore = null;
            // HellMeshes/HellMaterials are shared with the dimension and territory. Their owner releases them after all shells.
        }

        private static string ModelKey(Unit unit)
        {
            if (unit is Aircraft) return "legion_sky";
            string key = unit.definition != null ? unit.definition.jsonKey ?? "" : "";
            return key.IndexOf("MBT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("SPAAG", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("SAM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                key.IndexOf("MLRS", StringComparison.OrdinalIgnoreCase) >= 0 ? "legion_tank" : "legion_scout";
        }

        private static bool Geometry(Renderer renderer) => renderer != null && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer);
        private static Mesh MeshOf(Renderer renderer) => renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
            : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static bool DebugGeometry(Transform transform)
        {
            for (int depth = 0; transform != null && depth < 24; depth++, transform = transform.parent)
            {
                string name = transform.name;
                if (name.IndexOf("debug", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("marker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("selection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("gizmo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.StartsWith("OpsCompound_", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool Effect(Renderer renderer)
        {
            if (renderer.gameObject.layer == PhysicsLayers.Effects || renderer.GetComponentInParent<ParticleSystem>() != null) return true;
            for (Transform transform = renderer.transform; transform != null; transform = transform.parent)
                foreach (string word in effectWords)
                    if (transform.name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
