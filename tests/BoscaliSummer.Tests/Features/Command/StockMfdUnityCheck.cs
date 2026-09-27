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
// This verifies layout and reachability, not game adapters, authority or multiplayer.
public static class StockMfdUnityCheck
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string Owner = "BoscaliSummer.Features.Command.Presentation.MapUi.VanillaMfdRebuild";
    private static int captures;

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
            AvFont.Font = TMP_FontAsset.CreateFontAsset(new Font("C:/Windows/Fonts/consola.ttf"));
            new GameObject("Events", typeof(EventSystem));
            foreach (float height in new[] { 896f, 596f, 420f })
                foreach (string name in new[] { "Map", "Target", "Faction", "Hud", "Mission" })
                    Render(name, height);
            File.WriteAllText("result.txt", "PASS: " + captures + " stock presenter page renders at 896/596/420. Production built DLL and game metadata; synthetic labels and grids, no game launch or native adapter claim. Compact content is scrollable and every page includes bottom-of-content captures.\n");
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
        Assembly assembly = typeof(AvScreen).Assembly;
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
        AvKit.Panel(root, new Rect(0f, 0f, 0f, 0f), AvTheme.Surface);
        AvKit.Stretch((RectTransform)root.GetChild(0));
        type.GetMethod("Build", All).Invoke(presenter, new object[] { root });
        AvScreen shell = (AvScreen)type.BaseType.GetField("Shell", All).GetValue(presenter);
        shell.DataBar.State.text = name.ToUpperInvariant() + " DISPLAY";
        shell.DataBar.SetChip(0, "PREVIEW", false);
        shell.DataBar.SetChip(1, "DATA STUB", false);
        shell.DataBar.SetChip(2, "OFFLINE", false);
        Seed(presenter, name);
        for (int page = 0; page < shell.Tabs.Length; page++)
        {
            shell.SetPage(page);
            if (name == "Target")
                shell.DataBar.State.text = new[]
                {
                    "FILTERS / ACQUISITION", "ACQUIRE / CONTACTS", "PRESETS / LIBRARY",
                    "TARGETS / TRACKED", "CAMERA / SENSOR MARK"
                }[page];
            if (name == "Faction")
                shell.DataBar.State.text = "BOSCALI GENERAL AVIATION  /  " +
                    new[] { "ECONOMY", "FORCES", "LEDGER", "POLITICS" }[page];
            shell.WriteStatus(null, null, "OFFLINE LAYOUT PREVIEW · SYNTHETIC DATA");
            Canvas.ForceUpdateCanvases();
            if (shell.Body.height < 540f)
            {
                bool reachable = false;
                foreach (ScrollRect scroll in canvasObject.GetComponentsInChildren<ScrollRect>())
                    reachable |= scroll.content.rect.height >= 540f && scroll.vertical;
                if (!reachable) throw new Exception(name + " page " + page + " lacks compact scrolling.");
            }
            string prefix = name + "-" + height + "-" + page;
            Capture(canvasObject, height, prefix + ".png");
            foreach (ScrollRect scroll in canvasObject.GetComponentsInChildren<ScrollRect>())
            {
                if (scroll.content.rect.height <= scroll.viewport.rect.height + 1f) continue;
                scroll.verticalNormalizedPosition = 0f;
                Capture(canvasObject, height, prefix + "-bottom.png");
                scroll.verticalNormalizedPosition = 1f;
                break;
            }
        }
        Object.DestroyImmediate(canvasObject);
    }

    private static void Seed(object presenter, string name)
    {
        if (name == "Map")
        {
            Grid(presenter, "layers", new[] { "OBJECTIVES", "TARGET DETAILS", "JAMMING", "GRID LABELS", "PILOTS", "AIRBASES" });
            Grid(presenter, "overlays", new[] { "CONTROL FIELD", "FRONT LINE", "THREAT HEAT" });
            Grid(presenter, "hover", new[] { "OFF", "UNIT INFO", "AMMUNITION", "ORDERS" });
            Grid(presenter, "sizes", new[] { "SMALL", "MEDIUM", "LARGE" });
            Label(presenter, "overlayNote", "Sector control follows ground presence; the front is its zero contour.");
            Label(presenter, "detailSummary", "Choose the information shown when hovering a map contact.");
        }
        if (name == "Target")
        {
            Label(presenter, "filterCountReadout", "17");
            Label(presenter, "filterProfileReadout", "PROFILE / ALL");
            ((Image)presenter.GetType().GetField("filterGauge", All).GetValue(presenter)).fillAmount = .8f;
            Grid(presenter, "factionGrid", new[] { "FRIENDLY", "ENEMY" });
            Grid(presenter, "unitGrid", new[] { "AIRCRAFT", "MISSILES", "GROUND", "BUILDINGS", "SHIPS" });
            Grid(presenter, "vehicleGrid", new[] { "TRUCK", "UGV", "LCV", "AFV", "MBT", "ART", "AAA", "IR SAM", "R SAM", "RADAR" });
            Grid(presenter, "selectedGrid", new[] { "DARKREACH 21", "REVETMENT EAST AIRBASE", "TANK COMPANY NORTH" });
            Grid(presenter, "quickGrid", new[] { "AIR DEFENCE", "HOSTILE GROUND", "EMPTY" });
            Grid(presenter, "presetGrid", new[] { "ALL", "AIR DEFENCE", "HOSTILE GROUND", "NAVAL", "AIRCRAFT", "CUSTOM" });
        }
        if (name == "Faction")
        {
            Grid(presenter, "definitionGrid", new[] { "REVETMENT", "DARKREACH", "CHICANE", "CRICKET" });
            Grid(presenter, "infoGrid", new[] { "AIRBASE NORTH RIDGE", "AIRBASE SECTOR TWO", "HIGHWAY STRIP" });
            Label(presenter, "factionName", "Boscali Defence Force");
            Label(presenter, "factionSubtitle", "FACTION ORDER OF BATTLE");
            Label(presenter, "economyHeadline", "FRONTLINE OVERSTRETCH");
            Label(presenter, "economyEffect", "LOCAL EVENT / +35% SUPPORT COST • 03:42 LEFT");
            Label(presenter, "economyContract", "FIELD CONTRACT / $1,800   +   150 XP");
            Label(presenter, "mandateHeadline", "WAR WEARY");
            ((TMP_Text)presenter.GetType().GetField("mandateHeadline", All).GetValue(presenter)).color = AvTheme.Warning;
            ((Image)presenter.GetType().GetField("mandateRail", All).GetValue(presenter)).color = AvTheme.Warning;
            Label(presenter, "mandateEffect", "MORALE 42/100  •  NEW CONTRACTS -3% MONEY / XP\nACTIVE DIRECTIVE / HOLD NORTHERN AIRBASE");
            Label(presenter, "politicalEvent", "FRONTLINE OVERSTRETCH");
            Label(presenter, "politicalEffect", "LOCAL EVENT • +35% SUPPORT COST • 03:42 LEFT");
            Label(presenter, "politicalBrief", "LEADING SIDE • NEXT ORDER 00:45 / REAR DEPOTS DRAINED");
            Label(presenter, "politicalMission", "ACTIVE / HOLD NORTHERN AIRBASE");
            Label(presenter, "politicalMissionDetail", "$1,800   +   150 XP  •  SUCCESS +3 MORALE");
            Array metrics = (Array)presenter.GetType().GetField("resourceMetrics", All).GetValue(presenter);
            string[] figures = { "24,600", "4", "138", "42" };
            string[] captions = { "AVAILABLE FUNDS", "STOCKPILE", "IN ACTIVE ASSETS", "NEW CONTRACTS 0.97x" };
            for (int i = 0; i < metrics.Length; i++)
            {
                object metric = metrics.GetValue(i);
                metric.GetType().GetMethod("Set").Invoke(metric,
                    new object[] { figures[i], captions[i], i == 3 ? 0.42f : 0f,
                        i == 3 ? AvTheme.Warning : AvTheme.Accent });
            }
            ((TMP_Text)metrics.GetValue(3).GetType().GetField("Value").GetValue(metrics.GetValue(3))).color = AvTheme.Warning;
            ((Image)presenter.GetType().GetField("moraleRail", All).GetValue(presenter)).color = AvTheme.Warning;
        }
    }

    private static void Label(object owner, string field, string value)
    {
        if (owner.GetType().GetField(field, All)?.GetValue(owner) is TMP_Text text) text.text = value;
    }

    private static void Grid(object owner, string field, string[] labels)
    {
        object grid = owner.GetType().GetField(field, All)?.GetValue(owner);
        if (grid == null) return;
        bool exclusive = (bool)grid.GetType().GetField("exclusive", All).GetValue(grid);
        grid.GetType().GetMethod("SetData", All).Invoke(grid, new object[]
        {
            labels.Length, (Func<int, string>)(i => labels[i]), (Func<int, bool>)(i => exclusive ? i == 0 : i % 3 != 2),
            (Action<int>)(_ => { }), null, null, null, null
        });
    }

    private static void Capture(GameObject canvas, float height, string path)
    {
        Canvas.ForceUpdateCanvases();
        foreach (ScrollRect scroll in canvas.GetComponentsInChildren<ScrollRect>())
            scroll.Rebuild(CanvasUpdate.PostLayout);
        foreach (TMP_Text label in canvas.GetComponentsInChildren<TMP_Text>())
        {
            label.ForceMeshUpdate();
            if (label.transform.parent.name == "GridCell" && label.isTextOverflowing)
                throw new Exception(path + ": filter cell overflow: " + label.text);
        }
        var cameraObject = new GameObject("Camera", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = height / 2f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = AvTheme.Surface;
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
    }
}
#endif
