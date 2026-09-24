#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Real production OPS builders, deterministic display data; no game or network simulation.</summary>
public static class SupportPanelUnityCheck
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly Assembly Mod = typeof(AvScreen).Assembly;
    private static readonly string[] Pages = { "Station", "SpaceOps", "Status", "CyberOps", "SpecStatus", "SpecActions" };
    private static int assertions;
    private static object overlaySupport;

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
            MethodInfo setPaths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            ParameterInfo[] parameters = setPaths.GetParameters();
            var arguments = new object[parameters.Length];
            arguments[0] = Path.GetFullPath("SupportPreview.exe");
            for (int i = 1; i < arguments.Length; i++) arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            setPaths.Invoke(null, arguments);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvFont.Font = TMP_FontAsset.CreateFontAsset(new Font("C:/Windows/Fonts/consola.ttf"));
            foreach (float height in new[] { 420f, 596f, 896f })
                foreach (string page in Pages) Render(page, height);
            CheckActionForms();
            string window = OpsWindowUnityCheck.Run(overlaySupport, _ => DetachmentFixture(out _), () =>
            {
                object network = CyberFixture(out int held, out double clock);
                return new object[] { network, clock, held };
            }, () =>
            {
                object station = PlatformFixture(out double stationNow);
                return new object[] { station, stationNow, FreeCell(station) };
            });
            assertions += OpsWindowUnityCheck.Assertions;
            File.WriteAllText("result.txt", "PASS: " + (Pages.Length * 3) + " real OPS MFD page layouts painted by their production refresh paths; " + window + "; " + assertions +
                " geometry/readability/facts assertions. Production DLL builders with offline model/fixture data; no live game, state replication or input integration claimed.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static void Render(string page, float height)
    {
        var canvasObject = new GameObject("OpsPreview", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvasObject.transform;
        root.sizeDelta = new Vector2(480f, height);
        AvKit.Panel(root, new Rect(0f, 0f, 480f, height), AvTheme.SurfaceInert);
        AvScreen shell = AvScreen.Build(root, "OPS", new[] { "SPACE", "CYBER", "SPEC OPS" },
            new[] { new[] { "ALLOCATION", "" }, new[] { "ORBIT", "MOD" }, new[] { "CYBER", "NET" }, new[] { "SPEC OPS", "RDY" } },
            3, 480f, height, _ => { });
        string domainName = page == "Station" || page == "SpaceOps" ? "SPACE"
            : page == "Status" || page == "CyberOps" ? "CYBER" : "SPEC OPS";
        string modeName = page == "SpaceOps" || page == "CyberOps" || page == "SpecActions" ? "ACTIONS" : "STATUS";
        shell.DataBar.State.text = domainName + " / " + modeName + " · OFFLINE QA";
        shell.DataBar.SetChip(0, "OFFLINE QA", "info");
        shell.DataBar.SetChip(1, "FIXTURE", "inert");
        shell.DataBar.SetChip(2, "NO ORDERS", "inert");
        shell.Metrics[0].Set("8,089", "AVAILABLE", 1f, AvTheme.RailReady);
        shell.Metrics[1].Set("8/15", "STATION", .53f, AvTheme.RailInfo);
        shell.Metrics[2].Set("6/8", "INFOCON 3", .75f, AvTheme.RailCaution);
        shell.Metrics[3].Set("1/3", "1 POST", .25f, AvTheme.RailReady);
        shell.WriteStatus(null, null, "Offline layout check · no live game state or orders.");

        var panelObject = new GameObject("SupportPanel");
        object panel = panelObject.AddComponent(Mod.GetType("BoscaliSummer.Features.Support.Presentation.SupportPanel", true));
        ((Behaviour)panel).enabled = false;
        var managerObject = new GameObject("SupportManager");
        object manager = managerObject.AddComponent(Mod.GetType("BoscaliSummer.Features.Support.Runtime.SupportManager", true));
        ((Behaviour)manager).enabled = false;
        Type settingsType = Mod.GetType("BoscaliSummer.Features.Support.Configuration.SupportSettings", true);
        object settings = Activator.CreateInstance(settingsType, new ConfigFile(Path.GetFullPath("fixture.cfg"), false));
        Type catalogType = Mod.GetType("BoscaliSummer.Features.Support.Runtime.SupportCatalog", true);
        object catalog = Activator.CreateInstance(catalogType, Hidden, null, new[] { settings, null }, null);
        Set(manager, "settings", settings);
        Set(manager, "catalog", catalog);
        Set(panel, "support", manager);
        Set(panel, "shell", shell);
        Call(panel, "DecorateDomainTabs");
        overlaySupport = manager;
        int index = Array.IndexOf(Pages, page);
        int tab = index < 2 ? 0 : index < 4 ? 1 : 2;
        var pageRoot = (RectTransform)shell.CreatePage(tab, page).transform;
        // The page builds its own title row and STATUS / ACTIONS toggle inside the whole body (M1).
        Call(panel, tab == 2 ? "Build" + page : "Build" + page + "Page", pageRoot, shell.Body);
        shell.SetPage(tab);
        object clock = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.OrbitClock", true)
            .GetProperty("Default", BindingFlags.Static | BindingFlags.Public).GetValue(null);
        Paint(panel, page, clock);
        Call(panel, "RefreshActionRows", tab, false);
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        string prefix = "ops-" + page.ToLowerInvariant() + "-" + height;
        // Capture before asserting, so a failing page still leaves its picture.
        Capture(canvasObject, height, prefix + ".png");

        // Readability on every page: nothing ellipsized, nothing under the 10 px floor.
        foreach (TMP_Text text in pageRoot.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!text.enabled || string.IsNullOrEmpty(text.text)) continue;
            Check(!text.isTextTruncated, page + " " + height + ": text must be complete: " + text.text);
            Check(text.fontSize >= 9.99f, page + " " + height + ": text under the 10 px floor: " + text.text);
        }
        if (page == "SpecStatus" && height == 420f)
            foreach (object row in (IEnumerable)Get(panel, "teamRows"))
            {
                var name = (TMP_Text)Get(row, "Name");
                var line = (TMP_Text)Get(row, "Line");
                var nameCorners = new Vector3[4];
                var lineCorners = new Vector3[4];
                name.rectTransform.GetWorldCorners(nameCorners);
                line.rectTransform.GetWorldCorners(lineCorners);
                Check(nameCorners[2].x + 1f <= lineCorners[0].x,
                    "Compact team callsign and status must have separate columns.");
            }
        CheckFacts(panel, page);
        if (height >= 896f)
        {
            // M2: at 896 the page fills the body; no empty band over 12 % of it.
            float band = LargestEmptyBand(pageRoot, shell.Body, height);
            Check(band <= shell.Body.height * 0.12f, page + " leaves an empty band of " + band + " px at 896.");
            if (page == "SpaceOps" || page == "CyberOps" || page == "SpecActions") actionSignatures[page] = Signature(pageRoot);
        }

        bool scrolled = false;
        foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(true))
        {
            if (!scroll.gameObject.activeInHierarchy) continue;
            Check(scroll.content.rect.height >= scroll.viewport.rect.height, "Scroll content must cover viewport.");
            scroll.verticalNormalizedPosition = 0f;
            scrolled = true;
        }
        if (scrolled) Capture(canvasObject, height, prefix + "-bottom.png");
        if (page == "Station")
        {
            // No station: one card, one call to action (M6).
            Call(panel, "RefreshStationPage", null, 0.0, clock);
            Canvas.ForceUpdateCanvases();
            Check(!((RectTransform)Get(panel, "stationHero")).gameObject.activeSelf, "The pass dial must hide with no station.");
            Check(((RectTransform)Get(panel, "stationEmptyCard")).gameObject.activeSelf, "The empty card must show with no station.");
            if (height >= 896f)
            {
                float band = LargestEmptyBand(pageRoot, shell.Body, height);
                Check(band <= shell.Body.height * 0.12f, "SPACE STATUS with no station leaves an empty band of " + band + " px.");
            }
            Capture(canvasObject, height, prefix + "-empty.png");
        }
        // Do not call gameplay component teardown against an absent game session.
        Object.DestroyImmediate(canvasObject);
        panelObject.SetActive(false);
        managerObject.SetActive(false);
    }

    private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>> actionSignatures =
        new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();

    /// <summary>The real page painters, fed production-model fixtures.</summary>
    private static void Paint(object panel, string page, object clock)
    {
        switch (page)
        {
            case "Station":
            {
                object station = PlatformFixture(out double now);
                // The voice loop as a session fills it (the page never logs by itself offline).
                Call(panel, "Log", "LIFTOFF CONFIRMED · BASTION CORE CLIMBING");
                Call(panel, "Log", "ORBIT INSERTION CONFIRMED · LOW EARTH ORBIT");
                Call(panel, "Log", "HARD DOCK · IMG PORT");
                Call(panel, "Log", "LINK · BASTION ON STATION · CENTRE");
                Call(panel, "RefreshStationPage", station, now, clock);
                break;
            }
            case "SpaceOps":
            {
                object station = PlatformFixture(out double now);
                Call(panel, "RefreshSpaceOpsPage", false, station, now, clock);
                break;
            }
            case "Status":
            {
                object network = CyberFixture(out int held, out double now);
                Call(panel, "SelectSite", held);
                Check((int)Get(panel, "selectedSite") == held, "A node click must select that slot.");
                Call(panel, "CyberLog", "WATCH FLOOR ONLINE · AEGIS NET STANDING BY");
                Call(panel, "CyberLog", "C2 UP · CENTRAL AIRBASE");
                Call(panel, "CyberLog", "BREACH OPEN · CITY 11");
                Call(panel, "CyberLog", "STAGE 1 · CITY 11 · FOOTHOLD");
                Call(panel, "CyberLog", "STAGE 2 · CITY 11 · RADIUS 8 KM");
                Call(panel, "CyberLog", "BREACH OPEN · AIRFIELD 12 · TRACE 18%");
                Call(panel, "RefreshStatusPage", network, now);
                break;
            }
            case "CyberOps":
            {
                object network = CyberFixture(out _, out double now);
                Call(panel, "RefreshCyberOpsPage", false, network, now);
                break;
            }
            case "SpecStatus":
            {
                object detachment = DetachmentFixture(out double now);
                Call(panel, "SpecLog", "DETACHMENT ON THE NET · ALPHA AND BRAVO STANDING BY");
                Call(panel, "SpecLog", "CHARLIE RAISED · RECRUIT");
                Call(panel, "SpecLog", "ALPHA · INSERTED · RECON KERSEY");
                Call(panel, "SpecLog", "ALPHA · OBSERVATION POST OVER KERSEY");
                Call(panel, "SpecLog", "BRAVO · EN ROUTE · AIR DEFENCE 26/-4");
                Call(panel, "SpecLog", "CHARLIE · ON TASK · NORTH RIDGE AIRFIELD");
                Call(panel, "RefreshSpecStatus", detachment, now);
                int rows = 0;
                foreach (object row in (IEnumerable)Get(panel, "teamRows")) if (row != null) rows++;
                Check(rows == 4, "SPEC OPS STATUS must show all four team lanes.");
                break;
            }
            default:
                Call(panel, "RefreshSpecActions", DetachmentFixture(out _));
                break;
        }
    }

    /// <summary>M5: every ability surface prints the cost and readiness words <c>AbilityStatus</c> decided.</summary>
    private static void CheckFacts(object panel, string page)
    {
        Type status = Mod.GetType("BoscaliSummer.Features.Support.Presentation.Viz.AbilityStatus", true);
        MethodInfo facts = status.GetMethod("For", BindingFlags.Static | BindingFlags.Public);
        object support = Get(panel, "support");
        if (page == "SpaceOps")
            foreach (object row in (IEnumerable)Get(panel, "spaceRows"))
            {
                object action = Get(row, "Action");
                string readiness = ((TMP_Text)Get(row, "Readiness")).text;
                string cost = ((TMP_Text)Get(row, "Cost")).text;
                Check(!string.IsNullOrEmpty(readiness) && !string.IsNullOrEmpty(cost), "M3: a SPACE row must show cost and readiness.");
                if (action == null) continue;
                object f = facts.Invoke(null, new[] { support, action, (object)false });
                Check(readiness == (string)Field(f, "Readiness"), "SPACE readiness must be AbilityStatus's: " + readiness);
                string price = (string)Field(f, "CostText");
                Check(price == "\u2014" || cost.Contains(price), "SPACE cost must carry AbilityStatus's price: " + cost);
                object sw = Get(row, "Switch");
                bool enabled = (bool)Field(Get(sw, "Control"), "Enabled");
                Check(enabled == (bool)Field(f, "Enabled"), "The guarded switch must follow AbilityStatus.");
            }
        if (page == "CyberOps")
            foreach (object row in (IEnumerable)Get(panel, "abilityRows"))
            {
                object f = facts.Invoke(null, new[] { support, Get(row, "Definition"), (object)false });
                Check(((TMP_Text)Get(row, "Coverage")).text == (string)Field(f, "Readiness"), "CYBER readiness must be AbilityStatus's.");
                Check(((TMP_Text)Get(row, "Cost")).text == (string)Field(f, "CostText"), "CYBER cost must be AbilityStatus's.");
            }
        if (page == "SpecActions")
            foreach (object row in (IEnumerable)Get(panel, "actionRows"))
            {
                object f = facts.Invoke(null, new[] { support, Get(row, "Definition"), (object)false });
                Check(((TMP_Text)Get(Get(row, "View"), "Value")).text == (string)Field(f, "CostText"), "SPEC OPS cost must be AbilityStatus's.");
            }
    }

    private static object Field(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, Hidden);
        return field != null ? field.GetValue(target) : Property(target, name);
    }

    /// <summary>The tallest vertical run of the body that no visible graphic of the page covers.</summary>
    private static float LargestEmptyBand(RectTransform pageRoot, Rect body, float height)
    {
        var spans = new System.Collections.Generic.List<Vector2>();
        var corners = new Vector3[4];
        foreach (Graphic graphic in pageRoot.GetComponentsInChildren<Graphic>(false))
        {
            if (!graphic.enabled || graphic.color.a < 0.02f) continue;
            var text = graphic as TMP_Text;
            if (text != null && string.IsNullOrEmpty(text.text)) continue;
            graphic.rectTransform.GetWorldCorners(corners);
            float top = height * 0.5f - Mathf.Max(corners[1].y, corners[2].y);
            float bottom = height * 0.5f - Mathf.Min(corners[0].y, corners[3].y);
            spans.Add(new Vector2(top, bottom));
        }
        spans.Sort((a, b) => a.x.CompareTo(b.x));
        float cursor = -body.y, end = -body.y + body.height, largest = 0f;
        foreach (Vector2 span in spans)
        {
            if (span.y <= cursor) continue;
            if (span.x > cursor) largest = Mathf.Max(largest, Mathf.Min(span.x, end) - cursor);
            cursor = Mathf.Max(cursor, span.y);
            if (cursor >= end) break;
        }
        return Mathf.Max(largest, end - cursor);
    }

    /// <summary>Quantised image rects: two pages that share a form share most of these.</summary>
    private static System.Collections.Generic.HashSet<string> Signature(RectTransform pageRoot)
    {
        var set = new System.Collections.Generic.HashSet<string>();
        var corners = new Vector3[4];
        foreach (Image image in pageRoot.GetComponentsInChildren<Image>(false))
        {
            image.rectTransform.GetWorldCorners(corners);
            set.Add(Mathf.RoundToInt(corners[1].x / 8f) + ":" + Mathf.RoundToInt(corners[1].y / 8f) + ":" +
                    Mathf.RoundToInt((corners[2].x - corners[1].x) / 8f) + ":" + Mathf.RoundToInt((corners[1].y - corners[0].y) / 8f));
        }
        return set;
    }

    private static void CheckActionForms()
    {
        string[] pages = { "SpaceOps", "CyberOps", "SpecActions" };
        for (int a = 0; a < pages.Length; a++)
            for (int b = a + 1; b < pages.Length; b++)
            {
                var shared = new System.Collections.Generic.HashSet<string>(actionSignatures[pages[a]]);
                shared.IntersectWith(actionSignatures[pages[b]]);
                var union = new System.Collections.Generic.HashSet<string>(actionSignatures[pages[a]]);
                union.UnionWith(actionSignatures[pages[b]]);
                float jaccard = union.Count == 0 ? 1f : (float)shared.Count / union.Count;
                Check(jaccard < 0.5f, pages[a] + " and " + pages[b] + " share a layout (" + jaccard + ").");
            }
    }

    /// <summary>A real station fitted through the production model: the RECON loadout launched
    /// step by step and ticked to hard dock, so the truss and the console show a station the
    /// player could actually have built.</summary>
    private static object PlatformFixture(out double now)
    {
        Type platformType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.OrbitalPlatform", true);
        Type missionsType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.PlatformMissions", true);
        Type missionType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.PlatformMission", true);
        Type modulesType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.PlatformModules", true);
        Type clockType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.OrbitClock", true);
        object clock = clockType.GetProperty("Default", BindingFlags.Static | BindingFlags.Public).GetValue(null);
        object recon = Enum.Parse(missionType, "Recon");
        object platform = Activator.CreateInstance(platformType);
        MethodInfo next = missionsType.GetMethod("Next", BindingFlags.Static | BindingFlags.Public);
        MethodInfo price = modulesType.GetMethod("LaunchPrice", BindingFlags.Static | BindingFlags.Public);
        const double insertion = 45.0, dock = 20.0;

        now = 100.0;
        for (int launched = 0; launched < 8; launched++)
        {
            object step = next.Invoke(null, new[] { platform, recon, now });
            if ((bool)Property(step, "Complete")) break;
            object module = step.GetType().GetField("Module", Hidden).GetValue(step);
            var cell = (int)step.GetType().GetField("Cell", Hidden).GetValue(step);
            object failure = Invoke(platform, "TryLaunch", module, cell, (byte)0, 42,
                now, (float)price.Invoke(null, new[] { module }), insertion, dock);
            Check(failure.ToString() == "None", "The station fixture must launch " + module + ": " + failure);
            now += dock + insertion;
            Call(platform, "Tick", now, 0.01f, true, clock);
        }
        Check((bool)Property(platform, "Exists"), "The station fixture must reach orbit.");
        // Settle on a pass so the banner, tiles and console strip all read live figures.
        object state = Invoke(platform, "State", now, clock);
        if (!(bool)Property(state, "InPass"))
        {
            now += (double)state.GetType().GetField("TimeToPass", Hidden).GetValue(state) + 0.5;
            Call(platform, "Tick", now, 0.01f, true, clock);
        }
        return platform;
    }

    private static int OrbitalCoreCell()
    {
        Type platformType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.OrbitalPlatform", true);
        return (int)platformType.GetField("CoreCell", BindingFlags.Static | BindingFlags.Public).GetRawConstantValue();
    }

    /// <summary>The first cell the fixture station can still dock to, so the console's inspector and
    /// launch card show a real free cell rather than a refusal.</summary>
    private static int FreeCell(object platform)
    {
        Type platformType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Orbital.OrbitalPlatform", true);
        var cells = (int)platformType.GetField("CellCount", BindingFlags.Static | BindingFlags.Public).GetRawConstantValue();
        for (int cell = 0; cell < cells; cell++)
            if ((bool)Invoke(platform, "CanAttach", cell)) return cell;
        return OrbitalCoreCell();
    }

    /// <summary>A real CyberNetwork fixture — the home backbone, cities in and out of reach, one
    /// location breached to stage 2 and a live breach — driven through the production host tick
    /// and console painter, so the offline render shows the board the player actually sees.</summary>
    private static object CyberFixture(out int held, out double clock)
    {
        Type networkType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Cyber.CyberNetwork", true);
        Type nodeKindType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Cyber.NodeKind", true);
        Type locationKindType = Mod.GetType("BoscaliSummer.Features.Support.Domain.Cyber.LocationKind", true);
        object network = Activator.CreateInstance(networkType);
        clock = 60.0;
        Call(network, "PlaceStatic", 1, Enum.Parse(nodeKindType, "Command"), 0f, 0f, clock);
        Call(network, "PlaceStatic", 2, Enum.Parse(nodeKindType, "Base"), -14000f, 7000f, clock);
        Call(network, "PlaceStatic", 3, Enum.Parse(nodeKindType, "Base"), 11000f, -9000f, clock);
        object city = Enum.Parse(locationKindType, "City");
        object airfield = Enum.Parse(locationKindType, "Airfield");
        held = (int)Invoke(network, "ReportLocation", 11, city, 16000f, 8000f, clock);
        int reachable = (int)Invoke(network, "ReportLocation", 12, airfield, -12000f, -16000f, clock);
        Invoke(network, "ReportLocation", 13, city, 52000f, 38000f, clock);
        Check(held >= 0 && reachable >= 0, "The CYBER fixture must place its hackable locations.");

        // Host time: accrue resources, breach the near city twice (stage 2 gives it a radius and
        // intel), then open a quiet breach on the airfield so the strip and the board are live.
        for (int i = 0; i < 1200; i++) Call(network, "Tick", clock += 0.25, 0.25f, 1f);
        for (int pass = 0; pass < 2; pass++)
        {
            Invoke(network, "TryStartBreach", held, true, clock);
            for (int i = 0; i < 600 && (bool)Property(network, "BreachActive"); i++)
                Call(network, "Tick", clock += 0.25, 0.25f, 1f);
        }
        Check((int)Invoke(network, "Stage", held) >= 2, "The fixture breach must lift the city to stage 2.");
        Invoke(network, "TryStartBreach", reachable, true, clock);
        return network;
    }

    /// <summary>A real detachment through the production model: nine objectives across the
    /// theatre, CHARLIE raised, ALPHA's recon succeeded and holds an observation post over a
    /// scouted town, BRAVO en route to sabotage an air-defence site, CHARLIE on task seizing an
    /// airfield, DELTA still unformed.</summary>
    private static object DetachmentFixture(out double now)
    {
        Type detachmentType = Mod.GetType("BoscaliSummer.Features.Support.Domain.SpecOps.SpecOpsDetachment", true);
        Type kindType = Mod.GetType("BoscaliSummer.Features.Support.Domain.SpecOps.ObjectiveKind", true);
        Type missionType = Mod.GetType("BoscaliSummer.Features.Support.Domain.SpecOps.FieldMission", true);
        object detachment = Activator.CreateInstance(detachmentType);
        Call(detachment, "BeginObjectives");
        var fixture = new (string Kind, int Anchor, float X, float Z, int Threat, int Radars, bool Hostile, string Name)[]
        {
            ("Town", 101, 14000f, 6000f, 5, 0, true, "KERSEY"),
            ("AirDefence", 102, 26000f, -4000f, 4, 3, true, "AIR DEFENCE 26/-4"),
            ("Airfield", 103, 21000f, 15000f, 7, 1, true, "NORTH RIDGE AIRFIELD"),
            ("Outpost", 104, -9000f, 22000f, 0, 0, false, "LIGHTHOUSE POINT"),
            ("Town", 105, 34000f, 9000f, 11, 2, true, "PORT SAINT MARIE"),
            ("Airfield", 106, 41000f, -18000f, 9, 2, true, "SOUTHERN AIRBASE"),
            ("Town", 107, 5000f, -21000f, 1, 0, false, "VALE"),
            ("Outpost", 108, 30000f, 27000f, 3, 0, true, "HILL 402"),
            ("AirDefence", 109, 47000f, 4000f, 6, 4, true, "AIR DEFENCE 47/4")
        };
        foreach (var o in fixture)
            Call(detachment, "ReportObjective", Enum.Parse(kindType, o.Kind), o.Anchor, o.X, o.Z, o.Threat, o.Radars, o.Hostile, o.Name);
        Call(detachment, "EndObjectives");
        Func<double> lucky = () => 0.0;
        Check((bool)Invoke(detachment, "TryRaise", 2), "The fixture must raise CHARLIE.");
        Invoke(detachment, "TryLaunch", 0, Enum.Parse(missionType, "Recon"), 101, 12000f, 0.0);
        Call(detachment, "Tick", 50.0, lucky, null);
        Call(detachment, "Tick", 90.0, lucky, null);
        Invoke(detachment, "TryLaunch", 1, Enum.Parse(missionType, "Sabotage"), 102, 60000f, 90.0);
        Invoke(detachment, "TryLaunch", 2, Enum.Parse(missionType, "Seize"), 103, 8000f, 90.0);
        now = 140.0;
        Call(detachment, "Tick", now, lucky, null);
        Check(Property(detachment, "Formed").Equals(3), "The fixture must field three teams.");
        return detachment;
    }

    private static object Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Hidden).Invoke(target, args);
    private static object Property(object target, string property) =>
        target.GetType().GetProperty(property, Hidden).GetValue(target);


    private static object Get(object target, string field) => target.GetType().GetField(field, Hidden).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Hidden).Invoke(target, args);
    private static void Text(object target, string field, string value)
    {
        TMP_Text label = target.GetType().GetField(field, Hidden)?.GetValue(target) as TMP_Text;
        if (label != null) label.text = value;
    }
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }

    private static void Capture(GameObject canvas, float height, string file, float width = 480f)
    {
        Canvas.ForceUpdateCanvases();
        var cameraObject = new GameObject("Capture", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = height * .5f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.backgroundColor = AvTheme.SurfaceInert;
        camera.clearFlags = CameraClearFlags.SolidColor;
        var target = new RenderTexture((int)width * 2, (int)height * 2, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.GetFullPath(file), image.EncodeToPNG());
        RenderTexture.active = null;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(target);
    }
}
#endif
