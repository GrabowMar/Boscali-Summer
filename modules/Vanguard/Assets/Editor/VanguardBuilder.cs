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
    public static partial class VanguardBuilder
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
            new Spec { Key = "VG_MaldX", Name = "MALD-X BAIT", Short = "BAIT", Model = "MaldX",
                Description = "Loud fighter-signature decoy. Weaves toward your designated or locked target at 230 m/s and loiters over it for five minutes. Enemy radar sites that hold it for 3 s are EXPOSED to your side for 60 s; radar missiles chasing friendly aircraft within 4 km may switch to it.",
                Mass = 140, Yield = 1, Cost = 0.3f, Value = 90, RadarSize = 3.0f, Thrust = 900, BurnTime = 600, GLimit = 6, TurnRate = 12,
                Racks = new[] { "AGM_heavy_single", "AGM_heavy_triple", "CruiseMissile1_internalx2" } },
            new Spec { Key = "VG_MaldJ", Name = "ADM-160J MALD-J", Short = "MALD-J", Model = "MaldJ",
                Description = "SHADOW stand-in jammer. Raises its emitter mast and flies straight at 200 m/s toward the locked target (else ahead) for four minutes, projecting a 55 degree jam lobe out to 35 km. Enemy radars in the lobe cannot see friendly aircraft hiding within 12 degrees of the jammer's line, until they close inside 9 km burn-through.",
                Mass = 150, Yield = 1, Cost = 0.6f, Value = 110, RadarSize = 0.6f, Thrust = 950, BurnTime = 240, GLimit = 6, TurnRate = 12,
                Racks = new[] { "AGM_heavy_single", "AGM_heavy_triple", "CruiseMissile1_internalx2" } },
            new Spec { Key = "VG_Remora", Name = "XQ-58V REMORA", Short = "REMORA", Model = "Remora",
                Description = "ARCHER loyal-wingman drone with two mini radar missiles on underwing rails. Holds a wing slot and draws radar-guided missiles. VANGUARD: DESIGNATE your target and it turns in and fires both rounds inside 12 km, then rejoins; designate again when empty and it rams with its 60 kg warhead. Six-minute endurance.",
                Mass = 450, Yield = 60, Pierce = 300, Cost = 0.8f, Value = 40, RadarSize = 0.15f, Thrust = 4200, BurnTime = 360, GLimit = 9, TurnRate = 25,
                Racks = new[] { "AGM_heavy_single", "CruiseMissile1_internalx2" } },
            new Spec { Key = "VG_HawcX", Name = "HAWC-X Hypersonic Glide Vehicle", Short = "HAWC-X", Model = "HawcX",
                Description = "Boost-glide strike weapon. Climbs to 25 km at Mach 8, skips and weaves along the edge of space, then dives near-vertically onto the target.",
                Mass = 1500, Yield = 150, Pierce = 2500, Cost = 4f, Value = 30, RadarSize = 0.003f, Thrust = 180000, BurnTime = 30, GLimit = 25, TurnRate = 18,
                Racks = new[] { "AGM_heavy_single", "CruiseMissile1_internalx2" } },
            new Spec { Key = "VG_AegisDart", Name = "AIM-X AEGIS", Short = "AEGIS", Model = "AegisInterceptor",
                Description = "Low-profile three-dart hit-to-kill pod. Automatic: darts drop clear, pitch onto a missile locked on you and ignite; all-direction protection inside 1.1 km. SNAP: tap the station with a locked aircraft, helicopter or drone inside 2.5 km and 70 deg of the nose for a double-damage dart. The last dart stays with the defence while a missile is inbound.",
                Mass = 25, Yield = .2f, Pierce = 50, Cost = 0.15f, Value = 2, RadarSize = 0.0005f, Thrust = 9000, BurnTime = 2.5f, GLimit = 60, TurnRate = 55,
                Racks = new string[0] }, // AEGIS gets its own three-dart pod, see AegisPod()
            new Spec { Key = "VG_Glaive2A", Name = "GLAIVE-2A SENTINEL", Short = "GLAIVE-A", Model = "Glaive2A",
                Description = SentinelDescription,
                Mass = 80, Yield = 0, Cost = 1.2f, Value = 25, RadarSize = .12f, Thrust = 1600, BurnTime = 3, GLimit = 4, TurnRate = 30,
                Racks = new[] { "AGM_heavy_single", "CruiseMissile1_internalx2" } },
            new Spec { Key = "VG_Glaive2S", Name = "GLAIVE-2S SPECTRE Gunship Drone", Short = "SPECTRE", Model = "Glaive2S",
                Description = "Folding-wing gunship drone. Launch boost, 90 m/s ingress, then a left-hand pylon turn 450 m around the target and 350 m above it while its port-side 30 mm HE cannon fires short bursts: 240 rounds, 150 s on station. Designate a new ground target to HAND-OFF: the orbit slides to it.",
                Mass = 80, Yield = 0, Cost = 1.6f, Value = 28, RadarSize = .12f, Thrust = 1600, BurnTime = 3, GLimit = 4, TurnRate = 30,
                Racks = new[] { "AGM_heavy_single", "CruiseMissile1_internalx2" } },
            new Spec { Key = "VG_Orca", Name = "AGT-80 ORCA Glide Torpedo", Short = "ORCA", Model = "Orca",
                Description = "Glide torpedo with a clip-on wing kit. Locked on a ship: glides in, sheds the kit at the splash 5 km out and runs at 90 m/s, 6 m deep, into the hull; a hard turn in the last 300 m can beat it. No ship locked: lays a SLEEPER 3-12 km ahead (farther from higher up) that sinks to 35 m and waits up to 10 minutes; the first hostile hull inside 1.2 km wakes it for a rising run. Friendly ships never wake it.",
                Mass = 600, Yield = 450, Pierce = 600, Cost = 1.5f, Value = 30, RadarSize = 0.05f, Thrust = 1500, BurnTime = 300, GLimit = 8, TurnRate = 20,
                Racks = new[] { "AGM_heavy_single", "AGM_heavy_triple" } },
            new Spec { Key = "VG_AleX", Name = "ALE-X Towed Decoy", Short = "ALE-X", Model = "AleX",
                Description = "Fiber-towed decoy on an automatic reel: 60 m of line, 150 m or 250 m once a radar missile tracks you. SUPPRESS slows turret locks, DEFLECT bends missile aimpoints toward it, SEDUCE takes missiles over. Fire again to CUT it loose: it keeps them for 20 s. The fiber snaps above 7 g or below 50 m.",
                Mass = 20, Yield = 2, Cost = 0.05f, Value = 3, RadarSize = 1.5f, Thrust = 0, BurnTime = 1, GLimit = 30, TurnRate = 200,
                Racks = new string[0] }, // ALE-X gets its own two-decoy pod, see AleXPod()
        };

        public static void Run()
        {
            ImportModels();
            var materials = Materials();
            var infos = new Dictionary<string, WeaponInfo>();
            foreach (Spec spec in Missiles) infos[spec.Key] = Missile(spec, materials);
            MaldxIdentity(infos["VG_MaldX"]);
            foreach (Spec spec in Missiles)
                foreach (string rack in spec.Racks) Mount(spec, infos[spec.Key], rack, materials);
            Glaive2S(); // VanguardBuilder.glaive2s.cs
            AegisPod(infos["VG_AegisDart"], materials);
            AegisSnapSetup(infos["VG_AegisDart"]);
            AleXPod(infos["VG_AleX"], materials);
            AlexDress(materials);
            SkywellKit(materials);
            Lance(materials);
            AssetDatabase.SaveAssets();
            ValidateOperationalPrefabs();
            ValidateOrca();
            ValidateRemoraRig();
            ValidateMaldJ();
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
            {
                if (cell.transform.Cast<Transform>().Count(t => t.name == "VanguardVisual") != 1)
                    throw new Exception("AEGIS cell contains duplicate dart visuals: " + cell.name);
                if (Vector3.Dot(cell.transform.forward,aegis.transform.forward) < .99f ||
                    new SerializedObject(cell).FindProperty("railDirection").enumValueIndex != 1)
                    throw new Exception("AEGIS must carry horizontally and eject downward");
            }
            var carriage = aegis.GetComponentsInChildren<MeshRenderer>(true).Where(r => LodLevel(r.name) == 0 &&
                r.GetComponentsInParent<Transform>(true).Any(t => t.name == "VanguardVisual")).ToArray();
            var fitted = carriage[0].bounds;
            foreach (var renderer in carriage.Skip(1)) fitted.Encapsulate(renderer.bounds);
            if (fitted.size.y > .4f || fitted.size.x > .5f)
                throw new Exception("AEGIS carriage exceeds the low-profile pylon envelope: " + fitted.size);
            Debug.Log("[Vanguard] AEGIS_CARRIAGE_SIZE " + fitted.size);
            ValidateSentinel(); // GLAIVE-2A: VanguardBuilder.glaive2a.cs; GLAIVE-2S validates its rig in Glaive2S()
            Debug.Log("[Vanguard] OPERATIONAL_PREFABS_OK AEGIS=3_unique_cells GLAIVE=launch_assist_orbit_gun_drones");
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

        // Files of the retired model 'Glaive' (stem before the first '.' or '_'); Glaive2A / Glaive2S are different stems.
        static bool Retired(string fileName) => fileName.Split('.', '_')[0] == "Glaive";

        static void ImportModels()
        {
            // These exact assets belong to the retired GLAIVE parachute implementation.
            foreach (string name in new[] { "GlaiveCanopy.fbx", "GlaiveCanopy_Albedo.png", "GlaiveCanopy_Normal.png", "GlaiveCanopy_MetalGloss.png", "GlaiveCanopy_Icon.png" })
                AssetDatabase.DeleteAsset(R + "Models/" + name);
            AssetDatabase.DeleteAsset(R + "Materials/Skin_GlaiveCanopy.mat");
            // 'Glaive' was split into Glaive2A / Glaive2S: drop the old files and material left in the project.
            foreach (string file in Directory.Exists(R + "Models") ? Directory.GetFiles(R + "Models") : new string[0])
                if (!file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && Retired(Path.GetFileName(file)))
                    AssetDatabase.DeleteAsset(file.Replace('\\', '/'));
            AssetDatabase.DeleteAsset(R + "Materials/Skin_Glaive.mat");
            string src = Environment.GetEnvironmentVariable("VG_MODELS") ?? throw new Exception("VG_MODELS not set");
            Directory.CreateDirectory(R + "Models");
            foreach (string file in Directory.GetFiles(src))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("GlaiveCanopy", StringComparison.OrdinalIgnoreCase) || Retired(name)) continue;
                if (name.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_Albedo.png", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_Normal.png", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_MetalGloss.png", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("_Icon.png", StringComparison.OrdinalIgnoreCase))
                    File.Copy(file, R + "Models/" + name, true);
            }
            AssetDatabase.Refresh();
            foreach (string file in Directory.GetFiles(R + "Models"))
            {
                string path = file.Replace('\\', '/');
                if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
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
                else if (path.EndsWith("_Icon.png", StringComparison.OrdinalIgnoreCase))
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
                else if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    var tex = (TextureImporter)AssetImporter.GetAtPath(path);
                    tex.textureType = path.EndsWith("_Normal.png", StringComparison.OrdinalIgnoreCase) ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    tex.sRGBTexture = path.EndsWith("_Albedo.png", StringComparison.OrdinalIgnoreCase);
                    tex.maxTextureSize = 2048; // each models/<Name>.py picks its atlas (512 / 1024 / 2048); ValidateAndPreview checks the import kept it
                    tex.textureCompression = TextureImporterCompression.CompressedHQ;
                    tex.mipmapEnabled = true;
                    tex.anisoLevel = 4;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    // MetalGloss alpha is smoothness; preserve it without treating it as transparency.
                    tex.alphaSource = path.EndsWith("_MetalGloss.png", StringComparison.OrdinalIgnoreCase) ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
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

        // GLAIVE-2A and GLAIVE-2S each own an airframe (models Glaive2A / Glaive2S) but share the gun rig and flight setup.
        static bool IsGlaive(string model) => model.StartsWith("Glaive", StringComparison.Ordinal);

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
                new LOD(0.06f, renderers.Where(r => LodLevel(r.name) == 0).Cast<Renderer>().ToArray()),
                new LOD(0.015f, renderers.Where(r => LodLevel(r.name) == 1).Cast<Renderer>().ToArray()),
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

        // Atlas side in px of the baked Albedo in VG_MODELS: PNG IHDR width/height are big-endian ints at bytes 16 and 20.
        static int SourceAtlasSize(string model)
        {
            string path = Path.Combine(Environment.GetEnvironmentVariable("VG_MODELS") ?? throw new Exception("VG_MODELS not set"), model + "_Albedo.png");
            var header = new byte[24];
            using (var stream = File.OpenRead(path))
                if (stream.Read(header, 0, 24) != 24) throw new Exception(path + " is not a PNG");
            int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            if (header[1] != 'P' || width != height || width < 256 || width > 2048) // 2048 = ImportModels maxTextureSize
                throw new Exception(model + " Albedo must be a square PNG of 256..2048 px, got " + width + "x" + height);
            return width;
        }

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
            // Every Spec model (MaldX, MaldJ, Remora, HawcX, AegisInterceptor, Glaive2A, Glaive2S, Orca, AleX) plus the bespoke pods.
            var models = Missiles.Select(s => s.Model).Concat(new[] { "AegisPod", "AleXPod", "SkywellKit", "Lance" }).Distinct().ToArray();
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
                int atlas = SourceAtlasSize(model); // the side BuildVanguard.py baked (models/<Name>.py TEX), not a copy of it
                foreach (string channel in new[] { "Albedo", "Normal", "MetalGloss" })
                {
                    var texture = Load<Texture2D>(R + "Models/" + model + "_" + channel + ".png");
                    if (texture.width != atlas || texture.height != atlas) throw new Exception(model + " texture density: " + channel);
                }
                if (model == "SkywellKit") ValidateSkywellKit(visual); // VanguardBuilder.skywell.cs
                if (model == "MaldX" || model == "Glaive2S" || model == "Orca") // MaldJ: ValidateMaldJ; Glaive2A: ValidateSentinel
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
            if (spec.Key == "VG_Glaive2A") AddSentinelGun(visual);
            else if (IsGlaive(spec.Model))
            {
                AddGlaiveCannon(g, spec, visual);
            }
            var missile = g.GetComponent<Missile>();
            if (spec.Key == "VG_AegisDart" || IsGlaive(spec.Model))
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
                if (IsGlaive(spec.Model)) P(s, "finArea").floatValue = 1.2f;
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
                if (IsGlaive(spec.Model))
                {
                    var stages=P(s,"motors");
                    stages.arraySize=2;
                    for (int i=0;i<2;i++)
                    {
                        var stage=stages.GetArrayElementAtIndex(i);
                        stage.FindPropertyRelative("thrust").floatValue=i==0 ? 1600f : 600f;
                        stage.FindPropertyRelative("burnTime").floatValue=i==0 ? 3f : 210f;
                        stage.FindPropertyRelative("fuelMass").floatValue=i==0 ? 3f : 8f;
                        stage.FindPropertyRelative("delayTimer").floatValue=i==0 ? .20f : 0f;
                        // Propeller cruise must not inherit the launch motor's flame, trail or rocket sound.
                        if (i==1)
                        {
                            foreach (string field in new[] { "particleSystems", "trailEmitters", "audioSources", "lights", "destructEffects" })
                                stage.FindPropertyRelative(field).arraySize=0;
                            stage.FindPropertyRelative("startupSource").objectReferenceValue=null;
                            stage.FindPropertyRelative("IR_intensity").floatValue=.08f;
                        }
                    }
                }
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
                GameObject rackVisual = Visual(station.transform, spec.Model, materials);
                if (spec.Model != "MaldJ") StowWings(rackVisual, station.transform); // the MaldJ FBX already holds the stowed pose
                Edit(station, s => P(s, "info").objectReferenceValue = info);
            }
            SaveMount(g, mount, json, info, donor);
        }

        // AEGIS-3: three darts ride visibly under the pod and eject straight down (MountedMissile rail
        // Down), so remaining ammo reads at a glance. Offsets mirror CELLS in models/AegisPod.py.
        static void AddGlaiveCannon(GameObject missile, Spec spec, GameObject visual)
        {
            bool suppression = spec.Key == "VG_Glaive2S";
            var info = Copy<WeaponInfo>("Gun57mm_Pod", "WI_VG_GlaiveCannon_" + (suppression ? "S" : "A"));
            Edit(info, s =>
            {
                P(s, "weaponName").stringValue = suppression ? "GLAIVE 7.62 mm suppression gun" : "GLAIVE 7.62 mm gun";
                P(s, "shortName").stringValue = "GLAIVE GUN";
                P(s, "description").stringValue = "Light stabilized gun for infantry and exposed positions; finite ammunition and airframe recoil.";
                P(s, "muzzleVelocity").floatValue = 820f;
                P(s, "pierceDamage").floatValue = 6f;
                P(s, "blastDamage").floatValue = 0f;
                P(s, "armorTierEffectiveness").floatValue = 1f;
                P(s, "massPerRound").floatValue = .024f;
                P(s, "costPerRound").floatValue = .001f;
                P(s, "fireInterval").floatValue = .15f;
                P(s, "weaponIcon").objectReferenceValue = Icon(spec.Model);
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
                P(s, "magazineCapacity").intValue = 180;
                P(s, "magazines").intValue = 0;
                P(s, "startLoaded").boolValue = true;
                P(s, "fireRate").floatValue = 400f;
                P(s, "recoilImpulse").floatValue = 0f;
            });
        }

        static readonly Vector3[] AegisCells =
            { new Vector3(-.145f, -.13f, 0f), new Vector3(0f, -.13f, 0f), new Vector3(.145f, -.13f, 0f) };

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
                cell.transform.localRotation = Quaternion.identity;
                Visual(cell.transform, "AegisInterceptor", materials);
                Edit(cell, s =>
                {
                    P(s, "info").objectReferenceValue = info;
                    P(s, "railDirection").enumValueIndex = 1; // Down: compact horizontal carriage.
                    P(s, "railLength").floatValue = .55f; // Clear the aircraft before pitching upright.
                    P(s, "railSpeed").floatValue = 6f;
                    P(s, "railDelay").floatValue = 0f;
                });
            }
            Edit(mount, s => P(s, "ammo").intValue = AegisCells.Length);
            // Compact enough for the self-protection stations that carry flare and ECM pods.
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
                P(s, "weaponName").stringValue = "SKYWELL Pit-Stop Kit";
                P(s, "shortName").stringValue = "SKYWELL";
                P(s, "description").stringValue = "Roll-on service-glider kit. Fire to open the ramp and launch a tethered, folding-wing glider. Friendly pilots close astern and HOLD BRAKE; choose FUEL, MUNITIONS or BOTH (and which pylon goes first) under SUPPORT > PIT STOP. The glider plugs your probe and hot-swaps rounds station by station from an 8 t fuel / 1.5 t munitions stock. Fire again to recover.";
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
