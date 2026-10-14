#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using NOAvionics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Real production settings, dock, manager, physics placement and truck enrollment.
// Synthetic native state; no live aircraft, damage, multiplayer or native spawn claim.
public static class CommunityIssuesUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static Assembly Mod => typeof(AvConsole).Assembly;
    private static Type T(string name) => Mod.GetType("BoscaliSummer." + name, true);
    private static Type Ui(string name) => T("Modules.Command.Presentation.MapUi." + name);
    private static int checks;
    private static FieldInfo Field(object owner, string name)
    {
        for (Type t = owner as Type ?? owner.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo f = t.GetField(name, All | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        throw new MissingFieldException(name);
    }
    private static object Get(object owner, string name) => Field(owner, name).GetValue(owner is Type ? null : owner);
    private static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner is Type ? null : owner, value);
    private static object Call(object owner, string name, params object[] args) =>
        (owner as Type ?? owner.GetType()).GetMethod(name, All).Invoke(owner is Type ? null : owner, args);
    private static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    private static void Entry(object settings, string name, object value)
    {
        object entry = settings.GetType().GetProperty(name, All)?.GetValue(settings) ?? Get(settings, name);
        entry.GetType().GetProperty("Value").SetValue(entry, value);
    }
    private static object Value(object settings, string name)
    {
        object entry = settings.GetType().GetProperty(name, All)?.GetValue(settings) ?? Get(settings, name);
        return entry.GetType().GetProperty("Value").GetValue(entry);
    }

    public static void Run()
    {
        if (!UnityCheckHarness.EnsureTmpEssentials(Run)) return;
        try
        {
            UnityCheckHarness.SetExecutablePath("CommunityIssuesPreview.exe");
            Settings(); Dock(); StockRefresh(); Placement(); Enrollment(); FreeFlightFortifications(); EventPictures();
            File.WriteAllText("result.txt", "PASS: COMMUNITY ISSUES " + checks + " checks. Production settings/persistence, dock restore, stock layout refresh, Unity collider placement, EW enrollment, Free Flight fortification gates and live event-picture toggle; synthetic native state. No live input/damage, actual native spawning or multiplayer acceptance.");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            File.WriteAllText("result.txt", "FAIL: " + e); Debug.LogException(e); EditorApplication.Exit(1);
        }
    }

    private static void Settings()
    {
        string path = Path.GetFullPath("community.cfg");
        if (File.Exists(path)) File.Delete(path);
        var file = new ConfigFile(path, false);
        object settings = Activator.CreateInstance(T("Modules.Command.Configuration.CommandSettings"), All, null, new object[] { file }, null);
        Check(Value(settings, "TacMap").ToString() == "Classic" && !(bool)Value(settings, "HoloMap") && (float)Value(settings, "TerrainGhost") == 0f,
            "Fresh config must keep the native corner map and terrain ghost off.");
        Type holoType = T("Modules.Command.Runtime.HoloMap");
        var holo = new GameObject("Holo setting probe").AddComponent(holoType);
        Set(holo, "settings", settings);
        Entry(settings, "TacMap", Enum.Parse(Value(settings, "TacMap").GetType(), "Holo"));
        Entry(settings, "HoloMap", true);
        Check((bool)holoType.GetProperty("MiniOn", All).GetValue(holo), "Opted-in mini hologram is available.");
        Entry(settings, "HoloMap", false);
        Check(!(bool)holoType.GetProperty("MiniOn", All).GetValue(holo), "Master off suppresses a previously saved Holo corner-map mode immediately.");
        Entry(settings, "ExpandedMapUi", false); file.Save();
        var loaded = new ConfigFile(path, true);
        object reloaded = Activator.CreateInstance(settings.GetType(), All, null, new object[] { loaded }, null);
        Check(!(bool)Value(reloaded, "ExpandedMapUi") && !(bool)Value(reloaded, "HoloMap"), "Off settings persist without deleting config.");
        Object.DestroyImmediate(holo.gameObject);
    }

    private static MFDScreen Screen(Transform parent, string name)
    {
        var root = new GameObject(name, typeof(RectTransform)); root.transform.SetParent(parent, false);
        var rect = (RectTransform)root.transform; rect.sizeDelta = new Vector2(420f, 596f);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        var content = new GameObject("Content", typeof(RectTransform)); content.transform.SetParent(root.transform, false);
        ((RectTransform)content.transform).sizeDelta = rect.sizeDelta;
        var screen = root.AddComponent<CommunityPreviewScreen>(); screen.displayPanel = content;
        return screen;
    }

    private static void Dock()
    {
        var canvas = new GameObject("Map canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920f, 1080f);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var bay = new GameObject("Native bay", typeof(RectTransform)); bay.transform.SetParent(canvas.transform, false);
        var template = Screen(bay.transform, "Native template");
        MethodInfo resolve = Ui("MfdLayout").GetMethod("TryResolve", All);
        var args = new object[] { canvas, null, 420f }; Check((bool)resolve.Invoke(null, args), "Fixture map columns resolve.");
        Call(Ui("MfdPanelDock"), "Ensure", canvas, args[1]);
        Call(Ui("MfdPanelDock"), "Dock", template, true);
        // Same parent choice as MfdPanelInstaller.MakeRoot. One gets a structure sweep;
        // one is born just before settings-off, so Restore must rescue it itself.
        var enrolled = Screen(template.transform.parent, "Installed in dock");
        Call(Ui("MfdPanelDock"), "Dock", enrolled, false);
        var late = Screen(template.transform.parent, "Installed before sweep");
        Call(Ui("MfdPanelDock"), "Restore");
        Check(template.transform.parent == bay.transform && enrolled.transform.parent == bay.transform && late.transform.parent == bay.transform,
            "Every panel returns to the native bay instead of a slot scheduled for destruction.");
        Check(Get(Ui("MfdPanelDock"), "dock") == null, "Restored dock cannot block native map input.");
        Call(Ui("MfdPanelDock"), "Restore");
        Check(enrolled != null && late != null, "Repeated restore preserves new screens.");
        Object.DestroyImmediate(canvas.gameObject);
    }

    private static void StockRefresh()
    {
        Call(T("Core.Game.GameAccess"), "Initialise");
        var canvas = new GameObject("Stock canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920f, 1080f);
        var mfd = new GameObject("Native MFD").AddComponent<CommunityPreviewMfd>(); mfd.transform.SetParent(canvas.transform, false);
        Set(mfd, "leftButtons", new List<Button>()); Set(mfd, "rightButtons", new List<Button>());
        var map = new GameObject("Native map").AddComponent<ChromePreviewDynamicMap>();
        map.maximizedMapCanvas = canvas; SceneSingleton<DynamicMap>.i = map;
        typeof(DynamicMap).GetProperty("mapMaximized", All).SetValue(null, true);
        var manager = new GameObject("Map manager").AddComponent(Ui("MapUiManager"));
        Call(manager, "LateUpdate");
        var first = Get(Ui("MfdScreenFinish"), "root") as RectTransform;
        Check(first != null, "Stock map has a screen finish.");
        for (int i = 0; i < 12; i++) { Set(manager, "nextRefresh", 0f); Call(manager, "LateUpdate"); }
        Check(ReferenceEquals(first, Get(Ui("MfdScreenFinish"), "root")), "Saved stock layout does not recreate the screen finish at 10 Hz.");
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
        Set(manager, "nextRefresh", 0f); Call(manager, "LateUpdate");
        var resized = Get(Ui("MfdScreenFinish"), "root");
        Check(!ReferenceEquals(first, resized), "An actual canvas resize reconciles stock layout once.");
        Set(manager, "nextRefresh", 0f); Call(manager, "LateUpdate");
        Check(ReferenceEquals(resized, Get(Ui("MfdScreenFinish"), "root")), "The resized stock finish also remains stable.");
        typeof(DynamicMap).GetProperty("mapMaximized", All).SetValue(null, false); SceneSingleton<DynamicMap>.i = null;
        Object.DestroyImmediate(manager.gameObject); Object.DestroyImmediate(map.gameObject); Object.DestroyImmediate(canvas.gameObject);
    }

    private static void Placement()
    {
        var assets = ScriptableObject.CreateInstance<GameAssets>(); assets.terrainMaterial = new PhysicMaterial("Fixture terrain");
        object loader = Get(typeof(GameAssets), "loader"); Set(loader, "instance", assets);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.transform.position = new Vector3(0f, 99.5f, 0f);
        ground.transform.localScale = new Vector3(2000f, 1f, 2000f); ground.GetComponent<Collider>().sharedMaterial = assets.terrainMaterial;
        var definition = ScriptableObject.CreateInstance<BuildingDefinition>(); definition.unitPrefab = new GameObject("Emplacement prefab");
        definition.width = 4f; definition.length = 5f; definition.height = 3f; definition.value = 1f;
        bool Place(Vector3 point) { var args = new object[] { definition, point, Quaternion.identity, Vector3.zero }; return (bool)Call(T("Core.Game.GroundPlacement"), "TryPlace", args); }
        Physics.SyncTransforms(); Check(Place(new Vector3(0f, 100f, 0f)), "Open dry footprint is legal.");
        var helicopter = GameObject.CreatePrimitive(PrimitiveType.Cube); helicopter.name = "Aircraft footprint";
        helicopter.transform.position = new Vector3(0f, 104f, 0f); helicopter.transform.localScale = new Vector3(10f, 6f, 15f);
        Physics.SyncTransforms(); Check(!Place(new Vector3(0f, 100f, 0f)), "An aircraft over the deployment point blocks an emplacement.");
        Type builder = T("Garrisons.InfantryEncampmentBuilder");
        ((IDictionary)Get(builder, "CachedDefs"))["Fixture"] = definition; Set(builder, "catalogInitialized", true);
        object site = Activator.CreateInstance(builder.GetNestedType("EncampmentSite", All));
        var ownerGo = new GameObject("Insertion owner"); ownerGo.SetActive(false); Set(site, "Owner", ownerGo.AddComponent<CommunityPreviewFaction>());
        var spawnerGo = new GameObject("Insertion spawner"); spawnerGo.SetActive(false); var spawner = spawnerGo.AddComponent<CommunityPreviewSpawner>();
        Set(T("Core.Game.GameAccess"), "serverFrame", Time.frameCount); Set(T("Core.Game.GameAccess"), "serverActive", true);
        Check(Call(builder, "SpawnEmplacement", site, spawner, "Fixture", "Fixture", new Vector3(0f, 100f, 0f), Quaternion.identity, "MG") == null,
            "The actual infantry spawn path refuses the occupied footprint before calling native SpawnBuilding.");
        Check(Place(new Vector3(32f, 100f, 0f)), "The camp's next ring candidate can be placed outside the aircraft.");
        Call(builder, "ResetForScene"); Object.DestroyImmediate(spawnerGo); Object.DestroyImmediate(ownerGo);
        Object.DestroyImmediate(helicopter); Object.DestroyImmediate(ground); Object.DestroyImmediate(definition.unitPrefab); Object.DestroyImmediate(definition);
        Set(loader, "instance", null); Object.DestroyImmediate(assets);
    }

    private static void EventPictures()
    {
        AvBundle.Load(Debug.Log);
        string path = Path.GetFullPath("event-pictures.cfg"); if (File.Exists(path)) File.Delete(path);
        var file = new ConfigFile(path, false);
        object settings = Activator.CreateInstance(T("Modules.Events.Configuration.EventsSettings"), All, null, new object[] { file }, null);
        Check((bool)Value(settings, "PicturesEnabled"), "Event pictures preserve their default-on behavior.");
        Type cache = T("Modules.Events.Presentation.EventArtCache"); Call(cache, "Configure", settings);
        var canvas = new GameObject("Event picture fixture", typeof(RectTransform), typeof(Canvas));
        Type plateType = T("Modules.Events.Presentation.EventsMfdPanel").GetNestedType("EventPlateArt", All);
        object MakePlate() => Activator.CreateInstance(plateType, All, null, new object[] { (RectTransform)canvas.transform, 28f }, null);
        var alert = new GameObject("Live alert toggle").AddComponent(T("Modules.Events.Presentation.SuperEventAlert"));
        object expanded = MakePlate(), compact = MakePlate();
        var expandedRect = (RectTransform)plateType.GetProperty("Root", All).GetValue(expanded);
        var compactRect = (RectTransform)plateType.GetProperty("Root", All).GetValue(compact);
        expandedRect.sizeDelta = new Vector2(296f, 174f); compactRect.sizeDelta = new Vector2(40f, 22f);
        Set(alert, "plate", expanded); Set(alert, "compactPlate", compact);
        Set(alert, "settings", settings);
        object view = Activator.CreateInstance(T("Core.Contracts.ActiveEventView"), All, null, new object[] {
            "industrial_surge", "Industrial surge", "Fixture", "ECONOMIC", "SUPEREVENT", "ALL THEATER", true,
            "industrial_surge", "-35% SUPPORT COST", null, 0f, 60f, true, "" }, null);
        Set(alert, "activeView", view); Set(alert, "group", canvas.AddComponent<CanvasGroup>());
        Set(alert, "expandedPanel", expandedRect); Set(alert, "compactPanel", compactRect);
        Set(alert, "expandedGroup", expandedRect.gameObject.AddComponent<CanvasGroup>());
        Set(alert, "compactGroup", compactRect.gameObject.AddComponent<CanvasGroup>());
        Set(alert, "clock", AvText.Make(expandedRect, "Clock", AvTextRole.Micro, ""));
        Set(alert, "compactClock", AvText.Make(compactRect, "Compact clock", AvTextRole.Micro, ""));
        Set(alert, "closeAt", Time.unscaledTime + 24f); Set(alert, "collapseAt", Time.unscaledTime + 12f);
        Set(alert, "duration", 24f); Set(alert, "targetAlpha", 1f); Set(alert, "shownSerial", 7);
        Call(alert, "BindPictures");
        Check(((RawImage)Get(expanded, "art")).texture != null, "Built DLL loads its embedded event poster.");
        Entry(settings, "PicturesEnabled", false); Call(alert, "Tick");
        foreach (object plate in new[] { expanded, compact })
        {
            Check(!((RawImage)Get(plate, "art")).enabled && ((TMPro.TMP_Text)Get(plate, "mark")).gameObject.activeSelf &&
                (AvIcon)Get(plate, "glyphIcon") == AvIcon.AlertTriangle, "An already-open alert replaces both plates with the generic event symbol.");
            var mark = (TMPro.TMP_Text)Get(plate, "mark");
            Check(mark.font.HasCharacter(AvIconTable.Char(AvIcon.AlertTriangle)), "The generic symbol exists in the embedded production font.");
        }
        Check(Call(cache, "Get", "industrial_surge", "default") == null && Call(cache, "Thumb", "industrial_surge", "default") == null,
            "Picture and thumbnail requests return no artwork while disabled.");
        file.Save();
        var reloaded = Activator.CreateInstance(settings.GetType(), All, null, new object[] { new ConfigFile(path, true) }, null);
        Check(!(bool)Value(reloaded, "PicturesEnabled"), "The event-picture switch persists across config reloads.");
        Entry(settings, "PicturesEnabled", true); Call(alert, "Tick");
        Check(((RawImage)Get(expanded, "art")).enabled && ((RawImage)Get(compact, "art")).enabled,
            "The same open alert restores both pictures when enabled again.");
        Check(ReferenceEquals(Get(alert, "activeView"), view) && (int)Get(alert, "shownSerial") == 7,
            "Changing event pictures does not replay or replace the event.");
        Object.DestroyImmediate(alert.gameObject); Object.DestroyImmediate(canvas);
    }

    private static void FreeFlightFortifications()
    {
        PropertyInfo current = typeof(MissionManager).GetProperty("CurrentMission", All);
        object previous = current.GetValue(null);
        var shell = new GameObject("Civilian shell");
        var zone = new GameObject("Garrison gate").AddComponent(T("Garrisons.ZoneGarrisonManager"));
        var trench = new GameObject("Trench gate").AddComponent(T("Modules.Trenches.Runtime.TrenchManager"));
        try
        {
            var mission = new NuclearOption.SavedMission.Mission { Name = "Free Flight" };
            current.SetValue(null, mission);
            bool Allowed() => (bool)T("Core.Game.BoscaliMission").GetProperty("FortificationsAllowed", All).GetValue(null);
            Check(!Allowed(), "Native Free Flight mission closes the shared fortification gate.");
            Set(T("Core.Game.GameAccess"), "serverFrame", Time.frameCount); Set(T("Core.Game.GameAccess"), "serverActive", true);
            ((IList)Get(zone, "shellCatalogue")).Add(shell);
            Check(!(bool)Call(zone, "TryOccupyBuilding", shell, null), "Air-assault building occupation is blocked in Free Flight.");
            Check(!(bool)Call(zone, "Occupy", shell, null, null, 1, "Fixture:"), "Shared occupation path is blocked too.");
            Call(zone, "Update");
            Check(!(bool)Get(zone, "initialScanComplete") && ((IList)Get(zone, "pending")).Count == 0,
                "Free Flight does not begin the automatic building census or queue garrisons.");
            Check(!(bool)Call(zone, "IsMissionRooftopAvailable", shell.GetInstanceID()), "Free Flight roofs are not offered as occupation targets.");
            Check(!(bool)zone.GetType().GetProperty("SiegeActive", All).GetValue(null), "Free Flight cannot enable urban siege capture modifiers.");
            object settings = Activator.CreateInstance(T("Modules.Trenches.Configuration.TrenchesSettings"), All, null,
                new object[] { new ConfigFile(Path.GetFullPath("freeflight-trenches.cfg"), false) }, null);
            Entry(settings, "Enabled", true); Call(trench, "Configure", settings, null);
            Set(trench, "planJob", Activator.CreateInstance(Field(trench, "planJob").FieldType, true));
            Call(trench, "Update");
            Check(Get(trench, "planJob") == null && ((IList)Get(trench, "lines")).Count == 0,
                "Free Flight cancels pending trench planning before placement/commit work.");
            Check((bool)Value(settings, "Enabled"), "Mission gating does not overwrite the saved trench switch.");
            Set(T("Core.Game.GameAccess"), "serverActive", false);
            Type stage = T("Modules.Trenches.Domain.TrenchStage");
            Call(trench, "ReceiveGeometry", 1, 1, Enum.ToObject(stage, 0),
                new[] { Vector3.zero, Vector3.right * 100f }, null, null, null, null, null);
            Check(((IList)Get(trench, "clientLineList")).Count == 0, "Free Flight clients reject trench geometry instead of constructing local earthworks.");
            mission.Name = "Escalation";
            Check(Allowed() && (bool)Call(zone, "IsMissionRooftopAvailable", shell.GetInstanceID()),
                "The same managers regain fortification and roof eligibility in another mission.");
            current.SetValue(null, NuclearOption.SavedMission.Mission.NullMission);
            Check(!Allowed(), "Native NullMission does not fortify while no mission runs.");
        }
        finally
        {
            current.SetValue(null, previous);
            Object.DestroyImmediate(zone.gameObject); Object.DestroyImmediate(trench.gameObject); Object.DestroyImmediate(shell);
        }
    }

    private static void Enrollment()
    {
        var ownerGo = new GameObject("Our faction"); ownerGo.SetActive(false); var owner = ownerGo.AddComponent<CommunityPreviewFaction>();
        Type serviceType = T("Modules.Support.Runtime.CyberService");
        var service = new GameObject("Cyber service").AddComponent(serviceType);
        Type kind = T("Modules.Support.Domain.Cyber.AnchorKind"); object truckKind = Enum.Parse(kind, "EwTruck");
        object anchors = Activator.CreateInstance(T("Modules.Support.Domain.Cyber.CyberAnchorSet"), All, null, new object[] { 0, 0 }, null);
        object faction = Activator.CreateInstance(serviceType.GetNestedType("FactionCyber", All), true);
        Set(faction, "Owner", owner); Set(faction, "Anchors", anchors);
        var truckGo = new GameObject("New native radar truck"); truckGo.SetActive(false);
        var truck = truckGo.AddComponent<CommunityPreviewTruck>(); truck.HQ.Value = owner;
        truck.definition = ScriptableObject.CreateInstance<VehicleDefinition>(); truck.definition.jsonKey = "HLT-R";
        truck.radar = truckGo.AddComponent<CommunityPreviewRadar>();
        var partGo = new GameObject("Truck body"); partGo.SetActive(false); var part = partGo.AddComponent<CommunityPreviewPart>(); part.hitPoints = 300f; truck.Parts.Add(part);
        UnitRegistry.allUnits.Clear(); UnitRegistry.allUnits.Add(truck);
        Call(service, "EnrollTrucks", faction, 100f);
        Check((int)Call(anchors, "LiveCount", truckKind) == 1, "A new friendly native radar truck joins the EW network.");
        Call(service, "EnrollTrucks", faction, 110f);
        Check((int)Call(anchors, "Count", truckKind) == 1, "Repeated census cannot enroll the same truck twice.");
        truck.disabled = true;
        var replacementGo = new GameObject("Replacement radar"); replacementGo.SetActive(false);
        var replacement = replacementGo.AddComponent<CommunityPreviewTruck>(); replacement.HQ.Value = owner;
        replacement.definition = truck.definition; replacement.radar = replacementGo.AddComponent<CommunityPreviewRadar>(); replacement.Parts.Add(part);
        UnitRegistry.allUnits.Add(replacement);
        Call(service, "EnrollTrucks", faction, 120f);
        Check((int)Call(anchors, "Count", truckKind) == 1 && ReferenceEquals(Get(((IList)Get(faction, "Slots"))[0], "Unit"), replacement),
            "A replacement native truck fills a dead slot without growing the network or deleting native wrecks.");
        replacement.HQ.Value = null;
        var emptyFaction = Activator.CreateInstance(faction.GetType(), true); Set(emptyFaction, "Owner", owner);
        var emptyAnchors = Activator.CreateInstance(anchors.GetType(), All, null, new object[] { 0, 0 }, null); Set(emptyFaction, "Anchors", emptyAnchors);
        Call(service, "EnrollTrucks", emptyFaction, 130f);
        Check((int)Call(emptyAnchors, "Count", truckKind) == 0, "Destroyed and foreign radar trucks cannot contribute faction EW reach.");
        Object.DestroyImmediate(replacementGo);
        UnitRegistry.allUnits.Clear(); Object.DestroyImmediate(service.gameObject); Object.DestroyImmediate(truckGo); Object.DestroyImmediate(partGo); Object.DestroyImmediate(ownerGo);
    }
}
#endif
