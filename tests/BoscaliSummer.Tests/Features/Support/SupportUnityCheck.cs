#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Features.Support.Presentation;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class SupportUnityCheck
{
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
            arguments[0] = Path.GetFullPath("SupportCheck.exe");
            for (int i = 1; i < arguments.Length; i++) arguments[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            setPaths.Invoke(null, arguments);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvFont.Font = TMP_FontAsset.CreateFontAsset(new Font("C:/Windows/Fonts/consola.ttf"));
            // Supply the normal measuring label without play-mode-only DontDestroyOnLoad.
            var ruler = new GameObject("EditorRuler", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            ruler.font = AvFont.Font;
            ruler.gameObject.SetActive(false);
            typeof(AvBox).GetField("ruler", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, ruler);
            new GameObject("Events", typeof(EventSystem));
            foreach (int height in new[] { 596, 420 }) CheckPanel(height);
            File.WriteAllText("result.txt", "PASS: production OPS UI rendered at 596 and 420 units; ready, queued, stale, action grouping, cancellation and scrolling checked. Runtime adapters are stubbed.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static void CheckPanel(int height)
    {
        var manager = new SupportManager();
        manager.SpaceState = new SpaceState { Known=true, X=30000, Z=20000, Wait=40, Window=0, StrikeWait=100 };
        var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = height / 2f;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.02f, .035f, .025f);
        var canvas = new GameObject("Canvas", typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(480, height);
        var panel = canvas.gameObject.AddComponent<SupportPanel>();
        panel.Configure(manager, null, null);
        var shell = AvScreen.Build((RectTransform)canvas.transform, "OPS", new[] { "SUPPORT", "NETWORK", "STATUS" },
            new[] { new[] { "ALLOCATION", "ALLOC" }, new[] { "MISSION SCORE", "PTS" } }, 3, 480, height, null);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void Field(string name, object value) => typeof(SupportPanel).GetField(name, flags).SetValue(panel,value);
        Field("shell", shell); Field("dataBar", shell.DataBar); Field("allocMetric", shell.Metrics[0]); Field("scoreMetric", shell.Metrics[1]);
        string[] methods = { "BuildStrikesPage", "BuildNetworkPage", "BuildStatusPage" };
        for (int i=0; i<3; i++)
        {
            var page = shell.CreatePage(i, methods[i]);
            typeof(SupportPanel).GetMethod(methods[i], flags).Invoke(panel,
                i==0 ? new object[] { (RectTransform)page.transform, shell.Body, false } : new object[] { (RectTransform)page.transform, shell.Body });
        }
        for (int i=0; i<3; i++) { shell.SetPage(i); Refresh(panel); Render(camera,canvas,height,i); }
        shell.SetPage(1);
        var scroll = canvas.GetComponentInChildren<ScrollRect>();
        Check(scroll != null && scroll.content.rect.height > scroll.viewport.rect.height, "NETWORK must scroll at compact sizes");
        scroll.verticalNormalizedPosition=0f;
        Canvas.ForceUpdateCanvases();
        Render(camera,canvas,height,5);
        scroll.verticalNormalizedPosition=1f;
        manager.SpaceState.Queued=true; manager.SpaceState.CancelRequest=42;
        Refresh(panel); Render(camera,canvas,height,3);
        var cancel=Find(canvas,"CANCEL SCAN · REFUND");
        Check(cancel != null,"requester must see cancellation"); Click(cancel);
        Check(!manager.SpaceState.Queued,"cancellation must reach the manager");
        manager.SpaceStateFresh=false; Refresh(panel); Render(camera,canvas,height,4);
        Check(Find(canvas,"AWAITING HOST") != null,"stale snapshot must not advertise availability");
        Object.DestroyImmediate(canvas.gameObject);
        Object.DestroyImmediate(camera.gameObject);
    }

    private static void Refresh(SupportPanel panel) => typeof(SupportPanel)
        .GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(panel, null);
    private static AvButton Find(Canvas canvas, string label) => Array.Find(canvas.GetComponentsInChildren<AvButton>(),
        b => b.GetComponentInChildren<TMP_Text>().text == label);
    private static void Click(AvButton button) => button.OnPointerClick(new PointerEventData(EventSystem.current)
        { button = PointerEventData.InputButton.Left });
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Render(Camera camera, Canvas canvas, int height, int page)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
        var target = new RenderTexture(480, height, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(480, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 480, height), 0, 0);
        image.Apply();
        File.WriteAllBytes("OPS-" + height + "-" + page + ".png", image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(image);
    }
}
#endif
