#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BoscaliSummer.Modules.Events.Configuration;
using BoscaliSummer.Modules.Events.Domain;
using BoscaliSummer.Modules.Events.Presentation;
using BoscaliSummer.Modules.Events.Runtime;
using BoscaliSummer.Core.Contracts;
using NOAvionics;
using NuclearOption.Networking;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

/// <summary>
/// Offline gate for the EVN console (empty, active, scripted superevent, late fill) and the
/// field archive window (every shelf, from a synthetic encyclopedia). Compiles the real presentation sources
/// against stubbed game types, renders PNGs and fails on text overflow, text entering the scroll gutter, text
/// drawn over other text, low contrast, tabs or sections without an icon, parts overlapping their neighbour,
/// stale (unmeasured) layout, windows larger than the screen, a zero where a dash belongs and a comparative
/// bar that does not match its shelf.
/// </summary>
public static class EventsUnityCheck
{
    private const int W = 1920, H = 1080;
    private static readonly List<string> Failures = new List<string>();
    private static int checkedTexts, checkedFlows;

    public static void Run()
    {
        if (!EnsureTmpEssentials(Run)) return;
        try
        {
            SetExecutablePath("EventsCheck.exe");
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvBundle.ResetForTests();
            if (File.Exists("avionics-ui.bundle")) AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            Check(AvIcons.Available, "production icon atlas must load before rendering");
            Shader.SetGlobalFloat("_NOA_Now", 1e6f);
            AvFxDriver.Configure(AvBundle.Available ? AvFxTier.Full : AvFxTier.Off, false);
            AvStyleHost.SetTheme(AvThemeId.Steel);
            new GameObject("Events", typeof(EventSystem));
            Directory.CreateDirectory("renders");

            var eventTextures = new HashSet<Texture>();
            foreach (EventDefinition definition in EventCatalog.All)
            {
                Sprite art = EventArtCache.Get(definition.IconKey, "default");
                Check(art != null && art.rect.width == 1024 && art.rect.height == 576,
                    definition.Id + " must load a high-resolution dispatch image");
                Check(art != null && eventTextures.Add(art.texture),
                    definition.Id + " must have its own event texture");
            }
            EventArtCache.Clear();

            foreach (float height in new[] { 420f, 596f, 896f }) CheckConsole(height);
            CheckArchive(false, new Vector2(1920f, 1080f), "1080");
            CheckArchive(true, new Vector2(1920f, 1080f), "1080");
            CheckArchive(true, new Vector2(1366f, 768f), "768");
            CheckArchive(true, new Vector2(840f, 640f), "compact");
        }
        catch (Exception e) { Failures.Add("exception: " + e); }
        File.WriteAllText("result.txt", Failures.Count == 0
            ? "PASS: EVN console (empty, active, superevent, late fill, page switch) and field archive window " +
              "(eight shelves, three window sizes) render without overlap or overflow; " + checkedTexts + " text checks over " +
              checkedFlows + " flows. Game adapters are stubbed; in-game acceptance remains required."
            : "FAIL (" + Failures.Count + "):\n" + string.Join("\n", Failures.GetRange(0, Math.Min(60, Failures.Count))));
        EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
    }

    // ---- Fixtures ------------------------------------------------------------------------------------------

    private sealed class Rig
    {
        public readonly GameObject CameraGo;
        public readonly Camera Cam;
        public readonly Canvas Canvas;
        public readonly RenderTexture Target;

        public Rig()
        {
            CameraGo = new GameObject("cam", typeof(Camera));
            Cam = CameraGo.GetComponent<Camera>();
            Cam.orthographic = true;
            Cam.orthographicSize = H / 2f;
            Cam.transform.position = new Vector3(0f, 0f, -10f);
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.07f, 0.09f, 0.11f);
            Target = new RenderTexture(W, H, 24);
            Cam.targetTexture = Target;
            Canvas = new GameObject("canvas", typeof(Canvas)).GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)Canvas.transform).sizeDelta = new Vector2(W, H);
        }

        /// <summary>Saves a crop (top-left pixel coordinates) of the rendered frame.</summary>
        public void Save(string name, int x, int y, int w, int h)
        {
            Canvas.ForceUpdateCanvases();
            foreach (TMP_Text t in Canvas.GetComponentsInChildren<TMP_Text>()) t.ForceMeshUpdate();
            Cam.Render();
            RenderTexture.active = Target;
            x = Mathf.Clamp(x, 0, W - 1);
            y = Mathf.Clamp(y, 0, H - 1);
            w = Mathf.Min(w, W - x);
            h = Mathf.Min(h, H - y);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(x, H - y - h, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine("renders", name + ".png"), tex.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(tex);
        }

        public void Dispose()
        {
            Cam.targetTexture = null;
            Object.DestroyImmediate(Canvas.gameObject);
            Object.DestroyImmediate(CameraGo);
            Object.DestroyImmediate(Target);
        }
    }

    private sealed class PanelFixture
    {
        public Rig Rig;
        public EventsMfdPanel Panel;
        public EventsManager Events;
        public AvConsole Console;
        public MissionManager Mission;
        public float Height;
    }

    private static EventDefinition Def(string id) => Array.Find(EventCatalog.All, e => e.Id == id);
    private static EventDefinition FirstSuper() => Array.Find(EventCatalog.All, e => e.IsSuper);

    private static ActiveEventView View(EventDefinition d, float start, float end, bool resolved = true)
    {
        float mult = EventSelector.EffectiveSupportMultiplier(d.SupportCostMultiplier, 1f);
        var steps = new ActiveEventStep[d.Script.Length];
        for (int i = 0; i < steps.Length; i++) steps[i] = new ActiveEventStep(d.Script[i].Label, d.Script[i].AtSeconds);
        int percent = Mathf.RoundToInt((d.SupportCooldownMultiplier - 1f) * 100f);
        return new ActiveEventView(d.Id, d.Title, d.FlavorText, EventCatalog.CategoryLabel(d.Category),
            EventCatalog.TierLabel(d.Tier), EventCatalog.TargetLabel(d.Target), d.IsSuper, d.IconKey,
            EventSelector.EffectSummary(mult), steps, start, end, resolved,
            percent == 0 ? "" : "SUPPORT RESET " + (percent > 0 ? "+" : "") + percent + "%");
    }

    private static PanelFixture BuildPanel(float height)
    {
        var f = new PanelFixture { Rig = new Rig(), Height = height };
        f.Mission = new MissionManager { MissionTime = 600f };
        NetworkSceneSingleton<MissionManager>.i = f.Mission;
        Encyclopedia.i = Library();

        var settings = new EventsSettings(new ConfigFile(Path.GetFullPath("events-" + Guid.NewGuid().ToString("N") + ".cfg"), false));
        f.Events = new GameObject("EventsManager").AddComponent<EventsManager>();
        f.Panel = new GameObject("EvnPanel").AddComponent<EventsMfdPanel>();
        f.Panel.Configure(settings, f.Events, null);

        var template = new GameObject("Template", typeof(RectTransform), typeof(MFDScreen)).GetComponent<MFDScreen>();
        var templateRect = (RectTransform)template.transform;
        RectTransform mount = AvLay.Child((RectTransform)f.Rig.Canvas.transform, "MfdColumn");
        AvLay.Place(mount, 0f, 0f, 480f, height);
        templateRect.SetParent(mount, false);
        templateRect.anchorMin = templateRect.anchorMax = templateRect.pivot = new Vector2(0f, 1f);
        templateRect.sizeDelta = new Vector2(480f, 596f);

        var bezelGo = new GameObject("Bezel", typeof(RectTransform), typeof(Image), typeof(Button));
        bezelGo.transform.SetParent(f.Rig.Canvas.transform, false);
        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(bezelGo.transform, false);
        var highlight = new GameObject("Highlight", typeof(RectTransform), typeof(Image));
        highlight.transform.SetParent(bezelGo.transform, false);

        MethodInfo build = typeof(EventsMfdPanel).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Instance);
        var screen = (MFDScreen)build.Invoke(f.Panel, new object[] { template, bezelGo.GetComponent<Button>() });
        Check(screen != null, "EVN Build must return a screen");
        f.Console = (AvConsole)typeof(EventsMfdPanel).GetField("console", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f.Panel);
        Check(f.Console != null, "EVN console must exist after Build");
        // Regression: clamping console.Root after building baked a negative offset into it and shifted the page left.
        Check(f.Console.Root.anchoredPosition == Vector2.zero,
            "console root must sit at the screen root's origin, not carry a clamp offset (" + f.Console.Root.anchoredPosition + ")");
        return f;
    }

    private static void RefreshPanel(PanelFixture f)
    {
        typeof(EventsMfdPanel).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f.Panel, null);
        for (int i = 0; i < 3; i++) f.Console.Ticker.TickNow();
    }

    // ---- Console scenarios ---------------------------------------------------------------------------------

    private static void CheckConsole(float height)
    {
        PanelFixture f = BuildPanel(height);
        try
        {
            EventsManager ev = f.Events;

            // 1. Quiet theater, nothing known: one compact hero card, empty log note.
            RefreshPanel(f);
            Snapshot(f, "empty");
            // The DESK drawers count what the archive would file, with the same rule the archive uses.
            for (int shelf = 0; shelf < ArchiveShelf.Count; shelf++)
            {
                Transform tile = Array.Find(f.Console.Root.GetComponentsInChildren<Transform>(true), t => t.name == "Tile " + ArchiveShelf.Names[shelf]);
                TMP_Text count = tile != null ? Array.Find(tile.GetComponentsInChildren<TMP_Text>(true), t => t.name == "Count") : null;
                Check(count != null && count.text == ArchiveShelf.CountOf(shelf).ToString(),
                    "DESK drawer " + ArchiveShelf.Names[shelf] + " must carry the shelf's real record count");
            }

            // 2. Content arrives after Finish(): a medium dispatch, ground data, history.
            ev.Balance = new TheaterBalance(2, 6, 4, 11, 22, 0);
            ev.SupersFired = 1;
            ev.HistoryList.Add(View(Def("monsoon_season"), 120f, 240f));
            ev.HistoryList.Add(View(Def("war_bond_drive"), 250f, 400f));
            ev.HistoryList.Add(View(Def("global_supply_chain_crisis"), 420f, 560f));
            ev.Current = View(Def("industrial_surge"), 560f, 860f);
            ev.SupportCostMultiplier = EventSelector.EffectiveSupportMultiplier(Def("industrial_surge").SupportCostMultiplier, 1f);
            ev.LocalSupportCooldownMultiplier = Def("industrial_surge").SupportCooldownMultiplier;
            ev.LocalTargeted = true;
            RefreshPanel(f);
            CheckImageExposure(f);
            Snapshot(f, "active");

            // 3. Penalty superevent with a script, ending soon.
            f.Mission.MissionTime = 1000f;
            EventDefinition super = FirstSuper();
            ev.Current = View(super, 700f, 1045f);
            float superMultiplier = EventSelector.EffectiveSupportMultiplier(super.SupportCostMultiplier, 1f);
            ev.SupportCostMultiplier = superMultiplier;
            ev.LocalSupportCooldownMultiplier = super.SupportCooldownMultiplier;
            RefreshPanel(f);
            CheckImageExposure(f);
            Snapshot(f, "super");

            // Only the nearest unexecuted beat is NEXT; later orders remain QUEUED.
            f.Mission.MissionTime = 702f;
            RefreshPanel(f);
            Snapshot(f, "script-upcoming");
            var scripts = f.Console.Page(0).Content.GetComponentsInChildren<TMP_Text>(true);
            Check(Array.FindAll(scripts, t => t.text.Contains("[NEXT]")).Length == 1,
                "exactly one upcoming scripted order must be identified as NEXT");
            Check(Array.Exists(scripts, t => t.text.Contains("[QUEUED]")), "later scripted orders must read QUEUED");
            ev.Current = View(super, 700f, 1045f, false);
            RefreshPanel(f);
            Snapshot(f, "target-lost");
            Check(Array.Exists(scripts, t => t.text.Contains("[CANCELLED]")), "unresolved target must cancel the script");
            Check(Array.Exists(scripts, t => t.text.Contains("TARGET LOST")),
                "the live hero must rebind its cancellation notice when target resolution changes");

            // 4. Aimed at the other side: the hero says so.
            ev.LocalTargeted = false;
            ev.Current = View(Def("frontline_overstretch") ?? super, 900f, 1200f);
            ev.SupportCostMultiplier = 1f;
            ev.LocalSupportCooldownMultiplier = 1f;
            RefreshPanel(f);
            Snapshot(f, "other-side");

            // 5. The dispatch ends.
            ev.Current = null;
            f.Mission.MissionTime = 1300f;
            RefreshPanel(f);
            Snapshot(f, "ended");
        }
        finally
        {
            f.Rig.Dispose();
            Object.DestroyImmediate(f.Panel.gameObject);
            Object.DestroyImmediate(f.Events.gameObject);
            DisposeFixtures();
            Encyclopedia.i = null;
        }
    }

    private static void CheckImageExposure(PanelFixture f)
    {
        var hero = (AvPart)typeof(EventsMfdPanel).GetField("hero", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f.Panel);
        var plate = (RectTransform)hero.Rect.Find("Plate");
        var fade = (RectTransform)hero.Rect.Find("ArtFade");
        var art = plate.Find("Art").GetComponent<RawImage>();
        Check(art.uvRect.width * art.uvRect.height >= 0.999f,
            "the active event image must retain its entire source frame");
        Check((plate.anchoredPosition.y - fade.anchoredPosition.y) / plate.rect.height >= 0.8f - 0.001f,
            "at least four fifths of the active artwork must remain above the fade");
    }

    /// <summary>Renders both console pages and runs the layout / text gate on each.</summary>
    private static void Snapshot(PanelFixture f, string name)
    {
        for (int page = 0; page < f.Console.PageCount; page++)
        {
            f.Console.SetPage(page);
            for (int i = 0; i < 3; i++) f.Console.Ticker.TickNow();
            Canvas.ForceUpdateCanvases();
            RectTransform rootRect = (RectTransform)f.Console.Root.parent;
            Vector3[] corners = new Vector3[4];
            rootRect.GetWorldCorners(corners);
            int left = Mathf.RoundToInt(corners[0].x + W / 2f), top = Mathf.RoundToInt(H / 2f - corners[1].y);
            string prefix = "EVN-" + name + "-" + f.Height + "-p" + (page + 1);
            ScrollRect scroll = f.Console.Page(page).Content.GetComponentInParent<ScrollRect>();
            float[] positions = scroll != null && f.Console.Page(page).ContentHeight > scroll.viewport.rect.height + 1f
                ? new[] { 1f, .5f, 0f } : new[] { 1f };
            foreach (float position in positions)
            {
                if (scroll != null) scroll.verticalNormalizedPosition = position;
                string suffix = position == 1f ? "-top" : position == 0f ? "-bottom" : "-mid";
                f.Rig.Save(prefix + suffix, left, top, 480, Mathf.RoundToInt(rootRect.rect.height));
            }
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            string where = "console/" + name + "/" + f.Height + "/p" + (page + 1);
            Gate(f.Console.Root, where, true);
            CheckFlow(f.Console.Page(page), where);
        }
        f.Console.SetPage(0);
    }

    // ---- Archive scenarios ---------------------------------------------------------------------------------

    private static readonly List<Object> Fixtures = new List<Object>();

    /// <summary>An inactive prefab stand-in made of boxes (centre, size), the way a unit prefab sits unspawned.</summary>
    private static GameObject Boxes(string name, params Vector3[] centreSizePairs)
    {
        var root = new GameObject(name);
        root.SetActive(false);
        for (int i = 0; i + 1 < centreSizePairs.Length; i += 2)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(root.transform, false);
            box.transform.localPosition = centreSizePairs[i];
            box.transform.localScale = centreSizePairs[i + 1];
        }
        foreach (Collider c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        Fixtures.Add(root);
        return root;
    }

    private static Sprite MapSymbol()
    {
        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        var pixels = new Color32[32 * 32];
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                pixels[y * 32 + x] = Mathf.Abs(x - 16) <= (31 - y) / 2 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        texture.SetPixels32(pixels);
        texture.Apply();
        Fixtures.Add(texture);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f), 100f);
        Fixtures.Add(sprite);
        return sprite;
    }

    private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    private const string Notes =
        "A synthetic record used to exercise the recognition card. It carries notes long enough to wrap over several lines in the " +
        "record pane, so that the prose block, and not only the figures, decides how tall the scrollable card becomes when it is pulled.";

    /// <summary>
    /// Fills a definition from one fixture row:
    /// name|code|key|length|width|height|value|mass|manpower|armour|tolerance|rcs|visible|capCapacity|capStrength|capDefense|aS|aA|aM|aR.
    /// Every third record keeps short notes and one in seven keeps none, so both ends of the notes block render.
    /// </summary>
    private static T Unit<T>(string row, int n, Sprite symbol) where T : UnitDefinition, new()
    {
        string[] c = row.Split('|');
        return new T
        {
            unitName = c[0], code = c[1], jsonKey = c[2], length = F(c[3]), width = F(c[4]), height = F(c[5]), value = F(c[6]),
            mass = F(c[7]), manpower = F(c[8]), armorTier = F(c[9]), damageTolerance = F(c[10]), radarSize = F(c[11]),
            visibleRange = F(c[12]), captureCapacity = (int)F(c[13]), captureStrength = F(c[14]), captureDefense = F(c[15]),
            roleIdentity = new RoleIdentity { antiSurface = F(c[16]), antiAir = F(c[17]), antiMissile = F(c[18]), antiRadar = F(c[19]) },
            bogeyName = n % 5 == 3 ? c[0].Split(' ')[0] + " type" : "",
            description = n % 7 == 6 ? "" : n % 3 == 2 ? "A short synthetic note." : Notes,
            mapIcon = n % 4 != 1 ? symbol : null,
        };
    }

    /// <summary>
    /// A believable encyclopedia: several records on every unit shelf with varied figures (so the comparative
    /// bars mean something), records that leave a figure unestablished, one with no model, and one the game has
    /// disabled, which the shelves must not file.
    /// </summary>
    private static Encyclopedia Library()
    {
        var book = new Encyclopedia();
        Sprite symbol = MapSymbol();

        // name|code|key|L|W|H|value|mass|crew|armour|tol|rcs|vis|cap|capStr|capDef|roles x4|maxSpeed|stall|manoeuvre|maxWeight
        string[] aircraft =
        {
            "A-19 Brawler|A-19|CAS1|18.0|19.0|3.5|36|10620|1|1|1|0.6|3500|0|0|0|0.8|0.3|0|0|740|220|7.5|19000",
            "Alkyon AB-4|AB-4|FastBomber1|24.9|31.6|4.4|390|34100|2|1|1.5|0.002|5000|0|0|0|1|0.7|0.1|0.2|1725|230|8|62000",
            "CI-22 Cricket|CI-22|COIN|10.4|11.6|3.4|11.5|3100|2|1|1|0.5|2000|0|0|0|0.56|0.44|0|0|510|140|6.5|4800",
            "EW-25 Medusa|EW-25|EW1|19.2|18.1|4.5|145|0|4|1|1|0.006|3500|0|0|0|0.31|0.36|0.73|1|1030|120|6|26000",
            "FS-12 Revoker|FS-12|Fighter1|15.8|11.0|5.2|65|8680|1|1|1|0.08|3000|0|0|0|0.46|1|0|0.33|1445|260|9|16000",
            "FS-20 Vortex|FS-20|SmallFighter1|14.1|9.3|3.1|90|0|1|1|1|0.001|2500|0|0|0|0.46|1|0|0.33|1495|270|9|16000",
            "KR-67 Ifrit|KR-67|Multirole1|19.0|14.3|4.6|126|16040|1|1|1|0.002|3500|0|0|0|0.46|1|0|0.33|1500|240|9|30000",
            "SAH-46 Chicane|SAH-46|AttackHelo1|16.6|7.4|4.1|31|7550|2|1|1|0.02|2000|2|0|0|0.9|0.27|0|0|360|0|3.5|10500",
            "SFB-81 Darkreach|SFB-81|Darkreach|27.4|36.5|4.0|225|54311|2|1|1.5|0.01|5000|0|0|0|1|0|0|0|1005|240|4|115000",
            "T/A-30 Compass|T/A-30|trainer|13.2|12.1|3.3|21.8|6111|2|1|1|0.12|2500|0|0|0|0.64|0.62|0|0|1060|180|8|10800",
            "UH-90 Ibis|UH-90|UtilityHelo1|14.2|6.4|4.6|29.5|7300|2|1|1|0.03|3000|2|0|0|0.9|0.27|0|0|482|0|3.5|15500",
            "VL-49 Tarantula|VL-49|QuadVTOL1|28.5|22.7|6.1|118|28900|2|1|1|3|4500|20|0|0|0.9|0.27|0|0|535|0|2.5|53500",
            "VT-7 Vagrant|VT-7|VTOLTrainer1|13.4|9.8|3.8|28.9|5890|2|1|1|0.12|2500|0|0|0|0.64|0.62|0|0|1060|180|8|10050",
        };
        for (int i = 0; i < aircraft.Length; i++)
        {
            string[] c = aircraft[i].Split('|');
            AircraftDefinition d = Unit<AircraftDefinition>(aircraft[i], i, symbol);
            float length = d.length, span = d.width, height = d.height;
            d.unitPrefab = Boxes("Aircraft " + i, new Vector3(0f, 0f, 0f), new Vector3(1.4f, 1.2f, length),
                new Vector3(0f, 0f, -length * 0.08f), new Vector3(span, 0.2f, length * 0.24f),
                new Vector3(0f, height * 0.5f, -length * 0.42f), new Vector3(0.2f, height, length * 0.14f));
            d.aircraftInfo = new AircraftInfo { emptyWeight = 5200f, maxSpeed = F(c[20]), stallSpeed = F(c[21]), maneuverability = F(c[22]), maxWeight = F(c[23]) };
            book.aircraft.Add(d);
        }

        // ...|road speed (km/h)|disabled
        string[] vehicles =
        {
            "AFV6 AA|AA|6x6_1_AA|7.2|3.1|2.5|1.8|20000|3|3|1|1|5000|4|3|0|0.1|0.63|0.62|0|95|0",
            "AFV6 APC|APC|6x6_1_APC|7.2|3.1|2.5|1.3|19500|12|3|1|1|5000|8|12|0|0.4|0.15|0|0|100|0",
            "AFV8 IFV|IFV|AFV8_IFV|8.2|3.1|3.4|6.1|0|9|3|1|1|5500|6|9|0|0.59|0.43|0|0|90|0",
            "AeroSentry SPAAG|SPAAG|SPAAG1|8.4|3.6|3.4|8|0|2|4|5|1|6000|2|2|0|0.1|0.72|0.53|0|65|0",
            "Boltstrike RAM45 Launcher|SAM R|SAMTurret1|4.8|1.6|1.6|7|6000|0|1|0|1|6000|0|0|0|0|0.75|0.75|0|0|1",
            "HLT Flatbed|TRK|HLT-L|10.1|2.7|3.0|0.46|12000|2|1|0.5|1|6000|3|2|0|0|0|0|0|85|0",
            "HLT Fuel Tanker|FUEL|HLT-FT|10.1|2.7|3.0|0.51|20000|2|1|0.5|1|6000|3|2|0|0|0|0|0|80|0",
            "HLT Mobile Artillery|ARTY|HLT-MART|10.1|2.7|3.0|14|28000|2|1|0.5|1|6000|3|2|0|0.7|0|0|0|70|0",
            "HLT Radar Truck|RDR|HLT-R|10.1|2.7|4.1|20|20000|2|1|0.5|1|6000|3|2|0|0|1|0|0|75|0",
            "Hexhound GMG|UGV|UGV1_grenade|2.7|1.6|1.2|0.8|3000|0|1|0.2|1|1200|0|0|0|0.25|0|0|0|40|0",
            "LCV25 AT|LCV25|LightTruck1_AT|5.5|2.4|2.6|0.55|5100|4|2|0.3|1|4000|3|3|0|0.5|0.25|0|0|120|0",
            "Linebreaker IFV|IFV|Linebreaker_IFV|7.9|3.9|3.3|8.1|40000|9|4|1.5|1|5500|8|9|0|0.54|0.46|0|0|70|0",
            "MSV Nuclear Ballistic Missile Launcher|TBM|Truck2-TBM-N|10.1|2.7|3.4|15.7|20000|2|1|0.5|1|6000|3|2|0|1|0|0|0|80|0",
            "Spearhead MBT|MBT|MBT1|8.5|4.2|3.2|15.3|0|3|6|3|1|6500|2|3|0|0.7|0.2|0.1|0|68|0",
            "T9K41 Boltstrike|SAM R|RadarSAM1|8.6|3.8|5.2|12.5|0|2|4|1.5|1|6000|2|2|0|0|1|1|0|60|0",
        };
        for (int i = 0; i < vehicles.Length; i++)
        {
            string[] c = vehicles[i].Split('|');
            VehicleDefinition d = Unit<VehicleDefinition>(vehicles[i], i, symbol);
            d.unitPrefab = Boxes("Vehicle " + i, new Vector3(0f, d.height * 0.3f, 0f), new Vector3(d.width, d.height * 0.6f, d.length),
                new Vector3(0f, d.height * 0.8f, -d.length * 0.1f), new Vector3(d.width * 0.6f, d.height * 0.4f, d.length * 0.4f));
            if (F(c[20]) > 0f) d.unitPrefab.AddComponent<GroundVehicle>().topSpeed = F(c[20]);
            d.disabled = c[21] == "1";
            book.vehicles.Add(d);
        }

        // ...|top speed (km/h)|ship type
        string[] ships =
        {
            "Annex Class Carrier|SHP|AssaultCarrier1|242|40|50|3500|40124700|2000|5|35|1|50000|100|50|0|0.95|0.95|0.95|0|60|LHA",
            "Argus Class Frigate|SHP|Frigate1|112|15|38|750|4347500|120|4|40|1|15000|10|25|0|0.9|0.9|0.9|0|61|FFG",
            "Cursor Class LFD|SHP|SmallCarrier1|96|36|28|800|3495200|160|4|22|1|15000|10|25|0|0.9|0.9|0.9|0|70|LFD",
            "Dynamo Class Destroyer|SHP|Destroyer1|162|22|32|1350|12172000|180|5|30|1|30000|10|100|0|0.95|0.95|0.9|0|59|DDG",
            "Hyperion Class Carrier|SHP|FleetCarrier1|267|64|48|3500|0|2000|5|40|1|50000|10|100|0|0.95|0.95|0.95|0|58|CV",
            "OTB-31 landing craft|LCAC|LandingCraft1|26.1|13.9|7.5|90|165400|5|1|2|1|6000|1|5|0|0.1|0.2|0.1|0|125|LC",
            "Shard Class Corvette|SHP|Corvette1|76.2|10.5|13.8|190|679100|40|4|8|1|15000|10|25|0|0.9|0.9|0.9|0|70|FFL",
            "Surf Class Patrol Boat|PB|PatrolBoat1|30|8|9.2|26|97500|12|2|4|1|5000|10|25|0|0.9|0.2|0.1|0|100|PB",
        };
        for (int i = 0; i < ships.Length; i++)
        {
            string[] c = ships[i].Split('|');
            ShipDefinition d = Unit<ShipDefinition>(ships[i], i, symbol);
            d.unitPrefab = Boxes("Ship " + i, new Vector3(0f, d.height * 0.15f, 0f), new Vector3(d.width, d.height * 0.3f, d.length),
                new Vector3(d.width * 0.25f, d.height * 0.6f, -d.length * 0.1f), new Vector3(d.width * 0.3f, d.height * 0.6f, d.length * 0.18f));
            d.shipInfo = new ShipInfo { mass = 3500000f, topSpeed = F(c[20]) };
            d.shipType = (ShipType)Enum.Parse(typeof(ShipType), c[21]);
            book.ships.Add(d);
        }

        string[] buildings =
        {
            "12.7mm MG Emplacement|MG|Emplacement1_MG|5|5|1.6|0.1|0|1|2|1|1|500|2|2|100|0.15|0.1|0|0",
            "23mm AAA Emplacement|AAA|Emplacement1_23mm|5|5|1.8|0.25|0|2|2|1|1|500|2|2|100|0.18|0.55|0.02|0",
            "Aircraft Revetment|REV|revetment1|34.2|33.2|7.9|1|0|4|3|4|1|10000|10|0|25|0|0|0|0",
            "Control Tower|TWR|controlTower1|20.9|39.7|46.2|60|0|12|1|2|1|10000|10|0|30|0|0|0|0",
            "Enrichment Plant|BLD|enrichmentPlant1|197|240|55|4000|0|100|1|8|1|10000|10|0|50|0|0|0|0",
            "Fuel Depot|FUEL|fuelTank1|30|30|22.5|1.5|0|0|1|1|1|10000|0|0|30|0|0|0|0",
            "Hardened Aircraft Shelter|HGR-H|shelter1|34.2|33.2|7.9|6|0|4|8|1|1|10000|10|0|50|0|0|0|0",
            "Infantry Bunker|BNKR|gabionBunker1|10|10|4.5|0.1|0|6|2|3|1|500|10|2|200|0.28|0.25|0|0",
            "Munitions Bunker|AMMO|ammunitionBunker|27|20|4.5|12|0|10|8|3|1|2000|10|0|25|0|0|0|0",
            "Radar Station|RDR|radarStation1|20|25.8|36.6|25|0|12|1|2|1|10000|10|0|50|0|0|0|0",
            "Vertical Factory|FAC|factory_tall|55.3|111|74.6|60|0|10|1|6|1|10000|10|0|25|0|0|0|0",
        };
        for (int i = 0; i < buildings.Length; i++)
        {
            BuildingDefinition d = Unit<BuildingDefinition>(buildings[i], i, symbol);
            // The fuel depot keeps a prefab with nothing to draw: its card must say that no plate is held.
            d.unitPrefab = d.code == "FUEL" ? Boxes("Building " + i)
                : Boxes("Building " + i, new Vector3(0f, d.height * 0.5f, 0f), new Vector3(d.width, d.height, d.length));
            book.buildings.Add(d);
        }

        // name|code|key|L|W|H|value|mass|...|roles x4|range (m)|yield (kg TNT)|burn (s)|delta-v|round cost|seeker
        string[] missiles =
        {
            "127mm Guided Shell|SHL|Shell_127mm_guided|0.7|0.2|0.2|0.04|30|0|0.1|0|0.01|1500|0|0|0|0.67|0|0|0|0|8|0|0|0.04|GPS",
            "AAM-29 Scythe|MSL|AAM2|3.5|0.4|0.4|0.89|160|0|1|0|0.01|2000|0|0|0|0|1|0|0|40000|22|9|900|0.89|Active radar",
            "AAM-36 Scimitar|MSL|AAM4|3.7|0.5|0.5|2.1|200|0|1|0|0.01|2000|0|0|0|0|1|0|0|80000|28|14|1300|2.1|Active radar",
            "AGM-48|MSL|AGM1|1.6|0.2|0.2|0.09|50|0|0.1|0|0.02|2000|0|0|0|0.72|0|0|0|8000|9|4|420|0.09|Laser",
            "AGM-99|MSL|AShM2|3.9|0.4|0.4|1.25|450|0|0.1|0|0.008|1000|0|0|0|1|0|0|0|120000|165|90|800|1.25|Optical",
            "ALND-4 (20kt)|MSL|CruiseMissile20kt|6.3|2.7|1.0|32|1280|0|0.1|0|0.005|1500|0|0|0|1|0|0|0|600000|20000000|1800|700|32|Terrain",
            "ARAD-116|MSL|ARM1|4.4|0.3|0.3|0.68|400|0|0.1|0|0.02|2500|0|0|0|0|0|0|1|90000|66|30|1100|0.68|Anti-radiation",
            "CBO-400|BOMB|bomb_cluster_1|2.7|0.4|0.4|0.36|400|0|1|1|0.15|3000|0|0|0|1|0.1|0|0|12000|0|0|0|0.36|",
            "GPO-500|BOMB|bomb500|2.7|0.4|0.4|0.02|500|0|1|1|0.15|3000|0|0|0|1|0|0|0|9000|240|0|0|0.02|",
            "GPO-N (1.5kt)|BOMB|nuclearBomb1|2.7|0.5|0.5|28|400|0|1|0|0.2|4000|0|0|0|1|0|0|0|9000|1500000|0|0|28|",
            "IRM-S2|MSL|AAM3|2.5|0.2|0.2|0.18|60|0|1|0|0.01|2000|0|0|0|0|1|0.1|0|12000|6|5|700|0.18|Infrared",
            "StratoLance R9|MSL|SAM_Radar2|5.0|0.3|0.3|4|0|0|0.1|0|0.1|4000|0|0|0|0|1|0|0|150000|60|35|1600|4|Semi-active radar",
        };
        for (int i = 0; i < missiles.Length; i++)
        {
            string[] c = missiles[i].Split('|');
            MissileDefinition d = Unit<MissileDefinition>(missiles[i], i, symbol);
            d.unitPrefab = Boxes("Missile " + i, new Vector3(0f, 0f, 0f), new Vector3(d.height, d.height, d.length),
                new Vector3(0f, 0f, -d.length * 0.42f), new Vector3(d.width * 1.6f, 0.02f, d.length * 0.12f));
            Missile round = d.unitPrefab.AddComponent<Missile>();
            round.mass = d.mass;
            round.yield = F(c[21]);
            round.burnTime = F(c[22]);
            round.deltaV = F(c[23]);
            round.info = new WeaponInfo { costPerRound = F(c[24]), targetRequirements = new TargetRequirements { maxRange = F(c[20]) } };
            if (c[25].Length > 0) d.unitPrefab.AddComponent<MissileSeeker>().seekerType = c[25];
            book.missiles.Add(d);
        }
        return book;
    }

    private static void DisposeFixtures()
    {
        foreach (Object o in Fixtures) if (o != null) Object.DestroyImmediate(o);
        Fixtures.Clear();
    }

    private static readonly string[] ShelfTags = { "aircraft", "ground", "naval", "structures", "ordnance", "events", "world", "manual" };

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

    private static void CheckArchive(bool withLibrary, Vector2 units, string tag)
    {
        var rig = new Rig();
        Encyclopedia.i = withLibrary ? Library() : null;
        EventDeskArchive archive = null;
        try
        {
            archive = EventDeskArchive.Create(rig.Canvas.transform, units);
            AvWindow window = archive.Window;
            RectTransform root = window.Root;
            string prefix = "archive/" + tag + "/";

            void Frame(string name)
            {
                string where = prefix + name;
                for (int i = 0; i < 3; i++) window.Ticker.TickNow();
                Canvas.ForceUpdateCanvases();
                Vector3[] c = new Vector3[4];
                root.GetWorldCorners(c);
                int x = Mathf.RoundToInt(c[0].x + W / 2f), y = Mathf.RoundToInt(H / 2f - c[1].y);
                string renderName = "ARCHIVE-" + tag + "-" + name;
                int renderW = Mathf.RoundToInt(c[2].x - c[0].x) + 12, renderH = Mathf.RoundToInt(c[1].y - c[0].y) + 12;
                rig.Save(renderName, x - 6, y - 6, renderW, renderH);
                Gate(root, where, false);
                CheckCollisions(root, where);
                var panes = (ArchivePanesPart)FindPanes(window.Body);
                CheckFlow(window.Body, where + "/body");
                CheckFlow(panes.Left.Flow, where + "/index");
                CheckFlow(panes.Right.Flow, where + "/record");

                // The open record is the one armed line of the index, and only that one.
                int armed = 0;
                foreach (ArchiveIndexRow row in Field<ArchiveIndexRow[]>(Field<object>(archive, "index"), "rows"))
                    if (row.Rect.gameObject.activeSelf && Field<bool>(row, "chosen")) armed++;
                int held = (int)typeof(EventDeskArchive).GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(archive);
                Check(armed == (held > 0 ? 1 : 0), where + ": the index must arm exactly the open record (" + armed + ")");
                // A figure that is not established reads as a dash, never as a zero.
                foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false))
                    if ((t.name.StartsWith("FigureValue") || t.name.StartsWith("RoleValue")) && (t.text == "0" || t.text == "$0" || t.text.Length == 0))
                        Failures.Add(where + ": " + t.name + " reads '" + t.text + "' instead of a dash");

                ScrollRect recordScroll = panes.Right.Root.GetComponent<ScrollRect>();
                if (recordScroll.content.rect.height > recordScroll.viewport.rect.height + 1f)
                {
                    float was = recordScroll.verticalNormalizedPosition;
                    foreach (float position in new[] { .5f, 0f })
                    {
                        recordScroll.verticalNormalizedPosition = position;
                        rig.Save(renderName + (position == 0f ? "-bottom" : "-mid"), x - 6, y - 6, renderW, renderH);
                    }
                    recordScroll.verticalNormalizedPosition = was;
                }
            }

            TMP_Text Text(string name) => Array.Find(root.GetComponentsInChildren<TMP_Text>(false), t => t.name == name);

            if (!withLibrary)
            {
                // No encyclopedia: the unit shelves are empty drawers with an in-world notice, the tabs read a dash.
                archive.Show(ArchiveShelf.Aircraft);
                Frame("empty");
                Check(Text("Title 0") != null && Text("Title 0").text == "SHELF EMPTY", "an empty drawer must say SHELF EMPTY");
                Check(Array.Find(root.GetComponentsInChildren<TMP_Text>(true),
                        t => t.name == "Count" && t.transform.parent.name == "Shelf AIRCRAFT").text == "—",
                    "an empty shelf's tab must read a dash, not 0");
                return;
            }

            // Every shelf, first record.
            for (int shelf = 0; shelf < ArchiveShelf.Count; shelf++)
            {
                archive.Show(shelf);
                Frame(ShelfTags[shelf]);
                int expected = ArchiveShelf.CountOf(shelf);
                TMP_Text count = Array.Find(root.GetComponentsInChildren<TMP_Text>(true),
                    t => t.name == "Count" && t.transform.parent.name == "Shelf " + ArchiveShelf.Names[shelf]);
                Check(expected > 0 && count != null && count.text == expected.ToString(),
                    prefix + ShelfTags[shelf] + ": the shelf tab must carry its real record count (" + (count != null ? count.text : "none") + " vs " + expected + ")");
            }
            Check(ArchiveShelf.CountOf(ArchiveShelf.Ground) == Encyclopedia.i.vehicles.Count - 1,
                "a definition the game has disabled must not be filed on its shelf");

            // Aircraft: the comparative block. The fastest airframe fills its bar; a helicopter has no stall speed.
            var card = Field<UnitCardPart>(archive, "card");
            object figures = Field<object>(card, "figures");
            archive.Show(ArchiveShelf.Aircraft, 1);
            Frame("fastest");
            Check(Text("HeadTitle").text == "ALKYON AB-4", "Show(shelf, record) must open that record (" + Text("HeadTitle").text + ")");
            Check(Mathf.Abs(Field<float[]>(figures, "share")[0] - 1f) < 0.001f, "the fastest airframe's MAX SPEED bar must be full");
            Check(Text("FigureValue 0").text == "1,725" && Text("FigureUnit 0").text == "KM/H", "MAX SPEED must read 1,725 KM/H");
            Check(Text("FigureRank 0").text == "1/13", "the fastest of thirteen must rank 1/13 (" + Text("FigureRank 0").text + ")");
            archive.Show(ArchiveShelf.Aircraft, 7);
            Frame("helo");
            Check(Text("FigureValue 1").text == "—" && Field<float[]>(figures, "share")[1] < 0f,
                "a stall speed the definition does not establish must be a dash with no bar");
            float helo = Field<float[]>(figures, "share")[0];
            Check(helo > 0.19f && helo < 0.22f, "360 km/h against the shelf's 1,725 must draw about a fifth of the bar (" + helo + ")");

            // The plate: a model, a scale bar off the render, the filed dimensions, the four views.
            RawImage model = card.ModelView;
            Check(model.gameObject.activeInHierarchy && model.texture != null, "a unit card must draw its model");
            Check(Text("ScaleText") != null && Text("ScaleText").text.EndsWith(" M"), "the plate must carry a scale bar in metres");
            Check(Text("Dimensions").text.Contains("16.6") && Text("Dimensions").text.Contains("7.4"), "the plate must print the filed length and span");
            Click(Array.Find(root.GetComponentsInChildren<AvControl>(false), c => c.Label == "PLAN"));
            Frame("plan");
            Check(Text("PlateCaption").text.Contains("VIEW PLAN"), "PLAN must switch the plate to the planform (" + Text("PlateCaption").text + ")");
            float plan = Field<EventAircraftPreview>(archive, "preview").SpanMetres;
            Click(Array.Find(root.GetComponentsInChildren<AvControl>(false), c => c.Label == "PORT"));
            Frame("port");
            // In plan the frame must hold the 7.4 m rotor span or the 16.6 m length at the plate's aspect; in port the length.
            Check(plan > 0f && Field<EventAircraftPreview>(archive, "preview").SpanMetres > 0f, "each view must report the metres its frame spans");

            // Arrow keys step the index and wrap; a cross-reference opens its record.
            MethodInfo step = typeof(EventDeskArchive).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Instance);
            step.Invoke(archive, new object[] { 1 });
            for (int i = 0; i < 3; i++) window.Ticker.TickNow();
            Check(Text("HeadTitle").text == "SFB-81 DARKREACH", "DOWN must step to the next record (" + Text("HeadTitle").text + ")");
            archive.Show(ArchiveShelf.Aircraft, 0);
            step.Invoke(archive, new object[] { -1 });
            for (int i = 0; i < 3; i++) window.Ticker.TickNow();
            Check(Text("HeadTitle").text == "VT-7 VAGRANT", "UP from the first record must wrap to the last (" + Text("HeadTitle").text + ")");
            Transform chip = Array.Find(root.GetComponentsInChildren<Transform>(false), t => t.name.StartsWith("Ref ") && t.gameObject.activeInHierarchy);
            Check(chip != null, "a card must offer cross-references to the same role");
            if (chip != null)
            {
                string target = Array.Find(chip.GetComponentsInChildren<TMP_Text>(), t => t.name == "Name").text;
                chip.GetComponentInChildren<AvHit>().OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                for (int i = 0; i < 3; i++) window.Ticker.TickNow();
                Check(Text("HeadTitle").text == target, "a cross-reference must open the record it names (" + target + ")");
            }

            // At full height the index foot names the card that leads the shelf in each figure, and opens it (a
            // shorter window has no room for it). A shelf filed under class codes, several cards to a code, carries
            // no leaders: a class code would not say which card.
            if (tag == "1080")
            {
                archive.Show(ArchiveShelf.Aircraft, 0);
                for (int i = 0; i < 3; i++) window.Ticker.TickNow();
                Transform leader = Array.Find(root.GetComponentsInChildren<Transform>(false), t => t.name == "Leader 0");
                Check(leader != null && Array.Find(leader.GetComponentsInChildren<TMP_Text>(), t => t.name == "Value").text == "1,725 KM/H",
                    "the aircraft drawer must name its fastest card in the index foot");
                if (leader != null)
                {
                    leader.GetComponentInChildren<AvHit>().OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                    for (int i = 0; i < 3; i++) window.Ticker.TickNow();
                    Check(Text("HeadTitle").text == "ALKYON AB-4", "a shelf leader must open the card it names (" + Text("HeadTitle").text + ")");
                }
                archive.Show(ArchiveShelf.Ordnance, 0);
                for (int i = 0; i < 3; i++) window.Ticker.TickNow();
                Check(Array.Find(root.GetComponentsInChildren<Transform>(false), t => t.name == "Leader 0") == null,
                    "a shelf filed under class codes must not list leaders by code");
            }

            // A prefab with nothing to draw: the plate says so and its view controls stand down.
            int depot = -1;
            var library = Field<ArchiveLibrary>(archive, "library");
            for (int i = 0; i < library.Count(ArchiveShelf.Structures); i++)
                if (library.Card(ArchiveShelf.Structures, i).Code == "FUEL") depot = i;
            archive.Show(ArchiveShelf.Structures, depot);
            Frame("noplate");
            Check(Text("PlateNote") != null && !model.gameObject.activeInHierarchy, "a card with no model must say NO PLATE HELD");
            Check(Text("ScaleText") == null, "a card with no model must not carry a scale bar");

            // Naval is filed largest class first; ordnance prints a range for powered rounds only.
            archive.Show(ArchiveShelf.Naval);
            for (int i = 0; i < 3; i++) window.Ticker.TickNow();
            Check(Text("HeadCode").text == "CV", "the naval shelf must open on the fleet carrier (" + Text("HeadCode").text + ")");
            int bomb = -1;
            for (int i = 0; i < library.Count(ArchiveShelf.Ordnance); i++)
                if (library.Card(ArchiveShelf.Ordnance, i).Name == "GPO-N (1.5kt)") bomb = i;
            archive.Show(ArchiveShelf.Ordnance, bomb);
            Frame("bomb");
            Check(Text("FigureValue 1").text == "—", "an unpowered round must not print a range");
            Check(Text("FigureValue 2").text == "1.5" && Text("FigureUnit 2").text == "KT", "a 1.5 kt round must read 1.5 KT (" + Text("FigureValue 2").text + ")");

            // Every colour scheme: a card and a dossier must keep their contrast and must not collide in any of them.
            if (tag == "1080")
            {
                foreach (AvThemeId theme in (AvThemeId[])Enum.GetValues(typeof(AvThemeId)))
                {
                    if (theme == AvThemeId.Steel) continue;
                    AvStyleHost.SetTheme(theme);
                    archive.Show(ArchiveShelf.Aircraft, 4);
                    Frame("scheme-" + theme.ToString().ToLowerInvariant() + "-card");
                    archive.Show(ArchiveShelf.Events, 8);
                    Frame("scheme-" + theme.ToString().ToLowerInvariant() + "-dossier");
                }
                AvStyleHost.SetTheme(AvThemeId.Steel);
            }

            // Events: a scripted superevent (timed orders) and the last record.
            int superIndex = Array.FindIndex(EventCatalog.All, e => e.IsSuper);
            archive.Show(ArchiveShelf.Events, superIndex);
            Frame("super");
            Check(Text("HeadTitle").text == EventCatalog.All[superIndex].Title.ToUpperInvariant(), "the DESK must be able to open one dossier by index");
            Check(Text("StepTime 0") != null && Text("StepTime 0").text.StartsWith("T+"), "a scripted dossier must list its timed orders");
            archive.Show(ArchiveShelf.Events, EventCatalog.All.Length - 1);
            Frame("last");

            // A volume scrolls to the entry that was picked.
            archive.Show(ArchiveShelf.Manual, EventDocs.Guide.Length - 1);
            Frame("manual-last");

            // Window sits inside the screen and its body needs no outer scroll (the panes carry it).
            Rect r = root.rect;
            Check(r.height <= units.y - 2f * 40f + 1f && r.width <= units.x - 2f * 40f + 1f,
                "archive window " + r.size + " must fit the screen " + units + " with a margin");
            float bodyH = r.height - 30f - AvGridTokens.Footer;
            Check(window.Body.ContentHeight <= bodyH + 1f,
                "archive body content " + window.Body.ContentHeight.ToString("0") + " must fit its viewport " + bodyH.ToString("0"));
            Check(archive.transform.parent == rig.Canvas.transform && archive.GetComponentInParent<Canvas>() != null,
                "the archive must live under a canvas, not become a root canvas of its own");

            // The shelf tabs are clickable.
            Transform manualTab = Array.Find(root.GetComponentsInChildren<Transform>(true), t => t.name == "Shelf MANUAL");
            archive.Show(ArchiveShelf.Aircraft);
            manualTab.GetComponentInChildren<AvHit>().OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            for (int i = 0; i < 3; i++) window.Ticker.TickNow();
            Check(Array.Exists(root.GetComponentsInChildren<TMP_Text>(false), t => t.text == "READING A DISPATCH"),
                "the MANUAL tab must open the field manual");

            // Closing lets go of the model viewer and the cards.
            archive.Show(ArchiveShelf.Aircraft);
            Check(Field<EventAircraftPreview>(archive, "preview") != null, "a unit card must hold a model viewer while it is up");
            archive.Close();
            Check(Field<EventAircraftPreview>(archive, "preview") == null && library.Count(ArchiveShelf.Aircraft) == 0,
                "closing the archive must release the model viewer and drop the native cards");
        }
        finally
        {
            if (archive != null) Object.DestroyImmediate(archive.gameObject);
            DisposeFixtures();
            Encyclopedia.i = null;
            rig.Dispose();
        }
    }

    private static AvPart FindPanes(AvFlow flow)
    {
        var lines = (List<AvPart[]>)typeof(AvFlow).GetField("lines", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(flow);
        foreach (AvPart[] line in lines) foreach (AvPart p in line) if (p is ArchivePanesPart) return p;
        return null;
    }

    /// <summary>
    /// The archive's parts are arranged by hand, so two pieces of text could be placed over each other without any
    /// flow noticing. No two visible texts under <paramref name="root"/> may share ink.
    /// </summary>
    private static void CheckCollisions(RectTransform root, string where)
    {
        var rects = new List<Rect>();
        var texts = new List<TMP_Text>();
        var corners = new Vector3[4];
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || !CanvasOn(t) || t.name.StartsWith("Icon")) continue;
            t.ForceMeshUpdate();
            Bounds b = t.textBounds;
            if (b.size.x <= 0f || b.size.y <= 0f) continue;
            // Text bounds are in the text's own space; the window is unrotated, so two corners place them.
            Vector3 min = t.transform.TransformPoint(b.min), max = t.transform.TransformPoint(b.max);
            Rect ink = Rect.MinMaxRect(min.x + 1f, min.y + 1f, max.x - 1f, max.y - 1f);
            // Only what a scrolling pane lets through counts: text scrolled out of its viewport is not drawn.
            foreach (RectMask2D mask in t.GetComponentsInParent<RectMask2D>())
            {
                ((RectTransform)mask.transform).GetWorldCorners(corners);
                ink = Rect.MinMaxRect(Mathf.Max(ink.xMin, corners[0].x), Mathf.Max(ink.yMin, corners[0].y),
                    Mathf.Min(ink.xMax, corners[2].x), Mathf.Min(ink.yMax, corners[2].y));
            }
            if (ink.width <= 0f || ink.height <= 0f) continue;
            rects.Add(ink);
            texts.Add(t);
        }
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
                if (rects[i].Overlaps(rects[j]))
                    Failures.Add(where + ": '" + texts[i].text + "' (" + texts[i].name + ") is drawn over '" + texts[j].text + "' (" + texts[j].name + ")");
    }

    // ---- Gate ----------------------------------------------------------------------------------------------

    private static void Check(bool condition, string message) { if (!condition) Failures.Add(message); }

    private static void Click(AvControl control)
    {
        if (control == null) { Failures.Add("click: control not found"); return; }
        control.GetComponentInChildren<AvHit>(true).OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
    }

    /// <summary>Lines of a flow, in order: no shown part starts above the previous line's bottom, none is stale.</summary>
    private static void CheckFlow(AvFlow flow, string where)
    {
        checkedFlows++;
        var lines = (List<AvPart[]>)typeof(AvFlow).GetField("lines", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(flow);
        float previousBottom = float.NegativeInfinity;
        foreach (AvPart[] line in lines)
        {
            float lineBottom = float.NegativeInfinity;
            bool any = false;
            foreach (AvPart p in line)
            {
                if (p.Rect == null) continue;
                bool shown = p.Rect.gameObject.activeSelf;
                if (shown != p.PlacedShown) Failures.Add(where + ": " + p.Rect.name + " visibility changed without a re-layout");
                if (!shown) continue;
                any = true;
                float top = -p.Rect.anchoredPosition.y, bottom = top + p.Rect.rect.height;
                if (top < previousBottom - 0.5f)
                    Failures.Add(where + ": " + p.Rect.name + " (top " + top.ToString("0") + ") overlaps the line above (bottom " + previousBottom.ToString("0") + ")");
                float now = p.Measure(p.PlacedWidth);
                if (Mathf.Abs(now - p.PlacedHeight) > 0.6f)
                    Failures.Add(where + ": " + p.Rect.name + " is stale (measures " + now.ToString("0.0") + ", placed " + p.PlacedHeight.ToString("0.0") + ")");
                lineBottom = Mathf.Max(lineBottom, bottom);
            }
            if (any) previousBottom = lineBottom;
        }
    }

    private static void Gate(RectTransform root, string where, bool console)
    {
        Color ground = AvStyleHost.FuiColor("ground", Color.black);
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || t.text.Length == 0 || !CanvasOn(t)) continue;
            checkedTexts++;
            t.ForceMeshUpdate();
            Rect r = t.rectTransform.rect;
            bool icon = t.name.StartsWith("Icon");
            if (!icon)
            {
                Bounds b = t.textBounds;
                if (b.size.x > r.width + 1.5f)
                    Failures.Add(where + ": overflows width (" + b.size.x.ToString("0") + " > " + r.width.ToString("0") + ") '" + t.text + "'");
                if (b.size.y > r.height + 1.5f)
                    Failures.Add(where + ": overflows height (" + b.size.y.ToString("0") + " > " + r.height.ToString("0") + ") '" + t.text + "'");
                if (t.fontSize < AvTypeScale.Floor - 0.01f)
                    Failures.Add(where + ": below the 11 px floor (" + t.fontSize.ToString("0.0") + ") '" + t.text + "'");
            }
            ScrollRect scroll = t.GetComponentInParent<ScrollRect>();
            if (scroll != null)
            {
                var corners = new Vector3[4];
                t.rectTransform.GetWorldCorners(corners);
                RectTransform scrollRect = (RectTransform)scroll.transform;
                float right = scrollRect.InverseTransformPoint(corners[2]).x;
                float limit = scrollRect.rect.width - AvGridTokens.Pad - AvGridTokens.Gutter + 0.5f;
                if (right > limit) Failures.Add(where + ": enters the gutter (" + right.ToString("0") + " > " + limit.ToString("0") + ") '" + t.text + "'");
            }
            if (!icon && t.color.a > 0.5f)
            {
                Color back = BackgroundOf(t, ground);
                float contrast = Rgba.Contrast(t.color.ToRgba().WithAlpha(1f).Over(back.ToRgba()), back.ToRgba());
                if (contrast < 4.5f) Failures.Add(where + ": contrast " + contrast.ToString("0.00") + " for '" + t.text + "' (" + t.name + ")");
            }
        }
        if (console)
            foreach (AvControl tab in root.GetComponentsInChildren<AvControl>(true))
                if (tab.transform.parent != null && tab.transform.parent.name == "Tabs" && tab.transform.Find("Label") != null
                    && tab.GetComponentsInChildren<TMP_Text>(true).Length < 2)
                    Failures.Add(where + ": tab without icon " + tab.name);
        foreach (Transform s in root.GetComponentsInChildren<Transform>(true))
            if (s.name.StartsWith("Section ") && s.Find("Icon None") != null)
                Failures.Add(where + ": section without icon " + s.name);
    }

    private static Color BackgroundOf(TMP_Text t, Color ground)
    {
        for (Transform x = t.transform.parent; x != null; x = x.parent)
        {
            foreach (Transform child in x)
            {
                var f = child.GetComponent<AvFrame>();
                if (f != null && f.enabled && f.Fill && f.FillColor.a > 0.35f && child != t.transform)
                {
                    Rgba o = f.FillColor.ToRgba().Over(ground.ToRgba());
                    return new Color(o.R, o.G, o.B);
                }
            }
            var img = x.GetComponent<Image>();
            if (img != null && img.enabled && img.color.a > 0.35f)
                return new Color(img.color.r, img.color.g, img.color.b);
        }
        return ground;
    }
}
#endif
