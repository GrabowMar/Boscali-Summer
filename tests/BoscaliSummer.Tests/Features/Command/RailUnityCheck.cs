#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

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

    private static readonly string[] Labels =
    {
        "BDF", "MAP", "MFD", "SUD", "DPS", "PALA", "TGT", "MIS", "RAD", "SET", "WMC", "STR",
        "EVN", "ENV",
    };

    public static void Run()
    {
        try
        {
            if (Shader.Find("TextMeshPro/Distance Field") == null)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                AssetDatabase.importPackageCompleted += _ => EditorApplication.delayCall += Run;
                AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
                return;
            }

            var setPaths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var parameters = setPaths.GetParameters();
            var arguments = new object[parameters.Length];
            arguments[0] = Path.GetFullPath("RailCheck.exe");
            for (int i = 1; i < arguments.Length; i++) arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            setPaths.Invoke(null, arguments);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvFont.Font = TMP_FontAsset.CreateFontAsset(new Font("C:/Windows/Fonts/consola.ttf"));
            new GameObject("Events", typeof(EventSystem));

            CheckGlyphGeometry();
            CheckUiSounds();
            RenderGlyphStrip();
            CheckRail();

            File.WriteAllText("result.txt",
                "PASS: every catalog glyph builds geometry; the rail brands borrowed buttons with code, descriptor and glyph; " +
                "unknown codes keep the neutral glyph; a repeated adoption pass does not re-brand, stack a second decoration or " +
                "rewrite the line; the full-screen finish is single, above planning rooms, and input-transparent; " +
                "a reset label is re-asserted; the open screen latches; restore returns the vanilla label exactly; " +
                "seven UI cues have bounded samples, soft starts and silent tails. " +
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
            AvKit.Place(rect, new Rect(0f, 0f, 32f, 32f));
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

        var camera = CreateCamera(new Vector3(0f, 0f, -10f), 100f, new Color(.03f, .06f, .05f));
        for (int i = 0; i < Kinds.Length; i++)
        {
            var go = new GameObject("Glyph_" + Kinds[i], typeof(RectTransform), typeof(MfdGlyph));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(root.transform, false);
            AvKit.Place(rect, new Rect(18f + i % 8 * 56f, -(16f + i / 8 * 46f), 32f, 32f));

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
        Check(Mathf.Approximately(glass.GetComponent<Image>().color.a, .4f),
            "manual glass strength must not depend on ambient light");
        var layers = finish.GetComponentsInChildren<RawImage>();
        Check(layers.Length == 3, "finish owns only three additional overlay quads");
        foreach (var layer in layers)
            Check(layer.enabled && !layer.raycastTarget && layer.color.a > 0f && layer.color.a <= .5f,
                "enabled overlays must remain bounded and input transparent");
        AvDisplayGlass.Configure(false, .4f, false, .8f, .6f, 2, .5f);
        glass.Update();
        Check(!glass.GetComponent<Image>().enabled, "master OFF removes the original glass");
        foreach (var layer in layers) Check(!layer.enabled, "master OFF removes every extra overlay");
        AvDisplayGlass.Configure(true, .6f, true, 0f, 0f, 0, .25f);
        glass.Update();
        Check(glass.GetComponent<Image>().enabled, "master ON restores glass without rebuilding");
        foreach (var layer in layers) Check(!layer.enabled, "default finish has no CRT, vignette or tint");

        var buttons = new List<Button>();
        var screens = new List<MFDScreen>();
        for (int i = 0; i < Labels.Length; i++)
        {
            buttons.Add(MakeVanillaButton(Labels[i]));
            screens.Add(new GameObject("Screen_" + Labels[i], typeof(MFDScreen)).GetComponent<MFDScreen>());
        }

        var skins = new List<MfdRail.ButtonSkin>();
        Check(MfdRail.PrepareCapacity(columns.Rail.height, Labels.Length),
            "fourteen keys (six vanilla screens, WMC, the claimed screens and hosted EVN/ENV) must fit the rail");
        int adopted = MfdRail.Adopt(buttons, screens, null, null, skins);
        Check(adopted == Labels.Length, "every slot with a screen must be adopted, adopted=" + adopted);

        Check(skins[0].Label.text.Contains("BDF") && skins[1].Label.text.Contains("PALA"),
            "PALA must sit directly below BDF");
        Check(skins[2].Label.text.Contains("MAP") && skins[2].Label.text.Contains("TACTICAL"),
            "MAP must be branded with its descriptor");
        Check(skins[9].Label.text.Contains("SET") && skins[9].Label.text.Contains("SETTINGS"),
            "SET must be branded with its descriptor");
        Check(skins[12].Label.text.Contains("EVN") && skins[12].Label.text.Contains("EVENTS"),
            "the hosted EVN button must be branded in the rail");
        Check(skins[13].Label.text.Contains("ENV") && skins[13].Label.text.Contains("WEATHER"),
            "the hosted ENV button must be branded in the rail");
        Check(skins[4].Label.text == "SUD", "an unknown code keeps its sanitised code and no invented name");
        Check(skins[4].Icon != null, "an unknown code still gets the neutral glyph");
        Check(skins[2].Icon != null && skins[2].Icon.gameObject.activeSelf,
            "a branded button must carry a glyph");
        Check(skins[2].Label.alignment == TextAlignmentOptions.MidlineLeft,
            "branded labels lead with the icon column");

        skins[1].SetLatched(true);
        Check(skins[1].Background.color != skins[2].Background.color, "the open screen must read differently");
        Check(skins[1].Icon.color == AvTheme.Accent, "a latched glyph takes the accent");

        // The game re-runs VirtualMFD.SetupButtons on faction refreshes and Maximize can
        // fire more than once per opening; both must leave the rail's line intact.
        string branded = skins[2].Label.text;
        var secondPass = new List<MfdRail.ButtonSkin>();
        int readopted = MfdRail.Adopt(buttons, screens, null, null, secondPass);
        Check(readopted == 0 && secondPass.Count == 0,
            "a repeated adoption pass must not re-brand buttons the rail already wears");
        for (int i = 0; i < buttons.Count; i++)
            Check(CountDecorations(buttons[i]) == 1, "a borrowed button must carry exactly one rail decoration");
        Check(skins[2].Label.text == branded, "a repeated pass must not rewrite the branded line");
        skins[2].Label.text = "MAP";
        skins[2].Label.color = Color.green;
        skins[2].Reassert();
        Check(skins[2].Label.text == branded,
            "the rail must put its line back after the game resets the label");
        Check(skins[2].Label.color == AvTheme.TextPrimary,
            "the rail must put its label colour back after the game's style applier repaints it");

        if (MfdRail.TryGetRail(out RectTransform rail))
        {
            Check(rail.rect.width >= 150f, "the built rail must match the resolved column");
            var camera = CreateCamera(Vector3.zero, 470f, new Color(.08f, .13f, .16f));
            float top = rail.anchoredPosition.y + rail.rect.height * 0.5f;
            camera.transform.position = new Vector3(rail.anchoredPosition.x, top - 8f - 462f, -10f);
            Render(canvas, camera, 152, 940, "RAIL.png");
            Object.DestroyImmediate(camera.gameObject);
        }

        for (int i = 0; i < skins.Count; i++) skins[i].Restore();
        Check(skins[0].Label.text == "BDF", "restore must return the vanilla label text");
        Check(skins[0].Decoration == null && skins[0].Icon == null,
            "restore must remove the branding it added");
        MfdRailBrand mark = buttons[0].GetComponent<MfdRailBrand>();
        Check(mark == null || !mark.enabled,
            "restore must release the rail's ownership mark");

        Object.DestroyImmediate(root);
    }

    private static int CountDecorations(Button button)
    {
        int count = 0;
        for (int i = 0; i < button.transform.childCount; i++)
            if (button.transform.GetChild(i).name == "AvDecoration") count++;
        return count;
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

    private static Camera CreateCamera(Vector3 position, float size, Color background)
    {
        var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = size;
        camera.transform.position = position;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = background;
        return camera;
    }

    private static void Render(Canvas canvas, Camera camera, int width, int height, string file)
    {
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();

        var target = new RenderTexture(width, height, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;

        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(file, image.EncodeToPNG());

        RenderTexture.active = null;
        camera.targetTexture = null;
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(image);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
#endif
