#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// The TAC BOARD's other two corners (LAYOUT.md 7, the option Corners) and the visor's sway (LAYOUT.md 10), from stand-in native objects: the kill feed's box (a "ChatBackground" that wears the dark plate as its OWN Image, with a "Darkener"
// child whose Image is switched OFF, as in the game's scene, and four lines of text, 475 x 90 at 1080p, at the top-left; or one line in a box 198 x 34 as the game drew it in its first run) and the minimised map's plate (320 x 320 at the bottom-left; the backdrop already has the game's own map drawn in it, which the brackets are to sit round). The check judges where the
// brackets and the shelves are DRAWN (the quads of the furniture surface and the words on it) against LAYOUT.md 7 and the stand-in rectangles, that the plate is hidden while the board stands in for it and given back
// whenever it does not (the option off, the feed gone, the master switch, teardown), and that the sway moves the board and the corners together by whole pixels and puts them back. Renders corners-<case>-<W>x<H>.png.
// HUD holo pass C (2026-10-12): the TAC MAP caption now stands INSIDE the frame's lower left (an edge marker cannot sit on it), a north tick follows the map's turn (the game's mapImage z rotation is the heading), and
// the dressing is hidden whole while HudMapState.HoloActive is set. Run alone with -pages corners. (The canvas of this check is in pixels, one canvas unit to a pixel: the native rectangles are authored in the native units times the screen's height over 1080, as the game's are.)
public static partial class HudLogUnityCheck
{
    private sealed class CornerRig
    {
        public Canvas Canvas;
        public object Status, Frame;
        public RectTransform FeedBox, MapPlate;
        /// <summary>The stand-in for DynamicMap.mapImage: the game turns it by the heading (z, counter-clockwise).</summary>
        public Transform MapImage;
        public Image Darkener, Plate;
        public TextMeshProUGUI First;
        /// <summary>The stand-in for MessageUI.chat (inactive until the pilot types) and the stand-ins for its two text objects.</summary>
        public GameObject Chat;
        public readonly List<TextMeshProUGUI> Texts = new List<TextMeshProUGUI>();
        public Vector2 Size;
        /// <summary>Canvas units (pixels here) to a native unit (the screen's height over 1080) and to a design unit (never under 0.85).</summary>
        public float S, U;
    }

    /// <summary>The board with a stand-in kill feed and map plate adopted as the native corners, presented once with <paramref name="options"/>.</summary>
    private static CornerRig MakeCorners(Vector2 size, object options, int lineCount = 4, float heading = 0f, float preGroupAlpha = -1f)
    {
        var rig = new CornerRig { Size = size, S = size.y / 1080f, U = Mathf.Max(size.y / 1080f, 0.85f) };
        MakeBoard(size, 2f, out rig.Canvas, out rig.Status, out rig.Frame);
        var scene = new ScopeScene(2f, size, rig.Frame);
        FixtureSystems(2f, scene);
        StoresFixture("systems", 2f, scene);

        // The feed: the game's box at the top-left (anchored top, pivot top-left). The dark rounded plate the game draws is the Image on the box ITSELF (read from the scene's assets and seen in game: it held the texts as children, so a group
        // on it would hide them too); the child called Darkener, stretched inside it (6 less each way), has its Image switched OFF there. The stand-in has both so. Four lines as the mock draws them, or one as the game drew its first line.
        var box = new GameObject("ChatBackground", typeof(RectTransform), typeof(Image));
        rig.FeedBox = (RectTransform)box.transform;
        rig.FeedBox.SetParent(rig.Canvas.transform, false);
        rig.FeedBox.anchorMin = rig.FeedBox.anchorMax = rig.FeedBox.pivot = new Vector2(0f, 1f);
        rig.FeedBox.anchoredPosition = Vector2.zero;
        rig.FeedBox.sizeDelta = (lineCount == 1 ? new Vector2(198f, 34f) : new Vector2(475f, 90f)) * rig.S;
        rig.Plate = box.GetComponent<Image>();
        rig.Plate.color = new Color(0.02f, 0.03f, 0.04f, 0.78f);
        rig.Plate.raycastTarget = false;
        var dark = new GameObject("Darkener", typeof(RectTransform), typeof(Image));
        dark.transform.SetParent(box.transform, false);
        var darkRect = (RectTransform)dark.transform;
        darkRect.anchorMin = Vector2.zero; darkRect.anchorMax = Vector2.one; darkRect.sizeDelta = new Vector2(-6f, -6f) * rig.S;
        rig.Darkener = dark.GetComponent<Image>();
        rig.Darkener.color = new Color(0.02f, 0.03f, 0.04f, 0.78f);
        rig.Darkener.raycastTarget = false;
        rig.Darkener.enabled = false;
        TMP_FontAsset sans = TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset : HudFont();
        string[][] lines =
        {
            new[] { "23mm AAA Emplacement", "#ff4d4d", "12.7mm MG Emplacement", "#58a8ff" }, new[] { "12.7mm MG Emplacement", "#58a8ff", "AT-145 Emplacement", "#ff4d4d" },
            new[] { "AT-145 Emplacement", "#ff4d4d", "12.7mm MG Emplacement", "#58a8ff" }, new[] { "23mm AAA Emplacement", "#58a8ff", "23mm AAA Emplacement", "#ff4d4d" },
        };
        float[] centres = { 21f, 39f, 58f, 76f };
        for (int i = 0; i < (lineCount == 1 ? 1 : lines.Length); i++)
        {
            var label = new GameObject("Line" + i, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(box.transform, false);
            label.font = sans; label.fontSharedMaterial = sans.material; label.fontSize = 15f * rig.S; label.richText = true; label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.MidlineLeft; label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Overflow;
            label.text = lineCount == 1 ? "<color=#33ff44>ANumeric joined the game</color>"
                : "<color=" + lines[i][1] + ">" + lines[i][0] + "</color> demolished <color=" + lines[i][3] + ">" + lines[i][2] + "</color>";      // the game's own: the names are tagged, the verb between them is not
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(465f, 20f) * rig.S;
            rect.anchoredPosition = new Vector2(6f * rig.S, -((lineCount == 1 ? 15f : centres[i]) - 10f) * rig.S);      // the game's one line: its capitals at y 9 to 21, centre 15
            if (i == 0) rig.First = label;
            rig.Texts.Add(label);
        }
        // A group a feed text already had (the teardown case): the mod reuses it and gives its alpha back.
        if (preGroupAlpha >= 0f) rig.Texts[1].gameObject.AddComponent<CanvasGroup>().alpha = preGroupAlpha;
        // The chat input, inactive as the game has it until the pilot opens it.
        rig.Chat = new GameObject("ChatBox", typeof(RectTransform));
        rig.Chat.transform.SetParent(box.transform, false);
        rig.Chat.SetActive(false);
        foreach (TextMeshProUGUI t in rig.Texts) t.ForceMeshUpdate();      // the game's text has been laid out: the log reads its length
        // The map's plate: an invisible rectangle where the game's is (the backdrop has the map itself), anchored bottom-left.
        var plate = new GameObject("mapBackground", typeof(RectTransform));
        rig.MapPlate = (RectTransform)plate.transform;
        rig.MapPlate.SetParent(rig.Canvas.transform, false);
        rig.MapPlate.anchorMin = rig.MapPlate.anchorMax = rig.MapPlate.pivot = Vector2.zero;
        rig.MapPlate.anchoredPosition = Vector2.zero;
        rig.MapPlate.sizeDelta = new Vector2(320f, 320f) * rig.S;
        var image = new GameObject("mapImage", typeof(RectTransform));
        rig.MapImage = image.transform;
        rig.MapImage.SetParent(rig.MapPlate, false);
        rig.MapImage.localEulerAngles = new Vector3(0f, 0f, heading);

        Call(rig.Status, "AdoptCorners", rig.Darkener, rig.MapPlate, rig.MapImage, rig.Texts.ToArray<TMP_Text>(), rig.Chat);
        Set(rig.Status, "logClockOverride", LogNow);
        Set(rig.Status, "Options", options);
        SeedLog(rig);
        PresentCorners(rig);
        return rig;
    }

    /// <summary>Present and put the backdrop back at the bottom (the game's is the 3D world: the corner shade is the first thing in the canvas, under every mark but over nothing).</summary>
    private static void PresentCorners(CornerRig rig)
    {
        Call(rig.Status, "Present", rig.Frame);
        foreach (string name in new[] { "Dash", "Sky", "Cockpit" })
        {
            Transform t = rig.Canvas.transform.Find(name);
            if (t != null) t.SetAsFirstSibling();
        }
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>Tear the board down (what the scene change does) and return the feed plate's alpha as it was left.</summary>
    private static float EndCorners(CornerRig rig)
    {
        Call(rig.Status, "Destroy");
        float alpha = PlateAlpha(rig);
        Object.DestroyImmediate(rig.Canvas.gameObject);
        ScopeReset();
        return alpha;
    }

    /// <summary>A rectangle in canvas units from the canvas's top-left corner (y down), through its world corners.</summary>
    private static Rect CanvasRectOf(RectTransform canvas, RectTransform r)
    {
        var c = new Vector3[4];
        r.GetWorldCorners(c);
        Vector3 bl = canvas.InverseTransformPoint(c[0]), tr = canvas.InverseTransformPoint(c[2]);
        Rect cr = canvas.rect;
        return Rect.MinMaxRect(bl.x - cr.xMin, cr.yMax - tr.y, tr.x - cr.xMin, cr.yMax - bl.y);
    }

    /// <summary>The quads of the furniture surface as rectangles from the canvas's top-left (y down).</summary>
    private static List<Rect> CornerQuads(CornerRig rig)
    {
        AvQuadBuffer buffer = ((AvVector)Field(rig.Status, "cornerVector")).Buffer;
        float height = rig.Size.y;
        var list = new List<Rect>();
        for (int q = 0; q < buffer.Count; q++)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int v = 0; v < 4; v++)
            {
                x0 = Mathf.Min(x0, buffer.X[q * 4 + v]); x1 = Mathf.Max(x1, buffer.X[q * 4 + v]);
                y0 = Mathf.Min(y0, buffer.Y[q * 4 + v]); y1 = Mathf.Max(y1, buffer.Y[q * 4 + v]);
            }
            list.Add(Rect.MinMaxRect(x0, height - y1, x1, height - y0));
        }
        return list;
    }

    /// <summary>A thin horizontal line at <paramref name="y"/> covering at least <paramref name="from"/> to <paramref name="to"/> (or a thin vertical one at <paramref name="x"/>), within a pixel and a half.</summary>
    private static bool HasLine(List<Rect> quads, bool horizontal, float at, float from, float to)
    {
        const float tol = 1.6f;
        foreach (Rect r in quads)
        {
            if (horizontal && r.height <= 2.5f && Mathf.Abs(r.center.y - at) <= tol && r.xMin <= from + tol && r.xMax >= to - tol) return true;
            if (!horizontal && r.width <= 2.5f && Mathf.Abs(r.center.x - at) <= tol && r.yMin <= from + tol && r.yMax >= to - tol) return true;
        }
        return false;
    }

    /// <summary>The active words on the furniture surface: text and the rectangle of its glyph box from the canvas's top-left.</summary>
    private static Dictionary<string, Rect> CornerWords(CornerRig rig)
    {
        var words = new Dictionary<string, Rect>();
        var canvasRect = (RectTransform)rig.Canvas.transform;
        foreach (TextMeshProUGUI label in ((RectTransform)Field(rig.Status, "cornerSurface")).GetComponentsInChildren<TextMeshProUGUI>())
            if (label.gameObject.activeInHierarchy && !string.IsNullOrEmpty(label.text)) words[label.text] = CanvasRectOf(canvasRect, label.rectTransform);
        return words;
    }

    private static bool Up(string field, object status) { var r = (RectTransform)Field(status, field); return r != null && r.gameObject.activeInHierarchy; }

    /// <summary>How visible the feed's dark plate still is: the least of our group on the Darkener and the alpha of the box's own Image renderer (the plate the game really draws).</summary>
    private static float PlateAlpha(CornerRig rig) { var g = rig.Darkener.GetComponent<CanvasGroup>(); return Mathf.Min(g != null ? g.alpha : 1f, rig.Plate.canvasRenderer.GetAlpha()); }

    /// <summary>The pixel at (x, y) from the top-left of a PNG the check wrote.</summary>
    private static Color32 PngPixel(string png, int x, int y)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        ImageConversion.LoadImage(texture, File.ReadAllBytes(png));
        Color32 c = texture.GetPixel(x, texture.height - 1 - y);
        Object.DestroyImmediate(texture);
        return c;
    }

    private static int PixelGap(Color32 a, Color32 b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

    /// <summary>
    /// One line in a box 198 x 34, the game's first run (a capital reaches from y 9 to 21; the first capture had the brackets' arm through the "A" and the dark box still round the line): the furniture stays off the first
    /// letter, and the plate is gone for real, measured on the render itself against the same scene with no plate at all (the box's own Image taken out), and against the plate shown, so the check can tell.
    /// </summary>
    private static void CornerOneLine(CornerRig rig)
    {
        string res = (int)rig.Size.x + "x" + (int)rig.Size.y;
        var canvasRect = (RectTransform)rig.Canvas.transform;
        Check(PlateAlpha(rig) == 0f && rig.Plate.canvasRenderer.GetAlpha() == 0f, "One line: the dark plate the game draws, the box's own Image, is hidden: " + res);
        Check(!rig.Darkener.enabled, "...and the Darkener's Image stays off as the game has it (no native Image is ever switched on): " + res);
        rig.First.ForceMeshUpdate();
        TMP_CharacterInfo letter = rig.First.textInfo.characterInfo[0];
        Vector3 bl = canvasRect.InverseTransformPoint(rig.First.transform.TransformPoint(letter.bottomLeft)), tr = canvasRect.InverseTransformPoint(rig.First.transform.TransformPoint(letter.topRight));
        Rect cr = canvasRect.rect;
        Rect ink = Rect.MinMaxRect(bl.x - cr.xMin, cr.yMax - tr.y, tr.x - cr.xMin, cr.yMax - bl.y);
        Check(letter.character == 'A' && ink.yMin >= 6f * rig.S && ink.yMax <= 24f * rig.S, "One line: the stand-in's first letter is where the game's was (y " + ink.yMin / rig.S + " to " + ink.yMax / rig.S + "): " + res);
        foreach (Rect q in CornerQuads(rig))
            Check(!(q.xMin < ink.xMax - 0.5f && q.xMax > ink.xMin + 0.5f && q.yMin < ink.yMax - 0.5f && q.yMax > ink.yMin + 0.5f), "One line: the furniture stays off the first letter: " + q + " on " + ink + " " + res);
        rig.Plate.color = new Color(0.9f, 0.1f, 0.1f, 1f);      // a plate that cannot be mistaken for any backdrop (the game's is near black over a dark sky): this case is about whether it is drawn at all
        int px = Mathf.RoundToInt(192f * rig.S), py = Mathf.RoundToInt(3f * rig.S);      // inside the box, over the log's shelf line (which is 18 down) and clear of everything it draws
        string stem = "corners-oneline-" + res;
        Capture(rig.Canvas, stem + "-hidden.png");
        Color32 hidden = PngPixel(stem + "-hidden.png", px, py);
        rig.Plate.canvasRenderer.SetAlpha(1f);
        Capture(rig.Canvas, stem + "-shown.png");
        Color32 shown = PngPixel(stem + "-shown.png", px, py);
        rig.Plate.enabled = false;
        Capture(rig.Canvas, stem + "-none.png");
        Color32 none = PngPixel(stem + "-none.png", px, py);
        Check(PixelGap(shown, none) >= 40, "One line: the plate shown darkens the render at (" + px + ", " + py + ") enough to tell it: " + shown + " against " + none + " " + res);
        Check(PixelGap(hidden, none) <= 3, "One line: with the plate hidden the render is the one with no plate at (" + px + ", " + py + "): " + hidden + " against " + none + " " + res);
    }

    private static void CornerShot(CornerRig rig, string name)
    {
        string tag = "corners-" + name + "-" + (int)rig.Size.x + "x" + (int)rig.Size.y;
        foreach (string root in new[] { "cornerShade", "cornerFurniture", "logRoot" })
        {
            var tree = (RectTransform)Field(rig.Status, root);
            if (tree != null) foreach (Graphic g in tree.GetComponentsInChildren<Graphic>(true)) Check(!g.raycastTarget, "Corners take no click: " + g.name + " " + tag);
        }
        var surface = (AvVector)Field(rig.Status, "cornerVector");
        Check(surface == null || !surface.Buffer.Overflowed, "The corner furniture buffer did not fill: " + tag);
        var log = (AvVector)Field(rig.Status, "logVector");
        Check(log == null || !log.Buffer.Overflowed, "The event log buffer did not fill: " + tag);
        Capture(rig.Canvas, tag + ".png");
    }

    /// <summary>The furniture where LAYOUT.md 7 puts it, from the stand-in rectangles (canvas units are pixels here: u a design unit, s a native unit).</summary>
    private static void CornerPlaces(CornerRig rig, bool feed, bool map)
    {
        string res = (int)rig.Size.x + "x" + (int)rig.Size.y;
        float u = rig.U, s = rig.S, h = rig.Size.y;
        List<Rect> quads = CornerQuads(rig);
        Dictionary<string, Rect> words = CornerWords(rig);
        // DL EVT LOG (its own surface): the caption is on the log's shelf, 6 under its top, 12 in from the column's shared 16-unit inset.
        Dictionary<string, Rect> logWords = LogWords(rig);
        Check(logWords.ContainsKey("DL EVT LOG") == feed, "DL EVT LOG: the caption is on the shelf (" + feed + "): " + res);
        if (feed)
        {
            Rect w = logWords["DL EVT LOG"];
            Check(Mathf.Abs(w.center.y - 18f * u) <= 4f && Mathf.Abs(w.xMin - 28f * u) <= 6f * u, "DL EVT LOG: the caption sits on the shared shelf's line at x 28: " + w + " " + res);
        }
        // TAC MAP: the plate is x 0..320 by 760..1080 (native units); brackets 4 in at left and right, 6 under the top, 12 over the bottom, arm 18 (design units at 1080p: native units carried by k); the caption shelf INSIDE the frame at its
        // lower left: its line 14 over the bottom brackets' line, from 6 in from the left brackets (where a screen-edge marker cannot sit on it).
        float left = 4f, right = 320f * s - 4f, top = h - 320f * s + 6f, bottom = h - 12f, arm = Mathf.Round(18f / u) * u;
        Check(HasLine(quads, true, top, left, left + arm) == map && HasLine(quads, true, top, right - arm, right) == map, "TAC MAP: the top arms along y 766 (" + map + "): " + res);
        Check(HasLine(quads, true, bottom - 1f, left, left + arm) == map && HasLine(quads, true, bottom - 1f, right - arm, right) == map, "TAC MAP: the bottom arms 12 over the screen's bottom: " + res);
        Check(HasLine(quads, false, left, top, top + arm) == map && HasLine(quads, false, right - 1f, top, top + arm) == map, "TAC MAP: the vertical arms at the left and the right: " + res);
        Check(HasLine(quads, false, left, bottom - arm, bottom) == map && HasLine(quads, false, right - 1f, bottom - arm, bottom) == map, "TAC MAP: ...the bottom ones up from the bottom: " + res);
        Check(HasLine(quads, true, bottom - 14f * u, left + 6f * u, left + 14f * u) == map, "TAC MAP: the shelf's line 14 over the bottom brackets' line, inside the frame: " + res);
        Check(!HasLine(quads, true, top - 16f * u, left, left + 8f * u), "TAC MAP: nothing left of the old shelf over the frame's top: " + res);
        Check(words.ContainsKey("TAC MAP") == map, "TAC MAP: the caption is on the shelf (" + map + "): " + res);
        if (map)
        {
            Rect w = words["TAC MAP"];
            Check(Mathf.Abs(w.center.y - (bottom - 14f * u)) <= 4f && Mathf.Abs(w.xMin - (left + 18f * u)) <= 6f * u, "TAC MAP: the caption sits on the shelf's line at x 18: " + w + " " + res);
            Check(w.center.y > top + 100f * u && w.center.y < bottom && w.xMin > left && w.xMax < right, "TAC MAP: the caption is inside the frame, far under its top edge, where the game's edge markers sit: " + w + " " + res);
        }
    }

    /// <summary>
    /// The north tick (the game rotates its map image by the heading, counter-clockwise, every frame): north at the top of the frame for a northbound aircraft, on its left for an eastbound one, at the bottom (clear of the caption shelf)
    /// for a southbound one and on the right for a westbound one, a tick 8 long inwards from the frame's line and an N beyond its end; and no tick, no N, when the map image is not held.
    /// </summary>
    private static void CornerNorth(CornerRig rig)
    {
        string res = (int)rig.Size.x + "x" + (int)rig.Size.y;
        float u = rig.U, s = rig.S, h = rig.Size.y, left = 4f, right = 320f * s - 4f, top = h - 320f * s + 6f, bottom = h - 12f, midX = (left + right) * 0.5f, midY = (top + bottom) * 0.5f;
        float shelfEnd = (left + 6f * u) + 104f * u + 16f * u;      // the shelf's end and the 8 + 8 the tick keeps clear of it (design units carried to pixels)
        foreach (int heading in new[] { 0, 90, 180, 270 })
        {
            rig.MapImage.localEulerAngles = new Vector3(0f, 0f, heading);
            PresentCorners(rig);
            List<Rect> quads = CornerQuads(rig);
            Dictionary<string, Rect> words = CornerWords(rig);
            Check(words.ContainsKey("N"), "North tick: the N is on the frame, heading " + heading + ": " + res);
            if (!words.ContainsKey("N")) continue;
            Rect n = words["N"];
            switch (heading)
            {
                case 0:
                    Check(HasLine(quads, false, midX, top, top + 8f * u), "North tick: heading 0, a tick down from the middle of the top line: " + res);
                    Check(Mathf.Abs(n.center.x - midX) <= 3f && n.center.y > top + 8f * u, "North tick: ...and its N under the tick's end: " + n + " " + res);
                    break;
                case 90:
                    Check(HasLine(quads, true, midY, left, left + 8f * u), "North tick: heading 90 (east), north on the left: a tick from the left line: " + res);
                    Check(Mathf.Abs(n.center.y - midY) <= 4f && n.center.x > left + 8f * u, "North tick: ...and its N beside the tick's end: " + n + " " + res);
                    break;
                case 180:
                {
                    float x = Mathf.Max(midX, shelfEnd);
                    Check(HasLine(quads, false, x, bottom - 8f * u, bottom), "North tick: heading 180, a tick up from the bottom line, clear of the caption shelf (x " + x + "): " + res);
                    Check(n.center.x >= shelfEnd - 3f && n.center.y < bottom - 8f * u, "North tick: ...and its N over the tick's end, off the shelf: " + n + " " + res);
                    break;
                }
                default:
                    Check(HasLine(quads, true, midY, right - 8f * u, right), "North tick: heading 270 (west), north on the right: a tick from the right line: " + res);
                    Check(Mathf.Abs(n.center.y - midY) <= 4f && n.center.x < right - 8f * u, "North tick: ...and its N beside the tick's end: " + n + " " + res);
                    break;
            }
            foreach (KeyValuePair<string, Rect> other in words)
                if (other.Key != "N" && other.Value.Overlaps(n)) Check(false, "North tick: the N does not sit on " + other.Key + " (heading " + heading + "): " + n + " " + other.Value + " " + res);
        }
        // The map image gone (the game's object not found): the frame is dressed without a tick. (The native pieces stay adopted: Adopt takes a new CanvasGroup for the feed's plate, which Unity refuses in the frame the old one is destroyed.)
        rig.MapImage.localEulerAngles = new Vector3(0f, 0f, 0f);
        object natives = Field(rig.Status, "screenCorners");
        Set(natives, "mapImage", null);
        PresentCorners(rig);
        Check(!CornerWords(rig).ContainsKey("N") && CornerWords(rig).ContainsKey("TAC MAP"), "North tick: no readable rotation, no N (the caption stays): " + res);
        Set(natives, "mapImage", rig.MapImage);
        PresentCorners(rig);
        CornerShot(rig, "north");
    }

    private static void SetHolo(bool on) => Type("BoscaliSummer.Core.Contracts.HudMapState").GetField("HoloActive", All).SetValue(null, on);

    /// <summary>The hide rule for a map hologram that takes over the corner: while HudMapState.HoloActive is set the TAC MAP dressing (brackets, caption, north tick) is gone, noticed on the next frame, not the next tick; the kill feed's corner is not touched.</summary>
    private static void CornerHolo(Vector2 size)
    {
        string res = (int)size.x + "x" + (int)size.y;
        CornerRig rig = MakeCorners(size, PilotOptions());
        try
        {
            Check(!(bool)Type("BoscaliSummer.Core.Contracts.HudMapState").GetField("HoloActive", All).GetValue(null), "HoloActive is false by default: " + res);
            CornerPlaces(rig, true, true);
            Check(CornerWords(rig).ContainsKey("N"), "Holo off: the map is dressed, north tick and all: " + res);
            SetHolo(true);
            Call(rig.Status, "CornersFrame", true, false);      // a frame between two ticks: the corner notices on its own
            CornerPlaces(rig, true, false);
            Check(!CornerWords(rig).ContainsKey("N") && !CornerWords(rig).ContainsKey("TAC MAP"), "Holo on: no caption, no north tick, the map dressing is hidden whole: " + res);
            Check(LogWords(rig).ContainsKey("DL EVT LOG") && PlateAlpha(rig) == 0f, "...and the kill feed's corner is as it was: " + res);
            CornerShot(rig, "holo");
            PresentCorners(rig);
            CornerPlaces(rig, true, false);
            SetHolo(false);
            Call(rig.Status, "CornersFrame", true, false);
            CornerPlaces(rig, true, true);
            Check(CornerWords(rig).ContainsKey("N"), "Holo cleared: the dressing is back on the next frame: " + res);
        }
        finally { SetHolo(false); }
        EndCorners(rig);
    }

    /// <summary>The box the board reports for the banner and the cluster is the box it drew.</summary>
    private static void CornerBoardBox(CornerRig rig)
    {
        var canvasRect = (RectTransform)rig.Canvas.transform;
        Rect root = CanvasRectOf(canvasRect, (RectTransform)Field(rig.Status, "root"));
        object[] a = new object[4];
        bool ok = (bool)rig.Status.GetType().GetMethod("TryGetScreenBox").Invoke(rig.Status, a);
        float l = (float)a[0], t = (float)a[1], r = (float)a[2], b = (float)a[3];
        Check(ok && Mathf.Abs(l - root.xMin) < 1.5f && Mathf.Abs(t - root.yMin) < 1.5f && Mathf.Abs(r - root.xMax) < 1.5f && Mathf.Abs(b - root.yMax) < 1.5f,
            "The board's screen box (" + l + ", " + t + ", " + r + ", " + b + ") is the box it drew " + root);
        object box = rig.Status.GetType().GetProperty("BoardBox").GetValue(rig.Status);
        Type rt = box.GetType();
        float bl = (float)rt.GetProperty("Left").GetValue(box), bt = (float)rt.GetProperty("Top").GetValue(box), br = (float)rt.GetProperty("Right").GetValue(box), bb = (float)rt.GetProperty("Bottom").GetValue(box);
        Check(Mathf.Abs(bl + rig.Size.x / 2f - root.xMin) < 1.5f && Mathf.Abs(br + rig.Size.x / 2f - root.xMax) < 1.5f && Mathf.Abs(rig.Size.y / 2f - bt - root.yMin) < 1.5f && Mathf.Abs(rig.Size.y / 2f - bb - root.yMax) < 1.5f,
            "...and the cluster's box (centre-origin, y up) is the same box: " + bl + ", " + bt + ", " + br + ", " + bb);
        Rect feedBox = CanvasRectOf(canvasRect, rig.FeedBox);
        Check(Mathf.Abs(feedBox.width - 475f * rig.S) < 0.5f && Mathf.Abs(feedBox.height - 90f * rig.S) < 0.5f && Mathf.Abs(feedBox.xMin) < 0.5f && Mathf.Abs(feedBox.yMin) < 0.5f, "The stand-in feed box is where the native one is: " + feedBox);
    }

    private static void ScopeCorners()
    {
        foreach (Vector2 size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720) })
        {
            string res = (int)size.x + "x" + (int)size.y;
            // Corners on (the default): the plate is hidden, the shade is up, the furniture is where section 7 puts it, the board's box is reported.
            CornerRig rig = MakeCorners(size, PilotOptions());
            Check(PlateAlpha(rig) == 0f, "Corners on: the feed's own dark plate is hidden: " + res);
            Check(Up("cornerShade", rig.Status) && Up("cornerFurniture", rig.Status), "Corners on: the shade and the furniture are up: " + res);
            CornerPlaces(rig, true, true);
            CornerBoardBox(rig);
            foreach (TextMeshProUGUI line in rig.Texts) Check(line.gameObject.activeInHierarchy && line.color.a > 0.99f && line.text.Contains("demolished"), "The game's own feed text is not touched, only not seen (the log draws the lines): " + line.text);
            Check(TextAlpha(rig) == 0f, "Corners on: ...the game's feed text is hidden through our group: " + res);
            CornerShot(rig, "on");
            CornerNorth(rig);
            Check(EndCorners(rig) == 1f, "Teardown gives the feed's plate back: " + res);
            ScopeEventLog(size);
            CornerHolo(size);

            // Corners off: nothing of ours, the plate as the game made it.
            rig = MakeCorners(size, PilotOptions("Corners", false));
            Check(PlateAlpha(rig) == 1f && !Up("cornerShade", rig.Status) && !Up("cornerFurniture", rig.Status), "Corners off: the vanilla dark plate, no shade, no furniture: " + res);
            Check(rig.Darkener.GetComponent<CanvasGroup>() == null || rig.Darkener.GetComponent<CanvasGroup>().alpha == 1f, "Corners off: the plate is shown at full alpha: " + res);
            CornerShot(rig, "off");
            EndCorners(rig);

            // One line as the game drew it, the shade off so nothing but the plate could darken the box.
            rig = MakeCorners(size, PilotOptions("Shade", 0f), 1);
            CornerOneLine(rig);
            EndCorners(rig);
            if (size.x < 1500f) continue;

            // Without furniture (Lean): the shade stands in for the plate, nothing is drawn on it.
            rig = MakeCorners(size, PilotOptions("Declutter", Declutter("Lean")));
            Check(PlateAlpha(rig) == 0f && Up("cornerShade", rig.Status), "Lean: the plate is hidden and the shade is up: " + res);
            CornerPlaces(rig, false, false);
            CornerShot(rig, "nofurniture");
            EndCorners(rig);

            // No shade (the option at 0): the plate is still hidden (the pilot asked for no band, not for the dark box), the furniture stays.
            rig = MakeCorners(size, PilotOptions("Shade", 0f));
            Check(PlateAlpha(rig) == 0f && !Up("cornerShade", rig.Status) && Up("cornerFurniture", rig.Status), "Shade 0: no shade, the plate hidden, the furniture up: " + res);
            CornerPlaces(rig, true, true);
            CornerShot(rig, "shade0");
            EndCorners(rig);

            CornerLive(size);
        }
    }

    /// <summary>The corners under a running board: each native object coming and going, the options, the master switch and the teardown, and the sway.</summary>
    private static void CornerLive(Vector2 size)
    {
        CornerRig rig = MakeCorners(size, PilotOptions());
        // The feed's box goes (its lines expired): its shade and furniture go, the plate is the game's again at once; the map's furniture stays.
        rig.Darkener.gameObject.SetActive(false);
        PresentCorners(rig);
        Check(PlateAlpha(rig) == 1f && !Up("cornerShade", rig.Status), "live: no feed box, no shade, and the plate is not ours to hide");
        CornerPlaces(rig, false, true);
        rig.Darkener.gameObject.SetActive(true);
        // The map is hidden (maximised, or off): its furniture goes, the feed's stays.
        rig.MapPlate.gameObject.SetActive(false);
        PresentCorners(rig);
        Check(PlateAlpha(rig) == 0f, "live: the feed box is back and the plate is hidden again");
        CornerPlaces(rig, true, false);
        rig.MapPlate.gameObject.SetActive(true);
        // The master switch: the board goes, the corners stay (LAYOUT.md 8: "hides the board (corners stay)"), the plate stays ours to hide; they are a switch of their own.
        Set(rig.Status, "Options", PilotOptions("Enabled", false));
        PresentCorners(rig);
        Check(!((RectTransform)Field(rig.Status, "root")).gameObject.activeSelf, "live: the master switch off hides the board");
        Check(PlateAlpha(rig) == 0f && Up("cornerShade", rig.Status) && Up("cornerFurniture", rig.Status), "live: ...and the corners stay, the feed's plate still hidden");
        CornerPlaces(rig, true, true);
        Call(rig.Status, "CornersFrame", true, false);      // a frame between two ticks, as the board's TickNatives runs it
        Check(PlateAlpha(rig) == 0f && Up("cornerFurniture", rig.Status), "live: ...a frame between two ticks leaves them up too");
        Set(rig.Status, "Options", PilotOptions("Enabled", false, "Corners", false));
        PresentCorners(rig);
        Check(PlateAlpha(rig) == 1f && !Up("cornerShade", rig.Status) && !Up("cornerFurniture", rig.Status), "live: with the restyle off as well the plate is the game's and nothing of ours is up");
        Set(rig.Status, "Options", PilotOptions("Enabled", false));
        PresentCorners(rig);
        Check(PlateAlpha(rig) == 0f && Up("cornerFurniture", rig.Status), "live: the restyle on again with the board still off");
        Call(rig.Status, "Hide");
        Check(PlateAlpha(rig) == 1f && !Up("cornerShade", rig.Status) && !Up("cornerFurniture", rig.Status), "live: Hide (the module going away) stands the corners down with the board and gives the plate back");
        Set(rig.Status, "Options", PilotOptions());
        PresentCorners(rig);
        Check(PlateAlpha(rig) == 0f && Up("cornerFurniture", rig.Status), "live: and back on");
        // The game's gate shuts (a pause, the maximised map): the corners stand down on the next frame, not the next tick.
        Set(rig.Status, "cornersSuppressed", true);
        Call(rig.Status, "CornersFrame", true, false);
        Check(PlateAlpha(rig) == 1f && !Up("cornerFurniture", rig.Status), "live: a closed gate gives the plate back and takes the corners down");
        Set(rig.Status, "cornersSuppressed", false);
        PresentCorners(rig);

        // Sway: the board's root and the corners' roots move together by whole pixels; the sun bands stay hung from the top of the screen; zero puts everything back.
        var root = (RectTransform)Field(rig.Status, "root");
        var shade = (RectTransform)Field(rig.Status, "cornerShade");
        var furniture = (RectTransform)Field(rig.Status, "cornerFurniture");
        var band = ((Image)Field(rig.Status, "shadeBand")).rectTransform;
        var feedBand = ((Image)Field(rig.Status, "leftShadow")).rectTransform;
        Vector2 at = root.anchoredPosition;
        float bandY = band.position.y, feedBandY = feedBand.position.y;
        Call(rig.Status, "ApplySway", 2.4f / rig.U, -1.2f / rig.U);      // 2.4 and -1.2 pixels of units: rounds to 2 and -1
        Vector2 moved = root.anchoredPosition - at;
        Check(moved == new Vector2(2f, -1f), "sway: the board moved by whole pixels (2, -1): " + moved);
        Check(shade.offsetMin == new Vector2(2f, -1f) && furniture.offsetMax == new Vector2(2f, -1f), "sway: the corners moved with it: " + shade.offsetMin + " " + furniture.offsetMax);
        var logRoot = (RectTransform)Field(rig.Status, "logRoot");
        Check(logRoot.offsetMin == new Vector2(2f, -1f) && logRoot.offsetMax == new Vector2(2f, -1f), "sway: ...and the event log: " + logRoot.offsetMin);
        Check(Mathf.Abs(band.position.y - bandY) < 0.01f && Mathf.Abs(feedBand.position.y - feedBandY + 1f) < 0.01f, "sway: the board sun band stays fixed and the shared left shadow follows the ink");
        Canvas.ForceUpdateCanvases();
        CornerPlaces(rig, true, true);      // the furniture surface moved as a whole: its quads are where they were in it
        CornerShot(rig, "sway");
        Call(rig.Status, "ApplySway", 0.3f / rig.U, 0.2f / rig.U);      // under half a pixel: nothing to move
        Check(root.anchoredPosition == at, "sway: under half a pixel is no pixel: the board is back where it was: " + root.anchoredPosition);
        Check(shade.offsetMin == Vector2.zero && furniture.offsetMin == Vector2.zero && logRoot.offsetMin == Vector2.zero, "sway: ...and the corners with it");
        Call(rig.Status, "ApplySway", 9f, -9f);
        Check(root.anchoredPosition != at, "sway: a swing is a move");
        Call(rig.Status, "ApplySway", 0f, 0f);
        PresentCorners(rig);
        Check(root.anchoredPosition == at && shade.offsetMin == Vector2.zero, "sway: still, the board stays where the layout put it after a present too");
        EndCorners(rig);
    }
}
#endif
