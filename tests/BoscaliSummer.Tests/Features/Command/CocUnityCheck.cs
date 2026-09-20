#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Presentation;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Contracts;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Standalone render check for the STR "COC" page.
///
/// The chain-of-command board is Unity UI whose whole point is what a player sees: a tree,
/// a full-width personnel file that grows to its record, status cues and a staff log. None of that
/// can be exercised by the pure net8 tests. This check builds the real shell and the real
/// page from the production sources, feeds it a deterministic stubbed IHighCommandView staff
/// and writes one PNG per scenario so the page can be reviewed without launching the game.
///
/// Game/domain adapters the panel compiles against live in SettingsUnityStubs.cs. Production
/// members are reached only through reflection; no production file is modified.
/// </summary>
public static class CocUnityCheck
{
    private const float Width = AvTokens.PanelWidth;

    private static readonly List<string> Notes = new List<string>();
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

            var setPaths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var parameters = setPaths.GetParameters();
            var arguments = new object[parameters.Length];
            arguments[0] = Path.GetFullPath("CocCheck.exe");
            for (int i = 1; i < arguments.Length; i++) arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            setPaths.Invoke(null, arguments);

            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvFont.Font = TMP_FontAsset.CreateFontAsset(new Font("C:/Windows/Fonts/consola.ttf"));
            new GameObject("Events", typeof(EventSystem));

            IHighCommandView staff = Staff();

            RenderScenario(staff, 420f, 0, false, "coc-420-long.png",
                "height 420 (compact bay), ALLIED side, long-file selection, back-to-posts and collapse bounds");
            RenderScenario(staff, 596f, 0, false, "coc-596.png",
                "height 596 (AvTokens.PanelHeight), ALLIED side, dossier: GEN. D. HALVERSON (tier 0 theater commander, long bio, two-entry bonus)");
            RenderScenario(staff, 896f, 3, false, "coc-896.png",
                "height 896 (AvTokens.PanelHeightMax), ALLIED side, dossier: MAJ. T. VOSSBERG (tier 2 base commander, InTransit, very long Location, short bio)");
            RenderScenario(staff, 896f, 6, true, "coc-hostile.png",
                "height 896, HOSTILE side latched (cocShowHostile=true), dossier: COL. V. KRUPIN (known enemy, IntelAge 41s)");
            RenderScenario(staff, 596f, -1, false, "coc-nopost.png",
                "height 596, ALLIED side, no post open (cocSelectedId=-1), one compact selection prompt");
            // The card can be taller than the viewport: the longest record at full height
            // panel is the case where the viewport has to clip it and the page to scroll,
            // instead of the sheet being painted over the pinned status strip.
            RenderScenario(staff, 896f, 0, false, "coc-896-long.png",
                "height 896, ALLIED side, dossier: GEN. D. HALVERSON (longest bio and two bonus entries - the card scrolls within the viewport)");
            // No staff at all: the page must read as unavailable and must not leave a stale
            // selection bracketed on the map.
            RenderScenario(Staff(available: false), 596f, -1, false, "coc-nostaff.png",
                "height 596, no staff running (stub Available=false), dossier hidden and the map highlight cleared");
            foreach (float height in new[] { 420f, 596f, 896f })
                foreach (int page in new[] { 0, 2 })
                {
                    GameObject canvas = Build(height, staff, -1, false, page);
                    string prefix = (page == 0 ? "situation-" : "operations-") + height;
                    Capture(canvas, height, prefix + ".png");
                    ScrollRect scroll = canvas.GetComponentInChildren<ScrollRect>();
                    if (scroll != null && scroll.content.rect.height > scroll.viewport.rect.height + 1f)
                    {
                        scroll.verticalNormalizedPosition = 0f;
                        Capture(canvas, height, prefix + "-bottom.png");
                    }
                    Object.DestroyImmediate(canvas);
                }

            var report = new System.Text.StringBuilder();
            report.AppendLine("PASS: the real STR COC page (StrMfdPanel.BuildCocPage + RefreshCoc, production sources unmodified) rendered offline.");
            report.AppendLine(captures + " captures: COC roster/file scenarios plus SITUATION and OPERATIONS at 420/596/896; compact bottom reachability, file focus/back/collapse and filled-gauge sprites checked.");
            report.AppendLine("The stub IHighCommandView records Highlight(id); every scenario asserts the map highlight matches the open file (or -1 when none is open).");
            report.AppendLine("Staff stub: 8 posts - theater cmdr (tier 0), air/ground component cmdrs (tier 1), three base cmdrs (tier 2; one InTransit, one KIA, one Disrupted), one known enemy (IntelAge 41s) and one unconfirmed enemy. Portraits: synthetic sprites of mixed aspect (96x96, 80x120, 128x72, 64x64, 72x128, 100x100) and mixed pivots (centre, zero, one, top-left, bottom-right); the two unconfirmed/KIA posts keep the NO VISUAL fallback.");
            report.AppendLine("Renders (path | bytes | setup):");
            foreach (string note in Notes) report.AppendLine(note);
            report.AppendLine("Reflection used: fields shell/highCommand/settings/command, cocShowHostile, cocSelectedId; methods BuildCocPage(GameObject), Refresh(), RefreshCoc().");
            report.AppendLine("Skipped/worked around: CommandTree (HighCommand domain) unused by the contract; CommandSettings/CommandManager/TacticalSectorGrid/SectorControl/FactionHQ/UnitConverter stubbed.");
            File.WriteAllText("result.txt", report.ToString());
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    // ------------------------------------------------------------------ scenarios

    private static void RenderScenario(
        IHighCommandView staff, float height, int selectedId, bool hostile, string file, string description)
    {
        GameObject canvas = Build(height, staff, selectedId, hostile);
        string path = Capture(canvas, height, file);
        if (selectedId >= 0)
        {
            StrMfdPanel panel = canvas.GetComponentInChildren<StrMfdPanel>();
            Call(panel, "SelectCoc", selectedId);
            Call(panel, "RefreshCoc");
            Capture(canvas, height, Path.GetFileNameWithoutExtension(file) + "-file.png");
            ScrollRect scroll = canvas.GetComponentInChildren<ScrollRect>();
            Check(scroll != null && scroll.verticalNormalizedPosition < .99f,
                "Selecting a commander must bring the full-width personnel file into view.");
            foreach (AvButton button in canvas.GetComponentsInChildren<AvButton>())
                if (button.GetComponentInChildren<TMP_Text>()?.text == "BACK TO POSTS")
                {
                    button.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                    Check(scroll.verticalNormalizedPosition > .99f, "Back to posts must return to the roster.");
                }
            scroll.verticalNormalizedPosition = 0f;
            SetField(panel, "cocSelectedId", -1);
            Call(panel, "RefreshCoc");
            float limit = Mathf.Max(0f, scroll.content.rect.height - scroll.viewport.rect.height);
            Check(scroll.content.anchoredPosition.y >= -.5f && scroll.content.anchoredPosition.y <= limit + .5f,
                "Closing a long file must clamp scrolling to the shorter roster and log.");
            Call(panel, "SelectCoc", selectedId);
            Call(panel, "RefreshCoc");
        }
        Object.DestroyImmediate(canvas);

        // The page selects a post on the map: whatever file is open is the post the map
        // brackets, and a closed file clears it. This is the wiring check the render cannot show.
        var stub = (CocStaffStub)staff;
        Check(stub.HighlightedId == selectedId,
            file + ": the map highlight should be " + selectedId + " but the page asked for " +
            stub.HighlightedId);

        long bytes = new FileInfo(path).Length;
        Notes.Add(path + " | " + bytes + " bytes | highlight " + stub.HighlightedId + " | " + description);
        Debug.Log("[CocUnityCheck] " + path + " (" + bytes + " bytes)");
    }

    private static GameObject Build(float height, IHighCommandView staff, int selectedId, bool hostile, int pageIndex = 1)
    {
        var canvasObject = new GameObject("CocCanvas", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(Width, height);

        var backdropObject = new GameObject("PanelBackdrop", typeof(RectTransform), typeof(Image));
        var backdropRect = (RectTransform)backdropObject.transform;
        backdropRect.SetParent(canvasObject.transform, false);
        AvKit.Stretch(backdropRect);
        Image backdrop = backdropObject.GetComponent<Image>();
        backdrop.sprite = AvSprites.Panel;
        backdrop.type = Image.Type.Sliced;
        backdrop.color = Color.white;
        backdrop.raycastTarget = false;

        var contentObject = new GameObject("Content", typeof(RectTransform));
        var content = (RectTransform)contentObject.transform;
        content.SetParent(canvasObject.transform, false);
        AvKit.Stretch(content);

        AvScreen shell = AvScreen.Build(
            content, "STR",
            new[] { "SITUATION", "COMMAND", "OPERATIONS" },
            new[]
            {
                new[] { "THEATER CONTROL", "HELD" },
                new[] { "AIR DOMINANCE", "ALLIED" },
                new[] { "COMMAND", "STAFF" },
            },
            3, Width, height, _ => { });

        GameObject page = shell.CreatePage(pageIndex, "PreviewPage");
        shell.SetPage(pageIndex);

        var panelObject = new GameObject("StrMfdPanel");
        panelObject.transform.SetParent(canvasObject.transform, false);
        StrMfdPanel panel = panelObject.AddComponent<StrMfdPanel>();
        SetField(panel, "shell", shell);
        SetField(panel, "highCommand", staff);
        SetField(panel, "settings", new CommandSettings());
        SetField(panel, "command", new CommandManager());
        Call(panel, pageIndex == 0 ? "BuildSaPage" : pageIndex == 1 ? "BuildCocPage" : "BuildCmdPage", page);
        if (hostile) SetField(panel, "cocShowHostile", true);
        if (selectedId >= 0) SetField(panel, "cocSelectedId", selectedId);
        // Refresh() fills the shared chrome (data bar, metrics, status strip) and routes
        // through RefreshCoc(); the second call exercises the private page entry point
        // directly, exactly as the panel does on its own refresh tick.
        Call(panel, "Refresh");
        if (pageIndex == 1) Call(panel, "RefreshCoc");
        return canvasObject;
    }

    private static string Capture(GameObject canvasObject, float height, string file)
    {
        Canvas.ForceUpdateCanvases();
        foreach (ScrollRect scroll in canvasObject.GetComponentsInChildren<ScrollRect>())
            scroll.Rebuild(CanvasUpdate.PostLayout);
        foreach (Image gauge in canvasObject.GetComponentsInChildren<Image>(true))
            if (gauge.type == Image.Type.Filled)
                Check(gauge.sprite != null, "Filled gauge needs a sprite or Unity renders it permanently full.");
        foreach (TMP_Text text in canvasObject.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvasObject.transform);

        int pixelWidth = Mathf.RoundToInt(Width * 2f);
        int pixelHeight = Mathf.RoundToInt(height * 2f);

        var cameraObject = new GameObject("CocCamera", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = height * 0.5f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.04f, 0.07f, 0.06f);

        var target = new RenderTexture(pixelWidth, pixelHeight, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;

        var image = new Texture2D(pixelWidth, pixelHeight, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, pixelWidth, pixelHeight), 0, 0);
        image.Apply();
        string path = Path.GetFullPath(file);
        File.WriteAllBytes(path, image.EncodeToPNG());

        RenderTexture.active = null;
        camera.targetTexture = null;
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(cameraObject);

        var info = new FileInfo(path);
        Check(info.Exists && info.Length > 0, "render produced no bytes: " + file);
        captures++;
        return path;
    }

    // ---------------------------------------------------------------------- staff

    /// <summary>
    /// A portrait the way Wing Command hands them over: shapes differ, and so do the sprite
    /// pivots. The plate must not follow either, so the check draws a border and a diagonal
    /// wash - a crop that drifts or scales is obvious at a glance.
    /// </summary>
    private static Sprite Portrait(int seed, int width, int height, Vector2 pivot)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool border = x < 2 || y < 2 || x >= width - 2 || y >= height - 2;
                int wash = 40 + (x + y) * 170 / (width + height);
                pixels[y * width + x] = border
                    ? new Color32(235, 255, 245, 255)
                    : new Color32((byte)(wash + seed * 5), (byte)(wash + 50), (byte)(wash + 90), 255);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, width, height), pivot, 100f);
    }

    private static IHighCommandView Staff(bool available = true)
    {
        const string LongBio =
            "Born in the northern shipyards and raised on maintenance decks, Halverson flew three combat tours " +
            "before taking the theater chair. He reads a front line the way other officers read a ledger: supply " +
            "first, then the shape of the ground, and only then the aircraft. His staff say he has never once " +
            "raised his voice on the radio, which is somehow worse. He keeps a personal map of every airstrip " +
            "the faction has ever lost and marks the date of each loss in its margin.";

        var commanders = new List<CommanderView>
        {
            // 0 - theater commander, tier 0, long bio, two-entry bonus.
            new CommanderView(0, -1, 0, true, true, false, false, false, false,
                "GEN. D. HALVERSON", "GENERAL", "THEATER COMMANDER", "theater_hq_delta",
                "LOGISTICS MIND +15% STIPEND · RECLUSE -30% PATROL SIGHT", LongBio,
                0x1101, Portrait(1, 96, 96, new Vector2(0.5f, 0.5f)), -1f, 0.40f, 0f, 0f),

            // 1 - air component commander, tier 1.
            new CommanderView(1, 0, 1, true, true, false, false, false, false,
                "COL. R. MARCHETTI", "COLONEL", "AIR COMPONENT CMDR", "airbase_west_complex",
                "WING DOCTRINE +10% SORTIE READINESS",
                "Flew the first sortie of the war and has not left the ops room since.",
                0x1202, Portrait(2, 80, 120, Vector2.zero), -1f, 0.20f, 0f, 0f),

            // 2 - ground component commander, tier 1, under fire.
            new CommanderView(2, 0, 1, true, true, false, false, false, true,
                "COL. A. OKONKWO", "COLONEL", "GROUND COMPONENT CMDR", "garrison_north",
                "IRON GRIP -20% SUPPLY LOSS",
                "Holds the northern shoulder with two battalions and a longer memory.",
                0x1303, Portrait(3, 128, 72, Vector2.one), -1f, 0.20f, 0f, 0f),

            // 3 - base commander, tier 2, in transit, very long location, short bio.
            new CommanderView(3, 1, 2, true, true, false, true, false, false,
                "MAJ. T. VOSSBERG", "MAJOR", "BASE COMMANDER", "airstrip_city2_northern_annex",
                "SAPPER'S EYE +12% FORTIFICATION SPEED",
                "Kept the annex running on borrowed parts and stubbornness.",
                0x1404, Portrait(4, 64, 64, new Vector2(0.5f, 0.5f)), -1f, 0.07f, 0f, 0f),

            // 4 - base commander, tier 2, KIA.
            new CommanderView(4, 2, 2, true, true, true, false, false, false,
                "MAJ. L. FERRO", "MAJOR", "BASE COMMANDER", "depot_south_ridge",
                "HARD SCHEDULE +8% CONVOY THROUGHPUT",
                "Ran the southern depot for two years without a late delivery.",
                0x1505, null, -1f, 0.07f, 0f, 0f),

            // 5 - base commander, tier 2, succession running.
            new CommanderView(5, 1, 2, true, true, false, false, true, false,
                "CPT. M. SATO", "CAPTAIN", "BASE COMMANDER", "radar_site_9",
                "QUIET WATCH -15% PATROL SIGHT",
                "Keeps the eastern radar net lit through every raid.",
                0x1606, Portrait(6, 72, 128, new Vector2(0f, 1f)), -1f, 0.06f, 0f, 0f),

            // 6 - enemy post, known, intel 41 seconds old.
            new CommanderView(6, -1, 1, false, true, false, false, false, false,
                "COL. V. KRUPIN", "COLONEL", "AIR COMPONENT CMDR", "enemy_airbase_icaria",
                "REAPER DOCTRINE +20% KILL VALUE",
                "Commands the enemy air component from a hardened strip; his patrols arrive on schedule and leave on time.",
                0x1707, Portrait(7, 100, 100, new Vector2(1f, 0f)), 41f, 0.25f, 0f, 0f),

            // 7 - enemy post, unconfirmed.
            new CommanderView(7, 6, 2, false, false, false, false, false, false,
                "MAJ. E. ROUX", "MAJOR", "BASE COMMANDER", "unknown_post",
                "", "Local intel has not confirmed this post.",
                0x1808, null, -1f, 0.10f, 0f, 0f),
        };

        var log = new List<CommanderLogLine>
        {
            new CommanderLogLine(0, CommanderLogTone.Economy, "THEATER COMMANDER SECURED A SUPPLY CONTRACT.", 12f),
            new CommanderLogLine(5, CommanderLogTone.Order, "BASE COMMANDER SATO REPORTS PATROL ROUTE SET.", 95f),
            new CommanderLogLine(6, CommanderLogTone.Contact, "HOSTILE POST IDENTIFIED NEAR ICARIA.", 41f),
            new CommanderLogLine(4, CommanderLogTone.Loss, "BASE COMMANDER FERRO KILLED AT DEPOT SOUTH RIDGE.", 610f),
            new CommanderLogLine(1, CommanderLogTone.Alert, "AIR COMPONENT CMDR UNDER FIRE.", 3f),
        };
        var hostileLog = new List<CommanderLogLine>
        {
            new CommanderLogLine(6, CommanderLogTone.Contact, "ENEMY AIR COMPONENT CMDR SPOTTED.", 41f),
            new CommanderLogLine(4, CommanderLogTone.Economy, "ENEMY STIPEND PAYOUT OBSERVED.", 190f),
        };
        return new CocStaffStub(commanders, log, hostileLog, available);
    }

    private sealed class CocStaffStub : IHighCommandView
    {
        private readonly IReadOnlyList<CommanderView> commanders;
        private readonly IReadOnlyList<CommanderLogLine> log;
        private readonly IReadOnlyList<CommanderLogLine> hostileLog;
        private readonly bool available;

        public CocStaffStub(
            IReadOnlyList<CommanderView> commanders,
            IReadOnlyList<CommanderLogLine> log,
            IReadOnlyList<CommanderLogLine> hostileLog,
            bool available)
        {
            this.commanders = commanders;
            this.log = log;
            this.hostileLog = hostileLog;
            this.available = available;
        }

        public bool Available => available;
        public string Status => available ? "chain of command is running" : "No staff has formed for your faction.";
        public string Signal => "STIPEND PAID: 15% COMMAND SHARE";
        public float FriendlyCohesion => 0.72f;
        public int FriendlyActive => 5;
        public int FriendlyKia => 1;
        public IReadOnlyList<CommanderView> Commanders => commanders;
        public IReadOnlyList<CommanderLogLine> Log => log;
        public IReadOnlyList<CommanderLogLine> HostileLog => hostileLog;
        public int HighlightedId { get; private set; } = int.MinValue;

        public void Highlight(int id) => HighlightedId = id;

        public void Refresh() { }
    }

    // ------------------------------------------------------------------ plumbing

    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void SetField(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, Private);
        Check(info != null, "missing field " + field);
        info.SetValue(target, value);
    }

    private static object Call(object target, string method, params object[] arguments)
    {
        MethodInfo info = target.GetType().GetMethod(method, Private);
        Check(info != null, "missing method " + method);
        return info.Invoke(target, arguments);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

namespace BoscaliSummer.Features.Command.Configuration
{
    internal sealed class CommandSettings
    {
        public readonly Setting<bool> Enabled = new Setting<bool>(true);
        public readonly Setting<bool> FrontlinesOverlay = new Setting<bool>(false);
        internal sealed class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    }
}

namespace BoscaliSummer.Features.Command.Runtime
{
    internal enum SectorControl : byte { Neutral = 0, Friendly = 1, Hostile = 2, Contested = 3 }

    internal sealed class TacticalSectorGrid
    {
        public const int MaximumNodes = 128;
        public struct TacticalNode
        {
            public bool IsContested;
            public float CaptureProgress;
            public SectorControl Faction;
            public string Name;
            public bool IsAirbase;
        }
        private readonly System.Collections.Generic.List<TacticalNode> nodes =
            new System.Collections.Generic.List<TacticalNode>();
        public System.Collections.Generic.IReadOnlyList<TacticalNode> GetNodes() => nodes;
    }

    internal sealed class CommandManager
    {
        public BoscaliSummer.Features.Command.Domain.TacticalTheaterState TheaterState { get; } =
            new BoscaliSummer.Features.Command.Domain.TacticalTheaterState();
        public void UpdateTelemetry(FactionHQ hq) { }
    }
}

namespace BoscaliSummer.Features.Command.Presentation
{
    public partial class ComMapOverlay
    {
        internal BoscaliSummer.Features.Command.Runtime.TacticalSectorGrid Grid => null;
    }
}
#endif
