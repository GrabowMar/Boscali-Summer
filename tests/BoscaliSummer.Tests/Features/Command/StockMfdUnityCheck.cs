#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// Real production presenters and installed game type metadata, synthetic display data.
// This verifies kit v2 console layout and reachability, not game adapters, authority or multiplayer.
// Each presenter owns one AvConsole (the `Console` property of the Presenter base); its paged body is a
// single ScrollRect, so a page whose flow is taller than the body must be scrollable to its last row.
public static class StockMfdUnityCheck
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string Owner = "BoscaliSummer.Modules.Command.Presentation.MapUi.VanillaMfdRebuild";
    private static int captures;
    private static int assertions;

    public static void Run()
    {
        try
        {
            if (!EnsureTmpEssentials(Run)) return;
            SetExecutablePath("StockMfdCheck.exe");
            AvBundle.Load(Debug.Log);
            Check(AvBundle.Available && AvIcons.Available, "Production fonts and icon bundle must load before rendering.");
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            // The production assembly embeds the current font/shader bundle.
            new GameObject("Events", typeof(EventSystem));
            foreach (float height in new[] { 896f, 596f, 420f })
                foreach (string name in new[] { "Map", "Target", "Faction", "Hud", "Mission", "MissionEmpty" })
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
        Type type = assembly.GetType(Owner + "+" + (name == "MissionEmpty" ? "Mission" : name) + "Presenter", true);
        object[] args = new object[] { null, null };
        if (name.StartsWith("Mission")) args = new object[] { null };
        if (name == "Faction")
        {
            Type id = assembly.GetType("BoscaliSummer.Modules.Command.Presentation.MapUi.VanillaMfdPanelId", true);
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

        Seed(presenter, name);
        // Seeding happens after Finish(), as it does in the game; the ticker is pumped by hand (Unity time does
        // not advance offline), which is what re-measures every part whose content changed.
        for (int i = 0; i < 3; i++) console.Ticker.TickNow();

        for (int page = 0; page < console.PageCount; page++)
        {
            console.SetPage(page);
            Check(console.CurrentPage == page, name + ": SetPage(" + page + ") must land on that page.");
            console.SetTitle(Title(name, page));
            console.Footer.Set("OFFLINE LAYOUT PREVIEW \u00b7 SYNTHETIC DATA", AvState.Inert);
            for (int i = 0; i < 2; i++) console.Ticker.TickNow();
            Canvas.ForceUpdateCanvases();
            string prefix = name + "-" + height + "-" + page;
            // Capture before asserting, so a failing page still leaves its picture.
            Capture(canvasObject, height, prefix + ".png");
            Gate(console, prefix);

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
            default: return new[] { "MISSION", "OBJECTIVES", "CONTRACTS" };
        }
    }

    private static string Title(string name, int page)
    {
        string label = PageNames(name)[page];
        switch (name)
        {
            case "Faction": return "BOSCALI GENERAL AVIATION  /  " + label;
            case "Target": return "TARGETING  /  " + label;
            case "MissionEmpty": return "MISSION (NO DATA)  /  " + label;
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
            ((AvSlab)Field(presenter, "overlaySlab")).Set("CONTROL FIELD / LIVE GROUND PRESENCE", AvState.Ready);
            Note(presenter, "detailSummary", "UNIT INFO is selected. Hover a map unit to inspect it.");
            Note(presenter, "previewCaption", "MEDIUM 80% \u2022 Frame shape marks the side; jets keep the game's own silhouette.");
            foreach (object tile in (Array)Field(presenter, "previewTiles")) Call(tile, "SetState", true, .8f);
            var metrics = (AvMetric[])Field(presenter, "metrics");
            metrics[0].Set("1,000", "M", 1f, AvState.Ready);
            metrics[1].Set("LIVE", "", 1f, AvState.Ready);
            metrics[2].Set("4", "120 KM", 1f, AvState.Caution);
            metrics[3].Set("SAT", "SATELLITE", 1f, AvState.Ready);
        }
        if (name == "Target")
        {
            Grid(presenter, "factionGrid", new[] { "FRIENDLY", "ENEMY" }, new[] { "Friendly contacts", "Hostile contacts" });
            Grid(presenter, "unitGrid", new[] { "AIRCRAFT", "MISSILES", "GROUND", "BUILDINGS", "SHIPS" },
                new[] { "Airborne tracks", "Missiles in flight", "Vehicles & troops", "Bases & structures", "Naval contacts" });
            Grid(presenter, "vehicleGrid", new[] { "TRUCK", "UGV", "LCV", "AFV", "MBT", "ART", "AAA", "IR SAM", "R SAM", "RADAR" },
                new[] { "Supply trucks", "Unmanned ground", "Light combat", "Armored vehicles", "Main battle tanks", "Field artillery",
                    "Anti-air guns", "Heat-seeking SAM", "Radar-guided SAM", "Search radars" }, false, true);
            Grid(presenter, "selectedGrid", new[] { "DARKREACH 21", "REVETMENT EAST AIRBASE", "TANK COMPANY NORTH" }, null);
            Grid(presenter, "candidateGrid", new[] { "RAVEN 3", "AAA BATTERY SOUTH", "SAM SITE ECHO", "CHICANE 11", "PATROL BOAT" },
                new[] { "3.2 KM \u00b7 KNOWN POSITION", "8.9 KM \u00b7 KNOWN POSITION", "12.4 KM \u00b7 KNOWN POSITION", "18.0 KM \u00b7 KNOWN POSITION", "31.5 KM \u00b7 KNOWN POSITION" });
            Grid(presenter, "groupGrid", new[] { "GROUP 1", "GROUP 2", "GROUP 3" }, new[] { "3 STORED", "0 STORED", "1 STORED" });
            Grid(presenter, "quickGrid", new[] { "AIR DEFENCE", "HOSTILE GROUND", "EMPTY" }, new[] { "SLOT 1 \u00b7 F5", "SLOT 2 \u00b7 F6", "SLOT 3 \u00b7 F7" });
            Grid(presenter, "presetGrid", new[] { "ALL", "AIR DEFENCE", "HOSTILE GROUND", "NAVAL", "AIRCRAFT", "CUSTOM" },
                new[] { "Built-in profile", "Built-in profile", "Built-in profile", "Built-in profile", "Built-in profile", "Saved preset" });
            var filterTiles = (AvGauge[])Field(presenter, "filterMetrics");
            filterTiles[0].Set(1f, "2/2", AvState.Ready);
            filterTiles[1].Set(.8f, "4/5", AvState.Info);
            filterTiles[2].Set(0f, "0/10", AvState.Caution);
            Section(presenter, "contactsSection", "5 MATCH");
            Section(presenter, "presetSection", "1 / 8 SAVED");
            Section(presenter, "selectedSection", "3 TRACKED");
            ((AvReadout)Field(presenter, "presetReadout")).Set("ALL", "ACTIVE PROFILE", "Built-in. Every known contact, friend and foe.");
            ((AvSlab)Field(presenter, "cameraSlab")).Set("RECON MARK", AvState.Ready);
            var cameraTiles = (AvGauge[])Field(presenter, "cameraRings");
            cameraTiles[0].Set(.315f, "6.3", AvState.Info);
            cameraTiles[1].Set(412f / 3000f, "412", AvState.Info);
            ((AvHazardBar)Field(presenter, "cameraAge")).Set(.85f, "18 S", AvState.Info);
            var camera = (AvRow[])Field(presenter, "cameraRows");
            camera[0].Set("GRID (X / Z)", "X 12040 \u00b7 Z -8810", "", AvState.Info);
            camera[1].Set("ARMED CALL-IN", "NONE (ARM IN OPS)", "", AvState.Inert);
        }
        if (name == "Faction")
        {
            Roster(presenter, "definitionGrid");
            Roster(presenter, "attritionRoster");
            Grid(presenter, "infoGrid", new[] { "AIRBASE NORTH RIDGE", "AIRBASE SECTOR TWO", "HIGHWAY STRIP" },
                new[] { "OPERATIONAL \u00b7 6 AIRCRAFT", "OPERATIONAL \u00b7 2 AIRCRAFT", "CONTESTED \u00b7 0 AIRCRAFT" });
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
            var historyTiles = (Array)Field(Field(presenter, "history"), "Tiles");
            string[] minimum = { "$13,800", "3", "120", "30" }, maximum = { "$28,900", "8", "200", "60" };
            float[] starts = { 18000f, 4f, 145f, 42f }, swings = { 4200f, 1f, 20f, 8f }, drift = { 180f, .05f, .6f, .2f };
            for (int i = 0; i < historyTiles.Length; i++) {
                for (int j = 0; j < samples.Length; j++) samples[j] = starts[i] + swings[i] * Mathf.Sin(j * .35f) + j * drift[i];
                Call(historyTiles.GetValue(i), "Set", samples, samples.Length, minimum[i], maximum[i], figures[i], "CHANGE +6% / LAST 03:00");
            }

            // FORCES
            var gauges = (AvGauge[])Field(presenter, "ledgerRows");
            string[] gaugeText = { "42", "138", "6", "31" };
            float[] gaugeFill = { .3f, 1f, .05f, .22f };
            for (int i = 0; i < gauges.Length; i++) gauges[i].Set(gaugeFill[i], gaugeText[i], AvState.Ready);

            // LEDGER
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
            Call(Field(presenter, "modeHeader"), "SetMode", "NAVIGATION");
            ((AvGauge)Field(presenter, "gatesRing")).Set(.5f, "3/6", AvState.Info);
            ((AvGauge)Field(presenter, "typesRing")).Set(10f / 17f, "10/17", AvState.Info);
            Grid(presenter, "modes", new[] { "NAV", "GUN", "A2A", "A2G", "EW", "LOG" },
                new[] { "Routes & waypoints", "Guns & lead cue", "Air intercepts", "Ground attack cues", "Emitters & jamming", "Transport routing" }, true);
            Grid(presenter, "categories", new[] { "FRIENDLY", "ENEMY", "AIRCRAFT", "MISSILES", "VEHICLES", "BUILDINGS" },
                new[] { "Friendly contacts", "Hostile contacts", "Airborne tracks", "Incoming missiles", "Ground vehicles", "Bases & structures" });
            Grid(presenter, "vehicles", new[] { "TRUCK", "UGV", "LCV", "AFV", "MBT", "ART", "AAA", "IR SAM", "R SAM", "RADAR" },
                new[] { "Supply trucks", "Unmanned vehicles", "Light combat armor", "Armored vehicles", "Heavy tanks", "Field artillery", "Anti-air guns", "Heat-seeking SAMs", "Radar-guided SAMs", "Search radars" });
            Grid(presenter, "buildings", new[] { "CIVILIAN", "INDUSTRY", "EW RADAR", "DEPOT", "HANGAR", "FORTIFY", "MUNITIONS" },
                new[] { "Civilian sites", "Industrial plants", "Early warning radar", "Fuel & supply depots", "Hangars & shelters", "Fortifications", "Munitions stores" });
            ((AvGauge)Field(presenter, "airDefRing")).Set(.5f, "2/4", AvState.Ready);
            ((AvGauge)Field(presenter, "armorRing")).Set(1f, "3/3", AvState.Ready);
            ((AvGauge)Field(presenter, "shownVehRing")).Set(.6f, "6/10", AvState.Ready);
            ((AvGauge)Field(presenter, "strikeRing")).Set(1f, "4/4", AvState.Ready);
            ((AvGauge)Field(presenter, "militaryRing")).Set(4f / 6f, "4/6", AvState.Ready);
            ((AvGauge)Field(presenter, "civilianRing")).Set(0f, "OFF", AvState.Inert);
        }
        if (name == "Mission") SeedMission(presenter);
        if (name == "MissionEmpty") Call(presenter, "Render");
    }

    private const string Ns = "BoscaliSummer.Modules.Command.Presentation.MapUi.";

    /// <summary>A populated MIS board: two done and four live objectives, escalation holding at tactical,
    /// four offered contracts (two pages), a lead contract and a log.</summary>
    private static void SeedMission(object presenter)
    {
        Assembly assembly = typeof(AvConsole).Assembly;
        object model = Field(presenter, "model");
        SetField(model, "MissionName", "OPERATION DARKREACH");
        SetField(model, "Brief", "Secure the northern airbase, suppress the SAM belt along the ridge and hold the highway strip until relief arrives. " +
            "Expect armour from the east after the first hour; keep the tanker on station.");
        SetField(model, "HasBrief", true);
        SetField(model, "Mode", "MULTIPLAYER");
        SetField(model, "Clock", "00:42:10");
        SetField(model, "HasHq", true);
        SetField(model, "Score", 41.5f);
        SetField(model, "HasEscalation", true);
        SetField(model, "Current", 42f);
        SetField(model, "Tactical", 60f);
        SetField(model, "Strategic", 120f);

        Type lineType = presenter.GetType().GetNestedType("ObjectiveLine", All);
        Type phase = assembly.GetType(Ns + "MissionPhase", true);
        var lines = (IList)Field(model, "Objectives");
        string[] titles = { "Capture Northern Airbase", "Destroy SAM Battery Ridge", "Destroy Armoured Column East", "Reach Highway Strip", "Hold Relief Corridor", "Survey Depot Fire", "Destroy Fuel Depot Ostrov", "Capture Highway Junction" };
        string[] kinds = { "CAPTURE", "DESTROY", "DESTROY", "REACH", "HOLD", "SURVEIL", "DESTROY", "CAPTURE" };
        AvIcon[] icons = { AvIcon.Flag, AvIcon.Target, AvIcon.Target, AvIcon.MapPin, AvIcon.Clock, AvIcon.Eye, AvIcon.Target, AvIcon.Flag };
        float[] fractions = { 1f, 1f, .4f, .15f, 0f, .7f, .05f, 0f };
        float[] distances = { -1f, -1f, 18400f, 6200f, 850f, -1f, 41250f, 112000f };
        for (int i = 0; i < titles.Length; i++)
        {
            object line = Activator.CreateInstance(lineType);
            SetField(line, "Key", "obj" + i);
            SetField(line, "Title", titles[i]);
            SetField(line, "Kind", kinds[i]);
            SetField(line, "Source", "Objective " + (i + 1));
            SetField(line, "Icon", icons[i]);
            SetField(line, "Fraction", fractions[i]);
            SetField(line, "DistanceM", distances[i]);
            SetField(line, "Phase", Enum.Parse(phase, i < 2 ? "Done" : "Active"));
            lines.Add(line);
        }
        SetField(model, "ObjectivesDone", 2);
        SetField(model, "ObjectivesActive", 6);
        SetField(model, "ObjectiveSummary", "DESTROY 2   \u00b7   CAPTURE 1   \u00b7   REACH 1");

        SetField(model, "Installed", true);
        SetField(model, "Streamed", true);
        SetField(model, "Limit", 2);
        SetField(model, "ActiveContracts", 1);
        SetField(model, "Offers", 4);
        SetField(model, "Closed", 2);
        SetField(model, "AtStake", 1500);
        SetField(model, "Offered", 6400);
        SetField(model, "Paid", 2400);
        Type viewType = assembly.GetType("BoscaliSummer.Core.Contracts.SecondaryObjectiveView", true);
        var contracts = (IList)Field(model, "Contracts");
        string[] cTitles = { "SURVEY THE AFTERMATH", "ROOFTOP INSERTION", "HOLD THE LINE", "SILENCE THE RADAR" };
        for (int i = 0; i < cTitles.Length; i++)
        {
            object[] values =
            {
                i + 26, cTitles[i],
                "Recon patrol requests a clear visual report near the northern depot. Approach from a safe angle and hold the mark while the faction tasking clock runs.",
                "Northern Depot / Observation Sector", "AWAITING ACCEPTANCE", "$1,500 + 125 XP", 0f, i == 2 ? 95f : 240f + i * 60f,
                1500 + i * 500, 125, false, true, false, true, 0f, 0f, 1f, ""
            };
            object view = Activator.CreateInstance(viewType, All, null, values, null);
            contracts.Add(view);
            if (i == 0) SetField(model, "Lead", view);
        }
        SetField(presenter, "secondaryHasCapacity", true);
        object log = Field(presenter, "log");
        Call(log, "Add", "CONTRACT ACCEPTED  \u00b7  #26 SURVEY THE AFTERMATH", "00:41:52", AvState.Info);
        Call(log, "Add", "OBJECTIVE COMPLETE  \u00b7  Destroy SAM Battery Ridge", "00:38:05", AvState.Ready);
        Call(log, "Add", "CONTRACT CLOSED  \u00b7  #24 HOLD THE LINE", "00:31:40", AvState.Caution);
        Call(presenter, "Render");
    }

    // ---------------------------------------------------------------- reflection helpers

    private static void Roster(object presenter, string field)
    {
        string[] names = { "REVETMENT", "DARKREACH", "CHICANE", "CRICKET", "SENTINEL", "BULWARK", "OSPREY", "KESTREL" };
        int[] live = { 12, 9, 4, 2, 0, 1, 7, 5 }, lost = { 0, 3, 1, 6, 0, 0, 2, 5 };
        Call(Field(presenter, field), "SetRecords", names.Length,
            (Func<int, string>)(i => names[i]), (Func<int, string>)(i => "Synthetic mission accounting for " + names[i]),
            (Func<int, Sprite>)(_ => IconSprite()), (Func<int, int>)(i => live[i]),
            (Func<int, int>)(i => lost[i]), (Func<int, int>)(i => i + 2),
            "UNIT MANIFEST", "LIVE / LOST / MOBILE RESERVES");
    }

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

    private static void Section(object presenter, string field, string caption) => ((AvSection)Field(presenter, field)).SetCaption(caption);
    private static void Note(object presenter, string field, string text) => Call(Field(presenter, field), "Set", text);
    private static void Text(object presenter, string field, string text, AvState state) => Call(Field(presenter, field), "Set", text, state);

    /// <summary>MfdPagingGrid.SetData with labels, optional second-line subs, and either exclusive
    /// (radio) or mixed selection so both lit and unlit cells are painted.</summary>
    private static void Grid(object owner, string field, string[] labels, string[] subs, bool exclusive = false, bool icons = false)
    {
        object grid = Field(owner, field);
        Func<int, string> subFn = subs == null ? (Func<int, string>)null : (i => i < subs.Length ? subs[i] : null);
        Func<int, Sprite> iconFn = icons ? (Func<int, Sprite>)(_ => IconSprite()) : null;
        grid.GetType().GetMethod("SetData", All).Invoke(grid, new object[]
        {
            labels.Length, (Func<int, string>)(i => labels[i]), (Func<int, bool>)(i => exclusive ? i == 0 : i % 3 != 2),
            (Action<int>)(_ => { }), null, iconFn, null, subFn
        });
    }

    private static Sprite iconSprite;

    /// <summary>A stand-in for a NATO/vehicle-class mapIcon: a framed box, so the icon cell's geometry is exercised.</summary>
    private static Sprite IconSprite()
    {
        if (iconSprite != null) return iconSprite;
        var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                tex.SetPixel(x, y, x < 3 || y < 3 || x > 28 || y > 28 || (x > 8 && x < 24 && y > 12 && y < 19) ? Color.white : Color.clear);
        tex.Apply();
        iconSprite = Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(.5f, .5f));
        return iconSprite;
    }

    /// <summary>Pages other than the current one are hidden by disabling their canvas, not by deactivating them.</summary>
    private static bool CanvasOn(Transform t)
    {
        foreach (Canvas c in t.GetComponentsInParent<Canvas>(false))
            if (!c.enabled) return false;
        return true;
    }

    /// <summary>Text must fit its rect, stay out of the scroll gutter and never overlap a sibling part.</summary>
    private static void Gate(AvConsole con, string where)
    {
        float gutterLeft = AvTokens.PanelWidth - AvGridTokens.Pad - AvGridTokens.Gutter + 0.5f;
        foreach (TMP_Text t in con.Root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || t.name.StartsWith("Icon") || !CanvasOn(t.transform)) continue;
            t.ForceMeshUpdate();
            Rect r = t.rectTransform.rect;
            Bounds b = t.textBounds;
            if (b.size.x > r.width + 1.5f)
                throw new Exception(where + ": text overflows its width (" + b.size.x.ToString("0") + " > " + r.width.ToString("0") + ") '" + t.text + "'");
            if (b.size.y > r.height + 1.5f)
                throw new Exception(where + ": text overflows its height (" + b.size.y.ToString("0") + " > " + r.height.ToString("0") + ") '" + t.text + "'");
            if (t.fontSize < AvTypeScale.Floor - 0.01f)
                throw new Exception(where + ": text below the 11 px floor (" + t.fontSize.ToString("0.0") + ") '" + t.text + "'");
            if (t.GetComponentInParent<ScrollRect>() != null)
            {
                var corners = new Vector3[4];
                t.rectTransform.GetWorldCorners(corners);
                float right = con.Root.InverseTransformPoint(corners[2]).x;
                if (right > gutterLeft) throw new Exception(where + ": text enters the scroll gutter (" + right.ToString("0") + ") '" + t.text + "'");
            }
        }
        // Sibling parts of the page flow must not overlap.
        ScrollRect scroll = con.Root.GetComponentInChildren<ScrollRect>();
        RectTransform content = scroll != null ? scroll.content : null;
        if (content == null) return;
        // scroll.content is the current page's rect; its children are the flow's parts.
        RectTransform page = content;
        for (int i = 0; i < page.childCount; i++)
        {
            var a = page.GetChild(i) as RectTransform;
            if (a == null || !a.gameObject.activeSelf) continue;
            for (int j = i + 1; j < page.childCount; j++)
            {
                var b = page.GetChild(j) as RectTransform;
                if (b == null || !b.gameObject.activeSelf) continue;
                float ax0 = a.anchoredPosition.x, ax1 = ax0 + a.rect.width, ay0 = -a.anchoredPosition.y, ay1 = ay0 + a.rect.height;
                float bx0 = b.anchoredPosition.x, bx1 = bx0 + b.rect.width, by0 = -b.anchoredPosition.y, by1 = by0 + b.rect.height;
                if (ax0 < bx1 - .5f && bx0 < ax1 - .5f && ay0 < by1 - .5f && by0 < ay1 - .5f)
                    throw new Exception(where + ": '" + a.name + "' overlaps '" + b.name + "'");
            }
        }
    }

    private static void SetField(object owner, string field, object value)
    {
        for (Type t = owner.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo f = t.GetField(field, All | BindingFlags.DeclaredOnly);
            if (f == null) continue;
            f.SetValue(owner, value);
            return;
        }
        throw new Exception(owner.GetType().Name + " has no field " + field + " (harness out of date with the presenter).");
    }

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }

    private static void Capture(GameObject canvas, float height, string path)
    {
        // Capture settled content: executeMethod does not advance the transition timers.
        foreach (Image fill in canvas.GetComponentsInChildren<Image>(true))
            if (fill.name == "ScanCover") fill.enabled = false;
        foreach (AvReveal reveal in canvas.GetComponentsInChildren<AvReveal>(true)) reveal.Finish();
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
        Camera camera = OrthoCamera("Camera", height / 2f, AvStyleHost.FuiColor("ground", AvTheme.Surface));
        CapturePng(camera, 960, (int)height * 2, path);
        Object.DestroyImmediate(camera.gameObject);
        captures++;
        if (failure != null) throw new Exception(failure);
    }
}
#endif
