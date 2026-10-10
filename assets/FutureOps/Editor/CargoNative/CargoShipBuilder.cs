using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Blueprinter;
using UnityEditor;
using UnityEngine;
using System.Reflection;

public static class CargoShipBuilder
{
    const string Donor="Assets/Blueprinter/_donotship/";
    static string Out => Environment.GetEnvironmentVariable("FUTUREOPS_OUT");
    const string Root="Assets/Blueprinter/Mods/CargoShip/";
    const string Key="BS_ContainerFeeder";
    [Serializable] public class SectionFile { public Section[] parts; }
    [Serializable] public class Section { public string name; public Vector3 center_unity; public bool hull,rotor; }
    static SerializedProperty Field(UnityEngine.Object target,string name) => new SerializedObject(target).FindProperty(name);
    static void Set(UnityEngine.Object target,string name,UnityEngine.Object value) { var s=new SerializedObject(target); var p=s.FindProperty(name); if(p==null) throw new Exception(target.GetType()+" missing "+name); p.objectReferenceValue=value; s.ApplyModifiedPropertiesWithoutUndo(); }
    static void Number(UnityEngine.Object target,string name,float value) { var s=new SerializedObject(target); var p=s.FindProperty(name); if(p==null) throw new Exception("Missing numeric "+name); p.floatValue=value; s.ApplyModifiedPropertiesWithoutUndo(); }
    static void Bool(UnityEngine.Object target,string name,bool value) { var s=new SerializedObject(target); s.FindProperty(name).boolValue=value; s.ApplyModifiedPropertiesWithoutUndo(); }
    static void References(UnityEngine.Object target,string name,IEnumerable<UnityEngine.Object> values) { var s=new SerializedObject(target); var p=s.FindProperty(name); var list=values.ToArray(); p.arraySize=list.Length; for(int i=0;i<list.Length;i++) p.GetArrayElementAtIndex(i).objectReferenceValue=list[i]; s.ApplyModifiedPropertiesWithoutUndo(); }
    static Transform Anchor(Transform parent,string name,Vector3 pos) { var o=new GameObject(name); o.transform.SetParent(parent,false); o.transform.localPosition=pos; return o.transform; }
    public static void Run()
    {
        try {
            GameAssemblySync.Synchronize(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            RestoreStandIns();
            foreach(var path in Directory.GetFiles("Assets/CargoTextures","*.png")) {
                var texture=(TextureImporter)AssetImporter.GetAtPath(path.Replace('\\','/'));
                if(path.EndsWith("_Normal.png")) {texture.textureType=TextureImporterType.NormalMap; texture.sRGBTexture=false;}
                else if(path.EndsWith("Mask.png") || path.EndsWith("Roughness.png")) texture.sRGBTexture=false;
                texture.maxTextureSize=2048; texture.SaveAndReimport();
            }
            Directory.CreateDirectory(Root+"Meshes"); Directory.CreateDirectory(Root+"Materials"); AssetDatabase.Refresh();
            var sourceDefinition=AssetDatabase.LoadAssetAtPath<ShipDefinition>(Donor+"MonoBehaviour/Frigate1_PLACEHOLDER.asset");
            var sourcePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Donor+"GameObject/Frigate1_PLACEHOLDER.prefab");
            if(!sourceDefinition || !sourcePrefab) throw new Exception("Native donor unavailable; run Audit after assembly synchronization and editor restart");
            var definition=AssetDatabase.LoadAssetAtPath<ShipDefinition>(Root+Key+".asset");
            if(!definition) { definition=ScriptableObject.CreateInstance<ShipDefinition>(); AssetDatabase.CreateAsset(definition,Root+Key+".asset"); }
            EditorUtility.CopySerialized(sourceDefinition,definition); definition.name=Key; definition.jsonKey=Key;
            definition.unitName="Kestrel Class Container Feeder"; definition.bogeyName="Container feeder";
            definition.description="Unarmed coastal logistics ship. Eight independent hull compartments, twin screw propulsion and native ship commands. Cargo is visual stowage; no custom resupply or container spawning.";
            definition.shipType=ShipType.FFL; definition.shipInfo=new ShipInfo {mass=8000000,topSpeed=33.3f};
            definition.mass=8000000; definition.length=126.6f; definition.width=20.6f; definition.height=31.45f;
            definition.manpower=28; definition.armorTier=1; definition.damageTolerance=8; definition.value=500;
            definition.captureCapacity=0; definition.captureStrength=0; definition.captureDefense=0;
            definition.spawnOffset=Vector3.zero; definition.dontAutomaticallyAddToEncyclopedia=false;
            var ds=new SerializedObject(definition);
            foreach(var role in new[]{"antiSurface","antiAir","antiMissile","antiRadar"}) ds.FindProperty("roleIdentity."+role).floatValue=0;
            ds.FindProperty("typeIdentity.radar").floatValue=.05f; ds.FindProperty("typeIdentity.strategic").floatValue=.2f;
            ds.ApplyModifiedPropertiesWithoutUndo();
            var root=UnityEngine.Object.Instantiate(sourcePrefab); root.name=Key;
            if(PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            root.transform.position=Vector3.zero; root.transform.rotation=Quaternion.identity; root.transform.localScale=Vector3.one;
            var ship=root.GetComponent<Ship>(); var deck=root.GetComponent<ShipPart>();
            var prototype=sourcePrefab.GetComponentsInChildren<ShipPart>(true).First(p=>p.name=="frigate1_hull_port");
            var propulsionPrototype=sourcePrefab.GetComponentInChildren<ShipPropulsion>(true);
            // Capture a stock engine's sound and spray before removing the warship's child damage graph.
            var ps=new SerializedObject(propulsionPrototype);
            var particleSources=Enumerable.Range(0,ps.FindProperty("particles").arraySize).Select(i=>(ParticleSystem)ps.FindProperty("particles").GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            var thrustAudio=(AudioSource)ps.FindProperty("thrustSound").objectReferenceValue;
            var engineAudio=(AudioSource)ps.FindProperty("engineSound").objectReferenceValue;
            foreach(var station in root.GetComponentsInChildren<Turret>(true).Select(t=>t.gameObject).Distinct().OrderBy(g=>Depth(g.transform)).ToArray()) if(station && station!=root) UnityEngine.Object.DestroyImmediate(station);
            foreach(var part in root.GetComponentsInChildren<ShipPart>(true).Where(p=>p!=deck).OrderBy(p=>Depth(p.transform)).ToArray()) if(part) UnityEngine.Object.DestroyImmediate(part.gameObject);
            foreach(var component in root.GetComponentsInChildren<Component>(true).ToArray()) {
                if(!component) continue;
                string type=component.GetType().Name;
                if(component is Collider || component is MeshFilter || component is MeshRenderer || component is LODGroup ||
                    component is Turret || component is Gun || component is MissileLauncher ||
                    type=="FireControl" || type=="Airbase" || type=="Rearmer" || type=="Hangar" || type=="AmmoRack" || type=="UnitStorage" ||
                    (component is UnitPart && !(component is ShipPart))) UnityEngine.Object.DestroyImmediate(component);
            }
            // Airbase requires Capture, so remove its required component after the airbase itself.
            foreach(var capture in root.GetComponentsInChildren<Capture>(true)) UnityEngine.Object.DestroyImmediate(capture);
            ship.definition=definition; ship.weaponStations.Clear(); ship.MapHQ=null; ship.MapAirbase=null; ship.radar=null;
            var rootBody=root.GetComponent<Rigidbody>(); rootBody.mass=8000000; rootBody.centerOfMass=new Vector3(0,-1.2f,0);
            rootBody.drag=0; rootBody.angularDrag=.15f;
            var shipSettings=new SerializedObject(ship); var wakes=shipSettings.FindProperty("wakeParticles");
            for(int i=wakes.arraySize-1;i>=0;i--) if(!wakes.GetArrayElementAtIndex(i).FindPropertyRelative("system").objectReferenceValue) wakes.DeleteArrayElementAtIndex(i);
            shipSettings.ApplyModifiedPropertiesWithoutUndo();
            var sectionFile=JsonUtility.FromJson<SectionFile>(File.ReadAllText(Path.Combine(Out,"cargo-sections.json")));
            var modelPath="Assets/SourceModels/CargoShipNative.fbx";
            var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.globalScale=1; importer.useFileUnits=false; importer.useFileScale=true; importer.bakeAxisConversion=true;
            importer.importNormals=ModelImporterNormals.Import; importer.importTangents=ModelImporterTangents.CalculateMikk;
            importer.importAnimation=false; importer.isReadable=true; importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
            var sourceModel=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var materials=NativeMaterials(sourceModel);
            var parts=new Dictionary<string,ShipPart>(); var renderers=new List<Renderer>[] {new List<Renderer>(),new List<Renderer>(),new List<Renderer>()};
            var rotorTransforms=new List<Transform>();
            foreach(var section in sectionFile.parts.Where(p=>!p.rotor)) {
                ShipPart part;
                if(section.name=="WeatherDeck") part=deck;
                else {
                    var o=new GameObject(section.name) {layer=root.layer}; o.transform.SetParent(root.transform,false); o.transform.localPosition=section.center_unity;
                    part=o.AddComponent<ShipPart>(); EditorUtility.CopySerialized(prototype,part);
                }
                part.parentUnit=ship; part.rb=rootBody;
                part.mass=section.hull?850000:section.name.StartsWith("CargoBay")?180000:section.name=="Accommodation"?150000:section.name=="WeatherDeck"?100000:section.name=="SternEquipment"?10000:20000;
                part.hitPoints=100; Bool(part,"criticalPart",false);
                Number(part,"displacement",section.hull?2000:0); Number(part,"height",section.hull?11.6f:6);
                Number(part,"leakThreshold",75); Number(part,"leakRateMin",0); Number(part,"leakRateMax",45); Number(part,"sinkThreshold",.5f);
                var dragSettings=new SerializedObject(part); dragSettings.FindProperty("directionalDrag").vector3Value=new Vector3(1,2,.0037f); dragSettings.ApplyModifiedPropertiesWithoutUndo();
                Bool(part,"compartmentalized",true);
                var com=Anchor(part.transform,"CenterOfMass",Vector3.zero); Set(part,"centerOfMass",com);
                Vector3 forceRoot=section.hull?new Vector3(section.name.EndsWith("Port")?-4.5f:4.5f,0,section.center_unity.z):section.center_unity;
                if(section.hull) com.localPosition=part.transform.InverseTransformPoint(forceRoot);
                var force=Anchor(part.transform,"BuoyancyForce",part.transform.InverseTransformPoint(forceRoot)); Set(part,"forceTransform",force);
                Set(part,"rb",rootBody);
                References(part,"connectedCompartments",Array.Empty<UnityEngine.Object>());
                References(part,"damageMaterial.renderers",Array.Empty<UnityEngine.Object>());
                References(part,"disintegrateObjects",Array.Empty<UnityEngine.Object>());
                var damageRenderers=new List<Renderer>();
                for(int level=0;level<3;level++) {
                    var filter=sourceModel.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name==section.name+"_LOD"+level);
                    var visual=CreateVisuals(filter,part.transform,section.name+"_LOD"+level,materials);
                    damageRenderers.AddRange(visual); renderers[level].AddRange(visual);
                }
                References(part,"damageMaterial.renderers",damageRenderers.Cast<UnityEngine.Object>());
                var lod0=(MeshRenderer)damageRenderers[0]; var bounds=lod0.bounds;
                if(section.hull) {
                    var low=Rebase(sourceModel.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name==section.name+"_LOD2"),part.transform,section.name+"_Collider");
                    var collider=part.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh=low; collider.convex=true;
                } else {
                    var collider=part.gameObject.AddComponent<BoxCollider>(); collider.center=part.transform.InverseTransformPoint(bounds.center); collider.size=bounds.size;
                }
                parts.Add(section.name,part);
            }
            foreach(var section in sectionFile.parts.Where(p=>p.rotor)) {
                var engine=parts["PropulsionVisuals"];
                var pivot=Anchor(engine.transform,section.name,engine.transform.InverseTransformPoint(section.center_unity)); rotorTransforms.Add(pivot);
                var localRenderers=new List<Renderer>();
                for(int level=0;level<3;level++) {
                    var filter=sourceModel.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name==section.name+"_LOD"+level);
                    var visual=CreateVisuals(filter,pivot,section.name+"_LOD"+level,materials);
                    localRenderers.AddRange(visual); renderers[level].AddRange(visual);
                }
                var all=Field(engine,"damageMaterial.renderers"); var linked=Enumerable.Range(0,all.arraySize).Select(i=>all.GetArrayElementAtIndex(i).objectReferenceValue).Concat(localRenderers.Cast<UnityEngine.Object>()).ToArray();
                References(engine,"damageMaterial.renderers",linked);
            }
            var hull=parts.Where(p=>p.Key.StartsWith("Hull")).Select(p=>p.Value).ToArray();
            foreach(var compartment in hull) References(compartment,"connectedCompartments",hull.Where(p=>p!=compartment && Mathf.Abs(p.transform.position.z-compartment.transform.position.z)<34).Cast<UnityEngine.Object>());
            ship.parts=parts.Values.ToList(); ship.criticalParts=hull.ToList(); ship.criticalRatio=.75f;
            var propulsion=parts["PropulsionVisuals"].gameObject.AddComponent<ShipPropulsion>(); EditorUtility.CopySerialized(propulsionPrototype,propulsion);
            Set(propulsion,"part",parts["PropulsionVisuals"]); References(propulsion,"criticalParts",new UnityEngine.Object[]{parts["HullSternPort"],parts["HullSternStarboard"]});
            Number(propulsion,"thrust",1100000); Number(propulsion,"steeringThrust",500000);
            Set(propulsion,"thrustTransform",Anchor(propulsion.transform,"ThrustPoint",new Vector3(0,0,0)));
            var spray=new List<UnityEngine.Object>();
            foreach(var original in particleSources.Where(p=>p)) {
                var copy=UnityEngine.Object.Instantiate(original.gameObject,propulsion.transform); copy.name="PropulsionSpray"; copy.transform.localPosition=Vector3.zero; spray.Add(copy.GetComponent<ParticleSystem>());
            }
            References(propulsion,"particles",spray);
            foreach(var entry in new[]{new KeyValuePair<string,AudioSource>("thrustSound",thrustAudio),new KeyValuePair<string,AudioSource>("engineSound",engineAudio)}) {
                var sound=Anchor(propulsion.transform,entry.Key,Vector3.zero).gameObject.AddComponent<AudioSource>(); EditorUtility.CopySerialized(entry.Value,sound); Set(propulsion,entry.Key,sound);
            }
            var propSettings=new SerializedObject(propulsion); var rotators=propSettings.FindProperty("rotators"); rotators.arraySize=2;
            for(int i=0;i<2;i++) { var item=rotators.GetArrayElementAtIndex(i); item.FindPropertyRelative("transform").objectReferenceValue=rotorTransforms[i]; item.FindPropertyRelative("minSpeed").floatValue=0; item.FindPropertyRelative("maxSpeed").floatValue=i==0?240:-240; }
            propSettings.ApplyModifiedPropertiesWithoutUndo();
            var ai=root.GetComponent<ShipAI>(); Set(ai,"ship",ship); Set(ai,"keel",Anchor(root.transform,"Keel",new Vector3(0,-5.5f,0)));
            Number(ai,"standoffDistance",0);
            ship.cockpitViewPoint=Anchor(root.transform,"BridgeViewPoint",new Vector3(0,18,-40)); Set(ship,"obstacleTopTransform",Anchor(root.transform,"ObstacleTop",new Vector3(-2,25.95f,-43)));
            var group=root.AddComponent<LODGroup>(); group.SetLODs(new[]{new LOD(.2f,renderers[0].ToArray()),new LOD(.06f,renderers[1].ToArray()),new LOD(.005f,renderers[2].ToArray())}); group.RecalculateBounds();
            definition.unitPrefab=PrefabUtility.SaveAsPrefabAsset(root,Root+Key+".prefab"); EditorUtility.SetDirty(definition); AssetDatabase.SaveAssets();
            Validate(definition,root,parts);
            UnityEngine.Object.DestroyImmediate(root);
            BuildBundle(); EditorApplication.Exit(0);
        } catch(Exception e) { Debug.LogException(e); File.WriteAllText(Path.Combine(Out,"cargo-failure.txt"),e.ToString()); EditorApplication.Exit(1); }
    }
    static int Depth(Transform t) { int d=0; while(t.parent) {d++;t=t.parent;} return d; }
    static List<Renderer> CreateVisuals(MeshFilter source,Transform parent,string name,Dictionary<string,Material> materials)
    {
        var full=Rebase(source,parent,name+"_Full"); var slots=source.GetComponent<MeshRenderer>().sharedMaterials;
        var result=new List<Renderer>();
        // Native UnitPart.ApplyDamage writes renderer.material._HitPoints; each renderer needs one material.
        for(int sub=0;sub<full.subMeshCount;sub++) {
            int[] triangles=full.GetTriangles(sub); if(triangles.Length==0) continue;
            var mesh=UnityEngine.Object.Instantiate(full); mesh.name=name+"_"+slots[sub].name; mesh.subMeshCount=1; mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
            string path=Root+"Meshes/"+mesh.name+".asset"; var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing) {EditorUtility.CopySerialized(mesh,existing); UnityEngine.Object.DestroyImmediate(mesh); mesh=existing;} else AssetDatabase.CreateAsset(mesh,path);
            var visual=new GameObject(mesh.name) {layer=parent.gameObject.layer}; visual.transform.SetParent(parent,false); visual.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=visual.AddComponent<MeshRenderer>(); renderer.sharedMaterial=materials[slots[sub].name]; result.Add(renderer);
        }
        return result;
    }
    static Mesh Rebase(MeshFilter source,Transform parent,string name)
    {
        var mesh=UnityEngine.Object.Instantiate(source.sharedMesh); mesh.name=name;
        var m=parent.worldToLocalMatrix*source.transform.localToWorldMatrix;
        mesh.vertices=mesh.vertices.Select(m.MultiplyPoint3x4).ToArray();
        mesh.normals=mesh.normals.Select(n=>m.MultiplyVector(n).normalized).ToArray();
        mesh.RecalculateBounds(); string path=Root+"Meshes/"+name+".asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing) {EditorUtility.CopySerialized(mesh,existing); UnityEngine.Object.DestroyImmediate(mesh); mesh=existing;} else AssetDatabase.CreateAsset(mesh,path);
        return mesh;
    }
    static Dictionary<string,Material> NativeMaterials(GameObject model)
    {
        var result=new Dictionary<string,Material>();
        var prototype=AssetDatabase.LoadAssetAtPath<Material>(Donor+"Material/Frigate1_plating_PLACEHOLDER.mat");
        foreach(var name in model.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Select(m=>m.name).Distinct()) {
            string path=Root+"Materials/"+name+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path); if(!mat) {mat=new Material(prototype); AssetDatabase.CreateAsset(mat,path);} else EditorUtility.CopySerialized(prototype,mat);
            mat.name=name; bool cargo=name.StartsWith("Cargo"); string prefix=cargo?"Cargo":"MarinePaint";
            var albedo=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CargoTextures/"+name+"_Albedo.png");
            if(!albedo) throw new Exception("Missing coloured albedo "+name);
            mat.SetTexture("_BaseColor",albedo); mat.SetTexture("_BaseColorDamage",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CargoTextures/"+name+"_Damage.png"));
            foreach(var slot in new[]{"_Normal","_NormalDamage"}) mat.SetTexture(slot,AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CargoTextures/"+prefix+"_Normal.png"));
            mat.SetTexture("_MetallicRoughness",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CargoTextures/MarinePaint_MetallicRoughness.png"));
            mat.SetTexture("_MetallicRoughnessDamage",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CargoTextures/MarinePaint_MetallicRoughness.png"));
            mat.SetTexture("_DamageMask",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CargoTextures/DamageMask.png"));
            mat.SetTexture("_LeakMask",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CargoTextures/DamageMask.png"));
            mat.SetTexture("_PaintMask",Texture2D.blackTexture); mat.SetTexture("_AO",Texture2D.whiteTexture); mat.SetFloat("_HitPoints",100); EditorUtility.SetDirty(mat);
            result.Add(name,mat);
        }
        return result;
    }
    static void Validate(ShipDefinition definition,GameObject root,Dictionary<string,ShipPart> parts)
    {
        if(parts.Count!=18 || parts.Count(p=>p.Key.StartsWith("Hull"))!=8) throw new Exception("Damage graph must have 18 parts and 8 buoyant hull sections");
        var ship=root.GetComponent<Ship>(); if(ship.criticalParts.Count!=8 || ship.weaponStations.Count!=0 || !root.GetComponent<ShipAI>() || !root.GetComponent<NuclearOption.NetworkTransforms.ShipNetworkTransform>() || !root.GetComponent<Mirage.NetworkIdentity>()) throw new Exception("Incomplete native ship shell");
        float mass=parts.Sum(p=>p.Value.mass); if(Mathf.Abs(mass-8000000)>1) throw new Exception("Mass budget "+mass);
        float displacement=parts.Values.Sum(p=>Field(p,"displacement").floatValue);
        var jobType=typeof(Ship).Assembly.GetType("NuclearOption.Jobs.WaterJob_Math",true); var buoyancy=jobType.GetMethod("Buoyancy",BindingFlags.NonPublic|BindingFlags.Instance);
        float lift=(float)buoyancy.Invoke(Activator.CreateInstance(jobType),new object[]{.5f,displacement});
        if(Mathf.Abs(lift/(mass*9.81f)-1)>.005) throw new Exception("Native buoyancy does not balance the loaded mass");
        var lines=new List<string>{"PASS: native Ship/ShipAI/ShipNetworkTransform/NetworkIdentity, 18 damage parts, 8 port/starboard hull compartments, 8,000,000 kg mass, 16,000 m3 displacement.","Native WaterJob_Math equilibrium lift ratio="+(lift/(mass*9.81f)),"No weapons, custom AI, simulated resupply, or third-party mod code. Live sailing/flooding/multiplayer acceptance pending."};
        foreach(var p in parts) lines.Add(p.Key+" mass="+p.Value.mass+" displacement="+Field(p.Value,"displacement").floatValue+" colliders="+p.Value.GetComponents<Collider>().Length);
        File.WriteAllLines(Path.Combine(Out,"cargo-native-validation.txt"),lines);
    }
    static void BuildBundle()
    {
        // Only referenced vanilla stand-ins need a _donotship bundle. All files are in our scratch project.
        var required=new HashSet<string>(AssetDatabase.GetDependencies(Directory.GetFiles(Root,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".meta")).Select(p=>p.Replace('\\','/')).ToArray(),true),StringComparer.OrdinalIgnoreCase);
        var unused=BlueprinterAssets.GetGameAssetPaths().Where(p=>!required.Contains(p)).ToArray();
        GameAssetLock.IsLocked=false;
        AssetDatabase.StartAssetEditing();
        try {
            foreach(string path in unused) {
                string destination=path.Replace(Donor,Donor+"_stale/");
                string folder=Path.GetDirectoryName(destination).Replace('\\','/');
                BlueprinterAssets.EnsureFolder(folder);
                string error=AssetDatabase.MoveAsset(path,destination); if(!string.IsNullOrEmpty(error)) throw new Exception(error);
            }
        } finally { AssetDatabase.StopAssetEditing(); GameAssetLock.IsLocked=true; }
        var started=DateTime.UtcNow; ModBuilder.Build("CargoShip","CargoShip","0.1.0",Out);
        string built=Path.Combine(Out,"CargoShip_0.1.0.nobp"); if(!File.Exists(built)||File.GetLastWriteTimeUtc(built)<started.AddSeconds(-1)) throw new Exception("Fresh native bundle not produced");
        File.Copy(built,Path.Combine(Out,"CargoShip.nobp"),true);
        var bundle=AssetBundle.LoadFromFile(Path.Combine(Out,"CargoShip.nobp")); if(!bundle) throw new Exception("Cannot reopen native bundle");
        var definition=bundle.LoadAsset<ShipDefinition>(Root+Key+".asset");
        if(!definition || definition.jsonKey!=Key || !definition.unitPrefab || definition.unitPrefab.GetComponentsInChildren<ShipPart>(true).Length!=18) throw new Exception("Bundled native definition graph invalid");
        if(definition.unitPrefab.GetComponentsInChildren<Component>(true).Any(c=>!c)) throw new Exception("Missing native script in bundle");
        bundle.Unload(true); Debug.Log("[CargoShip] NATIVE_BUNDLE_OK");
    }
    static void RestoreStandIns()
    {
        string stale=Donor+"_stale/";
        var paths=AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith(stale,StringComparison.OrdinalIgnoreCase)&&!AssetDatabase.IsValidFolder(p)).ToArray();
        GameAssetLock.IsLocked=false; AssetDatabase.StartAssetEditing();
        try {
            foreach(string path in paths) {
                string destination=Donor+path.Substring(stale.Length);
                BlueprinterAssets.EnsureFolder(Path.GetDirectoryName(destination).Replace('\\','/'));
                string error=AssetDatabase.MoveAsset(path,destination); if(!string.IsNullOrEmpty(error)) throw new Exception(error);
            }
        } finally { AssetDatabase.StopAssetEditing(); GameAssetLock.IsLocked=true; }
    }
    public static void Audit()
    {
        try {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            GameAssemblySync.Synchronize();
            Debug.Log("[CargoShip] GAME installed="+GameAssemblies.IsInstalled+" assembly="+typeof(Ship).Assembly.Location+" base="+typeof(Ship).BaseType+" libraryExists="+File.Exists("Library/ScriptAssemblies/Assembly-CSharp.dll"));
            foreach (var script in AssetDatabase.LoadAllAssetsAtPath("Packages/nuclearoption/Assembly-CSharp.dll").OfType<MonoScript>().Where(s=>s.name=="ShipDefinition" || s.name=="Ship")) {
                string guid; long id; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script,out guid,out id);
                Debug.Log("[CargoShip] SCRIPT "+script.name+" guid="+guid+" fileID="+id+" class="+script.GetClass());
            }
            Debug.Log("[CargoShip] DONOR_FILE exists="+File.Exists(Donor+"MonoBehaviour/Frigate1_PLACEHOLDER.asset")+" guid="+AssetDatabase.AssetPathToGUID(Donor+"MonoBehaviour/Frigate1_PLACEHOLDER.asset"));
            Debug.Log("[CargoShip] ALL_DEFINITIONS "+string.Join(",",AssetDatabase.FindAssets("t:ShipDefinition").Select(AssetDatabase.GUIDToAssetPath)));
            var definition=AssetDatabase.LoadAssetAtPath<ShipDefinition>(Donor+"MonoBehaviour/Frigate1_PLACEHOLDER.asset");
            if (!definition) throw new Exception("Native frigate definition not imported; main type="+AssetDatabase.LoadMainAssetAtPath(Donor+"MonoBehaviour/Frigate1_PLACEHOLDER.asset")?.GetType());
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Donor+"GameObject/Frigate1_PLACEHOLDER.prefab");
            if (!prefab) throw new Exception("Native frigate prefab not imported");
            var lines=new List<string>{"DEFINITION "+JsonUtility.ToJson(definition),"ROOT "+prefab.name};
            foreach(var c in prefab.GetComponentsInChildren<Component>(true)) {
                if (!c) throw new Exception("Missing donor component");
                string path=AnimationUtility.CalculateTransformPath(c.transform,prefab.transform);
                if(c is ShipPart || c is ShipPropulsion || c is Ship || c is Rigidbody || c is Collider || c is ShipAI)
                    lines.Add(path+" | "+c.GetType().Name+" | "+EditorJsonUtility.ToJson(c));
                else if(!(c is Transform || c is MeshFilter || c is MeshRenderer || c is ParticleSystem || c is ParticleSystemRenderer || c is AudioSource))
                    lines.Add(path+" | "+c.GetType().Name);
            }
            File.WriteAllLines(Path.Combine(Out,"native-ship-audit.txt"),lines);
            Debug.Log("[CargoShip] DONOR_AUDIT_OK "+prefab.GetComponentsInChildren<ShipPart>(true).Length+" parts");
            EditorApplication.Exit(0);
        } catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
