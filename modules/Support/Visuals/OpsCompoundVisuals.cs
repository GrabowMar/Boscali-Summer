using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Support.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// Client-local look of the OPS buildings: a native anchor unit (pad, data center, EW truck, camp, launcher) keeps its
    /// collision, damage and networking, but its own meshes are hidden and the matching compound model
    /// (assets/OpsMobileCompounds, baked by build_compounds.py into Assets/Compounds/*.opsc) is drawn in its place. Every peer
    /// finds the anchors by their replicated unique name, so no network traffic. A dead anchor gets its native meshes back,
    /// so the vanilla wreck and fire show. No colliders.
    /// </summary>
    internal sealed class OpsCompoundVisuals : MonoBehaviour, ISceneService
    {
        private const float ScanInterval = 2f;
        private const int ScanCap = 4096;

        /// <summary>Name tag of a group's anchor (guards carry a "Guard" kind, so they never match) and its compound blob.</summary>
        private static readonly string[][] Kinds =
        {
            new[] { SupportNaming.Prefix + "Uplink:", "uplink" },
            new[] { SupportNaming.Prefix + "Cyber:Center:", "center" },
            new[] { SupportNaming.Prefix + "Cyber:Truck:", "truck" },
            new[] { SupportNaming.Prefix + "Cyber:Camp:", "camp" },
            new[] { SupportNaming.Prefix + "Cyber:Launcher:", "launcher" },
        };

        private sealed class Dressed
        {
            public GameObject Model;
            public readonly List<Renderer> Hidden = new List<Renderer>(16);
        }

        private readonly Dictionary<Unit, Dressed> dressed = new Dictionary<Unit, Dressed>();
        private readonly List<Unit> scratch = new List<Unit>();
        private float nextScan;

        // Loaded once per process: one mesh per compound, one material per palette name.
        private static readonly Dictionary<string, KeyValuePair<Mesh, Material[]>> models = new Dictionary<string, KeyValuePair<Mesh, Material[]>>();
        private static readonly Dictionary<string, Material> palette = new Dictionary<string, Material>();

        public void ResetForScene()
        {
            foreach (var pair in dressed) Undress(pair.Value);
            dressed.Clear();
            nextScan = 0f;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (Application.isBatchMode || Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + ScanInterval;

            scratch.Clear();
            foreach (var pair in dressed)
                if (pair.Key == null || pair.Key.disabled) scratch.Add(pair.Key);
            foreach (Unit gone in scratch) { Undress(dressed[gone]); dressed.Remove(gone); }

            List<Unit> units = UnitRegistry.allUnits;
            if (units == null) return;
            for (int i = 0; i < units.Count && i < ScanCap; i++)
            {
                Unit unit = units[i];
                if (unit == null || unit.disabled || !(unit is GroundVehicle) || dressed.ContainsKey(unit)) continue;
                string name = unit.NetworkUniqueName;
                if (name == null || !name.StartsWith(SupportNaming.Prefix, StringComparison.Ordinal)) continue;
                for (int k = 0; k < Kinds.Length; k++)
                    if (name.StartsWith(Kinds[k][0], StringComparison.Ordinal)) { Dress(unit, Kinds[k][1]); break; }
            }
        }

        private void Dress(Unit unit, string kind)
        {
            var entry = new Dressed();
            dressed[unit] = entry; // a failed load is remembered too, so it is not retried every scan
            if (!TryModel(kind, out Mesh mesh, out Material[] materials)) return;

            float ground = float.MaxValue;
            foreach (Renderer r in unit.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue; // keep particles, trails and lines
                if (r.enabled && r.gameObject.activeInHierarchy) ground = Mathf.Min(ground, r.bounds.min.y);
                if (r.forceRenderingOff) continue;
                r.forceRenderingOff = true;
                entry.Hidden.Add(r);
            }

            var model = new GameObject("OpsCompound_" + kind);
            model.transform.SetParent(unit.transform, false);
            // Wheels on the ground: the compound root sits at ground level under its truck.
            if (ground < float.MaxValue) model.transform.localPosition = new Vector3(0f, ground - unit.transform.position.y, 0f);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterials = materials;
            entry.Model = model;
        }

        private static void Undress(Dressed entry)
        {
            if (entry.Model != null) Destroy(entry.Model);
            foreach (Renderer r in entry.Hidden) if (r != null) r.forceRenderingOff = false;
            entry.Hidden.Clear();
        }

        private static OpsCompoundVisuals live;
        private void Awake() => live = this;

        /// <summary>
        /// Sim hook (tests/ingame/ops-compounds.json): the dressed anchors, each with its kind, whether the model drew and how many
        /// native meshes it hides. With "frame": n, the free camera looks at the n-th compound from 35 m.
        /// </summary>
        public static Dictionary<string, object> Probe(Dictionary<string, object> args)
        {
            var rows = new List<object>();
            Unit framed = null;
            int want = args != null && args.TryGetValue("frame", out object f) ? Convert.ToInt32(f) : -1;
            if (live != null)
                foreach (var pair in live.dressed)
                {
                    if (pair.Key == null) continue;
                    if (rows.Count == want) framed = pair.Key;
                    rows.Add(new Dictionary<string, object>
                    {
                        ["name"] = pair.Key.NetworkUniqueName,
                        ["model"] = pair.Value.Model != null ? pair.Value.Model.name : null,
                        ["hidden"] = pair.Value.Hidden.Count,
                    });
                }
            if (framed != null)
            {
                Vector3 center = framed.transform.position + Vector3.up * 2f;
                Vector3 eye = center + Quaternion.AngleAxis(140f, Vector3.up) * framed.transform.forward * 26f + Vector3.up * 16f;
                CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
                camera.SetCameraPosition(eye.ToGlobalPosition(), Quaternion.LookRotation(center - eye));
                camera.cameraVelocity = Vector3.zero;
                camera.allowInputs = false;
                camera.SetDesiredFoV(50f, 50f);
                SceneSingleton<DynamicMap>.i?.Minimize();
            }
            return new Dictionary<string, object> { ["ok"] = true, ["count"] = rows.Count, ["dressed"] = rows };
        }

        private static bool TryModel(string kind, out Mesh mesh, out Material[] materials)
        {
            if (models.TryGetValue(kind, out var cached)) { mesh = cached.Key; materials = cached.Value; return mesh != null; }
            mesh = null;
            materials = null;
            try
            {
                byte[] packed = EmbeddedResources.ReadAll(typeof(OpsCompoundVisuals).Assembly, "BoscaliSummer.Support.Compounds." + kind + ".opsc", 4 << 20);
                if (packed != null) Load(packed, kind, out mesh, out materials);
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("[Support.Compounds] " + kind + " model unreadable: " + e.Message);
                mesh = null;
            }
            models[kind] = new KeyValuePair<Mesh, Material[]>(mesh, materials);
            return mesh != null;
        }

        /// <summary>Reads the blob build_compounds.py writes (layout in its export_game_blob docstring).</summary>
        private static void Load(byte[] packed, string kind, out Mesh mesh, out Material[] materials)
        {
            using (var gz = new GZipStream(new MemoryStream(packed), CompressionMode.Decompress))
            using (var read = new BinaryReader(gz))
            {
                if (new string(read.ReadChars(4)) != "OPSC" || read.ReadInt32() != 1) throw new InvalidDataException("not an OPSC v1 blob");
                int subs = read.ReadInt32();
                if (subs < 1 || subs > 32) throw new InvalidDataException("bad submesh count");
                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                var indices = new List<int>[subs];
                materials = new Material[subs];
                for (int s = 0; s < subs; s++)
                {
                    int nameLength = read.ReadInt32();
                    if (nameLength < 0 || nameLength > 128) throw new InvalidDataException("bad name");
                    string name = System.Text.Encoding.UTF8.GetString(read.ReadBytes(nameLength));
                    var linear = new Color(read.ReadSingle(), read.ReadSingle(), read.ReadSingle());
                    float smoothness = read.ReadSingle(), metallic = read.ReadSingle();
                    int vertices = read.ReadInt32(), count = read.ReadInt32();
                    if (vertices < 0 || count < 0 || positions.Count + vertices > 1 << 20 || count > 3 << 20) throw new InvalidDataException("bad sizes");
                    int baseVertex = positions.Count;
                    for (int v = 0; v < vertices; v++)
                    {
                        positions.Add(new Vector3(read.ReadSingle(), read.ReadSingle(), read.ReadSingle()));
                        normals.Add(new Vector3(read.ReadSingle(), read.ReadSingle(), read.ReadSingle()));
                    }
                    var list = new List<int>(count);
                    for (int t = 0; t < count; t++) list.Add(baseVertex + read.ReadInt32());
                    indices[s] = list;
                    materials[s] = Palette(name, linear, smoothness, metallic);
                }
                mesh = new Mesh { name = "OpsCompound_" + kind, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.subMeshCount = subs;
                for (int s = 0; s < subs; s++) mesh.SetTriangles(indices[s], s, false);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
            }
        }

        private static Material Palette(string name, Color linear, float smoothness, float metallic)
        {
            if (palette.TryGetValue(name, out Material known)) return known;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name };
            Color color = linear.gamma; // the blob carries Blender's linear factors; material colours are sRGB
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            palette[name] = material;
            return material;
        }
    }
}
