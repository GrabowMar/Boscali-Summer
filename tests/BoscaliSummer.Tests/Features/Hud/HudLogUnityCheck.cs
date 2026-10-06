#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using NOAvionics;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// Production presenters, synthetic native dock/font, telemetry and message traffic.
// No native asset, live input or game-state adapter claim.
public static class HudLogUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Assembly Mod => typeof(AvConsole).Assembly;
    private static int captures, assertions;
    private static Type Type(string name) => Mod.GetType(name, true);
    private static FieldInfo Member(object owner, string name)
    {
        for (Type t = owner as Type ?? owner.GetType(); t != null; t = t.BaseType) {
            FieldInfo field = t.GetField(name, All | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException((owner as Type ?? owner.GetType()).FullName, name);
    }
    private static object Field(object owner, string name) => Member(owner, name).GetValue(owner is Type ? null : owner);
    private static void Set(object owner, string name, object value) => Member(owner, name).SetValue(owner is Type ? null : owner, value);
    private static object Call(object owner, string name, params object[] args)
    {
        Type t = owner as Type ?? owner.GetType();
        foreach (MethodInfo m in t.GetMethods(All))
            if (m.Name == name && m.GetParameters().Length == args.Length) return m.Invoke(owner is Type ? null : owner, args);
        throw new MissingMethodException(t.FullName, name);
    }
    public static void Run()
    {
        try {
            if (!EnsureTmpEssentials(Run)) return;
            SetExecutablePath("HudLogCheck.exe");
            AvBundle.Load(Debug.Log); Check(AvBundle.Available && AvIcons.Available, "Production UI assets loaded.");
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            new GameObject("Events", typeof(EventSystem));
            var colors = ScriptableObject.CreateInstance<ColorTheme>();
            Set(colors, "allClear", new Color(.65f, 1f, .87f)); Set(colors, "warning", new Color(1f, .75f, .3f));
            Set(colors, "alert", new Color(1f, .3f, .25f));
            var theme = ScriptableObject.CreateInstance<ThemeGroup>(); theme.SetColorTheme(colors);
            typeof(ThemeManager).GetProperty("Active", All).SetValue(null, theme);
            foreach (float width in new[] { 320f, 440f })
                foreach (bool details in new[] { false, true })
                    foreach (float scale in new[] { .85f, 1f, 1.2f, 1.45f }) Status(width, details, scale);
            foreach (Vector2 size in new[] { new Vector2(1280,720), new Vector2(1920,1080), new Vector2(2560,1080) }) Cluster(size);
            Contracts();
            foreach (float height in new[] { 160f, 420f, 596f })
                foreach (bool merged in new[] { false, true }) Log(height, merged);
            Wire();
            File.WriteAllText("result.txt", "PASS: " + captures + " production HUD/status/log/wire renders, " + assertions +
                " input-transparency/layout/lifecycle assertions. Synthetic native dock/font and telemetry; no native asset, live input or game adapter acceptance.");
            EditorApplication.Exit(0);
        } catch (Exception e) { File.WriteAllText("result.txt", "FAIL: " + e); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    private static Canvas MakeCanvas(string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace;
        ((RectTransform)go.transform).sizeDelta = size; return c;
    }
    private static CombatHUD NativeDock(Canvas canvas)
    {
        // Imported Assembly-CSharp MonoBehaviours cannot be added by the editor.
        // Builders only read the HUD's font metadata; a managed shell leaves that null,
        // then the owned labels receive an explicit synthetic font below.
        return (CombatHUD)FormatterServices.GetUninitializedObject(typeof(CombatHUD));
    }
    private static void SyntheticFont(RectTransform root)
    {
        var font = AvBundle.Font("NOA Rajdhani Medium SDF");
        foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>(true)) {
            label.font = font; label.fontSharedMaterial = font.material;
        }
    }
    private static void Status(float width, bool details, float scale)
    {
        Canvas canvas = MakeCanvas("Status", new Vector2(640, 600)); CombatHUD hud = NativeDock(canvas);
        var dock = new GameObject("WeaponsDock", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        dock.SetParent(canvas.transform, false); dock.sizeDelta = new Vector2(width, 40); dock.anchoredPosition = new Vector2(0, 230);
        dock.GetComponent<Image>().color = Color.black; dock.GetComponent<Image>().raycastTarget = false;
        object status = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.StatusPanel"), true);
        Call(status, "Build", hud, dock);
        SyntheticFont((RectTransform)Field(status, "root"));
        Type message = Type("BoscaliSummer.Modules.Hud.Domain.HudMessage"), tone = Type("BoscaliSummer.Core.Contracts.HudTone");
        Array rows = Array.CreateInstance(message, 4);
        string[] text = { "MISSILE WARNING / RIGHT QUARTER", "LANDING / FINAL APPROACH", "WEATHER / ICING CONDITIONS", "ACE HUNT / CONTACT REPORTED" };
        string[] detail = { "SA-19 RADAR LOCK / 9.4 KM / BREAK RIGHT", "NORTHERN AIRBASE / GEAR DOWN / 140 KT", "FREEZING LEVEL 1,500 M / CLIMB OUT OF CLOUD", "NIGHTFALL 2 / LAST OBSERVED 18 SECONDS AGO" };
        for (int i = 0; i < rows.Length; i++) {
            object row = Activator.CreateInstance(message); Set(row, "Text", text[i]); Set(row, "Detail", detail[i]);
            Set(row, "Tone", Enum.Parse(tone, i == 0 ? "Warning" : i == 2 ? "Caution" : "Info"));
            Set(row, "Bar", .3f + i * .15f); rows.SetValue(row, i);
        }
        Call(status, "Present", rows, rows.Length, details, 1, 1, scale, 0, 0);
        Canvas.ForceUpdateCanvases();
        Check(((CanvasGroup)Field(status, "group")).alpha > .2f, "Visible status opacity.");
        foreach (Graphic g in ((RectTransform)Field(status, "root")).GetComponentsInChildren<Graphic>())
            Check(!g.raycastTarget, "HUD must not intercept pointer input.");
        Capture(canvas, "HUD-status-" + width + "-" + details + "-" + scale.ToString("0.0") + ".png");
        foreach (var label in ((RectTransform)Field(status, "root")).GetComponentsInChildren<TextMeshProUGUI>())
            Check(!label.isTextOverflowing, "Status text remains readable: " + label.text);
        Call(status, "Present", rows, rows.Length, !details, 1, 1, scale, 0, 0);
        int detailCount = 0;
        foreach (var label in ((RectTransform)Field(status, "root")).GetComponentsInChildren<TextMeshProUGUI>())
            if (label.name == "Detail") detailCount++;
        Check(detailCount == (details ? 0 : rows.Length), "Detail toggling updates the existing rows.");
        Call(status, "Present", rows, 0, details, 1, 1, scale, 0, 0);
        Check(!((RectTransform)Field(status, "root")).gameObject.activeSelf, "Empty status hides owned tree.");
        Call(status, "Destroy"); Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject);
    }
    private static void Cluster(Vector2 size)
    {
        Canvas canvas = MakeCanvas("Cluster", size); CombatHUD hud = NativeDock(canvas);
        object cluster = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.ThirdPersonHudCluster"), true);
        Call(cluster, "Build", hud, canvas);
        SyntheticFont((RectTransform)Field(cluster, "root"));
        Call(cluster, "Present", true, AvUnits.Imperial, 240f, 1800f, -12f, 359.6f, .72f, 5.1f, .15f, .8f,
            true, Texture2D.whiteTexture, "SAM SITE", "9.4 KM");
        Capture(canvas, "HUD-cluster-" + size.x + "x" + size.y + ".png");
        object heading = Field(cluster, "hdg");
        Check(((TMP_Text)Field(heading, "Value")).text == "000°", "Rounded north wraps to 000 degrees.");
        Call(cluster, "WriteHeading", -1f);
        Check(((TMP_Text)Field(Field(cluster, "hdg"), "Value")).text == "359°", "Negative heading normalizes.");
        Call(cluster, "WriteHeading", 5f);
        Check(((TMP_Text)Field(Field(cluster, "hdg"), "Value")).text == "005°", "Heading retains three digits.");
        foreach (Graphic g in ((RectTransform)Field(cluster, "root")).GetComponentsInChildren<Graphic>())
            Check(!g.raycastTarget, "Flight cluster must not intercept pointer input.");
        Call(cluster, "Destroy"); Object.DestroyImmediate(canvas.gameObject);
    }
    private static void Log(float height, bool merged)
    {
        Canvas canvas = MakeCanvas("Log", new Vector2(480, height));
        Type log = Type("BoscaliSummer.Modules.Command.Presentation.MapUi.MfdLogPanel");
        var panel = new GameObject("LogSurface", typeof(RectTransform)).GetComponent<RectTransform>();
        panel.SetParent(canvas.transform, false); panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0,1);
        panel.sizeDelta = new Vector2(480,height); Set(log, "panel", panel);
        Call(log, "Rebuild", new Vector2(480,height), merged);
        var history = (IList)Field(log, "history"); history.Clear();
        for (int i=0;i<14;i++) Call(log, "AddEntry", (i % 3 == 0 ? "SAM BATTERY DESTROYED / NORTHERN RIDGE" :
            i % 3 == 1 ? "CONTRACT ACCEPTED / SURVEY THE AFTERMATH" : "HOSTILE AIRCRAFT DETECTED / SECTOR C4") + " / REPORT " + i, i%3==0);
        Call(log, "RefreshFeed", true);
        Capture(canvas, "LOG-" + height + "-" + merged + ".png");
        var scroll = (ScrollRect)Field(log,"scroll"); scroll.verticalNormalizedPosition = 0f;
        Capture(canvas, "LOG-" + height + "-" + merged + "-bottom.png");
        Canvas.ForceUpdateCanvases();
        Call(log, "AddEntry", "AIRBASE NORTHERN CAPTURED / NEW TRAFFIC", false);
        Call(log, "RefreshFeed", true);
        Canvas.ForceUpdateCanvases();
        Check(scroll.verticalNormalizedPosition < .95f, "New traffic does not jump a scrolled reader to latest.");
        Call(log, "TogglePause");
        string held = ((TMP_Text)Field(log, "body")).text;
        Call(log, "AddEntry", "HOSTILE AIRCRAFT DESTROYED / WHILE HELD", true);
        Call(log, "RefreshFeed", true);
        Check(((TMP_Text)Field(log, "body")).text == held, "Pause freezes local traffic.");
        Check(((TMP_Text)Field(log, "trafficCount")).text.Contains("1 NEW"), "Held view reports incoming traffic.");
        Capture(canvas, "LOG-" + height + "-" + merged + "-held.png");
        Call(log, "Latest");
        Check(!(bool)Field(log, "paused") && (scroll.content.rect.height <= scroll.viewport.rect.height + .5f ||
            scroll.verticalNormalizedPosition > .99f), "Latest resumes the live edge.");
        Check(scroll.content.rect.height >= scroll.viewport.rect.height, "Log content retains readable scroll area.");
        foreach (Selectable s in panel.GetComponentsInChildren<Selectable>())
            Check(s.navigation.mode == Navigation.Mode.None, "Log controls yield flight-stick focus.");
        Call(log,"Restore"); Object.DestroyImmediate(canvas.gameObject);
    }
    private static void Wire()
    {
        Canvas canvas = MakeCanvas("Wire", new Vector2(1280,720));
        Type layout = Type("BoscaliSummer.Modules.Command.Presentation.MapUi.MfdLayout");
        object columns = Call(layout,"Resolve",new Vector2(1280,720),480f);
        Type wire=Type("BoscaliSummer.Modules.Command.Presentation.MapUi.MfdNewsTicker");
        Call(wire,"Ensure",canvas,columns,null);
        var feed=Field(wire,"feed"); Call(feed,"IngestGameEvent","HOSTILE SAM LAUNCH / RIDGE BATTERY",0f);
        Set(wire,"currentMarqueeString",Call(feed,"BuildMarqueeText",5));
        foreach(string label in new[]{"labelA","labelB"}) ((TMP_Text)Field(wire,label)).text=(string)Field(wire,"currentMarqueeString");
        Capture(canvas,"WIRE-1280x720.png");
        Call(wire,"Reset"); Object.DestroyImmediate(canvas.gameObject);
    }
    private static void Contracts()
    {
        float oldSize = PlayerSettings.overlayTextSize;
        PlayerSettings.overlayTextSize = 32f;
        try {
            Canvas canvas = MakeCanvas("Contract overlays", new Vector2(1280,720));
            Type styleOwner = Type("BoscaliSummer.Core.Game.VanillaHudStyle");
            object style = Activator.CreateInstance(styleOwner.GetNestedType("CockpitStyle", All));
            var font = AvBundle.Font("NOA Rajdhani Medium SDF");
            Set(style,"Font",font); Set(style,"FontMaterial",font.material); Set(style,"Colour",Color.cyan);
            Set(style,"Pointer",AvSprites.White); Set(style,"Dot",AvSprites.White); Set(style,"Ring",AvSprites.White);
            Set(style,"PointerSize",new Vector2(12,12)); Set(style,"DotSize",new Vector2(4,4));
            Set(style,"RingSize",new Vector2(32,32)); Set(style,"LabelScale",.5f);
            Type cardType=Type("BoscaliSummer.Modules.DynamicOperations.Domain.ContractCard");
            Type viewType=Type("BoscaliSummer.Core.Contracts.SecondaryObjectiveView");
            Type markerType=Type("BoscaliSummer.Modules.DynamicOperations.Runtime.ContractMarker");
            for(int i=0;i<3;i++) {
                object[] facts={i+26,"SURVEY THE AFTERMATH / NORTHERN DEPOT","Synthetic contract briefing.","NORTHERN DEPOT",
                    i==2?"RETURN TO BASE":"IN PROGRESS","$1,500 + 125 XP",.55f, i==1?42f:192f,1500,125,false,false,true,true,0f,0f,500f,"NIGHTFALL-2"};
                object view=Activator.CreateInstance(viewType,All,null,facts,null);
                object card=Activator.CreateInstance(cardType,All,null,new[]{view},null);
                object marker=Activator.CreateInstance(markerType,All,null,new object[]{canvas.transform,"Contract "+i,style},null);
                float x=i==1?440f:-440f, y=170f-i*170f;
                Call(marker,"SetVisible",true); Call(marker,"Place",x,y);
                Call(marker,"SetIcons",Vector3.forward,new Vector3(500,0,5000),90f);
                Call(marker,"SetLabel",card,9400f,i!=2,x,470f,i==1,i==1?Color.yellow:Color.cyan);
                Call(marker,"GlideLabel");
                if(i==1) Check(((RectTransform)Field(marker,"label")).pivot.x==1f,"Contract right-edge label flips inward.");
            }
            Capture(canvas,"CONTRACT-HUD-1280x720.png");
            foreach(Graphic g in canvas.GetComponentsInChildren<Graphic>()) Check(!g.raycastTarget,"Contract overlay yields flight input.");
            Object.DestroyImmediate(canvas.gameObject);

            canvas=MakeCanvas("Contract map tags",new Vector2(1280,720));
            object mapStyle=Activator.CreateInstance(styleOwner.GetNestedType("MapStyle",All));
            foreach(string icon in new[]{"WaypointIcon","DestroyIcon","ReconIcon","CaptureIcon","Ring"}) Set(mapStyle,icon,AvSprites.White);
            Set(mapStyle,"LabelFont",Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            Set(mapStyle,"LabelSize",24f); Set(mapStyle,"LabelColour",Color.cyan); Set(mapStyle,"LabelScale",.5f);
            Type tagType=Type("BoscaliSummer.Modules.DynamicOperations.Runtime.ContractMapTag");
            object[] mapFacts={26,"SURVEY THE AFTERMATH / NORTHERN DEPOT","Synthetic briefing.","DEPOT","IN PROGRESS","$1,500",.55f,192f,1500,125,false,false,true,true,0f,0f,500f,"NIGHTFALL-2"};
            object mapView=Activator.CreateInstance(viewType,All,null,mapFacts,null);
            object mapCard=Activator.CreateInstance(cardType,All,null,new[]{mapView},null);
            object tag=Activator.CreateInstance(tagType,All,null,new object[]{canvas.transform,"Map contract",mapStyle,AvSprites.White},null);
            Call(tag,"SetVisible",true); Call(tag,"Apply",mapCard,mapStyle,Color.cyan);
            foreach(float zoom in new[]{.5f,1f,2f}) {
                Call(tag,"Place",0f,0f,1f/zoom,zoom); Call(tag,"SetRing",true,120f,new Color(0,1,1,.15f));
                Capture(canvas,"CONTRACT-MAP-zoom-"+zoom+".png");
                foreach(Graphic g in canvas.GetComponentsInChildren<Graphic>()) Check(!g.raycastTarget,"Contract map labels yield native clicks.");
            }
            Object.DestroyImmediate(canvas.gameObject);
        } finally { PlayerSettings.overlayTextSize=oldSize; }
    }
    private static void Capture(Canvas canvas, string path)
    {
        foreach(AvReveal r in canvas.GetComponentsInChildren<AvReveal>(true)) r.Finish();
        Canvas.ForceUpdateCanvases();
        foreach (var label in canvas.GetComponentsInChildren<TextMeshProUGUI>()) {
            if (string.IsNullOrEmpty(label.text)) continue;
            label.ForceMeshUpdate();
            Check(label.textInfo.characterCount > 0, "Visible text has geometry: " + label.text);
        }
        Vector2 size=((RectTransform)canvas.transform).rect.size;
        var camera = OrthoCamera("Camera", size.y / 2, new Color(.08f, .12f, .15f));
        CapturePng(camera, (int)size.x, (int)size.y, path);
        Object.DestroyImmediate(camera.gameObject); captures++;
    }
    private static void Check(bool okay,string message) { assertions++; if(!okay) throw new Exception(message); }
}
#endif

