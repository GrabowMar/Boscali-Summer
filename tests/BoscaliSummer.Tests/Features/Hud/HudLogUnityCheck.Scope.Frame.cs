#if UNITY_EDITOR
using System;
using System.Linq;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

// The TAC BOARD's frame: where its blocks sit (WIDE and the classic STACK), where the box goes under the pilot's Scale and offsets, the shade (the band and the one cloud field, no plate) and the furniture helpers of
// the page-drawing kit. The insides of the blocks are not what this file judges. Renders frame-<case>-<W>x<H>.png (not named like a page shot: the compare sheet leaves them out) and furniture-sheet.png.
// Run alone with -pages frame.
public static partial class HudLogUnityCheck
{
    private static object LayoutOf(string layout) => Enum.Parse(Type("BoscaliSummer.Core.Contracts.BoardLayout"), layout);

    private static object ToneOfScope(string tone) => Enum.Parse(Type("BoscaliSummer.Core.Contracts.ScopeTone"), tone);

    private static void ScopeFrame()
    {
        var hd = new Vector2(1920, 1080);
        FrameShot("wide-cruise", hd, "systems", 2f, PilotOptions());
        FrameShot("wide-live", hd, "threat", 6f, PilotOptions());
        FrameShot("stack-cruise", hd, "systems", 2f, PilotOptions("Layout", LayoutOf("Stack")));
        FrameShot("stack-live", hd, "threat", 6f, PilotOptions("Layout", LayoutOf("Stack")));
        FrameShot("shade0", hd, "systems", 2f, PilotOptions("Shade", 0f));
        FrameShot("shade1", hd, "systems", 2f, PilotOptions("Shade", 1f));
        FrameShot("scale07", hd, "systems", 2f, PilotOptions("Scale", 0.7f));
        FrameShot("scale13", hd, "systems", 2f, PilotOptions("Scale", 1.3f));
        FrameShot("offset", hd, "systems", 2f, PilotOptions("OffsetX", 300f, "OffsetY", 160f));
        FrameShot("offset-live", hd, "threat", 6f, PilotOptions("OffsetX", 120f, "OffsetY", 60f, "Opacity", 0.6f));
        FrameShot("wide-cruise", new Vector2(1280, 720), "systems", 2f, PilotOptions());
        FrameShot("wide-live", new Vector2(1280, 720), "threat", 6f, PilotOptions());
        FrameLive();
        ShadeSprites();
        FurnitureSheet();
    }

    /// <summary>The pilot's options change under a running board: each shows at the next present (one 10 Hz tick) on the same objects, the tree is not rebuilt.</summary>
    private static void FrameLive()
    {
        var size = new Vector2(1920, 1080);
        MakeBoard(size, 2f, out Canvas canvas, out object status, out object frame);
        var scene = new ScopeScene(2f, size, frame);
        FixtureSystems(2f, scene);
        StoresFixture("systems", 2f, scene);
        Call(status, "Present", frame);
        var root = (RectTransform)Field(status, "root");
        var shade = (RectTransform)Field(status, "shadeRect");
        int children = root.childCount, shadeChildren = shade.childCount;
        Check(Mathf.Abs(root.sizeDelta.x - 916f) < 1.5f && shade.gameObject.activeSelf, "live: the board starts WIDE with its shade");
        var field = (Texture2D)Field(status, "fieldTexture");
        int wide = field.width;
        field.SetPixel(0, 0, Color.red); field.Apply(false, false);      // a mark a bake would wipe (the rim of a bake is clear)
        Call(status, "Present", frame);
        Check(field.GetPixel(0, 0).a > 0.9f, "live: the same blocks do not bake the cloud field again");
        Set(status, "Options", PilotOptions("Layout", LayoutOf("Stack")));
        Call(status, "Present", frame);
        Check(Mathf.Abs(root.sizeDelta.x - 448f) < 1.5f, "live: Layout Stack shows at the next present: " + root.sizeDelta.x);
        Check((Texture2D)Field(status, "fieldTexture") == field && field.width < wide - 50 && field.GetPixel(0, 0).a < 0.01f, "live: other blocks bake the same texture again, resized: " + wide + " -> " + field.width);
        Set(status, "Options", PilotOptions("Scale", 0.7f));
        Call(status, "Present", frame);
        Check(Mathf.Abs(root.sizeDelta.x - 916f * 0.7f) < 1.5f, "live: Scale 0.7 shows at the next present: " + root.sizeDelta.x);
        Set(status, "Options", PilotOptions("OffsetX", 400f));
        Call(status, "Present", frame);
        var corners = new Vector3[4];
        root.GetWorldCorners(corners);
        Check(Mathf.Abs(((RectTransform)canvas.transform).rect.xMax - canvas.transform.InverseTransformPoint(corners[2]).x - 408f) < 1.5f, "live: OffsetX 400 moves the box 400 left at the next present");
        Set(status, "Options", PilotOptions("Shade", 0f));
        Call(status, "Present", frame);
        Check(!shade.gameObject.activeSelf, "live: Shade 0 switches the shade off at the next present");
        Set(status, "Options", PilotOptions());
        Call(status, "Present", frame);
        Canvas.ForceUpdateCanvases();
        Check(shade.gameObject.activeSelf && Mathf.Abs(root.sizeDelta.x - 916f) < 1.5f, "live: the defaults bring the board back");
        Check((RectTransform)Field(status, "root") == root && root.childCount == children && shade.childCount == shadeChildren, "live: the same tree throughout, nothing rebuilt");
        ScopeChecks(status, "frame-live");
        Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
    }

    /// <summary>One board at one moment of a page's scenario under the pilot's <paramref name="options"/>, with the stores header, checked for where the box sits and what the shade is, then captured.</summary>
    private static void FrameShot(string name, Vector2 size, string page, float t, object options)
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
        Call(status, "Present", frame);
        Canvas.ForceUpdateCanvases();
        string tag = "frame-" + name + "-" + (int)size.x + "x" + (int)size.y;
        ScopeChecks(status, tag);
        FrameChecks(status, canvas, size, options, tag);
        Capture(canvas, tag + ".png");
        Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
    }

    /// <summary>The box is the size its layout and the pilot's Scale say, sits where the margins and the offsets say (the top-right corner), stays on the screen, and carries a shade that is on only when asked for.</summary>
    private static void FrameChecks(object status, Canvas canvas, Vector2 size, object options, string tag)
    {
        var root = (RectTransform)Field(status, "root");
        float scale = (float)Field(options, "Scale"), ox = (float)Field(options, "OffsetX"), oy = (float)Field(options, "OffsetY"), shade = (float)Field(options, "Shade");
        bool stack = Field(options, "Layout").ToString() == "Stack";
        float baseUnit = Mathf.Max(size.y / 1080f, 0.85f), unit = baseUnit * scale;
        Check(Mathf.Abs(root.sizeDelta.x - (stack ? 448f : 916f) * unit) < 1.5f, tag + ": the box is " + (stack ? 448 : 916) + " units wide at the pilot's scale: " + root.sizeDelta.x + " / " + unit);
        var corners = new Vector3[4];
        root.GetWorldCorners(corners);
        Rect screen = ((RectTransform)canvas.transform).rect;
        Vector3 bottomLeft = canvas.transform.InverseTransformPoint(corners[0]), topRight = canvas.transform.InverseTransformPoint(corners[2]);
        Check(bottomLeft.x >= screen.xMin - 0.5f && bottomLeft.y >= screen.yMin - 0.5f && topRight.x <= screen.xMax + 0.5f && topRight.y <= screen.yMax + 0.5f, tag + ": the box is fully on the screen");
        Check(Mathf.Abs(screen.xMax - topRight.x - (8f + ox) * baseUnit) < 1.5f, tag + ": the box is 8 + the offset in from the right edge: " + (screen.xMax - topRight.x) + " / " + (8f + ox) * baseUnit);
        Check(Mathf.Abs(screen.yMax - topRight.y - (6f + oy) * baseUnit) < 1.5f, tag + ": the box is 6 + the offset down from the top edge: " + (screen.yMax - topRight.y) + " / " + (6f + oy) * baseUnit);
        // The shade: objects only while the pilot's Shade is above 0; ONE cloud field behind all the blocks (a texture, no cloud per block); nothing takes a click; no plate.
        var shadeRect = (RectTransform)Field(status, "shadeRect");
        Check(shadeRect.gameObject.activeSelf == shade > 0f, tag + ": the shade objects are on exactly when Shade is above 0");
        Check(root.Find("Plate") == null, tag + ": there is no plate");
        var group = shadeRect.GetComponent<CanvasGroup>();
        Check(group != null && group.ignoreParentGroups, tag + ": the shade ignores the board's opacity");
        if (shade > 0f)
        {
            RawImage[] fields = shadeRect.GetComponentsInChildren<RawImage>(true);
            Check(fields.Length == 1 && fields[0].gameObject.name == "CloudField" && fields[0].gameObject.activeSelf && !fields[0].raycastTarget, tag + ": one cloud field behind all the blocks, and it takes no click");
            foreach (Image i in shadeRect.GetComponentsInChildren<Image>(true)) Check(i.name == "SunBand" && i.type == Image.Type.Sliced && !i.raycastTarget, tag + ": the only sprite in the shade is the 9-sliced band: " + i.name);
            Check(Mathf.Abs(fields[0].color.a - 0.6f * shade) < 0.01f, tag + ": the field's core alpha is 0.60 x Shade");
            FieldChecks(status, fields[0], tag);
        }
    }

    /// <summary>The baked cloud field is one small texture, nothing along its rim (a cut edge would be a straight line in the sky), dark somewhere, and stretched over the box and the room its clouds need.</summary>
    private static void FieldChecks(object status, RawImage field, string tag)
    {
        var texture = (Texture2D)field.texture;
        Check(texture != null && texture.filterMode == FilterMode.Bilinear && texture.width * texture.height < 60000, tag + ": the field is one small bilinear texture: " + (texture != null ? texture.width + " x " + texture.height : "none"));
        Color32[] px = texture.GetPixels32();
        int w = texture.width, h = texture.height, rim = 0, core = 0;
        for (int i = 0; i < w; i++) rim = Math.Max(rim, Math.Max(px[i].a, px[(h - 1) * w + i].a));
        for (int j = 0; j < h; j++) rim = Math.Max(rim, Math.Max(px[j * w].a, px[j * w + w - 1].a));
        foreach (Color32 c in px) core = Math.Max(core, c.a);
        Check(rim <= 1, tag + ": the field is nothing along its rim, so it has no cut edge: " + rim);
        Check(core >= 200, tag + ": the field has a dark core: " + core);
        float unit = (float)Field(status, "unit");
        RectTransform r = field.rectTransform;
        float x0 = r.anchoredPosition.x / unit, y0 = -r.anchoredPosition.y / unit, x1 = x0 + r.sizeDelta.x / unit, y1 = y0 + r.sizeDelta.y / unit;
        Check(x0 <= 4f - 100f && y0 <= 0f - 100f && x1 >= 444f + 100f && y1 >= 100f + 100f, tag + ": the field is stretched over the box and its fade: " + x0 + ", " + y0 + " to " + x1 + ", " + y1);
        Check(Mathf.Abs(r.sizeDelta.x / unit / (w * 4f) - 1f) < 0.01f && Mathf.Abs(r.sizeDelta.y / unit / (h * 4f) - 1f) < 0.01f, tag + ": a texel is 4 box units across");
    }

    /// <summary>The generated sun band has no edge: it fades from the top down and ramps in at its left (the cloud field is a texture of its own, FieldChecks).</summary>
    private static void ShadeSprites()
    {
        Type sprites = Type("BoscaliSummer.Core.Ui.HudSprites");
        var band = (Sprite)sprites.GetMethod("SunBand", All).Invoke(null, new object[] { 0f });
        var faded = (Sprite)sprites.GetMethod("SunBand", All).Invoke(null, new object[] { 60f / 190f });
        Texture2D b = band.texture, f = faded.texture;
        int top = b.height - 1, mid = b.width / 2;
        Check(b.GetPixel(mid, top).a > 0.97f && b.GetPixel(mid, 0).a < 0.03f && b.GetPixel(mid, b.height / 2).a > 0.4f && b.GetPixel(mid, b.height / 2).a < 0.6f, "The band falls linearly from full at the top to nothing at its foot");
        Check(b.GetPixel(0, top).a < 0.02f && b.GetPixel(band.texture.width - 1, top).a < 0.02f && b.GetPixel(40, top).a > 0.95f, "The band ramps in at its left and out at its right");
        Check(f.GetPixel(mid, top).a < 0.05f && f.GetPixel(mid, top - 20).a > 0.4f && f.GetPixel(mid, 0).a < 0.03f, "A band that starts below the top of the screen fades in at its top");
        Check(band != faded && band.border == new Vector4(40f, 0f, 40f, 0f), "The band is 3-sliced across and stretched down");
    }

    /// <summary>The furniture helpers of the page-drawing kit on one sheet over the sky: the caption shelf (with and without a flag), corner brackets, lamp / arm / expendable markers, tone ticks and open tags; and with Furniture off the shelf draws nothing.</summary>
    private static void FurnitureSheet()
    {
        var size = new Vector2(1920, 1080);
        MakeBoard(size, 0f, out Canvas canvas, out object status, out _);
        var dock = (RectTransform)Field(status, "dock");
        var root = (RectTransform)Field(status, "root");
        root.sizeDelta = size; root.anchoredPosition = dock.rect.size;
        foreach (string name in new[] { "scopeRect", "pageRect" })
        {
            var r = (RectTransform)Field(status, name);
            r.gameObject.SetActive(true); r.anchoredPosition = Vector2.zero; r.sizeDelta = size;
        }
        object g = Field(status, "pageCanvas");
        var vector = (AvVector)Field(status, "pageVector");
        object palette = Activator.CreateInstance(Type(HudDomain + "ScopePalette"), new object[] { new Rgba(0.22f, 0.87f, 0.32f), new Rgba(0.95f, 0.85f, 0.29f), new Rgba(1f, 0.31f, 0.27f) });
        object align = Enum.Parse(Type("BoscaliSummer.Modules.Hud.Presentation.ScopeAlign"), "Left");
        Type canvasType = g.GetType();
        object Draw(string method, params object[] args) =>
            canvasType.GetMethods(All).First(m => m.Name == method && m.GetParameters().Length == args.Length && (args.Length == 0 || args[0] == null || !(args[0] is string) || m.GetParameters()[0].ParameterType == typeof(string))).Invoke(g, args);
        void Begin() => Draw("Begin", palette, 1f, size.y, 1f);

        // With Furniture off the shelf is not there: nothing in the mesh, no word.
        Begin();
        canvasType.GetProperty("Furniture").SetValue(g, false);
        Draw("CaptionShelf", 996f, 20f, 200f, "STORES MGMT", "HMD SYM  DCL 1");
        Draw("End");
        Check(vector.Buffer.Count == 0, "Furniture off: the caption shelf draws no stroke");
        Check(!((RectTransform)Field(status, "pageRect")).GetComponentsInChildren<TextMeshProUGUI>().Any(l => l.gameObject.activeSelf && !string.IsNullOrEmpty(l.text)), "Furniture off: the caption shelf draws no word");

        canvasType.GetProperty("Furniture").SetValue(g, true);
        Begin();
        float x = 996f;      // the box's left edge at 1080p: the sheet sits where the board does
        // The caption shelf, with the flag of a HERO block and without one.
        Draw("CaptionShelf", x, 14f, 272f, "STORES MGMT", "HMD SYM  DCL 0");
        Draw("CaptionShelf", x + 300f, 14f, 180f, "PWR BUS", null);
        Draw("CaptionShelf", x + 500f, 14f, 160f, "EXPENDABLES", null);
        // Corner brackets around a rectangle (a page body's: arm 10, DIM) and (a station's: arm 4, BRIGHT).
        object dim = ToneOfScope("Dim"), hi = ToneOfScope("Hi"), amber = ToneOfScope("Amber"), red = ToneOfScope("Red"), ink = ToneOfScope("Ink");
        Draw("Brackets", x, 60f, 200f, 70f, 10f, dim, 1f, 1f);
        Draw("Brackets", x + 240f, 60f, 60f, 20f, 4f, hi, 1f, 1f);
        // Markers: a lit lamp (4, BRIGHT), an unlit one (hollow 4, DIM), the arm marker (hollow amber 6, filled bright 6), an expendable's (5).
        float cy = 100f;
        Draw("Marker", x + 240f, cy, 4f, hi, true, 1f); Draw("Marker", x + 260f, cy, 4f, dim, false, 1f);
        Draw("Marker", x + 280f, cy, 6f, amber, false, 1f); Draw("Marker", x + 300f, cy, 6f, hi, true, 1f);
        Draw("Marker", x + 320f, cy, 5f, hi, true, 1f); Draw("Marker", x + 340f, cy, 5f, dim, false, 1f);
        // The ticker's tone ticks (ink, amber, red) and a pulsing one.
        Draw("ToneTick", x + 400f, 90f, ink, 1f, 3f, 12f); Draw("ToneTick", x + 420f, 90f, amber, 1f, 3f, 12f); Draw("ToneTick", x + 440f, 90f, red, 1f, 3f, 12f); Draw("ToneTick", x + 460f, 90f, red, 0.55f, 3f, 12f);
        // Open tags: ink, bright, amber and red, each over a hairline.
        Draw("OpenTag", "BEAMED", x, 170f, ink, 12f, align, 3f, 1f); Draw("OpenTag", "STOP", x + 120f, 170f, hi, 12f, align, 3f, 1f);
        Draw("OpenTag", "LOW", x + 200f, 170f, amber, 12f, align, 3f, 1f); Draw("OpenTag", "FIRE", x + 280f, 170f, red, 12f, align, 3f, 1f);
        Draw("End");
        vector.Commit();
        Check(vector.Buffer.Count > 40 && !vector.Buffer.Overflowed, "The helpers drew their strokes: " + vector.Buffer.Count);
        var words = ((RectTransform)Field(status, "pageRect")).GetComponentsInChildren<TextMeshProUGUI>().Where(l => l.gameObject.activeSelf).Select(l => l.text).ToList();
        foreach (string w in new[] { "STORES MGMT", "HMD SYM  DCL 0", "PWR BUS", "EXPENDABLES", "BEAMED", "STOP", "LOW", "FIRE" }) Check(words.Contains(w), "The helpers wrote " + w);
        Canvas.ForceUpdateCanvases();
        Capture(canvas, "furniture-sheet.png");
        Call(status, "Destroy"); Object.DestroyImmediate(canvas.gameObject); ScopeReset();
    }
}
#endif
