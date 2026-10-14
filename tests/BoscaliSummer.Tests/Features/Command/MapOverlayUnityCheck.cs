#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.DynamicOperations.Domain;

// Only native scene lifecycle is suppressed. All map widgets, meshes, glyphs, raster
// bakes and caption builders come from the production BoscaliSummer assembly.
public sealed class PreviewDynamicMap : DynamicMap
{
    protected override void Awake() { }
    private void OnEnable() { }
    private void Start() { }
    private void Update() { }
    private void OnDestroy() { }
}
public sealed class PreviewFactionHQ : FactionHQ
{
    private void Awake() { }
    private void OnEnable() { }
    private void Start() { }
    private void Update() { }
    private void OnDestroy() { }
}
public sealed class PreviewTrailIcon : UnitMapIcon
{
    private void OnDestroy() { }
}

public static partial class MapOverlayUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const string Prefix = "BoscaliSummer.Modules.";
    private static Assembly Mod => typeof(AvConsole).Assembly;
    private static readonly List<string> failures = new List<string>();
    private static readonly List<string> measurements = new List<string> { "capture\twidget\tvertices\ttext\twidth\theight\tpreferredWidth\tpreferredHeight" };
    private static readonly List<string> captures = new List<string> { "file\twidth\theight\tsurfaces" };
    private static int checks;
    private static string capture;
    private static bool nonWingOnly, tasksOnly;
    private static Type T(string name) => Mod.GetType(name.StartsWith("BoscaliSummer.") ? name : Prefix + name, true);
    private static FieldInfo Member(object owner, string name)
    {
        for (Type type = owner as Type ?? owner.GetType(); type != null; type = type.BaseType) {
            FieldInfo field = type.GetField(name, All | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException((owner as Type ?? owner.GetType()).FullName, name);
    }
    private static object Get(object owner, string name) => Member(owner, name).GetValue(owner is Type ? null : owner);
    private static void Set(object owner, string name, object value) => Member(owner, name).SetValue(owner is Type ? null : owner, value);
    private static object Call(object owner, string name, params object[] args)
    {
        Type type = owner as Type ?? owner.GetType();
        foreach (MethodInfo method in type.GetMethods(All))
            if (method.Name == name && method.GetParameters().Length == args.Length)
                return method.Invoke(owner is Type ? null : owner, args);
        throw new MissingMethodException(type.FullName, name);
    }
    private static object New(string name, params object[] args) => Activator.CreateInstance(T(name), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
    private static void Check(bool condition, string detail) { checks++; if (!condition) failures.Add(capture + ": " + detail); }

    public static void RunMapTasks() { nonWingOnly=true; tasksOnly=true; Run(); }

    public static void RunNonWing()
    {
        nonWingOnly = true;
        Run();
    }

    public static void Run()
    {
        try {
            if (!EnsureTmpEssentials(Run)) return;
            SetExecutablePath("MapOverlayCheck.exe");
            AvBundle.Load(Debug.Log); Check(AvBundle.Available && AvIcons.Available, "Embedded production assets loaded.");
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            if (!nonWingOnly) Call(T("Wing.Runtime.WingLog"), "Init", new ManualLogSource("MapPreview"));
            Directory.CreateDirectory("renders");
            PointerGuard();
            if (!tasksOnly) StrCases();
            if (!strOnly) {
            foreach (Vector2 size in new[] { new Vector2(1280,720), new Vector2(1920,1080) })
                foreach (float zoom in new[] { .7f, 1f, 1.8f }) {
                    if (tasksOnly) {
                        foreach(string state in new[]{"empty","radar","optical","overlap","budget","relief"}) Case(size,zoom,"threat",state);
                        foreach(string state in new[]{"offered","active","mixed","lost","cluster","relief"}) Case(size,zoom,"tasks",state);
                        Case(size,zoom,"combined","mixed");
                        continue;
                    }
                    foreach (string state in new[] { "empty", "calm", "contested", "salient" }) Case(size, zoom, "control", state);
                    Case(size, zoom, "trenches", "stages");
                    if (!nonWingOnly) {
                        Case(size, zoom, "wing", "roles");
                        foreach (string state in new[] { "empty", "route", "plan" }) Case(size,zoom,"wing-routes",state);
                    }
                    foreach (string state in new[] { "empty", "radar", "optical", "overlap" }) Case(size, zoom, "threat", state);
                    foreach (string state in new[] { "empty", "sparse", "long", "cluster", "cluster-edge", "filtered", "hunt" }) Case(size, zoom, "comms", state);
                    foreach (string state in new[] { "empty", "populated", "faded", "cleared" }) Case(size,zoom,"aircraft-trails",state);
                }
            if (!tasksOnly) foreach (Vector2 size in new[] { new Vector2(1280,720), new Vector2(1920,1080) }) Case(size,8f,"trenches","stages");
            }
        } catch (Exception error) { failures.Add("exception: " + error); Debug.LogException(error); }
        File.WriteAllLines("measurements.tsv", measurements); File.WriteAllLines("captures.tsv", captures);
        File.WriteAllText("failures.txt", string.Join("\n", failures));
        File.WriteAllText("result.txt", (failures.Count == 0 ? "PASS: " : "FAIL: ") + (captures.Count - 1) + " production map-overlay captures; " + checks +
            " real-widget mesh, state, text and pointer-transparency checks; " + failures.Count + " failures. " + (nonWingOnly ? "Scope: generic non-Wing overlays only; Wing/SQD/pilot/Ace excluded. " : "Scope: historical mixed overlay fixture. ") + "Synthetic native lifecycle/map adapter and display data; world observation, native input, oblique relief, replication and scene reload require in-game acceptance.");
        EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
    }

    // The floating-chrome pointer guard and the consumed click (annex 05 P5), on the real MapUiPointer and MfdMapInteractions of the built DLL.
    private static void PointerGuard()
    {
        capture = "pointer-guard";
        Type pointer = T("Command.Presentation.MapUi.MapUiPointer"), interactions = T("Command.Presentation.MapUi.MfdMapInteractions");
        var camera = new GameObject("Guard camera", typeof(Camera)).GetComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = 360f; camera.transform.position = new Vector3(0f, 0f, -1000f); camera.farClipPlane = 2000f;
        var target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
        var canvas = new GameObject("Guard canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera; ((RectTransform)canvas.transform).sizeDelta = new Vector2(1280, 720);
        RectTransform Card(string name, float x) {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(canvas.transform, false);
            rect.sizeDelta = new Vector2(100f, 100f); rect.anchoredPosition = new Vector2(x, 0f); return rect;
        }
        Vector2 Screen(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(camera, rect.position);
        bool Over(RectTransform rect) => (bool)Call(pointer, "OverControls", Screen(rect));
        Call(pointer, "ResetFloating");
        RectTransform first = Card("Floating one", -300f), second = Card("Floating two", 300f);
        Check(!Over(first), "an unregistered rect is not map chrome");
        Call(pointer, "Register", first); Call(pointer, "Register", first);
        Check(Over(first), "a registered rect claims the pointer");
        Check(!Over(second), "a second rect nobody registered does not");
        first.gameObject.SetActive(false);
        Check(!Over(first), "a hidden registered rect does not claim the pointer");
        first.gameObject.SetActive(true);
        Check(Over(first), "the rect claims the pointer again once shown");
        Call(pointer, "Unregister", first);
        Check(!Over(first), "Unregister releases the rect");
        Call(pointer, "Register", first); Call(pointer, "Register", second);
        Check(Over(second), "a second registered rect claims the pointer");
        Vector2 gone = Screen(first);
        Object.DestroyImmediate(first.gameObject);
        Check(!(bool)Call(pointer, "OverControls", gone) && Over(second), "a destroyed rect is dropped and the other stays");
        Call(pointer, "ResetFloating");
        Check(!Over(second), "ResetFloating forgets every rect");
        for (int i = 0; i < 9; i++) Call(pointer, "Register", Card("Spare " + i, -540f + i * 120f));
        Check(Over(canvas.transform.Find("Spare 7") as RectTransform) && !Over(canvas.transform.Find("Spare 8") as RectTransform), "at most eight rects register");
        Call(pointer, "ResetFloating");
        Call(interactions, "Restore");
        PropertyInfo clicked = interactions.GetProperty("ClickedThisFrame", All);
        Check(!(bool)clicked.GetValue(null), "no click is consumed at rest");
        Call(interactions, "ConsumeClick");
        Check((bool)clicked.GetValue(null), "ConsumeClick stamps the frame");
        Call(interactions, "Restore");
        Check(!(bool)clicked.GetValue(null), "Restore clears the stamp");
        camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(canvas.gameObject); Object.DestroyImmediate(camera.gameObject);
    }

    private static void Case(Vector2 size, float zoom, string surface, string state)
    {
        if (nonWingOnly && (surface == "wing" || surface == "wing-routes"))
            throw new InvalidOperationException("Wing builders are excluded from this preview run.");
        capture = surface + "-" + state + "-" + (int)size.x + "x" + (int)size.y + "-z" + zoom.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        var camera = new GameObject("UI camera", typeof(Camera)).GetComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = size.y * .5f; camera.transform.position = new Vector3(0f,0f,-1000f);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f,.08f,.09f); camera.farClipPlane = 2000f;
        var target = new RenderTexture((int)size.x,(int)size.y,24); camera.targetTexture = target;
        var canvas = new GameObject("Map canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera; ((RectTransform)canvas.transform).sizeDelta = size;
        var viewport = new GameObject("Native map viewport", typeof(RectTransform), typeof(RectMask2D)); viewport.transform.SetParent(canvas.transform,false);
        ((RectTransform)viewport.transform).sizeDelta = size;
        var image = new GameObject("Native map image", typeof(RectTransform), typeof(Image)); image.transform.SetParent(viewport.transform,false);
        var rect = (RectTransform)image.transform; rect.sizeDelta = new Vector2(1800f,900f); rect.localScale = Vector3.one * (size.y / 1000f) * zoom;
        image.GetComponent<Image>().color = new Color(.10f,.15f,.17f); image.GetComponent<Image>().raycastTarget = false;
        var services = new GameObject("Synthetic services");
        DynamicMap map = services.AddComponent<PreviewDynamicMap>(); map.enabled = false; map.mapImage = image; map.iconLayer = image; map.mapDisplayFactor = 900f / 81920f;
        map.HQ = services.AddComponent<PreviewFactionHQ>(); map.HQ.enabled = false; SceneSingleton<DynamicMap>.i = map; Set(typeof(DynamicMap),"<mapMaximized>k__BackingField",true);
        Call(T("BoscaliSummer.Core.Game.TheaterFrame"), "Invalidate");
        var previousServices=ModuleServices.Active;
        var projection=new FixtureProjection(map.mapDisplayFactor);
        if(state=="relief") { ModuleServices.Active=new ServiceRegistry(); ModuleServices.Active.Add<IMapProjection>(projection); }
        if(surface=="tasks" || surface=="combined") Tasks(services,map,state);
        if(surface=="combined") Threat(services,map,"overlap");
        if (surface == "control") Control(services,map,state);
        if (surface == "trenches") Trenches(services,map);
        if (surface == "wing") Wing(image.transform,camera);
        if (surface == "wing-routes") Routes(map,state);
        if (surface == "threat") Threat(services,map,state);
        if (surface == "comms") Comms(services,map,state);
        if (surface == "aircraft-trails") Trails(services,map,camera,state);
        foreach(Graphic graphic in canvas.GetComponentsInChildren<Graphic>(true)) EditorRegister(graphic);
        Canvas.ForceUpdateCanvases(); Gate(canvas.transform);
        camera.Render(); RenderTexture.active = target;
        var png = new Texture2D((int)size.x,(int)size.y,TextureFormat.RGB24,false); png.ReadPixels(new Rect(0,0,size.x,size.y),0,0); png.Apply();
        File.WriteAllBytes("renders/" + capture + ".png",png.EncodeToPNG()); captures.Add(capture + ".png\t" + (int)size.x + "\t" + (int)size.y + "\t" + surface);
        RenderTexture.active = null; camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(png);
        ModuleServices.Active=previousServices; SceneSingleton<DynamicMap>.i = null; Set(typeof(DynamicMap),"<mapMaximized>k__BackingField",false); Object.DestroyImmediate(services); Object.DestroyImmediate(canvas.gameObject); Object.DestroyImmediate(camera.gameObject);
    }

    private static void Control(GameObject services, DynamicMap map, string state)
    {
        object grid = New("Command.Runtime.TacticalSectorGrid",1000f,163840f,81920f,0f,0f);
        if (state != "empty") {
            float reach = state == "salient" ? 42000f : 80000f;
            Call(grid,"AddAirbasePresence",-30000f,0f,false,reach); Call(grid,"AddAirbasePresence",30000f,0f,true,reach);
            // A bulge each way and open ground in the corners: diagonal borders, the rim and the hatch all show.
            if (state == "salient") for (int i = -3; i <= 3; i++) {
                Call(grid,"AddTroopPresence",9000f,-14000f+i*2500f,40f,false,9000f); Call(grid,"AddTroopPresence",-8000f,15000f+i*2500f,40f,true,9000f);
                Call(grid,"AddTroopPresence",1500f,i*1500f,12f,i%2==0,3000f);
            }
            if (state == "contested") for (int i = -8; i <= 8; i++) {
                Call(grid,"AddTroopPresence",-1200f,i*4500f,30f,false,6000f); Call(grid,"AddTroopPresence",1200f,i*4500f,30f,true,6000f);
            }
            Call(grid,"EvaluateSectors",30f);
        }
        var overlay = (Behaviour)services.AddComponent(T("Command.Presentation.ComMapOverlay")); overlay.enabled = false; Set(overlay,"sectorGrid",grid); Call(overlay,"TryInitialize");
        Check((bool)Get(overlay,"initialized"),"Real command layer initializes.");
        Call(overlay,"EnsureTexture",2048,1024); Texture2D texture = (Texture2D)Get(overlay,"overlayTexture");
        texture.SetPixels32((Color32[])Call(grid,"BakeTexture",2048,1024,true,.65f)); texture.Apply(false);
        var raster = (RawImage)Get(overlay,"overlayImage"); raster.texture = texture; raster.enabled = true;
        var front = (Graphic)Get(overlay,"frontlineGraphic"); front.gameObject.SetActive(true); Call(front,"SetSource",grid);
        MeshGate(front,state == "empty" ? 0 : 1,16000);
        Call(overlay,"SetOverlayVisible",false); Check(!raster.gameObject.activeSelf && !front.gameObject.activeSelf,"Command map close hides both real layers."); Call(overlay,"SetOverlayVisible",true);
    }
    private static void Trenches(GameObject services, DynamicMap map)
    {
        var manager = (Behaviour)services.AddComponent(T("Trenches.Runtime.TrenchManager")); manager.enabled = false;
        IList lines = (IList)Get(manager,"clientLineList");
        for (int n = 0; n < 8; n++) {
            var curve = new Vector3[49]; var threat = new Vector3[49]; var rear = new Vector3[49]; var reserve = new Vector3[49];
            float z = -2400f + n/2*1600f;
            for (int i = 0; i < curve.Length; i++) { float x = -4000f + n%2*6000f + i*48f; curve[i] = new Vector3(x,0,z + 70f*Mathf.Sin(i*.10f)); threat[i] = Vector3.forward; rear[i]=curve[i]-Vector3.forward*150f; reserve[i]=curve[i]-Vector3.forward*300f; }
            object line = New("Trenches.Runtime.TrenchLine",n,"Preview " + n,n%2 == 0 ? map.HQ : null,.6f,48f,curve,threat,new float[49],curve,threat);
            Set(line,"<Stage>k__BackingField",Enum.ToObject(T("Trenches.Domain.TrenchStage"),n%5));
            Set(line,"<DefenderCount>k__BackingField",n%3==0 ? 0 : 8);
            Set(line,"<Suppressed>k__BackingField",n==5); Set(line,"<Overrun>k__BackingField",n==6);
            if(n%5>=2) { Set(line,"<Support>k__BackingField",rear); Set(line,"<Links>k__BackingField",new[]{new[]{curve[16],rear[16]},new[]{curve[32],rear[32]}}); }
            if(n%5>=3) Set(line,"<Redoubt>k__BackingField",reserve);
            if(n%5>=4) Set(line,"<Spurs>k__BackingField",new[]{new[]{curve[12],curve[12]+Vector3.forward*150f},new[]{curve[36],curve[36]+Vector3.forward*150f}});
            lines.Add(line);
        }
        var overlay=(Behaviour)services.AddComponent(T("Trenches.Presentation.TrenchMapOverlay")); overlay.enabled=false; Set(overlay,"trenchManager",manager); Call(overlay,"TryInitialize");
        Graphic graphic=(Graphic)Get(overlay,"layer"); MeshGate(graphic,1,20000);
        graphic.gameObject.SetActive(false); MeshGate(graphic,0,0); graphic.gameObject.SetActive(true);
    }
    private static void Wing(Transform parent,Camera camera)
    {
        Type presentation=T("Wing.Domain.WingMapPresentation"); Sprite sprite=(Sprite)Call(T("Wing.Presentation.IconFactory"),"Get","airframe");
        for(int i=0;i<18;i++) {
            var go=new GameObject("Native silhouette " + i,typeof(RectTransform),typeof(Image)); go.transform.SetParent(parent,false);
            Image host=go.GetComponent<Image>(); host.sprite=sprite; host.color=new Color(.55f,.75f,.95f); host.raycastTarget=false;
            host.rectTransform.sizeDelta=new Vector2(18f,30f); host.rectTransform.anchoredPosition=new Vector2(-660f+i%6*265f,-270f+i/6*250f);
            float iconScale=i/6==0 ?.7f:i/6==1 ?1f:2f;
            host.rectTransform.localScale=new Vector3(iconScale,iconScale*(i%2==0 ?.7f:1.3f),1f);
            host.rectTransform.localRotation=Quaternion.Euler(0,0,i%3==0 ?0:i%3==1 ?45:90);
            bool member=i%6<3, target=i%6==3, downed=i%6==4, selected=i%6==2;
            object look=Call(presentation,"Resolve",member,target,true,true,true,selected,downed);
            string text=i>=12 ? "B4 · AIM-120 86s" : i>=6 ? "SAR · LONG CALLSIGN" : "B4";
            Call(T("Wing.Presentation.WingMarkerBadge"),"Apply",host,look,new Color(.35f,.85f,1f),text);
            Check(host.sprite==sprite && host.color==new Color(.55f,.75f,.95f),"Wing badge preserves native silhouette and tint.");
            Transform ring=FindNamed(go.transform,"WingCommand_MembershipRing");
            if(ring!=null) MeshGate(ring.GetComponent<Graphic>(),1,600);
            Transform status=FindNamed(go.transform,"WingCommand_StatusLabel");
            if(status!=null && status.gameObject.activeSelf) {
                TMP_Text label=status.GetComponentInChildren<TMP_Text>(); label.ForceMeshUpdate();
                Vector3[] corners=new Vector3[4]; label.rectTransform.GetWorldCorners(corners);
                float bottom=float.PositiveInfinity; foreach(Vector3 corner in corners) bottom=Mathf.Min(bottom,camera.WorldToScreenPoint(corner).y);
                Vector2 origin=RectTransformUtility.WorldToScreenPoint(camera,host.rectTransform.TransformPoint(host.rectTransform.rect.center));
                Vector2 center=RectTransformUtility.WorldToScreenPoint(camera,label.rectTransform.TransformPoint(Vector2.zero));
                Vector2 width=RectTransformUtility.WorldToScreenPoint(camera,host.rectTransform.TransformPoint(new Vector2(host.rectTransform.rect.xMax,host.rectTransform.rect.yMin)))-RectTransformUtility.WorldToScreenPoint(camera,host.rectTransform.TransformPoint(host.rectTransform.rect.min));
                Vector2 height=RectTransformUtility.WorldToScreenPoint(camera,host.rectTransform.TransformPoint(new Vector2(host.rectTransform.rect.xMin,host.rectTransform.rect.yMax)))-RectTransformUtility.WorldToScreenPoint(camera,host.rectTransform.TransformPoint(host.rectTransform.rect.min));
                object geometry=Call(T("Wing.Domain.WingMapBadgeGeometry"),"ForIcon",width.magnitude,height.magnitude);
                float outer=(float)geometry.GetType().GetProperty("OuterRadiusPixels",All).GetValue(geometry);
                Check(bottom>=origin.y+outer+4.5f,"Wing status clears ring by screen pixels under heading/nonuniform zoom (gap="+(bottom-origin.y-outer)+").");
                Check(Mathf.Abs(center.x-origin.x)<.2f,"Wing status remains above icon rather than following heading.");
                Check(Mathf.Abs(label.fontSize-11f)<.01f && !label.raycastTarget,"Wing status preserves native11px/input transparency.");
                Vector2 screenRight=RectTransformUtility.WorldToScreenPoint(camera,label.rectTransform.TransformPoint(Vector2.right))-center;
                Vector2 screenUp=RectTransformUtility.WorldToScreenPoint(camera,label.rectTransform.TransformPoint(Vector2.up))-center;
                Check((screenRight-Vector2.right).sqrMagnitude<.001f && (screenUp-Vector2.up).sqrMagnitude<.001f,"Wing caption axes stay upright, unskewed and one screen pixel per unit.");
            }
            foreach(Graphic badge in go.GetComponentsInChildren<Graphic>()) if(badge!=host) {
                Check(Mathf.Abs(Mathf.DeltaAngle(badge.transform.eulerAngles.z,0f))<.05f,"Wing badge stays screen aligned after native icon heading.");
                Vector2 at=RectTransformUtility.WorldToScreenPoint(camera,badge.rectTransform.TransformPoint(Vector2.zero));
                Vector2 right=RectTransformUtility.WorldToScreenPoint(camera,badge.rectTransform.TransformPoint(Vector2.right))-at;
                Vector2 up=RectTransformUtility.WorldToScreenPoint(camera,badge.rectTransform.TransformPoint(Vector2.up))-at;
                Check((right-Vector2.right).sqrMagnitude<.001f && (up-Vector2.up).sqrMagnitude<.001f,"Wing ring/selection/text axes remain screen pixels without shear.");
            }
            object hidden=Call(presentation,"Resolve",false,false,false,false,false,false,false);
            Call(T("Wing.Presentation.WingMarkerBadge"),"Apply",host,hidden,Color.white,null);
            foreach(Transform child in go.transform) Check(!child.gameObject.activeSelf,"Wing marks clear when presentation is off.");
            Call(T("Wing.Presentation.WingMarkerBadge"),"Apply",host,look,new Color(.35f,.85f,1f),text);
        }
    }
    private static void Threat(GameObject services,DynamicMap map,string state)
    {
        var view=(Behaviour)services.AddComponent(T("Command.Presentation.ThreatMapOverlay")); view.enabled=false; Call(view,"TryInitialize");
        Array samples=(Array)Get(view,"samples"); Type sampleType=samples.GetType().GetElementType(); int count=state=="empty" ?0:state=="budget" ?32:state=="overlap" ?3:1;
        for(int i=0;i<count;i++) { object sample=Activator.CreateInstance(sampleType); Set(sample,"Position",new Vector3((i%8)*6500f-25000f,0f,(i/8)*12000f-20000f)); Set(sample,"RadarRadius",state=="optical" ?0f:42000f); Set(sample,"OpticalRadius",state=="optical" || state=="budget" || i==2 ?26000f:0f); samples.SetValue(sample,i); }
        Set(view,"<TrackedEmitters>k__BackingField",count); Call(view,"Bake");
        Graphic graphic=(Graphic)Get(view,"graphic");
        using(var vh=new VertexHelper()) {
            Call(graphic,"OnPopulateMesh",vh);
            Check(state=="empty" ? vh.currentVertCount==0:vh.currentVertCount>100,"Threat vector mesh clears/paints known envelopes.");
            Check(vh.currentVertCount<43000,"Threat vectors remain within the 32-site budget.");
            bool feather=false;
            for(int i=0;i<vh.currentVertCount;i++){var v=UIVertex.simpleVert;vh.PopulateUIVertex(ref v,i);feather|=v.color.a==0;}
            Check(state=="empty"||feather,"Sensor envelopes have transparent feathered edges.");
        }
        Check(!graphic.raycastTarget,"Sensor envelopes preserve native pointer input.");
        Call(view,"SetShown",false); Check(!((GameObject)Get(view,"root")).activeSelf,"Map close hides real vectors."); Call(view,"SetShown",true);
    }
    private sealed class FixtureProjection : IMapProjection
    {
        private readonly float factor;
        internal FixtureProjection(float factor) { this.factor=factor; }
        public bool IsActive=>true;
        public int Revision=>1;
        public bool TryProject(float x,float z,out float px,out float py) {
            px=(x+z*.25f)*factor; py=z*factor*.55f+Mathf.Sin(x/10000f)*20f;
            return float.IsFinite(px)&&float.IsFinite(py);
        }
        public bool TryScale(float x,float z,float metres,out float units) { units=metres*factor;return true; }
    }
    private static void Tasks(GameObject services,DynamicMap map,string state)
    {
        var view=(Behaviour)services.AddComponent(T("DynamicOperations.Runtime.ContractMapHud"));view.enabled=false;
        Call(view,"Build");
        RectTransform layer=(RectTransform)Get(view,"rootRect"); layer.SetParent(map.mapImage.transform,false);
        layer.anchorMin=layer.anchorMax=layer.pivot=new Vector2(.5f,.5f);layer.anchoredPosition=Vector2.zero;
        Array cards=(Array)Get(view,"cards");
        int count=state=="mixed"||state=="cluster"||state=="relief"?3:1;
        for(int i=0;i<count;i++) {
            bool offered=state=="offered"||i==0&&count>1;
            var entry=new SecondaryObjectiveView(71+i,i==0?"RECONNAISSANCE PASS":i==1?"COVER THE SUPPLY RUN":"CAPTURE FORWARD BASE",
                "","","ACTIVE","",.3f,320f,1400,125,false,offered,!offered,state!="lost",
                state=="cluster"?0f:-24000f+i*26000f,state=="cluster"?0f:-8000f+i*13000f,6000f);
            Check(ContractCard.TryRead(entry,out ContractCard card,true),"map accepts both offers and active tasks");cards.SetValue(card,i);
        }
        Set(view,"cardCount",count); Call(view,"Render",map);
        Array tags=(Array)Get(view,"tags"); int shown=0;
        for(int i=0;i<tags.Length;i++) {
            object tag=tags.GetValue(i);var root=(RectTransform)Get(tag,"root");
            if(!root.gameObject.activeSelf)continue;shown++;
            var text=(TMP_Text)Get(tag,"label");
            Check(text.text.Contains(i==0&&state!="active"&&state!="lost"?"OFFER":"ACCEPTED"),"map task state is explicit");
            Check(!text.raycastTarget,"task caption preserves native map clicks");
            Check(text.preferredHeight<=text.rectTransform.rect.height,"short task captions retain all status lines");
            Check(Mathf.Abs(text.transform.lossyScale.x-1f)<.001f,"task captions remain screen-sized through map zoom");
            MeshGate((Graphic)Get(tag,"symbol"),1,512);MeshGate((Graphic)Get(tag,"area"),1,2000);
            if(i>0&&state=="cluster")Check(((TMP_Text)Get(tags.GetValue(i-1),"label")).rectTransform.anchoredPosition.y<text.rectTransform.anchoredPosition.y,"coincident task captions are separated");
        }
        Check(shown==(state=="lost"?0:count),"only host-permitted positions become map tasks");
        Call(view,"Hide");foreach(object tag in tags) Check(!((RectTransform)Get(tag,"root")).gameObject.activeSelf,"map close hides task slots");
        Call(view,"Render",map);
    }
    private static void Routes(DynamicMap map,string state)
    {
        object view=New("Wing.Presentation.WmcMapOverlay"); Call(view,"Rebind",map);
        if(state!="empty") for(int element=0;element<4;element++) {
            Color colour=(Color)Call(T("Wing.Presentation.WmcMapOverlay"),"ElementColor",element);
            float z=-24000f+element*15000f;
            for(int p=0;p<3;p++) {
                object leg=New("Wing.Domain.RouteLeg"); Set(leg,"FromX",-60000f+p*35000f); Set(leg,"FromZ",z); Set(leg,"ToX",-25000f+p*35000f); Set(leg,"ToZ",z+6000f);
                Set(leg,"Number",p+1); Set(leg,"Km",35.5f*(p+1)); Set(leg,"Eta",180f*(p+1)); Set(leg,"Action",Enum.ToObject(T("Wing.Domain.ArrivalAction"),0));
                if(state=="plan") { Set(leg,"Text",(char)('A'+element)+"2 CAP · AFTER B1"); Set(leg,"Wide",p==1); }
                ((IList)Get(view,"legs")).Add(leg); ((IList)Get(view,"legColors")).Add(colour);
            }
            object ring=New("Wing.Domain.RouteRing"); Set(ring,"X",element*14000f-18000f); Set(ring,"Z",z); Set(ring,"Radius",4500f+element*1000f);
            ((IList)Get(view,"rings")).Add(ring); ((IList)Get(view,"ringColors")).Add(colour);
        }
        Call(view,"Draw",map); Call(view,"Hide");
        foreach(TMP_Text label in (IList)Get(view,"labels")) Check(!label.gameObject.activeSelf,"WMC route captions hide on map close.");
        Call(view,"Draw",map);
        if(state!="empty") { Call(view,"Ping",new GlobalPosition(6000f,0f,6000f)); Set(view,"pingStart",Time.unscaledTime-.5f); Call(view,"AnimatePing",map); }
    }
    private static void Comms(GameObject services,DynamicMap map,string state)
    {
        var manager=(Behaviour)services.AddComponent(T("Comms.Runtime.CommsManager")); manager.enabled=false;
        object config=New("Comms.Configuration.CommsSettings",new ConfigFile(Path.GetFullPath("comms-preview.cfg"),false)); Call(manager,"Configure",config,null,null);
        object mirrored=Get(manager,"client"); Set(mirrored,"LocalId",1UL); Set(manager,"<LocalFaction>k__BackingField",1);
        object board=mirrored.GetType().GetProperty("Board",All).GetValue(mirrored);
        bool clustered=state.StartsWith("cluster",StringComparison.Ordinal);
        int count=state=="empty" ?0:clustered ?24:6;
        if(state=="cluster-edge") {
            Rect viewport=((RectTransform)map.mapImage.transform.parent).rect;
            ((RectTransform)map.mapImage.transform).anchoredPosition=new Vector2(viewport.xMax-20f,viewport.yMax-20f);
        }
        for(int i=0;i<count;i++) {
            float x=-40000f+i%3*40000f,z=-20000f+i/3*34000f;
            if(clustered) { x=i%6*600f; z=i/6*600f; }
            object item=New("Comms.Domain.CommsItem"); Set(item,"Id",(uint)i+1); Set(item,"Author",(ulong)i+1); Set(item,"AuthorName",state=="long" ?"LONG CALLSIGN WARDEN":"VIPER " +(i+1)); Set(item,"Faction",i%3==0 ?2:1);
            string kind=i==4 ?"Sticker":i==5 ?"Stroke":i==3 ?"Label":"Ping";
            Set(item,"Kind",Enum.Parse(T("Comms.Domain.CommsItemKind"),kind)); Set(item,"Style",(byte)(kind=="Ping" ?i%3+1:0)); Set(item,"Size",(byte)0);
            Set(item,"Points",kind=="Stroke" ?new[]{-8000,-5000,-3500,2000,4000,3000}:new[]{Mathf.RoundToInt(x/4f),Mathf.RoundToInt(z/4f)});
            Set(item,"Text",kind=="Label" ?state=="long" ?"WWWWWWWWWWWWWWWWWW":"OBSERVE COAST ROAD":""); Set(item,"Created",Time.unscaledTime-1f); Set(item,"Expires",Time.unscaledTime+60f);
            Call(board,"Add",item,false,null);
        }
        if(state=="filtered") Call(mirrored,"ToggleMute",2UL);
        if(state=="hunt") {
            object hunt=New("Comms.Domain.HuntView"); Set(hunt,"AuthorName","WARDEN 21"); Set(hunt,"Revealed",true); Set(hunt,"HiddenX",25000f); Set(hunt,"HiddenZ",10000f);
            IList placings=(IList)Get(hunt,"Placings"); for(int i=0;i<2;i++) { object placing=New("Comms.Domain.HuntPlacingView"); Set(placing,"Name","VIPER "+(i+1)); Set(placing,"X",10000f+i*30000f); Set(placing,"Z",5000f+i*10000f); Set(placing,"Metres",450+i*2000); placings.Add(placing); }
            ((IList)Get(mirrored,"hunts")).Add(hunt);
        }
        var view=(Behaviour)services.AddComponent(T("Comms.Presentation.CommsMapLayer")); view.enabled=false; Call(view,"Configure",config,manager,null); Check((bool)Call(view,"TryBuild"),"Real COM map layer builds.");
        Graphic ink=(Graphic)Get(view,"ink"),pulse=(Graphic)Get(view,"pulse");
        Vector3[] originalInk=MeshPoints(ink),originalPulse=MeshPoints(pulse);
        Call(view,"LayoutTags",mirrored,map.mapDisplayFactor,Mathf.Abs(map.mapImage.transform.localScale.x));
        MeshGate(ink,state=="empty" ?0:1,60000); MeshGate(pulse,state=="empty" ?0:1,60000);
        Check(SamePoints(originalInk,MeshPoints(ink)) && SamePoints(originalPulse,MeshPoints(pulse)),"Caption placement preserves exact ink/pulse glyph geometry.");
        Check(((IList)Get(view,"tags")).Count<=48,"COM map caption pool stays bounded.");
        Camera camera=map.mapImage.GetComponentInParent<Canvas>().worldCamera;
        Rect screen=ScreenRect((RectTransform)map.mapImage.transform.parent,camera);
        Rect interior=new Rect(screen.x+6f,screen.y+6f,screen.width-12f,screen.height-12f);
        int eligibleCaptions=0;
        var boxes=new List<Rect>(); var anchors=new List<Vector2>();
        foreach(object item in (IEnumerable)board.GetType().GetProperty("Items",All).GetValue(board)) {
            string kind=Get(item,"Kind").ToString();
            if(kind=="Stroke" || (bool)Call(mirrored,"IsMuted",Get(item,"Author"))) continue;
            float x=(float)item.GetType().GetProperty("X",All).GetValue(item),z=(float)item.GetType().GetProperty("Z",All).GetValue(item);
            Vector2 at=(Vector2)Call(T("Comms.Presentation.CommsProjection"),"At",x,z,map.mapDisplayFactor);
            Vector2 point=RectTransformUtility.WorldToScreenPoint(camera,map.mapImage.transform.TransformPoint(at)); anchors.Add(point);
            if((kind=="Ping" || kind=="Label") && interior.Contains(point)) eligibleCaptions++;
        }
        foreach(object tag in (IList)Get(view,"tags")) {
            RectTransform tagRoot=(RectTransform)Get(tag,"root");
            FieldInfo leaderField=tag.GetType().GetField("leader",All);
            Image leader=leaderField!=null ?(Image)leaderField.GetValue(tag):null;
            if(!tagRoot.gameObject.activeInHierarchy) { if(leader!=null) Check(!leader.enabled,"Hidden COM caption leaves no orphan leader."); continue; }
            Graphic plate=(Graphic)Get(tag,"plate"); TMP_Text text=(TMP_Text)Get(tag,"label");
            Rect box=ScreenRect(plate.rectTransform,camera); boxes.Add(box);
            Check(box.xMin>=screen.xMin+5.5f && box.xMax<=screen.xMax-5.5f && box.yMin>=screen.yMin+5.5f && box.yMax<=screen.yMax-5.5f,"COM map plate remains inside viewport.");
            Check(text.preferredWidth<=plate.rectTransform.rect.width-8f+1.5f,"COM plate fits actual text metrics.");
            foreach(Vector2 anchor in anchors) Check(!box.Overlaps(new Rect(anchor.x-14f,anchor.y-14f,28f,28f)),"COM map caption does not obscure unchanged glyph anchor.");
            for(int n=0;n<boxes.Count-1;n++) Check(!box.Overlaps(boxes[n]),"COM map captions do not overlap.");
            if(leader!=null && leader.enabled) {
                Vector2 anchor=RectTransformUtility.WorldToScreenPoint(camera,map.mapImage.transform.TransformPoint((Vector2)Get(tag,"anchor")));
                Rect lineRect=leader.rectTransform.rect;
                Vector2 start=RectTransformUtility.WorldToScreenPoint(camera,leader.rectTransform.TransformPoint(new Vector2(lineRect.xMin,0f)));
                Vector2 end=RectTransformUtility.WorldToScreenPoint(camera,leader.rectTransform.TransformPoint(new Vector2(lineRect.xMax,0f)));
                Vector2 desired=new Vector2(Mathf.Clamp(anchor.x,box.xMin,box.xMax),Mathf.Clamp(anchor.y,box.yMin,box.yMax));
                Check(Mathf.Abs(Vector2.Distance(start,anchor)-14f)<.3f && Vector2.Distance(end,desired)<.3f,"COM leader connects unchanged anchor margin to its caption plate edge.");
                Check(!leader.raycastTarget && leader.transform.GetSiblingIndex()<tagRoot.GetSiblingIndex(),"COM leader stays input-transparent behind caption plates.");
            }
        }
        measurements.Add(capture+"\tCOM visible captions\t\t"+boxes.Count+" of "+eligibleCaptions+" eligible board anchors");
        if(clustered) Check(boxes.Count>=Mathf.Min(8,eligibleCaptions),"Dense COM map retains readable captions for available in-view anchors.");
    }
    private static void MeshGate(Graphic graphic,int minimum,int maximum)
    {
        EditorRegister(graphic);
        using(var vertices=new VertexHelper()) { Call(graphic,"OnPopulateMesh",vertices); Check(vertices.currentVertCount>=minimum && vertices.currentVertCount<=maximum,graphic.GetType().Name+" geometry count "+vertices.currentVertCount);
            measurements.Add(capture+"\t"+graphic.GetType().Name+"\t"+vertices.currentVertCount+"\t\t\t\t\t"); }
    }
    private static Transform FindNamed(Transform root,string name)
    {
        foreach(Transform child in root.GetComponentsInChildren<Transform>(true)) if(child.name==name) return child;
        return null;
    }
    private static Rect ScreenRect(RectTransform transform,Camera camera)
    {
        var corners=new Vector3[4]; transform.GetWorldCorners(corners);
        Vector2 min=RectTransformUtility.WorldToScreenPoint(camera,corners[0]),max=min;
        for(int i=1;i<corners.Length;i++) { Vector2 at=RectTransformUtility.WorldToScreenPoint(camera,corners[i]); min=Vector2.Min(min,at); max=Vector2.Max(max,at); }
        return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
    }
    private static Vector3[] MeshPoints(Graphic graphic)
    {
        using(var mesh=new VertexHelper()) {
            Call(graphic,"OnPopulateMesh",mesh); var points=new Vector3[mesh.currentVertCount]; var vertex=new UIVertex();
            for(int i=0;i<points.Length;i++) { mesh.PopulateUIVertex(ref vertex,i); points[i]=vertex.position; }
            return points;
        }
    }
    private static bool SamePoints(Vector3[] left,Vector3[] right)
    {
        if(left.Length!=right.Length) return false;
        for(int i=0;i<left.Length;i++) if(left[i]!=right[i]) return false;
        return true;
    }
    private static void Trails(GameObject services,DynamicMap map,Camera camera,string state)
    {
        var go=new GameObject("Observed aircraft history",typeof(RectTransform),typeof(CanvasRenderer));
        RectTransform rect=(RectTransform)go.transform; rect.SetParent(map.mapImage.transform.parent,false);
        rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.offsetMin=rect.offsetMax=Vector2.zero;
        var view=(Graphic)go.AddComponent(T("Command.Presentation.MapUi.AircraftTrailGraphic")); view.raycastTarget=false;
        var icons=new UnitMapIcon[50];
        if(state!="empty") {
            for(int i=0;i<icons.Length;i++) { var owner=new GameObject("Observed icon "+i); owner.transform.SetParent(services.transform,false); icons[i]=owner.AddComponent<PreviewTrailIcon>(); icons[i].enabled=false; }
            for(int sample=0;sample<15;sample++) {
                float time=sample*.5f; Call(view,"BeginSample",time);
                for(int i=0;i<icons.Length;i++) Call(view,"Record",icons[i],new Vector3(-470f+i%8*120f+sample*8f,-245f+i/8*75f+Mathf.Sin(sample*.4f+i)*16f,0f),new Color32((byte)(i%2==0 ?88:255),(byte)(i%2==0 ?200:74),(byte)(i%2==0 ?255:61),235));
                Call(view,"EndSample",camera,Vector3.zero,1,time);
            }
            Check(((IDictionary)Get(view,"tracks")).Count==48,"Observed history caps real tracks at48.");
            MeshGate(view,1,5000);
        }
        if(state=="faded") Call(view,"Refresh",camera,Vector3.zero,2,11f);
        if(state=="cleared") { Call(view,"BeginSample",20f); Call(view,"EndSample",camera,Vector3.zero,3,20f); }
        MeshGate(view,state=="empty" || state=="cleared" ?0:1,5000);
        if(state=="empty" || state=="cleared") Check(((IDictionary)Get(view,"tracks")).Count==0,"Observed trail removal leaves no stale track.");
    }
    private static void EditorRegister(Graphic graphic)
    {
        // Batch editor construction does not reliably run the inherited graphic lifecycle.
        // As in InteractionMenuUnityCheck, bootstrap only Unity registration; keep the
        // production geometry builder untouched. Reset uGUI's fake-null renderer cache.
        CanvasRenderer renderer=graphic.GetComponent<CanvasRenderer>();
        if(renderer==null) renderer=graphic.gameObject.AddComponent<CanvasRenderer>();
        Set(graphic,"m_CanvasRenderer",renderer);
        if(graphic.isActiveAndEnabled) typeof(Graphic).GetMethod("OnEnable",All).Invoke(graphic,null);
    }
    private static void Gate(Transform root)
    {
        foreach(Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) Check(!graphic.raycastTarget,"Passive map widget takes pointer input: "+graphic.name);
        foreach(TMP_Text text in root.GetComponentsInChildren<TMP_Text>()) {
            if(!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text)) continue; text.ForceMeshUpdate(); Rect rect=text.rectTransform.rect;
            measurements.Add(capture+"\t"+text.name+"\t\t"+text.text.Replace('\t',' ').Replace('\n',' ')+"\t"+rect.width+"\t"+rect.height+"\t"+text.preferredWidth+"\t"+text.preferredHeight);
            Check(text.preferredWidth<=rect.width+1.5f,"Caption width overflow: "+text.text+" "+text.preferredWidth+" > "+rect.width);
            Check(text.preferredHeight<=rect.height+1.5f,"Caption height overflow: "+text.text+" "+text.preferredHeight+" > "+rect.height);
            if(capture.StartsWith("wing-routes",StringComparison.Ordinal)) {
                Camera camera=text.GetComponentInParent<Canvas>().worldCamera;
                Vector2 at=RectTransformUtility.WorldToScreenPoint(camera,text.rectTransform.TransformPoint(Vector2.zero));
                float physical=text.fontSize*Vector2.Distance(at,RectTransformUtility.WorldToScreenPoint(camera,text.rectTransform.TransformPoint(Vector2.up)));
                measurements.Add(capture+"\tWMC physical font\t\t"+text.text+"\t"+physical.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture));
                Check(physical>=10.99f,"WMC map captions remain at least11 physical pixels at every resolution/zoom.");
            }
        }
    }
}
#endif
