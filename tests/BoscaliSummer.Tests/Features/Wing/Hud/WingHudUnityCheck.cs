#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// Real WingHudPanel.Build/Refresh, with a native Component adapter and synthetic
// display facts. No copied widget, flight tick, service activation or mission.
public static class WingHudUnityCheck
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const string Wing = "BoscaliSummer.Modules.Wing.";
    private static readonly Assembly Mod = typeof(AvConsole).Assembly;
    private static readonly List<string> failures = new List<string>();
    private static readonly List<string> measurements = new List<string> { "capture\ttext\tfont\twidth\theight\tpreferredWidth\tpreferredHeight\tmeshMinX\tmeshMaxX\ttruncated" };
    private static readonly List<string> captures = new List<string> { "file\twidth\theight" };
    private static readonly List<string> layout = new List<string> { "capture\ttext\tpanelHeight\tslotMinX\tslotMaxX\tslotMinY\tslotMaxY" };
    private static int assertions;

    public static void Run()
    {
        try
        {
            if (!EnsureTmpEssentials(Run)) return;
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvBundle.Load(Debug.Log);
            Check(AvBundle.Available && AvIcons.Available, "Production UI bundle must load");
            if (!AvBundle.Available || !AvIcons.Available) throw new InvalidOperationException("Production UI assets unavailable");
            foreach (string fontName in new[] { "NOA Rajdhani Medium SDF", "NOA JetBrains Mono Regular SDF" })
                Render(fontName);
            File.WriteAllLines("failures.txt", failures);
            File.WriteAllLines("text-measurements.tsv", measurements);
            File.WriteAllLines("capture-manifest.tsv", captures);
            File.WriteAllLines("layout-measurements.tsv", layout);
            File.WriteAllText("result.txt", (failures.Count == 0 ? "PASS: " : "FAIL: ") + "WING HUD " + (captures.Count - 1) +
                " production-widget captures; " + assertions + " native-adapter, text, layout and input-isolation checks; " + failures.Count +
                " failures. Inactive source-registered CombatHUD adapter; real Build/Refresh; synthetic formation/AP/member facts; no game or flight lifecycle.\n");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }
        catch (Exception error)
        {
            File.WriteAllText("result.txt", "FAIL: WING HUD native production preview " + error);
            File.WriteAllLines("failures.txt", failures);
            File.WriteAllLines("text-measurements.tsv", measurements);
            File.WriteAllLines("capture-manifest.tsv", captures);
            File.WriteAllLines("layout-measurements.tsv", layout);
            Debug.LogException(error); EditorApplication.Exit(1);
        }
    }

    private static void Render(string fontName)
    {
        var nativeHost = new GameObject("Inactive native HUD adapter", typeof(RectTransform));
        nativeHost.SetActive(false);
        var native = nativeHost.AddComponent<WingHudNativeAdapter>();
        Check(native != null, "Source-registered native CombatHUD subclass must attach");
        if (native == null) throw new InvalidOperationException("Native CombatHUD subclass attachment failed; no dummy widget fallback");
        MonoScript script = MonoScript.FromMonoBehaviour(native);
        Check(script != null && script.GetClass() == typeof(WingHudNativeAdapter), "Native adapter must have a registered source script");
        TMP_FontAsset font = AvBundle.Font(fontName);
        Check(font != null, "Preview native template font must exist: " + fontName);
        TMP_Text template = AvText.Make((RectTransform)nativeHost.transform, "Native template", AvTextRole.ProseSmall, "NATIVE HUD TEMPLATE");
        template.font = font;
        Check(native.GetComponentInChildren<TMP_Text>(true) == template, "Real native Component must find its inactive child font template");
        var canvasObject = new GameObject("Wing HUD preview", typeof(RectTransform), typeof(Canvas));
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        var canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.sizeDelta = new Vector2(600f, 300f);
        object widget = New(Wing + "Presentation.WingHudPanel");
        Call(widget, "Build", native, canvas);
        object wing = WingState(), ap = AutopilotState();
        string suffix = fontName.Contains("Mono") ? "mono" : "condensed";
        float sevenMemberHeight = 0f;
        foreach (string state in new[] { "AP-ONLY", "WING-THREE", "WING-SEVEN", "OVERRIDES", "ROTARY", "SHRINK", "CLEAR" })
        {
            IList members = (IList)Field(wing, "Members"); members.Clear();
            int count = state == "AP-ONLY" || state == "CLEAR" ? 0 : state == "WING-THREE" || state == "SHRINK" ? 3 : 7;
            for (int i = 0; i < count; i++) members.Add(MemberState(i));
            object selection = Field(wing, "<Selection>k__BackingField");
            Call(selection, "Select", state == "ROTARY" ? "staggered-trail" : "finger-four-right");
            Set(selection, "Spacing", EnumValue("SpacingPreset", state == "ROTARY" ? "Spread" : "Standard"));
            object session = Field(ap, "Session");
            Set(session, "<LateralOverride>k__BackingField", state == "OVERRIDES");
            Set(session, "<VerticalOverride>k__BackingField", state == "OVERRIDES");
            Call(widget, "Refresh", count == 0 ? null : wing, state == "WING-THREE" || state == "SHRINK" || state == "CLEAR" ? null : ap);
            RectTransform root = (RectTransform)Field(widget, "root");
            AvLay.Place(root, 24f, 24f, root.rect.width, root.rect.height);
            Canvas.ForceUpdateCanvases();
            string capture = "WINGHUD-" + state + "-" + suffix;
            Gate(widget, root, count, capture, font);
            if (state == "OVERRIDES") Check(((TMP_Text)Field(widget, "autopilot")).text == "AP (NAV 16/16)  (ALT 12500)  SPD 1260", capture + ": both override flags and full navigation/altitude/speed facts must render");
            if (state == "WING-SEVEN") sevenMemberHeight = root.rect.height;
            if (state == "SHRINK" || state == "CLEAR") Check(root.rect.height < sevenMemberHeight, capture + ": card must shrink after members/AP clear");
            Capture(canvasObject, capture + ".png", 600f, 300f);
        }
        Object.DestroyImmediate(nativeHost); Object.DestroyImmediate(canvasObject);
    }

    private static object WingState()
    {
        object wing = New(Wing + "Runtime.WingService");
        IList catalog;
        using (Stream stream = Mod.GetManifestResourceStream("WingCommand.formations.json"))
        using (var reader = new StreamReader(stream))
            catalog = (IList)CallStatic("FormationCatalog", "Parse", reader.ReadToEnd(), new List<string>());
        Set(wing, "<Selection>k__BackingField", New(Wing + "Domain.FormationSelection", catalog, "finger-four-right"));
        object formation = FormatterServices.GetUninitializedObject(TypeOf(Wing + "Domain.FormationWing"));
        object frame = New(Wing + "Domain.WingFrame"); Set(frame, "Count", 7);
        Array slots = (Array)Field(frame, "Slots");
        for (int i = 0; i < 7; i++)
        {
            object slot = Activator.CreateInstance(slots.GetType().GetElementType());
            object pos = New("BoscaliSummer.Core.Math.Vec3", i == 1 ? 12345f : 60f * i, 0f, 0f);
            object zero = New("BoscaliSummer.Core.Math.Vec3", 0f, 0f, 0f);
            Set(slot, "Ref", New(Wing + "Domain.RefState", pos, zero, zero)); slots.SetValue(slot, i);
        }
        Set(formation, "Frame", frame); Set(wing, "<Wing>k__BackingField", formation);
        return wing;
    }

    private static object AutopilotState()
    {
        object ap = New(Wing + "Runtime.PlayerAutopilot"), hold = New(Wing + "Domain.HoldSpec");
        Set(hold, "Lateral", EnumValue("LateralHold", "Nav")); Set(hold, "Vertical", EnumValue("VerticalHold", "Altitude"));
        Set(hold, "AltitudeM", 12500f); Set(hold, "Speed", true); Set(hold, "SpeedMps", 350f);
        Set(Field(ap, "Session"), "Spec", hold);
        Set(Field(ap, "Nav"), "<Index>k__BackingField", 15); Set(Field(ap, "Nav"), "<Count>k__BackingField", 16);
        return ap;
    }

    private static object MemberState(int index)
    {
        object member = FormatterServices.GetUninitializedObject(TypeOf(Wing + "Runtime.WingMember"));
        object brain = New(Wing + "Domain.FormationPilot", index, EnumValue("AirframeClass", "FixedWing"));
        Set(member, "Brain", brain); Set(member, "Seat", index);
        string[] phases = { "StationKeep", "Rejoin", "Defend", "React", "Trail", "Rejoin", "HoldOverhead" };
        Call(Field(brain, "Mind"), "Force", EnumValue("BehaviourId", phases[index]));
        object rejoin = Field(brain, "LastRejoin"); Set(rejoin, "FallingBehind", index == 1); Set(brain, "LastRejoin", rejoin);
        object report = New(Wing + "Domain.BindingReport");
        if (index == 2) Set(report, "GcasActive", true);
        if (index == 3) Set(report, "CollisionActive", true);
        if (index == 6) Set(report, "VerticalBy", EnumValue("ConstraintId", "Authority"));
        Set(Field(brain, "Pipeline"), "report", report);
        if (index == 4)
        {
            object recovery = FormatterServices.GetUninitializedObject(TypeOf(Wing + "Domain.RecoveryPilot"));
            Set(recovery, "<Intent>k__BackingField", EnumValue("RecoveryIntent", "Refit")); Set(member, "Recovery", recovery);
        }
        if (index == 5)
        {
            object settle = FormatterServices.GetUninitializedObject(TypeOf(Wing + "Domain.SettlePilot"));
            Set(settle, "<Phase>k__BackingField", EnumValue("SettlePhase", "Down")); Set(member, "Settle", settle);
        }
        object bingo = New(Wing + "Domain.BingoMonitor");
        Set(bingo, "lastFuel", .8f); Set(bingo, "rate", .001f); Set(bingo, "need", .8f - (index == 6 ? 59f : 1300f) * .001f);
        Set(member, "Bingo", bingo);
        return member;
    }

    private static void Gate(object widget, RectTransform root, int count, string where, TMP_FontAsset font)
    {
        TMP_Text[] rows = (TMP_Text[])Field(widget, "rows");
        Check(rows.Length == 7, where + ": widget must use the production seven-member ceiling");
        for (int i = 0; i < count; i++) Check(!string.IsNullOrWhiteSpace(rows[i].text), where + ": active member row must display facts " + i);
        for (int i = count; i < rows.Length; i++) Check(rows[i].text == "", where + ": stale member row must clear " + i);
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>()) Check(!graphic.raycastTarget, where + ": HUD widget must stay input-transparent");
        var background = (Graphic)Field(widget, "background");
        Check(Mathf.Abs(background.rectTransform.rect.height - root.rect.height) < .5f, where + ": backdrop must match card height");
        Check(root.rect.height <= 252f, where + ": card must fit the preview HUD safe area");
        float previousBottom = float.PositiveInfinity;
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>())
        {
            if (string.IsNullOrWhiteSpace(text.text)) continue;
            text.ForceMeshUpdate(); Rect rect = text.rectTransform.rect; Bounds mesh = text.textBounds;
            float width = text.preferredWidth, height = text.preferredHeight;
            measurements.Add(where + "\t" + text.text + "\t" + F(text.fontSize) + "\t" + F(rect.width) + "\t" + F(rect.height) + "\t" + F(width) + "\t" + F(height) + "\t" + F(mesh.min.x) + "\t" + F(mesh.max.x) + "\t" + text.isTextTruncated);
            Check(text.fontSize >= 11f, where + ": readable font floor: " + text.text);
            Check(text.font == font, where + ": labels must retain the native child template font: " + text.text);
            Check(!text.isTextTruncated && !text.isTextOverflowing, where + ": text must be fully displayed: " + text.text);
            Check(height <= rect.height + .5f, where + ": text must fit vertical slot: " + text.text);
            if (!text.enableWordWrapping) Check(width <= rect.width + .5f, where + ": text must fit horizontal slot: " + text.text);
            var corners = new Vector3[4]; text.rectTransform.GetWorldCorners(corners);
            Vector3 bottomLeft = root.InverseTransformPoint(corners[0]), topRight = root.InverseTransformPoint(corners[2]);
            Rect panel = root.rect;
            layout.Add(where + "\t" + text.text + "\t" + F(panel.height) + "\t" + F(bottomLeft.x) + "\t" + F(topRight.x) + "\t" + F(bottomLeft.y) + "\t" + F(topRight.y));
            Check(bottomLeft.x >= panel.xMin && topRight.x <= panel.xMax && bottomLeft.y >= panel.yMin && topRight.y <= panel.yMax,
                where + ": visible label slot must stay inside card: " + text.text);
            Check(topRight.y <= previousBottom + .5f, where + ": successive labels must not overlap: " + text.text);
            previousBottom = bottomLeft.y;
        }
    }

    private static void Capture(GameObject canvas, string path, float width, float height)
    {
        Camera camera = OrthoCamera("Preview camera", height / 2f, new Color(.04f, .065f, .08f));
        int pixelWidth = (int)width * 2, pixelHeight = (int)height * 2;
        CapturePng(camera, pixelWidth, pixelHeight, path);
        captures.Add(path + "\t" + pixelWidth + "\t" + pixelHeight);
        Object.DestroyImmediate(camera.gameObject);
    }

    private static Type TypeOf(string name) => Mod.GetType(name, true);
    private static object New(string type, params object[] args) => Activator.CreateInstance(TypeOf(type), All & ~BindingFlags.Static, null, args, CultureInfo.InvariantCulture);
    private static object EnumValue(string type, string name) => Enum.Parse(TypeOf(Wing + "Domain." + type), name);
    private static object CallStatic(string type, string name, params object[] args) => TypeOf(Wing + "Domain." + type).GetMethod(name, All).Invoke(null, args);
    private static string F(float number) => number.ToString("0.0", CultureInfo.InvariantCulture);
    private static void Check(bool pass, string reason) { assertions++; if (!pass) failures.Add(reason); }
}
#endif
