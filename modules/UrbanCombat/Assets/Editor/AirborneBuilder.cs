// Builds Airborne.nobp: the HALO paratrooper mount plus its jumper and canopy models.
//   Unity -batchmode -quit -projectPath <bp> -executeMethod Airborne.AirborneBuilder.Run
// Env: AB_MODELS (AirborneJumper.fbx, AirborneCanopy.fbx from Source/BuildAirborne.py),
//      AB_RIP (AssetRipper ExportedProject/Assets, for the soldier1 textures), AB_OUT (copy target).
// Same recipe as VanguardBuilder: copy donor ScriptableObjects, clone the donor prefab, re-point
// references, one OpAddWeaponToHardpoint. Vanilla components only; the drop itself lives in
// BoscaliSummer.dll (modules/UrbanCombat/Runtime/AirAssaultController.cs) keyed on the jsonKey.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blueprinter;
using UnityEditor;
using UnityEngine;

namespace Airborne
{
    public static class AirborneBuilder
    {
        const string R = "Assets/Blueprinter/Mods/Airborne/";
        const string D = "Assets/Blueprinter/_donotship/";
        const string Version = "0.1.0";
        const string Donor = "Troopsx8_UtilityHelo1_F";
        // Keep in sync with HaloDrop.MountKey in modules/UrbanCombat/Runtime.
        const string MountKey = "AB_Paratroopers_x16";
        const int Capacity = 16;
        static readonly string[] Textures = { "soldier1_b", "soldier1_n", "soldier1_ao" };

        public static void Run()
        {
            var materials = Materials();
            ImportModels(materials);
            var info = Info();
            Mount(info);
            AssetDatabase.SaveAssets();
            OpReferenceIndex.Refresh();
            Build();
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

        // ------------------------------------------------------------------ materials + models

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
            setup(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Action<Material> Plain(Color c, float smooth, bool twoSided = false) => m =>
        {
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smooth);
            // Canopy fabric is a single sheet in places (ribs, slider): show both faces.
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.doubleSidedGI = twoSided;
        };

        static Dictionary<string, Material> Materials()
        {
            string rip = Environment.GetEnvironmentVariable("AB_RIP") ?? throw new Exception("AB_RIP not set");
            Directory.CreateDirectory(R + "Models");
            foreach (string t in Textures)
                File.Copy(Path.Combine(rip, "Texture2D", t + ".png"), R + "Models/" + t + ".png", true);
            AssetDatabase.Refresh();
            foreach (string t in Textures)
            {
                var tex = (TextureImporter)AssetImporter.GetAtPath(R + "Models/" + t + ".png");
                tex.textureType = t.EndsWith("_n") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                tex.sRGBTexture = t.EndsWith("_b");
                tex.maxTextureSize = 1024;
                tex.textureCompression = TextureImporterCompression.CompressedHQ;
                tex.SaveAndReimport();
            }
            return new Dictionary<string, Material>
            {
                // The gun-nest soldier's own textures, so jumpers match the troops on the ground.
                ["AB_Body"] = MakeMaterial("AB_Body", m =>
                {
                    m.SetColor("_BaseColor", Color.white);
                    m.SetTexture("_BaseMap", Load<Texture2D>(R + "Models/soldier1_b.png"));
                    m.SetTexture("_BumpMap", Load<Texture2D>(R + "Models/soldier1_n.png"));
                    m.EnableKeyword("_NORMALMAP");
                    m.SetTexture("_OcclusionMap", Load<Texture2D>(R + "Models/soldier1_ao.png"));
                    m.EnableKeyword("_OCCLUSIONMAP");
                    m.SetFloat("_Metallic", 0f);
                    m.SetFloat("_Smoothness", 0.25f);
                }),
                ["AB_Gear"] = MakeMaterial("AB_Gear", Plain(new Color(0.36f, 0.33f, 0.24f), 0.2f)),
                ["AB_Dark"] = MakeMaterial("AB_Dark", Plain(new Color(0.045f, 0.045f, 0.05f), 0.45f)),
                ["AB_Wing"] = MakeMaterial("AB_Wing", Plain(new Color(0.11f, 0.13f, 0.11f), 0.35f, true)),
                ["AB_Canopy"] = MakeMaterial("AB_Canopy", Plain(new Color(0.27f, 0.3f, 0.2f), 0.15f, true)),
                ["AB_CanopyDark"] = MakeMaterial("AB_CanopyDark", Plain(new Color(0.06f, 0.065f, 0.06f), 0.1f, true)),
                ["AB_Lines"] = MakeMaterial("AB_Lines", Plain(new Color(0.6f, 0.6f, 0.55f), 0.2f)),
            };
        }

        static void ImportModels(Dictionary<string, Material> materials)
        {
            string src = Environment.GetEnvironmentVariable("AB_MODELS") ?? throw new Exception("AB_MODELS not set");
            foreach (string model in new[] { "AirborneJumper", "AirborneTrooper", "AirborneCanopy" })
                File.Copy(Path.Combine(src, model + ".fbx"), R + "Models/" + model + ".fbx", true);
            AssetDatabase.Refresh();
            foreach (string model in new[] { "AirborneJumper", "AirborneTrooper", "AirborneCanopy" })
            {
                string path = R + "Models/" + model + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.globalScale = 1f;
                importer.useFileUnits = false;
                importer.bakeAxisConversion = true;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importBlendShapes = true;
                importer.importBlendShapeNormals = ModelImporterNormals.Calculate;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.animationType = ModelImporterAnimationType.None;
                importer.isReadable = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                foreach (var pair in materials)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
                importer.SaveAndReimport();

                var asset = Load<GameObject>(path);
                var renderer = asset.GetComponentInChildren<Renderer>(true) ?? throw new Exception(model + " has no renderer");
                var mesh = MeshOf(renderer) ?? throw new Exception(model + " has no mesh");
                var shapes = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray();
                if (shapes.Length == 0) throw new Exception(model + " imported without blend shapes (" + renderer.GetType().Name + ")");
                if (renderer.sharedMaterials.Any(m => !materials.ContainsValue(m)))
                    throw new Exception(model + " has an unmapped material: " + string.Join(",", renderer.sharedMaterials.Select(m => m ? m.name : "null")));
                Debug.Log("[Airborne] MODEL_OK " + model + " tris=" + mesh.triangles.Length / 3 + " shapes=" + string.Join(",", shapes) +
                          " bounds=" + mesh.bounds.size.ToString("F2"));
            }
        }

        static Mesh MeshOf(Renderer r) =>
            r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>() is MeshFilter f ? f.sharedMesh : null;

        // A boneless mesh can import with a plain MeshRenderer; only a SkinnedMeshRenderer plays blend shapes.
        static void Skinned(GameObject root)
        {
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = filter.gameObject;
                var plain = go.GetComponent<MeshRenderer>();
                var mesh = filter.sharedMesh;
                var mats = plain ? plain.sharedMaterials : new Material[0];
                UnityEngine.Object.DestroyImmediate(plain);
                UnityEngine.Object.DestroyImmediate(filter);
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = false;
                smr.localBounds = mesh.bounds;
            }
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // Poses and the open canopy reach well past the rest bounds; pad so they never cull early.
                var b = smr.sharedMesh.bounds;
                b.Expand(8f / Mathf.Max(1e-4f, smr.transform.lossyScale.x));
                smr.localBounds = b;
            }
        }

        // ------------------------------------------------------------------ mount

        static WeaponInfo Info()
        {
            var info = Copy<WeaponInfo>("Troopsx8_info", "WI_AB_Paratroopers");
            Edit(info, s =>
            {
                P(s, "weaponName").stringValue = "HALO Paratroopers";
                P(s, "shortName").stringValue = "HALO";
                P(s, "description").stringValue =
                    "Sixteen wingsuit paratroopers. Each release sends a stick out the ramp in fireteams of four that glide up to " +
                    "6 km onto the designated target, open square canopies low and seize the building they land on, or dig in.";
                P(s, "massPerRound").floatValue = 120f;
                P(s, "costPerRound").floatValue = 0.1f;
                // Vanilla troops only fire low and slow; a HALO stick leaves from altitude at cruise.
                P(s, "targetRequirements.maxAltitude").floatValue = 12000f;
                P(s, "targetRequirements.maxSpeed").floatValue = 220f;
                P(s, "targetRequirements.maxRange").floatValue = 8000f;
                P(s, "targetRequirements.minAlignment").floatValue = 180f;
            });
            return info;
        }

        static void Mount(WeaponInfo info)
        {
            var mount = Copy<WeaponMount>(Donor, "WM_" + MountKey);
            var g = UnityEngine.Object.Instantiate(Load<GameObject>(D + "GameObject/" + Donor + "_PLACEHOLDER.prefab"));
            g.name = MountKey;
            var troops = g.GetComponentInChildren<MountedTroops>(true) ?? throw new Exception("donor lost MountedTroops");
            Edit(troops, s =>
            {
                P(s, "info").objectReferenceValue = info;
                P(s, "ammo").intValue = Capacity;
                P(s, "captureStrength").floatValue = Capacity;
                // Weapon.Rearm is a no-op for troops: a rearmable mount only files dead rearm requests.
                P(s, "Rearmable").boolValue = false;
            });
            // Templates the mod instantiates per jumper; inactive so the bay never draws them.
            foreach (string model in new[] { "AirborneJumper", "AirborneTrooper", "AirborneCanopy" })
            {
                var imported = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(R + "Models/" + model + ".fbx"));
                PrefabUtility.UnpackPrefabInstance(imported, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                Skinned(imported);
                // The FBX root carries the unit/axis conversion; the mod drives an identity wrapper.
                var t = new GameObject(model);
                imported.transform.SetParent(t.transform, false);
                var skin = imported.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var mb = skin.sharedMesh.bounds;
                var bounds = GeometryUtility.CalculateBounds(new[] { mb.min, mb.max }, skin.transform.localToWorldMatrix);
                Debug.Log("[Airborne] TEMPLATE_OK " + model + " root scale=" + imported.transform.localScale.ToString("F2") +
                          " rot=" + imported.transform.localEulerAngles.ToString("F0") + " world bounds=" + bounds.size.ToString("F2"));
                if (bounds.size.y < 0.5f) throw new Exception(model + " template is not metre-scale upright: " + bounds.size);
                // Both models share one export, and only the canopy is unambiguous: it hangs wholly
                // above the pelvis, nose (+1.2 m) shorter than tail (-1.7 m). Upside down or
                // backwards here means the stick would fly that way too.
                if (model == "AirborneCanopy" && (bounds.min.y < 0.2f || bounds.max.z > -bounds.min.z))
                    throw new Exception("canopy template is upside down or backwards: " + bounds.min.ToString("F2") + ".." + bounds.max.ToString("F2"));
                Debug.Log("[Airborne] AXES " + model + " " + bounds.min.ToString("F2") + ".." + bounds.max.ToString("F2"));
                t.transform.SetParent(g.transform, false);
                t.SetActive(false);
            }
            PrefabUtility.SaveAsPrefabAsset(g, R + MountKey + ".prefab");
            UnityEngine.Object.DestroyImmediate(g);
            Edit(mount, s =>
            {
                P(s, "jsonKey").stringValue = MountKey;
                P(s, "mountName").stringValue = "HALO Paratroopers x" + Capacity;
                P(s, "info").objectReferenceValue = info;
                P(s, "prefab").objectReferenceValue = Load<GameObject>(R + MountKey + ".prefab");
                P(s, "ammo").intValue = Capacity;
                P(s, "mass").floatValue = info.massPerRound * Capacity;
                P(s, "Troops").boolValue = true;
                P(s, "Cargo").boolValue = false;
                P(s, "disabled").boolValue = false;
            });
            Carriers();
        }

        static bool IsCargoSet(string name) =>
            name.IndexOf("Cargo", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Mission Bay", StringComparison.OrdinalIgnoreCase) >= 0;

        static void Carriers()
        {
            var op = ScriptableObject.CreateInstance<OpAddWeaponToHardpoint>();
            op.weaponJsonKey = MountKey;
            // Vanilla: every cargo bay of the VL-49 Tarantula.
            var tarantula = Load<AircraftDefinition>(D + "MonoBehaviour/QuadVTOL1_PLACEHOLDER.asset");
            var sets = tarantula.unitPrefab.GetComponentInChildren<WeaponManager>(true).hardpointSets;
            var indices = Enumerable.Range(0, sets.Length).Where(i => IsCargoSet(sets[i].name)).ToList();
            if (indices.Count == 0) throw new Exception("Tarantula has no cargo bay");
            op.aircraft.Add(new OpAddWeaponToHardpoint.AircraftTarget { aircraftJsonKey = tarantula.jsonKey, hardpointIndices = indices });
            // Third-party Aryx MC-260 Chimera 1.2.0 (Cargo Bay Rear/Front, Mission Bay). Blueprinter
            // only warns when that addon is absent.
            op.aircraft.Add(new OpAddWeaponToHardpoint.AircraftTarget { aircraftJsonKey = "Aryx_CargoPlane1", hardpointIndices = new List<int> { 1, 2, 3 } });
            Debug.Log("[Airborne] " + MountKey + " -> " + string.Join(", ", op.aircraft.Select(a => a.aircraftJsonKey + ":" + string.Join("/", a.hardpointIndices))));
            op.name = "Op_" + MountKey;
            string opPath = R + "Op_" + MountKey + ".asset";
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
            string outDir = Environment.GetEnvironmentVariable("AB_OUT") ?? throw new Exception("AB_OUT not set");
            string delivery = Path.GetFullPath("Delivery~");
            Directory.CreateDirectory(delivery);
            var started = DateTime.UtcNow;
            ModBuilder.Build("Airborne", "Airborne", Version, delivery);
            string built = Path.Combine(delivery, "Airborne_" + Version + ".nobp");
            if (!File.Exists(built) || File.GetLastWriteTimeUtc(built) < started.AddSeconds(-1))
                throw new Exception("fresh bundle was not produced");
            File.Copy(built, Path.Combine(outDir, "Airborne.nobp"), true);
            Debug.Log("[Airborne] BUNDLE_OK " + new FileInfo(built).Length + " bytes");
        }
    }
}
