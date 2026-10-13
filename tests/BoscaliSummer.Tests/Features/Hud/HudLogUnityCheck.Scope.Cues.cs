#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// The CUE stack (HUD holo pass E, 2026-10-13): the game's short messages on grey boxes drawn at the left edge, from stand-in native boxes (the corners' rig plus an action report with its grey plate and text, a reserve notice, a funds
// line and a score text, each where the game puts them at the top of the screen). The messages are pushed into the stack's ledger the way the patches push them (CueFeed.Log.Add: the game's rich text, how it was coloured, the game's time for
// it); the stack's own clock is set by the check, so every phase is drawn at a fixed age. The cases: one cue at 0.05 / 0.15 / 0.3 s (the bracket halves snapping in from 46 outside, the decode, the underline, a tone brighter for 0.12 s),
// the hold, the five fade steps, three cues stacked with a fourth in the ring and a new one sliding the others down, a long message (16 units, whole words, no dots), AMBER and RED, Lean (no shelf or class word) and Minimal (only AMBER and
// RED), the option off (the stand-ins' alphas come back exactly), a capture that sees nothing (fail open, one box at a time), a box that was never found, the closed gate, Opacity, Shade and the teardown. Renders cues-<case>-<W>x<H>.png.
// Run alone with -pages cues.
public static partial class HudLogUnityCheck
{
    /// <summary>The cue clock for the check (the game's <c>Time.timeSinceLevelLoad</c> is not ours to set).</summary>
    private const float CueNow = 500f;
    private const string ArmedRich = "Weapon safety lock <b>Disabled</b>", ArmedWords = "WEAPON SAFETY LOCK DISABLED";

    private sealed class CueRig
    {
        public CornerRig C;
        public object Status => C.Status;
        public Vector2 Size => C.Size;
        public float U => C.U;
        public GameObject ActionBox, ReserveBox, AllocBox;
        public TextMeshProUGUI ActionText, ReserveText, AllocText, ScoreText;
    }

    private static object CueLogOf() => Field(Type("BoscaliSummer.Modules.Hud.Patches.CueFeed"), "Log");
    private static void CueClear() => Call(CueLogOf(), "Clear");
    private static object CueSourceOf(string name) => EnumOf("CueSource", name);

    /// <summary>A message as the patches hand it over: the game's rich text, how its colour marked it, when it came and how long the game keeps it.</summary>
    private static void CueAdd(string source, string rich, string severity, float arrival, float seconds = 5f) =>
        Call(CueLogOf(), "Add", CueSourceOf(source), rich, EnumOf("CueSeverity", severity), arrival, seconds);

    private static float Timeline(string method, params object[] args) => Convert.ToSingle(Type(HudDomain + "CueTimeline").GetMethod(method, All).Invoke(null, args));

    /// <summary>The underline's visible span as fractions of the width at game time <paramref name="age"/> of a <paramref name="life"/> second cue.</summary>
    private static void UnderlineOf(float age, float life, out float from, out float to)
    {
        object[] args = { age, life, 0f, 0f };
        Type(HudDomain + "CueTimeline").GetMethod("Underline", All).Invoke(null, args);
        from = (float)args[2];
        to = (float)args[3];
    }

    private static TextMeshProUGUI StandInText(Transform parent, string name, string text, Color color, float size, TextAlignmentOptions align)
    {
        TMP_FontAsset sans = TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset : HudFont();
        var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(parent, false);
        label.font = sans; label.fontSharedMaterial = sans.material; label.fontSize = size; label.richText = true; label.raycastTarget = false;
        label.alignment = align; label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Overflow;
        label.color = color; label.text = text;
        var rect = label.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        return label;
    }

    /// <summary>A native box at the top centre of the screen the way the game anchors them: pivot top, a grey plate of its own Image, the text as a child.</summary>
    private static GameObject StandInBox(CueRig rig, string name, float y, Vector2 size, Color plate, out TextMeshProUGUI label, string text, Color ink)
    {
        float s = rig.C.S;
        var box = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)box.transform;
        rect.SetParent(rig.C.Canvas.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = size * s;
        rect.anchoredPosition = new Vector2(0f, -y * s);
        var image = box.GetComponent<Image>();
        image.color = plate; image.raycastTarget = false;
        label = StandInText(box.transform, "Text", text, ink, 18f * s, TextAlignmentOptions.Midline);
        return box;
    }

    /// <summary>The corners' rig with the four game boxes stood in: nothing in them until a case puts something there (the game's boxes are empty or off when they have nothing to say).</summary>
    private static CueRig MakeCues(Vector2 size, object options, bool score = true, float reserveGroupAlpha = -1f)
    {
        CueClear();
        var rig = new CueRig { C = MakeCorners(size, options) };
        Set(rig.Status, "cueClockOverride", CueNow);
        float s = rig.C.S;
        var grey = new Color(0.27f, 0.28f, 0.30f, 0.85f);
        rig.ActionBox = StandInBox(rig, "aircraftActions(Clone)", 100f, new Vector2(420f, 44f), grey, out rig.ActionText, "", new Color(1f, 0.85f, 0.3f));
        rig.ReserveBox = StandInBox(rig, "reserveReport", 29.5f, new Vector2(150f, 50f), grey, out rig.ReserveText, "+1", Color.green);
        rig.AllocBox = StandInBox(rig, "AllocationDisplay", 0f, new Vector2(360f, 30f), grey, out rig.AllocText, "$12.3M (+$803.7k)", Color.white);
        rig.ReserveBox.SetActive(false);
        rig.AllocBox.SetActive(false);
        rig.ScoreText = StandInText(rig.C.Canvas.transform, "killText", "", Color.white, 20f * s, TextAlignmentOptions.Midline);
        var scoreRect = rig.ScoreText.rectTransform;
        scoreRect.anchorMin = scoreRect.anchorMax = scoreRect.pivot = new Vector2(0.5f, 1f);
        scoreRect.sizeDelta = new Vector2(360f, 30f) * s; scoreRect.anchoredPosition = new Vector2(0f, -170f * s);
        if (reserveGroupAlpha >= 0f) rig.ReserveBox.AddComponent<CanvasGroup>().alpha = reserveGroupAlpha;      // a group the game's own object already had
        Call(rig.Status, "AdoptCue", CueSourceOf("Action"), rig.ActionText, null, new[] { rig.ActionBox, rig.ActionText.gameObject });
        Call(rig.Status, "AdoptCue", CueSourceOf("Reserve"), null, rig.ReserveBox, new[] { rig.ReserveBox });
        Call(rig.Status, "AdoptCue", CueSourceOf("Allocation"), null, rig.AllocBox, new[] { rig.AllocBox });
        if (score) Call(rig.Status, "AdoptCue", CueSourceOf("Score"), rig.ScoreText, null, new[] { rig.ScoreText.gameObject });
        return rig;
    }

    /// <summary>The game's box of one source as it would be while that message shows: text in the action report and the score line, the object up for the other two.</summary>
    private static void NativeSays(CueRig rig, string source, bool on, string text = null)
    {
        switch (source)
        {
            case "Action": rig.ActionText.text = on ? text ?? ArmedRich : ""; break;
            case "Score": rig.ScoreText.text = on ? text ?? "Detected units +1,0" : ""; break;
            case "Reserve": rig.ReserveBox.SetActive(on); break;
            default: rig.AllocBox.SetActive(on); break;
        }
    }

    /// <summary>One frame of the board's per-frame pass at game time <paramref name="age"/> seconds after <see cref="CueNow"/>.</summary>
    private static void CueAt(CueRig rig, float age)
    {
        Set(rig.Status, "cueClockOverride", CueNow + age);
        Call(rig.Status, "CornersFrame", true, false);
        BackdropBack(rig.C.Canvas);
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>The check's sky goes first in the canvas again (the game's own world is not a canvas element): a shade tree made in the frame is first of all.</summary>
    private static void BackdropBack(Canvas canvas)
    {
        foreach (string name in new[] { "Dash", "Sky", "Cockpit" })
        {
            Transform t = canvas.transform.Find(name);
            if (t != null) t.SetAsFirstSibling();
        }
    }

    /// <summary>A present (the 10 Hz tick: options taken, everything drawn again) at the same moment.</summary>
    private static void CuePresent(CueRig rig, float age)
    {
        Set(rig.Status, "cueClockOverride", CueNow + age);
        PresentCorners(rig.C);
    }

    private static void CueEnd(CueRig rig)
    {
        Call(rig.Status, "Destroy");
        Object.DestroyImmediate(rig.C.Canvas.gameObject);
        ScopeReset();
        CueClear();
    }

    private static bool CueUp(CueRig rig) => Up("cueRoot", rig.Status);

    private static List<TextMeshProUGUI> CueLabels(CueRig rig)
    {
        var list = new List<TextMeshProUGUI>();
        var surface = (RectTransform)Field(rig.Status, "cueSurface");
        if (surface != null)
            foreach (TextMeshProUGUI label in surface.GetComponentsInChildren<TextMeshProUGUI>())
                if (label.gameObject.activeInHierarchy && !string.IsNullOrEmpty(label.text)) list.Add(label);
        return list;
    }

    private static string Plain(TextMeshProUGUI label) => MarkUp.Replace(label.text, "");

    private static List<Rect> CueQuadsOf(CueRig rig)
    {
        var list = new List<Rect>();
        var vector = (AvVector)Field(rig.Status, "cueVector");
        if (vector == null) return list;
        AvQuadBuffer buffer = vector.Buffer;
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

    private static int CueDrawn(CueRig rig) => (int)Field(rig.Status, "cueCount");
    private static int CueCharge(CueRig rig) => (int)Field(rig.Status, "cueCharge");

    /// <summary>What the last paint drew for slot <paramref name="i"/>: a field of the stack's CueSpot (design units), or NaN for an empty slot.</summary>
    private static float Spot(CueRig rig, int i, string field)
    {
        object spot = ((Array)Field(rig.Status, "cueSpots")).GetValue(i);
        return spot.GetType().GetField("Cue").GetValue(spot) == null ? float.NaN : Convert.ToSingle(spot.GetType().GetField(field).GetValue(spot));
    }

    private static string SpotText(CueRig rig, int i)
    {
        object spot = ((Array)Field(rig.Status, "cueSpots")).GetValue(i);
        object cue = spot.GetType().GetField("Cue").GetValue(spot);
        return cue == null ? null : (string)cue.GetType().GetField("Text").GetValue(cue);
    }

    /// <summary>The label at a design y (middle) within a few pixels, nearest first: the shelf's caption (the slot's top + 6) or the message (+ 22, or + 14 with no shelf), after the nudge the canvas gives a word.</summary>
    private static TextMeshProUGUI CueLabelAt(CueRig rig, float designY, float em = 12f)
    {
        var canvasRect = (RectTransform)rig.C.Canvas.transform;
        float y = (designY - em * 0.07f) * rig.U;
        return CueLabels(rig).Where(l => Mathf.Abs(CanvasRectOf(canvasRect, l.rectTransform).center.y - y) <= 4.5f * rig.U).OrderBy(l => Mathf.Abs(CanvasRectOf(canvasRect, l.rectTransform).center.y - y)).FirstOrDefault();
    }

    private static TextMeshProUGUI CueMessage(CueRig rig, int i, bool shelf = true) => CueLabelAt(rig, Spot(rig, i, "Y") + (shelf ? 21f : 13f), SpotText(rig, i) != null && SpotText(rig, i).Length > 32 ? 16f : 20f);
    private static TextMeshProUGUI CueCaption(CueRig rig, int i) => CueLabelAt(rig, Spot(rig, i, "Y") + 4f);

    private static float GroupAlpha(Component c) { var g = c.GetComponent<CanvasGroup>(); return g != null ? g.alpha : 1f; }
    private static float NativeAlpha(CueRig rig, string source) =>
        source == "Action" ? Mathf.Min(GroupAlpha(rig.ActionBox.transform), GroupAlpha(rig.ActionText))
        : source == "Reserve" ? GroupAlpha(rig.ReserveBox.transform) : source == "Allocation" ? GroupAlpha(rig.AllocBox.transform) : GroupAlpha(rig.ScoreText);

    private static void CueShot(CueRig rig, string name)
    {
        string tag = "cues-" + name + "-" + (int)rig.Size.x + "x" + (int)rig.Size.y;
        foreach (string root in new[] { "cueShade", "cueRoot" })
        {
            var tree = (RectTransform)Field(rig.Status, root);
            if (tree != null) foreach (Graphic g in tree.GetComponentsInChildren<Graphic>(true)) Check(!g.raycastTarget, "Cues take no click: " + g.name + " " + tag);
        }
        var vector = (AvVector)Field(rig.Status, "cueVector");
        Check(vector == null || !vector.Buffer.Overflowed, "The cue buffer did not fill: " + tag);
        Capture(rig.C.Canvas, tag + ".png");
    }

    private static bool Near(float a, float b, float tol) => Mathf.Abs(a - b) <= tol;

    /// <summary>The rule every drawn cue keeps true at game time <paramref name="age"/> (its arrival at slot place <paramref name="i"/>): its brackets stand where the timeline puts them, hugging the words 8 outside, and its words are as wide as they say.</summary>
    private static void CueGeometry(CueRig rig, int i, float age, float life, bool shelf, string tag)
    {
        float u = rig.U, y = Spot(rig, i, "Y"), tx = Spot(rig, i, "TextX"), w = Spot(rig, i, "TextW"), lbar = Spot(rig, i, "LeftBar"), rbar = Spot(rig, i, "RightBar");
        float offset = Timeline("BracketOffset", age, life), lift = shelf ? 0f : 8f;
        Check(Near(lbar, tx - 8f - offset, 0.05f) && Near(rbar, tx + w + 8f + offset, 0.05f), "Cue " + i + ": the bracket halves stand " + offset + " outside the words' hug (" + (tx - 8f) + " / " + (tx + w + 8f) + "): " + lbar + " " + rbar + " " + tag);
        Check(Near(tx, 28f, 0.01f), "Cue " + i + ": the words start at 16 + 12: " + tx + " " + tag);
        List<Rect> quads = CueQuadsOf(rig);
        float top = (y + 12f - lift) * u, bottom = (y + 31f - lift) * u;
        Check(HasLine(quads, false, lbar * u, top, bottom) && HasLine(quads, false, rbar * u, top, bottom), "Cue " + i + ": a bar for each bracket half, 20 high: " + tag);
        Check(HasLine(quads, true, top, lbar * u, (lbar + 5f) * u) && HasLine(quads, true, bottom, lbar * u, (lbar + 5f) * u), "Cue " + i + ": the left half's arms point right: " + tag);
        Check(HasLine(quads, true, top, (rbar - 5f) * u, rbar * u) && HasLine(quads, true, bottom, (rbar - 5f) * u, rbar * u), "Cue " + i + ": the right half's arms point left: " + tag);
    }

    private static string DecodedOf(string text, int locked) => text.Substring(0, locked);

    /// <summary>The message label's text against the real words at game time <paramref name="age"/>: the locked part is real, the rest glyphs from A-Z 0-9, the spaces are the real spaces.</summary>
    private static void CueDecoded(CueRig rig, int i, float age, string text, string tag)
    {
        TextMeshProUGUI label = CueMessage(rig, i, CueCaption(rig, i) != null);
        Check(label != null, "Cue " + i + ": the message is drawn: " + tag);
        if (label == null) return;
        string shown = Plain(label);
        int locked = (int)Spot(rig, i, "Locked");
        Check(shown.Length == text.Length, "Cue " + i + ": the decode is as long as the message: '" + shown + "' " + tag);
        if (shown.Length != text.Length) return;
        Check(shown.Substring(0, locked) == DecodedOf(text, locked), "Cue " + i + ": the first " + locked + " characters have locked: '" + shown + "' " + tag);
        for (int c = locked; c < text.Length; c++)
            Check(text[c] == ' ' ? shown[c] == ' ' : "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".IndexOf(shown[c]) >= 0, "Cue " + i + ": character " + c + " is a glyph (a space stays one): '" + shown + "' " + tag);
    }

    private static void ScopeCues()
    {
        foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720) })
        {
            CueOne(size);
            CueStack(size);
            CueMessages(size);
            CueDeclutter(size);
            CueFailOpen(size);
            CueLive(size);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------- one cue

    private static void CueOne(Vector2 size)
    {
        string res = (int)size.x + "x" + (int)size.y;
        CueRig rig = MakeCues(size, PilotOptions());
        float u = rig.U;
        try
        {
            float top = Mathf.Round(size.y / u * 0.30f);
            CueAdd("Action", ArmedRich, "Normal", CueNow);
            NativeSays(rig, "Action", true);
            CueAt(rig, 0.05f);
            Check(CueUp(rig) && Up("cueShade", rig.Status) && CueDrawn(rig) == 1, "One cue: the stack and its shade are up: " + res);
            Check(CueCharge(rig) == 15, "One cue: all four game boxes are held and in charge (15): " + CueCharge(rig) + " " + res);
            Check(NativeAlpha(rig, "Action") == 0f && NativeAlpha(rig, "Reserve") == 0f && NativeAlpha(rig, "Allocation") == 0f && NativeAlpha(rig, "Score") == 0f, "One cue: the game's four boxes are hidden through groups of ours: " + res);
            Check(rig.ActionText.gameObject.activeInHierarchy && rig.ActionBox.activeSelf && rig.ActionText.color.a > 0.99f && rig.ActionText.text == ArmedRich, "One cue: ...the game's action box is still on, its text and colour untouched (only not seen): " + res);
            Check(Near(Spot(rig, 0, "Y"), top, 0.01f), "One cue: the slot's top is at 30 % of the screen's height (" + top + "): " + Spot(rig, 0, "Y") + " " + res);

            // SHOW at 0.05 s: the halves are outside, 12 characters... most of the message is still scrambling, the underline has started, the whole cue is white-hot.
            CueGeometry(rig, 0, 0.05f, 5f, true, "show 0.05 " + res);
            CueDecoded(rig, 0, 0.05f, ArmedWords, "show 0.05 " + res);
            TextMeshProUGUI msg = CueMessage(rig, 0), cap = CueCaption(rig, 0);
            Check(msg != null && cap != null && Plain(cap) == "WPN", "Show 0.05: the shelf's class word is WPN: " + res);
            Check(msg.color.r > 0.97f && msg.color.g > 0.97f && msg.color.b > 0.97f && (msg.fontStyle & FontStyles.Bold) != 0, "Show 0.05: the message is bold and a tone brighter than BRIGHT (white) for the first 0.12 s: " + msg.color + " " + res);
            Check(cap.color.a > 0.99f && Luma(cap.color) > Luma(new Color(0.5f, 0.87f, 0.6f)) - 0.05f, "Show 0.05: ...the class word a tone brighter too (INK, not DIM): " + cap.color + " " + res);
            Check(Spot(rig, 0, "Locked") > 0f && Spot(rig, 0, "Locked") < ArmedWords.Length, "Show 0.05: part of it has locked: " + Spot(rig, 0, "Locked") + " " + res);
            List<Rect> quads = CueQuadsOf(rig);
            float hugL = Spot(rig, 0, "TextX") - 8f, hugR = Spot(rig, 0, "TextX") + Spot(rig, 0, "TextW") + 8f;
            UnderlineOf(0.05f, 5f, out float ulFrom, out float ulTo);
            Check(ulFrom == 0f && ulTo > 0.3f && ulTo < 0.7f && HasLine(quads, true, (top + 31f) * u, hugL * u, (hugL + (hugR - hugL) * ulTo) * u) && !HasLine(quads, true, (top + 31f) * u, hugL * u, (hugR - 1f) * u), "Show 0.05: the underline has drawn " + ulTo + " of the way from the left: " + res);
            CueShot(rig, "show-005");

            // 0.15 s: the halves hug, more has locked, the colour is BRIGHT again.
            CueAt(rig, 0.15f);
            CueGeometry(rig, 0, 0.15f, 5f, true, "show 0.15 " + res);
            CueDecoded(rig, 0, 0.15f, ArmedWords, "show 0.15 " + res);
            msg = CueMessage(rig, 0);
            Check(Near(Spot(rig, 0, "LeftBar"), Spot(rig, 0, "TextX") - 8f, 0.01f), "Show 0.15: the halves have snapped in and hug the words: " + res);
            Check(msg.color.b < 0.97f && Luma(msg.color) > 0.8f, "Show 0.15: BRIGHT, the flash is over: " + msg.color + " " + res);
            CueShot(rig, "show-015");

            // 0.3 s: every character has locked.
            CueAt(rig, 0.3f);
            CueGeometry(rig, 0, 0.3f, 5f, true, "show 0.3 " + res);
            Check(Plain(CueMessage(rig, 0)) == ArmedWords, "Show 0.3: the message reads WEAPON SAFETY LOCK DISABLED: '" + Plain(CueMessage(rig, 0)) + "' " + res);
            CueShot(rig, "show-030");

            // HOLD: nothing moves; the shelf is DIM; the underline is whole; the message is AMBER (a caution: the weapons are armed).
            CueAt(rig, 1f);
            CueGeometry(rig, 0, 1f, 5f, true, "hold " + res);
            quads = CueQuadsOf(rig);
            msg = CueMessage(rig, 0); cap = CueCaption(rig, 0);
            Color amber = new Color32(255, 216, 74, 255);
            Check(Near(msg.color.r, amber.r, 0.03f) && Near(msg.color.g, amber.g, 0.03f) && Near(msg.color.b, amber.b, 0.03f) && (msg.fontStyle & FontStyles.Bold) != 0, "Hold: weapons ARMED is bold AMBER: " + msg.color + " " + res);
            Check(cap.color.a < 0.8f && cap.color.a > 0.6f && (cap.fontStyle & FontStyles.Bold) == 0, "Hold: the class word is DIM (INK at 0.70) and regular: " + cap.color + " " + res);
            Check(HasLine(quads, true, (top + 31f) * u, hugL * u, hugR * u), "Hold: the underline is whole: " + res);
            Check(HasLine(quads, true, (top + 4f) * u, 16f * u, 24f * u), "Hold: the shelf's hairline starts at x 16: " + res);
            Check(quads.Any(q => Near(q.width, 4f * u, 1.2f) && Near(q.height, 4f * u, 1.2f) && Near(q.center.y, (top + 4f) * u, 2f)), "Hold: ...and ends in a 4 x 4 node: " + res);
            Check(Mathf.Abs(Spot(rig, 0, "TextW") - Plain(msg).Length * 13f) < 0.01f && Spot(rig, 0, "TextW") <= 420f, "Hold: 27 characters at 20 are 351 wide, inside 420: " + Spot(rig, 0, "TextW") + " " + res);
            CueShot(rig, "hold");

            // FADE: the underline retracts to the right, the halves spread out, the words step 1, 0.55, 0.85, 0.3, then nothing.
            float[] steps = { 1f, 0.55f, 0.85f, 0.3f };
            for (int s = 0; s < 4; s++)
            {
                float at = 4.65f + 0.07f * s + 0.035f;
                CueAt(rig, at);
                msg = CueMessage(rig, 0);
                Check(msg != null && Near(msg.color.a, steps[s], 0.02f), "Fade step " + s + " (" + at + " s): the words' alpha is " + steps[s] + ": " + (msg != null ? msg.color.a.ToString() : "none") + " " + res);
                CueGeometry(rig, 0, at, 5f, true, "fade step " + s + " " + res);
                if (s == 2)
                {
                    quads = CueQuadsOf(rig);
                    Check(HasLine(quads, true, (top + 31f) * u, (hugL + (hugR - hugL) * 0.3f) * u, hugR * u) && !HasLine(quads, true, (top + 31f) * u, hugL * u, (hugL + (hugR - hugL) * 0.15f) * u), "Fade step 2: the underline has retracted toward the right (a quarter of the width gone): " + res);
                    Check(Near(Spot(rig, 0, "LeftBar"), hugL - 11.5f, 0.1f), "Fade step 2: the halves are 11.5 outside again (half the fade, ease-in): " + Spot(rig, 0, "LeftBar") + " " + res);
                    CueShot(rig, "fade-2");
                }
            }
            CueAt(rig, 4.965f);
            Check(CueDrawn(rig) == 1 && CueMessage(rig, 0) == null, "Fade step 4: nothing of the words is drawn (alpha 0): " + res);
            NativeSays(rig, "Action", false);
            CueAt(rig, 5.05f);
            Check(CueDrawn(rig) == 0 && CueLabels(rig).Count == 0, "Gone at the game's time: the stack is empty: " + res);
            Check(NativeAlpha(rig, "Action") == 0f && CueUp(rig), "Gone: the stack stays in charge of the game's boxes (they have nothing to say): " + res);
            // A very short line is held for 1.2 s.
            CueClear();
            CueAdd("Action", "Tail Hook Deployed", "Normal", CueNow + 10f, 0.4f);
            NativeSays(rig, "Action", true, "Tail Hook Deployed");
            CueAt(rig, 10.9f);
            Check(CueDrawn(rig) == 1 && CueMessage(rig, 0) != null && Plain(CueMessage(rig, 0)) == "TAIL HOOK DEPLOYED", "A 0.4 s line is still up at 0.9 s (held to 1.2 s), steady: " + res);
            CueAt(rig, 11.3f);
            Check(CueDrawn(rig) == 0, "...and gone after 1.2 s: " + res);
        }
        finally { CueEnd(rig); }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------- the stack

    private static void CueStack(Vector2 size)
    {
        string res = (int)size.x + "x" + (int)size.y;
        CueRig rig = MakeCues(size, PilotOptions());
        float u = rig.U;
        try
        {
            float top = Mathf.Round(size.y / u * 0.30f);
            CueAdd("Action", "Landing gear down", "Normal", CueNow, 8f);
            CueAdd("Reserve", "FS-20 RESERVE GRANTED", "Normal", CueNow + 0.5f, 8f);
            CueAdd("Score", "Detected units +1,0", "Normal", CueNow + 1f, 8f);
            CueAdd("Allocation", "<color=#FFFFFFFF>$12.3M</color> <color=#80FF80FF>(+$803.7k)</color>", "Normal", CueNow + 1.5f, 8f);
            NativeSays(rig, "Action", true, "Landing gear down"); NativeSays(rig, "Reserve", true); NativeSays(rig, "Score", true); NativeSays(rig, "Allocation", true);
            CueAt(rig, 4f);
            Check(CueDrawn(rig) == 3, "Stack: four cues in the ring, at most three drawn: " + CueDrawn(rig) + " " + res);
            string[] words = { "$12.3M (+$803.7K)", "DETECTED UNITS +1,0", "FS-20 RESERVE GRANTED" }, classes = { "FUNDS", "FUNDS", "RSV" };
            for (int i = 0; i < 3; i++)
            {
                Check(Near(Spot(rig, i, "Y"), top + 34f * i, 0.01f), "Stack: cue " + i + " is " + i + " slots under the top (34 apart): " + Spot(rig, i, "Y") + " " + res);
                Check(SpotText(rig, i) == words[i], "Stack: newest on top: cue " + i + " reads '" + words[i] + "': '" + SpotText(rig, i) + "' " + res);
                Check(Plain(CueCaption(rig, i) ?? CueMessage(rig, i)) == classes[i], "Stack: cue " + i + "'s class word is " + classes[i] + " " + res);
                Check(CueMessage(rig, i) != null && Plain(CueMessage(rig, i)) == words[i], "Stack: cue " + i + "'s message is drawn in capitals: '" + (CueMessage(rig, i) != null ? Plain(CueMessage(rig, i)) : "none") + "' " + res);
                CueGeometry(rig, i, 4f - (i == 0 ? 1.5f : i == 1 ? 1f : 0.5f), 8f, true, "stack " + i + " " + res);
            }
            Check(CueLabels(rig).All(l => Plain(l) != "SYS" && !Plain(l).Contains("LANDING")), "Stack: the fourth, oldest cue is not drawn: " + res);
            Check(Spot(rig, 2, "Y") + 34f < (size.y - 320f * rig.C.S) / u && Spot(rig, 0, "Y") > 100f, "Stack: under the event log and over the corner map: " + res);
            Check(Mathf.Abs(CanvasRectOf((RectTransform)rig.C.Canvas.transform, CueCaption(rig, 0).rectTransform).xMin - 28f * u) <= 6f * u, "Stack: anchored at the left, the shelf's caption 16 + 12 in: " + res);
            var patch = (Image)Field(rig.Status, "cuePatch");
            Rect patchBox = CanvasRectOf((RectTransform)rig.C.Canvas.transform, patch.rectTransform);
            Check(patch.gameObject.activeInHierarchy && patch.color.a > 0.3f && patchBox.yMin <= (Spot(rig, 0, "Y") - 20f) * u && patchBox.yMax >= (Spot(rig, 2, "Y") + 30f) * u && patchBox.xMin < 16f * u && patchBox.xMax > (Spot(rig, 2, "TextX") + Spot(rig, 2, "TextW")) * u, "Stack: one soft shade behind all three, as wide and as tall as they are, its core under the ink: " + patchBox + " " + patch.color.a + " " + res);
            Check(NativeAlpha(rig, "Action") == 0f && NativeAlpha(rig, "Reserve") == 0f && NativeAlpha(rig, "Allocation") == 0f && NativeAlpha(rig, "Score") == 0f, "Stack: every game box is hidden: " + res);
            CueShot(rig, "stack3");

            // A new cue slides the others down a slot over 0.12 s.
            CueAdd("Action", "Radar Armed", "Normal", CueNow + 4f, 8f);
            NativeSays(rig, "Action", true, "Radar Armed");
            CueAt(rig, 4f);
            Check(Near(Spot(rig, 0, "Y"), top, 0.01f) && Near(Spot(rig, 1, "Y"), top, 0.01f), "Slide: at the instant a cue arrives the others have not moved: " + Spot(rig, 1, "Y") + " " + res);
            Check(SpotText(rig, 0) == "RADAR ARMED" && SpotText(rig, 1) == "$12.3M (+$803.7K)", "Slide: the new cue is on top: " + res);
            CueAt(rig, 4.06f);
            Check(Near(Spot(rig, 1, "Y"), top + 34f * 0.875f, 1f) && Near(Spot(rig, 2, "Y"), top + 34f + 34f * 0.875f, 1f) , "Slide: 0.06 s in, the others are 7/8 of the way down: " + Spot(rig, 1, "Y") + " " + Spot(rig, 2, "Y") + " " + res);
            CueShot(rig, "slide");
            CueAt(rig, 4.2f);
            Check(Near(Spot(rig, 1, "Y"), top + 34f, 0.5f) && Near(Spot(rig, 2, "Y"), top + 68f, 0.5f), "Slide: and a slot down at 0.12 s: " + res);
        }
        finally { CueEnd(rig); }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------- the words

    private static void CueMessages(Vector2 size)
    {
        string res = (int)size.x + "x" + (int)size.y;
        CueRig rig = MakeCues(size, PilotOptions());
        float u = rig.U;
        try
        {
            // A long message: 90 characters drop to 16 units, then lose whole words, no dots, and stay inside 420.
            const string Long = "Reinforcements are inbound from the northern staging area with fuel and ammunition for all squadrons";
            CueAdd("Action", Long, "Normal", CueNow);
            NativeSays(rig, "Action", true, Long);
            CueAt(rig, 1f);
            string cut = SpotText(rig, 0);
            Check(cut == "REINFORCEMENTS ARE INBOUND FROM THE" && !cut.Contains(".") && !cut.EndsWith(" "), "Long: whole words up to the room, no dots: '" + cut + "' " + res);
            Check(Spot(rig, 0, "TextW") <= 420f && Mathf.Abs(Spot(rig, 0, "TextW") - cut.Length * 10.4f) < 0.01f, "Long: at 16 units, inside 420: " + Spot(rig, 0, "TextW") + " " + res);
            Check(CueMessage(rig, 0) != null && Plain(CueMessage(rig, 0)) == cut, "Long: the message is drawn whole: " + res);
            CueGeometry(rig, 0, 1f, 5f, true, "long " + res);
            CueShot(rig, "long");

            // 33 characters: 16 units, nothing lost.
            CueClear();
            CueAdd("Action", "Stability Assist enabled for all axes", "Normal", CueNow);
            CueAt(rig, 1f);
            Check(SpotText(rig, 0) == "STABILITY ASSIST ENABLED FOR ALL AXES" && Near(Spot(rig, 0, "TextW"), 37 * 10.4f, 0.01f), "Long: 37 characters stay whole at 16: " + SpotText(rig, 0) + " " + res);

            // AMBER and RED, with their class words.
            CueClear();
            CueAdd("Action", "<color=#FFA094FF>Engine fire</color>", "Alert", CueNow + 0.5f);
            CueAdd("Action", ArmedRich, "Normal", CueNow + 1f);
            CueAdd("Action", "Weapon safety lock <b>Enabled</b>", "Normal", CueNow + 1.5f);
            NativeSays(rig, "Action", true);
            CueAt(rig, 2.5f);
            Color amber = new Color32(255, 216, 74, 255), red = new Color32(255, 160, 148, 255);
            TextMeshProUGUI safe = CueMessage(rig, 0), armed = CueMessage(rig, 1), fire = CueMessage(rig, 2);
            Check(safe != null && armed != null && fire != null, "Tones: three messages are drawn: " + res);
            Check(Plain(CueCaption(rig, 0)) == "WPN" && Plain(CueCaption(rig, 1)) == "WPN" && Plain(CueCaption(rig, 2)) == "DMG", "Tones: SAFE and ARMED are WPN, the failure DMG: " + res);
            Check(Near(armed.color.r, amber.r, 0.03f) && Near(armed.color.g, amber.g, 0.03f) && (armed.fontStyle & FontStyles.Bold) != 0, "Tones: ARMED is bold AMBER: " + armed.color + " " + res);
            Check(Near(fire.color.r, red.r, 0.03f) && Near(fire.color.g, red.g, 0.03f) && Near(fire.color.b, red.b, 0.03f) && (fire.fontStyle & FontStyles.Bold) != 0, "Tones: an alert-coloured failure is bold RED: " + fire.color + " " + res);
            Check(Luma(safe.color) > Luma(armed.color) && safe.color.b > 0.8f && (safe.fontStyle & FontStyles.Bold) != 0, "Tones: SAFE is bold BRIGHT: " + safe.color + " " + res);
            List<Rect> quads = CueQuadsOf(rig);
            float lbar = Spot(rig, 2, "LeftBar") * u, y = Spot(rig, 2, "Y");
            Check(quads.Any(q => Near(q.center.x, lbar, 1.6f) && q.height > 15f * u * 0.9f), "Tones: the red cue's brackets are drawn: " + res);
            CueShot(rig, "amber-red");

            // A money line: FUNDS; digits and the decimal point are the face's own.
            NativeSays(rig, "Action", false);
            CueClear();
            CueAdd("Allocation", "<color=#FFFFFFFF>$12.3M</color> <color=#FF8080FF>(-$803.7k)</color>", "Caution", CueNow);
            NativeSays(rig, "Allocation", true);
            CueAt(rig, 1f);
            Check(Plain(CueCaption(rig, 0)) == "FUNDS" && Near(CueMessage(rig, 0).color.r, amber.r, 0.03f), "Funds: a loss is a FUNDS caution (AMBER): " + res);
        }
        finally { CueEnd(rig); }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------- declutter and options

    private static void CueDeclutter(Vector2 size)
    {
        string res = (int)size.x + "x" + (int)size.y;
        CueRig rig = MakeCues(size, PilotOptions("Declutter", Declutter("Lean")));
        float u = rig.U;
        try
        {
            float top = Mathf.Round(size.y / u * 0.30f);
            CueAdd("Action", "Landing gear down", "Normal", CueNow);
            CueAdd("Action", ArmedRich, "Normal", CueNow + 0.5f);
            NativeSays(rig, "Action", true);
            CueAt(rig, 2f);
            CuePresent(rig, 2f);
            List<Rect> quads = CueQuadsOf(rig);
            Check(CueDrawn(rig) == 2 && CueLabels(rig).All(l => Plain(l) != "WPN" && Plain(l) != "SYS"), "Lean: both cues, no class word: " + string.Join("|", CueLabels(rig).Select(Plain)) + " " + res);
            Check(!quads.Any(q => Near(q.width, 4f * u, 1.2f) && Near(q.height, 4f * u, 1.2f)), "Lean: no shelf, no node: " + res);
            Check(CueMessage(rig, 0, false) != null && Plain(CueMessage(rig, 0, false)) == ArmedWords, "Lean: the message sits where the shelf was (8 higher): " + res);
            Check(HasLine(quads, false, Spot(rig, 0, "LeftBar") * u, (top + 4f) * u, (top + 23f) * u), "Lean: the brackets stay: " + res);
            CueShot(rig, "lean");
            Set(rig.Status, "Options", PilotOptions("Declutter", Declutter("Minimal")));
            CuePresent(rig, 2f);
            Check(CueDrawn(rig) == 1 && SpotText(rig, 0) == ArmedWords, "Minimal: only the AMBER cue: " + CueDrawn(rig) + " " + res);
            Check(CueLabels(rig).All(l => Plain(l) != "WPN"), "Minimal: no class word: " + res);
            CueShot(rig, "minimal");
            CueAdd("Action", "<color=#FFA094FF>Engine fire</color>", "Alert", CueNow + 2f);
            CueAt(rig, 2.5f);
            Check(CueDrawn(rig) == 2 && SpotText(rig, 0) == "ENGINE FIRE", "Minimal: and a RED one: " + res);
        }
        finally { CueEnd(rig); }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------- fail open

    private static void CueFailOpen(Vector2 size)
    {
        string res = (int)size.x + "x" + (int)size.y;
        // The option off: the game's boxes exactly as they were, a group they already had at its own alpha, none of ours holding anything down.
        CueRig rig = MakeCues(size, PilotOptions(), true, 0.8f);
        try
        {
            CueAdd("Action", ArmedRich, "Normal", CueNow);
            CueAdd("Reserve", "FS-20 RESERVE GRANTED", "Normal", CueNow, 8f);
            NativeSays(rig, "Action", true); NativeSays(rig, "Reserve", true);
            CueAt(rig, 1f);
            Check(NativeAlpha(rig, "Action") == 0f && NativeAlpha(rig, "Reserve") == 0f && CueUp(rig), "Option: with Corners on the stand-ins are hidden: " + res);
            Set(rig.Status, "Options", PilotOptions("Corners", false));
            PresentCorners(rig.C);
            Check(NativeAlpha(rig, "Action") == 1f && NativeAlpha(rig, "Allocation") == 1f && NativeAlpha(rig, "Score") == 1f, "Option: Corners off, the game's boxes are whole (alpha 1): " + res);
            Check(NativeAlpha(rig, "Reserve") == 0.8f, "Option: ...and a group the game's own object had is at its own alpha, 0.8: " + NativeAlpha(rig, "Reserve") + " " + res);
            Check(!CueUp(rig) && !Up("cueShade", rig.Status) && CueCharge(rig) == 0, "Option: nothing of the stack is up: " + res);
            Check(rig.ActionBox.activeSelf && rig.ActionText.gameObject.activeSelf && rig.ReserveBox.activeSelf, "Option: no native object was ever switched off by the mod: " + res);
            CueShot(rig, "off");
            Set(rig.Status, "Options", PilotOptions());
            CuePresent(rig, 1f);
            Check(NativeAlpha(rig, "Action") == 0f && CueUp(rig) && CueDrawn(rig) == 2, "Option: back on, the stack is back and the boxes hidden again: " + res);

            // The closed gate (a pause, the maximised map): the boxes are the game's on the next frame.
            Set(rig.Status, "cornersSuppressed", true);
            Call(rig.Status, "CornersFrame", true, false);
            Check(NativeAlpha(rig, "Action") == 1f && !CueUp(rig), "Gate shut: the game's boxes whole, the stack down: " + res);
            Set(rig.Status, "cornersSuppressed", false);
            CuePresent(rig, 1f);
            Check(NativeAlpha(rig, "Action") == 0f && CueUp(rig), "Gate open: the stack is back: " + res);

            // Opacity is the stack's group alone; Shade 0 draws no shade.
            Set(rig.Status, "Options", PilotOptions("Opacity", 0.5f));
            CuePresent(rig, 1f);
            Check(Near(((CanvasGroup)Field(rig.Status, "cueGroup")).alpha, 0.5f, 0.001f), "Opacity: the stack's own group follows it: " + res);
            Set(rig.Status, "Options", PilotOptions("Shade", 0f));
            CuePresent(rig, 1f);
            Check(!Up("cueShade", rig.Status) && CueUp(rig) && Near(((CanvasGroup)Field(rig.Status, "cueGroup")).alpha, 1f, 0.001f), "Shade 0: no shade, the cues stay: " + res);
            Set(rig.Status, "Options", PilotOptions("Shade", 1f));
            CuePresent(rig, 1f);
            Check(Up("cueShade", rig.Status) && ((Image)Field(rig.Status, "cuePatch")).color.a > 0.5f, "Shade 1: a stronger shade: " + ((Image)Field(rig.Status, "cuePatch")).color.a + " " + res);
            Set(rig.Status, "Options", PilotOptions());
            CuePresent(rig, 1f);

            // The radio module's notices for the ticker ("sys" channel): the stack takes them while the action report's cues are drawn (once: the ticker never gets them), and leaves them to the ticker when it is not.
            System.Type feed = Type("BoscaliSummer.Modules.Hud.Patches.CueFeed");
            MethodInfo take = feed.GetMethod("TakeNotice", All);
            CueClear();
            CuePresent(rig, 1f);
            Check((int)Field(feed, "Charge") == 15, "Ticker: with the stack in charge of all four boxes the charge the board publishes is 15: " + (int)Field(feed, "Charge") + " " + res);
            Check((bool)take.Invoke(null, new object[] { "RADAR ARMED", Tone("Info") }) && (int)Field(CueLogOf(), "count") == 1, "Ticker: a notice from the radio module becomes a cue, and the ticker does not get it: " + res);
            CueClear();
            Check((bool)take.Invoke(null, new object[] { "ENGINE FIRE", Tone("Caution") }), "Ticker: ...an alert-coloured one (the radio module's Caution tone) too: " + res);
            object cue = CueLogOf().GetType().GetProperty("Item").GetValue(CueLogOf(), new object[] { 0 });
            Check(cue.GetType().GetField("Severity").GetValue(cue).ToString() == "Alert" && cue.GetType().GetField("Class").GetValue(cue).ToString() == "Dmg" && (string)cue.GetType().GetField("Text").GetValue(cue) == "ENGINE FIRE", "Ticker: ...a RED DMG cue: " + res);
            Check(!(bool)take.Invoke(null, new object[] { "", Tone("Info") }) && !(bool)take.Invoke(null, new object[] { null, Tone("Info") }), "Ticker: no words, nothing taken: " + res);
            CueClear();
            Set(rig.Status, "Options", PilotOptions("Corners", false));
            PresentCorners(rig.C);
            Check((int)Field(feed, "Charge") == 0 && !(bool)take.Invoke(null, new object[] { "RADAR ARMED", Tone("Info") }) && (int)Field(CueLogOf(), "count") == 0, "Ticker: Corners off, the stack is in charge of nothing and the notice stays the ticker's: " + res);
            Set(rig.Status, "Options", PilotOptions());
            PresentCorners(rig.C);
        }
        finally { CueEnd(rig); }

        // A capture that sees nothing: the game's action report has text and the stack has none for 0.6 s. That box alone is the game's again; a message another box shows is still drawn; a seen message puts it back.
        rig = MakeCues(size, PilotOptions());
        try
        {
            NativeSays(rig, "Action", true);
            CueAdd("Reserve", "FS-20 RESERVE GRANTED", "Normal", CueNow, 30f);
            NativeSays(rig, "Reserve", true);
            CueAt(rig, 0.3f);
            Check(NativeAlpha(rig, "Action") == 0f, "Blind: the first moment is within the grace: " + res);
            CueAt(rig, 1f);
            Check(NativeAlpha(rig, "Action") == 1f && NativeAlpha(rig, "Reserve") == 0f && CueDrawn(rig) == 1, "Blind: no Action cue for 0.6 s while its box has text, the game's box is whole; the reserve notice is still drawn and its box hidden: " + res);
            Check(((int)Field(rig.Status, "cueCharge") & 1) == 0 && ((int)Field(rig.Status, "cueCharge") & 2) != 0, "Blind: the Action bit is out of charge: " + CueCharge(rig) + " " + res);
            CueShot(rig, "blind");
            CueAdd("Action", ArmedRich, "Normal", CueNow + 1f);
            CueAt(rig, 1.1f);
            Check(NativeAlpha(rig, "Action") == 0f && CueDrawn(rig) == 2, "Blind: a message seen, the stack is in charge of that box again: " + res);
        }
        finally { CueEnd(rig); }

        // A box that was not found (never adopted): its messages are not drawn, its text is not touched.
        rig = MakeCues(size, PilotOptions(), false);
        try
        {
            CueAdd("Score", "Detected units +1,0", "Normal", CueNow);
            CueAdd("Action", ArmedRich, "Normal", CueNow);
            NativeSays(rig, "Score", true); NativeSays(rig, "Action", true);
            CueAt(rig, 1f);
            Check(CueDrawn(rig) == 1 && SpotText(rig, 0) == ArmedWords, "Not found: the score line has no box held, so only the action report is drawn: " + CueDrawn(rig) + " " + res);
            Check(rig.ScoreText.GetComponent<CanvasGroup>() == null && rig.ScoreText.color.a > 0.99f && (CueCharge(rig) & 8) == 0, "Not found: ...and the game's score text is not touched: " + res);
            CueShot(rig, "notfound");
        }
        finally { CueEnd(rig); }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------------------------------- live: the board's life

    private static void CueLive(Vector2 size)
    {
        string res = (int)size.x + "x" + (int)size.y;
        // The sway: the stack's roots move with the board's by whole pixels.
        CueRig rig = MakeCues(size, PilotOptions());
        try
        {
            CueAdd("Action", ArmedRich, "Normal", CueNow);
            NativeSays(rig, "Action", true);
            CueAt(rig, 1f);
            var root = (RectTransform)Field(rig.Status, "cueRoot");
            var shade = (RectTransform)Field(rig.Status, "cueShade");
            Call(rig.Status, "ApplySway", 2.4f / rig.U, -1.2f / rig.U);
            Check(root.offsetMin == new Vector2(2f, -1f) && shade.offsetMax == new Vector2(2f, -1f), "Sway: the stack and its shade move with the board, by whole pixels: " + root.offsetMin + " " + res);
            Call(rig.Status, "ApplySway", 0f, 0f);
            Check(root.offsetMin == Vector2.zero, "Sway: and back: " + res);

            // Steady frames draw nothing again; the clock running back (a new scene) empties the stack and gives the box back.
            int sig = (int)Field(rig.Status, "cueSig");
            CueAt(rig, 1.2f);
            CueAt(rig, 1.4f);
            Check((int)Field(rig.Status, "cueSig") == sig, "Steady: a hold changes no signature, so nothing is drawn again: " + res);
            NativeSays(rig, "Action", false);
            Set(rig.Status, "cueClockOverride", 3f);
            Call(rig.Status, "CornersFrame", true, false);
            Check(CueDrawn(rig) == 0 && (int)Field(CueLogOf(), "count") == 0, "A new scene (the clock ran back): the ring is empty: " + res);

            // The master switch off leaves the corners, and the stack with them.
            CueAdd("Action", ArmedRich, "Normal", CueNow);
            NativeSays(rig, "Action", true);
            Set(rig.Status, "cueClockOverride", CueNow);
            Set(rig.Status, "Options", PilotOptions("Enabled", false));
            PresentCorners(rig.C);
            Check(CueUp(rig) && NativeAlpha(rig, "Action") == 0f, "The master switch off: the corners stay, and so do the cues: " + res);
            Call(rig.Status, "Hide");
            Check(!CueUp(rig) && NativeAlpha(rig, "Action") == 1f, "Hide (the module going away): the stack is down and the game's box whole: " + res);
        }
        finally { CueEnd(rig); }

        // The teardown puts the game's boxes exactly as they were.
        rig = MakeCues(size, PilotOptions(), true, 0.8f);
        TextMeshProUGUI score = rig.ScoreText;
        GameObject action = rig.ActionBox, reserve = rig.ReserveBox;
        bool reserveWas = reserve.activeSelf, actionWas = action.activeSelf, scoreWas = score.gameObject.activeSelf;
        try
        {
            CueAdd("Action", ArmedRich, "Normal", CueNow);
            NativeSays(rig, "Action", true);
            CueAt(rig, 1f);
            Check(NativeAlpha(rig, "Action") == 0f && NativeAlpha(rig, "Reserve") == 0f, "Teardown: both kinds of group hold the boxes down while the stack is up: " + res);
            Call(rig.Status, "Destroy");
            var ours = action.GetComponent<CanvasGroup>();
            Check(ours == null || ours.alpha == 1f, "Teardown: a group the mod added is at alpha 1 (and goes at the end of the frame): " + res);
            var theirs = reserve.GetComponent<CanvasGroup>();
            Check(theirs != null && theirs.alpha == 0.8f, "Teardown: the group the object already had keeps its place and gets its own alpha back, 0.8: " + (theirs != null ? theirs.alpha.ToString() : "gone") + " " + res);
            Check(score.gameObject.activeSelf == scoreWas && action.activeSelf == actionWas && reserve.activeSelf == reserveWas, "Teardown: no native object was switched on or off by the mod: " + res);
            Check((int)Field(CueLogOf(), "count") == 0, "Teardown: the ledger is the next scene's: " + res);
        }
        finally
        {
            Object.DestroyImmediate(rig.C.Canvas.gameObject);
            ScopeReset();
            CueClear();
        }
    }
}
#endif
