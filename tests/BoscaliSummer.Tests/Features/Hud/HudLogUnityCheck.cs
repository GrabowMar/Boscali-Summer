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
// The TAC BOARD scope pages render from fixtures, one partial file per page: HudLogUnityCheck.Scope.cs holds the shared harness and the
// -pages filter, HudLogUnityCheck.Scope.<Name>.cs the fixture of each page (see Run-HudLogUnityCheck.ps1 -Pages).
public static partial class HudLogUnityCheck
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
        // No exact arity: a method whose extra trailing parameters are optional (reflection does not fill defaults by itself).
        foreach (MethodInfo m in t.GetMethods(All)) {
            ParameterInfo[] p = m.GetParameters();
            if (m.Name != name || p.Length <= args.Length || !p[args.Length].IsOptional) continue;
            object[] full = new object[p.Length];
            args.CopyTo(full, 0);
            for (int i = args.Length; i < p.Length; i++) full[i] = p[i].DefaultValue;
            return m.Invoke(owner is Type ? null : owner, full);
        }
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
            // The visor mock's ink, amber and red (LAYOUT.md 1: #7fdf9a, #ffd84a, #ffa094), so a render lines up with the shots colour for colour. The board takes them from the theme; they are not in the code.
            Set(colors, "allClear", (Color)new Color32(127, 223, 154, 255)); Set(colors, "warning", (Color)new Color32(255, 216, 74, 255));
            Set(colors, "alert", (Color)new Color32(255, 160, 148, 255));
            var theme = ScriptableObject.CreateInstance<ThemeGroup>(); theme.SetColorTheme(colors);
            typeof(ThemeManager).GetProperty("Active", All).SetValue(null, theme);
            string[] pages = ScopePagesArg();
            if (pages.Length == 0) {   // the whole check; with -pages only the scope fixtures of those pages run
                foreach (Vector2 size in new[] { new Vector2(1280,720), new Vector2(1920,1080), new Vector2(2560,1080) }) Cluster(size);
                Contracts();
                foreach (float height in new[] { 160f, 420f, 596f })
                    foreach (bool merged in new[] { false, true }) Log(height, merged);
                Wire();
            }
            VanguardGauges();
            ScopeShots(pages);
            File.WriteAllText("result.txt", "PASS: " + captures + " production HUD/scope/log/wire renders, " + assertions +
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
        var font = HudFont();   // the HUD's face is mono: proportional type hides collisions
        foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>(true)) {
            label.font = font; label.fontSharedMaterial = font.material;
        }
    }
    private const string HudDomain = "BoscaliSummer.Modules.Hud.Domain.";
    private static object Boxed(string name) => Activator.CreateInstance(Type(HudDomain + name));
    private static object EnumOf(string name, string value) => Enum.Parse(Type(HudDomain + name), value);
    private static object Tone(string tone) => Enum.Parse(Type("BoscaliSummer.Core.Contracts.HudTone"), tone);
    /// <summary>A hazy sky behind the HUD (near white when asked): thin ink on a bright backdrop is the case the scrims and tones are for.</summary>
    private static void Sky(Canvas canvas, bool white = false)
    {
        var texture = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++) texture.SetPixel(0, y, white ? Color.Lerp(new Color(.94f, .95f, .96f), new Color(.86f, .88f, .91f), y / 63f)
            : Color.Lerp(new Color(.82f, .89f, .95f), new Color(.40f, .58f, .82f), y / 63f));
        texture.Apply();
        var sky = new GameObject("Sky", typeof(RectTransform), typeof(Image));
        sky.transform.SetParent(canvas.transform, false); sky.transform.SetAsFirstSibling();
        var rect = (RectTransform)sky.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = sky.GetComponent<Image>(); image.raycastTarget = false; image.sprite = Sprite.Create(texture, new Rect(0, 0, 1, 64), new Vector2(.5f, .5f));
    }
    private static TMP_FontAsset HudFont() => AvBundle.Font("NOA JetBrains Mono Regular SDF");
    /// <summary>A stand-in for one of the native weapon labels: Build reads only its font and its size.</summary>
    private static TextMeshProUGUI NativeLabel(Transform parent, string name, TMP_FontAsset font, float size)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(parent, false); label.font = font; label.fontSharedMaterial = font.material; label.fontSize = size; label.text = "X";
        return label;
    }
    /// <summary>
    /// Whether a word fits its box once its allowed shrink is used. TMP reports an autosized label's preferred width at its largest size,
    /// and <c>isTextOverflowing</c> is false for Overflow-mode and autosized text, so neither says whether a word would be cut.
    /// </summary>
    private static bool Fits(TMP_Text t, float tolerance)
    {
        float width = t.preferredWidth, smallest = t.enableAutoSizing && t.fontSizeMax > 0f ? width * t.fontSizeMin / t.fontSizeMax : width;
        return smallest <= t.rectTransform.rect.width * tolerance;
    }
    private sealed class ClusterCase
    {
        public string Name, SpdCaption, AltCaption, FuelScale;
        public float Speed, Alt, Radar, Climb, Mach, G, Fuel, Bank, Slip, Aoa, Scale = 1f;
        public bool Bingo, Joker, White;
    }
    /// <summary>
    /// The third-person cluster on a hazy sky (blue, or near-white where thin ink is hardest to read): cruising with AoA and a real G;
    /// every caution lit (over-G, PULL UP, BINGO fuel); low ground alone; over-G with JOKER at once; and once at the player's larger text setting.
    /// </summary>
    private static void Cluster(Vector2 size)
    {
        var cases = new[] {
            new ClusterCase { Name = "cruise", Speed = 240f, Alt = 1800f, Radar = 900f, Climb = 3f, Mach = .72f, G = 1.1f, Fuel = .62f, Bank = 6f, Slip = -1f, Aoa = 3.4f, SpdCaption = "SPD", AltCaption = "ALT", FuelScale = "FUEL 62" },
            new ClusterCase { Name = "cautions", Speed = 130f, Alt = 420f, Radar = 120f, Climb = -32f, Mach = .35f, G = 7.6f, Fuel = .09f, Bank = 38f, Slip = 4f, Aoa = 14.2f, Bingo = true, Joker = true, SpdCaption = "OVER-G", AltCaption = "PULL UP", FuelScale = "BINGO 9" },
            new ClusterCase { Name = "low", Speed = 130f, Alt = 420f, Radar = 120f, Climb = -8f, Mach = .35f, G = 1.4f, Fuel = .62f, Bank = 12f, Slip = 2f, Aoa = 9f, SpdCaption = "SPD", AltCaption = "LOW R ", FuelScale = "FUEL 62" },
            new ClusterCase { Name = "joker", Speed = 240f, Alt = 1800f, Radar = 900f, Climb = 3f, Mach = .72f, G = 7.6f, Fuel = .20f, Bank = 50f, Slip = 0f, Aoa = 6f, Joker = true, SpdCaption = "OVER-G", AltCaption = "ALT", FuelScale = "JOKER 20" },
            new ClusterCase { Name = "haze", Speed = 130f, Alt = 420f, Radar = 120f, Climb = -32f, Mach = .35f, G = 7.6f, Fuel = .09f, Bank = 38f, Slip = 4f, Aoa = 14.2f, Bingo = true, Joker = true, White = true, SpdCaption = "OVER-G", AltCaption = "PULL UP", FuelScale = "BINGO 9" },
            new ClusterCase { Name = "large-text", Speed = 240f, Alt = 1800f, Radar = 900f, Climb = 3f, Mach = .72f, G = 1.1f, Fuel = .62f, Bank = 6f, Slip = -1f, Aoa = 3.4f, Scale = 1.5f, SpdCaption = "SPD", AltCaption = "ALT", FuelScale = "FUEL 62" },
        };
        System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (ClusterCase c in cases) {
            if (c.Name == "large-text" && size.x != 1920f) continue;
            Canvas canvas = MakeCanvas("Cluster", size); CombatHUD hud = NativeDock(canvas);
            Sky(canvas, c.White);
            object cluster = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.ThirdPersonHudCluster"), true);
            Call(cluster, "Build", hud, canvas);
            SyntheticFont((RectTransform)Field(cluster, "root"));
            object flight = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.ThirdPersonHudCluster+Flight"));
            foreach (var kv in new[] { ("SpeedMps", c.Speed), ("AltitudeM", c.Alt), ("RadarAltM", c.Radar), ("ClimbMps", c.Climb), ("HeadingDeg", 359.6f), ("Mach", c.Mach),
                ("G", c.G), ("GLimit", 9f), ("Fuel", c.Fuel), ("Throttle", .8f), ("BankDeg", c.Bank), ("SlipDeg", c.Slip), ("AoaDeg", c.Aoa) })
                Set(flight, kv.Item1, kv.Item2);
            Set(flight, "FuelBingo", c.Bingo); Set(flight, "FuelJoker", c.Joker);
            Call(cluster, "Present", true, AvUnits.Imperial, flight, TargetFeed(), "SAM SITE", 9400f, 120f, c.Scale);
            Canvas.ForceUpdateCanvases();
            string tag = c.Name + "-" + size.x + "x" + size.y;
            Capture(canvas, "HUD-cluster-" + tag + ".png");
            object spd = Field(cluster, "spd"), alt = Field(cluster, "alt");
            Check(((TMP_Text)Field(Field(cluster, "hdg"), "Value")).text.StartsWith("000°"), "Rounded north wraps to 000 degrees.");
            Call(cluster, "WriteHeading", -1f);
            Check(((TMP_Text)Field(Field(cluster, "hdg"), "Value")).text.StartsWith("359°"), "Negative heading normalizes.");
            Call(cluster, "WriteHeading", 5f);
            Check(((TMP_Text)Field(Field(cluster, "hdg"), "Value")).text.StartsWith("005°"), "Heading retains three digits.");
            string altCaption = ((TMP_Text)Field(alt, "Caption")).text, spdCaption = ((TMP_Text)Field(spd, "Caption")).text;
            Check(c.AltCaption == "LOW R " ? altCaption.StartsWith("LOW R ") : altCaption == c.AltCaption, "The ALT caption names the ground cue in words: " + altCaption + " (" + tag + ")");
            Check(spdCaption == c.SpdCaption, "The SPD caption says OVER-G in words when the box is amber: " + spdCaption + " (" + tag + ")");
            Check((((TMP_Text)Field(spd, "CaptionRight")).text ?? "") == (c.SpdCaption == "OVER-G" ? "" : "AOA " + c.Aoa.ToString("0.0", inv) + "°"), "AoA reads in the SPD caption row (OVER-G takes the row): " + ((TMP_Text)Field(spd, "CaptionRight")).text);
            Check(((TMP_Text)Field(spd, "SubLine")).text == "M " + c.Mach.ToString("0.00", inv) + "  G " + c.G.ToString("0.0", inv), "G is the load factor: " + ((TMP_Text)Field(spd, "SubLine")).text);
            Check(((TMP_Text)Field(alt, "BarLabel")).text == c.FuelScale, "The fuel scale says BINGO / JOKER as the board does: " + ((TMP_Text)Field(alt, "BarLabel")).text);
            foreach (object box in new[] { spd, alt })
                foreach (string name in new[] { "Caption", "CaptionRight", "Value", "SubLine", "BarLabel" }) {
                    var label = (TMP_Text)Field(box, name);
                    if (label != null && !string.IsNullOrEmpty(label.text))
                        Check(Fits(label, 1f), "A cluster word fits its box: " + name + " [" + label.text + "] " + label.preferredWidth + " > " + label.rectTransform.rect.width + " (" + tag + ")");
                }
            // The SPD caption row holds the caption and the AoA: they must not run into each other.
            TMP_Text left = (TMP_Text)Field(spd, "Caption"), right = (TMP_Text)Field(spd, "CaptionRight");
            left.ForceMeshUpdate(); right.ForceMeshUpdate();
            Check(left.renderedWidth + right.renderedWidth + 6f <= ((RectTransform)Field(spd, "Root")).rect.width - 14f, "The SPD caption and the AoA do not collide: " + left.renderedWidth + " + " + right.renderedWidth + " (" + tag + ")");
            foreach (Graphic g in ((RectTransform)Field(cluster, "root")).GetComponentsInChildren<Graphic>())
                Check(!g.raycastTarget, "Flight cluster must not intercept pointer input.");
            Call(cluster, "Destroy"); Object.DestroyImmediate(canvas.gameObject);
        }
    }
    private static Texture2D targetFeed;
    /// <summary>A stand-in for the target camera's picture (pale sky over dark ground, not the flat white of a missing one): what the third-person target card is judged against.</summary>
    private static Texture2D TargetFeed()
    {
        if (targetFeed != null) return targetFeed;
        targetFeed = new Texture2D(1, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 32; y++) targetFeed.SetPixel(0, y, y < 14 ? Color.Lerp(new Color(.18f, .24f, .17f), new Color(.30f, .38f, .26f), y / 13f) : Color.Lerp(new Color(.55f, .68f, .82f), new Color(.78f, .86f, .93f), (y - 14) / 17f));
        targetFeed.Apply();
        return targetFeed;
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
            Set(style,"Font",font); Set(style,"FontMaterial",font.material); Set(style,"LabelScale",.5f);
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
                Call(marker,"SetPointer",i==1?0f:90f,i==1);
                if(i==0) {
                    var lens=new GameObject("Contract lens").AddComponent<Camera>(); lens.fieldOfView=60f; lens.enabled=false;
                    Call(marker,"SetRing",500f,3000f,720f,lens,false,Color.cyan);
                    Object.DestroyImmediate(lens.gameObject);
                    Check(((RectTransform)Field(marker,"ring")).gameObject.activeSelf,"Contract area ring draws inside its fade range.");
                }
                Call(marker,"SetLabel",card,i==0?300f:9400f,i!=2,x,470f,i==1,Color.cyan,i==1?Color.yellow:Color.cyan);
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

