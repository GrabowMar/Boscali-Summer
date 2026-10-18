#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NOAvionics;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// DL EVT LOG (HUD holo pass D, 2026-10-13): the top-left corner's own event log, from a stand-in native feed (the corners' rig: a box with a plate, the feed's text objects, the chat input). The lines the game adds are pushed into the
// log's ledger the way the patch pushes them (EventFeed.Capture: rich text, removal time, stamp, the pilot's names); the log's own clock is set by the check. The cases: six rows, one, a 90 character line, the pilot's own name, Lean,
// Minimal, the wipe / BRIGHT / fade of a row, Opacity, the option off (the stand-in's text alpha comes back exactly), the chat input open (the stand-in shows whole, the log is gone), a capture that sees nothing (fail open) and the teardown
// (a group the text already had gets its alpha back and stays; one the mod added goes). Renders corners-log-<case>-<W>x<H>.png. Run alone with -pages corners.
public static partial class HudLogUnityCheck
{
    /// <summary>The log's clock for the check (the game's <c>Time.timeSinceLevelLoad</c> is not ours to set).</summary>
    private const float LogNow = 200f;

    private static Type FeedType() => Type("BoscaliSummer.Modules.Hud.Patches.EventFeed");
    private static object FeedLog() => Field(FeedType(), "Log");
    private static void LogClear() => FeedLog().GetType().GetMethod("Clear", All).Invoke(FeedLog(), null);

    /// <summary>A line as the patch hands it over: the game's rich text, when it came and when the game takes it off, the mission stamp and the pilot's names.</summary>
    private static void LogAdd(string rich, float arrival, float life = 60f, string stamp = "09:35", string player = null, string aircraft = null) =>
        FeedType().GetMethod("Capture", All).Invoke(null, new object[] { rich, arrival, arrival + life, stamp, player, aircraft });

    /// <summary>The stand-in's own lines become the ledger's (as the game's feed and the patch agree), all of them settled long ago.</summary>
    private static void SeedLog(CornerRig rig)
    {
        LogClear();
        for (int i = 0; i < rig.Texts.Count; i++) LogAdd(rig.Texts[i].text, LogNow - 10f + i, 60f, "09:3" + i);
    }

    private const string Blue = "<color=#58A8FFFF>", Red = "<color=#FF4D4DFF>", End = "</color>";

    /// <summary>Six rows: three interceptions are the game's own screenshot lines, then the pilot's own kill, a join, a radio call and a chat line that has just come (BRIGHT).</summary>
    private static void LogSix(CornerRig rig)
    {
        LogClear();
        LogAdd(Blue + "FGA-57 Anvil" + End + " intercepted " + Red + "MLRS rocket" + End, LogNow - 30f, 60f, "09:31");
        LogAdd(Blue + "Cursor Class LFD" + End + " intercepted " + Red + "MLRS rocket" + End, LogNow - 20f, 60f, "09:32");
        LogAdd(Red + "Boscali 2-1 [FS-20 Vortex]" + End + " shot down " + Blue + "Anvil Mk 3" + End, LogNow - 14f, 60f, "09:33", "Boscali 2-1", "Boscali 2-1 [FS-20 Vortex]");
        LogAdd("<color=#FFD84AFF>PilotOne joined the game</color>", LogNow - 9f, 60f, "09:34");
        LogAdd("Hawk 2-1: Contact bearing 090, angels 12", LogNow - 5f, 60f, "09:35");
        LogAdd("<color=#AABBCCFF>(ally)Wingman:</color> Splash one, bearing 270", LogNow - 0.6f, 60f, "09:36");
        Set(rig.Status, "logClockOverride", LogNow);
        PresentCorners(rig);
    }

    /// <summary>The words on the log's surface by text (the first of each) and the rectangles of their glyph boxes from the canvas's top-left.</summary>
    private static Dictionary<string, Rect> LogWords(CornerRig rig)
    {
        var words = new Dictionary<string, Rect>();
        var canvasRect = (RectTransform)rig.Canvas.transform;
        foreach (TextMeshProUGUI label in LogLabels(rig))
            if (!words.ContainsKey(label.text)) words[label.text] = CanvasRectOf(canvasRect, label.rectTransform);
        return words;
    }

    private static List<TextMeshProUGUI> LogLabels(CornerRig rig)
    {
        var surface = (RectTransform)Field(rig.Status, "logSurface");
        var list = new List<TextMeshProUGUI>();
        if (surface != null)
            foreach (TextMeshProUGUI label in surface.GetComponentsInChildren<TextMeshProUGUI>())
                if (label.gameObject.activeInHierarchy && !string.IsNullOrEmpty(label.text)) list.Add(label);
        return list;
    }

    /// <summary>The words of row <paramref name="i"/> (design y of its middle from the block's plan), left to right.</summary>
    private static List<TextMeshProUGUI> LogRow(CornerRig rig, int i)
    {
        float y = (PlanF(rig, "FirstRowY") + 16f * i) * rig.U;
        var canvasRect = (RectTransform)rig.Canvas.transform;
        return LogLabels(rig).Where(l => Mathf.Abs(CanvasRectOf(canvasRect, l.rectTransform).center.y - y) <= 3f).OrderBy(l => CanvasRectOf(canvasRect, l.rectTransform).xMin).ToList();
    }

    private static float PlanF(CornerRig rig, string field) { object plan = Field(rig.Status, "logPlan"); return (float)plan.GetType().GetField(field).GetValue(plan); }
    private static int PlanRows(CornerRig rig) { object plan = Field(rig.Status, "logPlan"); return (int)plan.GetType().GetField("Rows").GetValue(plan); }

    private static List<Rect> LogQuads(CornerRig rig)
    {
        AvQuadBuffer buffer = ((AvVector)Field(rig.Status, "logVector")).Buffer;
        var list = new List<Rect>();
        for (int q = 0; q < buffer.Count; q++)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int v = 0; v < 4; v++)
            {
                x0 = Mathf.Min(x0, buffer.X[q * 4 + v]); x1 = Mathf.Max(x1, buffer.X[q * 4 + v]);
                y0 = Mathf.Min(y0, buffer.Y[q * 4 + v]); y1 = Mathf.Max(y1, buffer.Y[q * 4 + v]);
            }
            list.Add(Rect.MinMaxRect(x0, rig.Size.y - y1, x1, rig.Size.y - y0));
        }
        return list;
    }

    /// <summary>How many quads on the log's surface are filled in one of the colours the game gave its names (a 4 unit square: the squares in front of the names).</summary>
    private static int LogSwatches(CornerRig rig, params int[] rgbs)
    {
        AvQuadBuffer buffer = ((AvVector)Field(rig.Status, "logVector")).Buffer;
        int n = 0;
        for (int q = 0; q < buffer.Count; q++)
        {
            Rgba c = buffer.C[q * 4];
            foreach (int rgb in rgbs)
                if (Mathf.Abs(c.R - ((rgb >> 16) & 255) / 255f) < 0.01f && Mathf.Abs(c.G - ((rgb >> 8) & 255) / 255f) < 0.01f && Mathf.Abs(c.B - (rgb & 255) / 255f) < 0.01f) { n++; break; }
        }
        return n;
    }

    private static float TextAlpha(CornerRig rig)
    {
        float a = 1f;
        foreach (TextMeshProUGUI t in rig.Texts) { var g = t.GetComponent<CanvasGroup>(); if (g != null) a = Mathf.Min(a, g.alpha); }
        return a;
    }

    private static float Luma(Color c) => 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;

    private static TextMeshProUGUI LogLabel(CornerRig rig, string trimmed) => LogLabels(rig).FirstOrDefault(l => l.text.Trim() == trimmed);

    /// <summary>The frame the game's own LateUpdate would be followed by: nothing but the per-frame corner pass.</summary>
    private static void LogFrame(CornerRig rig, float clock)
    {
        Set(rig.Status, "logClockOverride", clock);
        Call(rig.Status, "CornersFrame", true, false);
        Canvas.ForceUpdateCanvases();
    }

    private static string[] WordsOfRow(CornerRig rig, int i) => LogRow(rig, i).Select(l => l.text.Trim()).ToArray();

    private static void ScopeEventLog(Vector2 size)
    {
        string res = (int)size.x + "x" + (int)size.y;
        CornerRig rig = MakeCorners(size, PilotOptions());
        float u = rig.U;
        try
        {
            // --- The stand-in's own four lines, as MakeCorners seeded them: four rows, the shelf, the game's text and plate hidden.
            Check(Up("logRoot", rig.Status) && PlateAlpha(rig) == 0f && TextAlpha(rig) == 0f, "Log: in charge of the feed corner: its tree is up, the game's plate and text are hidden: " + res);
            Check(PlanRows(rig) == 4, "Log: the stand-in's four lines are four rows: " + res);

            // --- Six rows.
            LogSix(rig);
            Check(PlanRows(rig) == 6 && Up("logRoot", rig.Status) && Up("cornerShade", rig.Status), "Log six: six rows, the log and its shade up: " + res);
            Check(PlateAlpha(rig) == 0f && TextAlpha(rig) == 0f, "Log six: the game's plate and text hidden: " + res);
            Check(rig.Texts.All(t => t.gameObject.activeInHierarchy && t.color.a > 0.99f), "Log six: ...and not one of the game's text objects was switched off or recoloured: " + res);
            float left = PlanF(rig, "Left"), top = PlanF(rig, "Top"), width = PlanF(rig, "Width"), first = PlanF(rig, "FirstRowY"), stampX = PlanF(rig, "StampX"), lineX = PlanF(rig, "LineX");
            Check(Mathf.Abs(left - 16f) < 0.01f && Mathf.Abs(top - 12f) < 0.01f && Mathf.Abs(first - 36f) < 0.01f, "Log six: the block shares the left column's 16-unit inset, 12 down, the first row 24 under its top: " + left + " " + top + " " + first + " " + res);
            Check(width >= 280f && width <= 560f && width % 40f == 0f, "Log six: the block is 280 to 560 wide in steps of 40: " + width + " " + res);
            string[] classes = { "INTCP", "INTCP", "KILL", "NET", "COMM", "COMM" };
            string[] stamps = { "09:31", "09:32", "09:33", "09:34", "09:35", "09:36" };
            for (int i = 0; i < 6; i++)
            {
                string[] w = WordsOfRow(rig, i);
                Check(w.Length >= 3 && w[0] == stamps[i] && w[1] == classes[i], "Log six: row " + i + " reads the stamp " + stamps[i] + " and the class " + classes[i] + ", oldest at the top: " + string.Join("|", w) + " " + res);
            }
            Check(WordsOfRow(rig, 0).SequenceEqual(new[] { "09:31", "INTCP", "FGA-57 ANVIL", "INTERCEPTED", "MLRS ROCKET" }), "Log six: the screenshot line is its names and its verb in capitals: " + string.Join("|", WordsOfRow(rig, 0)) + " " + res);
            Check(WordsOfRow(rig, 4).SequenceEqual(new[] { "09:35", "COMM", "HAWK 2-1:", "CONTACT BEARING 090, ANGELS 12" }), "Log six: the radio call is the speaker and the words: " + string.Join("|", WordsOfRow(rig, 4)) + " " + res);
            foreach (TextMeshProUGUI l in LogLabels(rig)) Check(l.text == l.text.ToUpperInvariant(), "Log six: everything is in capitals: " + l.text);
            Rect r0 = CanvasRectOf((RectTransform)rig.Canvas.transform, LogRow(rig, 0)[0].rectTransform), r5 = CanvasRectOf((RectTransform)rig.Canvas.transform, LogRow(rig, 5)[0].rectTransform);
            Check(Mathf.Abs((r5.center.y - r0.center.y) - 80f * u) <= 3f, "Log six: five pitches of 16 between the first row and the last: " + (r5.center.y - r0.center.y) + " " + res);
            Rect stampBox = CanvasRectOf((RectTransform)rig.Canvas.transform, LogRow(rig, 0)[0].rectTransform);
            Check(Mathf.Abs(stampBox.xMin - stampX * u) <= 6f * u, "Log six: the stamp is 12 in from the block's edge: " + stampBox + " " + res);
            Check(Mathf.Abs(CanvasRectOf((RectTransform)rig.Canvas.transform, LogRow(rig, 0)[2].rectTransform).xMin - (lineX + 7.8f) * u) <= 6f * u, "Log six: the line starts a square's cell after the line column: " + res);
            // Tones: settled copy is pale at 90%, own/fresh copy full strength, verbs and timestamps sit behind it.
            TextMeshProUGUI ink = LogLabel(rig, "FGA-57 ANVIL"), own = LogLabel(rig, "BOSCALI 2-1 [FS-20 VORTEX]"), verb = LogLabel(rig, "INTERCEPTED"), fresh = LogLabel(rig, "SPLASH ONE, BEARING 270"), stamp = LogLabel(rig, "09:31");
            Check(ink != null && own != null && verb != null && fresh != null && stamp != null, "Log six: the words are there to judge: " + res);
            Check(Luma(own.color) * own.color.a > Luma(ink.color) * ink.color.a + 0.05f, "Log six: the pilot's own name has more visible emphasis than settled copy: " + res);
            Check(Luma(fresh.color) * fresh.color.a > Luma(ink.color) * ink.color.a + 0.05f, "Log six: a row under 1.5 s old has full emphasis: " + res);
            Check(Luma(verb.color) < Luma(ink.color) && ink.color.a >= 0.89f && stamp.color.a < 0.8f, "Log six: message copy stays readable while verbs and timestamps remain secondary: " + res);
            // The squares in the game's colours: two in each of the three interceptions' and the kill's rows (blue and red), one on the join line, one on the chat speaker; none for the radio call.
            Check(LogSwatches(rig, 0x58A8FF, 0xFF4D4D, 0xFFD84A, 0xAABBCC) == 8, "Log six: a square in front of each coloured name (8): " + LogSwatches(rig, 0x58A8FF, 0xFF4D4D, 0xFFD84A, 0xAABBCC) + " " + res);
            Check(LogSwatches(rig, 0x58A8FF) == 3 && LogSwatches(rig, 0xFF4D4D) == 3 && LogSwatches(rig, 0xFFD84A) == 1 && LogSwatches(rig, 0xAABBCC) == 1, "Log six: ...in the colour the game gave that name: " + res);
            // The furniture: the shelf and its caption on top, the spine with a tick on each row.
            List<Rect> quads = LogQuads(rig);
            Dictionary<string, Rect> words = LogWords(rig);
            Check(words.ContainsKey("DL EVT LOG") && Mathf.Abs(words["DL EVT LOG"].center.y - (top + 6f) * u) <= 4f && Mathf.Abs(words["DL EVT LOG"].xMin - (left + 12f) * u) <= 6f * u, "Log six: the caption sits on the shelf's line, 12 in from the block's left: " + res);
            Check(HasLine(quads, true, (top + 6f) * u, left * u, (left + 8f) * u) && HasLine(quads, true, (top + 6f) * u, (left + 12f + 78f + 8f) * u, (left + width - 4f) * u), "Log six: the shelf's hairline runs each side of the caption to the block's right end: " + res);
            Check(quads.Any(q => Mathf.Abs(q.center.x - (left + width) * u) <= 2f && Mathf.Abs(q.center.y - (top + 6f) * u) <= 2f && q.width >= 3f * u - 1f && q.width <= 4f * u + 1f), "Log six: ...ending in a 4 x 4 node: " + res);
            Check(HasLine(quads, false, (left + 1f) * u, (first - 8f) * u, (first + 80f + 8f) * u), "Log six: the spine runs down the left from over the first row to under the last: " + res);
            for (int i = 0; i < 6; i++) Check(HasLine(quads, true, (first + 16f * i) * u, (left + 1f) * u, (left + 5f) * u), "Log six: a tick on row " + i + ": " + res);
            float right = 0f;
            foreach (TextMeshProUGUI l in LogLabels(rig)) right = Mathf.Max(right, CanvasRectOf((RectTransform)rig.Canvas.transform, l.rectTransform).xMin + l.text.Length * 7.8f * u);
            Check(right <= (left + width) * u + 2f, "Log six: no row runs past the block's right end: " + right + " " + (left + width) * u + " " + res);
            Check(!HasLine(quads, true, 3f * u, 2f * u, 12f * u) && !HasLine(quads, false, 2f * u, 3f * u, 13f * u), "Log six: the old feed's corner brackets are gone: " + res);
            CornerShot(rig, "log-six");

            // --- One row.
            LogClear();
            LogAdd("Hawk 2-1: Contact bearing 090, angels 12", LogNow - 5f);
            LogFrame(rig, LogNow);
            Check(PlanRows(rig) == 1 && WordsOfRow(rig, 0).Length == 4, "Log one: one row: " + res);
            Check(Mathf.Abs(PlanF(rig, "Height") - (24f + 8f)) < 0.01f && PlanF(rig, "Width") == 480f, "Log one: the block is 32 high and the least multiple of 40 that holds the row, 480: " + PlanF(rig, "Height") + " " + PlanF(rig, "Width") + " " + res);
            CornerShot(rig, "log-one");

            // --- A 90 character line (a whole sentence of words): whole words off its end and a DIM plus, no dots.
            LogClear();
            LogAdd("Reinforcements are inbound from the northern staging area with fuel and ammunition for all squadrons", LogNow - 5f);
            LogFrame(rig, LogNow);
            string[] longRow = WordsOfRow(rig, 0);
            Check(longRow.Length == 4 && longRow[2] == "REINFORCEMENTS ARE INBOUND FROM THE NORTHERN STAGING" && longRow[3] == "+", "Log long: whole words up to the room, then the plus: " + string.Join("|", longRow) + " " + res);
            Check(LogLabels(rig).All(l => !l.text.Contains("..") && !l.text.Contains("…")), "Log long: no dots: " + res);
            Check(PlanF(rig, "Width") == 560f, "Log long: the block is as wide as it goes, 560: " + res);
            Check(CanvasRectOf((RectTransform)rig.Canvas.transform, LogRow(rig, 0)[3].rectTransform).xMin + 7.8f * u <= (left + 560f) * u, "Log long: the plus is inside the block: " + res);
            Check(LogRow(rig, 0)[3].color.a < 0.8f, "Log long: the plus is DIM: " + res);
            CornerShot(rig, "log-long");

            // --- Lean and Minimal: no shelf, no spine, no stamps; Minimal keeps only the pilot's KILL rows and COMM.
            LogSix(rig);
            Set(rig.Status, "Options", PilotOptions("Declutter", Declutter("Lean")));
            PresentCorners(rig);
            words = LogWords(rig);
            quads = LogQuads(rig);
            Check(PlanRows(rig) == 6 && !words.ContainsKey("DL EVT LOG"), "Log Lean: six rows and no shelf: " + res);
            Check(LogLabels(rig).All(l => !(l.text.Length == 5 && l.text[2] == ':')), "Log Lean: no stamps: " + res);
            Check(!quads.Any(q => q.width <= 2.5f && q.height > 30f * u), "Log Lean: no spine: " + res);
            Check(words.ContainsKey("INTCP") && words.ContainsKey("KILL") && words.ContainsKey("COMM"), "Log Lean: the class words stay: " + res);
            Check(PlateAlpha(rig) == 0f && TextAlpha(rig) == 0f && Up("cornerShade", rig.Status), "Log Lean: the game's feed is still replaced and the shade is up: " + res);
            Check(Mathf.Abs(PlanF(rig, "FirstRowY") - (PlanF(rig, "Top") + 8f)) < 0.01f, "Log Lean: the first row is 8 under the block's top: " + res);
            CornerShot(rig, "log-lean");
            Set(rig.Status, "Options", PilotOptions("Declutter", Declutter("Minimal")));
            PresentCorners(rig);
            Check(PlanRows(rig) == 3, "Log Minimal: three rows (the pilot's own KILL, the radio call and the chat line): " + PlanRows(rig) + " " + res);
            Check(WordsOfRow(rig, 0)[0] == "KILL" && WordsOfRow(rig, 1)[0] == "COMM" && WordsOfRow(rig, 2)[0] == "COMM", "Log Minimal: ...in that order: " + res);
            Check(!LogWords(rig).ContainsKey("INTCP") && !LogWords(rig).ContainsKey("NET") && !LogWords(rig).ContainsKey("DL EVT LOG"), "Log Minimal: no interceptions, no join lines, no shelf: " + res);
            CornerShot(rig, "log-minimal");
            // Minimal with nothing it keeps: the log is still in charge (the game's feed stays hidden) and draws nothing.
            LogClear();
            LogAdd("<color=#FFD84AFF>PilotOne joined the game</color>", LogNow - 5f);
            PresentCorners(rig);
            Check(PlanRows(rig) == 0 && Up("logRoot", rig.Status) && PlateAlpha(rig) == 0f && TextAlpha(rig) == 0f && LogLabels(rig).Count == 0 && !Up("cornerShade", rig.Status), "Log Minimal: nothing it keeps: nothing drawn, the game's feed still hidden, no shade: " + res);
            Set(rig.Status, "Options", PilotOptions());
            LogSix(rig);

            // --- A row's life: it wipes in over 180 ms, is BRIGHT for 1.5 s, and fades over its last second; the corner notices each step between ticks.
            LogClear();
            LogAdd("<color=#AABBCCFF>Wingman:</color> Splash one, bearing 270", LogNow, 12f, "09:40");
            int Drawn() => LogLabels(rig).Where(l => l.text != "DL EVT LOG").Sum(l => l.text.Length);
            LogFrame(rig, LogNow);
            Check(Drawn() == 0, "Log life: at the instant a line arrives nothing of it is drawn yet: " + Drawn() + " " + res);
            LogFrame(rig, LogNow + 0.09f);
            int half = Drawn();
            Check(half > 0 && half < 30, "Log life: half way through the wipe part of it is: " + half + " " + res);
            LogFrame(rig, LogNow + 0.2f);
            int whole = Drawn();
            Check(whole > half && LogLabel(rig, "SPLASH ONE, BEARING 270") != null, "Log life: after 180 ms all of it: " + whole + " " + res);
            LogFrame(rig, LogNow + 1.0f);
            float lit = Luma(LogLabel(rig, "WINGMAN:").color) * LogLabel(rig, "WINGMAN:").color.a;
            LogFrame(rig, LogNow + 2.0f);
            float settled = Luma(LogLabel(rig, "WINGMAN:").color) * LogLabel(rig, "WINGMAN:").color.a;
            Check(lit > settled + 0.05f, "Log life: BRIGHT at 1 s, INK at 2 s, noticed on a frame between ticks: " + lit + " " + settled + " " + res);
            LogFrame(rig, LogNow + 11.5f);
            Check(Mathf.Abs(LogLabel(rig, "WINGMAN:").color.a - 0.5f) <= 0.12f, "Log life: half faded half way through the last second: " + LogLabel(rig, "WINGMAN:").color.a + " " + res);
            LogFrame(rig, LogNow + 12.1f);
            Check(PlanRows(rig) == 0 && LogLabels(rig).Count == 0, "Log life: gone when the game takes the line off its feed: " + res);

            // --- Opacity is the log's group alone.
            LogSix(rig);
            Set(rig.Status, "Options", PilotOptions("Opacity", 0.5f));
            PresentCorners(rig);
            Check(Mathf.Abs(((CanvasGroup)Field(rig.Status, "logGroup")).alpha - 0.5f) < 0.001f, "Log: Opacity is the log's own group: " + res);
            Set(rig.Status, "Options", PilotOptions());
            PresentCorners(rig);

            // --- The chat input open: the game's feed is whole, the log (and its shade) is gone; closed, they swap back at once.
            rig.Chat.SetActive(true);
            Call(rig.Status, "CornersFrame", true, false);
            Check(TextAlpha(rig) == 1f && PlateAlpha(rig) == 1f && !Up("logRoot", rig.Status) && !Up("cornerShade", rig.Status), "Chat open: the game's text and plate whole, the log and its shade gone: " + res);
            rig.Chat.SetActive(false);
            Call(rig.Status, "CornersFrame", true, false);
            Check(TextAlpha(rig) == 0f && PlateAlpha(rig) == 0f && Up("logRoot", rig.Status) && Up("cornerShade", rig.Status), "Chat closed: the log is back and the game's feed hidden again, on the next frame: " + res);

            // --- A capture that sees nothing: the game's feed has text and the log has none for 0.6 s. Fail open; a row makes it trust itself again.
            LogClear();
            LogFrame(rig, LogNow);
            Check(TextAlpha(rig) == 0f, "Blind: the first moment is within the grace: " + res);
            LogFrame(rig, LogNow + 1f);
            Check(TextAlpha(rig) == 1f && PlateAlpha(rig) == 1f && !Up("logRoot", rig.Status), "Blind: with no row for 0.6 s while the game's feed has text, the game's feed is left whole (fail open): " + res);
            LogAdd("Hawk 2-1: Contact", LogNow + 1f, 60f, "09:41");
            LogFrame(rig, LogNow + 1f);
            Check(TextAlpha(rig) == 0f && PlateAlpha(rig) == 0f && Up("logRoot", rig.Status), "Blind: a row seen, the log is in charge again: " + res);

            // --- The option off (Corners): the game's feed exactly as it was, nothing of the log up.
            LogSix(rig);
            Set(rig.Status, "Options", PilotOptions("Corners", false));
            PresentCorners(rig);
            Check(TextAlpha(rig) == 1f && PlateAlpha(rig) == 1f && !Up("logRoot", rig.Status) && !Up("cornerShade", rig.Status), "Corners off: the game's text alpha is back to 1, the plate too, nothing of the log is up: " + res);
            foreach (TextMeshProUGUI t in rig.Texts) { var g = t.GetComponent<CanvasGroup>(); Check(g == null || g.alpha == 1f, "Corners off: ...no group of ours holds a text down: " + t.name); }
            CornerShot(rig, "log-off");
            Set(rig.Status, "Options", PilotOptions());
            PresentCorners(rig);
            Check(TextAlpha(rig) == 0f && Up("logRoot", rig.Status), "Corners back on: the log is in charge again: " + res);

            // --- Gate shut (a pause, the maximised map): the game's feed is whole on the next frame.
            Set(rig.Status, "cornersSuppressed", true);
            Call(rig.Status, "CornersFrame", true, false);
            Check(TextAlpha(rig) == 1f && PlateAlpha(rig) == 1f && !Up("logRoot", rig.Status), "Gate shut: the game's feed text is whole and the log is down: " + res);
            Set(rig.Status, "cornersSuppressed", false);
            PresentCorners(rig);

            // --- The game's box gone (its lines expired): nothing hidden that is not ours to hide, the log down.
            rig.Darkener.gameObject.SetActive(false);
            PresentCorners(rig);
            Check(!Up("logRoot", rig.Status) && PlateAlpha(rig) == 1f, "Box gone: no log, and the plate is not ours to hide: " + res);
            rig.Darkener.gameObject.SetActive(true);
            PresentCorners(rig);
            Check(Up("logRoot", rig.Status) && TextAlpha(rig) == 0f, "Box back: the log is back: " + res);
        }
        finally
        {
            Call(rig.Status, "Destroy");
            Object.DestroyImmediate(rig.Canvas.gameObject);
            ScopeReset();
        }
        LogTeardown(size);
    }

    /// <summary>Teardown puts the game's text exactly as it was: a group a text object already had keeps its alpha and stays, one the mod added is taken off.</summary>
    private static void LogTeardown(Vector2 size)
    {
        string res = (int)size.x + "x" + (int)size.y;
        CornerRig rig = MakeCorners(size, PilotOptions(), 4, 0f, 0.8f);
        TextMeshProUGUI pre = rig.Texts[1], added = rig.Texts[0];
        var group = pre.GetComponent<CanvasGroup>();
        try
        {
            Check(TextAlpha(rig) == 0f && group.alpha == 0f, "Teardown: both kinds of group hold the text down while the log is in charge: " + res);
            Call(rig.Status, "Destroy");
            Check(group != null && group.alpha == 0.8f, "Teardown: the group the text already had keeps its place and gets its own alpha back, 0.8: " + (group != null ? group.alpha.ToString() : "gone") + " " + res);
            var ours = added.GetComponent<CanvasGroup>();
            Check(ours == null || ours.alpha == 1f, "Teardown: the group the mod added is at alpha 1 (and goes at the end of the frame): " + res);
            Check(added.gameObject.activeSelf && pre.gameObject.activeSelf && rig.Chat != null, "Teardown: no native object was ever switched off: " + res);
            Check(PlateAlpha(rig) == 1f, "Teardown: and the plate is back: " + res);
        }
        finally
        {
            Object.DestroyImmediate(rig.Canvas.gameObject);
            ScopeReset();
        }
    }
}
#endif
