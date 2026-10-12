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
    private static bool radioOnlyMode, wingOnly, wingOnlyEmpty, wingHost;

    public static void Run() => RunPreview(false);
    public static void RunRadioOnly() => RunPreview(true);
    public static void RunWingOnly() { wingOnly = true; RunPreview(false); }

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
                if (wingOnly && height == 896f) { wingOnlyEmpty = true; RenderWing(height); wingOnlyEmpty = false; }
                if (wingOnly && height != 420f) { wingHost = true; RenderWing(height); wingHost = false; }
                if (!wingOnly) RenderRadio(height, config);
            }
            if (!radioOnly && !wingOnly) RenderRecoveryOffer();
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
        Set(context, "Client", !wingHost);
        Set(context, "MissionTime", 1840f);
        // Host view: a real, memberless service, so every host-only control shows as the player flying alone sees it.
        if (wingHost)
            try { Set(context, "Wing", TypeOf(Wing + "Runtime.WingService").GetProperty("Instance", All).GetValue(null) ?? Activator.CreateInstance(TypeOf(Wing + "Runtime.WingService"), true)); }
            catch (Exception error) { failures.Add("host service: " + (error.InnerException ?? error)); }
        // Six aircraft in two elements with every duty, flag and station phase the snapshot can carry.
        byte[] duty = { 0, 1, 3, 0, 2, 4 }, fuel = { 59, 219, 110, 240, 31, 255 }, ammo = { 255, 156, 0, 204, 92, 255 };
        byte[] flags = { 4, 0, 8, 1, 2 | 16, 0 }, err = { 2, 14, 41, 0, 96, 0 }, phase = { 2, 1, 0, 2, 3, 2 };
        sbyte[] closure = { 0, 12, -18, 1, 35, 0 };
        Array rows = (Array)Field(context, "Rows");
        int members = wingOnlyEmpty ? 0 : duty.Length;
        for (int i = 0; i < members; i++)
        {
            object row = Activator.CreateInstance(rows.GetType().GetElementType());
            Set(row, "Id", (uint)(100 + i)); Set(row, "Slot", (byte)i); Set(row, "Element", (byte)(i >= 3 ? 1 : 0));
            Set(row, "Duty", duty[i]); Set(row, "Fuel", fuel[i]); Set(row, "Ammo", ammo[i]); Set(row, "Flags", flags[i]);
            Set(row, "Err10", err[i]); Set(row, "Closure", closure[i]); Set(row, "Phase", phase[i]);
            rows.SetValue(row, i);
        }
        Set(context, "Count", members);
        Call(context, "Rescope");
        string[][] subs = { new[] { "ORDERS", "FORMATION", "ROUTE" }, new[] { "STANCES", "SORTIE" },
            new[] { "DISPATCH" }, new[] { "PRESETS" }, new[] { "ROSTER", "AIRCRAFT", "STUDIO" } };
        string tag = wingOnlyEmpty ? "-empty" : wingHost ? "-host" : "";
        // Callsigns, types and element tasks as a live wing would name them (the table's own offline hooks).
        string[] calls = { "NIGHTJAR", "COASTAL-WATCH", "GREY GOSHAWK", "HARBOR-FOUR", "VIKING", "SALT" };
        string[] types = { "KR-67", "KR-67", "FS-12", "FS-20", "CI-22", "T/A-30" };
        object wingTable = Field(pages[0], "table");
        Set(wingTable, "PreviewIdentity", (Func<uint, string[]>)(id => new[] { calls[(id - 100) % calls.Length], types[(id - 100) % types.Length] }));
        Set(wingTable, "PreviewTask", (Func<int, string>)(e => e == 0 ? "CAP · 12 KM · 4:20" : "ESCORT · STRIKE PACKAGE"));
        for (int i = 0; i < pages.Length; i++)
        {
            console.SetPage(i);
            foreach (string sub in subs[i])
            {
                string prefix = "WMC-" + height + "-" + i + "-" + sub + tag;
                try
                {
                    if (i == 0 || i == 1 || i == 4) Call(pages[i], "ShowSubNamed", sub);
                    Call(pages[i], "Shown", context);
                    Call(pages[i], "Refresh", context);
                    Call(pages[i], "Refresh", context);
                }
                catch (Exception error) { failures.Add(prefix + ": page threw: " + (error.InnerException ?? error)); }
                console.Footer.Set("STATUS  ·  " + (Get(pages[i], "Hint") as string ?? ""), AvState.Inert);
                Settle(console);
                Gate(console, prefix);
                Scrolled(canvas, console, height, prefix);
                if (!wingHost) continue;
                try { SeedWingDisplay(pages[i], i, sub); }
                catch (Exception error) { failures.Add(prefix + ": seed threw: " + (error.InnerException ?? error).Message); continue; }
                if (i < 3 && !(i == 0 && sub == "ROUTE")) continue;
                console.Page(i).RequestRelayout(); Settle(console);
                Gate(console, prefix + "-seeded");
                Scrolled(canvas, console, height, prefix + "-seeded");
            }
        }
        Object.DestroyImmediate(canvas);
    }

    // Every screenful of a scrolling page, top to bottom, so nothing below the fold goes unseen.
    private static void Scrolled(GameObject canvas, AvConsole console, float height, string prefix)
    {
        ScrollRect scroll = console.Root.GetComponentInChildren<ScrollRect>();
        float overflow = scroll != null && scroll.content != null && scroll.viewport != null ? scroll.content.rect.height - scroll.viewport.rect.height : 0f;
        Capture(canvas, height, prefix + ".png");
        if (overflow <= 1f) return;
        float step = scroll.viewport.rect.height - 24f;
        for (int n = 1; n * step < overflow + step - 1f && n < 8; n++)
        {
            scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(n * step / overflow);
            Canvas.ForceUpdateCanvases();
            Capture(canvas, height, prefix + "-s" + n + ".png");
        }
        scroll.verticalNormalizedPosition = 1f;
        Canvas.ForceUpdateCanvases();
    }

    private static void SeedWingDisplay(object page, int index, string sub)
    {
        if (index == 3)
        {
            Call(Field(page, "card"), "Set", "KR-67 / COASTAL PATROL", "6/8 STATIONS / 2,450 KG / 1 BLOCKED STORE", "EDITED", "warn", "warn", null, .75f);
            SeedHardpoints(page);
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

    // No native airframe asset loads offline: the production station board and store picker are bound directly.
    private static void SeedHardpoints(object page)
    {
        string[] stores = { "ARAD-121 / MARITIME STRIKE", "BLOCKED BY INNER WING", "EXTERNAL FUEL / 1,500 L", "27 MM CANNON · 240 RDS", "COASTAL SEARCH RADAR", "NOT YET · TACTICAL RELEASE", "UNKNOWN STORE", "— EMPTY —" };
        string[] mass = { "420 kg", "–", "1,280 kg", "210 kg", "95 kg", "–", "–", "–" };
        string[] state = { "ready", "inert", "ready", "ready", "ready", "caution", "caution", "inert" };
        object board = Field(page, "hpBoard"), picker = Field(page, "picker");
        Call(Field(page, "hpEmpty"), "Set", 0, "");
        Call(board, "SetStations", stores.Length, new List<int> { 2, 2, 1, 1, 1, 1, 1, 1 });
        for (int i = 0; i < stores.Length; i++) Call(board, "Bind", i, stores[i], mass[i], state[i], i != 1 && i != 7, i != 1, "Pick the store.");
        Call(board, "SetSelected", 0); Call(board, "Rebuild");
        Call(picker, "SetHead", "STATION 1 · OUTER WING ×2", "HOLDS ARAD-121");
        Call(picker, "SetBody", (object)null);
        string[] names = { "ARAD-121", "AAM-29 SCYTHE", "AGM-48 HARPOON-L", "EXTERNAL FUEL 1,500 L", "PAB-250 GLIDE BOMB", "TACTICAL NUCLEAR STORE", "ECM POD", "— EMPTY —" };
        string[] verdict = { "ANTI-RADIATION · 2 RDS", "AIR-TO-AIR · 4 RDS", "ANTI-SHIP · 2 RDS", "FUEL", "GLIDE BOMB · 4 RDS", "NOT YET · TACTICAL RELEASE", "RANK · ACE", "CLEAR THE STATION" };
        int shown = Math.Min(names.Length, (int)picker.GetType().GetField("PerPage", All).GetValue(null));
        Call(picker, "SetCount", shown);
        for (int i = 0; i < shown; i++) Call(picker, "Bind", i, names[i], verdict[i], i == 7 ? "0 kg" : (180 + i * 115) + " kg", i < 5 ? "ok" : "warn", i == 0, i != 6, "Fit the store.");
        Call(picker, "SetPaging", 0, names.Length);
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
