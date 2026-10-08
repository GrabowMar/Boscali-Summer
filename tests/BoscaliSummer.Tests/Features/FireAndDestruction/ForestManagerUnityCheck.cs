#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

// The release manager runs unchanged. Only host status, headless status and network
// delivery are substituted; forest indexing and terrain casts use their real code.
public static class ForestManagerUnityCheck
{
    private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private static readonly Assembly Mod=Assembly.Load("BoscaliSummer");
    private static bool server=true;
    private static int checks;
    private static Type T(string name)=>Mod.GetType("BoscaliSummer."+name,true);
    private static object Get(object o,string name)=>o.GetType().GetField(name,All).GetValue(o);
    private static void Set(object o,string name,object value)=>o.GetType().GetField(name,All).SetValue(o,value);
    private static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,All).Invoke(o,args);
    private static void Check(bool ok,string text) { checks++; if(!ok) throw new Exception(text); }
    private static bool Host(ref bool __result) { __result=server; return false; }
    private static bool Headless(ref bool __result) { __result=true; return false; }
    private static bool NoSend()=>false;
    public static void Run()
    {
        var harmony=new Harmony("boscalisummer.tests.forest-manager");
        try
        {
            var paths=typeof(BepInEx.Paths).GetMethod("SetExecutablePath",All);
            var parameters=paths.GetParameters(); var arguments=new object[parameters.Length];
            arguments[0]=Path.GetFullPath("ForestManagerCheck.exe");
            for(int i=1;i<arguments.Length;i++) arguments[i]=parameters[i].HasDefaultValue?parameters[i].DefaultValue:null;
            paths.Invoke(null,arguments);
            var config=new ConfigFile(Path.GetFullPath("forest.cfg"),false) { SaveOnConfigSet=false };
            object settings=FormatterServices.GetUninitializedObject(T("Core.Config.ModConfiguration"));
            object fire=Activator.CreateInstance(T("Modules.FireAndDestruction.Configuration.FireAndDestructionSettings"),All,null,new object[]{config},null);
            object diagnostics=Activator.CreateInstance(T("Core.Diagnostics.DiagnosticSettings"),All,null,new object[]{config},null);
            Set(settings,"<FireAndDestruction>k__BackingField",fire); Set(settings,"<Diagnostics>k__BackingField",diagnostics);
            T("Plugin").GetProperty("Settings",All).SetValue(null,settings);
            T("Plugin").GetProperty("Logger",All|BindingFlags.DeclaredOnly).SetValue(null,new ManualLogSource("ForestManagerCheck"));
            harmony.Patch(T("Core.Game.GameAccess").GetMethod("IsServer",All),prefix:new HarmonyMethod(typeof(ForestManagerUnityCheck),nameof(Host)));
            MethodInfo headless=AccessTools.PropertyGetter(typeof(GameManager),"IsHeadless");
            if(headless!=null) harmony.Patch(headless,prefix:new HarmonyMethod(typeof(ForestManagerUnityCheck),nameof(Headless)));
            else AccessTools.Field(typeof(GameManager),"IsHeadless").SetValue(null,true);
            harmony.Patch(T("Modules.FireAndDestruction.Networking.ModNet").GetMethod("BroadcastFire",All),prefix:new HarmonyMethod(typeof(ForestManagerUnityCheck),nameof(NoSend)));
            if(Datum.origin==null) AccessTools.Property(typeof(Datum),"origin").SetValue(null,new GameObject("Datum fixture").transform);
            AccessTools.Field(typeof(Datum),"SeaLevel").SetValue(null,new GlobalPosition(0,-10,0));
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name="Terrain fixture";
            float groundHeight=Datum.LocalSeaY+20f;
            ground.transform.position=new Vector3(0,groundHeight-1f,0); ground.transform.localScale=new Vector3(1000,2,1000);
            int mask=PhysicsLayers.StaticsMask;
            // The scratch project's layer names differ from the installed game's names.
            if(mask==0) { mask=1<<8; AccessTools.Field(typeof(PhysicsLayers),"StaticsMask").SetValue(null,mask); }
            for(int layer=0;layer<32;layer++) if((mask&(1<<layer))!=0) { ground.layer=layer; break; }
            Physics.SyncTransforms();
            object manager=new GameObject("Host fire manager").AddComponent(T("Fire.ImpactFireManager"));
            object index=Get(manager,"forestIndex");
            var points=new List<Vector2>(); var keys=new List<long>();
            for(int x=-220;x<=220;x+=4) for(int z=-220;z<=220;z+=4)
            {
                points.Add(new Vector2(x,z)); keys.Add(((long)Mathf.FloorToInt(x/32f)<<32)^(uint)Mathf.FloorToInt(z/32f));
            }
            Set(index,"cellSize",32f); Set(index,"points",points.ToArray()); Call(index,"IndexSortedKeys",keys.ToArray(),keys.Count);
            Check((bool)index.GetType().GetMethod("Contains",new[]{typeof(float),typeof(float)}).Invoke(index,new object[]{0f,0f}),"Fuel fixture was not indexed");
            GlobalPosition ignition=new Vector3(0,groundHeight+0.2f,0).ToGlobalPosition();
            object[] probe={ignition,new GlobalPosition(),Vector3.zero};
            Check((bool)T("Fire.TerrainProbeCache").GetMethod("TryProbe",All).Invoke(null,probe),"Terrain fixture cast failed: mask="+mask);
            Check(((GlobalPosition)probe[1]).ToLocalPosition().y>Datum.LocalSeaY,"Terrain fixture is below sea level");
            Call(manager,"Ignite",ignition,0f,true,0,true,null,null);
            var sites=(IList)Get(manager,"fires"); Check(sites.Count==1,"Initial forest cell did not ignite");
            for(int tick=0;tick<16;tick++)
            {
                foreach(object site in sites) Set(site,"NextSpread",0f);
                Set(manager,"spreadBudget",24); Call(manager,"UpdateFires");
                Check((int)Get(manager,"spreadBudget")>=0,"Probe budget underflow");
                Check(sites.Count<=24,"Host site cap exceeded");
            }
            Check(sites.Count==24,"Dense fuel did not grow a connected front to the host cap");
            var known=new HashSet<long>(); var snapshots=new List<GlobalPosition>(); int owners=0;
            foreach(object site in sites)
            {
                object cell=Get(site,"Cell"); known.Add((long)cell.GetType().GetProperty("Key").GetValue(cell));
                snapshots.Add((GlobalPosition)Get(site,"Position")); if((bool)Get(site,"SmokeOwner")) owners++;
                Check((int)Get(site,"SpreadAttempts")<=3,"Successful advance cap exceeded");
            }
            Check(owners==1,"Connected front did not consolidate smoke");
            var cellSequence=new List<string>();
            foreach(object site in sites)
                cellSequence.Add(((long)Get(site,"Cell").GetType().GetProperty("Key").GetValue(Get(site,"Cell"))).ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.WriteAllLines("front-cells.txt",cellSequence);
            foreach(object site in sites)
            {
                object cell=Get(site,"Cell"); var neighbours=(long[])Get(cell,"Neighbours");
                bool adjacent=false; foreach(long key in neighbours) if(known.Contains(key)) adjacent=true;
                Check(adjacent,"Spread left a detached cell");
            }
            ((ConfigEntry<int>)Get(fire,"ActiveFireLimit")).Value=8;
            foreach(object site in sites) Set(site,"NextSpread",0f);
            Set(manager,"spreadBudget",24); Call(manager,"UpdateFires");
            Check(sites.Count==24 && (int)Get(manager,"spreadBudget")==24,"Lowered cap removed sites or spent spread probes");
            foreach(object site in sites) Set(site,"Expires",-1f);
            Call(manager,"UpdateFires"); Check(sites.Count==0,"Expired cells leaked");
            Call(manager,"Ignite",snapshots[0],0f,true,0,true,null,null);
            Check(sites.Count==0,"Recently burnt fuel reignited immediately");
            server=false;
            object client=new GameObject("Client fire manager").AddComponent(T("Fire.ImpactFireManager"));
            foreach(var position in snapshots) Call(client,"ReceiveIgnition",position,30f,true,1f);
            Call(client,"RefreshForestTopology"); var clientSites=(IList)Get(client,"fires");
            Check(clientSites.Count==24,"Late join used local host cap instead of wire hard ceiling");
            owners=0; foreach(object site in clientSites) if((bool)Get(site,"SmokeOwner")) owners++;
            Check(owners==1,"Client rebuilt different smoke connectivity");
            foreach(var position in snapshots) Call(client,"ReceiveIgnition",position,30f,true,1f);
            Check(clientSites.Count==24,"Replay duplicated cells");
            var joins=new Dictionary<object,int>(); foreach(object site in clientSites) joins[site]=(int)Get(site,"JoinedEdges");
            Set(clientSites[0],"Expires",-1f);
            Call(client,"UpdateFires"); Check(clientSites.Count==23,"Partial burnout did not remove exactly one cell");
            foreach(object site in clientSites)
                Check(((int)Get(site,"JoinedEdges")&joins[site])==joins[site],"Burnout reopened a shared edge into consumed ground");
            Set(client,"spreadBudget",24); foreach(object site in clientSites) Set(site,"NextSpread",0f);
            Call(client,"UpdateFires"); Check(clientSites.Count==23 && (int)Get(client,"spreadBudget")==24,"Client simulated authoritative spread");
            server=true;
            object isolated=new GameObject("Firebreak check").AddComponent(T("Fire.ImpactFireManager"));
            object isolatedIndex=Get(isolated,"forestIndex");
            Type cellType=T("Fire.FireFrontCell");
            object startCell=cellType.GetMethod("FromKey",All).Invoke(null,new object[]{long.Parse(cellSequence[0])});
            object center=Get(startCell,"Center"); var breakNeighbours=(long[])Get(startCell,"Neighbours");
            object destination=cellType.GetMethod("Seed",All).Invoke(null,new object[]{(int)(breakNeighbours[0]>>32),(int)breakNeighbours[0]});
            float sx=(float)Get(center,"X"), sz=(float)Get(center,"Z");
            float dx=(float)Get(destination,"X"), dz=(float)Get(destination,"Z");
            Vector2[] sparse={new Vector2(sx,sz),new Vector2(dx,dz)};
            SetFuel(isolatedIndex,sparse);
            Call(isolated,"Ignite",new GlobalPosition(sx,ignition.y,sz),0f,true,0,true,null,null);
            var isolatedSites=(IList)Get(isolated,"fires"); Check(isolatedSites.Count==1,"Sparse source fuel did not ignite");
            object source=isolatedSites[0];
            int count=(int)startCell.GetType().GetProperty("Count").GetValue(startCell);
            Set(source,"TriedEdges",((1<<count)-1)^1); Set(source,"NextSpread",0f); Set(isolated,"spreadBudget",24);
            Call(isolated,"TrySpread",source,30f,Vector3.zero);
            Check(isolatedSites.Count==1,"Fire jumped the cleared shared edge despite destination fuel");
            var polygon=(Array)Get(startCell,"Vertices");
            object va=polygon.GetValue(0), vb=polygon.GetValue(1);
            float ex=sx+((float)Get(va,"X")+(float)Get(vb,"X"))*0.5f;
            float ez=sz+((float)Get(va,"Z")+(float)Get(vb,"Z"))*0.5f;
            SetFuel(isolatedIndex,new[]{sparse[0],sparse[1],new Vector2(ex,ez)});
            Set(source,"TriedEdges",((1<<count)-1)^1); Set(source,"NextSpread",0f); Set(isolated,"spreadBudget",24);
            Call(isolated,"TrySpread",source,30f,Vector3.zero);
            Check(isolatedSites.Count==2,"Continuous edge fuel did not allow adjacent spread");
            File.WriteAllText("result.txt","PASS: "+checks+" release-manager assertions: connected dense-fuel spread, probe/site/advance caps, consolidated smoke, reduced host cap, burnout fuel cooldown and consumed-edge retention, late join, replay and client authority. Unity flat terrain fixture; network delivery is substituted; no live multiplayer acceptance.");
            harmony.UnpatchSelf(); EditorApplication.Exit(0);
        }
        catch(Exception error) { File.WriteAllText("result.txt","FAIL: "+error); harmony.UnpatchSelf(); EditorApplication.Exit(1); }
    }
    private static void SetFuel(object index,Vector2[] points)
    {
        var keys=new long[points.Length];
        for(int i=0;i<points.Length;i++) keys[i]=((long)Mathf.FloorToInt(points[i].x/32f)<<32)^(uint)Mathf.FloorToInt(points[i].y/32f);
        Set(index,"cellSize",32f); Set(index,"points",points); Call(index,"IndexSortedKeys",keys,points.Length);
    }
}
#endif
