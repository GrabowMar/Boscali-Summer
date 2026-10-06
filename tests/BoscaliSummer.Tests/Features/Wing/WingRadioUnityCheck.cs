#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using BepInEx.Logging;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// Production DLL builders and refresh methods; no copied presenter implementation.
// Synthetic snapshots/catalogues exercise offline layout, never audio decoding or world mutation.
public static class WingRadioUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Assembly Mod = typeof(AvConsole).Assembly;
    private const string Wing = "BoscaliSummer.Modules.Wing.";
    private const string Radio = "BoscaliSummer.Modules.Radio.";
    private static readonly List<string> failures = new List<string>();
    private static readonly List<string> measurements = new List<string> { "capture\ttext\tfont\twidth\theight\tpreferredWidth\tpreferredHeight\toverflow\tmeshMinX\tmeshMaxX" };
    private static readonly List<string> captureManifest = new List<string> { "file\twidth\theight" };
    private static readonly List<string> musicBounds = new List<string> { "capture\trow\tminY\tmaxY\tviewportMinY\tviewportMaxY\tcontentHeight\tviewportHeight" };
    private static int captures, assertions;
    private static bool radioOnlyMode;

    public static void Run() => RunPreview(false);
    public static void RunRadioOnly() => RunPreview(true);

    private static void RunPreview(bool radioOnly)
    {
        radioOnlyMode = radioOnly;
        try
        {
            if (!EnsureTmpEssentials(() => RunPreview(radioOnly))) return;
            SetExecutablePath(radioOnly ? "RadioCheck.exe" : "WingRadioCheck.exe");
            if (radioOnly) Check(Mod.GetName().Name == "BoscaliSummer", "Radio-only builders must resolve from the production mod DLL");
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvBundle.Load(Debug.Log);
            Check(AvBundle.Available && AvIcons.Available, "Production font/shader/icon bundle must load before rendering");
            if (!AvBundle.Available || !AvIcons.Available) throw new InvalidOperationException("Production UI bundle unavailable");
            new GameObject("Events", typeof(EventSystem));
            var config = new ConfigFile(Path.GetFullPath("preview.cfg"), false);
            config.SaveOnConfigSet = false;
            if (!radioOnly)
            {
                // Only native field reads are used: no Unity attachment, lifecycle or mission services.
                var mission = (MissionManager)FormatterServices.GetUninitializedObject(typeof(MissionManager));
                mission.currentEscalation = 35f; mission.tacticalThreshold = 20f; mission.strategicThreshold = 80f;
                NetworkSceneSingleton<MissionManager>.i = mission;
                Check(MissionManager.AllowTactical() && !MissionManager.AllowStrategic(), "Synthetic escalation adapter must read native mission facts");
                TypeOf(Wing + "Configuration.WingSettings").GetProperty("Instance", All).SetValue(null,
                    New(Wing + "Configuration.WingConfig", config));
                CallStatic(Wing + "Runtime.WingLog", "Init", new ManualLogSource("WingPreview"));
            }
            foreach (float height in new[] { 896f, 596f, 420f })
            {
                if (!radioOnly) RenderWing(height);
                RenderRadio(height, config);
            }
            if (!radioOnly) RenderRecoveryOffer();
            if (radioOnly)
                for (int i = 1; i < captureManifest.Count; i++)
                    Check(captureManifest[i].StartsWith("RAD-", StringComparison.Ordinal), "Radio-only capture manifest must exclude Wing surfaces");
            File.WriteAllLines("text-measurements.tsv", measurements);
            File.WriteAllLines("capture-manifest.tsv", captureManifest);
            File.WriteAllLines("music-bounds.tsv", musicBounds);
            File.WriteAllLines("failures.txt", failures);
            File.WriteAllText("result.txt", (failures.Count == 0 ? "PASS: " : "FAIL: ") + captures + " production " + (radioOnly ? "Radio-only" : "Wing/Radio") + " captures at 896/596/420; " + assertions +
                " layout, text and flight-input-isolation assertions; " + failures.Count + " failures. Synthetic display data; no game launch, audio, host/client, native radial or adapter acceptance.\n");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }
        catch (Exception error)
        {
            File.WriteAllText("result.txt", "FAIL: " + error);
            File.WriteAllLines("text-measurements.tsv", measurements);
            File.WriteAllLines("capture-manifest.tsv", captureManifest);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void RenderWing(float height)
    {
        var canvas = NewCanvas("WMC", height);
        var console = AvConsole.Build((RectTransform)canvas.transform, "WMC", "WING / OFFLINE", 5, AvTokens.PanelWidth, height);
        console.Tabs((AvIcon.Target, "TACTICAL"), (AvIcon.AdjustmentsHorizontal, "BEHAVIOUR"), (AvIcon.Coins, "SUPPLY"),
            (AvIcon.Stack2, "LOADOUT"), (AvIcon.UsersGroup, "WING"));
        var chips = console.Chips(3);
        chips[0].Set("FUEL MIN 23%", AvState.Caution);
        chips[1].Set("AMMO MIN 61%", AvState.Ready);
        chips[2].Set("THREAT 2", AvState.Caution);
        object controls = New(Wing + "Presentation.WmcControls");
        string[] classes = { "WmcTactical", "WmcPlan", "WmcSupply", "WmcLoadout", "WmcWing" };
        object[] pages = new object[classes.Length];
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i] = New(Wing + "Presentation." + classes[i], controls);
            Call(pages[i], "Build", console.Page(i), console.Ticker, i);
        }
        console.Finish();
        object context = New(Wing + "Presentation.WmcContext");
        Set(context, "Client", true);
        Set(context, "Count", 3);
        Set(context, "MissionTime", 1840f);
        Array rows = (Array)Field(context, "Rows");
        for (int i = 0; i < 3; i++)
        {
            object row = Activator.CreateInstance(rows.GetType().GetElementType());
            Set(row, "Id", (uint)(100 + i)); Set(row, "Slot", (byte)i); Set(row, "Element", (byte)(i == 2 ? 1 : 0));
            Set(row, "Fuel", (byte)(i == 0 ? 23 : 86)); Set(row, "Ammo", (byte)(i == 1 ? 61 : 100));
            rows.SetValue(row, i);
        }
        Call(context, "Rescope");
        string[][] subs = { new[] { "ORDERS", "FORMATION", "ROUTE" }, new[] { "TUNING", "PLAN", "RECORD" },
            new[] { "DISPATCH" }, new[] { "PRESETS" }, new[] { "ROSTER", "STUDIO" } };
        for (int i = 0; i < pages.Length; i++)
        {
            console.SetPage(i);
            foreach (string sub in subs[i])
            {
                if (i == 0 || i == 1 || i == 4) Call(pages[i], "ShowSubNamed", sub);
                Call(pages[i], "Refresh", context);
                console.Footer.Set("OFFLINE / CLIENT MIRROR / " + sub, AvState.Info);
                Settle(console);
                string prefix = "WMC-" + height + "-" + i + "-" + sub;
                Capture(canvas, height, prefix + ".png");
                Gate(console, prefix);
                Bottom(canvas, console, height, prefix);
                SeedWingDisplay(pages[i], i, sub);
                console.Page(i).RequestRelayout(); Settle(console);
                Capture(canvas, height, prefix + "-populated.png"); Gate(console, prefix + "-populated");
                Bottom(canvas, console, height, prefix + "-populated");
            }
        }
        Object.DestroyImmediate(canvas);
    }

    private static void SeedWingDisplay(object page, int index, string sub)
    {
        if (index == 0 && sub == "FORMATION")
        {
            object form = Field(page, "form");
            Call(Field(form, "view"), "SetValues", "FINGER FOUR / FIXED WING", "STANDARD / 80 m", "LEVEL / GATE", "B COASTAL WATCH / 3 AC", "3/4 IN SLOT");
            object family = Field(form, "familyGrid"), shape = Field(form, "shapeGrid");
            Call(family, "SetShownCount", 3); Call(shape, "SetShownCount", 4);
            SetGrid(family, new[] { "FIXED WING", "ROTARY", "ESCORT" });
            SetGrid(shape, new[] { "FINGER FOUR", "LINE ABREAST", "TRAIL", "VIC" });
            ((AvFlow)Field(form, "flow")).RequestRelayout();
        }
        if (index == 2)
        {
            Call(Field(page, "dispatch"), "Set", "BLOCKED", AvState.Caution, "KR-67 / 42,500", null,
                "NIGHTJAR / FINGER FOUR / 75% FUEL / FORWARD OPERATING BASE EAST",
                "Crew limit reached. Reserve or release a wingman before requisitioning this airframe.", "warn");
        }
        if (index == 3)
        {
            Call(Field(page, "card"), "Set", "KR-67 / COASTAL PATROL", "6/8 STATIONS / 2,450 KG / 1 BLOCKED STORE", "EDITED", "warn", "warn", null, .75f);
            SeedHardpoints(page);
        }
        if (index == 4 && sub == "ROSTER")
        {
            object list = Field(page, "list"); Call(list, "Show", 4, false, null);
            string[] status = { "FLYING #2", "NEXT", "LOCAL SAR", "CAPTURED" };
            string[] callsigns = { "NIGHTJAR", "COASTAL-WATCH", "GREY GOSHAWK", "HARBOR-FOUR" };
            for (int i = 0; i < 4; i++)
            {
                object row = list.GetType().GetProperty("Item", All).GetValue(list, new object[] { i });
                Call(row, "SetIdentity", "V", Enum.Parse(TypeOf(Wing + "Domain.WingRank"), "Veteran"), callsigns[i], "Alexander Kowalski", "12 K");
                Call(row, "SetState", status[i], i < 2 ? "ready" : "warn");
            }
            object dossier = Field(page, "dossier");
            Call(dossier, "SetIdentity", "NIGHTJAR / ALEXANDER KOWALSKI");
            Call(dossier, "SetRankLine", "VETERAN / 1,840 XP / ACE AT 2,500");
            Call(dossier, "SetRecord", "12 KILLS / 8 SORTIES / 3 RESCUES");
            Call(dossier, "SetRadio", "RADIO / PROFESSIONAL"); Call(dossier, "SetStamp", "FLYING #2", "ready"); Call(dossier, "SetXp", .73f, false);
            ((AvFlow)Field(page, "rosterFlow")).RequestRelayout();
        }
        if (index == 4 && sub == "STUDIO")
        {
            object studio = Field(page, "studioPage"), draft = New(Wing + "Domain.CustomPilotRecord");
            Set(draft, "Callsign", "NIGHTJAR"); Set(draft, "Name", "Alexander Kowalski");
            Set(draft, "Background", "Coastal patrol instructor. Prefers a quiet radio and precise formation flying. Experienced in low visibility approaches and search and rescue over water.");
            Set(studio, "draft", draft); Set(studio, "draftStart", Call(draft, "Clone", (object)null)); Set(studio, "draftNew", true);
            Set(studio, "draftRevision", (int)Field(studio, "draftRevision") + 1); Call(studio, "FillFields");
            Call(studio, "RefreshPicker"); Call(studio, "RefreshStudio"); Call(studio, "RefreshRecord");
            ((AvFlow)Field(studio, "flow")).RequestRelayout();
        }
    }

    private static void SeedHardpoints(object page)
    {
        const int count = 8;
        string[] names = { "OUTER WING", "INNER WING", "CENTERLINE", "INTERNAL GUN", "SENSOR BAY", "AUXILIARY", "TAIL STATION", "SPARE STATION" };
        var precludes = new int[count][]; precludes[1] = new[] { 0 };
        object layout = New(Wing + "Domain.StationLayout", names, new string[count], new bool[count], new[] { 2, 2, 1, 1, 1, 1, 1, 1 }, precludes);
        Set(page, "layout", layout);
        Set(page, "current", New(Wing + "Domain.LoadoutTemplateRecord", "preview", "kr67", "COASTAL PATROL", new string[0]));
        var options = Array.CreateInstance(TypeOf(Wing + "Domain.WingLoadoutCatalog+StoreOption"), count);
        var facts = Array.CreateInstance(TypeOf(Wing + "Domain.StoreFacts"), count);
        var keys = (IList)Field(page, "keys"); keys.Clear();
        string[] labels = { "ARAD-121 / MARITIME STRIKE", "", "EXTERNAL FUEL / 1,500 L", "27 MM CANNON", "COASTAL SEARCH RADAR", "TACTICAL NUCLEAR STORE", "UNKNOWN SAVED STORE", "" };
        for (int i = 0; i < count; i++)
        {
            string key = i == 1 || i == 7 ? null : "preview-store-" + i; keys.Add(key);
            object option = New(Wing + "Domain.WingLoadoutCatalog+StoreOption", key, labels[i], i == 3 ? 240 : 1, 210f, 0f, 1f, false,
                i != 6, false, i == 5, false, false, Enum.Parse(TypeOf(Wing + "Domain.StoreKind"), "Other"), null, false);
            options.SetValue(option, i); facts.SetValue(option.GetType().GetProperty("Facts", All).GetValue(option), i);
        }
        Set(page, "setOptions", options); Set(page, "setFacts", facts); Set(page, "clearedSets", new bool[count]);
        object mission = New(Wing + "Domain.MissionFacts"); Set(mission, "TacticalOpen", false); Set(page, "mission", mission);
        Call(page, "RefreshHardpoints");
        // No native airframe asset is loaded in this offline fixture. Drive the production
        // list's binder with the synthetic station catalogue after exercising normal refresh.
        Call(Field(page, "hpEmpty"), "Set", 0, "");
        ((AvList)Field(page, "hpList")).SetCount(count);
        IDictionary bound = (IDictionary)Field(page, "stationRows");
        Check(bound.Count == count, "LOADOUT must bind every synthetic hardpoint row");
        string rowsText = "";
        foreach (AvRow row in bound.Keys)
        {
            Check(row.Shown, "LOADOUT populated hardpoint must be shown");
            rowsText += ((TMP_Text)Field(row, "sub")).text + "\n";
        }
        foreach (string state in new[] { "MARITIME STRIKE", "BLOCKED BY", "NOT YET", "UNKNOWN STORE", "EMPTY" })
            Check(rowsText.Contains(state), "LOADOUT populated hardpoints must exercise " + state);
    }

    private static void SetGrid(object grid, string[] values)
    {
        PropertyInfo item = grid.GetType().GetProperty("Item", All);
        for (int i = 0; i < values.Length; i++) ((AvControl)item.GetValue(grid, new object[] { i })).Label = values[i];
    }

    private static void RenderRadio(float height, ConfigFile config)
    {
        var canvas = NewCanvas("RAD", height);
        var console = AvConsole.Build((RectTransform)canvas.transform, "RAD", "RADIO", 2, AvTokens.PanelWidth, height);
        console.Tabs((AvIcon.Radio, "RECEIVER"), (AvIcon.Music, "MUSIC"));
        Type panel = TypeOf(Radio + "Presentation.RadioPanel");
        var audioHost = new GameObject("SyntheticRadioState");
        var manager = audioHost.AddComponent(TypeOf(Radio + "Runtime.RadioManager"));
        Set(manager, "settings", New(Radio + "Configuration.RadioSettings", config));
        object receiver = New(Radio + "Runtime.RadioProgram", manager, audioHost, "Receiver", new ManualLogSource("RadioPreview"));
        object deck = New(Radio + "Runtime.RadioProgram", manager, audioHost, "Deck", new ManualLogSource("DeckPreview"));
        Set(manager, "receiver", receiver); Set(manager, "deck", deck);
        panel.GetField("console", All).SetValue(null, console);
        panel.GetField("manager", All).SetValue(null, manager);
        CallStatic(panel, "BuildReceiver", console.Page(0));
        CallStatic(panel, "BuildDeck", console.Page(1));
        SeedRadio(manager, false);
        console.Finish();
        foreach (string state in new[] { "STANDBY", "ON-AIR", "PAUSED", "SCANNING", "DEAD-AIR", "OFF-AIR", "NO-LIBRARY" })
        {
            Set(manager, "scanning", state == "SCANNING");
            Set(manager, "offStation", state == "DEAD-AIR"); Set(manager, "offAir", state == "OFF-AIR");
            Set(receiver, "<State>k__BackingField", Enum.Parse(receiver.GetType().GetNestedType("Transport"),
                state == "ON-AIR" ? "Playing" : state == "PAUSED" ? "Paused" : "Stopped"));
            if (state == "NO-LIBRARY") SeedRadio(manager, true);
            console.SetPage(0); SettleRadio(console, panel);
            string prefix = "RAD-" + height + "-RECEIVER-" + state;
            Capture(canvas, height, prefix + ".png"); Gate(console, prefix); Bottom(canvas, console, height, prefix);
            if (state == "ON-AIR")
            {
                panel.GetField("stationPage", All).SetValue(null, 1); SettleRadio(console, panel);
                Capture(canvas, height, prefix + "-page2.png"); Gate(console, prefix + "-page2");
                panel.GetField("stationPage", All).SetValue(null, 0);
                Set(manager, "selectedChannel", 3); Set(manager, "tunedDial", CallStatic(Radio + "Runtime.RadioDial", "Fm", 94500));
                SettleRadio(console, panel);
                Capture(canvas, height, prefix + "-long-station.png"); Gate(console, prefix + "-long-station");
                Set(manager, "selectedChannel", 0); Set(manager, "tunedDial", CallStatic(Radio + "Runtime.RadioDial", "Fm", 88500));
            }
        }
        SeedRadio(manager, false);
        Set(manager, "offStation", true); Set(manager, "offAir", false); Set(manager, "scanning", false);
        Set(receiver, "<State>k__BackingField", Enum.Parse(receiver.GetType().GetNestedType("Transport"), "Stopped"));
        foreach (string band in new[] { "Air", "Mw" })
        {
            Set(manager, "tunedDial", CallStatic(Radio + "Runtime.RadioDial", band, band == "Air" ? 118025 : 1700));
            console.SetPage(0); SettleRadio(console, panel);
            string prefix = "RAD-" + height + "-RECEIVER-" + band.ToUpperInvariant();
            Capture(canvas, height, prefix + ".png"); Gate(console, prefix); Bottom(canvas, console, height, prefix);
        }
        SeedRadio(manager, false);
        foreach (string state in new[] { "PLAYING", "PAUSED", "STOPPED", "NO-TRACKS", "NO-FOLDERS" })
        {
            Set(deck, "<State>k__BackingField", Enum.Parse(deck.GetType().GetNestedType("Transport"),
                state == "PLAYING" ? "Playing" : state == "PAUSED" ? "Paused" : "Stopped"));
            if (state == "NO-TRACKS") Set(manager, "deckFolder", 1);
            if (state == "NO-FOLDERS") SeedRadio(manager, true);
            console.SetPage(1); SettleRadio(console, panel);
            string prefix = "RAD-" + height + "-MUSIC-" + state;
            Capture(canvas, height, prefix + ".png"); Gate(console, prefix); Bottom(canvas, console, height, prefix);
            if (state == "PLAYING" || state == "PAUSED" || state == "STOPPED")
                GateMusicRows(console, panel, prefix);
            if (state == "NO-TRACKS" || state == "NO-FOLDERS")
                foreach (AvRow row in (AvRow[])panel.GetField("trackRows", All).GetValue(null))
                    Check(!row.Shown, prefix + ": empty library must not show a blank track card");
            if (state == "PLAYING")
            {
                panel.GetField("trackPage", All).SetValue(null, 1); SettleRadio(console, panel);
                Capture(canvas, height, prefix + "-page2.png"); Gate(console, prefix + "-page2");
                GateMusicRows(console, panel, prefix + "-page2");
                panel.GetField("trackPage", All).SetValue(null, 0);
            }
        }
        CallStatic(panel, "Reset");
        Object.DestroyImmediate(canvas); Object.DestroyImmediate(audioHost);
    }

    private static void GateMusicRows(AvConsole console, Type panel, string prefix)
    {
        RectTransform viewport = console.Root.GetComponentInChildren<ScrollRect>().viewport;
        Rect view = viewport.rect;
        AvRow[] rows = (AvRow[])panel.GetField("trackRows", All).GetValue(null);
        var corners = new Vector3[4];
        int advertised = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            if (!rows[i].Shown) continue;
            advertised++;
            rows[i].Rect.GetWorldCorners(corners);
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            foreach (Vector3 corner in corners)
            {
                float y = viewport.InverseTransformPoint(corner).y;
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            musicBounds.Add(prefix + "\t" + i + "\t" + minY.ToString("0.0") + "\t" + maxY.ToString("0.0") + "\t" +
                view.yMin.ToString("0.0") + "\t" + view.yMax.ToString("0.0") + "\t" +
                console.Page(1).Content.rect.height.ToString("0.0") + "\t" + console.Page(1).ViewportHeight.ToString("0.0"));
            // A flow's blank bottom padding may extend beyond its viewport. Every track
            // advertised by the pager must have its entire card inside the visible body.
            Check(minY >= view.yMin - .5f && maxY <= view.yMax + .5f,
                prefix + ": advertised track " + (i + 1) + " must fit above the footer");
        }
        Check(advertised > 0, prefix + ": populated track page must show a track");
    }

    private static void SeedRadio(object manager, bool empty)
    {
        Array stationTracks = Array.CreateInstance(TypeOf(Radio + "Runtime.RadioStationTrack"), 1);
        stationTracks.SetValue(CallStatic(Radio + "Runtime.RadioStationTrack", "Local",
            New(Radio + "Runtime.RadioTrack", "Coastal watch / evening transmission", "preview.ogg", ".ogg")), 0);
        Array stations = Array.CreateInstance(TypeOf(Radio + "Runtime.RadioStation"), empty ? 0 : 8);
        Array dials = Array.CreateInstance(TypeOf(Radio + "Runtime.RadioDial"), stations.Length);
        float[] strengths = new float[stations.Length];
        string[] names = { "Boscali Republic Radio", "PALA State Radio", "Base Broadcast", "Coastal Emergency Coordination Network",
            "Island Weather Service", "Tower Approach", "Western Maritime Watch", "Home archive" };
        for (int i = 0; i < stations.Length; i++)
        {
            object dial = CallStatic(Radio + "Runtime.RadioDial", "Fm", 88500 + 2000 * i);
            stations.SetValue(New(Radio + "Runtime.RadioStation", "preview-" + i, "R" + i, names[i], null, dial, stationTracks), i);
            dials.SetValue(dial, i); strengths[i] = 1f - .1f * i;
        }
        Set(manager, "stations", stations); Set(manager, "stationDials", dials); Set(manager, "stationStrengths", strengths);
        Set(manager, "programText", "EVENING WATCH"); Set(manager, "ticker", "COASTAL WEATHER: LOW CLOUD OVER EASTERN APPROACHES");
        Set(manager, "status", "Receiver standing by"); Set(manager, "deckStatus", "Local music deck"); Set(manager, "deckFolder", 0);
        Array tracks = Array.CreateInstance(TypeOf(Radio + "Runtime.RadioTrack"), empty ? 0 : 22);
        for (int i = 0; i < tracks.Length; i++) tracks.SetValue(New(Radio + "Runtime.RadioTrack",
            i == 1 ? "A very long evening coastal transmission from the northern islands" : "Watch rotation " + (i + 1), "preview.ogg", ".ogg"), i);
        Array folders = Array.CreateInstance(TypeOf(Radio + "Runtime.RadioChannel"), empty ? 0 : 2);
        if (!empty)
        {
            folders.SetValue(New(Radio + "Runtime.RadioChannel", "Coastal watch / mission recordings", tracks), 0);
            folders.SetValue(New(Radio + "Runtime.RadioChannel", "Empty collection", Array.CreateInstance(tracks.GetType().GetElementType(), 0)), 1);
        }
        Set(manager, "localLibrary", New(Radio + "Runtime.RadioLibrary", folders, tracks.Length));
        Set(manager, "stationRevision", (int)Field(manager, "stationRevision") + 1);
    }

    private static GameObject NewCanvas(string name, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)go.transform).sizeDelta = new Vector2(AvTokens.PanelWidth, height);
        return go;
    }

    private static void RenderRecoveryOffer()
    {
        // The recovery offer's production UI builders without candidate selection or takeover actions.
        Type offer = TypeOf(Wing + "Runtime.WingTakeover");
        var canvas = NewCanvas("RecoveryOffer", 520f);
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(760f, 520f);
        var panel = AvLay.Child((RectTransform)canvas.transform, "Panel");
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.sizeDelta = new Vector2(720f, 500f); panel.anchoredPosition = Vector2.zero;
        var content = AvLay.Child(panel, "Content"); AvLay.Fill(content);
        offer.GetField("panel", All).SetValue(null, panel); offer.GetField("content", All).SetValue(null, content);
        CallStatic(Wing + "Presentation.WingUi", "Backdrop", panel);
        CallStatic(offer, "BuildHeader"); CallStatic(offer, "BuildCards"); CallStatic(offer, "BuildFooter");
        var cards = (IList)offer.GetField("cards", All).GetValue(null);
        for (int i = 0; i < cards.Count; i++)
        {
            object card = cards[i]; ((GameObject)Field(card, "Root")).SetActive(true);
            ((TMP_Text)Field(card, "Key")).text = (i + 1).ToString();
            ((TMP_Text)Field(card, "Callsign")).text = i == 0 ? "COASTAL-WATCH" : "NIGHTJAR";
            ((TMP_Text)Field(card, "Type")).text = "KR-67";
            ((TMP_Text)Field(card, "Meta")).text = "FUEL 23%     STORES 61%     RANGE 12.4 KM";
        }
        var hint = (RectTransform)offer.GetField("footerHint", All).GetValue(null);
        hint.anchoredPosition = new Vector2(24f, -457f);
        var decline = (AvControl)offer.GetField("declineButton", All).GetValue(null);
        decline.Label = "[R] NORMAL RESPAWN"; decline.Rect.anchoredPosition = new Vector2(486f, -452f);
        Canvas.ForceUpdateCanvases();
        Capture(canvas, 520f, "WING-recovery-offer.png", 760f); GateRoot(panel, "WING-recovery-offer");
        foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>())
            if (graphic is TMP_Text) Check(!graphic.raycastTarget, "WING-recovery-offer: label must not intercept card clicks");
        cards.Clear(); offer.GetField("panel", All).SetValue(null, null); offer.GetField("content", All).SetValue(null, null);
        Object.DestroyImmediate(canvas);
    }

    private static void Settle(AvConsole console)
    {
        for (int i = 0; i < 5; i++) console.Ticker.TickNow();
        foreach (AvReveal reveal in console.Root.GetComponentsInChildren<AvReveal>(true)) reveal.Finish();
        Canvas.ForceUpdateCanvases();
        foreach (ScrollRect scroll in console.Root.GetComponentsInChildren<ScrollRect>()) scroll.Rebuild(CanvasUpdate.PostLayout);
    }

    private static void SettleRadio(AvConsole console, Type panel)
    {
        Settle(console);
        // This synchronous fixture has no frame boundary for Unity's deferred Destroy.
        object scope = panel.GetField("scopePart", All).GetValue(null);
        Transform currentTicks = (Transform)Field(scope, "tickLayer"), currentMarkers = (Transform)Field(scope, "markerLayer");
        var obsolete = new List<GameObject>();
        foreach (Transform child in ((AvPart)scope).Rect)
            if ((child.name == "Ticks" && child != currentTicks) || (child.name == "Markers" && child != currentMarkers)) obsolete.Add(child.gameObject);
        foreach (GameObject child in obsolete) Object.DestroyImmediate(child);
    }

    private static void Bottom(GameObject canvas, AvConsole console, float height, string prefix)
    {
        ScrollRect scroll = console.Root.GetComponentInChildren<ScrollRect>();
        Check(scroll != null && scroll.vertical, prefix + ": vertical scroll body");
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;
        float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
        if (overflow <= 1f) return;
        scroll.verticalNormalizedPosition = 0f;
        Canvas.ForceUpdateCanvases();
        Check(Mathf.Abs(scroll.content.anchoredPosition.y - overflow) <= 2f, prefix + ": last content row must be reachable");
        Capture(canvas, height, prefix + "-bottom.png");
        scroll.verticalNormalizedPosition = 1f;
    }

    private static void Gate(AvConsole console, string where)
    {
        GateRoot(console.Root, where);
    }

    private static void GateRoot(Transform root, string where)
    {
        foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true))
            Check(selectable.navigation.mode == Navigation.Mode.None, where + ": control captures flight stick focus " + selectable.name);
        foreach (AvControl control in root.GetComponentsInChildren<AvControl>())
            if (Visible(control.transform) && !string.IsNullOrWhiteSpace(control.Label) && Field(control, "glyph") != null)
                Check(!(bool)Field(control, "iconOnly"), where + ": labelled key overlaps its centered icon: " + control.Label);
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>())
        {
            if (!Visible(text.transform) || string.IsNullOrWhiteSpace(text.text.Replace("\u200b", ""))) continue;
            text.ForceMeshUpdate();
            Bounds mesh = text.textBounds;
            measurements.Add(where + "\t" + text.text.Replace('\n', ' ').Replace('\t', ' ') + "\t" + text.fontSize.ToString("0.0") + "\t" +
                text.rectTransform.rect.width.ToString("0.0") + "\t" + text.rectTransform.rect.height.ToString("0.0") + "\t" +
                text.preferredWidth.ToString("0.0") + "\t" + text.preferredHeight.ToString("0.0") + "\t" + text.isTextOverflowing + "\t" +
                mesh.min.x.ToString("0.0") + "\t" + mesh.max.x.ToString("0.0"));
            if (text.name.StartsWith("Icon ", StringComparison.Ordinal)) continue;
            Check(text.fontSize >= 10f, where + ": text below 10 px: " + text.text);
            Check(!text.isTextOverflowing, where + ": text overflows: " + text.text + " (" + text.name + ")");
            if (!text.enableWordWrapping)
                // TMP's preferredWidth can retain the pre-autosize font measurement. The
                // generated glyph bounds reflect the font size that is actually displayed.
                Check(mesh.min.x >= text.rectTransform.rect.xMin - .5f && mesh.max.x <= text.rectTransform.rect.xMax + .5f,
                    where + ": text exceeds horizontal slot: " + text.text + " (" + text.name + ")");
            Check(!text.isTextTruncated, where + ": text is truncated: " + text.text + " (" + text.name + ")");
        }
    }

    private static bool Visible(Transform transform)
    {
        for (Transform t = transform; t != null; t = t.parent)
        {
            if (!t.gameObject.activeSelf) return false;
            Canvas canvas = t.GetComponent<Canvas>();
            if (canvas != null && !canvas.enabled) return false;
        }
        return true;
    }

    private static void Capture(GameObject canvas, float height, string path, float width = AvTokens.PanelWidth)
    {
        foreach (Image fill in canvas.GetComponentsInChildren<Image>(true)) if (fill.name == "ScanCover") fill.enabled = false;
        Canvas.ForceUpdateCanvases();
        Camera camera = OrthoCamera("Camera", height / 2f, AvStyleHost.FuiColor("ground", AvTheme.Surface));
        int pixelWidth = (int)width * 2, pixelHeight = (int)height * 2;
        CapturePng(camera, pixelWidth, pixelHeight, path);
        captureManifest.Add(path + "\t" + pixelWidth + "\t" + pixelHeight);
        Object.DestroyImmediate(camera.gameObject); captures++;
    }

    private static Type TypeOf(string name)
    {
        if (radioOnlyMode && name.StartsWith(Wing, StringComparison.Ordinal))
            throw new InvalidOperationException("Radio-only preview attempted Wing reflection: " + name);
        return Mod.GetType(name, true);
    }
    private static object New(string name, params object[] args) => Activator.CreateInstance(TypeOf(name), All, null, args, null);
    private static void Set(object owner, string name, object value)
    {
        FieldInfo field = owner.GetType().GetField(name, All);
        if (field != null) field.SetValue(owner, value);
        else owner.GetType().GetProperty(name, All).SetValue(owner, value);
    }
    private static object CallStatic(string type, string name, params object[] args) => CallStatic(TypeOf(type), name, args);
    private static object CallStatic(Type type, string name, params object[] args) => type.GetMethod(name, All).Invoke(null, args);
    private static void Check(bool condition, string message) { assertions++; if (!condition) failures.Add(message); }
}
#endif
