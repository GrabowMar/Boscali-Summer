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
    private static readonly string[] Pages = { "Platform", "Planner", "Enemy", "Network", "Architect", "Threat", "CyberOps", "SpecOps", "Intel" };
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
            RenderOverlay("PlatformUplink");
            RenderOverlay("CyberConsole");
            File.WriteAllText("result.txt", "PASS: 27 real OPS page layouts, top and bottom renders; " + assertions +
                " geometry/readability assertions; both full-screen overlay builders. Production DLL builders with offline fixture text; no live game, state replication or input integration claimed.");
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
        AvScreen shell = AvScreen.Build(root, "OPS", new[] { "SPACE", "CYBER", "SPEC OPS", "INTEL" },
            new[] { new[] { "ALLOCATION", "" }, new[] { "ORBIT", "MOD" }, new[] { "CYBER", "NET" }, new[] { "RESERVE", "INT" } },
            3, 480f, height, _ => { });
        shell.DataBar.State.text = "OPERATIONS · " + page.ToUpperInvariant();
        shell.DataBar.SetChip(0, "OFFLINE QA", "info");
        shell.DataBar.SetChip(1, "FIXTURE", "inert");
        shell.DataBar.SetChip(2, "NO ORDERS", "inert");
        shell.Metrics[0].Set("8,089", "AVAILABLE", 1f, AvTheme.RailReady);
        shell.Metrics[1].Set("8/15", "STATION", .53f, AvTheme.RailInfo);
        shell.Metrics[2].Set("6/8", "INFOCON 3", .75f, AvTheme.RailCaution);
        shell.Metrics[3].Set("4", "SOF 3", .5f, AvTheme.RailInfo);
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
        overlaySupport = manager;
        int tab = page == "SpecOps" ? 2 : page == "Intel" ? 3 : Array.IndexOf(Pages, page) < 3 ? 0 : 1;
        if (tab >= 2)
        {
            Type reserve = Mod.GetType("BoscaliSummer.Features.Support.Domain.OpsReserve", true);
            Call(panel, "BuildProgramPage", tab, Enum.Parse(reserve, page == "SpecOps" ? "SpecOps" : "Intel"));
        }
        else
        {
            var pageRoot = (RectTransform)shell.CreatePage(tab, page).transform;
            string[] subLabels = tab == 0 ? new[] { "PLATFORM", "MISSION PLANNER", "ENEMY ACTIVITY" }
                : new[] { "NETWORK", "ARCHITECT", "THREATS", "OPERATIONS" };
            int sub = Array.IndexOf(Pages, page) - (tab == 0 ? 0 : 3);
            float segment = (shell.Body.width - (subLabels.Length - 1) * 4f) / subLabels.Length;
            for (int i = 0; i < subLabels.Length; i++)
                AvStyled.Button(pageRoot, new Rect(shell.Body.x + i * (segment + 4f), shell.Body.y, segment, 30f),
                    subLabels[i], "tab", () => { }, AvButtonStyle.Tab).SetLatched(i == sub);
            var subBody = new Rect(shell.Body.x, shell.Body.y - 38f, shell.Body.width, shell.Body.height - 38f);
            Call(panel, "Build" + page + "Page", pageRoot, subBody);
        }
        shell.SetPage(tab);
        Seed(panel, page);
        foreach (string rows in new[] { "catalogueRows", "facilityRows", "incidentRows", "foreignRows", "rosterRows" })
            foreach (object row in (IEnumerable)Get(panel, rows)) if (row != null) SeedRow(panel, row);
        foreach (object row in (IEnumerable)Get(panel, "actionRows")) SeedRow(panel, Get(row, "View"));
        foreach (object program in (IEnumerable)Get(panel, "programPages"))
        {
            if (program == null) continue;
            foreach (object row in (IEnumerable)Get(program, "Rows")) SeedRow(panel, row);
            var doctrine = Get(program, "DoctrineRows") as IEnumerable;
            if (doctrine != null) foreach (object row in doctrine) SeedRow(panel, row);
        }
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        if (page == "Platform")
        {
            foreach (object card in (IEnumerable)Get(panel, "cards"))
            {
                TMP_Text cost = (TMP_Text)Get(card, "Cost");
                cost.ForceMeshUpdate();
                Check(!cost.isTextTruncated, "Station ability cost must be complete: " + cost.text);
                TMP_Text state = (TMP_Text)Get(card, "Status");
                state.ForceMeshUpdate();
                Check(!state.isTextTruncated, "Station ability state must be complete.");
            }
        }
        if (page == "Architect")
        {
            Call(panel, "SelectSite", 15);
            Check((int)Get(panel, "rosterPage") == 3, "Last network slot must select roster page four.");
            Call(panel, "SelectSite", 0);
            Check((int)Get(panel, "rosterPage") == 0, "First network slot must select roster page one.");
        }
        string prefix = "ops-" + page.ToLowerInvariant() + "-" + height;
        Capture(canvasObject, height, prefix + ".png");
        if (page == "Platform")
        {
            ScrollRect abilityScroll = root.GetComponentInChildren<ScrollRect>();
            if (abilityScroll != null)
            {
                TMP_Text firstCost = null;
                foreach (object card in (IEnumerable)Get(panel, "cards")) { firstCost = (TMP_Text)Get(card, "Cost"); break; }
                if (firstCost != null)
                {
                    Vector3 local = abilityScroll.content.InverseTransformPoint(firstCost.transform.position);
                    Vector2 offset = abilityScroll.content.anchoredPosition;
                    offset.y = Mathf.Clamp(-local.y - 80f, 0f, abilityScroll.content.rect.height - abilityScroll.viewport.rect.height);
                    abilityScroll.content.anchoredPosition = offset;
                    Capture(canvasObject, height, prefix + "-abilities.png");
                }
            }
        }
        foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(true))
        {
            if (!scroll.gameObject.activeInHierarchy) continue;
            Check(scroll.content.rect.height >= scroll.viewport.rect.height, "Scroll content must cover viewport.");
            scroll.verticalNormalizedPosition = 0f;
        }
        Capture(canvasObject, height, prefix + "-bottom.png");
        if (page == "Platform")
        {
            Call(panel, "SetPlatformDetailVisibility", false);
            Text(panel, "bannerWord", "NO STATION");
            Text(panel, "bannerBand", "NO ORBIT");
            Text(panel, "bannerNote", "Design it in MISSION PLANNER: launch a core, then dock modules one at a time.");
            ((TMP_Text)Get(panel, "bannerClock")).gameObject.SetActive(false);
            ((TMP_Text)Get(panel, "bannerClockKey")).gameObject.SetActive(false);
            ((AvButton)Get(panel, "bannerPlanner")).gameObject.SetActive(true);
            Capture(canvasObject, height, prefix + "-empty.png");
        }
        // Do not call gameplay component teardown against an absent game session.
        Object.DestroyImmediate(canvasObject);
        panelObject.SetActive(false);
        managerObject.SetActive(false);
    }

    private static void Seed(object panel, string page)
    {
        Text(panel, "bannerBand", "LOW ORBIT · 500 KM · 51.6°");
        Text(panel, "bannerWord", "OVERHEAD");
        Text(panel, "bannerClockKey", "LOSS OF SIGNAL IN");
        Text(panel, "bannerClock", "01:34");
        Text(panel, "bannerNote", "PASS 12 ASC · HDG 035° · 00:26 OF 02:00 · GET 00:39:34");
        Text(panel, "cyberInfocon", "INFOCON 3");
        Text(panel, "cyberPhase", "ACTIVE · HEAT 48");
        Text(panel, "cyberBandText", "BANDWIDTH 620/800 MB · NET +12/S");
        Text(panel, "cyberNote", "Intrusion detected at GATEWAY 2. Isolate the site to stop the advance.");
        Text(panel, "campaignPhase", "ACTIVE");
        Text(panel, "campaignClock", "NEXT ~01:25");
        Text(panel, "campaignNote", "EMITTERS EXPOSED TO HOSTILE ACTOR 1 FOR 45 S · EMCON OR MOVE");
        Text(panel, "footholdNote", "NO FOOTHOLD · TRACE AN ATTACKER TO GET ONE");
        Text(panel, "launchTitle", "NEXT LAUNCH · SIGINT ARRAY");
        Text(panel, "launchPrice", "1,250");
        Text(panel, "launchProjection", "AFTER · MASS 28.0 T / 40 T · SUN +4.0 KW · DARK -2.0 KW · STORE 600 KJ");
        Text(panel, "launchStatus", "GO · 5 S COUNT · DOCKS 20 S LATER");
        Text(panel, "inspectorTitle", "B3 · CORE MODULE");
        Text(panel, "inspectorSummary", "Station command and power. Attach a module beside a connected truss cell.");
        Text(panel, "inspectorEffects", "12.0 T · +4 KW SUN · 600 KJ · HEAVY LIFT");
        Text(panel, "operationsHint", "ARM AN OPERATION, THEN RIGHT-CLICK THE MAP TO SET ITS TARGET.");
        Text(panel, "inspectorName", "GATEWAY 2 · JAMMER");
        Text(panel, "inspectorState", "OFF-NET · NO PATH TO C2");
        Text(panel, "inspectorDetail", "14.2 KM EAST · 4.0 KM NORTH · NO PATH TO CYBER COMMAND");
        Text(panel, "inspectorMore", "COVER 12 KM · STRENGTH 100% · UMBRELLA 65%");
        if (page != "Platform") return;
        ((AvButton)Get(panel, "bannerPlanner")).gameObject.SetActive(false);
        foreach (object card in (IEnumerable)Get(panel, "cards"))
        {
            ((TMP_Text)Get(card, "Status")).text = "ARMED · RIGHT-CLICK MAP OR UPLINK";
            ((TMP_Text)Get(card, "Cost")).text = Get(card, "Kind").ToString() == "Uplink" ? "LOAD 2.0KW · VIEW" : "ALLOC 12,500 · 240KJ · 120S";
        }
    }

    private static void SeedRow(object panel, object row)
    {
        var price = Get(row, "Value") as TMP_Text;
        if (price != null) price.text = "12,500";
        var primary = Get(row, "Primary") as AvButton;
        if (primary != null) primary.SetEnabled(false);
        var detail = Get(row, "Detail") as TMP_Text;
        if (detail != null) detail.text = "NEXT: improves coverage and readiness for this support role.";
        var tone = panel.GetType().GetNestedType("Tone", BindingFlags.NonPublic);
        panel.GetType().GetMethod("Paint", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
            new[] { row, Enum.Parse(tone, "Locked"), "LOCKED · UNLOCK IN SQD OR BUILD REQUIRED FACILITY" });
    }

    private static void RenderOverlay(string name)
    {
        Type type = Mod.GetType("BoscaliSummer.Features.Support.Presentation." + name, true);
        object second = name == "PlatformUplink"
            ? Activator.CreateInstance(Mod.GetType("BoscaliSummer.Features.Support.Presentation.PlatformProducts", true))
            : new[] { "00:25:00  WATCH FLOOR ONLINE · NO ACTIVE INCIDENTS", "", "", "", "", "" };
        object overlay = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { overlaySupport, second });
        var component = (Behaviour)overlay;
        component.enabled = false;
        Canvas canvas = (Canvas)Get(overlay, "canvas");
        canvas.GetComponent<CanvasScaler>().enabled = false;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.enabled = true;
        canvas.transform.position = Vector3.zero;
        canvas.transform.localScale = Vector3.one;
        canvas.transform.rotation = Quaternion.identity;
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920f, 1080f);
        ((GameObject)Get(overlay, "content")).SetActive(true);
        var bar = (AvStyled.DataBar)Get(overlay, "bar");
        bar.State.text = name == "PlatformUplink" ? "BASTION · STATION UPLINK" : "SPECTRUM DEFENCE · INFOCON 3";
        for (int i = 0; i < 4; i++) bar.SetChip(i, i == 0 ? "OFFLINE QA" : "FIXTURE", "inert");
        Text(overlay, "slate", "NO STATION ON ORBIT\nLAUNCH A CORE IN MISSION PLANNER");
        Text(overlay, "liveLabel", "NO FEED");
        Text(overlay, "productCaption", "NO RADAR PRODUCT");
        Text(overlay, "productDetail", "A radar scan forms the product here. Offline fixture: no image source.");
        Text(overlay, "boardTitle", "INFOCON 3 · WATCH");
        Text(overlay, "boardAdvice", "Select a site on the network map to inspect its state and available countermeasures.");
        Text(overlay, "siteName", "NO SITE SELECTED");
        Text(overlay, "siteState", "CLICK A SITE ON THE BOARD");
        Text(overlay, "status", "STATUS · OFFLINE PRESENTATION FIXTURE · NO ORDERS SENT");
        foreach (TMP_Text value in (TMP_Text[])Get(overlay, "values")) value.text = "—";
        string statuses = name == "PlatformUplink" ? "taskStatus" : "verbStatus";
        foreach (TMP_Text text in (TMP_Text[])Get(overlay, statuses))
        {
            text.text = name == "PlatformUplink" ? "ALLOC 12,500 · 240 KJ\nLOCKED · STATION NOT OVERHEAD" : "30 MB · LOW BANDWIDTH\nSELECT A SITE OR INCIDENT";
            text.ForceMeshUpdate();
            Check(!text.isTextTruncated, "Overlay cost and refusal must be fully readable.");
        }
        if (name == "PlatformUplink")
        {
            ((Image)Get(overlay, "liveLight")).enabled = false;
            AvButton[] buttons = (AvButton[])Get(overlay, "taskButtons");
            string[] labels = { "[1] RADAR SCAN", "[2] ELINT SWEEP", "[3] ROD FROM GOD", "[4] EMP · FRIENDLY FIRE" };
            for (int i = 0; i < buttons.Length; i++) { buttons[i].SetText(labels[i]); buttons[i].SetEnabled(false); }
            ((AvButton)Get(overlay, "deliverButton")).SetEnabled(false);
        }
        else
        {
            ((AvButton)Get(overlay, "adviceButton")).SetText("NO ACTION NEEDED");
            ((AvButton)Get(overlay, "adviceButton")).SetEnabled(false);
            foreach (AvButton button in (AvButton[])Get(overlay, "verbButtons")) button.SetEnabled(false);
            foreach (AvButton button in (AvButton[])Get(overlay, "incidentButtons"))
            {
                button.SetText("NO INCIDENT");
                button.SetEnabled(false);
            }
        }
        Capture(canvas.gameObject, 1080f, "ops-" + name.ToLowerInvariant() + ".png", 1920f);
        canvas.gameObject.SetActive(false);
    }

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
