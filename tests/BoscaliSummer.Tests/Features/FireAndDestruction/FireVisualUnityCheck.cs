#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using BoscaliSummer.Fire;
using UnityEditor;
using UnityEngine;

// Real Unity particle modules, with scene/material discovery and cloud sorting stubbed.
public static class FireVisualUnityCheck
{
    private static bool engineError;
    private static void TrackErrors() { Application.logMessageReceived+=(text,stack,type)=>
        { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) engineError=true; }; }
    public static void ConnectedPreview()
    {
        TrackErrors();
        try
        {
            AssetDatabase.Refresh();
            var material = new Material(Shader.Find("Boscali/OfflineFirePreview"));
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/smoke_hard.png"));
            material.SetTexture("_EmissionTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/fire_small_e.png"));
            var pool = new FireVisualPool(); const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(FireVisualPool).GetField("templatesSearched",flags).SetValue(pool,true);
            typeof(FireVisualPool).GetField("flameMaterial",flags).SetValue(pool,material);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name="Preview ground"; ground.transform.position=new Vector3(30f,-1f,20f);
            ground.transform.localScale=new Vector3(500f,2f,500f);
            var soil=new Material(Shader.Find("Standard")); soil.color=new Color(0.23f,0.27f,0.17f);
            soil.SetFloat("_Glossiness",0f); ground.GetComponent<Renderer>().sharedMaterial=soil;
            Physics.SyncTransforms(); TerrainProbeCache.Clear();
            GameAssets.i=new GameAssets(); GameAssets.i.scorchMarkDecal=new GameObject("Soot fixture");
            GameAssets.i.scorchMarkDecal.AddComponent<UnityEngine.Rendering.Universal.DecalProjector>();
            var nativeMesh=new Mesh { name="Shared native mesh sentinel" };
            nativeMesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.forward}; nativeMesh.triangles=new[]{0,1,2};
            GameAssets.i.scorchMarkDecal.AddComponent<MeshFilter>().sharedMesh=nativeMesh;
            var scarPool=new BurnScarPool();
            var camera=new GameObject("Camera").AddComponent<Camera>();
            camera.fieldOfView=52f; camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(0.40f,0.48f,0.53f); camera.farClipPlane=2000f;
            RenderSettings.ambientLight=new Color(0.45f,0.48f,0.50f);
            var sun=new GameObject("Sun").AddComponent<Light>(); sun.type=LightType.Directional; sun.intensity=1.1f;
            sun.transform.rotation=Quaternion.Euler(45f,-30f,0f);
            var cells=new List<FireFrontCell>(); var visuals=new List<FireVisualPool.Visual>(); var known=new HashSet<long>();
            string[] sequence=File.ReadAllLines("front-cells.txt"); Require(sequence.Length==24,"Expected 24 production cells");
            for(int count=0;count<24;count++)
            {
                long selected=long.Parse(sequence[count],System.Globalization.CultureInfo.InvariantCulture);
                cells.Add(FireFrontCell.FromKey(selected)); known.Add(selected);
                var newest=cells[count]; var position=new GlobalPosition(newest.Center.X,0.2f,newest.Center.Z);
                var visual=pool.Acquire(position,true); visual.SetForestCell(newest,Vector3.up); visuals.Add(visual);
                scarPool.StampForest(position,newest);
                for(int index=0;index<visuals.Count;index++)
                {
                    int mask=0; for(int edge=0;edge<cells[index].Count;edge++) if(known.Contains(cells[index].Neighbours[edge])) mask|=1<<edge;
                    visuals[index].SetFrontEdges(mask);
                    for(int layer=0;layer<visuals[index].Systems.Length;layer++)
                    { var ps=visuals[index].Systems[layer]; ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear); ps.useAutoRandomSeed=false; ps.randomSeed=(uint)(113+index*137+layer*37); ps.Play(false); }
                }
                if(count!=2 && count!=11 && count!=23) continue;
                for(int step=0;step<80;step++) foreach(var v in visuals)
                {
                    v.SetPhase(20f,1f,new Vector3(6f,0f,2f));
                    foreach(var ps in v.Systems) ps.Simulate(0.2f,false,false,true);
                }
                Capture(camera,new Vector3(155f,180f,-140f),new Vector3(45f,0f,20f),"front-"+(count+1)+"-cells.png");
            }
            Capture(camera,new Vector3(112f,38f,-65f),new Vector3(45f,5f,20f),"front-close.png");
            Require(Datum.origin.GetComponentsInChildren<MeshFilter>().Length==24,"Expected one soot mesh per cell");
            Require(nativeMesh.vertexCount==3,"Forest stamp modified the shared native prefab mesh");
            camera.transform.position=new Vector3(10000f,0f,0f); scarPool.UpdateCulling(camera);
            foreach(var renderer in Datum.origin.GetComponentsInChildren<MeshRenderer>()) Require(!renderer.enabled,"Distant soot did not cull");
            camera.transform.position=new Vector3(0f,30f,0f); scarPool.UpdateCulling(camera);
            foreach(var renderer in Datum.origin.GetComponentsInChildren<MeshRenderer>()) Require(renderer.enabled,"Nearby soot did not return");
            foreach(var v in visuals) Require(v.FrontMesh.vertexCount==0 || v.Systems[0].shape.shapeType==ParticleSystemShapeType.Mesh,"Box emission survived an exposed edge");
            Require(!engineError,"Unity emitted an engine error; inspect check.log");
            File.WriteAllText("result.txt","PASS: 3/12/24 connected-cell previews use the sequence exported by the release-manager spread fixture, production Voronoi geometry, flame perimeter meshes, BurnScarPool, TerrainProbeCache, Windows shader bundle and native fire textures. Native mesh preservation and 4 km soot culling checked. Flat fixture terrain; timers accelerated, substitute flame shader, native smoke excluded; no live acceptance.");
            scarPool.Clear(); pool.Clear(); EditorApplication.Exit(0);
        }
        catch(Exception error) { File.WriteAllText("result.txt","FAIL: "+error); EditorApplication.Exit(1); }
    }
    public static void Preview()
    {
        try
        {
            AssetDatabase.Refresh();
            var material = new Material(Shader.Find("Boscali/OfflineFirePreview"));
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/smoke_hard.png"));
            material.SetTexture("_EmissionTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/fire_small_e.png"));
            var pool = new FireVisualPool();
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FireVisualPool).GetField("templatesSearched", fields).SetValue(pool, true);
            typeof(FireVisualPool).GetField("flameMaterial", fields).SetValue(pool, material);
            var fire = pool.Acquire(new GlobalPosition { x = 0f, z = 0f }, true);
            var camera = new GameObject("Preview camera").AddComponent<Camera>();
            camera.fieldOfView = 52f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.36f, 0.43f, 0.47f);
            camera.farClipPlane = 2000f;
            RenderSettings.ambientLight = new Color(0.40f, 0.44f, 0.48f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            Primitive("Ground", PrimitiveType.Cube, new Vector3(0f, -1f, 0f), new Vector3(600f, 2f, 600f), new Color(0.17f, 0.20f, 0.12f));
            Primitive("Ash bed", PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(48f, 0.03f, 34f), new Color(0.07f, 0.065f, 0.055f));
            var random = new System.Random(712);
            for (int i = 0; i < 130; i++)
            {
                float x = (float)random.NextDouble() * 230f - 115f;
                float z = (float)random.NextDouble() * 200f - 80f;
                if (x*x/900f + z*z/625f < 1.6f) continue;
                float height = 10f + (float)random.NextDouble() * 11f;
                Primitive("Trunk", PrimitiveType.Cylinder, new Vector3(x, height*0.38f, z), new Vector3(0.7f, height*0.38f, 0.7f), new Color(0.16f, 0.11f, 0.07f));
                Primitive("Tree proxy", PrimitiveType.Sphere, new Vector3(x, height*0.73f, z), new Vector3(height*0.48f, height*0.65f, height*0.43f), new Color(0.12f, 0.22f+(float)random.NextDouble()*0.06f, 0.12f));
            }
            for (int i = 0; i < fire.Systems.Length; i++) { fire.Systems[i].useAutoRandomSeed = false; fire.Systems[i].randomSeed = (uint)(113+i*37); }
            for (int step = 0; step < 100; step++)
            {
                fire.SetPhase(step*0.2f, 1f, new Vector3(6f,0f,2f));
                foreach (var system in fire.Systems) system.Simulate(0.2f, false, false, true);
            }
            fire.SetLight(true);
            Capture(camera, new Vector3(60f, 27f, -76f), new Vector3(0f, 7f, 0f), "after-close.png");
            Capture(camera, new Vector3(145f, 135f, -190f), new Vector3(0f, 0f, 0f), "after-aerial.png");
            fire.Root.SetActive(false);
            Type beforeType = typeof(FireVisualPool).Assembly.GetType("BoscaliSummer.Fire.FireVisualPoolBefore", true);
            object beforePool = Activator.CreateInstance(beforeType);
            beforeType.GetField("templatesSearched", fields).SetValue(beforePool, true);
            beforeType.GetField("flameMaterial", fields).SetValue(beforePool, material);
            object before = beforeType.GetMethod("Acquire").Invoke(beforePool, new object[] { new GlobalPosition(), true });
            Type visualType = before.GetType();
            var systems = (ParticleSystem[])visualType.GetField("Systems").GetValue(before);
            for (int i = 0; i < systems.Length; i++) { systems[i].useAutoRandomSeed=false; systems[i].randomSeed=(uint)(113+i*37); }
            for (int step = 0; step < 100; step++)
            {
                visualType.GetMethod("SetPhase").Invoke(before, new object[] { step*0.2f, 1f, new Vector3(6f,0f,2f) });
                foreach (var system in systems) system.Simulate(0.2f, false, false, true);
            }
            visualType.GetMethod("SetLight").Invoke(before, new object[] { true });
            Capture(camera, new Vector3(60f, 27f, -76f), new Vector3(0f, 7f, 0f), "before-close.png");
            File.WriteAllText("result.txt", "PASS: before/after Unity renders use production flame code, native fire_small_e/smoke_hard textures, deterministic particle seeds. Substitute shader, tree/ground/ash proxies; native smoke, terrain and URP post-processing excluded.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { File.WriteAllText("result.txt", "FAIL: "+error); EditorApplication.Exit(1); }
    }
    private static void Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type); go.name=name; go.transform.position=position; go.transform.localScale=scale;
        var material = new Material(Shader.Find("Standard")); material.color=color; material.SetFloat("_Glossiness",0f);
        go.GetComponent<Renderer>().sharedMaterial=material;
    }
    private static void Capture(Camera camera, Vector3 position, Vector3 target, string name)
    {
        camera.transform.position=position; camera.transform.LookAt(target);
        var rt=new RenderTexture(1600,900,24); camera.targetTexture=rt; camera.Render();
        RenderTexture.active=rt; var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,1600,900),0,0); image.Apply();
        File.WriteAllBytes(name,image.EncodeToPNG()); RenderTexture.active=null; camera.targetTexture=null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt);
    }
    public static void Run()
    {
        TrackErrors();
        try
        {
            var pool = new FireVisualPool();
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FireVisualPool).GetField("templatesSearched", fields).SetValue(pool, true);
            typeof(FireVisualPool).GetField("flameMaterial", fields).SetValue(pool,
                new Material(Shader.Find("Particles/Standard Unlit")));
            var position = new GlobalPosition { x = 130f, z = 270f };
            var fire = pool.Acquire(position, true);
            fire.SetPhase(20f, 1f, new Vector3(40f, 0f, -40f));
            Require(fire.Systems.Length == 4, "Missing flame/ember layers");
            for (int i = 0; i < 3; i++)
            {
                var main = fire.Systems[i].main;
                Require(main.startSize3D && main.startSizeY.constantMax > main.startSizeX.constantMax,
                    "Forest flames must be upright tongues");
                Require(main.startRotation.constantMin >= -0.17f && main.startRotation.constantMax <= 0.17f,
                    "Flame rotation escaped upright range");
                Require(main.startLifetime.constantMax > main.startLifetime.constantMin,
                    "Authored lifetime range was lost");
                Require(fire.Systems[i].shape.position.y > 0f, "Emitter extends below ground");
                Require(fire.Systems[i].main.maxParticles == 220, "Flame budget changed");
            }
            Require(fire.Systems[3].main.startSize.constantMax < 0.5f, "Embers too large");
            Require(fire.Systems[3].velocityOverLifetime.x.constantMin > fire.Systems[0].velocityOverLifetime.x.constantMin,
                "Embers must travel farther downwind than flames");
            fire.SetPhase(21f, 0f, Vector3.zero);
            foreach (var system in fire.Systems)
                Require(system.emission.rateOverTimeMultiplier == 0f, "Burnout still emits");
            var cell=FireFrontCell.FromKey(FireFrontCell.Locate(position.x,position.z));
            fire.SetForestCell(cell,Vector3.up);
            int full=(1<<cell.Count)-1;
            fire.SetFrontEdges(full); fire.SetPhase(20f,1f,Vector3.zero);
            Require(fire.FrontMesh.vertexCount==0 && fire.Systems[0].emission.rateOverTimeMultiplier==0f,"Interior cell still emits perimeter flames");
            fire.SetFrontEdges(full^1); fire.SetPhase(20f,1f,Vector3.zero);
            Require(fire.FrontMesh.vertexCount==4 && fire.Systems[0].shape.mesh==fire.FrontMesh,"Single exposed edge did not produce one strip");
            Mesh unchanged=fire.FrontMesh; fire.SetFrontEdges(full^1);
            Require(ReferenceEquals(unchanged,fire.FrontMesh),"Stable topology replaced its mesh");
            for(int i=0;i<32;i++) fire.SetFrontEdges(full^(1<<(i%2)));
            long allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<128;i++) fire.SetFrontEdges(full^(1<<(i%2)));
            long rebuildBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
            Require(rebuildBytes==0,"Warm front mesh rebuilds allocated "+rebuildBytes+" managed bytes");
            fire.SetSleeping(true);
            Require(!fire.Root.activeSelf && !fire.Light.enabled, "Sleeping effect still active");
            pool.Release(fire);
            var building = pool.Acquire(position, false);
            Require(ReferenceEquals(fire, building), "Pool did not reuse visual");
            Require(!building.Systems[0].main.startSize3D && building.Systems[0].shape.position == Vector3.zero,
                "Forest shape leaked into building reuse");
            Require(building.Systems[0].main.startSize.constantMax == building.BaseSizes[0].constantMax,
                "Forest size leaked into building reuse");
            building.SetPhase(20f, 1f, Vector3.zero);
            CheckFlipbookInheritance();
            CheckSmokeVelocity();
            Require(!engineError,"Unity emitted an engine error; inspect check.log");
            pool.Clear();
            File.WriteAllText("result.txt", "PASS: Unity flame/ember ranges, ground anchoring, budgets, burnout, flipbook inheritance, sleep/reuse, 128 warm mesh rebuilds with zero managed allocations, smoke velocity modes and animated-profile preservation. Native material appearance not tested.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("result.txt", "FAIL: " + error);
            EditorApplication.Exit(1);
        }
    }
    private static void CheckFlipbookInheritance()
    {
        var templateRoot=new GameObject("Damage burn fixture");
        templateRoot.AddComponent<DamageParticles>();
        var child=new GameObject("wall flame");
        child.transform.SetParent(templateRoot.transform,false);
        var template=child.AddComponent<ParticleSystem>();
        child.GetComponent<ParticleSystemRenderer>().sharedMaterial=
            new Material(Shader.Find("Particles/Standard Unlit"));
        var sheet=template.textureSheetAnimation;
        sheet.enabled=true; sheet.mode=ParticleSystemAnimationMode.Grid;
        sheet.numTilesX=4; sheet.numTilesY=8; sheet.cycleCount=3;
        var pool=new FireVisualPool();
        var visual=pool.Acquire(new GlobalPosition { x=5f, z=5f }, false);
        Require(visual!=null && visual.Systems.Length==4,"Flipbook template produced no flame systems");
        foreach(var system in visual.Systems)
        {
            var copied=system.textureSheetAnimation;
            Require(copied.enabled,"Flame system lost the vanilla flipbook");
            Require(copied.numTilesX==4 && copied.numTilesY==8,"Flame system shows the whole atlas at once");
            Require(copied.cycleCount==3,"Flipbook cycle count was not inherited");
        }
        pool.Release(visual);
        UnityEngine.Object.DestroyImmediate(visual.Root);
        UnityEngine.Object.DestroyImmediate(templateRoot);
    }
    private static void CheckSmokeVelocity()
    {
        var root=new GameObject("Smoke mode fixture"); var child=new GameObject("Smoke layer"); child.transform.SetParent(root.transform,false);
        var system=child.AddComponent<ParticleSystem>(); var main=system.main; main.startLifetime=4f;
        var velocity=system.velocityOverLifetime; velocity.y=2f;
        var smoke=new FuelDepotSmokePool.Visual { Root=root, Systems=new[]{system}, BaseRates=new[]{10f},
            BaseVelocityXMin=new[]{0f}, BaseVelocityXMax=new[]{0f}, BaseVelocityZMin=new[]{0f}, BaseVelocityZMax=new[]{0f},
            SourceRoots=new[]{child.transform}, SourceIndices=new[]{0}, SourceIntensity=new[]{1f}, SourceDelay=new[]{0f},
            ActiveSourceCount=1, IntensityScale=1f, DriftScale=1f, GrowthSeconds=1f, Buoyancy=1f,
            Profile=FuelDepotSmokePool.SmokeProfile.Forest };
        smoke.SetPhase(20f,1f,new Vector3(6f,0f,2f)); system.Simulate(0.2f,false,false,true);
        Require(velocity.x.mode==velocity.y.mode && velocity.z.mode==velocity.y.mode,"Smoke axes have incompatible curve modes");
        Require(velocity.y.constantMin==2f && velocity.y.constantMax==2f,"Smoke fix changed native vertical speed");
        var animated=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.Linear(0f,1f,1f,3f));
        velocity.x=animated; velocity.y=animated; velocity.z=animated;
        smoke.SetPhase(20f,1f,new Vector3(6f,0f,2f)); system.Simulate(0.2f,false,false,true);
        Require(velocity.x.mode==ParticleSystemCurveMode.Curve && velocity.y.mode==ParticleSystemCurveMode.Curve && velocity.z.mode==ParticleSystemCurveMode.Curve,
            "Wind overwrote an authored animated smoke profile");
        UnityEngine.Object.DestroyImmediate(root);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
public struct GlobalPosition
{
    public float x, y, z;
    public GlobalPosition(float x,float y,float z) { this.x=x; this.y=y; this.z=z; }
    public Vector3 ToLocalPosition() => new Vector3(x, y, z);
}
public static class PositionExtensions { public static GlobalPosition ToGlobalPosition(this Vector3 p) => new GlobalPosition(p.x,p.y,p.z); }
public static class Datum { public static Transform origin = new GameObject("Datum").transform; }
public static class GameManager { public static bool IsHeadless => false; }
public static class PhysicsLayers { public const int StaticsMask = ~0; }
public class GameAssets { public static GameAssets i; public GameObject scorchMarkDecal; }
public class UnitPart:MonoBehaviour { public DamageEffect[] damageEffects; public GameObject[] disintegrationEffects; }
public class DamageEffect { public GameObject prefab; }
public class Building:MonoBehaviour { public GameObject wreckage; }
public class BuildingDefinition { public string jsonKey,unitName; public GameObject unitPrefab; }
public class Encyclopedia { public static Encyclopedia i; public List<BuildingDefinition> buildings; }
public class DamageParticles : MonoBehaviour { }
namespace BoscaliSummer.Fire
{
    public static class Plugin { public static class Logger { public static void LogInfo(string value) {} public static void LogWarning(string value) {} } }
    public static class ScorchDecalMaterialResolver { public static Material Resolve()=>null; public static void ResetForScene() {} }
    internal sealed class CloudDeckSorting
    {
        public bool Sync(ParticleSystem[] systems, Vector3 position, bool behind) => behind;
        public void Clear() {}
    }
}
namespace BoscaliSummer.Core.Fx
{
    public static class FxBus { public static class Scales { public const float Particles = 1f, RenderTargets=1f; } }
}
namespace BoscaliSummer.Core.Contracts { public static class FxBudget { public static int ScaleCount(int n,float scale)=>n; } }
namespace BoscaliSummer.Core.Util { public static class EmbeddedResources { public static byte[] ReadAll(Assembly a,string n,int max)=>File.ReadAllBytes("forestfire.bundle"); } }
namespace UnityEngine.Rendering.Universal
{
    public class DecalProjector:MonoBehaviour
    {
        public Vector3 size; public uint renderingLayerMask;
        public float startAngleFade,endAngleFade,fadeFactor,drawDistance; public Material material;
    }
}
#endif
