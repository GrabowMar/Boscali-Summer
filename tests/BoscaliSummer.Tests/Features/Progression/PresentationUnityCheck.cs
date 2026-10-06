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
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

/// <summary>Production presentation builders with deterministic display fixtures; no game session.</summary>
public static class PresentationUnityCheck
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly Mod = typeof(AvConsole).Assembly;
    private static int assertions;
    private static int captures;

    public static void Run() => Execute(false, false);

    public static void RunEventAlertOnly() => Execute(true, false);

    public static void RunSqdOnly() => Execute(false, true);

    public static void RunAceHuntOnly() => Execute(false, true, true);

    private static void Execute(bool eventAlertOnly, bool sqdOnly, bool aceHuntOnly = false)
    {
        try
        {
            if (Shader.Find("TextMeshPro/Distance Field") == null)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                AssetDatabase.importPackageCompleted += _ =>
                    EditorApplication.delayCall += () => Execute(eventAlertOnly, sqdOnly, aceHuntOnly);
                AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath,
                    "Package Resources/TMP Essential Resources.unitypackage"), false);
                return;
            }

            SetExecutablePath("PresentationPreview.exe");
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvBundle.ResetForTests();
            AvBundle.Load(Debug.Log);
            Check(AvIcons.Available, "production icon atlas must load before rendering");
            Shader.SetGlobalFloat("_NOA_Now", 1e6f);
            AvFxDriver.Configure(AvBundle.Available ? AvFxTier.Full : AvFxTier.Off, false);
            AvStyleHost.SetTheme(AvThemeId.Steel);
            new GameObject("Events", typeof(EventSystem));
            if (!eventAlertOnly && !aceHuntOnly)
            {
                foreach (float height in new[] { 420f, 596f, 896f })
                {
                    RenderSqd(height);
                    // The EVN field archive has its own harness (Features/Events/Run-EventsUnityCheck.ps1).
                }
            }
            if (!eventAlertOnly) RenderAceHunt();
            if (!sqdOnly) RenderEventAlert();
            File.WriteAllText("result.txt", "PASS: " + captures +
                " production-builder renders; " + assertions +
                " compact scroll/readability assertions. Offline fixture text only; no live game, input, audio or networking claim.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static void RenderSqd(float height)
    {
        GameObject canvasObject = MakeCanvas("SqdPreview", height, out RectTransform root);
        AvConsole console = AvConsole.Build(root, "PILOT", "SQUADRON DOSSIER", 5, 480f, height);
        AvMetric[] metrics = console.Metrics("SCORE", "RANK", "PICKS");
        console.Tabs((AvIcon.User, "PILOT"), (AvIcon.Star, "SKILLS"), (AvIcon.Skull, "ACES"),
            (AvIcon.Pencil, "STUDIO"), (AvIcon.Plane, "PLANE"));
        metrics[0].Set("2,450", "NEXT IN 2,050", .089f, AvState.Ready);
        metrics[1].Set("3", "FLIGHT OFFICER", 0f, AvState.Ready);
        metrics[2].Set("2", "4/7 EARNED · +0", .57f, AvState.Ready);
        console.Footer.Set("Offline layout check · production builder · no live game state.");

        var panelObject = new GameObject("SqdMfdPanel");
        object panel = panelObject.AddComponent(TypeOf("BoscaliSummer.Modules.Progression.Presentation.SqdMfdPanel"));
        ((Behaviour)panel).enabled = false;
        var managerObject = new GameObject("ProgressionManager");
        object manager = managerObject.AddComponent(TypeOf("BoscaliSummer.Modules.Progression.Runtime.ProgressionManager"));
        ((Behaviour)manager).enabled = false;
        object settings = NewSettings("BoscaliSummer.Modules.Progression.Configuration.ProgressionSettings",
            "progression-fixture.cfg");
        Call(manager, "Configure", settings, null, null, null);
        Set(manager, "localRank", 3);
        Set(manager, "localScore", 2450);
        Set(manager, "localEarnedPoints", 0);
        Set(manager, "localMaximumPoints", 7);
        Set(manager, "localScorePerPoint", 750);
        Set(panel, "progression", manager);
        Set(panel, "settings", settings);
        Set(panel, "console", console);

        string[] methods = { "BuildPilotPage", "BuildSkillsPage", "BuildWingsPage", "BuildStudioPage", "BuildPlanePage" };
        string[] names = { "pilot", "skills", "wings", "studio", "plane" };
        for (int i = 0; i < methods.Length; i++) Call(panel, methods[i], console.Page(i));
        console.Finish();
        // Content binds AFTER Finish and the page re-lays through the ticker, exactly as in the game.
        Settle(console);

        // Hover help survives the conversion: skills confirm (1), studio buttons, rows and fields, plane pager (1).
        int helpTips = root.GetComponentsInChildren<AvHelpTip>(true).Length;
        Check(helpTips >= 14, "SQD pages must carry their hover help, found " + helpTips);

        var portrait = FixtureSprite(72, 90, new Color(.10f, .18f, .16f), new Color(.02f, .05f, .05f));
        var crest = FixtureSprite(64, 64, new Color(.16f, .12f, .05f), new Color(.05f, .04f, .02f));
        object perksEmpty = Perks(manager, 0u, 0);

        // ---- PILOT -----------------------------------------------------------------------
        console.SetPage(0);
        SeedPilot(panel, false, portrait, crest);
        Call(panel, "RefreshCommittedSkills", perksEmpty);
        Gate(console, root, "SQD pilot empty");
        CaptureConsole(canvasObject, height, "sqd-pilot-empty-" + height);
        object perksHeld = Perks(manager, HeldMask, 6);
        SeedPilot(panel, true, portrait, crest);
        Call(panel, "RefreshCommittedSkills", perksHeld);
        Gate(console, root, "SQD pilot populated");
        CaptureConsole(canvasObject, height, "sqd-pilot-" + height);
        // Host thresholds are cumulative (750, 2250, 4500), never a score modulo.
        Perks(manager, 0u, 2);
        Call(panel, "RefreshPilotPage", false, 2450, 0);
        var nextValue = (TMP_Text)Get(Get(panel, "ringScore"), "value");
        Check(nextValue.text == "2,050", "PILOT NEXT must use the host's escalating score ladder: " + nextValue.text);
        Call(panel, "RefreshPilotPage", false, 15750, 0);
        Check(nextValue.text == "MAX", "PILOT NEXT must stop at the exhausted ladder");

        // ---- SKILLS: the real refresh path against a real manager --------------------------
        console.SetPage(1);
        Perks(manager, 0u, 1); // a fresh career: one earned pick, so every lane's tool is open
        Call(panel, "RefreshSkillsPage");
        Gate(console, root, "SQD skills empty");
        ValidateSkillNodes(panel);
        CaptureConsole(canvasObject, height, "sqd-skills-empty-" + height);
        Perks(manager, HeldMask, 6);
        Set(panel, "skillAwaitingConfirmation", (byte?)3);
        Call(panel, "RefreshSkillsPage");
        Gate(console, root, "SQD skills populated");
        ValidateSkillNodes(panel);
        CaptureConsole(canvasObject, height, "sqd-skills-" + height);
        // Real node selection, including locked and held grades: inspection must never issue an unlock.
        foreach (byte id in new byte[] { 0, 3, 12 })
        {
            ScrollRect skillScroll = console.Page(1).Content.GetComponentInParent<ScrollRect>();
            skillScroll.verticalNormalizedPosition = 0f;
            Call(panel, "SelectSkill", id);
            Settle(console);
            Check((byte?)Get(panel, "skillAwaitingConfirmation") == id, "any grade must be inspectable");
            Check(skillScroll.content.rect.height <= skillScroll.viewport.rect.height + .5f ||
                skillScroll.verticalNormalizedPosition > .99f,
                "grade selection must bring its detail into view");
            AvControl unlock = (AvControl)Get(panel, "skillConfirmButton");
            Check(unlock.Interactable == (id == 3), "held or capped grade inspection must keep unlock disabled");
            Gate(console, root, "SQD skill inspection " + id);
            CaptureConsole(canvasObject, height, "sqd-skills-inspect-" + id + "-" + height);
        }
        Set(panel, "skillAwaitingConfirmation", null);

        // ---- ACES ------------------------------------------------------------------------
        console.SetPage(2);
        Call(panel, "RefreshWingsPage");
        Gate(console, root, "SQD aces empty");
        CaptureConsole(canvasObject, height, "sqd-wings-empty-" + height);
        SeedWings(panel, portrait, crest);
        Gate(console, root, "SQD aces populated");
        CaptureConsole(canvasObject, height, "sqd-wings-" + height);

        // ---- STUDIO ----------------------------------------------------------------------
        console.SetPage(3);
        Call(panel, "RefreshStudioPage");
        Gate(console, root, "SQD studio empty");
        CaptureConsole(canvasObject, height, "sqd-studio-empty-" + height);
        SeedStudio(panel, portrait, crest);
        Gate(console, root, "SQD studio populated");
        CaptureConsole(canvasObject, height, "sqd-studio-" + height);

        // ---- PLANE -----------------------------------------------------------------------
        console.SetPage(4);
        Call(panel, "RefreshPlanePage");
        Gate(console, root, "SQD plane empty");
        CaptureConsole(canvasObject, height, "sqd-plane-empty-" + height);
        SeedPlane(panel);
        Gate(console, root, "SQD plane populated");
        CaptureConsole(canvasObject, height, "sqd-plane-populated-" + height);

        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(panelObject);
        Object.DestroyImmediate(managerObject);
    }

    // Strike tool + two grades, Recon tool: two tools held, so SIGNALS and ENGINEER close.
    private const uint HeldMask = (1u << 0) | (1u << 1) | (1u << 2) | (1u << 6);

    /// <summary>Sets the manager's career (held grades, earned picks) and returns its real PerkView list.</summary>
    private static object Perks(object manager, uint mask, int earned)
    {
        Set(manager, "localState", Activator.CreateInstance(
            TypeOf("BoscaliSummer.Modules.Progression.Runtime.PerkState"), mask));
        Set(manager, "localEarnedPoints", earned);
        Type view = TypeOf("BoscaliSummer.Core.Contracts.IProgressionView");
        return view.GetMethod("GetPerks").Invoke(manager, null);
    }

    private static void Settle(AvConsole console)
    {
        for (int i = 0; i < 3; i++) console.Ticker.TickNow();
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>Settle, then the readable, fit and overlap gates for the current page.</summary>
    private static void Gate(AvConsole console, RectTransform root, string name)
    {
        Settle(console);
        ValidateReadable(root, name);
        ValidateFit(console, name);
        ValidateNoTextOverlap(console, name);
    }

    private static Sprite FixtureSprite(int w, int h, Color top, Color bottom)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float t = y / (float)(h - 1);
                Color c = Color.Lerp(bottom, top, t);
                float hx = (x - w * .5f) / (w * .5f), hy = (y - h * .62f) / (h * .22f);
                if (hx * hx * 1.6f + hy * hy < 1f) c = new Color(.62f, .55f, .48f);
                float sx = (x - w * .5f) / (w * .46f), sy = (y - h * .18f) / (h * .34f);
                if (sx * sx + sy * sy < 1f) c = new Color(.24f, .30f, .27f);
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f));
    }

    private static void SeedPilot(object panel, bool populated, Sprite portrait, Sprite crest)
    {
        object id = Get(panel, "pilotIdentity");
        if (populated)
            Call(id, "Set", "LOCAL PROFILE", "DAYMAN", "M. FONTAINE", "3", "FLIGHT OFFICER", "GEN 1",
                "Awaiting aircraft", AvState.Info, portrait, crest, "BOSCALI SUMMER", AvState.Ready, "ACTIVE");
        else
            Call(id, "Set", "PILOT DOSSIER", "RECORD PENDING", "—", "0", "ROOKIE", "GEN 1",
                "NO STATUS ON FILE", AvState.Info, null, null, "NO SQUADRON NAME", AvState.Ready, "ACTIVE");

        string[] tiles = { "tileSortie", "tileTime", "tileFuel", "tileDeaths" };
        string[] full = { "1,240", "10:39", "62%", "1" }, none = { "—", "0:00", "—", "0" };
        for (int i = 0; i < tiles.Length; i++)
            Call(Get(panel, tiles[i]), "Set", populated ? full[i] : none[i],
                populated && i == 3 ? AvState.Caution : AvState.Inert);
        Call(Get(panel, "pilotBackground"), "Set", populated
            ? "Flew medical supply routes along the coast before joining the reserves. Precise on the radio, calm under pressure, and never once late for a briefing."
            : "No service background on file.");

        object sortie = Get(panel, "sortieGrid");
        string[] sortieKeys = { "sortieAirframe", "sortieCondition", "sortieLife", "sortieMission" };
        string[] sortieValues = populated
            ? new[] { "SAF-22 CHICANE", "AIRBORNE", "RESPAWNING", "18,450" }
            : new[] { "NO AIRCRAFT", "GROUND", "ONE LIFE", "0" };
        for (int i = 0; i < sortieKeys.Length; i++)
            Call(sortie, "Set", (int)Get(panel, sortieKeys[i]), sortieValues[i],
                populated && i == 1 ? AvState.Ready : AvState.Inert);
        string[] ringFields = { "ringScore", "ringBonus", "ringPicks", "ringFree" };
        string[] ringValues = populated ? new[] { "2,050", "+1", "6/7", "2" } : new[] { "750", "+0", "0/7", "0" };
        float[] ringFractions = populated ? new[] { .27f, .05f, 6f / 7f, 2f / 7f } : new float[4];
        for (int i = 0; i < ringFields.Length; i++)
            Call(Get(panel, ringFields[i]), "Set", ringFractions[i], ringValues[i],
                populated && i == 3 ? AvState.Ready : AvState.Info);
    }

    private static void SeedWings(object panel, Sprite portrait, Sprite crest)
    {
        Call(Get(panel, "huntTile"), "Set", "ACTIVE", AvState.Danger);
        object lead = Get(panel, "wingLeadRow");
        Call(lead, "Set", "DAYMAN", "M. FONTAINE   ·   FLIGHT LEAD", "SAF-22 CHICANE", "AIRBORNE", AvState.Ready);
        Call(lead, "SetThumb", portrait);
        Call(Get(panel, "friendlySection"), "SetCaption", "2 WINGMEN · 1 AIRBORNE");
        Call(Get(panel, "wingTeamNote"), "SetShown", false);
        var slots = (Array)Get(panel, "wingmanSlots");
        string[] airframes = { "SAF-22 CHICANE", "COMP-2 ULTIMATE" }, status = { "AIRBORNE", "LANDED" };
        for (int i = 0; i < slots.Length; i++)
        {
            object slot = slots.GetValue(i);
            Call(slot, "SetShown", i < 2);
            if (i < 2)
                Call(slot, "Set", "WINGMAN " + (i + 1), airframes[i], status[i], null, i == 0 ? AvState.Ready : AvState.Caution);
        }
        string[] tiles = { "wingsTotalTile", "wingsActiveTile", "wingsAliveTile" }, values = { "4", "2", "5" };
        for (int i = 0; i < tiles.Length; i++)
        {
            Call(Get(panel, tiles[i]), "SetShown", true);
            Call(Get(panel, tiles[i]), "Set", values[i], i == 0 ? AvState.Inert : AvState.Caution);
        }
        Call(Get(panel, "hostileSection"), "SetCaption", "1–2 OF 4 WINGS");
        Call(Get(panel, "hostileEmpty"), "SetShown", false);
        Call(Get(panel, "wingsPager"), "SetShown", true);
        int index = 0;
        foreach (object card in (IEnumerable)Get(panel, "wingRows"))
        {
            Call(card, "SetShown", true);
            Call(card, "Set", index == 0 ? "▲" : "◆", index == 0 ? "NIGHT LANCE" : "BLACK TIDE",
                index == 0 ? "REVENANT / VOSS" : "MARROW / KADE",
                index == 0 ? "TIER 3 · SKILL VETERAN · FIRST ENCOUNTER" : "TIER 2 · SKILL TRAINED · RETURN #1",
                index == 0 ? "HUNTING" : "PATROLLING", index == 0 ? 3 : 0, index == 0 ? 4 : 3,
                index == 0 ? "YOU" : "", index == 0 ? 3 : 2, index == 0 ? 15 : 5);
            Call(card, "SetCrest", crest);
            Call(card, "SetPortrait", portrait);
            index++;
        }
        Call(Get(panel, "wingsThreat"), "Set", new[] { .6f, .4f, .06f, .06f }, "2/4 ACTIVE", AvState.Caution);
    }

    private static void SeedStudio(object panel, Sprite portrait, Sprite crest)
    {
        Call(Get(panel, "studioLauncher"), "Set", "WING COMMAND NOT CONNECTED", "The Wing feature is not installed.");
        Call(Get(panel, "studioEmblem"), "Set", crest);
        Call(Get(panel, "studioSquadronField"), "set_Text", "BOSCALI SUMMER");
        Call(Get(panel, "studioMessageText"), "SetShown", true);
        Call(Get(panel, "studioMessageText"), "Set", "Rolled a new emblem.");
    }

    private static void SeedPlane(object panel)
    {
        Call(panel, "SetPlaneLive", true);
        Call(Get(panel, "planeIdentity"), "Set", "SAF-22 CHICANE", "AIRBORNE", AvState.Ready, .36f, .82f);
        ((TMP_Text)Get(Get(panel, "planeDamage"), "state")).text = "NATIVE PREVIEW OFFLINE";
        Call(Get(panel, "planeTuneName"), "Set", "RANGE");
        Call(Get(panel, "planeTuneState"), "Set", "RANGE: 10% less fuel draw; 85% throttle ceiling.");
        string[] flight = { "940 km/h", "870 km/h", "6,120m", "+18.2m/s", "287°", "3.4 G" };
        Array tiles = (Array)Get(panel, "planeFlight");
        for (int i = 0; i < tiles.Length; i++) Call(tiles.GetValue(i), "Set", flight[i], AvState.Inert);
        object systems = Get(panel, "planeSystems");
        string[] systemValues = { "UP", "ON", "12 READY", "2 TRACKED" };
        for (int i = 0; i < systemValues.Length; i++)
            Call(systems, "Set", i, systemValues[i], i == 3 ? AvState.Danger : AvState.Inert);
        Call(Get(panel, "planeSelected"), "Set", "AAM-10", "SELECTED STATION", "4 / 6", null, AvState.Info);
        Call(Get(panel, "planeStoresSection"), "SetCaption", "1–4 OF 8");
        Call(Get(panel, "planeStorePager"), "SetShown", true);
        string[] storeNames = { "1  AAM-10", "2  AAM-10", "3  CBU-12", "4  20MM CANNON" };
        string[] ammo = { "4 / 6", "2 / 4", "3 / 3", "320" };
        var stores = (Array)Get(panel, "planeStores");
        for (int i = 0; i < stores.Length; i++)
            Call(stores.GetValue(i), "Set", storeNames[i], null, ammo[i], i == 0 ? "SELECTED" : null,
                i == 0 ? AvState.Info : AvState.Inert);
        string[] faults = { "LEFT WINGROOT", "ENGINE RIGHT", "RUDDER", "COCKPIT" };
        string[] values = { "42%", "67%", "88%", "100%" };
        float[] fractions = { .42f, .67f, .88f, 1f };
        var faultRows = (Array)Get(panel, "planeFaults");
        for (int i = 0; i < faultRows.Length; i++)
        {
            object row = faultRows.GetValue(i);
            AvState state = i == 0 ? AvState.Danger : i == 3 ? AvState.Ready : AvState.Caution;
            Call(row, "SetShown", true);
            Call(row, "Set", faults[i], null, values[i], null, state);
            Call(row, "SetMeter", fractions[i], AvTheme.RailCaution);
        }
    }

    /// <summary>Every skill node is wide and tall enough for its name, and none overlap.</summary>
    private static void ValidateSkillNodes(object panel)
    {
        Canvas.ForceUpdateCanvases();
        var rects = new List<Vector3[]>();
        foreach (object row in (IEnumerable)Get(panel, "skillRows"))
        {
            RectTransform cell = ((AvPart)Get(row, "Node")).Rect;
            var corners = new Vector3[4];
            cell.GetWorldCorners(corners);
            Check(corners[2].x - corners[0].x >= 88f && corners[2].y - corners[0].y >= 44f,
                "SQD skill node is too small to hold a name and its state glyph.");
            // A name breaks between words, never inside one: no more lines than words.
            var label = (TMP_Text)Get(Get(row, "Node"), "label");
            label.ForceMeshUpdate();
            int words = label.text.Split(' ').Length;
            Check(label.textInfo.lineCount <= Math.Min(2, words),
                "SQD skill node name breaks inside a word or runs past two lines: " + label.text);
            foreach (Vector3[] other in rects)
                Check(corners[2].x <= other[0].x + .5f || corners[0].x >= other[2].x - .5f ||
                      corners[2].y <= other[0].y + .5f || corners[0].y >= other[2].y - .5f,
                    "SQD skill nodes overlap.");
            rects.Add(corners);
        }
    }

    /// <summary>No two rendered texts of the current page may share glyph area (the double-print bug).</summary>
    private static void ValidateNoTextOverlap(AvConsole console, string name)
    {
        var boxes = new List<KeyValuePair<TMP_Text, Rect>>();
        foreach (TMP_Text t in console.Page(console.CurrentPage).Content.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(t.text) || t.name.StartsWith("Icon")) continue;
            if (t.GetComponentInParent<TMP_InputField>() != null) continue; // the field's own zero-width caret text
            t.ForceMeshUpdate();
            Bounds b = t.textBounds;
            Vector3 a = t.transform.TransformPoint(b.min), c = t.transform.TransformPoint(b.max);
            boxes.Add(new KeyValuePair<TMP_Text, Rect>(t, Rect.MinMaxRect(
                Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y))));
        }
        var bad = new List<string>();
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                Rect x = boxes[i].Value, y = boxes[j].Value;
                float w = Mathf.Min(x.xMax, y.xMax) - Mathf.Max(x.xMin, y.xMin);
                float h = Mathf.Min(x.yMax, y.yMax) - Mathf.Max(x.yMin, y.yMin);
                if (w > 1.5f && h > 2f) bad.Add(name + ": \"" + boxes[i].Key.text + "\" overlaps \"" + boxes[j].Key.text + "\"");
            }
        Check(bad.Count == 0, "text overlap (" + bad.Count + "): " + string.Join(" | ", bad.GetRange(0, Math.Min(20, bad.Count))));
    }

    /// <summary>Kit v2 fit gate for a console page: no wrapped-off text, no text into the scroll gutter.</summary>
    private static void ValidateFit(AvConsole console, string name)
    {
        float gutterLeft = 480f - AvGridTokens.Pad - AvGridTokens.Gutter + 0.5f;
        var bad = new List<string>();
        foreach (TMP_Text t in console.Page(console.CurrentPage).Content.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.gameObject.activeInHierarchy || string.IsNullOrEmpty(t.text) || t.name.StartsWith("Icon")) continue;
            t.ForceMeshUpdate();
            Rect r = t.rectTransform.rect;
            Bounds b = t.textBounds;
            if (b.size.x > r.width + 1.5f) bad.Add(name + " width " + b.size.x + " > " + r.width + ": " + t.text);
            if (b.size.y > r.height + 1.5f) bad.Add(name + " height " + b.size.y + " > " + r.height + ": " + t.text);
            var corners = new Vector3[4];
            t.rectTransform.GetWorldCorners(corners);
            float right = console.Root.InverseTransformPoint(corners[2]).x;
            if (right > gutterLeft) bad.Add(name + " enters the scroll gutter: " + t.text);
        }
        Check(bad.Count == 0, "fit failures (" + bad.Count + "): " + string.Join(" | ", bad.GetRange(0, Math.Min(40, bad.Count))));
    }

    private static void CaptureConsole(GameObject canvas, float height, string prefix)
    {
        foreach (ScrollRect scroll in canvas.GetComponentsInChildren<ScrollRect>(true))
            if (scroll.gameObject.activeInHierarchy) scroll.verticalNormalizedPosition = 1f;
        Capture(canvas, 480f, height, prefix + "-top.png");
        if (height >= 800f)
        {
            foreach (ScrollRect scroll in canvas.GetComponentsInChildren<ScrollRect>(true))
                if (scroll.gameObject.activeInHierarchy) scroll.verticalNormalizedPosition = .5f;
            Capture(canvas, 480f, height, prefix + "-mid.png");
        }
        foreach (ScrollRect scroll in canvas.GetComponentsInChildren<ScrollRect>(true))
            if (scroll.gameObject.activeInHierarchy) scroll.verticalNormalizedPosition = 0f;
        Capture(canvas, 480f, height, prefix + "-bottom.png");
    }


    private static void RenderAceHunt()
    {
        var componentObject = new GameObject("AceHuntComponent");
        object hunt = componentObject.AddComponent(TypeOf("BoscaliSummer.Modules.Progression.Presentation.AceHuntHud"));
        ((Behaviour)hunt).enabled = false;
        Call(hunt, "Build");
        GameObject root = (GameObject)Get(hunt, "root");
        Canvas canvas = root.GetComponent<Canvas>();
        RectTransform expanded = (RectTransform)Get(hunt, "expandedPanel");
        RectTransform compact = (RectTransform)Get(hunt, "compactPanel");
        ((CanvasGroup)Get(hunt, "expandedGroup")).alpha = 1f;
        ((CanvasGroup)Get(hunt, "compactGroup")).alpha = 1f;
        Sprite portrait = FixtureSprite(72, 90, new Color(.10f, .18f, .16f), new Color(.02f, .05f, .05f));
        ((Image)Get(hunt, "portrait")).sprite = portrait;
        ((TMP_Text)Get(hunt, "portraitFallback")).gameObject.SetActive(false);
        var overflow = new List<string>();
        foreach (string state in new[] { "active", "returning", "no-abilities" })
        {
            bool returning = state == "returning";
            string handle = returning ? "WATCHKEEPER-X" : "VOSS";
            Text(hunt, "callsign", handle);
            Text(hunt, "identity", returning ? "M. Fontaine  ·  <> Distant Thunder" : "Revenant  ·  <> Night Lance");
            Text(hunt, "status", "STATUS HUNTING   ·   TARGET YOU");
            Text(hunt, "proficiency", returning ? "ELITE" : "VETERAN");
            Text(hunt, "formation", returning ? "12 / 12 ACTIVE" : "3 / 4 ACTIVE");
            Text(hunt, "returning", returning ? "RETURNING ACE  ·  ENCOUNTER 12" : "ENEMY ACE  ·  TIER 3");
            Text(hunt, "crestCaption", returning ? "DISTANT THUNDER" : "NIGHT LANCE");
            Text(hunt, "compactStatus", "ACE THREAT  ·  " + handle + "  ·  " + (returning ? "12/12" : "3/4") + " ACTIVE");
            foreach (GameObject ability in (GameObject[])Get(hunt, "abilitySlots")) ability.SetActive(state != "no-abilities");
            ((TMP_Text)Get(hunt, "noAbilities")).gameObject.SetActive(state == "no-abilities");
            foreach (bool folded in new[] { false, true })
            {
                expanded.gameObject.SetActive(!folded); compact.gameObject.SetActive(folded);
                PrepareWorldCanvas(canvas, 1920f, 1080f);
                ValidateReadable((RectTransform)root.transform, "ACE " + state);
                foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
                    Check(!graphic.raycastTarget, "The passive ace threat overlay must not take pointer input.");
                Canvas.ForceUpdateCanvases();
                foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>())
                {
                    label.ForceMeshUpdate();
                    if (label.isTextOverflowing) overflow.Add(state + ": " + label.text);
                }
                string prefix = "ace-hunt-" + state + (folded ? "-compact" : "-expanded");
                Capture(root, 1920f, 1080f, prefix + "-1080p.png");
                root.transform.localScale = Vector3.one * (720f / 1080f);
                Capture(root, 1280f, 720f, prefix + "-720p.png");
            }
        }
        Object.DestroyImmediate(root); Object.DestroyImmediate(componentObject);
        Check(overflow.Count == 0, "Ace threat text must fit:\n" + string.Join("\n", overflow));
    }

    private static void RenderEventAlert()
    {
        Type toneType = TypeOf("BoscaliSummer.Modules.Events.Presentation.EventAlertTone");
        AudioClip tone = (AudioClip)toneType.GetMethod("Clip", All).Invoke(null, null);
        var toneSamples = new float[tone.samples];
        Check(tone.GetData(toneSamples, 0), "Superevent chime samples must be readable.");
        float tonePeak = 0f;
        foreach (float sample in toneSamples) tonePeak = Mathf.Max(tonePeak, Mathf.Abs(sample));
        Check(tonePeak > .1f && tonePeak < .5f && Mathf.Abs(toneSamples[0]) < .001f &&
              Mathf.Abs(toneSamples[toneSamples.Length - 1]) < .01f,
            "Superevent chime must have headroom and quiet edges.");
        var componentObject = new GameObject("AlertComponent");
        object alert = componentObject.AddComponent(TypeOf("BoscaliSummer.Modules.Events.Presentation.SuperEventAlert"));
        ((Behaviour)alert).enabled = false;
        Call(alert, "Build");
        GameObject root = (GameObject)Get(alert, "root");
        Canvas canvas = root.GetComponent<Canvas>();
        PrepareWorldCanvas(canvas, 1920f, 1080f);
        ((CanvasGroup)Get(alert, "group")).alpha = 1f;
        Text(alert, "stamp", "SUPEREVENT  /  POLITICAL");
        Text(alert, "target", "ALL THEATER");
        Text(alert, "eyebrow", "THEATER DISPATCH  /  LIVE");
        Text(alert, "title", "CEASEFIRE ULTIMATUM");
        Text(alert, "scope", "DIRECTED TO  /  ALL THEATER");
        Text(alert, "flavor", "Diplomats have set a deadline. Both sides rush stocked materiel toward the line before the embargo, temporarily cutting support costs and dispatching convoys.");
        Text(alert, "impact", "-30% SUPPORT COST");
        Text(alert, "nextOrder", "T-0:08   FRONTLINE CONVOY REQUESTED");
        Text(alert, "clock", "ON AIR  0:24");
        Text(alert, "compactTitle", "CEASEFIRE ULTIMATUM");
        Text(alert, "compactImpact", "-30% SUPPORT COST");
        Text(alert, "compactClock", "0:12");
        var relief = new Color32(111, 219, 191, 255);
        ((TMP_Text)Get(alert, "impact")).color = relief;
        ((TMP_Text)Get(alert, "compactImpact")).color = relief;
        Type cache = TypeOf("BoscaliSummer.Modules.Events.Presentation.EventArtCache");
        Type catalog = TypeOf("BoscaliSummer.Modules.Events.Domain.EventCatalog");
        Array entries = (Array)catalog.GetField("All", All).GetValue(null);
        MethodInfo atlasTile = cache.GetMethod("AtlasTile", All);
        Texture2D sharedTexture = null;
        for (int i = 0; i < entries.Length; i++)
        {
            string key = (string)entries.GetValue(i).GetType().GetProperty("IconKey", All)
                .GetValue(entries.GetValue(i));
            Sprite tile = (Sprite)atlasTile.Invoke(null, new object[] { key });
            Check(tile != null && tile.rect.width == 192f && tile.rect.height == 108f,
                "Every event must resolve a 192x108 atlas tile.");
            if (sharedTexture == null) sharedTexture = tile.texture;
            Check(ReferenceEquals(sharedTexture, tile.texture),
                "All event tiles must share one decoded atlas texture.");
        }
        Sprite poster = (Sprite)cache.GetMethod("Get", All).Invoke(null,
            new object[] { "ceasefire_ultimatum", "tier_super" });
        Check(poster != null, "The superevent dispatch must load its embedded poster.");
        // The alert draws its art through two EventPlateArt plates (poster or category glyph).
        foreach (string plateField in new[] { "plate", "compactPlate" })
        {
            object plate = Get(alert, plateField);
            Check(plate != null, "The superevent alert must build its " + plateField + ".");
            plate.GetType().GetMethod("Bind", All).Invoke(plate, new object[] { poster, NOAvionics.AvIcon.AlertTriangle, Color.white });
        }
        TMP_Text title = (TMP_Text)Get(alert, "title");
        TMP_Text next = (TMP_Text)Get(alert, "nextOrder");
        TMP_Text flavor = (TMP_Text)Get(alert, "flavor");
        foreach (object entry in entries)
        {
            Type entryType = entry.GetType();
            if (!(bool)entryType.GetProperty("IsSuper", All).GetValue(entry)) continue;
            title.text = ((string)entryType.GetProperty("Title", All).GetValue(entry)).ToUpperInvariant();
            flavor.text = (string)entryType.GetProperty("FlavorText", All).GetValue(entry);
            Canvas.ForceUpdateCanvases();
            Check(!title.isTextOverflowing && !flavor.isTextOverflowing,
                "Superevent headline and flavor must fit: " + title.text);
        }
        title.text = "CEASEFIRE ULTIMATUM";
        flavor.text = "Diplomats have set a deadline. Both sides rush stocked materiel toward the line before the embargo, temporarily cutting support costs and dispatching convoys.";
        ValidateReadable((RectTransform)root.transform, "EVN alert");
        Check(!title.isTextOverflowing && !next.isTextOverflowing,
            "Superevent headline and next order must fit their dispatch areas.");
        Capture(root, 1920f, 1080f, "evn-alert.png");
        PrepareWorldCanvas(canvas, 1920f, 1080f);
        // The production canvas uses ScaleWithScreenSize/Expand: 720p renders the
        // 1920x1080 layout at two-thirds scale, not at an unscaled 1280-unit width.
        root.transform.localScale = Vector3.one * (720f / 1080f);
        Capture(root, 1280f, 720f, "evn-alert-720p.png");
        PrepareWorldCanvas(canvas, 1920f, 1080f);
        ((RectTransform)Get(alert, "expandedPanel")).gameObject.SetActive(false);
        ((RectTransform)Get(alert, "compactPanel")).gameObject.SetActive(true);
        ((CanvasGroup)Get(alert, "compactGroup")).alpha = 1f;
        ValidateReadable((RectTransform)root.transform, "EVN compact alert");
        Capture(root, 1920f, 1080f, "evn-alert-compact.png");
        RectTransform compact = (RectTransform)Get(alert, "compactPanel");
        AvControl[] compactControls = compact.GetComponentsInChildren<AvControl>();
        Check(compactControls.Length == 1 && compactControls[0].Help.Contains("effects remain active"),
            "The folded dispatch must keep a dismissal control with a clear effect boundary.");
        int compactHitTargets = 0;
        foreach (Graphic graphic in compact.GetComponentsInChildren<Graphic>())
        {
            if (!graphic.raycastTarget) continue;
            compactHitTargets++;
            Check(graphic.GetComponent<AvHit>() != null,
                "Folded dispatch art and text must leave pointer input to the dismissal control.");
        }
        Check(compactHitTargets == 1, "The compact dismissal must be the only folded dispatch hit target.");
        root.transform.localScale = Vector3.one * (720f / 1080f);
        Capture(root, 1280f, 720f, "evn-alert-compact-720p.png");
        Set(alert, "targetAlpha", 1f);
        compactControls[0].GetComponentInChildren<AvHit>().Click(
            new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
        Check((float)Get(alert, "targetAlpha") == 0f, "The compact dismissal must close the presentation.");
        Object.DestroyImmediate(root); Object.DestroyImmediate(componentObject);
    }

    private static GameObject MakeCanvas(string name, float height, out RectTransform root)
    {
        var canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        PrepareWorldCanvas(canvas, 480f, height);
        root = (RectTransform)canvasObject.transform;
        AvLay.Fill(AvLay.Solid(root, "Ground", AvTheme.SurfaceInert).rectTransform);
        return canvasObject;
    }

    private static void PrepareWorldCanvas(Canvas canvas, float width, float height)
    {
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform root = (RectTransform)canvas.transform;
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
        root.pivot = new Vector2(.5f, .5f);
        root.anchoredPosition3D = Vector3.zero;
        root.localScale = Vector3.one;
        root.sizeDelta = new Vector2(width, height);
    }

    private static void ValidateReadable(RectTransform root, string name)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!text.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(text.text) || text.name.StartsWith("Icon")) continue; // kit icon glyphs are not type
            text.ForceMeshUpdate();
            Check(text.fontSize >= 10f, name + " has unreadable type: " + text.text);
            Check(text.color.a >= .35f, name + " has near-invisible type: " + text.text);
        }
    }

    private static void Capture(GameObject canvas, float width, float height, string file)
    {
        foreach (Image fill in canvas.GetComponentsInChildren<Image>(true))
            if (fill.name == "ScanCover") fill.enabled = false;
        foreach (AvReveal reveal in canvas.GetComponentsInChildren<AvReveal>(true)) reveal.Finish();
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        Camera camera = OrthoCamera("Capture", height * .5f, AvTheme.SurfaceInert);
        CapturePng(camera, (int)width * 2, (int)height * 2, Path.GetFullPath(file));
        Object.DestroyImmediate(camera.gameObject);
        captures++;
    }

    private static Type TypeOf(string name) => Mod.GetType(name, true);
    private static object NewSettings(string type, string file) =>
        Activator.CreateInstance(TypeOf(type), new ConfigFile(Path.GetFullPath(file), false));
    private static object Get(object target, string field) => target.GetType().GetField(field, All)?.GetValue(target);
    private static object GetStatic(Type type, string field) => type.GetField(field, All)?.GetValue(null);
    private static void Text(object target, string field, string value)
    {
        TMP_Text label = target == null ? null : target.GetType().GetField(field, All)?.GetValue(target) as TMP_Text;
        if (label != null) label.text = value;
    }
    private static void SetEntry(object settings, string property, object value)
    {
        object entry = settings.GetType().GetProperty(property, All).GetValue(settings);
        entry.GetType().GetProperty("Value", All).SetValue(entry, value);
    }
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
}
#endif
