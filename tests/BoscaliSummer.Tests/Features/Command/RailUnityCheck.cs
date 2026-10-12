#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static UnityCheckHarness;

/// <summary>
/// Standalone render/geometry check for the control rail and the vector glyphs.
///
/// The rail cannot be exercised in the pure net8 tests: it is Unity UI, and its whole
/// point is what a player sees. This check adopts fake vanilla bezel buttons on a real
/// canvas, verifies the branding, the latch state and the exact restore, and writes a PNG
/// of the rail and of every glyph kind so the visual result can be reviewed without the
/// game. Game adapters (MFDScreen, VirtualMFD, ThemeManager) come from the shared stubs.
/// </summary>
public static class RailUnityCheck
{
    private static readonly string[] Kinds =
    {
        "map", "faction", "hud", "target", "flag", "wing", "support", "theater",
        "person", "radio", "settings", "funds", "gauge", "missile", "radar", "dot",
        "pulse", "control", "front", "grid", "weather",
    };

    // The bezel as a Boscali mission shows it, in the game's own slot order.
    private static readonly string[] Labels =
    {
        "FAC", "MAP", "HUD", "PILOT", "OPS", "STR", "COM", "RAD", "TGT", "MIS", "SET", "WMC",
        "EVN", "ENV",
    };

    public static void Run()
    {
        try
        {
            if (!EnsureTmpEssentials(Run)) return;

            SetExecutablePath("RailCheck.exe");
            AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            AvBundle.LoadIconsFromBytes(File.ReadAllBytes("avionics-icons.bundle"), Debug.Log);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            new GameObject("Events", typeof(EventSystem));

            CheckGlyphGeometry();
            CheckUiSounds();
            RenderGlyphStrip();
            CheckRail();

            File.WriteAllText("result.txt",
                "PASS: every catalog glyph builds geometry; the rail builds one key of its own per screen slot, grouped by purpose, " +
                "with code, descriptor and glyph, and none for a spare slot; unknown codes keep the neutral glyph in the last group; " +
                "a rebuild replaces the keys instead of stacking them; a key press reaches the bezel button's own binding and the open " +
                "screen's key latches; the bezel buttons are never altered; a scheme change repaints plate and keys; every rail text " +
                "fits its box at 11 px or more; reset releases a stowed bezel button instead of destroying it; the full-screen finish " +
                "is single, above planning rooms, and input-transparent; seven UI cues have bounded samples, soft starts and silent tails. " +
                "Game adapters are stubbed; in-game acceptance remains required.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    // --------------------------------------------------------------------- glyphs

    private static void CheckUiSounds()
    {
        var build = typeof(AvUiSound).GetMethod("Build", BindingFlags.NonPublic | BindingFlags.Static);
        Check(build != null, "UI cue builder missing");
        foreach (AvUiCue cue in Enum.GetValues(typeof(AvUiCue)))
        {
            var clip = (AudioClip)build.Invoke(null, new object[] { cue });
            Check(clip != null, "UI cue missing: " + cue);
            Check(clip.frequency == 44100 && clip.channels == 1, "UI cue format: " + cue);
            var data = new float[clip.samples];
            Check(clip.GetData(data, 0), "UI cue unreadable: " + cue);
            float peak = 0f;
            for (int i = 0; i < data.Length; i++)
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            Check(peak > .04f && peak < .85f, "UI cue level: " + cue + " " + peak);
            Check(Mathf.Abs(data[0]) < .001f && Mathf.Abs(data[data.Length - 1]) < .01f,
                "UI cue edge click: " + cue);
            Object.DestroyImmediate(clip);
        }
    }

    private static void CheckGlyphGeometry()
    {
        foreach (string kind in Kinds)
        {
            var go = new GameObject("Glyph_" + kind, typeof(RectTransform), typeof(MfdGlyph));
            var rect = go.GetComponent<RectTransform>();
            AvLay.Place(rect, 0f, 0f, 32f, 32f);
            MfdGlyph glyph = go.GetComponent<MfdGlyph>();
            glyph.SetKind(kind, Color.white);

            var populate = typeof(MfdGlyph).GetMethod("OnPopulateMesh",
                BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(VertexHelper) }, null);
            var mesh = new VertexHelper();
            populate.Invoke(glyph, new object[] { mesh });
            Check(mesh.currentVertCount > 0, "glyph '" + kind + "' produced no geometry");
            Check(mesh.currentVertCount % 4 == 0 || kind == "dot", "glyph '" + kind + "' has a malformed strip");
            mesh.Dispose();
            Object.DestroyImmediate(go);
        }
    }

    private static void RenderGlyphStrip()
    {
        var root = new GameObject("GlyphCanvas", typeof(Canvas), typeof(RectTransform));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)root.transform).sizeDelta = new Vector2(480f, 100f);

        var camera = OrthoCamera("Camera", 100f, new Color(.03f, .06f, .05f));
        for (int i = 0; i < Kinds.Length; i++)
        {
            var go = new GameObject("Glyph_" + Kinds[i], typeof(RectTransform), typeof(MfdGlyph));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(root.transform, false);
            AvLay.Place(rect, 18f + i % 8 * 56f, 16f + i / 8 * 46f, 32f, 32f);

            MfdGlyph glyph = go.GetComponent<MfdGlyph>();
            glyph.raycastTarget = false;
            glyph.SetKind(Kinds[i], new Color(.92f, 1f, .96f));
        }

        Render(canvas, camera, 960, 400, "RAIL-glyphs.png");
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(camera.gameObject);
    }

    // ------------------------------------------------------------------------ rail

    private static void CheckRail()
    {
        var root = new GameObject("RailCanvas", typeof(Canvas), typeof(RectTransform));
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)root.transform).sizeDelta = new Vector2(1920f, 1080f);

        MfdLayout.Columns columns = MfdLayout.Resolve(new Vector2(1920f, 1080f));
        Check(columns.Rail.width >= 150f, "the rail must be wide enough for icon, code and descriptor");
        MfdRail.Ensure(canvas, columns);
        MfdScreenFinish.Ensure(canvas);
        GameObject finish = GameObject.Find("NOAvionics.ScreenFinish");
        Check(finish != null, "the screen finish must exist outside the panel dock");
        Canvas finishCanvas = finish.GetComponent<Canvas>();
        Canvas.ForceUpdateCanvases();
        Check(finishCanvas.renderMode == RenderMode.ScreenSpaceOverlay &&
            finishCanvas.pixelRect.width >= Screen.width - 1f &&
            finishCanvas.pixelRect.height >= Screen.height - 1f,
            "the screen finish must span the viewport as its own overlay canvas");
        Check(finishCanvas.sortingOrder > 30001,
            "the screen finish must also cover the large planning workspaces (order " +
            finishCanvas.sortingOrder + ", override " + finishCanvas.overrideSorting + ")");
        Check(!finish.GetComponent<CanvasGroup>().blocksRaycasts &&
            !finish.GetComponentInChildren<Image>().raycastTarget,
            "the screen finish must leave all MFD and map input untouched");
        MfdScreenFinish.Ensure(canvas);
        Check(GameObject.Find("NOAvionics.ScreenFinish") == finish,
            "reconciling the map must reuse the single screen finish");
        CheckFinishTexture();
        var glass = finish.GetComponentInChildren<AvDisplayGlass>();
        AvDisplayGlass.Configure(true, .4f, false, .8f, .6f, 2, .5f);
        glass.Update();
        Image finishImage = glass.GetComponent<Image>();
        bool shaderFinish = finishImage.material != null && finishImage.material.HasProperty("_Reflection");
        Check(shaderFinish ? Mathf.Approximately(finishImage.material.GetFloat("_Reflection"), .4f * .45f) :
            Mathf.Approximately(finishImage.color.a, .4f), "manual glass strength must not depend on ambient light");
        var layers = finish.GetComponentsInChildren<RawImage>();
        Check(layers.Length == (shaderFinish ? 0 : 3),
            "shader finish is one quad; fallback owns exactly three bounded overlays");
        foreach (var layer in layers)
            Check(layer.enabled && !layer.raycastTarget && layer.color.a > 0f && layer.color.a <= .5f,
                "enabled overlays must remain bounded and input transparent");
        AvDisplayGlass.Configure(false, .4f, false, .8f, .6f, 2, .5f);
        glass.Update();
        Check(!glass.GetComponent<Image>().enabled, "master OFF removes the original glass");
        foreach (var layer in layers) Check(!layer.enabled, "master OFF removes every extra overlay");
        AvDisplayGlass.Configure(true, .35f, true, 0f, 0f, 0, .25f);
        glass.Update();
        Check(glass.GetComponent<Image>().enabled, "master ON restores glass without rebuilding");
        foreach (var layer in layers) Check(!layer.enabled, "default finish has no CRT, vignette or tint");

        var buttons = new List<Button>();
        var screens = new List<MFDScreen>();
        for (int i = 0; i < Labels.Length; i++) AddSlot(buttons, screens, Labels[i]);
        // A spare slot (a live button with no screen behind it) and a code the catalog does not know.
        buttons.Add(MakeVanillaButton("-"));
        screens.Add(null);
        AddSlot(buttons, screens, "SUD");

        Check(MfdRail.TryGetRail(out RectTransform rail) && rail.rect.width >= 150f, "the built rail must match the resolved column");
        Check(MfdRail.Fits(columns.Rail.height, Labels.Length + 1), "the mission's screens must fit the rail");
        Check(!MfdRail.Fits(columns.Rail.height, 40), "a bezel too long for 44px keys must be reported as not fitting");
        int built = MfdRail.Build(buttons, screens, null, null);
        Check(built == Labels.Length + 1, "every slot with a screen gets a key and the spare gets none, built=" + built);
        MfdRailKey unknown = MfdRail.Keys[MfdRail.Keys.Count - 1];
        Check(unknown.Entry.Code == "SUD" && !unknown.Entry.HasName && unknown.Entry.Group == MfdRailGroup.Aux,
            "an unknown code keeps its code, no invented name, and the last place");
        Check(Texts(unknown).Count(t => t.font == AvBundle.Font(AvTypeScale.AssetName(AvFace.Icons))) == 1,
            "an unknown code still gets a glyph");

        // The render shows the rail as a mission has it: the same slots without the unknown code.
        buttons.RemoveAt(buttons.Count - 1);
        screens.RemoveAt(screens.Count - 1);
        built = MfdRail.Build(buttons, screens, null, null);
        IReadOnlyList<MfdRailKey> keys = MfdRail.Keys;
        Check(built == Labels.Length && keys.Count == Labels.Length && rail.GetComponentsInChildren<AvHit>(true).Length == Labels.Length,
            "a rebuild replaces the keys instead of stacking a second set");
        MfdRailKey first = keys[0];
        Check(MfdRail.Build(buttons, screens, null, null) == Labels.Length && ReferenceEquals(MfdRail.Keys[0], first) && first.Rect != null,
            "an unchanged bezel keeps the keys it has");
        string order = string.Join(" ", keys.Select(k => k.Entry.Code));
        Check(order == "FAC MAP STR EVN ENV MIS TGT WMC OPS COM RAD HUD PILOT SET",
            "keys gather by purpose in the catalog's order, got " + order);
        MfdRailKey map = keys[1];
        Check(Texts(map).Any(t => t.text == "MAP") && Texts(map).Any(t => t.text == "TACTICAL"),
            "a key carries the code and the word it stands for");
        string groups = string.Join(" ", rail.GetComponentsInChildren<TMP_Text>(false).Where(t => t.name == "Group").Select(t => t.text));
        Check(groups == "SITUATION TASKING COMMS SYSTEM", "each group of keys sits under its label, got " + groups);

        map.Press();
        Check(screens[1].isActive, "a key press must reach the bezel button's own binding");
        Check(map.Latched && keys.Count(k => k.Latched) == 1, "exactly the open screen's key latches");
        Check(Texts(map).Any(t => t.text == "ON"), "the open key says so in words, not by colour alone");
        Color accent = AvStyleHost.FuiColor("caution", AvTheme.Warning);
        Check(map.GlyphColor == accent && keys[2].GlyphColor != accent, "the open key's glyph takes the tab accent");
        Check(buttons[1].GetComponentInChildren<TMP_Text>().text == "MAP" && buttons[1].GetComponent<Image>().sprite == AvSprites.Control &&
            buttons[1].transform.childCount == 1, "the rail must leave the bezel button exactly as the game made it");

        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in rail.GetComponentsInChildren<TMP_Text>(false))
        {
            text.ForceMeshUpdate();
            Check(text.fontSize >= AvTypeScale.Floor, "rail text below the 11 px floor: '" + text.text + "' at " + text.fontSize);
            Check(text.preferredWidth <= text.rectTransform.rect.width + 0.5f,
                "rail text wider than its box: '" + text.text + "' " + text.preferredWidth + " > " + text.rectTransform.rect.width);
        }

        // One key hovered, so the render shows all three looks: rest, hover and open.
        keys[6].Rect.GetComponentInChildren<AvHit>().Hover(true);
        RenderRail(canvas, rail, "RAIL.png");

        // The bug this check exists for: the rail used to keep the colours of the scheme it was built under.
        AvFrame plate = rail.Find("Plate").GetComponent<AvFrame>();
        Color portalPlate = plate.FillColor, portalGlyph = map.GlyphColor;
        AvStyleHost.SetTheme(AvThemeId.Phosphor);
        rail.GetComponent<AvTicker>().RestyleAll();
        Check(plate.FillColor != portalPlate, "a scheme change must repaint the rail plate");
        Check(map.GlyphColor != portalGlyph && map.GlyphColor == AvStyleHost.FuiColor("caution", AvTheme.Warning),
            "a scheme change must repaint the keys, the open one in the new scheme's accent");
        RenderRail(canvas, rail, "RAIL-phosphor.png");
        AvStyleHost.SetTheme(AvThemeId.Portal);

        map.Press();
        Check(!screens[1].isActive && !map.Latched, "pressing the open key closes its screen and unlatches the key");

        // The game's buttons wait in the rail's stowage; losing the rail must never take one with it.
        buttons[0].transform.SetParent(MfdRail.Stowage, false);
        MfdRail.Reset();
        Check(!MfdRail.TryGetRail(out _), "reset must forget the rail");
        Check(buttons[0] != null && buttons[0].transform.parent == canvas.transform,
            "reset must release a stowed bezel button, not destroy it");

        Object.DestroyImmediate(root);
    }

    private static void AddSlot(List<Button> buttons, List<MFDScreen> screens, string code)
    {
        MFDScreen screen = new GameObject("Screen_" + code, typeof(MFDScreen)).GetComponent<MFDScreen>();
        screen.shortName = code;
        Button button = MakeVanillaButton(code);
        // Stand-in for VirtualMFD.PressLeftButton: a press toggles its screen and closes the others.
        button.onClick.AddListener(() =>
        {
            bool open = !screen.isActive;
            foreach (MFDScreen other in screens) if (other != null) other.isActive = false;
            screen.isActive = open;
        });
        buttons.Add(button);
        screens.Add(screen);
    }

    private static TMP_Text[] Texts(MfdRailKey key) => key.Rect.GetComponentsInChildren<TMP_Text>(true);

    private static void RenderRail(Canvas canvas, RectTransform rail, string file)
    {
        float half = rail.rect.height * 0.5f + 6f;
        var camera = OrthoCamera("Camera", half, new Color(.08f, .13f, .16f), Vector3.zero);
        camera.transform.position = new Vector3(rail.anchoredPosition.x, rail.anchoredPosition.y, -10f);
        int height = Mathf.RoundToInt(half * 4f);
        Render(canvas, camera, Mathf.RoundToInt(height * (rail.rect.width + 12f) / (half * 2f)), height, file);
        Object.DestroyImmediate(camera.gameObject);
    }

    private static void CheckFinishTexture()
    {
        Texture2D source = AvSprites.DisplayScreen.texture;
        var target = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, target);
        RenderTexture.active = target;
        var pixels = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        pixels.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
        pixels.Apply();
        RenderTexture.active = null;

        Color centre = pixels.GetPixel(source.width / 2, source.height / 2);
        Color lip = pixels.GetPixel(0, source.height / 2);
        Color otherLip = pixels.GetPixel(source.width - 1, source.height / 2);
        Color reflection = pixels.GetPixel(source.width / 4, source.height - 24);
        Check(centre.a >= 0.035f && centre.a <= 0.07f,
            "the map-centre screen finish must remain perceptible without obscuring labels (alpha " + centre.a + ")");
        Check(reflection.a >= centre.a + 0.04f,
            "the whole-screen glass must carry a visible broad reflection");
        Check(lip.r >= lip.b + 0.04f && otherLip.b >= otherLip.r + 0.04f,
            "the screen edges must have a restrained red/cyan fringe");

        Object.DestroyImmediate(pixels);
        Object.DestroyImmediate(target);
    }

    private static Button MakeVanillaButton(string text)
    {
        var go = new GameObject("Bezel_" + text, typeof(RectTransform), typeof(Image), typeof(Button));
        Image image = go.GetComponent<Image>();
        image.sprite = AvSprites.Control;
        image.type = Image.Type.Sliced;
        image.color = new Color(.25f, .25f, .25f, 1f);

        var labelGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 20f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 8f;
        label.fontSizeMax = 24f;
        label.alignment = TextAlignmentOptions.Center;
        label.richText = false;
        label.color = Color.white;
        return go.GetComponent<Button>();
    }

    // ------------------------------------------------------------------- plumbing

    private static void Render(Canvas canvas, Camera camera, int width, int height, string file)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();

        CapturePng(camera, width, height, file);
    }
}
#endif
