#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// Native discovery and gameplay lifecycle are deliberately outside this fixture.
// Production deck/footer placement, snapshot, adoption, tick and restoration run intact.
public static class MapChromeUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const string Prefix = "BoscaliSummer.Modules.Command.";
    private static readonly List<string> failures = new List<string>();
    private static readonly List<string> captures = new List<string> { "file\twidth\theight\tappearance\tcontext" };
    private static readonly List<string> measurements = new List<string>();
    private static int checks;
    private static string capture;
    private static IEnumerator work;
    private static Type T(string name) => typeof(AvConsole).Assembly.GetType(Prefix + name, true);
    private static Type Deck => T("Presentation.MapUi.MfdMapDeck");
    private static Type Footer => T("Presentation.MapUi.MfdMapFooter");
    private static FieldInfo Field(object owner, string name)
    {
        for (Type type = owner as Type ?? owner.GetType(); type != null; type = type.BaseType) {
            FieldInfo field = type.GetField(name, All | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException((owner as Type ?? owner.GetType()).FullName, name);
    }
    private static object Get(object owner, string name) => Field(owner, name).GetValue(owner is Type ? null : owner);
    private static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner is Type ? null : owner, value);
    private static object Call(object owner, string name, params object[] args)
    {
        Type type = owner as Type ?? owner.GetType();
        foreach (MethodInfo method in type.GetMethods(All))
            if (method.Name == name && method.GetParameters().Length == args.Length)
                return method.Invoke(owner is Type ? null : owner, args);
        throw new MissingMethodException(type.FullName, name);
    }
    private static void Check(bool condition, string detail) { checks++; if (!condition) failures.Add(capture + ": " + detail); }

    public static void Run()
    {
        try {
            if (!EnsureTmpEssentials(Run)) return;
            SetExecutablePath("MapChromePreview.exe");
            AvBundle.Load(Debug.Log); Check(AvBundle.Available && AvIcons.Available, "Embedded production fonts/icons load.");
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            Directory.CreateDirectory("renders"); MakeWallpaper();
            work = Cases(); EditorApplication.update += Step;
        } catch (Exception error) { failures.Add("Initialization: " + error); Finish(); }
    }
    private static void Step()
    {
        try { if (work.MoveNext()) return; }
        catch (Exception error) { failures.Add(capture + ": " + error); Debug.LogException(error); }
        EditorApplication.update -= Step; Finish();
    }
    private static void Finish()
    {
        File.WriteAllLines("failures.txt", failures); File.WriteAllLines("captures.tsv", captures); File.WriteAllLines("measurements.tsv", measurements);
        File.WriteAllText("result.txt", (failures.Count == 0 ? "PASS: " : "FAIL: ") + "MAP CHROME " + checks + " checks, " + (captures.Count - 1) + " captures, " + failures.Count +
            " failures. Real production deck/tray/footer primitives; synthetic native surfaces. Native discovery/input, gameplay actions and deferred destruction/live lifecycle remain untested (editor cleanup uses DestroyImmediate). Deck overlay converted to world-space only for offscreen capture after original sorting/scaler assertions.");
        EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
    }
    private static IEnumerator Cases()
    {
        foreach (AvThemeId theme in (AvThemeId[])Enum.GetValues(typeof(AvThemeId)))
            foreach (Vector2 size in new[] { new Vector2(1280,720), new Vector2(1920,1080), new Vector2(2560,1080) })
                foreach (string appearance in new[] { "default", "cover", "fit", "stretch" })
                    foreach (string context in new[] { "airbase", "spectator", "telemetry" }) {
                        AvStyleHost.SetTheme(theme);
                        IEnumerator one = Case(theme, size, appearance, context);
                        while (one.MoveNext()) yield return one.Current;
                    }
    }

    private sealed class NativeState
    {
        public RectTransform Rect;
        public Transform Parent;
        public int Sibling;
        public Vector2 Min, Max, Pivot, Size, Anchored;
        public Vector3 Position, Scale;
        public Quaternion Rotation;
        public Image Background, Disabled, ButtonImage, ButtonChild;
        public Button Button;
        public Sprite ButtonSprite;
        public CanvasGroup Group;
        public float Alpha;
        public bool Blocks;
        public int Clicks;
        public NativeState(string name, Transform parent, int index, bool group)
        {
            Rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            Rect.SetParent(parent, false); Rect.anchorMin = new Vector2(.13f,.27f); Rect.anchorMax = new Vector2(.13f,.27f);
            Rect.pivot = new Vector2(.31f,.73f); Rect.sizeDelta = new Vector2(580,32);
            Rect.anchoredPosition3D = new Vector3(17+index*11,-23-index*13,index*2);
            Rect.localRotation = Quaternion.Euler(0,0,index*3); Rect.localScale = new Vector3(.9f+index*.03f,.85f+index*.04f,1);
            Parent = Rect.parent; Sibling = Rect.GetSiblingIndex(); Min=Rect.anchorMin; Max=Rect.anchorMax; Pivot=Rect.pivot;
            Size=Rect.sizeDelta; Anchored=Rect.anchoredPosition; Position=Rect.localPosition; Scale=Rect.localScale; Rotation=Rect.localRotation;
            Background=Rect.GetComponent<Image>(); Background.color=new Color(.22f,.3f,.34f); Background.raycastTarget=false;
            var inactive = new GameObject("Native disabled decoration",typeof(RectTransform),typeof(Image)); inactive.transform.SetParent(Rect,false);
            Disabled=inactive.GetComponent<Image>(); Disabled.enabled=false; Disabled.raycastTarget=false;
            var button = new GameObject("Native button",typeof(RectTransform),typeof(Image),typeof(Button)); button.transform.SetParent(Rect,false);
            RectTransform buttonRect=(RectTransform)button.transform; buttonRect.anchorMin=new Vector2(.65f,.1f); buttonRect.anchorMax=new Vector2(.98f,.9f); buttonRect.offsetMin=buttonRect.offsetMax=Vector2.zero;
            ButtonImage=button.GetComponent<Image>(); ButtonImage.color=new Color(.12f,.29f,.35f); ButtonImage.raycastTarget=true; ButtonImage.sprite=AvSprites.GroundGradient; ButtonSprite=ButtonImage.sprite;
            Button=button.GetComponent<Button>(); Button.targetGraphic=ButtonImage; Button.transition=Selectable.Transition.None;
            Navigation nav=Button.navigation; nav.mode=Navigation.Mode.None; Button.navigation=nav; Button.onClick.AddListener(()=>Clicks++);
            Label(button.transform,"Action",index==0 ? "INSTRUMENTS" : index==1 ? "SELECT AIRCRAFT" : index==2 ? "CYCLE VIEW" : "TRACK UNIT",Vector2.zero,Vector2.one);
            var child = new GameObject("Native button decoration",typeof(RectTransform),typeof(Image)); child.transform.SetParent(button.transform,false);
            ButtonChild=child.GetComponent<Image>(); ButtonChild.color=ButtonImage.color; ButtonChild.raycastTarget=false; ((RectTransform)child.transform).sizeDelta=new Vector2(2,2);
            Label(Rect,"Native readout",index==0 ? "12:34  /  IAS 320  /  ALT 1.2 KM" : index==1 ? "AIRBASE ALPHA / READY" : index==2 ? "SPECTATOR / FREE CAMERA" : "UNIT 14 / CONDITION 86%",new Vector2(.02f,.1f),new Vector2(.63f,.9f));
            if(group) { Group=Rect.gameObject.AddComponent<CanvasGroup>(); Group.alpha=.82f; Group.blocksRaycasts=false; Group.interactable=false; Alpha=Group.alpha; Blocks=Group.blocksRaycasts; }
        }
        public void Restored()
        {
            Check(Rect.parent==Parent,"Exact native parent restored: "+Rect.name); Check(Rect.GetSiblingIndex()==Sibling,"Exact sibling restored: "+Rect.name);
            Check(Rect.anchorMin==Min && Rect.anchorMax==Max && Rect.pivot==Pivot,"Native anchors/pivot restored: "+Rect.name);
            Check(Rect.sizeDelta==Size && Rect.anchoredPosition==Anchored && Rect.localPosition==Position,"Native position/size restored: "+Rect.name);
            Check(Rect.localRotation==Rotation && Rect.localScale==Scale,"Native rotation/scale restored: "+Rect.name);
            Check(Background.enabled && !Disabled.enabled,"Original enabled/disabled images restored: "+Rect.name);
            Check(ButtonImage.enabled && ButtonChild.enabled && Button.targetGraphic==ButtonImage && ButtonImage.sprite==ButtonSprite && ButtonImage.color==new Color(.12f,.29f,.35f),"Native button graphics preserved: "+Rect.name);
            Button.onClick.Invoke(); Check(Clicks==3,"Native button callback retained before/during/after adoption: "+Rect.name);
            if(Group!=null) Check(Group.alpha==Alpha && Group.blocksRaycasts==Blocks && !Group.interactable,"Native CanvasGroup restored: "+Rect.name);
        }
    }
    private static void Label(Transform parent,string name,string value,Vector2 min,Vector2 max)
    {
        var text=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>(); text.transform.SetParent(parent,false);
        text.rectTransform.anchorMin=min; text.rectTransform.anchorMax=max; text.rectTransform.offsetMin=new Vector2(3,0); text.rectTransform.offsetMax=new Vector2(-3,0);
        text.font=AvType.Face(AvFace.Mono); text.fontSize=12; text.color=AvTheme.TextPrimary; text.text=value; text.alignment=TextAlignmentOptions.Midline; text.enableWordWrapping=false; text.raycastTarget=false;
    }
    private static void Adopt(string field,NativeState native,RectTransform slot,Vector2? size)
    {
        object[] args={Get(Footer,field),native.Rect,slot,size}; Call(Footer,"Adopt",args); Set(Footer,field,args[0]);
        if(field!="instruments") Call(args[0],"SuppressBackgroundImages");
    }
    private static IEnumerator Case(AvThemeId theme,Vector2 size,string appearance,string context)
    {
        capture="chrome-"+theme+"-"+(int)size.x+"x"+(int)size.y+"-"+appearance+"-"+context;
        var owner=new GameObject("Synthetic native scene");
        var mapCanvas=new GameObject("Native maximized map canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)).GetComponent<Canvas>(); mapCanvas.transform.SetParent(owner.transform,false);
        mapCanvas.renderMode=RenderMode.WorldSpace; mapCanvas.overrideSorting=true; mapCanvas.sortingOrder=2; ((RectTransform)mapCanvas.transform).sizeDelta=size;
        CanvasScaler sourceScaler=mapCanvas.GetComponent<CanvasScaler>(); sourceScaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; sourceScaler.referenceResolution=new Vector2(1920,1080); sourceScaler.matchWidthOrHeight=.7f;
        object columns=Call(T("Presentation.MapUi.MfdLayout"),"Resolve",size,548f); Rect mapArea=(Rect)Get(columns,"Map");
        var mapImage=new GameObject("Native terrain",typeof(RectTransform),typeof(Image)); mapImage.transform.SetParent(mapCanvas.transform,false);
        RectTransform mapRect=(RectTransform)mapImage.transform; mapRect.pivot=new Vector2(0,1); mapRect.sizeDelta=mapArea.size; mapRect.anchoredPosition=mapArea.position;
        Image terrain=mapImage.GetComponent<Image>(); terrain.color=new Color(.10f,.19f,.21f,.47f); terrain.raycastTarget=false; Color originalTerrain=terrain.color;
        var service=new GameObject("Native lifecycle shell"); service.transform.SetParent(owner.transform,false);
        DynamicMap map=service.AddComponent<ChromePreviewDynamicMap>(); map.enabled=false; map.mapImage=mapImage; SceneSingleton<DynamicMap>.i=map; Set(typeof(DynamicMap),"<mapMaximized>k__BackingField",true);
        FieldInfo backgroundField=Field(map,"mapBackground"); var backgroundGo=new GameObject("Native map background",typeof(RectTransform)); backgroundGo.transform.SetParent(mapImage.transform,false);
        Graphic nativeBackground=(Graphic)backgroundGo.AddComponent(backgroundField.FieldType); nativeBackground.color=new Color(.25f,.35f,.45f,.31f); nativeBackground.raycastTarget=false; Color originalBackground=nativeBackground.color; Set(map,"mapBackground",nativeBackground);
        backgroundGo.SetActive(false); // State restoration is checked; this diagnostic quad does not obscure the viewport.
        object settings=Activator.CreateInstance(T("Configuration.CommandSettings"),All,null,new object[]{new ConfigFile(Path.GetFullPath("chrome.cfg"),false)},null);
        Config(settings,"MapTerrainOpacity",.8f); Config(settings,"MapTrayOpacity",.45f); Config(settings,"BackgroundImage",appearance!="default"); Config(settings,"BackgroundImagePreset",3);
        Config(settings,"CustomWallpaperFile","chrome-preview.png"); Config(settings,"BackgroundImageOpacity",.7f); Config(settings,"WallpaperFitMode",appearance=="fit" ? 1 : appearance=="stretch" ? 2 : 0);
        Call(Deck,"Configure",settings); Call(Deck,"Ensure",mapCanvas,columns);
        GameObject backdrop=(GameObject)Get(Deck,"backdrop"); GameObject tray=(GameObject)Get(Deck,"tray");
        Canvas deckCanvas=backdrop.GetComponent<Canvas>();
        Check(deckCanvas.renderMode==RenderMode.ScreenSpaceOverlay && (deckCanvas.isRootCanvas || deckCanvas.overrideSorting) && deckCanvas.sortingOrder==0,"Deck is below native map/gameplay sorting.");
        Check(backdrop.transform.parent==mapCanvas.transform.parent && tray.transform.parent==mapCanvas.transform,"Deck/tray retain native canvas hierarchy.");
        Check(sourceScaler.referenceResolution==backdrop.GetComponent<CanvasScaler>().referenceResolution && sourceScaler.matchWidthOrHeight==backdrop.GetComponent<CanvasScaler>().matchWidthOrHeight,"Deck copies native scaler.");
        Check(mapImage.transform.parent==mapCanvas.transform,"Deck never reparents native map."); Check(tray.transform.GetSiblingIndex()==0,"Tray is first native-map sibling.");
        RectTransform trayRect=(RectTransform)tray.transform; Check(trayRect.sizeDelta==mapArea.size+new Vector2(20,20) && trayRect.anchoredPosition==mapArea.position+new Vector2(-10,10),"Tray bleeds exactly ten units around map.");
        Image user=(Image)Get(Deck,"backdropUserImage"); Check(user.gameObject.activeSelf==(appearance!="default"),"Wallpaper visibility matches selected appearance.");
        Call(Deck,"ApplyAppearance",settings); Call(Deck,"ApplyAppearance",settings);
        Check(nativeBackground.color==new Color(1,1,1,.68f),"Repeated appearance applies intended native background alpha.");
        var replacementGo=new GameObject("Replacement native map background",typeof(RectTransform),typeof(Image)); replacementGo.transform.SetParent(mapImage.transform,false); replacementGo.SetActive(false);
        Image replacement=replacementGo.GetComponent<Image>(); Color replacementOriginal=new Color(.17f,.29f,.43f,.37f); replacement.color=replacementOriginal; replacement.raycastTarget=false;
        Set(map,"mapBackground",replacement); Call(Deck,"ApplyAppearance",settings);
        Check(nativeBackground.color==originalBackground,"Replacing native background first restores original live instance.");
        Call(Deck,"ApplyAppearance",settings);
        if(appearance!="default") {
            Check(user.sprite!=null,"Production wallpaper loader supplies sprite.");
            if(appearance=="cover") Check(user.rectTransform.sizeDelta.y>=size.y && user.rectTransform.sizeDelta.x>=size.x && !user.preserveAspect,"Cover fills canvas without distortion.");
            else Check(user.rectTransform.anchorMin==Vector2.zero && user.rectTransform.anchorMax==Vector2.one && user.preserveAspect==(appearance=="fit"),"Fit/stretch flags and full-canvas anchors correct.");
        }
        var nativeParent=new GameObject("Original native gameplay canvas",typeof(RectTransform)); nativeParent.transform.SetParent(mapCanvas.transform,false);
        var originals=new NativeState[4]; for(int i=0;i<4;i++) { new GameObject("Original sibling spacer "+i,typeof(RectTransform)).transform.SetParent(nativeParent.transform,false); originals[i]=new NativeState(new[]{"Native instruments","Native airbase","Native spectator","Native telemetry"}[i],nativeParent.transform,i,i==3 && appearance=="fit"); originals[i].Button.onClick.Invoke(); }
        Call(Footer,"EnsureRoot",mapCanvas); Rect area=(Rect)Call(Footer,"ResolveArea",columns); Call(Footer,"PlaceFooter",area); Call(Footer,"BuildChrome",area.size); Call(Footer,"PlaceSlots",area.size);
        RectTransform footer=(RectTransform)Get(Footer,"footer"); RectTransform instruments=(RectTransform)Get(Footer,"instrumentsSlot"); RectTransform contextual=(RectTransform)Get(Footer,"contextSlot");
        Check(footer.anchoredPosition==area.position && footer.sizeDelta==area.size,"Footer uses resolved bottom reserve.");
        Check(size.x<1600 ? contextual.anchoredPosition.y<instruments.anchoredPosition.y && Mathf.Approximately(contextual.sizeDelta.x,instruments.sizeDelta.x) : Mathf.Approximately(contextual.anchoredPosition.y,instruments.anchoredPosition.y) && contextual.anchoredPosition.x>instruments.anchoredPosition.x,"Narrow two-row/wide side-by-side slots.");
        Check(instruments.GetComponent<RectMask2D>()!=null && contextual.GetComponent<RectMask2D>()!=null,"Native surface slots masked.");
        Adopt("instruments",originals[0],instruments,new Vector2(1000,Mathf.Min(60,instruments.rect.height)));
        Adopt("airbase",originals[1],contextual,null); Adopt("spectator",originals[2],contextual,null); Adopt("unitDebug",originals[3],contextual,new Vector2(contextual.rect.width,Mathf.Min(48,contextual.rect.height)));
        Adopt("instruments",originals[0],instruments,new Vector2(1000,Mathf.Min(60,instruments.rect.height)));
        Adopt("airbase",originals[1],contextual,null); Adopt("spectator",originals[2],contextual,null); Adopt("unitDebug",originals[3],contextual,new Vector2(contextual.rect.width,Mathf.Min(48,contextual.rect.height)));
        originals[1].Rect.gameObject.SetActive(context=="airbase"); originals[2].Rect.gameObject.SetActive(context=="spectator"); Set(Footer,"nextUnitProbe",float.MaxValue); Call(Footer,"Tick"); footer.SetAsLastSibling();
        CanvasGroup telemetryGroup=originals[3].Rect.GetComponent<CanvasGroup>(); Check(telemetryGroup!=null && telemetryGroup.alpha==(context=="telemetry" ? 1f : 0f) && telemetryGroup.blocksRaycasts==(context=="telemetry"),"Native contextual controls outrank telemetry.");
        for(int i=0;i<4;i++) { originals[i].Button.onClick.Invoke(); Check(originals[i].ButtonImage.enabled && originals[i].ButtonChild.enabled,"Native interactive images remain enabled during adoption."); if(i>0) Check(!originals[i].Background.enabled && !originals[i].Disabled.enabled,"Native background suppressed without enabling disabled images."); }
        foreach(GameObject passive in new[]{backdrop,tray}) { CanvasGroup group=passive.GetComponent<CanvasGroup>(); Check(!group.interactable && !group.blocksRaycasts,"Deck/tray CanvasGroup is passive."); foreach(Graphic graphic in passive.GetComponentsInChildren<Graphic>(true)) Check(!graphic.raycastTarget,"Deck/tray graphic is passive."); }
        foreach(Graphic graphic in ((RectTransform)Get(Footer,"chrome")).GetComponentsInChildren<Graphic>(true)) Check(!graphic.raycastTarget,"Footer chrome is passive."); Check(!footer.GetComponent<Graphic>().raycastTarget,"Footer base is passive.");
        if(theme==AvThemeId.Steel) Capture(owner,mapCanvas,deckCanvas,size,appearance,context);
        measurements.Add(capture+"\tfooter="+area+"\tinstruments="+instruments.sizeDelta+"\tcontext="+contextual.sizeDelta);
        Call(Footer,"Restore"); Call(Deck,"Restore");
        for(int i=0;i<4;i++) originals[i].Restored();
        Check(terrain.color==originalTerrain && terrain.enabled,"Native terrain color/enabled restored.");
        Check(nativeBackground.color==originalBackground,"Native map background color restored.");
        Check(replacement.color==replacementOriginal,"Replacement native map background color restored after repeated appearance.");
        Check(Get(Footer,"footer")==null && Get(Deck,"backdrop")==null && Get(Deck,"tray")==null,"Owned static roots released.");
        Call(Footer,"Restore"); Call(Deck,"Reset"); // Idempotent cleanup.
        Check(replacement.color==replacementOriginal && nativeBackground.color==originalBackground,"Idempotent restore/reset preserves both native colors.");
        if(originals[3].Group==null) {
            Check(telemetryGroup.alpha==1f && telemetryGroup.blocksRaycasts,"Added telemetry group reset before requested destroy.");
            Object.DestroyImmediate(telemetryGroup);
        }
        // Unity refuses deferred Destroy in edit mode. Restoration above ran unchanged;
        // this isolated fixture cleanup is deliberately not evidence of live teardown.
        if(backdrop!=null) Object.DestroyImmediate(backdrop); if(tray!=null) Object.DestroyImmediate(tray); if(footer!=null) Object.DestroyImmediate(footer.gameObject);
        yield return null;
        SceneSingleton<DynamicMap>.i=null; Set(typeof(DynamicMap),"<mapMaximized>k__BackingField",false); Object.DestroyImmediate(owner);
    }
    private static void Config(object settings,string name,object value) => settings.GetType().GetProperty(name,All).GetValue(settings).GetType().GetProperty("Value").SetValue(settings.GetType().GetProperty(name,All).GetValue(settings),value);
    private static void MakeWallpaper()
    {
        string folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.ConfigPath,"BoscaliSummer","wallpapers"));
        if(!folder.StartsWith(Directory.GetCurrentDirectory()+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Preview wallpaper path escaped isolated project: "+folder);
        Directory.CreateDirectory(folder); var texture=new Texture2D(240,80,TextureFormat.RGB24,false);
        for(int y=0;y<80;y++) for(int x=0;x<240;x++) texture.SetPixel(x,y,new Color(x/240f,y/80f,(x/40)%2==0 ? .2f : .7f));
        texture.Apply(); File.WriteAllBytes(Path.Combine(folder,"chrome-preview.png"),texture.EncodeToPNG()); Object.DestroyImmediate(texture);
    }
    private static void Capture(GameObject owner,Canvas mapCanvas,Canvas deckCanvas,Vector2 size,string appearance,string context)
    {
        var camera=new GameObject("Preview capture camera",typeof(Camera)).GetComponent<Camera>(); camera.enabled=false; camera.orthographic=true; camera.orthographicSize=size.y*.5f; camera.transform.position=new Vector3(0,0,-1000); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.04f,.065f,.075f); camera.farClipPlane=2000;
        // The owned deck's real overlay mode, sorting and scaler were checked above. Only
        // this screenshot conversion lets a render texture include its actual graphics.
        deckCanvas.GetComponent<CanvasScaler>().enabled=false; deckCanvas.renderMode=RenderMode.WorldSpace; deckCanvas.worldCamera=camera; ((RectTransform)deckCanvas.transform).sizeDelta=size; deckCanvas.transform.localPosition=new Vector3(0,0,1);
        mapCanvas.worldCamera=camera;
        foreach(Graphic graphic in owner.GetComponentsInChildren<Graphic>(true)) {
            CanvasRenderer renderer=graphic.GetComponent<CanvasRenderer>(); if(renderer==null) renderer=graphic.gameObject.AddComponent<CanvasRenderer>(); Set(graphic,"m_CanvasRenderer",renderer); if(graphic.isActiveAndEnabled) typeof(Graphic).GetMethod("OnEnable",All).Invoke(graphic,null);
        }
        Canvas.ForceUpdateCanvases(); foreach(TMP_Text text in owner.GetComponentsInChildren<TMP_Text>()) if(text.isActiveAndEnabled) text.ForceMeshUpdate();
        var target=new RenderTexture((int)size.x,(int)size.y,24); camera.targetTexture=target; camera.Render(); RenderTexture previous=RenderTexture.active; RenderTexture.active=target;
        var png=new Texture2D((int)size.x,(int)size.y,TextureFormat.RGB24,false); png.ReadPixels(new Rect(0,0,size.x,size.y),0,0); png.Apply(); File.WriteAllBytes("renders/"+capture+".png",png.EncodeToPNG()); captures.Add(capture+".png\t"+(int)size.x+"\t"+(int)size.y+"\t"+appearance+"\t"+context);
        RenderTexture.active=previous; camera.targetTexture=null; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(png); Object.DestroyImmediate(camera.gameObject);
    }
}
#endif
