#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Presentation;
using BoscaliSummer.Modules.Support.Runtime;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Current production C2 CAP builder and painter with pure model fixtures. Verifies both supported
/// panel heights, row facts, favourites, footer, text and layout. No game, input or network simulation.
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
        var view = new CapView();
        view.Tiles.AddRange(tiles);
        view.Credit = state == "fresh" ? 75 : 600;
        view.NextUnlock = state == "fresh" ? "HOLD A BASE → HEAVY" : "";
        view.Words = words;
        view.Pending = state == "pending";
        for (int i = 0; i < view.Favourites.Length; i++) view.Favourites[i] = controller.Favourites[i];
        view.Console = new BoscaliSummer.Modules.Support.Domain.C2.C2Console();
        view.Console.Add("LEDGER +9 CR · 600 CR", BoscaliSummer.Modules.Support.Domain.C2.C2Tone.Info, 1f);
        view.Faction = "Boscali";
        view.Callsign = "VIPER-2";
        view.Session = "36-91-E5";
        view.KeyRot = "4:12";
        view.Uplinks = "3/4";
        view.UplinkTone = AvState.Caution;
        view.Space = "DEGRADED";
        panel.Paint(view);
        for (int i = 0; i < 8; i++) panel.Ticker.TickNow();
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        string where = state + " " + height;
        Capture(height, "calls-" + state + "-" + height + ".png");
        GateConsole(panel.ConsoleRoot, where);
        CheckFacts(panel, controller, tiles, words, where);

        var cap = Read<object>(panel, "cap");
        var rows = Read<Dictionary<SupportActionId, C2Row>>(cap, "rows");
        foreach (C2Row row in rows.Values)
        {
            Rect rect = InSpace(panel.ConsoleRoot, row.Rect);
            Check(rect.yMin >= panel.ConsoleRoot.rect.yMin + C2Footer.Height - 1f && rect.yMax <= panel.ConsoleRoot.rect.yMax - C2Chrome.Height + 1f,
                where + ": every CALL row must sit between the chrome and the footer.");
        }
        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(controllerObject);
    }

    private static void CheckFacts(CallsPanel panel, CallsController calls, IReadOnlyList<CallTile> tiles, string words, string where)
    {
        var cap = Read<object>(panel, "cap");
        var rows = Read<Dictionary<SupportActionId, C2Row>>(cap, "rows");
        Check(rows.Count == CallSheet.Rows.Count, where + ": exactly one row per current CALL.");
        foreach (CallTile tile in tiles)
        {
            C2Row row = rows[tile.Id];
            Check(Read<TMP_Text>(row, "nameText").text == tile.Label, where + ": CALL label comes from the pure view.");
            Check(Read<TMP_Text>(row, "priceText").text == tile.CostText, where + ": CALL price comes from the pure view.");
            Check(Read<TMP_Text>(row, "stateText").text == C2Cap.StateWord(tile), where + ": CALL readiness comes from the pure view.");
            Check(row.Primary.Interactable == tile.Enabled, where + ": the primary button follows the tile's enabled state.");
            Check(row.Armed == (tile.State == CallState.Armed), where + ": row armed state follows the pure view.");
            TMP_Text chip = Read<TMP_Text>(row, "chipText");
            if (chip.gameObject.activeSelf) Check(chip.text == tile.Reason && chip.text.Length > 0, where + ": the reason chip comes from the pure quote.");
        }
        AvControl[] favourites = Read<AvControl[]>(cap, "favourites");
        for (int i = 0; i < favourites.Length; i++)
            foreach (CallTile tile in tiles)
                if (tile.Id == calls.Favourites[i])
                    Check(favourites[i].Label == (i + 1) + " · " + tile.Label && favourites[i].Interactable == tile.Enabled &&
                        favourites[i].Armed == (tile.State == CallState.Armed), where + ": favourite mirrors its CALL.");
        bool armed = false;
        foreach (CallTile tile in tiles) armed |= tile.State == CallState.Armed;
        C2Footer footer = Read<C2Footer>(panel, "footer");
        Check(Read<TMP_Text>(footer, "words").text.StartsWith(C2Cap.FooterWords(words, "").Substring(0, Mathf.Min(12, C2Cap.FooterWords(words, "").Length)), StringComparison.Ordinal),
            where + ": outcome words have one home in the footer.");
        Check(Read<AvControl>(cap, "abort").Rect.gameObject.activeSelf == armed, where + ": ABORT appears only while armed.");
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
