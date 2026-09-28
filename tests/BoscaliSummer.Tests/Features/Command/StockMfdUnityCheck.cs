#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Real production presenters and installed game type metadata, synthetic display data.
// This verifies kit v2 console layout and reachability, not game adapters, authority or multiplayer.
// Each presenter owns one AvConsole (the `Console` property of the Presenter base); its paged body is a
// single ScrollRect, so a page whose flow is taller than the body must be scrollable to its last row.
public static class StockMfdUnityCheck
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string Owner = "BoscaliSummer.Features.Command.Presentation.MapUi.VanillaMfdRebuild";
    private static int captures;
    private static int assertions;

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
            MethodInfo paths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            ParameterInfo[] parameters = paths.GetParameters();
            var args = new object[parameters.Length];
            args[0] = Path.GetFullPath("StockMfdCheck.exe");
            for (int i = 1; i < args.Length; i++) args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            paths.Invoke(null, args);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            // Legacy AvFont still labels a few v1 primitives; kit v2 parts resolve AvType, which with no
            // avionics-ui.bundle in this harness falls back to TMP_Settings' default face.
            AvFont.Font = TMP_FontAsset.CreateFontAsset(new Font("C:/Windows/Fonts/consola.ttf"));
            new GameObject("Events", typeof(EventSystem));
            foreach (float height in new[] { 896f, 596f, 420f })
                foreach (string name in new[] { "Map", "Target", "Faction", "Hud", "Mission" })
                    Render(name, height);
            File.WriteAllText("result.txt", "PASS: " + captures + " stock presenter page captures at 896/596/420 (kit v2 AvConsole pages), " + assertions +
                " reachability/overflow assertions. Production built DLL and game metadata; synthetic labels and grids, no game launch or native adapter claim. " +
                "A page taller than its body scrolls to its last row, and every scrollable page includes a bottom-of-content capture.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("result.txt", "FAIL: " + error);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void Render(string name, float height)
    {
        Assembly assembly = typeof(AvConsole).Assembly;
        Type type = assembly.GetType(Owner + "+" + name + "Presenter", true);
        object[] args = new object[] { null, null };
        if (name == "Mission") args = new object[] { null };
        if (name == "Faction")
        {
            Type id = assembly.GetType("BoscaliSummer.Features.Command.Presentation.MapUi.VanillaMfdPanelId", true);
            args = new object[] { null, null, null, Enum.Parse(id, "Bdf", true) };
        }
        object presenter = Activator.CreateInstance(type, All, null, args, null);
        var canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvasObject.transform;
        root.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
        type.GetMethod("Build", All).Invoke(presenter, new object[] { root });

        // Presenter.Console is a protected auto-property on the (private, nested) base type.
        AvConsole console = (AvConsole)type.BaseType.GetProperty("Console", All).GetValue(presenter);
        Check(console != null, name + ": Presenter.Build must create its AvConsole.");
        Check(console.PageCount == PageNames(name).Length, name + ": page count " + console.PageCount + " does not match its tabs.");
        var body = (RectTransform)console.Root.Find("Body");
        Check(body != null, name + ": AvConsole must expose a Body rect.");

        Chips(presenter, "PREVIEW", "DATA STUB", "OFFLINE");
        Seed(presenter, name);
        // Seeding mutates parts after the build-time relayout; the ticker is not pumped offline.
        for (int page = 0; page < console.PageCount; page++) console.Page(page).Relayout();

        for (int page = 0; page < console.PageCount; page++)
        {
            console.SetPage(page);
            Check(console.CurrentPage == page, name + ": SetPage(" + page + ") must land on that page.");
            console.SetTitle(Title(name, page));
            console.Footer.Set("OFFLINE LAYOUT PREVIEW \u00b7 SYNTHETIC DATA", AvState.Inert);
            Canvas.ForceUpdateCanvases();
            string prefix = name + "-" + height + "-" + page;
            // Capture before asserting, so a failing page still leaves its picture.
            Capture(canvasObject, height, prefix + ".png");

            ScrollRect scroll = console.Root.GetComponentInChildren<ScrollRect>();
            Check(scroll != null && scroll.vertical, name + " page " + page + ": the console body must scroll vertically.");
            Check(scroll.content != null && scroll.content.rect.height > 1f, name + " page " + page + ": page content must be laid out.");
            float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
            // Reachability: whatever does not fit the body must be scrolled into view, down to the last row.
            if (overflow > 1f)
            {
                scroll.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
                float travelled = scroll.content.anchoredPosition.y;
                Check(Mathf.Abs(travelled - overflow) <= 2f, name + " page " + page + " " + height + ": last row unreachable (scrolled " + travelled + " of " + overflow + ").");
                Capture(canvasObject, height, prefix + "-bottom.png");
                scroll.verticalNormalizedPosition = 1f;
            }
            else if (body.rect.height < 540f)
                Debug.Log(name + " page " + page + " " + height + ": content fits its compact body, nothing to scroll.");
        }
        Object.DestroyImmediate(canvasObject);
    }

    private static string[] PageNames(string name)
    {
        switch (name)
        {
            case "Map": return new[] { "LAYERS", "READABILITY" };
            case "Target": return new[] { "FILTERS / ACQUISITION", "ACQUIRE / CONTACTS", "PRESETS / LIBRARY", "TARGETS / TRACKED", "CAMERA / SENSOR MARK" };
            case "Faction": return new[] { "ECONOMY", "FORCES", "LEDGER", "POLITICS" };
            case "Hud": return new[] { "MODE", "VEHICLES", "BUILDINGS" };
            default: return new[] { "MISSION", "OBJECTIVES", "SECONDARY" };
        }
    }

    private static string Title(string name, int page)
    {
        string label = PageNames(name)[page];
        switch (name)
        {
            case "Faction": return "BOSCALI GENERAL AVIATION  /  " + label;
            case "Target": return "TARGETING  /  " + label;
            default: return name.ToUpperInvariant() + "  /  " + label;
        }
    }

    private static void Seed(object presenter, string name)
    {
        if (name == "Map")
        {
            Grid(presenter, "layers", new[] { "OBJECTIVES", "TARGET DETAILS", "JAMMING", "GRID LABELS", "PILOTS", "AIRBASES" },
                new[] { "Mission markers", "Target markers", "Jam indicators", "Grid coordinates", "Pilot icons", "Base icons" });
            Grid(presenter, "overlays", new[] { "CONTROL FIELD", "FRONT LINE", "SENSOR CONTOURS", "SATELLITE MAP" },
                new[] { "Sector control", "Control boundary", "Sensor coverage", "Terrain imagery" });
            Grid(presenter, "hover", new[] { "OFF", "UNIT INFO", "AMMUNITION", "ORDERS" },
                new[] { "No hover tooltip", "Unit information", "Weapon and ammunition", "Current unit orders" }, true);
            Grid(presenter, "sizes", new[] { "SMALL 60%", "MEDIUM 80%", "LARGE 100%" }, new[] { "COMPACT", "BALANCED", "FULL SIZE" }, true);
            Note(presenter, "overlayNote", "Sector control follows real ground presence; the front is its zero contour.");
            Note(presenter, "detailSummary", "UNIT INFO is selected. Hover a map unit to inspect it.");
            Note(presenter, "previewCaption", "MEDIUM 80% \u2022 Actual icons vary by unit; preview is illustrative.");
            foreach (object tile in (Array)Field(presenter, "previewTiles")) Call(tile, "SetState", true, .8f);
            Chips(presenter, "LAYERS 8/10", "TOOLTIP INFO", "MEDIUM 80%");
            var metrics = (AvMetric[])Field(presenter, "metrics");
            metrics[0].Set("1,000", "M", 1f, AvState.Ready);
            metrics[1].Set("LIVE", "", 1f, AvState.Ready);
            metrics[2].Set("4", "120 KM", 1f, AvState.Caution);
            metrics[3].Set("SAT", "SATELLITE", 1f, AvState.Ready);
        }
        if (name == "Target")
        {
            Grid(presenter, "factionGrid", new[] { "FRIENDLY", "ENEMY" }, null);
            Grid(presenter, "unitGrid", new[] { "AIRCRAFT", "MISSILES", "GROUND", "BUILDINGS", "SHIPS" }, null);
            Grid(presenter, "vehicleGrid", new[] { "TRUCK", "UGV", "LCV", "AFV", "MBT", "ART", "AAA", "IR SAM", "R SAM", "RADAR" }, null);
            Grid(presenter, "selectedGrid", new[] { "DARKREACH 21", "REVETMENT EAST AIRBASE", "TANK COMPANY NORTH" }, null);
            Grid(presenter, "candidateGrid", new[] { "RAVEN 3", "AAA BATTERY SOUTH", "SAM SITE ECHO", "CHICANE 11", "PATROL BOAT" },
                new[] { "3.2 KM \u00b7 KNOWN POSITION", "8.9 KM \u00b7 KNOWN POSITION", "12.4 KM \u00b7 KNOWN POSITION", "18.0 KM \u00b7 KNOWN POSITION", "31.5 KM \u00b7 KNOWN POSITION" });
            Grid(presenter, "groupGrid", new[] { "GROUP 1", "GROUP 2", "GROUP 3" }, new[] { "3 STORED", "0 STORED", "1 STORED" });
            Grid(presenter, "quickGrid", new[] { "AIR DEFENCE", "HOSTILE GROUND", "EMPTY" }, new[] { "SLOT 1 \u00b7 F5", "SLOT 2 \u00b7 F6", "SLOT 3 \u00b7 F7" });
            Grid(presenter, "presetGrid", new[] { "ALL", "AIR DEFENCE", "HOSTILE GROUND", "NAVAL", "AIRCRAFT", "CUSTOM" }, null);
            ((AvGauge)Field(presenter, "filterGauge")).Set(.8f, "17 / 21", AvState.Ready);
            Section(presenter, "contactsSection", "5 MATCH");
            Section(presenter, "presetSection", "1 / 8 SAVED");
            Section(presenter, "selectedSection", "3 TRACKED");
            ((AvReadout)Field(presenter, "presetReadout")).Set("ALL", "ACTIVE PROFILE", "Built-in. Every known contact, friend and foe.");
            ((AvRow)Field(presenter, "cameraStatusRow")).Set("RECON SURFACE MARK", "Surface reference recorded; expires 120 seconds after capture.", "", AvState.Ready);
            var camera = (AvRow[])Field(presenter, "cameraRows");
            string[] cameraKeys = { "GRID (X / Z)", "ELEVATION (Y)", "SLANT RANGE", "MARK AGE", "ARMED CALL-IN" };
            string[] cameraValues = { "X 12040 \u00b7 Z -8810", "412 m ASL", "6.3 km", "18s", "NONE (ARM IN OPS)" };
            for (int i = 0; i < camera.Length; i++) camera[i].Set(cameraKeys[i], null, cameraValues[i], AvState.Info);
            ((AvRow)Field(presenter, "cameraReticleRow")).Set("SENSOR ALIGNMENT", "SURFACE MARK LOCKED \u00b7 REFERENCE RECORDED", "", AvState.Ready);
            Chips(presenter, "17 FILTERS", "ALL", "HUD LINK");
        }
        if (name == "Faction")
        {
            Grid(presenter, "definitionGrid", new[] { "REVETMENT", "DARKREACH", "CHICANE", "CRICKET", "SENTINEL", "BULWARK", "OSPREY", "KESTREL" },
                new[] { "12 CURRENT  /  0 LOST", "9 CURRENT  /  3 LOST", "4 CURRENT  /  1 LOST", "2 CURRENT  /  6 LOST", "0 CURRENT  /  0 LOST", "1 CURRENT  /  0 LOST", "7 CURRENT  /  2 LOST", "5 CURRENT  /  5 LOST" });
            Grid(presenter, "infoGrid", new[] { "AIRBASE NORTH RIDGE", "AIRBASE SECTOR TWO", "HIGHWAY STRIP" },
                new[] { "OPERATIONAL \u00b7 6 AIRCRAFT", "OPERATIONAL \u00b7 2 AIRCRAFT", "CONTESTED \u00b7 0 AIRCRAFT" });
            Chips(presenter, "SCORE 41.5", "$24,600", "WHD 4");
            Call(Field(presenter, "factionHeader"), "Set", "Boscali Defence Force", "FACTION ORDER OF BATTLE");
            var resource = (AvMetric[])Field(presenter, "resourceMetrics");
            string[] figures = { "$24,600", "4", "138", "42.0" };
            string[] captions = { "AVAILABLE", "STOCKPILE", "IN ASSETS", "CONTRACTS 0.97x" };
            for (int i = 0; i < resource.Length; i++)
                resource[i].Set(figures[i], captions[i], i == 3 ? .42f : 0f, i == 3 ? AvState.Caution : AvState.Ready);

            // ECONOMY
            Text(presenter, "economyHeadline", "FRONTLINE OVERSTRETCH", AvState.Inert);
            Text(presenter, "economyEffect", "LOCAL EVENT / +35% SUPPORT COST \u2022 03:42 LEFT", AvState.Caution);
            Text(presenter, "economyContract", "FIELD CONTRACT / $1,800   +   150 XP", AvState.Inert);
            var samples = new float[36];
            for (int i = 0; i < samples.Length; i++) samples[i] = 18000f + 4200f * Mathf.Sin(i * .35f) + i * 180f;
            ((AvLineChart)Field(presenter, "resourceChart")).SetSeries(samples, samples.Length, "$13,800", "$28,900", "$24,600");
            Note(presenter, "resourceSummary", "FUNDS  \u2022  CHANGE +$6,600  \u2022  LAST 03:00");

            // FORCES
            var gauges = (AvGauge[])Field(presenter, "forceGauges");
            string[] gaugeText = { "42", "138", "6", "31" };
            float[] gaugeFill = { .3f, 1f, .05f, .22f };
            for (int i = 0; i < gauges.Length; i++) gauges[i].Set(gaugeFill[i], gaugeText[i], AvState.Ready);

            // LEDGER
            var ledger = (AvRow[])Field(presenter, "ledgerRows");
            string[] classes = { "BUILDINGS", "VEHICLES", "SHIPS", "AIRCRAFT" };
            string[] ledgerValues = { "42", "138", "6", "31" };
            for (int i = 0; i < ledger.Length; i++)
                ledger[i].Set(classes[i], "RESERVES / UNIT  \u2022  " + (20 + i * 15) + "% OF CLASS", ledgerValues[i], AvState.Ready);
            object chart = Property(Field(presenter, "ledgerChart"), "Chart");
            Call(chart, "Set", new[] { 42f, 138f, 6f, 31f }, new[] { 10f, 44f, 1f, 9f }, "RESERVES", "CURRENT", "UNIT",
                (Func<float, string>)(v => v.ToString("0")));

            // POLITICS
            Text(presenter, "mandateHeadline", "WAR WEARY", AvState.Caution);
            Text(presenter, "mandateEffect", "MORALE 42.0/100  \u2022  NEW CONTRACTS -3% MONEY / XP\nACTIVE DIRECTIVE / HOLD NORTHERN AIRBASE", AvState.Inert);
            Text(presenter, "politicalEvent", "FRONTLINE OVERSTRETCH", AvState.Inert);
            Text(presenter, "politicalEffect", "LOCAL EVENT \u2022 +35% SUPPORT COST \u2022 03:42 LEFT", AvState.Caution);
            Text(presenter, "politicalBrief", "LEADING SIDE \u2022 NEXT ORDER 00:45 / REAR DEPOTS DRAINED", AvState.Inert);
            Text(presenter, "politicalMission", "ACTIVE / HOLD NORTHERN AIRBASE", AvState.Inert);
            Text(presenter, "politicalMissionDetail", "$1,800   +   150 XP  \u2022  SUCCESS +3 MORALE", AvState.Inert);
        }
        if (name == "Hud")
        {
            Chips(presenter, "NAV", "6/10 VEH", "4/7 BLD");
            ((AvReadout)Field(presenter, "modeReadout")).Set("NAVIGATION", "", "AUTO SELECT / CURRENT MODE");
            ((AvRow)Field(presenter, "gatesRow")).Set("GATES", "MAXIMISE TRACKS", "3 OF 6", AvState.Info);
            ((AvRow)Field(presenter, "surfaceRow")).Set("SURFACE", null, "6 VEH \u00b7 4 BLD", AvState.Info);
            Grid(presenter, "modes", new[] { "NAV", "GUN", "A2A", "A2G", "EW", "LOG" },
                new[] { "Routes & waypoints", "Guns & lead cue", "Air intercepts", "Ground attack cues", "Emitters & jamming", "Transport routing" }, true);
            Grid(presenter, "categories", new[] { "FRIENDLY", "ENEMY", "AIRCRAFT", "MISSILES", "VEHICLES", "BUILDINGS" },
                new[] { "Friendly contacts", "Hostile contacts", "Airborne tracks", "Incoming missiles", "Ground vehicles", "Bases & structures" });
            Grid(presenter, "vehicles", new[] { "TRUCK", "UGV", "LCV", "AFV", "MBT", "ART", "AAA", "IR SAM", "R SAM", "RADAR" },
                new[] { "Supply trucks", "Unmanned vehicles", "Light combat armor", "Armored vehicles", "Heavy tanks", "Field artillery", "Anti-air guns", "Heat-seeking SAMs", "Radar-guided SAMs", "Search radars" });
            Grid(presenter, "buildings", new[] { "CIVILIAN", "INDUSTRY", "EW RADAR", "DEPOT", "HANGAR", "FORTIFY", "MUNITIONS" },
                new[] { "Civilian sites", "Industrial plants", "Early warning radar", "Fuel & supply depots", "Hangars & shelters", "Fortifications", "Munitions stores" });
            ((AvRow)Field(presenter, "airDefRow")).Set("AIR DEFENSE", null, "2 OF 4 ACTIVE", AvState.Ready);
            ((AvRow)Field(presenter, "armorRow")).Set("ARMORED TARGETS", null, "3 OF 3 ACTIVE", AvState.Ready);
            ((AvRow)Field(presenter, "strikeRow")).Set("STRIKE TARGETS", null, "4 OF 4 ACTIVE", AvState.Ready);
            ((AvRow)Field(presenter, "civilianRow")).Set("CIVILIAN ASSETS", null, "OFF (PROTECTED)", AvState.Inert);
        }
        if (name == "Mission")
        {
            Chips(presenter, "3 PRIMARY", "1/2 SECONDARY", "00:42:10");
            Call(Field(presenter, "briefPart"), "Set", "OPERATION DARKREACH",
                "Secure the northern airbase, suppress the SAM belt along the ridge and hold the highway strip until relief arrives.",
                true, "MISSION TIME 00:42:10   \u00b7   CAMPAIGN");
            ((AvSection)Field(presenter, "ladderSection")).SetCaption("HOLDING \u00b7 TACTICAL AT 60%");
            ((AvGauge)Field(presenter, "ladderGauge")).Set(.4f, "40%", AvState.Caution);
            var rungs = (AvRow[])Field(presenter, "ladderRows");
            string[] rungNames = { "CONVENTIONAL", "TACTICAL NUCLEAR", "STRATEGIC NUCLEAR" };
            string[] rungStates = { "CURRENT", "PENDING", "PENDING" };
            for (int i = 0; i < rungs.Length; i++) rungs[i].Set(rungNames[i], i == 0 ? "BASELINE \u2014 ALWAYS ACTIVE" : "THRESHOLD " + (i * 60), rungStates[i], i == 0 ? AvState.Caution : AvState.Inert);
            ((AvRow)Field(presenter, "contractPreviewRow")).Set("HOLD NORTHERN AIRBASE", "$1,800   +   150 XP", "OFFERED", AvState.Info);
            ((AvSection)Field(presenter, "objectivesSection")).SetCaption("3 ACTIVE");
            ((AvSection)Field(presenter, "boardSection")).SetCaption("1 / 2 ACTIVE");
            ((AvRow)Field(presenter, "boardSummaryRow")).Set("CONTRACT BOARD", "2 offers \u00b7 1 accepted", "OPEN", AvState.Info);
        }
    }

    // ---------------------------------------------------------------- reflection helpers

    private static object Field(object owner, string field)
    {
        for (Type t = owner.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo f = t.GetField(field, All | BindingFlags.DeclaredOnly);
            if (f != null)
            {
                object value = f.GetValue(owner);
                Check(value != null, owner.GetType().Name + "." + field + " is null after Build.");
                return value;
            }
        }
        throw new Exception(owner.GetType().Name + " has no field " + field + " (harness out of date with the presenter).");
    }

    private static object Property(object owner, string property)
    {
        PropertyInfo p = owner.GetType().GetProperty(property, All);
        if (p == null) throw new Exception(owner.GetType().Name + " has no property " + property + ".");
        return p.GetValue(owner);
    }

    private static object Call(object owner, string method, params object[] args)
    {
        foreach (MethodInfo m in owner.GetType().GetMethods(All))
            if (m.Name == method && m.GetParameters().Length == args.Length) return m.Invoke(owner, args);
        throw new Exception(owner.GetType().Name + " has no " + method + "/" + args.Length + " (harness out of date with the presenter).");
    }

    private static void Chips(object presenter, params string[] labels)
    {
        var chips = (AvChip[])Field(presenter, "chips");
        for (int i = 0; i < chips.Length && i < labels.Length; i++) chips[i].Set(labels[i], AvState.Inert);
    }

    private static void Section(object presenter, string field, string caption) => ((AvSection)Field(presenter, field)).SetCaption(caption);
    private static void Note(object presenter, string field, string text) => Call(Field(presenter, field), "Set", text);
    private static void Text(object presenter, string field, string text, AvState state) => Call(Field(presenter, field), "Set", text, state);

    /// <summary>MfdPagingGrid.SetData with labels, optional second-line subs, and either exclusive
    /// (radio) or mixed selection so both lit and unlit cells are painted.</summary>
    private static void Grid(object owner, string field, string[] labels, string[] subs, bool exclusive = false)
    {
        object grid = Field(owner, field);
        Func<int, string> subFn = subs == null ? (Func<int, string>)null : (i => i < subs.Length ? subs[i] : null);
        grid.GetType().GetMethod("SetData", All).Invoke(grid, new object[]
        {
            labels.Length, (Func<int, string>)(i => labels[i]), (Func<int, bool>)(i => exclusive ? i == 0 : i % 3 != 2),
            (Action<int>)(_ => { }), null, null, null, subFn
        });
    }

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }

    private static void Capture(GameObject canvas, float height, string path)
    {
        Canvas.ForceUpdateCanvases();
        foreach (ScrollRect scroll in canvas.GetComponentsInChildren<ScrollRect>())
            scroll.Rebuild(CanvasUpdate.PostLayout);
        string failure = null; // report after the picture is written, so a failing page still leaves its PNG
        foreach (TMP_Text label in canvas.GetComponentsInChildren<TMP_Text>())
        {
            label.ForceMeshUpdate();
            // MfdIconCell titles (parent "Cell") are wrapped in a bounded rect: never clipped.
            if (label.transform.parent.name == "Cell" && label.name == "Title" && label.isTextOverflowing)
                failure = failure ?? path + ": filter cell overflow: " + label.text;
            // ...and neither Title nor Sub may spill out of the cell's fixed-height frame (a 3-column
            // grid wraps a long label onto extra lines that the row height must still contain).
            if (label.transform.parent.name == "Cell" && (label.name == "Title" || label.name == "Sub") && label.text.Length > 0)
            {
                var corners = new Vector3[4];
                var cell = (RectTransform)label.transform.parent;
                cell.GetWorldCorners(corners);
                float cellBottom = corners[0].y;
                label.rectTransform.GetWorldCorners(corners);
                if (corners[0].y < cellBottom - .5f)
                    failure = failure ?? path + ": cell text spills below its frame: " + label.text;
            }
        }
        var cameraObject = new GameObject("Camera", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = height / 2f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = AvStyleHost.FuiColor("ground", AvTheme.Surface);
        var target = new RenderTexture(960, (int)height * 2, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(cameraObject);
        captures++;
        if (failure != null) throw new Exception(failure);
    }
}
#endif
