#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// The wing side of the TAC BOARD on real page fixtures: the lamp line, the page's header shelf and brackets, the gauge line and the ticker (LAYOUT.md 3.4 to 3.7), at the declutter levels and in the three arrangements.
// Renders lines-<case>-<W>x<H>.png (not named like a page shot: the compare sheet leaves them out). Run alone with -pages lines.
//   lean-threat / minimal-threat   a live page under LEAN and MINIMAL: no shelf and no title (the headline stays), the lit lamps packed (MINIMAL: the threat lamps only), no gauge line
//   gauges-threat                  four gauges in two rows of the gauge line, every word whole
//   notice-red-x3                  a coalesced red notice: "x3" apart at the right end, the words clear of it (WIDE, STACK, 1280 x 720); once it reads no "x" at all
//   threat-lamps-only              the lamp line with only the threat lamps, over the cruise strip's absence (MINIMAL on cruise with a lock)
//   shot-timeline                  the SHOT page's strip: seven chips with names of five characters at most, no ellipsis (LAYOUT.md 12.3)
//   (no shot)                      a translucent ring fill is no ink on a page surface: the quads of ScopeCanvas.Ring (LAYOUT.md 9 rule 1)
public static partial class HudLogUnityCheck
{
    private static void ScopeLines()
    {
        var hd = new Vector2(1920, 1080);
        LinesShot("lean-threat", hd, "threat", 6f, PilotOptions("Declutter", Declutter("Lean")), null, (status, tag) =>
        {
            List<string> words = BoardWords(status);
            foreach (string w in new[] { "FA", "RDR", "LOCK", "9 S" }) Check(words.Contains(w), tag + ": " + w + " is drawn (a lit lamp, the headline)");
            foreach (string w in new[] { "GEAR", "HOVR", "NVG", "SAFE", "ENG", "SPKE", "THREAT", "ACE HUNT" }) Check(!words.Contains(w), tag + ": " + w + " is not drawn (an unlit lamp, the title, the gauge line)");
            Check(((NOAvionics.AvVector)Field(status, "frameVector")).Buffer.Count > 0, tag + ": the page keeps its four brackets");
            Check(WordInk(status, "FA", out Rect fa) && Mathf.Abs(fa.x - (LampsLeft(status) + 7f)) < 3f, tag + ": the first lit lamp starts at the lamp line's left end, packed: " + fa.x);
        });
        LinesShot("minimal-threat", hd, "threat", 6f, PilotOptions("Declutter", Declutter("Minimal")), null, (status, tag) =>
        {
            List<string> words = BoardWords(status);
            Check(words.Contains("LOCK") && words.Exists(w => w.StartsWith("MSL")) && words.Contains("9 S"), tag + ": the threat lamps and the headline are drawn");
            foreach (string w in new[] { "GEAR", "FA", "RDR", "HOVR", "SAFE", "SPKE", "THREAT" }) Check(!words.Contains(w), tag + ": " + w + " is not drawn");
            Check(WordInk(status, "LOCK", out Rect lk) && lk.xMax > LampsLeft(status) + 300f, tag + ": the threat lamps sit against the right end: " + lk.xMax);
        });
        LinesShot("gauges-threat", hd, "threat", 6f, PilotOptions(), null, (status, tag) =>
        {
            List<string> words = BoardWords(status);
            Check(words.Contains("THREAT") && words.Contains("9 S") && words.Contains("GEAR") && words.Contains("SPKE"), tag + ": at Full the shelf has its title and its headline, and the unlit lamps are drawn");
            TextMeshProUGUI title = LabelOf(status, "THREAT"), gear = LabelOf(status, "GEAR");
            Check(title != null && (title.fontStyle & FontStyles.Bold) != 0 && title.color.a > 0.99f, tag + ": the page title is bold BRIGHT");
            Check(gear != null && (gear.fontStyle & FontStyles.Bold) == 0 && gear.color.a < 0.75f, tag + ": an unlit lamp's word is regular DIM");
            // The decimal point (LAYOUT.md 0): in "9.5KM" the 5 starts 0.45 em after the dot's cell, so 0.65 + 0.45 em (13.2 at 12) after the 9, not 2 x 0.65 (15.6).
            TextMeshProUGUI km = ParsedLabel(status, "9.5KM");
            Check(km != null, tag + ": a word with a decimal point is on the page");
            if (km != null)
            {
                var root = (RectTransform)Field(status, "root");
                TMP_TextInfo info = km.textInfo;
                float step = (root.InverseTransformPoint(km.transform.TransformPoint(new Vector3(info.characterInfo[2].origin, 0f, 0f))).x - root.InverseTransformPoint(km.transform.TransformPoint(new Vector3(info.characterInfo[0].origin, 0f, 0f))).x) / (float)Field(status, "unit");
                Check(Mathf.Abs(step - 13.2f) < 0.8f, tag + ": the 5 follows the 9 by 13.2 units: " + step);
            }
            bool onFirst = WordInk(status, "EMITTER", out Rect first), onThird = WordInk(status, "ACE HUNT", out Rect third);
            Check(onFirst && onThird, tag + ": the first and the third gauge are on the line");
            Check(Mathf.Abs(third.y - first.y - 18f) < 1.5f && Mathf.Abs(third.x - first.x) < 1.5f, tag + ": the third gauge starts the second row, 18 under the first: " + (third.y - first.y) + ", " + (third.x - first.x));
            foreach (string w in words) Check(!w.Contains("…") && !w.Contains("..."), tag + ": nothing is cut: " + w);
        });
        foreach (var arrangement in new (string name, Vector2 size, object options)[] { ("wide", hd, PilotOptions()), ("stack", hd, PilotOptions("Layout", LayoutOf("Stack"))), ("720", new Vector2(1280, 720), PilotOptions()) })
            foreach (int count in new[] { 3, 1 })
                if (count == 3 || arrangement.name == "wide")
                    LinesShot("notice-red-x" + count + (arrangement.name == "wide" ? "" : "-" + arrangement.name), arrangement.size, "threat", 6f, arrangement.options, scene =>
                    {
                        scene.SetFrame("HasTicker", true);
                        scene.SetFrame("Ticker", scene.Fill(scene.Domain("TickerLine"), "Tone", Tone("Warning"), "Channel", "events", "Text", "BASE UNDER ATTACK - AIRBASE_CITY", "Count", count, "Age", 0.5f));
                    }, (status, tag) =>
                    {
                        List<string> words = BoardWords(status);
                        Check(words.Contains("WARNING · BASE UNDER ATTACK - AIRBASE_CITY"), tag + ": the red notice reads as words, the count is not among them");
                        if (count == 1) { Check(!words.Exists(w => w.Length > 1 && w[0] == 'x' && char.IsDigit(w[1])), tag + ": a notice that came once has no count"); return; }
                        float right = LampsLeft(status) + 440f;
                        Check(WordInk(status, "x3", out Rect x3) && Mathf.Abs(x3.xMax - right) < 2.5f, tag + ": x3 ends at the line's right end: " + x3.xMax + " / " + right);
                        Check(WordInk(status, "WARNING · BASE UNDER ATTACK - AIRBASE_CITY", out Rect text) && text.xMax < x3.x - 8f, tag + ": the words end clear of the count: " + text.xMax + " / " + x3.x);
                    });
        LinesShot("threat-lamps-only", hd, "systems", 2f, PilotOptions("Declutter", Declutter("Minimal")), scene => scene.SetFrame("Threat", ShellLamps(true, 2)), (status, tag) =>
        {
            List<string> words = BoardWords(status);
            Check(words.Contains("LOCK") && words.Contains("MSL 2"), tag + ": the lock and the missile lamps are drawn");
            foreach (string w in new[] { "GEAR", "FA", "RDR", "SPKE", "SYSTEMS" }) Check(!words.Contains(w), tag + ": " + w + " is not drawn");
            Check(!Shown(status, "scopeRect"), tag + ": Minimal draws no cruise strip");
            Check(WordInk(status, "MSL 2", out Rect m) && Mathf.Abs(m.xMax - (LampsLeft(status) + 440f)) < 2.5f, tag + ": the last threat lamp ends at the line's right end: " + m.xMax);
        });
        LinesShot("shot-timeline", hd, "shot", 6f, PilotOptions(), null, (status, tag) =>
        {
            List<string> words = BoardWords(status);
            foreach (string w in new[] { "SKYWY", "GBU12", "AAM4", "DART" }) Check(words.Contains(w), tag + ": the timeline's chip reads " + w);
            foreach (string w in words) Check(!w.Contains("…") && !w.Contains("..."), tag + ": nothing is cut with an ellipsis: " + w);
        });
        PageRing();
        ScopeReset();
    }

    /// <summary>A ring's translucent disc on a page surface (LAYOUT.md 9 rule 1: a path fill at 0.45 or under is dropped, at any size) draws no quad; on a surface that is not open, and a strong fill, it does.</summary>
    private static void PageRing()
    {
        object tone = ToneOfScope("Ink");
        int Quads(bool open, float fillAlpha)
        {
            var quads = new NOAvionics.AvQuadBuffer(2000);
            object canvas = Activator.CreateInstance(Type("BoscaliSummer.Modules.Hud.Presentation.ScopeCanvas"), quads, null, "ring-check");
            canvas.GetType().GetProperty("Open", All).SetValue(canvas, open);
            var rgb = new NOAvionics.Rgba(0.5f, 0.87f, 0.6f);
            Call(canvas, "Begin", Activator.CreateInstance(Type(HudDomain + "ScopePalette"), rgb, rgb, rgb), 1f, 100f, 1f);
            Call(canvas, "Ring", 50f, 50f, 20f, tone, 1f, 0f, tone, fillAlpha, 0f, 0f, 0f);      // no outline: only the disc
            return quads.Count;
        }
        Check(Quads(true, 0.07f) == 0 && Quads(true, 0.45f) == 0, "A page's translucent ring fill (a zone, a seeker cone, the inner ring of a dock) draws no disc");
        Check(Quads(true, 0.7f) > 0, "...a strong fill stays");
        Check(Quads(false, 0.07f) > 0, "...and the shell's own surfaces keep theirs");
    }

    /// <summary>One board at one moment of a page's scenario under the pilot's options, with the stores header, optionally changed by <paramref name="more"/> before it is presented, judged by <paramref name="judge"/> and captured.</summary>
    private static void LinesShot(string name, Vector2 size, string page, float t, object options, Action<ScopeScene> more, Action<object, string> judge)
    {
        MakeBoard(size, t, out Canvas canvas, out object status, out object frame);
        Set(status, "Options", options);
        foreach (float before in PreRoll(page, t))
        {
            var rehearsal = new ScopeScene(before, size, frame);
            Fixture(page, before, rehearsal);
            StoresFixture(page, before, rehearsal);
            Set(frame, "Now", before);
            Call(status, "Present", frame);
        }
        Set(frame, "Now", t);
        var scene = new ScopeScene(t, size, frame);
        Fixture(page, t, scene);
        StoresFixture(page, t, scene);
        more?.Invoke(scene);
        Call(status, "Present", frame);
        Canvas.ForceUpdateCanvases();
        string tag = "lines-" + name + "-" + (int)size.x + "x" + (int)size.y;
        ScopeChecks(status, tag);
        judge(status, tag);
        Capture(canvas, tag + ".png");
        Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
    }

    /// <summary>The first label on the board showing exactly <paramref name="text"/>, or null.</summary>
    private static TextMeshProUGUI LabelOf(object status, string text)
    {
        foreach (TextMeshProUGUI label in ((RectTransform)Field(status, "root")).GetComponentsInChildren<TextMeshProUGUI>())
            if (label.gameObject.activeInHierarchy && label.text == text) return label;
        return null;
    }

    /// <summary>The first label whose characters, the markup left out, are <paramref name="parsed"/> (a word with a decimal point carries spacing tags in its text), or null.</summary>
    private static TextMeshProUGUI ParsedLabel(object status, string parsed)
    {
        foreach (TextMeshProUGUI label in ((RectTransform)Field(status, "root")).GetComponentsInChildren<TextMeshProUGUI>())
        {
            if (!label.gameObject.activeInHierarchy) continue;
            label.ForceMeshUpdate();
            if (label.GetParsedText() == parsed) return label;
        }
        return null;
    }

    /// <summary>The box x of the wing's left edge (the lamp line's, the gauge line's and the ticker's): 476 in WIDE, 4 in STACK.</summary>
    private static float LampsLeft(object status) => Field(Field(status, "Options"), "Layout").ToString() == "Stack" ? 4f : 476f;

    /// <summary>The ink of the word <paramref name="text"/> in box design units (x to the right and y down from the box's top-left corner; the rectangle is the drawn glyphs'), false when it is not on the board.</summary>
    private static bool WordInk(object status, string text, out Rect ink)
    {
        var root = (RectTransform)Field(status, "root");
        float unit = (float)Field(status, "unit");
        ink = default;
        foreach (TextMeshProUGUI label in root.GetComponentsInChildren<TextMeshProUGUI>())
        {
            if (!label.gameObject.activeInHierarchy || label.text != text) continue;
            label.ForceMeshUpdate();
            TMP_TextInfo info = label.textInfo;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < info.characterCount; i++)
            {
                TMP_CharacterInfo c = info.characterInfo[i];
                if (!c.isVisible) continue;
                foreach (Vector3 corner in new[] { c.bottomLeft, c.topRight })
                {
                    Vector3 local = root.InverseTransformPoint(label.transform.TransformPoint(corner));
                    float x = (local.x - root.rect.xMin) / unit, y = (root.rect.yMax - local.y) / unit;
                    x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                }
            }
            if (x0 > x1) continue;
            ink = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
        }
        return false;
    }
}
#endif
