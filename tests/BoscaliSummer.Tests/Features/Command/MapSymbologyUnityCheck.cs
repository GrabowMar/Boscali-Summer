#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Offline contact-symbol sheet: renders the real atlas sprites through a real UI camera at the sizes the
/// map draws them (and magnified), over a relief-like backdrop, next to the game's bare icons for
/// comparison. Checks the atlas invariants (opaque outline, transparent corners, every sprite builds)
/// and writes PNGs for review. The game's aircraft and ship sprites are read from BOSCALI_VANILLA_ICONS
/// (white-with-alpha exports of the vanilla map icons) when that folder exists.
/// </summary>
public static class MapSymbologyUnityCheck
{
    private const int Width = 1600, Height = 1000;
    private static readonly Color Friendly = new Color(0.20f, 0.60f, 1.00f);
    private static readonly Color Hostile = new Color(1.00f, 0.28f, 0.24f);
    private static readonly Color Neutral = new Color(0.72f, 0.78f, 0.82f);

    private static readonly string[] Air =
        { "mapIcon_multirole1", "mapIcon_smallFighter1", "HELO", "mapIcon_trainer", "mapIcon_cricket", "mapIcon_quadVTOL1" };
    private static readonly string[] Sea =
        { "HULL", "HULL", "HULL" };
    private static readonly MapGlyph[] Ground =
        { MapGlyph.Armor, MapGlyph.Light, MapGlyph.Supply, MapGlyph.Artillery, MapGlyph.AntiAir, MapGlyph.Sam, MapGlyph.Radar };

    private static Transform root;
    private static Font font;

    public static void Run()
    {
        try
        {
            VerifyAtlas();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var cam = new GameObject("Cam", typeof(Camera)).GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = Height * 0.5f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(Width, Height);
            root = canvas.transform;

            AddBackdrop();
            string iconDir = Environment.GetEnvironmentVariable("BOSCALI_VANILLA_ICONS");
            var vanilla = new Dictionary<string, Sprite>();
            if (!string.IsNullOrEmpty(iconDir) && Directory.Exists(iconDir))
                foreach (string file in Directory.GetFiles(iconDir, "*.png"))
                    vanilla[Path.GetFileNameWithoutExtension(file)] = Load(file);

            const float plate = 24f;
            const float glyph = plate / MapSymbology.PlateRatio;

            Label("REAL SIZE  plate " + plate + " px  glyph " + glyph.ToString("0.0") + " px  (ground | air | sea)", 12, 8);
            MapSide[] sides = { MapSide.Friendly, MapSide.Hostile, MapSide.Neutral };
            Color[] tints = { Friendly, Hostile, Neutral };
            for (int s = 0; s < sides.Length; s++)
            {
                float y = 44f + s * 40f, x = 30f;
                foreach (MapGlyph g in Ground) { Unit(x, y, sides[s], tints[s], MapSymbolAtlas.Glyph(g), plate); x += 38f; }
                x += 16f;
                foreach (string n in Air) { Unit(x, y, sides[s], tints[s], Pick(vanilla, n, MapGlyph.Aircraft), plate); x += 38f; }
                x += 16f;
                foreach (string n in Sea) { Unit(x, y, sides[s], tints[s], Pick(vanilla, n, MapGlyph.Hull), plate); x += 38f; }
            }

            Label("BEFORE: the game's bare icons at the old 11 px cap (vehicle blob | air | hairline hulls)", 12, 176);
            for (int s = 0; s < 2; s++)
            {
                float y = 210f + s * 26f, x = 30f;
                Bare(x, y, Pick(vanilla, "mapIcon_vehicle", MapGlyph.Light), tints[s], 11f); x += 38f;
                foreach (string n in Air) { Bare(x + 38f, y, Pick(vanilla, n, MapGlyph.Aircraft), tints[s], 11f); x += 38f; }
                x += 16f;
                foreach (string n in new[] { "patrolBoat1_mapIcon", "frigate1_mapIcon", "fleetCarrier1_mapIcon" }) { Bare(x + 38f, y, Pick(vanilla, n, MapGlyph.Hull), tints[s], 11f); x += 38f; }
            }

            Label("MAGNIFIED 3x  plate 72 px", 12, 268);
            for (int s = 0; s < 2; s++)
            {
                float y = 330f + s * 84f, x = 60f;
                foreach (MapGlyph g in Enum.GetValues(typeof(MapGlyph)))
                {
                    Unit(x, y, sides[s], tints[s], MapSymbolAtlas.Glyph(g), 72f);
                    x += 90f;
                }
                foreach (string n in new[] { Air[0], Air[1], Air[4], Air[5] })
                {
                    Unit(x, y, sides[s], tints[s], Pick(vanilla, n, MapGlyph.Aircraft), 72f);
                    x += 90f;
                }
            }

            Label("DENSITY  mixed platoon, real size, both sides", 12, 508);
            var random = new System.Random(7);
            MapGlyph[] all = { MapGlyph.Armor, MapGlyph.Light, MapGlyph.Supply, MapGlyph.Artillery, MapGlyph.AntiAir, MapGlyph.Sam, MapGlyph.Radar };
            for (int i = 0; i < 26; i++)
            {
                bool hostile = i % 2 == 1;
                float x = 60f + (float)random.NextDouble() * 320f + (hostile ? 380f : 0f);
                float y = 560f + (float)random.NextDouble() * 150f;
                Unit(x, y, hostile ? MapSide.Hostile : MapSide.Friendly, hostile ? Hostile : Friendly,
                    MapSymbolAtlas.Glyph(all[random.Next(all.Length)]), plate);
            }
            for (int i = 0; i < 8; i++)
            {
                bool hostile = i % 3 == 0;
                float x = 60f + (float)random.NextDouble() * 700f;
                float y = 560f + (float)random.NextDouble() * 150f;
                Unit(x, y, hostile ? MapSide.Hostile : MapSide.Friendly, hostile ? Hostile : Friendly,
                    Pick(vanilla, Air[random.Next(Air.Length)], MapGlyph.Aircraft), plate);
            }

            Canvas.ForceUpdateCanvases();
            var target = new RenderTexture(Width, Height, 24);
            cam.targetTexture = target;
            cam.Render();
            RenderTexture.active = target;
            var output = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            output.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            output.Apply();
            File.WriteAllBytes("symbol-sheet.png", output.EncodeToPNG());
            // The real-size rows blown up with nearest sampling: what the pixels truly look like.
            File.WriteAllBytes("symbol-sheet-real3x.png", Crop(output, 0, Height - 250, 800, 250, 3).EncodeToPNG());
            File.WriteAllText("result.txt", "PASS: atlas invariants hold and the symbol sheet rendered.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("result.txt", "FAIL: " + error);
            EditorApplication.Exit(1);
        }
    }

    private static void VerifyAtlas()
    {
        foreach (MapSide side in Enum.GetValues(typeof(MapSide)))
        {
            Color32[] pixels = MapSymbolAtlas.RenderPlate(side);
            int n = MapSymbolAtlas.Size;
            if (pixels[0].a != 0 || pixels[n - 1].a != 0 || pixels[n * (n - 1)].a != 0 || pixels[n * n - 1].a != 0)
                throw new Exception("Plate " + side + " is not clear at the corners.");
            if (pixels[(n / 2) * n + n / 2].a < 120)
                throw new Exception("Plate " + side + " has no dark fill behind the glyph.");
            int bright = 0;
            foreach (Color32 p in pixels) if (p.a == 255 && p.r > 240) bright++;
            if (bright < 120) throw new Exception("Plate " + side + " has no tintable outline.");
            if (MapSymbolAtlas.Plate(side) == null) throw new Exception("Plate " + side + " did not build.");
        }
        foreach (MapGlyph glyph in Enum.GetValues(typeof(MapGlyph)))
        {
            Color32[] pixels = MapSymbolAtlas.RenderGlyph(glyph);
            int n = MapSymbolAtlas.Size, covered = 0, edge = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a > 128) covered++;
                int x = i % n, y = i / n;
                if ((x < 1 || y < 1 || x > n - 2 || y > n - 2) && pixels[i].a > 0) edge++;
            }
            if (covered < 200 || covered > n * n * 0.6f)
                throw new Exception("Glyph " + glyph + " covers " + covered + " px: unreadable or a blob.");
            if (edge > 0) throw new Exception("Glyph " + glyph + " touches the sprite border.");
            if (MapSymbolAtlas.Glyph(glyph) == null) throw new Exception("Glyph " + glyph + " did not build.");
        }
    }

    private static Sprite Pick(Dictionary<string, Sprite> vanilla, string name, MapGlyph fallback)
    {
        if (name == "HELO") return MapSymbolAtlas.Glyph(MapGlyph.Helicopter);
        if (name == "HULL") return MapSymbolAtlas.Glyph(MapGlyph.Hull);
        return vanilla.TryGetValue(name, out Sprite sprite) ? sprite : MapSymbolAtlas.Glyph(fallback);
    }

    private static Sprite Load(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear };
        texture.LoadImage(File.ReadAllBytes(path));
        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
    }

    private static void AddBackdrop()
    {
        string map = Environment.GetEnvironmentVariable("BOSCALI_BACKDROP");
        var texture = new Texture2D(256, 128, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear };
        bool loaded = !string.IsNullOrEmpty(map) && File.Exists(map);
        if (loaded)
        {
            var source = new Texture2D(2, 2);
            source.LoadImage(File.ReadAllBytes(map));
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 256; x++)
                {
                    Color c = source.GetPixelBilinear(.25f + x / 256f * .25f, .25f + y / 128f * .12f);
                    float g = c.grayscale;
                    texture.SetPixel(x, y, new Color(.09f + g * .26f, .13f + g * .32f, .17f + g * .38f));
                }
        }
        else
        {
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 256; x++)
                {
                    float n = Mathf.PerlinNoise(x * .05f, y * .05f) * .6f + Mathf.PerlinNoise(x * .21f, y * .21f) * .4f;
                    texture.SetPixel(x, y, new Color(.09f + n * .26f, .13f + n * .32f, .17f + n * .38f));
                }
        }
        texture.Apply();
        var image = new GameObject("Backdrop", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(root, false);
        image.texture = texture;
        image.raycastTarget = false;
        ((RectTransform)image.transform).sizeDelta = new Vector2(Width, Height);
    }

    private static void Unit(float x, float y, MapSide side, Color tint, Sprite glyph, float plate)
    {
        Add(x, y, MapSymbolAtlas.Plate(side), tint, plate);
        Add(x, y, glyph, tint, plate / MapSymbology.PlateRatio);
    }

    private static void Bare(float x, float y, Sprite sprite, Color tint, float size) => Add(x, y, sprite, tint, size);

    private static void Add(float x, float y, Sprite sprite, Color tint, float size)
    {
        var image = new GameObject("Symbol", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(root, false);
        image.sprite = sprite;
        image.color = tint;
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = new Vector2(x - Width * .5f, Height * .5f - y);
    }

    private static void Label(string text, float x, float y)
    {
        var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.transform.SetParent(root, false);
        label.font = font;
        label.fontSize = 13;
        label.color = new Color(.85f, .9f, .95f);
        label.text = text;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.alignment = TextAnchor.MiddleLeft;
        var rect = label.rectTransform;
        rect.sizeDelta = new Vector2(900f, 18f);
        rect.pivot = new Vector2(0f, .5f);
        rect.anchoredPosition = new Vector2(x - Width * .5f, Height * .5f - y - 9f);
    }

    private static Texture2D Crop(Texture2D source, int x0, int y0, int w, int h, int scale)
    {
        var result = new Texture2D(w * scale, h * scale, TextureFormat.RGB24, false);
        for (int y = 0; y < h * scale; y++)
            for (int x = 0; x < w * scale; x++)
                result.SetPixel(x, y, source.GetPixel(x0 + x / scale, y0 + y / scale));
        result.Apply();
        return result;
    }
}
#endif
