#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Modules.Autopilot.Domain;
using BoscaliSummer.Modules.Autopilot.Presentation;
using BoscaliSummer.Tests.Features.Autopilot;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

/// <summary>
/// Offline visual check of the v7 interaction menu: runs the production <see cref="MenuPainter"/> and <see cref="MenuTextPool"/> on the shared
/// AvionicsUi kit over the REAL catalog (<see cref="MenuCatalog"/> over the prototype's scenario worlds), puts the pure controller into each gallery
/// state of the prototype with the same input rig the unit tests use, and renders PNGs at 1920x1080 (every state) and 1280x720 / 2560x1080 (root-idle,
/// flight-dock, comms-calls, left-dock) over a real cockpit backdrop. The scratch project runs in Linear colour space like the game (see the runner). Assertions run with the real TMP font: the shared geometry matrix
/// (<see cref="MenuGeometryChecks"/>), no label truncated, text at least 12 px, no vector quad dropped. A PASS is not visual acceptance:
/// read the PNGs against the prototype shots (Compare-MenuShots.py puts them side by side). The game host (AceRadialMenuUi), input and game
/// adapters are never run here.
/// </summary>
public static class InteractionMenuUnityCheck
{
    private const int Capacity = 7000;
    /// <summary>Every failed assertion, reported together at the end so one run shows all the defects and still renders every state.</summary>
    private static readonly List<string> Failures = new List<string>();
    private static readonly List<string> Allocation = new List<string>();

    private static void Check(bool ok, string message)
    {
        if (ok) return;
        Failures.Add(message);
        Debug.LogError(message);
    }

    private static readonly int[][] One = { new[] { 1920, 1080 } };
    private static readonly int[][] Three = { new[] { 1920, 1080 }, new[] { 1280, 720 }, new[] { 2560, 1080 } };

    private sealed class Frame
    {
        public string Id;
        public Func<MenuWorld> World;
        public Action<MenuRig> Setup;
        public int[][] Screens;
    }

    private static Frame F(string id, Func<MenuWorld> world, Action<MenuRig> setup, int[][] screens) =>
        new Frame { Id = id, World = world, Setup = setup, Screens = screens };

    private static void FlickTo(MenuRig r, float angle, float frac)
    {
        float[] p = r.Polar(angle, frac * r.L.R);
        r.Mouse(p[0], p[1]);
    }

    private static readonly Frame[] Gallery =
    {
        F("root-idle", MenuWorlds.Default, r => r.HoldOpen(), Three),
        F("peek-weapons", MenuWorlds.Default, r => { r.HoldOpen(); FlickTo(r, 18f, 0.8f); }, One),
        F("flight-dock", MenuWorlds.Default, r => { r.HoldOpen(); FlickTo(r, 90f, 0.87f); r.ToRow("flight/gear"); r.Adv(0.1f); }, Three),
        F("weapons-select", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(1); r.Down(); r.Down(); r.Down(); r.Enter(); }, One),
        F("comms-calls", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(5); r.Wheel(1); r.Wheel(1); r.ToRow("comms/con-my-way"); }, Three),
        F("defence-latched", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(9); r.ToRow("defence/flares", 0.55f); }, One),
        F("eject-hold", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(3); for (int i = 0; i < 6; i++) r.Down(); r.EnterDown(); r.Adv(0.48f); }, One),
        F("wing-nowing", MenuWorlds.NoWing, r => r.HoldOpen(), One),
        F("wing-empty", MenuWorlds.NoWing, r => { r.HoldOpen(); FlickTo(r, 126f, 0.8f); }, One),
        F("left-dock", MenuWorlds.Default, r => { r.HoldOpen(); FlickTo(r, 306f, 0.87f); r.ToRow("defence/radar"); r.Adv(0.1f); }, Three),
        F("toast", MenuWorlds.Default, r => { r.HoldOpen(); FlickTo(r, 90f, 0.87f); r.ToRow("flight/gear"); r.Adv(0.1f); r.CUp(); }, One),
        F("hat-nav", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(2); r.Down(); r.Down(); }, One),
        // the controller wheel: flick to a category, centre the stick to enter it, aim a row
        F("pad-ring", MenuWorlds.Default, r => { r.PadY(true); r.Adv(0.2f); r.Stick(1f, 0f); r.Time += 0.6f; r.Stick(1f, 0f); }, One),
        F("pad-wheel", MenuWorlds.Default, r => { r.PadY(true); r.Adv(0.2f); r.Stick(1f, 0f); r.Time += 1.05f; r.Stick(1f, 0f); r.Stick(0f, 0f); r.Time += 0.2f; r.Stick(0.809f, -0.588f); }, Three),
        F("pad-comms", MenuWorlds.Default, r => { r.PadY(true); r.Adv(0.2f); r.Stick(0.309f, 0.951f); r.Time += 1.05f; r.Stick(0.309f, 0.951f); r.Stick(0f, 0f); r.Time += 0.2f; r.Stick(1f, 0f); }, One),
        F("support-perks", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(7); for (int i = 0; i < 4; i++) r.Down(); r.Enter(); r.Down(); }, One),
        F("view-missile", MenuWorlds.Missile, r => { r.HoldOpen(); FlickTo(r, 270f, 0.87f); r.ToRow("view/mslnext"); r.Adv(0.1f); }, One),
        // HUD: direct manual page slots, major board elements and the display options.
        F("view-board", MenuWorlds.ThreeBoardPages, r => { r.HoldOpen(); FlickTo(r, 54f, 0.87f); r.ToRow("hud/page1", 0.95f); r.Adv(0.1f); }, Three),
        F("view-board-off", MenuWorlds.BoardOff, r => { r.HoldOpen(); FlickTo(r, 54f, 0.87f); r.ToRow("hud/board", 0.95f); r.Adv(0.1f); }, One),
        F("hud-pages", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(2); r.ToRow("hud/page1"); r.Click(); r.ToRow("hud/page1/dock"); r.Adv(0.1f); }, One),
        F("hud-display", MenuWorlds.ThreeBoardPages, r => { r.LatchOpen(); r.PressDigit(2); r.ToRow("hud/display"); r.Click(); r.ToRow("hud/count"); r.Adv(0.1f); }, One),
        F("hud-parts", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(2); r.ToRow("hud/parts"); r.Click(); r.ToRow("hud/parts/status"); r.Adv(0.1f); }, One),
        F("cockpit", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(6); r.ToRow("cockpit/receiver"); r.Adv(0.1f); }, Three),
        F("cockpit-feel", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(6); r.ToRow("cockpit/feel"); r.Click(); r.ToRow("cockpit/feel/visor"); r.Adv(0.1f); }, One),
        F("ground-flight", MenuWorlds.Ground, r => { r.LatchOpen(); r.PressDigit(3); r.Down(); }, One),
        // WING > FORMATION: 22 shapes in 4 families, scrolled so the CLASSIC and ESCORT groups meet, HIGH COVER focused
        F("wing-formation", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(4); r.ToRow("wing/shape"); r.Click(); for (int i = 0; i < 5; i++) r.Wheel(1); r.ToRow("wing/shape/high-cover"); r.Adv(0.1f); }, One),
        // OPS PERKS: grouped by front with the front glyph, PRSM cooling down in place
        F("perk-cooldown", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(7); for (int i = 0; i < 4; i++) r.Down(); r.Enter(); r.ToRow("support/perks/prsm"); r.Adv(0.1f); }, One),
        F("pit-stop", MenuWorlds.Pit, r => { r.LatchOpen(); r.PressDigit(7); r.ToRow("support/pit"); r.Enter(); r.ToRow("support/pit/station/9"); r.Adv(0.1f); }, Three),
        // the jammer is the active countermeasure: its name is the DEFENCE badge
        F("jammer-idle", MenuWorlds.Jammer, r => r.HoldOpen(), One),
        // a refused press (digit 9 in a 6-row dock): the row gutters flash amber for 180 ms (checked against the settled frame below)
        F("deny-pulse", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(3); r.PressDigit(9); }, One),
        // recall: FLIGHT ASSIST ran last, so a press at the ring came back to it; one arrow down and its margin chevron shows
        F("recall-flight", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(3); r.ToRow("flight/assist"); r.Click(); r.Adv(0.2f); r.Backspace(); r.Enter(); r.Down(); }, One),
        F("recall-root", MenuWorlds.Default, r => { r.LatchOpen(); r.PressDigit(3); r.ToRow("flight/assist"); r.Click(); r.Backspace(); }, One),
    };

    public static void Run()
    {
        try
        {
            if (!EnsureTmpEssentials(Run)) return;
            AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            if (!AvBundle.Available || !AvIcons.Available) throw new Exception("Production fonts and icons must load.");
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);

            string only = Arg("-menuStates");
            int renders = 0;
            foreach (Frame frame in Gallery)
            {
                if (!string.IsNullOrEmpty(only) && Array.IndexOf(only.Split(','), frame.Id) < 0) continue;
                foreach (int[] s in frame.Screens) { RenderFrame(frame, s[0], s[1]); renders++; }
            }
            if (Failures.Count > 0) throw new Exception(Failures.Count + " failed check(s):\n" + string.Join("\n", Failures));
            File.WriteAllText("result.txt",
                "PASS: " + renders + " renders of the v7 ring + dock menu (production painter, text pool and catalog on the shared AvionicsUi kit); the shared geometry " +
                "matrix, no truncated label, 12 px text floor and no dropped vector quad held with the real TMP font. Game host and adapters are not run here; " +
                "compare the PNGs with the prototype shots and check in game. Steady-state paint allocation: " + string.Join("; ", Allocation) + ".");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("result.txt", "FAIL: " + error);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return null;
    }

    private static void RenderFrame(Frame frame, int w, int h)
    {
        AvHudHost host = AvHudHost.Create("Menu check " + frame.Id, null, 4);
        host.Canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)host.Root.transform).sizeDelta = new Vector2(w, h);
        AvVector surface = AvVector.Create(host.Static, "Menu", Capacity);
        var text = new MenuTextPool(host.Data);
        var painter = new MenuPainter(surface.Buffer, text);

        var rig = new MenuRig(frame.World(), w, h, text);
        string at = " " + frame.Id + " @" + w + "x" + h;
        MenuGeometryChecks.All((ok, message) => Check(ok, message + at), rig.Model, rig.L, text);
        frame.Setup(rig);
        float now = rig.Time;
        painter.Paint(rig.C, rig.L, now);          // first paint starts the dock slide ...
        painter.Paint(rig.C, rig.L, now + 1f);     // ... the second one is the settled frame
        surface.Commit();
        Check(!surface.Buffer.Overflowed, "vector buffer overflowed" + at + " (" + surface.Buffer.Capacity + " quads)");
        long denyFrame = Fingerprint(surface.Buffer);
        if (w == 1920 && (frame.Id == "root-idle" || frame.Id == "flight-dock" || frame.Id == "comms-calls" || frame.Id == "wing-formation"))
        {
            // a steady frame must not allocate: paint 300 settled frames and read the managed heap (a coarse meter: the old painter cost a few KB a frame)
            long control = Grown(i => Sink = new byte[256]);
            Check(control > 20 * 1024, "the allocation meter is blind: 300 x 256 B read as " + control + " B" + at);
            long grown = Grown(i => painter.Paint(rig.C, rig.L, now + 2f + i * 0.016f));
            Allocation.Add(frame.Id + " " + grown / 1024 + " KB/300 frames (meter control " + control / 1024 + " KB)");
            Check(grown < 256 * 1024, "a steady painted frame allocates" + at + ": " + grown / 1024 + " KB over 300 frames");
            painter.Paint(rig.C, rig.L, now + 1f);
        }

        // Offline the vector layers' engine-driven OnEnable never runs: force it so the geometry flushes into a mesh.
        var graphicOnEnable = typeof(Graphic).GetMethod("OnEnable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        foreach (AvVector av in host.Root.GetComponentsInChildren<AvVector>(true))
        {
            if (av.GetComponent<CanvasRenderer>() == null) av.gameObject.AddComponent<CanvasRenderer>();
            graphicOnEnable.Invoke(av, null);
            av.Commit();
        }
        Canvas.ForceUpdateCanvases();
        AddBackdrop(host, w, h);
        Canvas.ForceUpdateCanvases();
        int labels = 0;
        foreach (TMP_Text label in host.Data.GetComponentsInChildren<TMP_Text>(false))
        {
            if (string.IsNullOrEmpty(label.text)) continue;
            label.ForceMeshUpdate();
            labels++;
            Check(!label.isTextTruncated, "label truncated" + at + ": " + label.text + " (rect " + label.rectTransform.rect.width + "x" + label.rectTransform.rect.height + ", preferred " + label.preferredWidth + ", bounds " + label.textBounds.size.x + ", size " + label.fontSize + ", spacing " + label.characterSpacing + ", font " + (label.font != null ? label.font.name : "none") + ")");
            Check(label.fontSize >= 11.95f, "text below the 12 px floor" + at + ": " + label.text + " (" + label.fontSize + ")");
        }
        Check(labels > 0, "nothing was drawn" + at);

        var focus = new GameObject("Focus", typeof(RectTransform)).GetComponent<RectTransform>();
        focus.SetParent(host.Static, false);
        AvHudHost.Place(focus, 0f, 0f, w, h);
        RenderPng(host.Canvas, focus, frame.Id + "-" + w + "x" + h + ".png", w, h);
        if (frame.Id == "deny-pulse")
        {
            // the same state after the pulse has faded must paint differently: the refusal is visible, and it ends
            Check(rig.C.Denying, "the refusal pulse is running in the deny frame" + at);
            rig.Adv(MenuController.DenySec + 0.05f);
            painter.Paint(rig.C, rig.L, rig.Time + 1f);
            Check(!rig.C.Denying && Fingerprint(surface.Buffer) != denyFrame, "the refusal pulse changes the painted frame while it runs" + at);
        }
        Object.DestroyImmediate(host.Root);
    }

    private static byte[] Sink;

    /// <summary>Managed heap growth over 300 calls of <paramref name="step"/>, without a collection in between.</summary>
    private static long Grown(Action<int> step)
    {
        GC.Collect();
        long before = GC.GetTotalMemory(false);
        for (int i = 0; i < 300; i++) step(i);
        return GC.GetTotalMemory(false) - before;
    }

    /// <summary>A hash of every vertex colour in the painted quads: two frames that differ in any colour differ here.</summary>
    private static long Fingerprint(AvQuadBuffer buffer)
    {
        long h = 17;
        for (int i = 0; i < buffer.Count * 4; i++)
        {
            Rgba c = buffer.C[i];
            h = h * 31 + (long)(c.R * 1000f) + 7 * (long)(c.G * 1000f) + 13 * (long)(c.B * 1000f) + 29 * (long)(c.A * 1000f);
        }
        return h;
    }

    private static Texture2D backdropTexture;

    private static void AddBackdrop(AvHudHost host, float w, float h)
    {
        string path = Arg("-menuBackdrop");
        var bar = new GameObject("Backdrop", typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform)bar.transform;
        rt.SetParent(host.Static, false);
        rt.SetAsFirstSibling();
        AvHudHost.Place(rt, 0f, 0f, w, h);
        var image = bar.GetComponent<RawImage>();
        image.raycastTarget = false;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) { image.color = new Color(0.02f, 0.03f, 0.04f); return; }
        if (backdropTexture == null) { backdropTexture = new Texture2D(2, 2); backdropTexture.LoadImage(File.ReadAllBytes(path)); }
        // the 16:9 plate at full height, centred; a wider screen gets dark bars either side (a stretched cockpit would mislead)
        float plate = Mathf.Min(w, h * 16f / 9f);
        AvHudHost.Place(rt, (w - plate) / 2f, 0f, plate, h);
        image.texture = backdropTexture;
    }

    private static void RenderPng(Canvas canvas, RectTransform focus, string path, int pixelWidth, int pixelHeight)
    {
        canvas.transform.localScale = Vector3.one;
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
        var corners = new Vector3[4];
        focus.GetWorldCorners(corners);
        var camera = OrthoCamera("Render camera", pixelHeight / 2f, new Color(0.02f, 0.03f, 0.04f), (corners[0] + corners[2]) * 0.5f + new Vector3(0f, 0f, -10f));
        CapturePng(camera, pixelWidth, pixelHeight, path);
        Object.DestroyImmediate(camera.gameObject);
    }
}
#endif
