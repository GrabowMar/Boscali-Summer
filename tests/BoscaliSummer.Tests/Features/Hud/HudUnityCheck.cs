#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BoscaliSummer.Features.Hud.Configuration;
using BoscaliSummer.Features.Hud.Presentation;
using BoscaliSummer.Features.Hud.Runtime;
using BoscaliSummer.Features.Hud.Domain;
using BoscaliSummer.Framework.Contracts;
using NOAvionics.Ui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
public static class HudUnityCheck
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    public static void Run()
    {
        try
        {
            if (Shader.Find("TextMeshPro/Distance Field") == null)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                AssetDatabase.importPackageCompleted += _ => EditorApplication.delayCall += Run;
                AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
                return;
            }
            var pathMethod = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var args = new object[pathMethod.GetParameters().Length]; args[0] = Path.GetFullPath("HudCheck.exe");
            for (int i = 1; i < args.Length; i++) args[i] = pathMethod.GetParameters()[i].DefaultValue;
            pathMethod.Invoke(null, args);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvFont.Font = TMP_FontAsset.CreateFontAsset(new Font("C:/Windows/Fonts/consola.ttf"));
            var config = new HudSettings(new ConfigFile(Path.GetFullPath("hud-check-" + Guid.NewGuid().ToString("N") + ".cfg"), false));
            CheckViews(config); CheckService(config); CheckProjection(); CheckNativeOwnership(); CheckTelemetry(); CheckVisibility(config); CheckMissileOrder();
            File.WriteAllText("result.txt", "PASS: production view reflow/identity; fixed geometry on lost video/tracks/marks; passive input; canonical typography; no native art; camera texture ownership; data-only acquisition; stale handles; same-frame camera changes and target labels reproject together; origin-shift reprojection preserves jamming offset and never replays gameplay/input; native visibility restoration; stable missile order; compact status and borderless camera; direct ownship damage sampling; native-style readout ribbons, compact idle stack and duplicate climb-rate suppression. Game adapters are fixtures. In-game acceptance remains required.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { File.WriteAllText("result.txt", "FAIL: " + error); Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void CheckViews(HudSettings config)
    {
        var parent = new GameObject("HUD owner");
        var aircraft = new GameObject("Ownship").AddComponent<Aircraft>();
        var incoming = new GameObject("Detected missile").AddComponent<Missile>(); incoming.transform.position = new Vector3(1600,0,0);
        aircraft.warnings.knownMissiles.Add(incoming);
        var missiles = new MissileTelemetry(); missiles.Read(aircraft, true);
        var board = new ThirdPersonTargetBoard(parent.transform);
        var video = new RenderTexture(320,180,0); video.Create();
        RenderTexture.active = video; GL.Clear(true,true,new Color(.18f,.24f,.28f)); RenderTexture.active = null;
        var systems = new SystemsReading { Valid = true, FaultsAvailable = true, Parts = 24, Damaged = 2, Failures = 1 };
        board.Present(config, systems, missiles, video, "FORWARD", "MARK 4.2 km   12s"); Canvas.ForceUpdateCanvases();
        var surface = Field<HudSurface>(board, "surface"); var panel = Field<RectTransform>(board, "panel");
        Require(surface.Root.activeSelf, "Dock visible with camera");
        Require(Field<RectTransform>(board,"video").anchoredPosition.y == 0,"Camera aligns with the bottom safe edge; optional observations grow upward");
        Require(surface.Root.transform.Find("Dock/Camera slot/Video ground") == null,"Camera picture has no padded background frame");
        Require(Field<RectTransform>(board,"instrumentBacking").sizeDelta.y < panel.sizeDelta.y,"Housing ends above the camera picture");
        int objects = surface.Root.GetComponentsInChildren<Transform>(true).Length;
        Vector2 size = panel.sizeDelta, position = panel.anchoredPosition;
        missiles.Read(null,false); board.Present(config, default, missiles, null, null, null);
        Require(panel.sizeDelta == size && panel.anchoredPosition == position, "Data loss cannot move or resize dock");
        Require(Field<RawImage>(board,"feed").texture == null && Field<TMP_Text>(board,"standby").gameObject.activeSelf, "Camera loss clears stale image and displays standby");
        for (int i=0;i<100;i++)
        {
            config.BoardCorner.Value = i%4; config.BoardScaleStep.Value = i%4; config.BoardInsetX.Value = i*6;
            config.AirframeEnabled.Value = i%2==0; config.ThirdPersonShotsEnabled.Value = i%3==0;
            board.Present(config, systems, missiles, video, "FORWARD", null);
            Require(surface.Root.GetComponentsInChildren<Transform>(true).Length == objects, "Settings cannot rebuild widget tree");
        }
        config.BoardCorner.Value = config.BoardInsetX.Value = 0; config.BoardScaleStep.Value = 1;
        config.AirframeEnabled.Value = config.ThirdPersonShotsEnabled.Value = true;
        missiles.Read(aircraft,true); board.Present(config,systems,missiles,video,"FORWARD","MARK 4.2 km   12s");
        ValidateGraphics(surface); Render(surface.Canvas,panel,"tactical-dock.png");
        var displayed = Field<System.Collections.Generic.List<MissileTelemetry.ShotEntry>>(missiles,"shown");
        for (int i=0;i<6;i++)
            displayed.Add(new MissileTelemetry.ShotEntry { Outbound=i>=3, Seeker=i%2==0?"SARH":"IR", RangeText=(i+2)+" km",
                Track=new ShotTrack { Fraction=(i+1)/7f, Eta=12+i*8 } });
        displayed.RemoveAt(0);
        missiles.GetType().GetField("shownInbound",Private).SetValue(missiles,3);
        missiles.GetType().GetField("shownOutbound",Private).SetValue(missiles,3);
        board.Present(config,systems,missiles,video,"FORWARD","MARK 4.2 km   12s"); Render(surface.Canvas,panel,"missile-lanes.png");
        missiles.Read(null,false);
        board.Present(config,new SystemsReading { Valid=true, FaultsAvailable=true },missiles,video,"FORWARD",null);
        Require(Field<RectTransform>(board,"instrumentBacking").sizeDelta.y <= 44,"Idle tactical strip cannot reserve empty threat rows or duplicate nominal counters");
        Render(surface.Canvas,panel,"tactical-idle.png");
        board.Present(config,default,missiles,null,null,null); Render(surface.Canvas,panel,"camera-loss.png");
        config.AirframeEnabled.Value = config.ThirdPersonShotsEnabled.Value = config.MarkEnabled.Value = false;
        board.Present(config,default,missiles,video,"FORWARD",null);
        Require(surface.Root.activeSelf && Field<RawImage>(board,"feed").enabled,"Camera-only mode works");
        Render(surface.Canvas,panel,"camera-only.png"); board.Hide(); Require(Field<RawImage>(board,"feed").texture == null,"Hide clears borrowed texture");
        board.Destroy(); Require(video.IsCreated(),"View cannot release borrowed render texture");
        var flight = new FlightInstrumentView(parent.transform);
        var reading = new FlightReading { Valid=true, Speed=260, Altitude=1543, RadarAltitude=1397, Climb=41, Heading=278, Fuel=.5f, Throttle=1, G=1, Mach=.78f, AoA=2.4f, Pitch=4, Roll=-15 };
        flight.Present(reading,false,1,0,1);
        var fs=Field<HudSurface>(flight,"surface"); ValidateGraphics(fs);
        Require(fs.Root.GetComponentInChildren<TMP_Text>().color != NOAvionics.Ui.AvTheme.TextPrimary,"Flight instruments use HUD ink instead of white dashboard type");
        foreach (HudPanel backing in fs.Root.GetComponentsInChildren<HudPanel>())
            Require(backing.rectTransform.rect.height <= 28,"Glass flight instruments use value ribbons, not large cards");
        Render(fs.Canvas,Field<RectTransform>(flight,"left"),"flight-energy.png");
        Render(fs.Canvas,Field<RectTransform>(flight,"right"),"flight-altitude.png");
        Render(fs.Canvas,(RectTransform)fs.Transform,"flight-layout.png"); flight.Hide();
        var status=new StatusFeedView(parent.transform);
        var messages=new[] { new HudMessage { Tone=HudTone.Warning, Text="INBOUND MISSILE", Detail="Radar warning / defensive action" },
            new HudMessage { Tone=HudTone.Info,Text="SUPPORT NET READY",Detail="Allocation available",Bar=1 },
            new HudMessage { Tone=HudTone.Info,Text="MAIN EFFORT / AIRSTRIP",Detail="Capture zone \u00b7 21 km" } };
        status.Present(messages,3,config,default);
        var ss=Field<HudSurface>(status,"surface");
        Require(Field<RectTransform>(status,"panel").sizeDelta.x <= 300 && Field<RectTransform>(status,"panel").sizeDelta.y <= 116,"Status strip remains compact at normal scale");
        ValidateGraphics(ss); Render(ss.Canvas,Field<RectTransform>(status,"panel"),"status-feed.png");
        status.Present(new[] { new HudMessage { Tone=HudTone.Info, Text="SUPPORT - NET READY", Detail="ALLOCATION 100%", Bar=1 },
            new HudMessage { Tone=HudTone.Info, Text="MAIN EFFORT - NONE" } },2,config,default);
        Require(Field<RectTransform>(status,"panel").sizeDelta.y == 68,"Normal two-feed status removes the blank detail row");
        Render(ss.Canvas,Field<RectTransform>(status,"panel"),"status-idle.png");
        status.Destroy(); flight.Destroy(); Object.DestroyImmediate(parent); Object.DestroyImmediate(aircraft.gameObject); Object.DestroyImmediate(incoming.gameObject);
        video.Release(); Object.DestroyImmediate(video);
    }
    private static void ValidateGraphics(HudSurface surface)
    {
        Canvas.ForceUpdateCanvases();
        foreach (HudPanel panel in surface.Root.GetComponentsInChildren<HudPanel>())
        {
            Require(panel.canvasRenderer != null, "Custom panel requires an actual CanvasRenderer");
            Mesh mesh = panel.canvasRenderer.GetMesh();
            Require(mesh != null && mesh.vertexCount > 0, "Configured panel must submit visible geometry, not only have a valid rectangle");
        }
        foreach (Graphic graphic in surface.Root.GetComponentsInChildren<Graphic>(true)) Require(!graphic.raycastTarget,"HUD must not intercept flight input");
        Require(!surface.Group.blocksRaycasts && surface.Root.GetComponent<GraphicRaycaster>() == null,"Passive canvas");
        foreach (TMP_Text label in surface.Root.GetComponentsInChildren<TMP_Text>(true))
            Require(label.font == AvFont.Font && label.fontSharedMaterial == AvFont.Font.material,"Canonical font/material pair");
        foreach (Image image in surface.Root.GetComponentsInChildren<Image>(true)) Require(image.sprite == null,"No copied native art");
        Require(surface.Root.GetComponentInChildren<StatusDisplay>(true) == null,"No native display behavior clone");
    }
    private static void CheckService(HudSettings config)
    {
        var host=new GameObject("Status service").AddComponent<HudBoard>(); host.Configure(config,null);
        host.DeclareChannel("test","Test"); var line=host.Acquire("owner","test","one"); line.Set(HudTone.Info,"READY",null,1);
        Require(host.transform.childCount == 0,"Acquiring/setting data must not create UI");
        host.ResetForScene(); line.Set(HudTone.Warning,"Ghost",null,1);
        Require(host.Channels.Count == 1,"Channel declarations persist across scene reset");
        Require(!ReferenceEquals(line,host.Acquire("owner","test","one")),"Scene reset invalidates old handles"); Object.DestroyImmediate(host.gameObject);
    }
    private static void CheckProjection()
    {
        var cameraObject=new GameObject("Camera fixture"); var camera=cameraObject.AddComponent<Camera>();
        var manager=cameraObject.AddComponent<CameraStateManager>(); HUDUnitMarker.Camera=camera; manager.mainCamera=camera;
        var ui=new GameObject("Native HUD",typeof(RectTransform),typeof(Canvas)); ui.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
        var hud=ui.AddComponent<FlightHud>(); SceneSingleton<FlightHud>.i=hud;
        var own=new GameObject("Aircraft").AddComponent<Aircraft>();
        var target=new GameObject("Known target").AddComponent<Unit>(); target.transform.position=new Vector3(8,2,80);
        var icon=new GameObject("Native marker",typeof(RectTransform),typeof(Image)).GetComponent<Image>(); icon.transform.SetParent(ui.transform,false);
        var marker=new HUDUnitMarker { unit=target,image=icon }; var projection=new NativeHudProjection();
        var combat=ui.AddComponent<CombatHUD>(); combat.aircraft=own; combat.Selected=marker; SceneSingleton<CombatHUD>.i=combat;
        var label=new GameObject("Target label",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(ui.transform,false); combat.SetLabel(label);
        marker.UpdatePosition(own.NetworkHQ,default,Vector3.forward); projection.Record(marker);
        Vector3 jam=new Vector3(5,-3,0); icon.transform.position+=jam; projection.RecordDistortion(marker,jam);
        Vector3 shift=new Vector3(1024,0,0); cameraObject.transform.position-=shift; target.transform.position-=shift; ui.transform.position-=shift; Datum.originPosition-=shift;
        FlightHud.Updates=HeadMountedDisplay.Updates=HUDAppManager.Updates=CombatHUD.Updates=0;
        FlightHud.Projection=()=>projection.Render(own,manager); // Force reentrancy.
        projection.Render(own,manager); projection.Render(own,manager);
        Vector3 expected=camera.WorldToScreenPoint(target.transform.position); expected.z=0;
        Require(Vector3.Distance(icon.transform.position,expected+jam)<.1f,"Origin shift must not erase native jamming offsets or leave stale projections");
        Require(marker.Updates==2 && FlightHud.Updates==1,"Visual pass runs once despite recursive callbacks");
        Require(HeadMountedDisplay.Updates==0 && HUDAppManager.Updates==0 && CombatHUD.Updates==0,"Render callback cannot replay native gameplay/input/app updates");
        // An explicit canvas rebuild can run before the camera finishes this same frame.
        cameraObject.transform.position += new Vector3(4, 1, 0);
        projection.Render(own,manager);
        expected=camera.WorldToScreenPoint(target.transform.position); expected.z=0;
        Require(Vector3.Distance(icon.transform.position,expected+jam)<.1f,"Late camera movement after an early canvas pass must reproject in the same frame");
        Require(Vector3.Distance(label.transform.position,icon.transform.position)<.1f,"Selected target label and marker share the final projection");
        int projected=marker.Updates; projection.Render(own,manager);
        Require(marker.Updates==projected,"An unchanged camera/canvas pass cannot duplicate projection work");
        marker.UpdatePosition(own.NetworkHQ,default,Vector3.forward); projection.Record(marker);
        Vector3 freshJam=new Vector3(-2,4,0); icon.transform.position+=freshJam; projection.RecordDistortion(marker,freshJam);
        projection.Render(own,manager);
        Require(Vector3.Distance(icon.transform.position,expected+freshJam)<.1f,"A new native projection replaces old jamming offsets instead of accumulating them");
        FlightHud.Projection=null; SceneSingleton<FlightHud>.i=null; SceneSingleton<CombatHUD>.i=null;
        Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(ui); Object.DestroyImmediate(own.gameObject); Object.DestroyImmediate(target.gameObject);
    }
    private static void CheckNativeOwnership()
    {
        var root=new GameObject("Native ownership").AddComponent<FlightHud>();
        var app=new GameObject("Native fuel",typeof(RectTransform),typeof(FuelGauge),typeof(CanvasGroup)); app.transform.SetParent(root.transform,false);
        CanvasGroup group=app.GetComponent<CanvasGroup>(); group.alpha=.65f;
        var climb=new GameObject("Native climb",typeof(RectTransform),typeof(Climbrate)); climb.transform.SetParent(root.transform,false);
        var adapter=new NativeHudPresentation(); adapter.Apply(root);
        Require(climb.GetComponent<CanvasGroup>() != null && climb.GetComponent<CanvasGroup>().alpha == 0,"Replacement V/S hides the duplicate native climb-rate text");
        Require(group.alpha==0 && app.GetComponent<FuelGauge>().enabled,"Hide presentation only, never stop native behavior");
        adapter.Restore(); Require(Mathf.Abs(group.alpha-.65f)<.001f,"Restore existing native opacity exactly");
        adapter.Apply(root); Require(group.alpha == 0 && app.GetComponents<CanvasGroup>().Length == 1,"Same-frame restore/reapply retains one live group");
        adapter.Dispose(); Require(Mathf.Abs(group.alpha-.65f)<.001f,"Teardown restores native alpha"); Object.DestroyImmediate(root.gameObject);
    }
    private static void CheckVisibility(HudSettings config)
    {
        var scene = new GameObject("Visibility fixture");
        var own = scene.AddComponent<Aircraft>(); own.cockpit = scene.AddComponent<Cockpit>();
        var camera = scene.AddComponent<CameraStateManager>(); camera.orbitState = camera.currentState = new object(); camera.followingUnit = own;
        var combat = scene.AddComponent<CombatHUD>(); combat.aircraft = own;
        SceneSingleton<CameraStateManager>.i = camera; SceneSingleton<CombatHUD>.i = combat;
        var controller = scene.AddComponent<ThirdPersonHudController>(); controller.Configure(config);
        Require(controller.Active, "Local orbit view enables independent HUD");
        Require(!controller.ModifyVanillaHud && !controller.NativeModificationsActive, "Vanilla modifications default off while independent HUD remains active");
        Require(!(bool)controller.GetType().GetProperty("CorrectProjection",Private).GetValue(controller),"No vanilla adjustment before custom camera has moved");
        controller.FlightCamera.AppliedFrame = Time.frameCount;
        Require((bool)controller.GetType().GetProperty("CorrectProjection",Private).GetValue(controller) && !controller.NativeModificationsActive,"Custom camera synchronizes markers without opting in to native instrument replacement");
        controller.FlightCamera.Reset();
        controller.ModifyVanillaHud = true;
        Require(controller.NativeModificationsActive, "Vanilla modifications require explicit opt in");
        controller.ModifyVanillaHud = false;
        controller.ResetLayout();
        Require(controller.Active && !controller.NativeModificationsActive, "Layout reset preserves opt out and independent panels");
        GameplayUI.GameIsPaused = true; Require(!controller.Active, "Pause gates every frame"); GameplayUI.GameIsPaused = false;
        DynamicMap.mapMaximized = true; Require(!controller.Active, "Map hides overlays"); DynamicMap.mapMaximized = false;
        PlayerSettings.cinematicMode = true; Require(!controller.Active, "Cinematic hides overlays"); PlayerSettings.cinematicMode = false;
        camera.followingUnit = null; Require(!controller.Active, "Spectating cannot retain ownship widgets"); camera.followingUnit = own;
        own.disabled = true; Require(!controller.Active, "Destroyed aircraft cannot retain live data"); own.disabled = false;
        camera.currentState = camera.cockpitState = new object(); Require(!controller.Active, "Cockpit retains its native instruments");
        SceneSingleton<CameraStateManager>.i = null; SceneSingleton<CombatHUD>.i = null; Object.DestroyImmediate(scene);
    }
    private static void CheckMissileOrder()
    {
        var own=new GameObject("Track ordering ownship").AddComponent<Aircraft>();
        var first=new GameObject("First contact").AddComponent<Missile>(); first.transform.position=new Vector3(1000,0,0);
        var second=new GameObject("Second contact").AddComponent<Missile>(); second.transform.position=new Vector3(1100,0,0);
        own.warnings.knownMissiles.Add(first); own.warnings.knownMissiles.Add(second);
        var reader=new MissileTelemetry(); reader.Read(own,true);
        var firstEntry=reader.Shown[0];
        first.transform.position=new Vector3(1200,0,0); second.transform.position=new Vector3(900,0,0);
        reader.GetType().GetField("lastShotRead",Private).SetValue(reader,-99f); reader.Read(own,true);
        Require(ReferenceEquals(reader.Shown[0],firstEntry),"Existing missile rows retain order when their ranges cross");
        reader.Read(null,false); Object.DestroyImmediate(own.gameObject); Object.DestroyImmediate(first.gameObject); Object.DestroyImmediate(second.gameObject);
    }
    private static void CheckTelemetry()
    {
        var own=new GameObject("Ownship data").AddComponent<Aircraft>(); own.cockpit=own.gameObject.AddComponent<Cockpit>(); own.cockpit.rb=own.gameObject.AddComponent<Rigidbody>();
        var intact=new GameObject("Intact").AddComponent<UnitPart>(); var damaged=new GameObject("Damaged").AddComponent<UnitPart>(); damaged.hitPoints=50;
        var lost=new GameObject("Lost").AddComponent<UnitPart>(); lost.detached=true;
        own.partLookup.Add(intact); own.partLookup.Add(damaged); own.partLookup.Add(lost);
        var reader=new FlightTelemetry(); reader.Read(own);
        Require(reader.Systems.Valid && reader.Systems.Damaged==1 && reader.Systems.Detached==1,"Damage counts come from ownship parts, no artwork or health averaging");
        Require(reader.Flight.G==1,"Stationary upright aircraft has native one-G load");
        reader.Reset(); Require(!reader.Flight.Valid && !reader.Systems.Valid,"Reset clears ownship snapshot");
        Object.DestroyImmediate(own.gameObject); Object.DestroyImmediate(intact.gameObject); Object.DestroyImmediate(damaged.gameObject); Object.DestroyImmediate(lost.gameObject);
    }
    private static void Render(Canvas canvas, RectTransform focus, string path)
    {
        var originalMode = canvas.renderMode;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.transform.localScale = Vector3.one;
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
        Vector3[] corners = new Vector3[4]; focus.GetWorldCorners(corners);
        int width = Mathf.CeilToInt(focus.rect.width + 32), height = Mathf.CeilToInt(focus.rect.height + 32);
        var camera = new GameObject("Render camera").AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = height / 2f;
        camera.transform.position = (corners[0] + corners[2]) * .5f + new Vector3(0, 0, -10);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.65f, .73f, .8f);
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        camera.Render(); RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG()); RenderTexture.active = null;
        Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        canvas.renderMode = originalMode;
    }

    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Private).GetValue(owner);
    private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
}
#endif
