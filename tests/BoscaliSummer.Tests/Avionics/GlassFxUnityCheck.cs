#if UNITY_EDITOR
using System;
using System.IO;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders the full-display finish (NOA/UI/Glass2, from avionics-fx.bundle) over a stand-in picture at fixed
/// instants: idle with the refresh band mid-screen, four steps of the power-on raster, two jammed frames and
/// a still display. Saves each frame and asserts what the live signal promises: the tube is dark outside the
/// opening raster, the picture is back by the end, jamming changes the frame, and LIVE SIGNAL 0 does none of it.
/// </summary>
public static class GlassFxUnityCheck
{
    private const int W = 1280, H = 720;
    private const float Now = 703.5f;   // frac(Now / 7) = 0.5: the refresh band sits mid-screen

    public static void Run()
    {
        string result;
        try
        {
            AvBundle.ResetForTests();
            AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            AvBundle.LoadFxFromBytes(File.ReadAllBytes("avionics-fx.bundle"), Debug.Log);
            Shader shader = AvBundle.Shader("NOA/UI/Glass2");
            Require(shader != null, "Glass2 loads from avionics-fx.bundle");
            Require(AvFxDriver.GlassMaterial != null && AvFxDriver.GlassMaterial.shader == shader, "the shared glass material prefers Glass2");

            Camera cam = new GameObject("cam", typeof(Camera)).GetComponent<Camera>();
            cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            var target = new RenderTexture(W, H, 24);
            cam.targetTexture = target;
            Canvas canvas = new GameObject("canvas", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 5f;

            // Stand-in for a maximised MFD: ground, a left panel, a map and a few lit strokes.
            Solid(canvas.transform, "ground", new Color(.035f, .060f, .070f), 0f, 0f, 1f, 1f);
            Solid(canvas.transform, "panel", new Color(.090f, .130f, .150f), .02f, .04f, .26f, .96f);
            Solid(canvas.transform, "map", new Color(.150f, .210f, .190f), .28f, .04f, .92f, .96f);
            for (int i = 0; i < 6; i++)
                Solid(canvas.transform, "stroke" + i, new Color(.33f, .85f, .94f), .04f, .86f - i * .12f, .12f + i * .02f, .88f - i * .12f);
            Image glass = Solid(canvas.transform, "glass", Color.white, 0f, 0f, 1f, 1f);
            var material = new Material(shader);
            material.SetTexture("_NoiseTex", AvBundle.Noise);
            material.SetFloat("_Reflection", .16f); material.SetFloat("_Scan", .3f); material.SetFloat("_Edge", .3f);
            material.SetFloat("_Light", 1f);
            glass.material = material;
            Shader.SetGlobalFloat("_NOA_FxTier", 2f);

            Texture2D idle = Shot(cam, target, material, .6f, 0f, 0f, "glass-idle");
            Texture2D still = Shot(cam, target, material, 0f, .08f, 0f, "glass-still-display");
            Texture2D boot1 = Shot(cam, target, material, .6f, .08f, 0f, "glass-power-on-1");
            Shot(cam, target, material, .6f, .30f, 0f, "glass-power-on-2");
            Shot(cam, target, material, .6f, .60f, 0f, "glass-power-on-3");
            Texture2D boot4 = Shot(cam, target, material, .6f, .92f, 0f, "glass-power-on-4");
            Texture2D jam = Shot(cam, target, material, .6f, 0f, .6f, "glass-jam-1");
            Shader.SetGlobalFloat("_NOA_Now", Now + .12f);
            Texture2D jam2 = Shot(cam, target, material, .6f, 0f, .6f, "glass-jam-2", Now + .12f);

            Require(Lum(idle, 40, 40) > .01f, "idle: the corner of the picture is visible");
            Require(Lum(boot1, 40, 40) < .004f, "power-on start: the tube is dark outside the raster");
            Require(Lum(boot1, W / 2, H / 2) > Lum(idle, W / 2, H / 2), "power-on start: the opening line is brighter than the picture");
            Require(Lum(boot4, 40, 40) > .01f, "power-on end: the picture is back");
            Require(Lum(still, 40, 40) > .01f, "LIVE SIGNAL 0: no power-on blanking");
            Require(Difference(idle, jam) > .002f, "jamming changes the frame");
            Require(Difference(jam, jam2) > .002f, "jamming moves between frames");
            result = "PASS: Glass2 renders idle, power-on, jam and still-display frames; live signal assertions hold.";
        }
        catch (Exception e) { result = "FAIL: " + e; }
        File.WriteAllText("result.txt", result);
        UnityEditor.EditorApplication.Exit(result.StartsWith("PASS") ? 0 : 1);
    }

    // boot01 > 0: the display powered on that fraction of the 0.34 s raster ago; 0: long ago.
    private static Texture2D Shot(Camera cam, RenderTexture target, Material material, float life, float boot01, float glitch,
        string name, float now = Now)
    {
        material.SetFloat("_Life", life);
        Shader.SetGlobalFloat("_NOA_Now", now);
        Shader.SetGlobalFloat("_NOA_Boot", boot01 > 0f ? now - boot01 * .34f : 0f);
        Shader.SetGlobalFloat("_NOA_Glitch", glitch);
        Canvas.ForceUpdateCanvases();
        cam.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(W, H, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, W, H), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(name + ".png", image.EncodeToPNG());
        return image;
    }

    private static float Lum(Texture2D t, int x, int y)
    {
        Color c = t.GetPixel(x, y);
        return .2126f * c.r + .7152f * c.g + .0722f * c.b;
    }

    private static float Difference(Texture2D a, Texture2D b)
    {
        float sum = 0f; int n = 0;
        for (int y = 8; y < H; y += 16)
            for (int x = 8; x < W; x += 16) { sum += Mathf.Abs(Lum(a, x, y) - Lum(b, x, y)); n++; }
        return sum / n;
    }

    private static Image Solid(Transform parent, string name, Color color, float x0, float y0, float x1, float y1)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Image image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static void Require(bool ok, string what) { if (!ok) throw new Exception(what); }
}
#endif
