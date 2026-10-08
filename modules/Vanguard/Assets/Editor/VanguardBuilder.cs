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
                Description = "AEGIS-3 hard-kill self-defence pod. Three hit-to-kill darts drop clear, swing onto missiles closing from any direction inside 2 km and light their motors. Roughly two in three hits kill.",
                Mass = 25, Yield = 3, Pierce = 50, Cost = 0.15f, Value = 2, RadarSize = 0.0005f, Thrust = 9000, BurnTime = 2.5f, GLimit = 60, TurnRate = 70,
                Racks = new string[0] }, // AEGIS gets its own three-dart pod, see AegisPod()
            new Spec { Key = "VG_Glaive2A", Name = "GLAIVE-2A UGV Carrier", Short = "GLAIVE-A", Model = "Glaive",
                Description = "Powered glide carrier. Flies up to 35 km to the target, comes in at 40 m and puts two Hexhound GMG robots on the ground beside it. Their batteries last four minutes.",
                Mass = 900, Yield = 1, Cost = 1.2f, Value = 25, RadarSize = 0.4f, Thrust = 2500, BurnTime = 200, GLimit = 6, TurnRate = 15,
                Racks = new[] { "AGM_heavy_single", "CruiseMissile1_internalx2" } },
            new Spec { Key = "VG_Glaive2S", Name = "GLAIVE-2S SAM Drop", Short = "GLAIVE-S", Model = "Glaive",
                Description = "As GLAIVE-2A, carrying two Hexhound SAM robots: an instant short-range air-defence pocket behind the lines for four minutes.",
                Mass = 950, Yield = 1, Cost = 1.6f, Value = 28, RadarSize = 0.4f, Thrust = 2500, BurnTime = 200, GLimit = 6, TurnRate = 15,
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
            OpReferenceIndex.Refresh();
            Build();
        }

        // ------------------------------------------------------------------ helpers

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
                m.SetColor("_BaseColor", new Color(0.02f, 0.03f, 0.04f));
                m.SetFloat("_Metallic", 0.9f);
                m.SetFloat("_Smoothness", 0.95f);
            }),
            ["Glow"] = MakeMaterial("Vanguard_Glow", m =>
            {
                m.SetColor("_BaseColor", new Color(0.1f, 0.6f, 1f));
                m.SetFloat("_Smoothness", 0.7f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.25f, 0.8f, 1f) * 4f);
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
            m.SetFloat("_Smoothness", 1f);
        });

        // BuildVanguard.py joins each model into parts named Skin / Glow / Glass / Dark.
        static GameObject Visual(Transform parent, string model, Dictionary<string, Material> materials)
        {
            var g = Clone(R + "Models/" + model + ".fbx");
            g.name = "VanguardVisual";
            g.transform.SetParent(parent, false);
            g.transform.localPosition = Vector3.zero;
            g.transform.localRotation = Quaternion.identity;
            foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = parent.gameObject.layer;
            var skin = Skin(model);
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
            {
                // Articulated models keep per-joint children ("Glow.001", "Dark.003"): match on the base name.
                string key = r.name.IndexOf('.') > 0 ? r.name.Substring(0, r.name.IndexOf('.')) : r.name;
                var mat = materials.TryGetValue(key, out var shared) ? shared : skin;
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
            }
            return g;
        }

        static Sprite Icon(string model) => Load<Sprite>(R + "Models/" + model + "_Icon.png");

        static Bounds VisualBounds(GameObject visual)
        {
            var rs = visual.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds;
            foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
            return b;
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
            var missile = g.GetComponent<Missile>();
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
                if (spec.Key == "VG_HawcX") P(s, "supersonicDrag").floatValue = 0.35f; // waverider: holds Mach 8 in the glide
                P(s, "foldingFins").arraySize = 0;
                var motor = P(s, "motors").GetArrayElementAtIndex(0);
                motor.FindPropertyRelative("thrust").floatValue = spec.Thrust;
                motor.FindPropertyRelative("burnTime").floatValue = spec.BurnTime;
                // AEGIS darts cold-launch: they fall clear unpowered while VanguardFlight slews them.
                if (spec.Key == "VG_AegisDart") motor.FindPropertyRelative("delayTimer").floatValue = 0.45f;
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
                Visual(station.transform, spec.Model, materials);
                Edit(station, s => P(s, "info").objectReferenceValue = info);
            }
            SaveMount(g, mount, json, info, donor);
        }

        // AEGIS-3: three darts ride visibly under the pod and eject straight down (MountedMissile rail
        // Down), so remaining ammo reads at a glance. Offsets mirror the cradles in BuildVanguard.py.
        static readonly Vector3[] AegisCells =
            { new Vector3(-0.13f, -0.095f, 0.15f), new Vector3(0f, -0.095f, 0.15f), new Vector3(0.13f, -0.095f, 0.15f) };

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
            for (int i = 0; i < AegisCells.Length; i++)
            {
                var cell = i == 0 ? first : UnityEngine.Object.Instantiate(first, first.transform.parent);
                cell.name = "AegisCell" + i;
                cell.transform.localPosition = home + AegisCells[i];
                cell.transform.localRotation = Quaternion.identity;
                Visual(cell.transform, "AegisInterceptor", materials);
                Edit(cell, s =>
                {
                    P(s, "info").objectReferenceValue = info;
                    P(s, "railDirection").enumValueIndex = 1; // Down
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
                P(s, "description").stringValue = "Roll-on robotic tanker kit. Fire to open the ramp and deploy two telescoping arms: friendly aircraft holding position behind the ramp are docked, refuelled and re-armed from an 8 t fuel / 1.5 t munitions stock. Fire again to stow.";
                P(s, "weaponIcon").objectReferenceValue = Icon("SkywellKit");
            });
            var mount = Copy<WeaponMount>(donor, "WM_" + json);
            var g = Clone(D + "GameObject/" + donor + "_PLACEHOLDER.prefab");
            g.name = json;
            HideRenderers(g);
            var station = g.GetComponentsInChildren<MountedCargo>(true).First();
            // The cargo donor's frame is pitched -90 deg about X relative to the aircraft (sim probe: kit length axis
            // came out vertical), so the kit is pitched back to lie flat on the bay floor, nose forward.
            var kitVisual = Visual(station.transform.parent, "SkywellKit", materials).transform;
            kitVisual.localPosition = station.transform.localPosition;
            kitVisual.localRotation = Quaternion.Euler(90f, 0f, 0f) * kitVisual.localRotation;
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
                P(s, "description").stringValue = "Electromagnetic railgun pod. 3 km/s tungsten slugs punch through any armour; one round every two seconds, twelve in the magazine.";
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
                    P(s, "tracerColor").colorValue = new Color(0.3f, 0.8f, 1f);
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
