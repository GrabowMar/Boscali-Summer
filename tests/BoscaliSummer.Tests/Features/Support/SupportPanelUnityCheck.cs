#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Presentation;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Current production CALLS builder and painter with pure model fixtures. Verifies both supported
/// panel heights, row facts, favourites, banner, text and layout. No game, input or network simulation.
/// </summary>
public static partial class SupportPanelUnityCheck
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string[] States = { "fresh", "armed", "refused", "pending", "offline", "mixed" };
    private static readonly List<string> Failures = new List<string>();
    private static int assertions, gatedTexts;
    private static bool engineError;

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
            Application.logMessageReceived += (message, _, type) => {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) engineError = true;
                if (type == LogType.Warning && message.Contains("font asset") && message.Contains("was not found"))
                    Fail("Shipped font or fallback is missing a displayed glyph: " + message);
            };
            MethodInfo setPaths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            ParameterInfo[] parameters = setPaths.GetParameters();
            var arguments = new object[parameters.Length];
            arguments[0] = Path.GetFullPath("SupportPreview.exe");
            for (int i = 1; i < arguments.Length; i++) arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            setPaths.Invoke(null, arguments);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvBundle.ResetForTests();
            Check(File.Exists("avionics-ui.bundle"), "The layout check needs the actual shipped font/icon bundle.");
            AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            AvFxDriver.Configure(AvFxTier.Full, false);
            Shader.SetGlobalFloat("_NOA_Now", 1e6f);
            foreach (float height in new[] { 596f, 896f })
                foreach (string state in States) Render(state, height);
            Check(!engineError, "Unity reported an engine error while painting CALLS.");
            AvBundle.ResetForTests();
            if (Failures.Count > 0) throw new Exception(Failures.Count + " visual gate failures:\n" + string.Join("\n", Failures.GetRange(0, Math.Min(80, Failures.Count))));
            File.WriteAllText("result.txt", "PASS: 12 current CALLS layouts, production builder/painter with shipped bundle faces; " +
                assertions + " row/banner/favourite/geometry/readability assertions over " + gatedTexts +
                " gated texts. Pure fixture data; live input, request delivery and state replication remain untested.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static void Render(string state, float height)
    {
        var canvasObject = new GameObject("CallsPreview", typeof(RectTransform), typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var root = (RectTransform)canvasObject.transform;
        root.sizeDelta = new Vector2(AvTokens.PanelWidth, height);
        var controllerObject = new GameObject("CallsController");
        var controller = controllerObject.AddComponent<CallsController>();
        controller.enabled = false;
        SupportActionId[] favourites = { SupportActionId.Recon, SupportActionId.Prsm, SupportActionId.JtacMark, SupportActionId.Cruise };
        for (int i = 0; i < favourites.Length; i++) controller.Favourites[i] = favourites[i];
        var panel = canvasObject.AddComponent<CallsPanel>();
        panel.enabled = false;
        typeof(CallsPanel).GetField("calls", Hidden).SetValue(panel, controller);
        panel.BuildForHarness(root, height);

        var tiles = new List<CallTile>();
        foreach (CallRow row in CallSheet.Rows)
        {
            bool armed = state == "armed" && row.Id == SupportActionId.Prsm;
            bool offline = state == "offline";
            bool unlocked = row.Tier == CallTier.Light || state != "fresh";
            float balance = state == "fresh" ? 75f : 600f;
            float cooldown = 0f;
            bool pending = false;
            if (state == "mixed")
            {
                int variant = tiles.Count % 7;
                offline = variant == 0;
                pending = variant == 1;
                armed = variant == 2;
                unlocked = variant != 3;
                cooldown = variant == 4 ? 12.2f : 0f;
                balance = variant == 5 ? 0f : 600f;
            }
            var price = state == "fresh" ? new PriceInputs(0.5f, 10, false, null, false, 1f, 1f, 1f)
                : new PriceInputs(0f, 10, true, "UPLINK DOWN", false, 1f, 1f, 1f);
            string unlock = state == "fresh" ? CallFloors.NextUnlock(0, 10, 0f, 1f) : "HOLD 1 MORE → STRATEGIC";
            tiles.Add(CallsView.Tile(row, CallPricing.Quote(row.Tier, price), unlocked, unlock,
                balance, cooldown, armed, pending, offline));
        }
        string words = state == "refused" ? CallWords.Refusal(CallRefusal.NoAim)
            : state == "armed" ? "ARMED · PRSM · PRESS AGAIN OR RIGHT-CLICK MAP"
            : state == "pending" ? "PENDING · CRUISE SALVO" : "";
        panel.Paint(tiles, state == "fresh" ? "75 CR" : "600 CR", state == "fresh" ? "HOLD A BASE → HEAVY" : "", words, state == "pending");
        AvConsole shell = Read<AvConsole>(panel, "shell");
        for (int i = 0; i < 8; i++) shell.Ticker.TickNow();
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        string where = state + " " + height;
        Capture(height, "calls-" + state + "-" + height + ".png");
        GateConsole(shell, where);
        CheckFacts(panel, controller, tiles, words, where);

        var rows = Read<Dictionary<SupportActionId, CallLine>>(panel, "rows");
        RectTransform viewport = shell.Root.GetComponentInChildren<ScrollRect>().viewport;
        foreach (CallLine line in rows.Values)
        {
            Rect rect = InSpace(viewport, line.Rect);
            Check(rect.yMax <= viewport.rect.yMax + 1f && rect.yMin >= viewport.rect.yMin - 1f,
                where + ": every CALL row must fit inside the page.");
        }
        if (height == 896f)
        {
            CallLine last = rows[CallSheet.Rows[CallSheet.Rows.Count - 1].Id];
            float empty = InSpace(viewport, last.Rect).yMin - viewport.rect.yMin;
            Check(empty <= viewport.rect.height * 0.12f, "896 CALLS page must not leave a bottom band over 12 percent.");
        }
        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(controllerObject);
    }

    private static void CheckFacts(CallsPanel panel, CallsController calls, IReadOnlyList<CallTile> tiles, string words, string where)
    {
        var rows = Read<Dictionary<SupportActionId, CallLine>>(panel, "rows");
        Check(rows.Count == CallSheet.Rows.Count, where + ": exactly one row per current CALL.");
        foreach (CallTile tile in tiles)
        {
            CallLine line = rows[tile.Id];
            Check(Read<TMP_Text>(line, "name").text == tile.Label && Read<TMP_Text>(line, "tier").text == tile.TierWord,
                where + ": CALL label and tier come from the pure view.");
            Check(Read<TMP_Text>(line, "cost").text == tile.CostText && Read<TMP_Text>(line, "state").text == tile.StateWord,
                where + ": CALL price and readiness come from the pure view.");
            Check(line.Armed == (tile.State == CallState.Armed) && line.Dim == !tile.Enabled,
                where + ": row armed and dim state follow the pure view.");
            TMP_Text reason = Read<TMP_Text>(line, "reason");
            Check(reason.text == tile.Reason, where + ": one reason chip comes from the pure quote.");
            if (reason.gameObject.activeSelf) Check(reason.text.Length > 0, where + ": no empty reason chip.");
        }
        AvControl[] favourites = Read<AvControl[]>(panel, "favourites");
        for (int i = 0; i < favourites.Length; i++)
            foreach (CallTile tile in tiles)
                if (tile.Id == calls.Favourites[i])
                    Check(favourites[i].Label == (i + 1) + " · " + tile.Label && favourites[i].Interactable == tile.Enabled &&
                        favourites[i].Armed == (tile.State == CallState.Armed), where + ": favourite mirrors its CALL.");
        BriefCard banner = Read<BriefCard>(panel, "banner");
        Check(Read<TMP_Text>(banner, "body").text == (words.Length == 0 ? "HOTLINE OPEN · PRESS A CALL TO ARM" : words),
            where + ": outcome words have one home in the banner.");
        bool armed = false;
        foreach (CallTile tile in tiles) armed |= tile.State == CallState.Armed;
        Check(banner.Control.Rect.gameObject.activeSelf == armed, where + ": ABORT appears only while armed.");
    }

    private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    private static void Fail(string message) { if (!Failures.Contains(message)) Failures.Add(message); }

    private static void Capture(float height, string file)
    {
        var cameraObject = new GameObject("Capture", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = height * .5f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.backgroundColor = AvStyleHost.FuiColor("ground", Color.black);
        camera.clearFlags = CameraClearFlags.SolidColor;
        var target = new RenderTexture((int)AvTokens.PanelWidth * 2, (int)height * 2, 24);
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
