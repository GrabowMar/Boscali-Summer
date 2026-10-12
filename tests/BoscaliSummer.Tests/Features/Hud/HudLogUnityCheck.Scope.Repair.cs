#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// Cases for the four-reviewer pass over the TAC BOARD branch (2026-10-12): the faults that only show in a running game, each failing without its fix. Run alone with -pages repair.
//   shade             the dock is lost and found again (Ensure builds the tree anew without Destroy): the cloud field shows the texture that outlived the old one, not UGUI's white square
//   header fault      a stores header that threw is forgiven once no stores block is drawn (it would hold the native boxes and the board under the native panel for good)
//   min stay / hold   the pilot's PAGE MIN STAY keeps a page up after its feed is gone; the RWR HOLD the pilot sets reaches the radar-warning watch (HudBoard.TakeOptions)
//   pixels            a 3 high bar and a 5 high marker centred on a whole unit are 3 and 5 pixels at every row; a hollow 4 x 4 or 5 x 5 box is a closed ring of pixels
//   threat column     the call column stands on COL 332 (LAYOUT.md 12.2): the call word is centred on 377 of the page
public static partial class HudLogUnityCheck
{
    private static void ScopeRepair()
    {
        RepairShade();
        RepairHeaderFault();
        RepairOptions();
        RepairPixels();
        RepairThreatColumn();
        ScopeReset();
    }

    private static void RepairShade()
    {
        var size = new Vector2(1920, 1080);
        MakeBoard(size, 2f, out Canvas canvas, out object status, out object frame);
        var scene = new ScopeScene(2f, size, frame);
        FixtureSystems(2f, scene);
        StoresFixture("systems", 2f, scene);
        Call(status, "Present", frame);
        Canvas.ForceUpdateCanvases();
        Check(Field(status, "fieldTexture") != null && ((RawImage)Field(status, "shadeField")).texture == (Texture)Field(status, "fieldTexture"), "shade: the cloud field shows its baked texture");
        // The native dock goes and comes back: its children are destroyed with it, and Ensure() builds the board again under the new one without the board's Destroy().
        Object.DestroyImmediate(((RectTransform)Field(status, "root")).gameObject);
        Set(status, "root", null);
        Call(status, "Build", Field(status, "dock"), null, null);
        Call(status, "Present", frame);
        Canvas.ForceUpdateCanvases();
        var field = (RawImage)Field(status, "shadeField");
        Check(field != null && field.gameObject.activeInHierarchy, "shade: the rebuilt board's cloud field is up");
        Check(field.texture != null && field.texture == (Texture)Field(status, "fieldTexture"), "shade: ...and it shows the texture that outlived the old tree (no texture is a solid white square)");
        Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
    }

    private static void RepairHeaderFault()
    {
        var size = new Vector2(1920, 1080);
        // A board with no stores feed draws no stores block: a header that had thrown must not hold the native boxes for good.
        MakeBoard(size, 2f, out Canvas canvas, out object status, out object frame);
        Set(status, "headerFault", true);
        Call(status, "Present", frame);
        Check(!(bool)Field(status, "headerFault"), "header fault: with no stores block drawn the fault is forgiven");
        Set(status, "headerFault", true);
        Call(status, "Hide");
        Check(!(bool)Field(status, "headerFault"), "header fault: ...and so is a hidden board's");
        // With the stores blocks drawn the fault is the last paint's: a good paint clears it.
        var scene = new ScopeScene(2f, size, frame);
        FixtureSystems(2f, scene);
        StoresFixture("systems", 2f, scene);
        Set(status, "headerFault", true);
        Call(status, "Present", frame);
        Check(!(bool)Field(status, "headerFault"), "header fault: ...a paint that works clears it too");
        Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
    }

    private static void RepairOptions()
    {
        var size = new Vector2(1920, 1080);
        var notices = new int[1];
        // PAGE MIN STAY: THREAT is up at 6 s, its feed is gone at 11 s. With no minimum the scope is SYSTEMS again; with 10 s it holds the page it was showing.
        foreach (float minStay in new[] { 0f, 10f })
        {
            object status = OptionBoard(size, "threat", 6f, PilotOptions("PageMinSeconds", minStay), notices, out Canvas canvas, out object frame);
            Check(PageTitle(status) == "THREAT", "min stay " + minStay + ": THREAT takes the scope");
            ScopeReset();
            Set(frame, "Now", 11f);
            Call(status, "Present", frame);
            string expected = minStay > 5f ? "THREAT" : "SYSTEMS";
            Check(PageTitle(status) == expected, "min stay " + minStay + ": five seconds after its feed went the scope shows " + expected + ": " + PageTitle(status));
            Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject);
        }

        // RWR HOLD: the pilot's setting reaches the radar-warning watch through the board's option tick (a board shell with the watch and the settings, no scene).
        string path = Path.Combine(Path.GetTempPath(), "boscali-hudcheck-" + Guid.NewGuid().ToString("N") + ".cfg");
        try
        {
            object file = Activator.CreateInstance(System.Type.GetType("BepInEx.Configuration.ConfigFile, BepInEx", true), path, false);
            object settings = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Configuration.HudSettings"), file);
            Type contract = Type("BoscaliSummer.Core.Contracts.ITacBoardSettings");
            object board = FormatterServices.GetUninitializedObject(Type("BoscaliSummer.Modules.Hud.Presentation.HudBoard"));
            object watch = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Runtime.ThreatWatch"), true);
            Set(board, "threats", watch);
            Set(board, "settings", settings);
            contract.GetProperty("ThreatHoldSeconds").SetValue(settings, 20f);
            Call(board, "TakeOptions");
            Check(Mathf.Abs((float)Field(watch, "Hold") - 20f) < 0.001f, "RWR hold: the pilot's 20 s reaches the radar-warning watch: " + Field(watch, "Hold"));
            contract.GetProperty("ThreatHoldSeconds").SetValue(settings, 3f);
            Call(board, "TakeOptions");
            Check(Mathf.Abs((float)Field(watch, "Hold") - 3f) < 0.001f, "RWR hold: ...and a change of it on the next tick: " + Field(watch, "Hold"));
            Check(Mathf.Abs((float)Field(Field(board, "options"), "ThreatHoldSeconds") - 3f) < 0.001f, "RWR hold: ...the snapshot the board keeps has it too");
        }
        finally { try { File.Delete(path); } catch (Exception) { } }
        ScopeReset();
    }

    /// <summary>The pixels (by centre) that the quads of <paramref name="draw"/> cover on a canvas of one canvas unit to a design unit and one device pixel, 100 high, as (column, row from the top).</summary>
    private static HashSet<(int, int)> CoveredPixels(Action<object> draw)
    {
        var quads = new NOAvionics.AvQuadBuffer(500);
        object canvas = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.ScopeCanvas"), quads, null, "pixel-check");
        var rgb = new NOAvionics.Rgba(0.5f, 0.87f, 0.6f);
        Call(canvas, "Begin", Activator.CreateInstance(Type(HudDomain + "ScopePalette"), rgb, rgb, rgb), 1f, 100f, 1f);
        draw(canvas);
        var covered = new HashSet<(int, int)>();
        for (int q = 0; q < quads.Count; q++)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int v = 0; v < 4; v++)
            {
                x0 = Mathf.Min(x0, quads.X[q * 4 + v]); x1 = Mathf.Max(x1, quads.X[q * 4 + v]);
                y0 = Mathf.Min(y0, quads.Y[q * 4 + v]); y1 = Mathf.Max(y1, quads.Y[q * 4 + v]);
            }
            for (int i = Mathf.FloorToInt(x0); i < Mathf.CeilToInt(x1); i++)
                for (int j = Mathf.FloorToInt(y0); j < Mathf.CeilToInt(y1); j++)
                    if (i + 0.5f > x0 && i + 0.5f < x1 && j + 0.5f > y0 && j + 0.5f < y1) covered.Add((i, 100 - 1 - j));
        }
        return covered;
    }

    private static void RepairPixels()
    {
        object ink = ToneOfScope("Ink"), bg = ToneOfScope("Bg");
        // A bar 3 high centred on a whole unit (the jammer's charge, by - 1.5) and a marker 5 high (an expendable's): the same height at every row, odd or even. Round-to-even made them 2 or 4, and 4 or 6.
        for (int cy = 40; cy < 44; cy++)
        {
            int rows = RowsOf(CoveredPixels(g => Call(g, "Fill", 10f, cy - 1.5f, 72f, 3f, ink, 1f)));
            Check(rows == 3, "pixels: a 72 x 3 bar centred on row " + cy + " is 3 pixels tall: " + rows);
            rows = RowsOf(CoveredPixels(g => Call(g, "Fill", 10f, cy - 2.5f, 5f, 5f, ink, 1f)));
            Check(rows == 5, "pixels: a 5 x 5 marker centred on row " + cy + " is 5 pixels tall: " + rows);
        }
        // A hollow box is a closed ring of pixels, corners included: a lamp's 4 x 4 (12 pixels) and an expendable's 5 x 5 (16), a 4 x 4 filled box is 16.
        foreach (int side in new[] { 4, 5, 6 })
        {
            HashSet<(int, int)> ring = CoveredPixels(g => Call(g, "Box", 10f, 10f, (float)side, (float)side, ink, 1f, 1f, bg, 0f, 0f, 0f, 0f, 0f));
            int want = side * side - (side - 2) * (side - 2);
            bool closed = ring.Count == want;
            for (int i = 0; i < side && closed; i++)
                foreach (var p in new[] { (10 + i, 10), (10 + i, 10 + side - 1), (10, 10 + i), (10 + side - 1, 10 + i) })
                    closed &= ring.Contains(p);
            Check(ring.Count == want, "pixels: a hollow " + side + " x " + side + " box covers its " + want + " border pixels, once each: " + ring.Count);
            Check(closed, "pixels: ...the ring is closed, all four corners in it");
        }
    }

    private static int RowsOf(HashSet<(int, int)> pixels)
    {
        var rows = new HashSet<int>();
        foreach (var p in pixels) rows.Add(p.Item2);
        return rows.Count;
    }

    private static void RepairThreatColumn()
    {
        var hd = new Vector2(1920, 1080);
        // THREAT at 6 s calls for chaff: its word and its sub-word are centred on 377 of the page (LAYOUT.md 12.2, COL 332 + half of 90), 14 left of where the column stood before the critique.
        LinesShot("threat-call-column", hd, "threat", 6f, PilotOptions(), null, (status, tag) =>
        {
            bool found = false;
            foreach (string word in new[] { "DISPENSE", "CHAFF", "BEAM", "FLARE" })
                if (WordInk(status, word, out Rect ink) && ink.center.y > 50f && ink.center.y < 100f)
                {
                    found = true;
                    Check(Mathf.Abs(ink.center.x - (LampsLeft(status) + 377f)) < 2.5f, tag + ": the call word " + word + " is centred on 377 of the page: " + (ink.center.x - LampsLeft(status)));
                }
            Check(found, tag + ": the call box has a word");
        });
    }
}
#endif
