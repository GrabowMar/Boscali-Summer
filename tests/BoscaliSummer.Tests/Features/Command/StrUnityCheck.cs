#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Presentation;
using BoscaliSummer.Tests.Features.Command;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

/// <summary>
/// Offline render check of the Station Board (annex 05 P12). It builds the real panel from the production sources, binds it to the model the real builder
/// makes from each wire-legal fixture (StrFixtures), and renders the three tabs at 480 wide and 596, 896 and 1500 tall. The run FAILS on text overflow, 11 px
/// floor breaches, gutter entry, contrast below 4.5, overlapping parts or texts, a missing section icon, a zero percentage or a non-dash unknown readout,
/// an id that does not resolve, a disabled control with no reason, a banner that repeats another state's words, an effort amber off the mockup's, or a
/// string, id or fold line of StrMockupSpec.json that the render misses. RunSelfTest injects an oversized string and a missing id and must FAIL.
/// Game adapters the panel compiles against live in SettingsUnityStubs.cs; the real hub, reader and map files are never compiled (the panel binds IStrHub).
/// </summary>
public static class StrUnityCheck
{
    private const float Width = AvTokens.PanelWidth;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly float[] Heights = { 596f, 896f, 1500f };
    private static readonly string[] PageNames = { "board", "sitrep", "log" };

    private static int captures, fixturesRendered, pressesChecked;
    private static readonly List<string> Notes = new List<string>();

    public static void Run() => Execute(false);

    public static void RunSelfTest() => Execute(true);

    private static void Execute(bool selfTest)
    {
        try
        {
            if (!EnsureTmpEssentials(() => Execute(selfTest))) return;
            SetExecutablePath("StrCheck.exe");
            AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvStyleHost.SetTheme(AvThemeId.Portal);
            // A stand-in for the game palette so the render reads like the game (blue own, red hostile); in the game these come from ThemeManager.
            NuclearOption.UIStyleSystem.ThemeManager.Override = new NuclearOption.UIStyleSystem.Theme
            {
                ColorTheme = new NuclearOption.UIStyleSystem.Palette
                {
                    AllClear = new Color(0.30f, 1.00f, 0.35f), MapIconFriendly = new Color(0.31f, 0.62f, 1.00f), HudUnitFriendly = new Color(0.31f, 0.62f, 1.00f),
                    HudUnitNeutral = new Color(0.50f, 0.50f, 0.50f), HudUnitHostile = new Color(1.00f, 0.33f, 0.28f), HudUnitSelected = new Color(0.30f, 1.00f, 0.35f),
                    Warning = new Color(1.00f, 0.55f, 0.20f), Alert = new Color(1.00f, 0.18f, 0.12f),
                },
            };
            new GameObject("Events", typeof(EventSystem));
            Directory.CreateDirectory("renders");
            SpecFile spec = JsonUtility.FromJson<SpecFile>(File.ReadAllText("StrMockupSpec.json"));
            Check(spec != null && spec.fixtures != null && spec.fixtures.Length > 0, "StrMockupSpec.json did not parse");

            if (selfTest) { SelfTest(spec); return; }

            foreach (string fixture in StrFixtures.Names)
                foreach (float height in Heights)
                    RenderFixture(fixture, height, spec);
            EmptyStatesDiffer();
            PressesReachTheVerbs();
            EffortInk(spec);

            if (PanelGate.Failures.Count > 0)
            {
                File.WriteAllText("result.txt", "FAIL: " + PanelGate.Failures.Count + " gate failure(s)\n" + string.Join("\n", PanelGate.Failures));
                EditorApplication.Exit(1);
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("PASS: the real STR Station Board rendered offline on kit v2 for " + fixturesRendered + " fixture x height builds; gate clean (" + PanelGate.CheckedTexts +
                " texts checked: no overflow, no 11px-floor breach, no gutter entry, contrast >= 4.5, no part or text overlap, honest unknowns, every glyph in the font, every id resolves and every disabled control says why, " + pressesChecked + " presses by id reach the verb their button stands for, fold at " + spec.foldLine + ", mockup spec, effort ink).");
            report.AppendLine(captures + " captures at 480 wide: BOARD at 596, 896 and 1500 for all " + StrFixtures.Names.Length + " fixtures, SITREP and LOG at 896.");
            report.AppendLine("Content is bound after Finish() through the harness hub (the real builder over StrFixtures feeds) and advanced with Ticker.TickNow(), as the game does.");
            report.AppendLine("Renders (path | bytes | note):");
            foreach (string note in Notes) report.AppendLine(note);
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

    // ------------------------------------------------------------------ spec

    [Serializable] public class SpecFixture { public string name; public int page; public string[] strings = new string[0]; public string[] ids = new string[0]; public string[] foldRects = new string[0]; }
    [Serializable] public class SpecInk { public string name; public string hex; public float maxDeltaE; public string rect; }
    [Serializable] public class SpecFile { public float foldLine; public SpecFixture[] fixtures; public SpecInk[] ink; }

    // ------------------------------------------------------------------ the harness hub

    private sealed class FakeVerbs : IStrVerbs
    {
        public readonly List<string> Sent = new List<string>();
        public bool Pick(int proposalId, int revision) { Sent.Add("pick " + proposalId); return true; }
        public bool Cancel(int operationId, int revision) { Sent.Add("cancel " + operationId); return true; }
        public bool Posture(TheaterWarPosture posture) { Sent.Add("posture " + posture); return true; }
        public void Accept(int contractId) => Sent.Add("accept " + contractId);
        public bool SendWing(int contractId, out string answer) { Sent.Add("wing " + contractId); answer = "WING ORDER SENT"; return true; }
        public bool Recall(out string answer) { Sent.Add("recall"); answer = "WING RECALLED"; return true; }
    }

    /// <summary>The panel's hub in the harness: the real builder over a fixture's feeds, the real gate and runner over a recording fake of the verbs.</summary>
    private sealed class StrTestHub : IStrHub
    {
        private readonly StrFeeds feeds;
        private readonly StrViewModel model = new StrViewModel();
        private readonly StrOrdinals plat = new StrOrdinals("P"), sort = new StrOrdinals("S");
        private readonly FakeVerbs verbs = new FakeVerbs();
        private StrUiState ui;

        public StrTestHub(string fixture)
        {
            feeds = StrFixtures.Feeds(fixture);
            Tick();
        }

        public List<string> Sent => verbs.Sent;
        public StrViewModel Current => model;
        public int Revision => model.Revision;
        public StrUiState Ui => ui;

        private void Tick() => StrViewBuilder.Build(model, in feeds, in ui, plat, sort, StrFixtures.Now);

        public void Select(StrItemId id, bool focusMap) { ui.Selected = id; Tick(); }
        public void Hover(StrItemId id) { ui.Hover = id; Tick(); }
        public void Unhover(StrItemId id) { if (ui.Hover == id) { ui.Hover = StrItemId.None; Tick(); } }
        public void ClearSelection() { ui.Selected = StrItemId.None; Tick(); }

        public StrAct Run(StrActionKind kind, StrItemId id, int arg = 0)
        {
            StrAct act = StrActions.Run(kind, id, arg, model, verbs, ref ui, StrFixtures.Now);
            Tick();
            return act;
        }

        public void SetLayer(StrLayers layer, bool on) { }
    }

    // ------------------------------------------------------------------ one fixture at one height

    private sealed class Built
    {
        public GameObject Canvas;
        public StrMfdPanel Panel;
        public StrTestHub Hub;
        public AvConsole Console => Panel.Console;
    }

    private static Built Build(string fixture, float height)
    {
        var canvasObject = new GameObject("StrCanvas", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(Width, height);
        var hub = new StrTestHub(fixture);
        StrMfdPanel panel = StrMfdPanel.BuildForTest((RectTransform)canvasObject.transform, height, hub);
        panel.BindNow();
        var built = new Built { Canvas = canvasObject, Panel = panel, Hub = hub };
        Settle(built.Console);
        return built;
    }

    private static void Settle(AvConsole console)
    {
        // Offline, time does not advance: the ticker's fast tick stands in for the game's frames (relayouts run on it).
        for (int i = 0; i < 6; i++) console.Ticker.TickNow();
    }

    private static void RenderFixture(string fixture, float height, SpecFile spec)
    {
        Built b = Build(fixture, height);
        fixturesRendered++;
        AvConsole con = b.Console;
        for (int page = 0; page < 3; page++)
        {
            con.SetPage(page);
            Settle(con);
            string where = fixture + "@" + height + "/" + PageNames[page];
            GatePage(b, where, page);
            SpecPage(b, fixture, page, height, where, spec);
            if (page == 0 || Mathf.Approximately(height, 896f))
            {
                string file = "renders/" + fixture + "-" + PageNames[page] + "-" + height + ".png";
                string path = Capture(b.Canvas, height, file);
                if (page == 0 && Mathf.Approximately(height, 896f)) Notes.Add(path + " | " + new FileInfo(path).Length + " bytes | " + fixture + " BOARD at 896");
                if (page == 0 && height < 700f)
                {
                    ScrollRect scroll = b.Canvas.GetComponentInChildren<ScrollRect>();
                    if (scroll != null && scroll.content.rect.height > scroll.viewport.rect.height + 1f)
                    {
                        scroll.verticalNormalizedPosition = 0f;
                        Capture(b.Canvas, height, "renders/" + fixture + "-board-" + height + "-bottom.png");
                        scroll.verticalNormalizedPosition = 1f;
                    }
                }
            }
        }
        Object.DestroyImmediate(b.Canvas);
    }

    /// <summary>Overflow, floor, gutter, contrast, overlap, icons, collisions, honesty, reasons and unique ids of the page in view.</summary>
    private static void GatePage(Built b, string where, int page)
    {
        AvConsole con = b.Console;
        PanelGate.GateTexts(con.Root, where, PanelGate.GutterLeft, con.Root);
        PanelGate.PartOverlap(con.Page(page).Content, where);
        PanelGate.TextCollisions(con.Page(page).Content, con.Root, where);
        PanelGate.SectionIcons(con.Root, where);
        PanelGate.Honesty(con.Root, where, where.StartsWith("unknown@"));
        PanelGate.Glyphs(con.Root, where);
        Reasons(b, where);
    }

    /// <summary>Every registered id is legal and resolves; no two parts claim one id; every disabled control on screen has a reason.</summary>
    private static void Reasons(Built b, string where)
    {
        StrControls controls = b.Panel.Controls;
        if (controls.Collisions > 0) PanelGate.Fail(where, controls.Collisions + " control id(s) claimed by two parts");
        foreach (string id in controls.Ids)
        {
            if (!StrControlIds.IsValid(id)) PanelGate.Fail(where, "id outside the grammar: " + id);
            StrAct gate = controls.Gate(id);
            if (!gate.Allowed && gate.Text.Length < 8) PanelGate.Fail(where, "control " + id + " is refused with no reason");
        }
        foreach (AvControl c in b.Console.Root.GetComponentsInChildren<AvControl>(false))
        {
            if (!c.isActiveAndEnabled || !CanvasOn(c) || c.Interactable) continue;
            if (string.IsNullOrEmpty(c.Help) || c.Help.Length < 8) PanelGate.Fail(where, "disabled control '" + c.Label + "' has no reason");
        }
    }

    /// <summary>The spec's strings and ids for this fixture and page, and its fold line at the 596 px bay.</summary>
    private static void SpecPage(Built b, string fixture, int page, float height, string where, SpecFile spec)
    {
        foreach (SpecFixture f in spec.fixtures)
        {
            if (f.name != fixture || f.page != page) continue;
            List<string> texts = PanelGate.ActiveTexts(b.Console.Root);
            foreach (string s in f.strings)
                if (!texts.Exists(t => t.Contains(s))) PanelGate.Fail(where, "missing string '" + s + "'");
            foreach (string id in f.ids)
                if (!b.Panel.Controls.Has(id)) PanelGate.Fail(where, "missing id '" + id + "'");
            if (!Mathf.Approximately(height, 596f)) continue;
            foreach (string path in f.foldRects)
            {
                RectTransform rect = PanelGate.FindRect(b.Console.Root, path);
                if (rect == null) { PanelGate.Fail(where, "fold element not on screen: " + path); continue; }
                float bottom = PanelGate.BottomFromTop(rect, b.Console.Root);
                if (bottom > spec.foldLine + 0.5f) PanelGate.Fail(where, "fold: '" + path + "' ends at " + bottom.ToString("0") + " below " + spec.foldLine);
            }
        }
    }

    // ------------------------------------------------------------------ gates across fixtures

    /// <summary>Each empty or reduced state says its own thing: no two share a headline or a flavour line, and none repeats another's chip with its headline.</summary>
    private static void EmptyStatesDiffer()
    {
        string[] states = { "assessing", "noopening", "reduced", "staffoff", "hostlacks", "nofaction", "waiting", "noanswer" };
        var heads = new Dictionary<string, string>();
        var flavours = new Dictionary<string, string>();
        var pairs = new Dictionary<string, string>();
        foreach (string s in states)
        {
            StrViewModel vm = StrFixtures.Model(s);
            if (vm.Banner.Chip.Length < 4 || vm.Banner.Head.Length < 8 || vm.FooterNote.Length < 12) PanelGate.Fail("empty-states", s + " has no banner words");
            Distinct(heads, vm.Banner.Head, s, "headline");
            Distinct(flavours, vm.FooterNote, s, "flavour");
            Distinct(pairs, vm.Banner.Chip + "|" + vm.Banner.Head, s, "chip and headline");
        }
    }

    private static void Distinct(Dictionary<string, string> seen, string key, string state, string what)
    {
        if (seen.TryGetValue(key, out string other)) PanelGate.Fail("empty-states", state + " repeats " + other + "'s " + what + " '" + key + "'");
        else seen[key] = state;
    }

    /// <summary>
    /// The click path minus the pointer, end to end: a press by id goes through the panel's registry, the real gate and runner, and reaches the verb the button
    /// stands for (the fake records it); a refused press reaches none. The pointer half is the in-game Probe and the owner's click-through.
    /// </summary>
    private static void PressesReachTheVerbs()
    {
        Built p = Build("proposals", 896f);
        ExpectVerb(p, "prop.2.pick", "pick ");
        ExpectVerb(p, "posture.bold", "posture Bold");
        Object.DestroyImmediate(p.Canvas);

        Built r = Build("rich", 896f);
        ExpectVerb(r, "sortie.2.accept", "accept ");
        ExpectVerb(r, "sortie.1.wing", "wing ");
        ExpectVerb(r, "front.1.redirect", "pick ");
        ExpectVerb(r, "replan", null);               // the first press only arms
        ExpectVerb(r, "replan", "cancel ");          // the second inside the window calls the operation off
        Object.DestroyImmediate(r.Canvas);

        Built t = Build("wing-tasked", 896f);
        int tasked = -1;
        for (int i = 0; i < t.Hub.Current.SortieCount; i++)
        {
            StrSortieRow row = t.Hub.Current.Sorties[i];
            if (row.HasWing && (row.WingState == WingSortieState.Tasked || row.WingState == WingSortieState.OnStation)) tasked = i;
        }
        if (tasked < 0) PanelGate.Fail("press", "the wing-tasked fixture has no tasked sortie");
        else ExpectVerb(t, StrControlIds.Sortie(tasked + 1, "wing"), "recall");
        Object.DestroyImmediate(t.Canvas);

        Built c = Build("client", 896f);
        c.Hub.Sent.Clear();
        StrControls.PressResult refused = c.Panel.Controls.Press("sortie.1.wing");
        if (refused.Ok || c.Hub.Sent.Count != 0 || refused.Act.Why != StrWhy.HostOnly)
            PanelGate.Fail("press", "a client's SEND WING must be refused with HostOnly and reach no verb: ok=" + refused.Ok + " why=" + refused.Act.Why + " sent=[" + string.Join(", ", c.Hub.Sent) + "]");
        Object.DestroyImmediate(c.Canvas);
    }

    /// <summary>Presses <paramref name="id"/>; the fake must have recorded exactly one verb starting with <paramref name="verb"/>, or none when it is null.</summary>
    private static void ExpectVerb(Built b, string id, string verb)
    {
        b.Hub.Sent.Clear();
        pressesChecked++;
        StrControls.PressResult res = b.Panel.Controls.Press(id);
        bool right = verb == null ? b.Hub.Sent.Count == 0 : b.Hub.Sent.Count == 1 && b.Hub.Sent[0].StartsWith(verb, StringComparison.Ordinal);
        if (!res.Ok || !right)
            PanelGate.Fail("press " + id, "expected " + (verb == null ? "no verb (armed only)" : "one '" + verb.Trim() + "' verb") + ", got ok=" + res.Ok + " why=" + res.Act.Why + " '" + res.Act.Text + "' sent=[" + string.Join(", ", b.Hub.Sent) + "]");
    }

    /// <summary>The effort amber within dE76 of the mockup's, as the sheet defines it and as the render shows it on the slab.</summary>
    private static void EffortInk(SpecFile spec)
    {
        foreach (SpecInk ink in spec.ink)
        {
            Color target = Hex(ink.hex);
            Color actual = StrInk.Of(StrInkRole.Effort);
            float de = DeltaE76(actual, target);
            if (de > ink.maxDeltaE) PanelGate.Fail("ink", ink.name + ": the sheet's effort is dE76 " + de.ToString("0.0") + " from " + ink.hex);
            string rendered = "renders/rich-board-896.png";
            Built b = Build("rich", 896f);
            b.Console.SetPage(0);
            Settle(b.Console);
            string path = Capture(b.Canvas, 896f, "renders/ink-probe.png");
            RectTransform slab = PanelGate.FindRect(b.Console.Root, ink.rect);
            if (slab == null) { PanelGate.Fail("ink", "no " + ink.rect + " on the rich board to sample"); Object.DestroyImmediate(b.Canvas); continue; }
            int hits = 0, total = 0;
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(path));
            var corners = new Vector3[4];
            slab.GetWorldCorners(corners);
            Vector3 lo = b.Console.Root.InverseTransformPoint(corners[0]), hi = b.Console.Root.InverseTransformPoint(corners[2]);
            int x0 = Mathf.FloorToInt(Mathf.Min(lo.x, hi.x) * 2f), x1 = Mathf.CeilToInt(Mathf.Max(lo.x, hi.x) * 2f);
            int y0 = Mathf.FloorToInt((896f + Mathf.Min(lo.y, hi.y)) * 2f), y1 = Mathf.CeilToInt((896f + Mathf.Max(lo.y, hi.y)) * 2f);
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(tex.height, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(tex.width, x1); x++)
                {
                    total++;
                    if (DeltaE76(tex.GetPixel(x, y), target) <= ink.maxDeltaE) hits++;
                }
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(b.Canvas);
            float share = total > 0 ? hits / (float)total : 0f;
            if (share < 0.25f) PanelGate.Fail("ink", ink.name + ": only " + (share * 100f).ToString("0") + "% of the " + ink.rect + " slab is within dE76 " + ink.maxDeltaE + " of " + ink.hex + " in the render");
            Notes.Add(rendered + " | effort ink | sheet dE76 " + de.ToString("0.0") + ", slab " + (share * 100f).ToString("0") + "% within " + ink.maxDeltaE);
        }
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }

    // CIE76 in D65 Lab from sRGB.
    private static float DeltaE76(Color a, Color b)
    {
        Vector3 la = Lab(a), lb = Lab(b);
        return (la - lb).magnitude;
    }

    private static Vector3 Lab(Color c)
    {
        float r = Linear(c.r), g = Linear(c.g), bl = Linear(c.b);
        float x = (r * 0.4124f + g * 0.3576f + bl * 0.1805f) / 0.95047f;
        float y = r * 0.2126f + g * 0.7152f + bl * 0.0722f;
        float z = (r * 0.0193f + g * 0.1192f + bl * 0.9505f) / 1.08883f;
        float fx = F(x), fy = F(y), fz = F(z);
        return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
    }

    private static float Linear(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
    private static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;

    // ------------------------------------------------------------------ self test

    /// <summary>The negative control: an oversized string and a missing id must fail the gates the real run uses.</summary>
    private static void SelfTest(SpecFile spec)
    {
        Built b = Build("rich", 596f);
        b.Console.SetPage(0);
        Settle(b.Console);
        TMP_Text victim = null;
        foreach (TMP_Text t in b.Console.Root.GetComponentsInChildren<TMP_Text>(false))
            if (t.name == "Title" && t.isActiveAndEnabled && t.text.Length > 0 && t.GetComponentInParent<AvControl>() == null) { victim = t; break; }
        Check(victim != null, "self-test: no hero title to inflate");
        victim.text = new string('W', 400);
        // The missing id: the sortie ticket the spec names no longer registers (what a dropped binding would do).
        b.Panel.Controls.Clear();
        GatePage(b, "selftest@596/board", 0);
        SpecPage(b, "rich", 0, 596f, "selftest@596/board", spec);
        Object.DestroyImmediate(b.Canvas);
        // The wrong verb: a pick press that is expected to reach a recall must be reported.
        Built q = Build("proposals", 596f);
        ExpectVerb(q, "prop.2.pick", "recall");
        Object.DestroyImmediate(q.Canvas);
        bool overflow = PanelGate.Failures.Exists(f => f.Contains("overflows width")), missing = PanelGate.Failures.Exists(f => f.Contains("missing id"));
        bool wrongVerb = PanelGate.Failures.Exists(f => f.StartsWith("press prop.2.pick: expected one 'recall' verb", StringComparison.Ordinal));
        File.WriteAllText("result.txt", "FAIL: self-test: the gate reported " + PanelGate.Failures.Count + " failure(s), overflow detected=" + overflow + ", missing id detected=" + missing + ", wrong verb detected=" + wrongVerb + "\n" + string.Join("\n", PanelGate.Failures));
        EditorApplication.Exit(1);
    }

    // ------------------------------------------------------------------ capture

    private static string Capture(GameObject canvasObject, float height, string file)
    {
        Canvas.ForceUpdateCanvases();
        // Offline executeMethod does not advance time: end the page-change scan cover before capturing the settled page, otherwise the real bundle's
        // effect hides every body despite a passing text gate.
        foreach (StrMfdPanel panel in canvasObject.GetComponentsInChildren<StrMfdPanel>())
            ((Image)typeof(AvConsole).GetField("scanCover", Private).GetValue(panel.Console)).enabled = false;
        foreach (AvReveal reveal in canvasObject.GetComponentsInChildren<AvReveal>(true)) reveal.Finish();
        foreach (ScrollRect scroll in canvasObject.GetComponentsInChildren<ScrollRect>()) scroll.Rebuild(CanvasUpdate.PostLayout);
        foreach (TMP_Text text in canvasObject.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvasObject.transform);

        Camera camera = OrthoCamera("StrCamera", height * 0.5f, new Color(0.04f, 0.07f, 0.06f), new Vector3(0f, 0f, -10f));
        string path = Path.GetFullPath(file);
        CapturePng(camera, Mathf.RoundToInt(Width * 2f), Mathf.RoundToInt(height * 2f), path);
        Object.DestroyImmediate(camera.gameObject);
        var info = new FileInfo(path);
        Check(info.Exists && info.Length > 0, "render produced no bytes: " + file);
        captures++;
        return path;
    }
}

namespace BoscaliSummer.Modules.Command.Configuration
{
    internal sealed class CommandSettings
    {
        public readonly Setting<bool> Enabled = new Setting<bool>(true);
        internal sealed class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    }
}
#endif
