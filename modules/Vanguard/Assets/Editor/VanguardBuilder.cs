// Builds Vanguard.nobp from vanilla donors + the Blender models (BuildVanguard.py output).
//   Unity -batchmode -quit -projectPath <bp> -executeMethod Vanguard.VanguardBuilder.Run
// Env: VG_MODELS (folder with the *.fbx), VG_OUT (where Vanguard.nobp is copied).
// Recipe follows Circuit-Breaker: copy donor ScriptableObjects, clone donor prefabs, hide donor
// renderers, attach our FBX, re-point references, then one OpAddWeaponToHardpoint per rack that
// reuses every hardpoint the donor rack is allowed on. Prefabs carry vanilla components only;
// behaviour lives in BoscaliSummer.dll (modules/Vanguard/Runtime) keyed on the jsonKeys below.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blueprinter;
using UnityEditor;
using UnityEngine;

namespace Vanguard
{
    public static class VanguardBuilder
    {
        const string R = "Assets/Blueprinter/Mods/Vanguard/";
        const string D = "Assets/Blueprinter/_donotship/";
        const string Version = "0.1.0";

        // Keep in sync with modules/Vanguard/Domain/VanguardKeys.cs.
        sealed class Spec
        {
            public string Key, Name, Short, Model, Description;
            public float Mass, Yield, Pierce, Cost, Value, RadarSize, Thrust, BurnTime, GLimit, TurnRate;
            public string[] Racks;
        }

        static readonly Spec[] Missiles =
        {
            new Spec { Key = "VG_MaldX", Name = "ADM-160X MALD-X", Short = "MALD-X", Model = "MaldX",
                Description = "Miniature air-launched decoy. Flies a weaving 80 km route with a fighter-sized radar signature, then orbits. Radar-guided missiles chasing the launcher may switch to it.",
                Mass = 140, Yield = 1, Cost = 0.3f, Value = 90, RadarSize = 0.6f, Thrust = 900, BurnTime = 600, GLimit = 6, TurnRate = 12,
                Racks = new[] { "AGM_heavy_single", "AGM_heavy_triple", "CruiseMissile1_internalx2" } },
            new Spec { Key = "VG_MaldJ", Name = "ADM-160J MALD-J", Short = "MALD-J", Model = "MaldX",
                Description = "Jammer decoy. As MALD-X, and jams every enemy radar inside a 60 degree cone ahead of it out to 25 km.",
                Mass = 150, Yield = 1, Cost = 0.6f, Value = 110, RadarSize = 0.6f, Thrust = 950, BurnTime = 600, GLimit = 6, TurnRate = 12,
                Racks = new[] { "AGM_heavy_single", "AGM_heavy_triple", "CruiseMissile1_internalx2" } },
            new Spec { Key = "VG_Remora", Name = "XQ-58V REMORA", Short = "REMORA", Model = "Remora",
                Description = "Attritable escort drone. Holds a wing slot on the launcher and draws radar-guided missiles. On STRIKE it dives into the launcher's target with a 60 kg warhead. Six-minute endurance.",
                Mass = 450, Yield = 60, Pierce = 300, Cost = 0.8f, Value = 40, RadarSize = 0.15f, Thrust = 4200, BurnTime = 360, GLimit = 9, TurnRate = 25,
                Racks = new[] { "AGM_heavy_single", "CruiseMissile1_internalx2" } },
            new Spec { Key = "VG_HawcX", Name = "HAWC-X Hypersonic Glide Vehicle", Short = "HAWC-X", Model = "HawcX",
                Description = "Boost-glide strike weapon. Climbs to 25 km at Mach 8, skips and weaves along the edge of space, then dives near-vertically onto the target.",
                Mass = 1500, Yield = 150, Pierce = 2500, Cost = 4f, Value = 30, RadarSize = 0.003f, Thrust = 180000, BurnTime = 30, GLimit = 25, TurnRate = 18,
                Racks = new[] { "AGM_heavy_single", "CruiseMissile1_internalx2" } },
            new Spec { Key = "VG_AegisDart", Name = "AIM-X AEGIS", Short = "AEGIS", Model = "AegisInterceptor",
                Description = "Three upright cold-launch darts on an exposed rail. Detects visible inbound threats inside 1.1 km from any direction, drops a dart, snaps it onto the threat and ignites. Three shots; actual contact applies native armor damage, never a proximity kill roll.",
                Mass = 25, Yield = .2f, Pierce = 50, Cost = 0.15f, Value = 2, RadarSize = 0.0005f, Thrust = 9000, BurnTime = 2.5f, GLimit = 45, TurnRate = 55,
                Racks = new string[0] }, // AEGIS gets its own three-dart pod, see AegisPod()
            new Spec { Key = "VG_Glaive2A", Name = "GLAIVE-2A Airborne Gun Turret", Short = "GLAIVE-A", Model = "Glaive",
                Description = "Winged autonomous gun pod. Flies near the target at 650 m, brakes under a parachute and fires its stabilized 30 mm cannon from above while descending. 240 rounds; hostile tracked targets only.",
                Mass = 180, Yield = 0, Cost = 1.2f, Value = 25, RadarSize = 0.4f, Thrust = 1700, BurnTime = 200, GLimit = 6, TurnRate = 15,
                Racks = new[] { "AGM_heavy_single", "CruiseMissile1_internalx2" } },
            new Spec { Key = "VG_Glaive2S", Name = "GLAIVE-2S Suppression Gun Turret", Short = "GLAIVE-S", Model = "Glaive",
                Description = "Parachute-suspended gun pod with a stabilized 30 mm cannon and 240 high-explosive rounds. Brakes above a tracked hostile target, shoots during a slow descent, and retires before ground contact.",
                Mass = 190, Yield = 0, Cost = 1.6f, Value = 28, RadarSize = 0.4f, Thrust = 1700, BurnTime = 200, GLimit = 6, TurnRate = 15,
                Racks = new[] { "AGM_heavy_single", "CruiseMissile1_internalx2" } },
            new Spec { Key = "VG_Orca", Name = "AGT-80 ORCA Glide Torpedo", Short = "ORCA", Model = "Orca",
                Description = "Long-glide torpedo. Glides ~25 km toward a ship, enters the water 5 km out and runs at 90 m/s, 6 m deep, into the hull. Ships only; a hard turn in the last 300 m can beat it.",
                Mass = 600, Yield = 450, Pierce = 600, Cost = 1.5f, Value = 30, RadarSize = 0.05f, Thrust = 1500, BurnTime = 300, GLimit = 8, TurnRate = 20,
                Racks = new[] { "AGM_heavy_single", "AGM_heavy_triple" } },
            new Spec { Key = "VG_AleX", Name = "ALE-X Towed Decoy", Short = "ALE-X", Model = "AleX",
                Description = "Fiber-towed decoy. Trails 100 m behind the aircraft; radar missiles tracking you may switch to it, mostly from the rear. Lost above 7 g, below 50 m or after five minutes.",
                Mass = 20, Yield = 2, Cost = 0.05f, Value = 3, RadarSize = 1.5f, Thrust = 0, BurnTime = 1, GLimit = 30, TurnRate = 200,
                Racks = new string[0] }, // ALE-X gets its own two-decoy pod, see AleXPod()
        };

        public static void Run()
        {
            ImportModels();
            var materials = Materials();
            var infos = new Dictionary<string, WeaponInfo>();
            foreach (Spec spec in Missiles) infos[spec.Key] = Missile(spec, materials);
            foreach (Spec spec in Missiles)
                foreach (string rack in spec.Racks) Mount(spec, infos[spec.Key], rack, materials);
            AegisPod(infos["VG_AegisDart"], materials);
            AleXPod(infos["VG_AleX"], materials);
            SkywellKit(materials);
            Lance(materials);
            AssetDatabase.SaveAssets();
            ValidateOperationalPrefabs();
            OpReferenceIndex.Refresh();
            Build();
            ValidateAndPreview(materials);
        }

        // ------------------------------------------------------------------ helpers

        static void ValidateOperationalPrefabs()
        {
            var aegis = Load<GameObject>(R + "VG_Aegis_Pod.prefab");
            var cells = aegis.GetComponentsInChildren<MountedMissile>(true);
            if (cells.Length != 3) throw new Exception("AEGIS requires exactly three launch cells");
            foreach (var cell in cells)
                if (cell.transform.Cast<Transform>().Count(t => t.name == "VanguardVisual") != 1)
                    throw new Exception("AEGIS cell contains duplicate dart visuals: " + cell.name);
            foreach (string key in new[] { "VG_Glaive2A", "VG_Glaive2S" })
            {
                var pod = Load<GameObject>(R + key + ".prefab");
                var guns = pod.GetComponentsInChildren<Gun>(true);
                if (guns.Length != 1 || !guns[0].ForceServerAuthority || guns[0].info.muzzleVelocity != 950f)
                    throw new Exception(key + " native cannon / host authority invalid");
                var canopy = pod.transform.Find("GlaiveCanopy");
                if (canopy == null || canopy.gameObject.activeSelf) throw new Exception(key + " canopy must start stowed");
                var names = new HashSet<string>(pod.GetComponentsInChildren<Transform>(true).Select(t => t.name));
                foreach (string name in new[] { "GunYaw", "GunPitch", "GlaiveMuzzle", "WingL", "WingR" })
                    if (!names.Contains(name)) throw new Exception(key + " missing rig node " + name);
            }
            Debug.Log("[Vanguard] OPERATIONAL_PREFABS_OK AEGIS=3_unique_cells GLAIVE=2_native_host_guns");
        }

        static T Load<T>(string p) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(p) ?? throw new Exception("Missing " + p);

        static SerializedProperty P(SerializedObject s, string n) =>
            s.FindProperty(n) ?? throw new Exception(s.targetObject.name + " missing " + n);

        static void Edit(UnityEngine.Object o, Action<SerializedObject> a)
        {
            var s = new SerializedObject(o);
            a(s);
            s.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(o);
        }

        static T Copy<T>(string donor, string name) where T : UnityEngine.Object
        {
            string p = R + name + ".asset";
            if (!File.Exists(p) && !AssetDatabase.CopyAsset(D + "MonoBehaviour/" + donor + "_PLACEHOLDER.asset", p))
                throw new Exception("copy failed " + donor);
            var o = Load<T>(p);
            o.name = name;
            return o;
        }

        static GameObject Clone(string path)
        {
            var g = UnityEngine.Object.Instantiate(Load<GameObject>(path));
            g.name = g.name.Replace("(Clone)", "");
            return g;
        }

        static void HideRenderers(GameObject g)
        {
            foreach (var r in g.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var l in g.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(l);
        }

        static void ImportModels()
        {
            string src = Environment.GetEnvironmentVariable("VG_MODELS") ?? throw new Exception("VG_MODELS not set");
            Directory.CreateDirectory(R + "Models");
            foreach (string file in Directory.GetFiles(src))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".fbx") || name.EndsWith("_Albedo.png") || name.EndsWith("_Normal.png") || name.EndsWith("_MetalGloss.png")
                    || name.EndsWith("_Icon.png"))
                    File.Copy(file, R + "Models/" + name, true);
            }
            AssetDatabase.Refresh();
            foreach (string file in Directory.GetFiles(R + "Models"))
            {
                string path = file.Replace('\\', '/');
                if (path.EndsWith(".fbx"))
                {
                    var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                    importer.globalScale = 1f;
                    importer.useFileUnits = false;
                    importer.bakeAxisConversion = true;
                    importer.importNormals = ModelImporterNormals.Import;
                    importer.importTangents = ModelImporterTangents.CalculateMikk;
                    importer.animationType = ModelImporterAnimationType.None;
                    importer.importAnimation = false;
                    importer.importCameras = false;
                    importer.importLights = false;
                    importer.importBlendShapes = false;
                    importer.importVisibility = false; // Source hides low meshes only while making Blender previews.
                    importer.isReadable = false;
                    importer.optimizeMeshPolygons = true;
                    importer.optimizeMeshVertices = true;
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.SaveAndReimport();
                }
                else if (path.EndsWith("_Icon.png"))
                {
                    // Vanilla weapon icons: 512x256 white line art on black, sampled as a UI sprite.
                    var icon = (TextureImporter)AssetImporter.GetAtPath(path);
                    icon.textureType = TextureImporterType.Sprite;
                    icon.spriteImportMode = SpriteImportMode.Single;
                    icon.sRGBTexture = true;
                    icon.mipmapEnabled = false;
                    icon.maxTextureSize = 512;
                    icon.textureCompression = TextureImporterCompression.Uncompressed;
                    icon.SaveAndReimport();
                }
                else if (path.EndsWith(".png"))
                {
                    var tex = (TextureImporter)AssetImporter.GetAtPath(path);
                    tex.textureType = path.EndsWith("_Normal.png") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    tex.sRGBTexture = path.EndsWith("_Albedo.png");
                    tex.maxTextureSize = 2048; // BuildVanguard.py bakes 2048 for the 4-7 m airframes, 1024 otherwise
                    tex.textureCompression = TextureImporterCompression.CompressedHQ;
                    tex.mipmapEnabled = true;
                    tex.anisoLevel = 4;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    // MetalGloss alpha is smoothness; preserve it without treating it as transparency.
                    tex.alphaSource = path.EndsWith("_MetalGloss.png") ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                    tex.alphaIsTransparency = false;
                    tex.isReadable = false;
                    tex.SaveAndReimport();
                }
            }
        }

        static Material MakeMaterial(string name, Action<Material> setup)
        {
            Directory.CreateDirectory(R + "Materials");
            string p = R + "Materials/" + name + ".mat";
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m)
            {
                m = new Material(lit) { name = name };
                AssetDatabase.CreateAsset(m, p);
            }
            m.shader = lit;
            m.enableInstancing = true;
            setup(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Dictionary<string, Material> Materials() => new Dictionary<string, Material>
        {
            ["Dark"] = MakeMaterial("Vanguard_Dark", m =>
            {
                m.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.055f));
                m.SetFloat("_Metallic", 0.4f);
                m.SetFloat("_Smoothness", 0.4f);
            }),
            ["Glass"] = MakeMaterial("Vanguard_Glass", m =>
            {
                m.SetColor("_BaseColor", new Color(0.10f, 0.16f, 0.20f));
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.82f);
            }),
            ["Glow"] = MakeMaterial("Vanguard_Glow", m =>
            {
                m.SetColor("_BaseColor", new Color(0.07f, 0.3f, 0.36f));
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.7f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.08f, 0.4f, 0.46f) * 0.8f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }),
        };

        // One textured skin per model: baked albedo, panel-line normal map, metal (R) / smoothness (A).
        static Material Skin(string model) => MakeMaterial("Skin_" + model, m =>
        {
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseMap", Load<Texture2D>(R + "Models/" + model + "_Albedo.png"));
            m.SetTexture("_BumpMap", Load<Texture2D>(R + "Models/" + model + "_Normal.png"));
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", Load<Texture2D>(R + "Models/" + model + "_MetalGloss.png"));
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetFloat("_Smoothness", 1f);
            if (model == "GlaiveCanopy") m.SetFloat("_Cull", 0f);
        });

        // BuildVanguard.py joins each model into parts named Skin / Glow / Glass / Dark.
        static GameObject Visual(Transform parent, string model, Dictionary<string, Material> materials)
        {
            var g = Clone(R + "Models/" + model + ".fbx");
            g.name = "VanguardVisual";
            g.transform.SetParent(parent, false);
            g.transform.localPosition = Vector3.zero;
            // The Blender FBX (nose +Y, up +Z) arrives axis-baked with its nose along the root's +Y; pitch it +90 deg so
            // the nose lies along the mount's +Z (forward) and its top faces +Y. Identity here stood every model on end.
            g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = parent.gameObject.layer;
            var skin = Skin(model);
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
            {
                // Articulated models keep per-joint children ("Glow.001", "Dark.003"): match on the base name.
                string key = DetailName(r.name);
                key = key.IndexOf('.') > 0 ? key.Substring(0, key.IndexOf('.')) : key;
                var mat = materials.TryGetValue(key, out var shared) ? shared : skin;
                // Each source part has one material. Fail instead of silently rendering duplicate material slots.
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                if (mesh.subMeshCount != 1) throw new Exception(model + "/" + r.name + " has duplicate submeshes");
                r.sharedMaterials = new[] { mat };
                r.enabled = true;
            }
            ConfigureLods(g, model);
            return g;
        }

        static int LodLevel(string name) => name.EndsWith("_LOD1") ? 1 : name.EndsWith("_LOD2") ? 2 : 0;
        static string DetailName(string name) => LodLevel(name) == 0 ? name : name.Substring(0, name.Length - 5);

        static void ConfigureLods(GameObject visual, string model)
        {
            // FBX names ending _LOD1/_LOD2 can produce importer-generated groups. Use our exact groups instead.
            foreach (var old in visual.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(old);
            var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            var details = renderers.Where(r => LodLevel(r.name) == 0).ToDictionary(r => r.name);
            foreach (var r in renderers.Where(r => LodLevel(r.name) > 0))
            {
                if (!details.TryGetValue(DetailName(r.name), out var detail))
                    throw new Exception(model + "/" + r.name + " lost its detail mesh");
                // Joint transforms are the detail mesh itself. Reparent each low mesh so all levels move together.
                r.transform.SetParent(detail.transform, true);
            }
            if (renderers.Length != details.Count * 3) throw new Exception(model + " requires three meshes per LOD part");
            Transform kite = model == "SkywellKit" ? details.Values.Single(r => r.name == "Kite").transform : null;
            AddLods(visual.transform, renderers.Where(r => kite == null || !r.transform.IsChildOf(kite)).ToArray());
            if (kite != null)
            {
                // Pallet movement and the kite's deployed wings/tail need separate, conservative bounds.
                var palletGroup = visual.GetComponent<LODGroup>();
                palletGroup.size = Mathf.Max(palletGroup.size, 10f / LargestScale(visual.transform));
                AddLods(kite, renderers.Where(r => r.transform.IsChildOf(kite)).ToArray());
                var kiteGroup = kite.GetComponent<LODGroup>();
                kiteGroup.size = Mathf.Max(kiteGroup.size, 18f / LargestScale(kite));
            }
        }

        static float LargestScale(Transform t) => Mathf.Max(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y), Mathf.Abs(t.lossyScale.z));

        static void AddLods(Transform root, MeshRenderer[] renderers)
        {
            var group = root.gameObject.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None; // Avoid rendering two levels together on small, numerous weapons.
            group.SetLODs(new[]
            {
                new LOD(0.25f, renderers.Where(r => LodLevel(r.name) == 0).Cast<Renderer>().ToArray()),
                new LOD(0.08f, renderers.Where(r => LodLevel(r.name) == 1).Cast<Renderer>().ToArray()),
                new LOD(0.002f, renderers.Where(r => LodLevel(r.name) == 2).Cast<Renderer>().ToArray()),
            });
            group.RecalculateBounds();
        }

        static void StowWings(GameObject visual, Transform station)
        {
            foreach (Transform joint in visual.GetComponentsInChildren<Transform>(true))
                if (joint.name == "WingL" || joint.name == "WingR")
                {
                    Vector3 axis = joint.parent.InverseTransformDirection(station.up);
                    joint.localRotation = Quaternion.AngleAxis(joint.name == "WingL" ? -90f : 90f, axis) * joint.localRotation;
                }
        }

        static Sprite Icon(string model) => Load<Sprite>(R + "Models/" + model + "_Icon.png");

        static Bounds VisualBounds(GameObject visual)
        {
            var rs = visual.GetComponentsInChildren<Renderer>(true)
                .Where(r => LodLevel(r.name) == 0 && r.GetComponentInParent<Gun>(true) == null).ToArray();
            var b = rs[0].bounds;
            foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }

        [Serializable]
        sealed class ModelCheck
        {
            public string model;
            public int[] triangles;
            public int renderersPerLod;
            public int lodGroups;
            public int textureSize;
            public Vector3 dimensions;
        }

        [Serializable]
        sealed class ImportReport { public ModelCheck[] models; }

        static void ValidateAndPreview(Dictionary<string, Material> materials)
        {
            string directory = Environment.GetEnvironmentVariable("VG_PREVIEWS");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.40f, 0.44f, 0.50f);
            }
            var models = Missiles.Select(s => s.Model).Concat(new[] { "AegisPod", "AleXPod", "SkywellKit", "Lance", "GlaiveCanopy" }).Distinct().ToArray();
            var reports = new List<ModelCheck>();
            foreach (string model in models)
            {
                var wrapper = new GameObject("Vanguard import check");
                var visual = Visual(wrapper.transform, model, materials);
                var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
                int[] triangles = Enumerable.Range(0, 3).Select(level => renderers.Where(r => LodLevel(r.name) == level)
                    .Sum(r => (int)r.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3)).ToArray();
                if (triangles[0] <= 0 || triangles[1] >= triangles[0] || triangles[2] >= triangles[1])
                    throw new Exception(model + " LOD geometry did not decrease: " + string.Join("/", triangles));
                foreach (var r in renderers.Where(r => LodLevel(r.name) != 0))
                    if (r.transform.parent.name != DetailName(r.name)) throw new Exception(model + " LOD lost joint parent: " + r.name);
                var importer = (ModelImporter)AssetImporter.GetAtPath(R + "Models/" + model + ".fbx");
                if (importer.isReadable || importer.importAnimation || importer.importCameras || importer.importLights)
                    throw new Exception(model + " import retained unused data");
                foreach (string channel in new[] { "Albedo", "Normal", "MetalGloss" })
                {
                    var texture = Load<Texture2D>(R + "Models/" + model + "_" + channel + ".png");
                    int expected = new[] { "Remora", "HawcX", "Lance", "Glaive", "SkywellKit" }.Contains(model) ? 2048 : 1024;
                    if (texture.width != expected || texture.height != expected) throw new Exception(model + " texture density: " + channel);
                }
                if (model == "SkywellKit")
                {
                    var names = new HashSet<string>(visual.GetComponentsInChildren<Transform>(true).Select(t => t.name));
                    foreach (string name in new[] { "Deck", "Winch", "Kite", "Tail", "Wing", "WingOuterL", "WingOuterR", "WingletL", "WingletR", "Nozzle", "Loader", "LoaderFore", "Cradle" })
                        if (!names.Contains(name)) throw new Exception("Skywell lost joint " + name);
                }
                if (model == "MaldX" || model == "Glaive" || model == "Orca")
                {
                    Transform left = renderers.Single(r => r.name == "WingL").transform;
                    Transform right = renderers.Single(r => r.name == "WingR").transform;
                    if (left.position.x >= 0 || right.position.x <= 0) throw new Exception(model + " wing pivot handedness");
                    float deployedWidth = VisualBounds(visual).size.x;
                    StowWings(visual, wrapper.transform);
                    if (VisualBounds(visual).size.x >= deployedWidth * 0.65f) throw new Exception(model + " mounted wings do not fold along body");
                    // Rebuild the deployed check object so the preview represents the flight asset.
                    UnityEngine.Object.DestroyImmediate(visual);
                    visual = Visual(wrapper.transform, model, materials);
                }
                var check = new ModelCheck { model = model, triangles = triangles, renderersPerLod = renderers.Length / 3,
                    lodGroups = visual.GetComponentsInChildren<LODGroup>(true).Length, dimensions = VisualBounds(visual).size,
                    textureSize = Load<Texture2D>(R + "Models/" + model + "_Albedo.png").width };
                reports.Add(check);
                Debug.Log("[Vanguard] IMPORT_OK " + model + " LOD=" + string.Join("/", triangles) + " size=" + check.dimensions);
                if (!string.IsNullOrEmpty(directory))
                    for (int level = 0; level < 3; level++) Preview(visual, Path.Combine(directory, model + "_LOD" + level + ".png"), level);
                UnityEngine.Object.DestroyImmediate(wrapper);
            }
            string reportPath = Path.Combine(string.IsNullOrEmpty(directory) ? Environment.GetEnvironmentVariable("VG_OUT") : directory, "UnityImportReport.json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(new ImportReport { models = reports.ToArray() }, true));
        }

        static void Preview(GameObject visual, string path, int level)
        {
            foreach (var group in visual.GetComponentsInChildren<LODGroup>(true)) group.ForceLOD(level);
            Bounds b = VisualBounds(visual);
            var cameraObject = new GameObject("Vanguard preview camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.aspect = 1280f / 800f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.075f, 0.095f, 0.12f);
            camera.transform.position = b.center + new Vector3(1.0f, 0.65f, 1.0f).normalized * (b.extents.magnitude * 4f + 1f);
            camera.transform.LookAt(b.center);
            float halfWidth = 0f, halfHeight = 0f;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3(x, y, z));
                        Vector3 view = camera.transform.InverseTransformPoint(corner);
                        halfWidth = Mathf.Max(halfWidth, Mathf.Abs(view.x));
                        halfHeight = Mathf.Max(halfHeight, Mathf.Abs(view.y));
                    }
            camera.orthographicSize = Mathf.Max(halfHeight, halfWidth / camera.aspect) * 1.10f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 200f;
            var keyObject = new GameObject("Vanguard preview key");
            var key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 2f;
            key.color = new Color(1f, 0.94f, 0.84f);
            key.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            var fillObject = new GameObject("Vanguard preview fill");
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.65f;
            fill.color = new Color(0.55f, 0.70f, 1f);
            fill.transform.rotation = Quaternion.Euler(25f, 145f, 0f);
            var target = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            target.Create();
            camera.targetTexture = target;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            RenderTexture.active = previous;
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(keyObject);
            UnityEngine.Object.DestroyImmediate(fillObject);
            Debug.Log("[Vanguard] PREVIEW_OK " + Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ weapons

        static WeaponInfo Missile(Spec spec, Dictionary<string, Material> materials)
        {
            var info = Copy<WeaponInfo>("info_CruiseMissile1", "WI_" + spec.Key);
            var def = Copy<MissileDefinition>("CruiseMissile1", "Def_" + spec.Key);
            Edit(info, s =>
            {
                P(s, "weaponName").stringValue = spec.Name;
                P(s, "shortName").stringValue = spec.Short;
                P(s, "description").stringValue = spec.Description;
                P(s, "massPerRound").floatValue = spec.Mass;
                P(s, "costPerRound").floatValue = spec.Cost;
                P(s, "blastDamage").floatValue = spec.Yield;
                P(s, "pierceDamage").floatValue = spec.Pierce;
                P(s, "nuclear").boolValue = false;
                P(s, "targetRequirements.minRange").floatValue = 0;
                P(s, "targetRequirements.maxRange").floatValue = spec.Key == "VG_HawcX" ? 600000 : 150000;
                P(s, "targetRequirements.minAlignment").floatValue = 180;
                P(s, "targetRequirements.lineOfSight").boolValue = false;
                // The AEGIS dart is never seen on a rack; the loadout shows its pod with darts.
                P(s, "weaponIcon").objectReferenceValue = Icon(spec.Key == "VG_AegisDart" ? "AegisPod" : spec.Key == "VG_AleX" ? "AleXPod" : spec.Model);
            });
            Edit(def, s =>
            {
                P(s, "jsonKey").stringValue = spec.Key;
                P(s, "unitName").stringValue = spec.Name;
                P(s, "description").stringValue = spec.Description;
                P(s, "mass").floatValue = spec.Mass;
                P(s, "value").floatValue = spec.Value;
                P(s, "radarSize").floatValue = spec.RadarSize;
                P(s, "disabled").boolValue = false;
            });

            var g = Clone(D + "GameObject/CruiseMissile1_PLACEHOLDER.prefab");
            g.name = spec.Key;
            HideRenderers(g);
            var visual = Visual(g.transform, spec.Model, materials);
            if (spec.Model == "Glaive")
            {
                var canopy = Visual(g.transform, "GlaiveCanopy", materials);
                canopy.name = "GlaiveCanopy";
                canopy.SetActive(false);
                AddGlaiveCannon(g, spec, visual);
            }
            var missile = g.GetComponent<Missile>();
            if (spec.Key == "VG_AegisDart")
            {
                // The 900 kg donor collider must not make a 90 cm dart hit like a cruise missile.
                foreach (var collider in g.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                var dartCollider = g.AddComponent<BoxCollider>();
                var bounds = VisualBounds(visual);
                dartCollider.center = g.transform.InverseTransformPoint(bounds.center);
                dartCollider.size = bounds.size;
            }
            Edit(missile, s =>
            {
                P(s, "info").objectReferenceValue = info;
                P(s, "definition").objectReferenceValue = def;
                P(s, "mass").floatValue = spec.Mass;
                P(s, "blastYield").floatValue = spec.Yield;
                P(s, "pierceDamage").floatValue = spec.Pierce;
                P(s, "gLimit").floatValue = spec.GLimit;
                P(s, "maxTurnRate").floatValue = spec.TurnRate;
                // Donor aero is a 900 kg cruise missile's (finArea 3). finArea is lift as well as drag, so winged bodies
                // keep it; only the AEGIS dart (a rocket that turns on thrust and torque) gets area ~ mass^(2/3).
                if (spec.Key == "VG_AegisDart") P(s, "finArea").floatValue *= Mathf.Pow(spec.Mass / 900f, 2f / 3f);
                if (spec.Model == "Glaive") P(s, "finArea").floatValue = 1.6f;
                if (spec.Key == "VG_HawcX") P(s, "supersonicDrag").floatValue = 0.35f; // waverider: holds Mach 8 in the glide
                P(s, "foldingFins").arraySize = 0;
                var motor = P(s, "motors").GetArrayElementAtIndex(0);
                motor.FindPropertyRelative("thrust").floatValue = spec.Thrust;
                motor.FindPropertyRelative("burnTime").floatValue = spec.BurnTime;
                // AEGIS darts cold-launch: they fall clear unpowered while VanguardFlight slews them.
                if (spec.Key == "VG_AegisDart") motor.FindPropertyRelative("delayTimer").floatValue = .34f;
                // Motor.Thrust burns fuelMass off the rigidbody mass. The CruiseMissile1 donor's 400 kg would drive the
                // light Vanguard bodies (25 kg dart, 140 kg MALD) negative and blow up their physics: cap fuel at 40 %
                // of the airframe, none for unpowered bodies (ALE-X).
                var fuel = motor.FindPropertyRelative("fuelMass");
                fuel.floatValue = spec.Thrust <= 0f ? 0f : Mathf.Min(fuel.floatValue, spec.Mass * 0.4f);
            });
            var seeker = g.GetComponent<OpticalSeekerCruiseMissile>();
            if (!seeker) throw new Exception("CruiseMissile1 donor lost its OpticalSeekerCruiseMissile");
            // Decoys, drones and darts must not trip the enemy's missile warning; HAWC-X should.
            seeker.triggerMissileWarning = spec.Key == "VG_HawcX" || spec.Key == "VG_Orca";
            EditorUtility.SetDirty(seeker);

            var b = VisualBounds(visual);
            Edit(def, s =>
            {
                P(s, "length").floatValue = b.size.z;
                P(s, "width").floatValue = b.size.x;
                P(s, "height").floatValue = b.size.y;
            });
            PrefabUtility.SaveAsPrefabAsset(g, R + spec.Key + ".prefab");
            UnityEngine.Object.DestroyImmediate(g);
            var prefab = Load<GameObject>(R + spec.Key + ".prefab");
            Edit(info, s => P(s, "weaponPrefab").objectReferenceValue = prefab);
            Edit(def, s => P(s, "unitPrefab").objectReferenceValue = prefab);
            return info;
        }

        static void Mount(Spec spec, WeaponInfo info, string donor, Dictionary<string, Material> materials)
        {
            string json = spec.Key + "_" + donor;
            var mount = Copy<WeaponMount>(donor, "WM_" + json);
            var g = Clone(D + "GameObject/" + donor + "_PLACEHOLDER.prefab");
            g.name = json;
            HideRenderers(g);
            // Keep the donor rack hardware visible; only its ammunition is replaced.
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
                if (!r.GetComponentInParent<MountedMissile>(true)) r.enabled = true;
            foreach (var station in g.GetComponentsInChildren<MountedMissile>(true))
            {
                StowWings(Visual(station.transform, spec.Model, materials), station.transform);
                Edit(station, s => P(s, "info").objectReferenceValue = info);
            }
            SaveMount(g, mount, json, info, donor);
        }

        // AEGIS-3: three darts ride visibly under the pod and eject straight down (MountedMissile rail
        // Down), so remaining ammo reads at a glance. Offsets mirror the cradles in BuildVanguard.py.
        static void AddGlaiveCannon(GameObject missile, Spec spec, GameObject visual)
        {
            bool suppression = spec.Key == "VG_Glaive2S";
            var info = Copy<WeaponInfo>("Gun57mm_Pod", "WI_VG_GlaiveCannon_" + (suppression ? "S" : "A"));
            Edit(info, s =>
            {
                P(s, "weaponName").stringValue = suppression ? "GLAIVE 30 mm HE" : "GLAIVE 30 mm AP";
                P(s, "shortName").stringValue = "GLAIVE GUN";
                P(s, "description").stringValue = "Host-controlled stabilized airborne turret cannon.";
                P(s, "muzzleVelocity").floatValue = 950f;
                P(s, "pierceDamage").floatValue = suppression ? 45f : 150f;
                P(s, "blastDamage").floatValue = suppression ? 3f : .3f;
                P(s, "armorTierEffectiveness").floatValue = 4f;
                P(s, "massPerRound").floatValue = .37f;
                P(s, "costPerRound").floatValue = .001f;
                P(s, "fireInterval").floatValue = 1f / 9f;
                P(s, "weaponIcon").objectReferenceValue = Icon("Glaive");
            });
            var donor = Clone(D + "GameObject/gun_30mm_rotary_pod_PLACEHOLDER.prefab");
            var gun = donor.GetComponentsInChildren<Gun>(true).Single();
            var pitch = visual.GetComponentsInChildren<Transform>(true).Single(t => t.name == "GunPitch");
            gun.transform.SetParent(pitch, false);
            if (gun.gameObject != donor) UnityEngine.Object.DestroyImmediate(donor);
            gun.gameObject.name = "GlaiveCannon";
            gun.transform.localPosition = Vector3.zero;
            gun.transform.localRotation = Quaternion.identity;
            foreach (var renderer in gun.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            Edit(gun, s =>
            {
                P(s, "info").objectReferenceValue = info;
                P(s, "ForceServerAuthority").boolValue = true;
                P(s, "heatEnabled").boolValue = false;
                P(s, "guidedProjectile").objectReferenceValue = null;
                P(s, "magazineCapacity").intValue = 240;
                P(s, "magazines").intValue = 0;
                P(s, "startLoaded").boolValue = true;
                P(s, "fireRate").floatValue = 540f;
                P(s, "recoilImpulse").floatValue = 0f;
            });
        }

        static readonly Vector3[] AegisCells =
            { new Vector3(0f, -0.54f, -.38f), new Vector3(0f, -0.54f, 0f), new Vector3(0f, -0.54f, .38f) };

        static void AegisPod(WeaponInfo info, Dictionary<string, Material> materials)
        {
            const string donor = "AGM_heavy_single";
            const string json = "VG_Aegis_Pod";
            var mount = Copy<WeaponMount>(donor, "WM_" + json);
            var g = Clone(D + "GameObject/" + donor + "_PLACEHOLDER.prefab");
            g.name = json;
            HideRenderers(g);
            var first = g.GetComponentsInChildren<MountedMissile>(true).Single();
            Vector3 home = first.transform.localPosition;
            Visual(first.transform.parent, "AegisPod", materials).transform.localPosition = home;
            // Copy clean donor cells before dressing any one of them. Copying a dressed
            // first cell duplicated its VanguardVisual on all subsequent rounds.
            var cells = new MountedMissile[AegisCells.Length];
            cells[0] = first;
            for (int i = 1; i < cells.Length; i++) cells[i] = UnityEngine.Object.Instantiate(first, first.transform.parent);
            for (int i = 0; i < AegisCells.Length; i++)
            {
                var cell = cells[i];
                cell.name = "AegisCell" + i;
                cell.transform.localPosition = home + AegisCells[i];
                cell.transform.localRotation = Quaternion.Euler(-90f,0f,0f); // Upright darts drop backward along their own axis.
                Visual(cell.transform, "AegisInterceptor", materials);
                Edit(cell, s =>
                {
                    P(s, "info").objectReferenceValue = info;
                    P(s, "railDirection").enumValueIndex = 4; // Backward: nose-up mount ejects toward the ground.
                    P(s, "railLength").floatValue = 0.35f;
                    P(s, "railSpeed").floatValue = 4f;
                    P(s, "railDelay").floatValue = 0f;
                });
            }
            Edit(mount, s => P(s, "ammo").intValue = AegisCells.Length);
            // Compact enough for the self-protection stations that carry flare and ECM pods.
            SaveMount(g, mount, json, info, donor, "SpecialFlarePod", "ECMPod1");
        }

        // ALE-X: two decoys in aft tubes, released backward onto the fiber. = BuildVanguard.py ALEX_CELLS.
        static readonly Vector3[] AleXCells = { new Vector3(0.06f, 0f, -0.7f), new Vector3(-0.06f, 0f, -0.7f) };

        static void AleXPod(WeaponInfo info, Dictionary<string, Material> materials)
        {
            const string donor = "AGM_heavy_single";
            const string json = "VG_AleX_Pod";
            var mount = Copy<WeaponMount>(donor, "WM_" + json);
            var g = Clone(D + "GameObject/" + donor + "_PLACEHOLDER.prefab");
            g.name = json;
            HideRenderers(g);
            var first = g.GetComponentsInChildren<MountedMissile>(true).Single();
            Vector3 home = first.transform.localPosition;
            Visual(first.transform.parent, "AleXPod", materials).transform.localPosition = home;
            for (int i = 0; i < AleXCells.Length; i++)
            {
                var cell = i == 0 ? first : UnityEngine.Object.Instantiate(first, first.transform.parent);
                cell.name = "AleXCell" + i;
                cell.transform.localPosition = home + AleXCells[i];
                cell.transform.localRotation = Quaternion.identity;
                Edit(cell, s =>
                {
                    P(s, "info").objectReferenceValue = info;
                    P(s, "railDirection").enumValueIndex = 4; // Backward
                    P(s, "railLength").floatValue = 0.8f;
                    P(s, "railSpeed").floatValue = 6f;
                    P(s, "railDelay").floatValue = 0f;
                });
            }
            Edit(mount, s => P(s, "ammo").intValue = AleXCells.Length);
            SaveMount(g, mount, json, info, donor, "SpecialFlarePod", "ECMPod1");
        }

        // SKYWELL: cargo-hold refuel/rearm kit on the FuelContainer1x1 cargo mount (a MountedCargo). Firing it never
        // drops anything: SkywellFirePatch toggles the kit. The articulated visual keeps its joint names (runtime API).
        static void SkywellKit(Dictionary<string, Material> materials)
        {
            const string donor = "FuelContainer1x1";
            const string json = "VG_Skywell_Kit";
            var info = Copy<WeaponInfo>("FuelContainer1_info", "WI_VG_Skywell");
            Edit(info, s =>
            {
                P(s, "weaponName").stringValue = "SKYWELL Refuel/Rearm Kit";
                P(s, "shortName").stringValue = "SKYWELL";
                P(s, "description").stringValue = "Roll-on service-glider kit. Fire to open the ramp and launch a tethered, folding-wing service glider. Friendly pilots close astern and HOLD BRAKE: the glider refuels them through its tail boom and loads missiles onto empty pylons from an 8 t fuel / 1.5 t munitions stock. Fire again to recover and stow.";
                P(s, "weaponIcon").objectReferenceValue = Icon("SkywellKit");
            });
            var mount = Copy<WeaponMount>(donor, "WM_" + json);
            var g = Clone(D + "GameObject/" + donor + "_PLACEHOLDER.prefab");
            g.name = json;
            HideRenderers(g);
            var station = g.GetComponentsInChildren<MountedCargo>(true).First();
            Visual(station.transform.parent, "SkywellKit", materials).transform.localPosition = station.transform.localPosition;
            Edit(station, s => P(s, "info").objectReferenceValue = info);
            Edit(mount, s => P(s, "ammo").intValue = 1);
            SaveMount(g, mount, json, info, donor);
            // Third-party Aryx MC-260 Chimera cargo bays (as AirborneBuilder); Blueprinter only warns if it is absent.
            // Carriers are exactly the VL-49 Tarantula's cargo / mission bays and the Aryx Chimera (not the donor's Ibis).
            var op = Load<OpAddWeaponToHardpoint>(R + "Op_" + json + ".asset");
            op.aircraft.Clear();
            var tarantula = Load<AircraftDefinition>(D + "MonoBehaviour/QuadVTOL1_PLACEHOLDER.asset");
            var sets = tarantula.unitPrefab.GetComponentInChildren<WeaponManager>(true).hardpointSets;
            var bays = Enumerable.Range(0, sets.Length).Where(i => sets[i].name.IndexOf("Cargo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                sets[i].name.IndexOf("Mission Bay", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (bays.Count == 0) throw new Exception("Tarantula has no cargo bay");
            op.aircraft.Add(new OpAddWeaponToHardpoint.AircraftTarget { aircraftJsonKey = tarantula.jsonKey, hardpointIndices = bays });
            if (!op.aircraft.Any(a => a.aircraftJsonKey == "Aryx_CargoPlane1"))
                op.aircraft.Add(new OpAddWeaponToHardpoint.AircraftTarget { aircraftJsonKey = "Aryx_CargoPlane1", hardpointIndices = new List<int> { 1, 2, 3 } });
            EditorUtility.SetDirty(op);
            Debug.Log("[Vanguard] SKYWELL carriers: " + string.Join(", ", op.aircraft.Select(a => a.aircraftJsonKey + ":" + string.Join("/", a.hardpointIndices))));
        }

        static void Lance(Dictionary<string, Material> materials)
        {
            const string donor = "gun_57mm_pod";
            const string json = "VG_Lance_Pod";
            var info = Copy<WeaponInfo>("Gun57mm_Pod", "WI_VG_Lance");
            Edit(info, s =>
            {
                P(s, "weaponName").stringValue = "RG-12 LANCE Railgun";
                P(s, "shortName").stringValue = "LANCE";
                P(s, "description").stringValue = "Electromagnetic railgun pod. HOLD the trigger to charge the capacitor bank and release a 3.4 km/s full-power slug; taps fire weak snap shots. Twelve rounds; the bank recharges between shots.";
                P(s, "muzzleVelocity").floatValue = 3000;
                P(s, "pierceDamage").floatValue = 4000;
                P(s, "blastDamage").floatValue = 25;
                P(s, "dragCoef").floatValue = 0.05f;
                P(s, "costPerRound").floatValue = 0.02f;
                P(s, "weaponIcon").objectReferenceValue = Icon("Lance");
            });
            var mount = Copy<WeaponMount>(donor, "WM_" + json);
            var g = Clone(D + "GameObject/" + donor + "_PLACEHOLDER.prefab");
            g.name = json;
            HideRenderers(g);
            Visual(g.transform, "Lance", materials);
            foreach (var gun in g.GetComponentsInChildren<Gun>(true))
                Edit(gun, s =>
                {
                    // Fire from the model's lit bore, not the donor 57 mm barrel. = BuildVanguard.py lance() muzzle.
                    var muzzles = P(s, "muzzles");
                    for (int i = 0; i < muzzles.arraySize; i++)
                        if (muzzles.GetArrayElementAtIndex(i).objectReferenceValue is Transform t)
                            t.position = g.transform.TransformPoint(new Vector3(0f, 0f, 2.62f));
                    P(s, "info").objectReferenceValue = info;
                    P(s, "fireRate").floatValue = 30;
                    P(s, "magazineCapacity").intValue = 12;
                    P(s, "tracerColor").colorValue = new Color(0.82f, 0.90f, 1f);
                    P(s, "muzzleParticles").arraySize = 0; // Electromagnetic exit: no powder-gun flame plume.
                    P(s, "tracerRatio").intValue = 1;
                    P(s, "tracerSize").floatValue = 3f;
                });
            Edit(mount, s => P(s, "ammo").intValue = 12);
            // Any station that takes a gun pod or a single heavy AGM can hang the pod.
            SaveMount(g, mount, json, info, donor, "gun_30mm_rotary_pod", "AGM_heavy_single");
        }

        static void SaveMount(GameObject g, WeaponMount mount, string json, WeaponInfo info, params string[] donors)
        {
            PrefabUtility.SaveAsPrefabAsset(g, R + json + ".prefab");
            UnityEngine.Object.DestroyImmediate(g);
            Edit(mount, s =>
            {
                P(s, "jsonKey").stringValue = json;
                P(s, "mountName").stringValue = info.weaponName + (mount.ammo > 1 ? " x" + mount.ammo : "");
                P(s, "info").objectReferenceValue = info;
                P(s, "prefab").objectReferenceValue = Load<GameObject>(R + json + ".prefab");
                P(s, "mass").floatValue = info.massPerRound * Mathf.Max(1, mount.ammo);
                P(s, "disabled").boolValue = false;
            });
            Carriers(json, donors);
        }

        // Every aircraft station that may carry the donor rack may carry ours.
        static void Carriers(string json, string[] donors)
        {
            var op = ScriptableObject.CreateInstance<OpAddWeaponToHardpoint>();
            op.weaponJsonKey = json;
            var keys = new HashSet<string>(donors.Select(d => Load<WeaponMount>(D + "MonoBehaviour/" + d + "_PLACEHOLDER.asset").jsonKey));
            foreach (string path in Directory.GetFiles(D + "MonoBehaviour", "*_PLACEHOLDER.asset"))
            {
                var aircraft = AssetDatabase.LoadAssetAtPath<AircraftDefinition>(path.Replace('\\', '/'));
                if (!aircraft || !aircraft.unitPrefab) continue;
                var manager = aircraft.unitPrefab.GetComponentInChildren<WeaponManager>(true);
                if (!manager) continue;
                var indices = new List<int>();
                for (int i = 0; i < manager.hardpointSets.Length; i++)
                    if (manager.hardpointSets[i].weaponOptions.Any(w => w && keys.Contains(w.jsonKey))) indices.Add(i);
                if (indices.Count > 0)
                    op.aircraft.Add(new OpAddWeaponToHardpoint.AircraftTarget { aircraftJsonKey = aircraft.jsonKey, hardpointIndices = indices });
            }
            if (op.aircraft.Count == 0) throw new Exception("no carrier offers " + string.Join("/", donors));
            Debug.Log("[Vanguard] " + json + " -> " + string.Join(", ", op.aircraft.Select(a => a.aircraftJsonKey + ":" + string.Join("/", a.hardpointIndices))));
            op.name = "Op_" + json;
            string opPath = R + "Op_" + json + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<OpAddWeaponToHardpoint>(opPath);
            if (old)
            {
                EditorUtility.CopySerialized(op, old);
                UnityEngine.Object.DestroyImmediate(op);
            }
            else AssetDatabase.CreateAsset(op, opPath);
        }

        static void Build()
        {
            string outDir = Environment.GetEnvironmentVariable("VG_OUT") ?? throw new Exception("VG_OUT not set");
            string delivery = Path.GetFullPath("Delivery~");
            Directory.CreateDirectory(delivery);
            var started = DateTime.UtcNow;
            ModBuilder.Build("Vanguard", "Vanguard", Version, delivery);
            string built = Path.Combine(delivery, "Vanguard_" + Version + ".nobp");
            if (!File.Exists(built) || File.GetLastWriteTimeUtc(built) < started.AddSeconds(-1))
                throw new Exception("fresh bundle was not produced");
            File.Copy(built, Path.Combine(outDir, "Vanguard.nobp"), true);
            Debug.Log("[Vanguard] BUNDLE_OK " + new FileInfo(built).Length + " bytes");
        }
    }
}
