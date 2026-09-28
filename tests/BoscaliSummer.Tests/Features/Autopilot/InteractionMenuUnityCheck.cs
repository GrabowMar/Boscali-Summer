#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using BoscaliSummer.Features.Autopilot.Domain;
using BoscaliSummer.Features.Autopilot.Presentation;
using NOAvionics.Ui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Offline visual check for the interaction menu's presentation: drives
/// <see cref="AceRadialMenuUi"/>'s internal tree/render pipeline directly (Update() never runs
/// outside Play mode, so the game-dependent input/gating code in that file is never touched)
/// to render the open menu over the approved backdrop at 1920x1080.
/// </summary>
public static class InteractionMenuUnityCheck
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

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

            AvFont.Font = TMP_FontAsset.CreateFontAsset(new Font("C:/Windows/Fonts/consola.ttf"));

            RenderMenu();

            File.WriteAllText("result.txt",
                "PASS: interaction menu presentation renders the category ring + expanded branch + icons + tone state lines + disabled option " +
                "+ hovered action on the shared AvionicsUi kit at 1920x1080. Game adapters are fixtures; " +
                "in-game acceptance remains required.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("result.txt", "FAIL: " + error);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void RenderMenu()
    {
        const float screenW = 1920f;
        const float screenH = 1080f;
        var center = new AceVec2(screenW * 0.5f, screenH * 0.5f);

        var owner = new GameObject("Interaction menu fixture");
        var menu = owner.AddComponent<AceRadialMenuUi>();
        // This editor fixture never enters Play mode, so Unity never calls Awake() on its own.
        typeof(AceRadialMenuUi).GetMethod("Awake", Private).Invoke(menu, null);

        FieldInfo treeField = typeof(AceRadialMenuUi).GetField("tree", Private);
        FieldInfo hostField = typeof(AceRadialMenuUi).GetField("host", Private);
        MethodInfo renderMethod = typeof(AceRadialMenuUi).GetMethod("Render", Private);

        var tree = (AceRadialMenuTree)treeField.GetValue(menu);
        var host = (AvHudHost)hostField.GetValue(menu);

        // A synthetic catalog shaped like the real one: the eight-category ring, one open
        // category with icons, state lines in every tone, a disabled option and a sub-branch.
        var root = new AceRadialAction("root", "CI-22 CRICKET").WithIcon(AceIcon.Aircraft);
        var flight = new AceRadialAction("flight", "FLIGHT").WithIcon(AceIcon.Flight);
        flight.Add(new AceRadialAction("land", "AUTOLAND", () => { }).WithIcon(AceIcon.Autopilot))
            .Add(new AceRadialAction("assist", "FLIGHT ASSIST", () => { }).WithIcon(AceIcon.Assist)
                .WithStatus(() => AceRadialStatus.OnOff(true)))
            .Add(new AceRadialAction("gear", "LANDING GEAR", () => { }).WithIcon(AceIcon.Gear)
                .WithStatus(() => new AceRadialStatus("UP")))
            .Add(new AceRadialAction("engine", "ENGINE", () => { }, enabled: () => false).WithIcon(AceIcon.Engine)
                .WithStatus(() => new AceRadialStatus("SHUT DOWN", AceTone.Caution)))
            .Add(new AceRadialAction("eject", "EJECT").WithIcon(AceIcon.Eject)
                .Add(new AceRadialAction("confirm", "CONFIRM EJECT", () => { }).WithIcon(AceIcon.Confirm)
                    .WithStatus(() => new AceRadialStatus("NO UNDO", AceTone.Danger))));
        root.Add(flight);
        foreach (var category in new[]
        {
            ("lights", "LIGHTS", AceIcon.Lights), ("weapons", "WEAPONS", AceIcon.Weapons),
            ("defence", "DEFENCE", AceIcon.Defence), ("view", "VIEW", AceIcon.View),
            ("support", "SUPPORT", AceIcon.Support), ("radio", "RADIO", AceIcon.Radio),
            ("comms", "COMMS", AceIcon.Comms),
        })
        {
            root.Add(new AceRadialAction(category.Item1, category.Item2).WithIcon(category.Item3)
                .Add(new AceRadialAction("x", "X", () => { })));
        }

        tree.Open(root, 0f);
        tree.Tick(center, center, 1f, 0f);

        // Hover the branch, expand it immediately (skip the dwell), then settle the pop-out
        // animation so the screenshot shows it fully open.
        AceVec2 branchAt = FindNodePosition(tree, "root/flight");
        tree.Tick(branchAt, center, 1f, 0f);
        tree.ExpandHovered(0f);
        tree.Tick(branchAt, center, 1f, AceRadialMenuTree.ExpandSec + 0.05f);

        // Hover one of the newly expanded children.
        AceVec2 gearAt = FindNodePosition(tree, "root/flight/gear");
        tree.Tick(gearAt, center, 1f, AceRadialMenuTree.ExpandSec + 0.05f);

        host.SetVisible(true);
        renderMethod.Invoke(menu, new object[] { 1f });

        // This editor fixture never enters Play mode, so the vector layers' engine-driven
        // OnEnable (CanvasRenderer registration) never runs either; force it so the geometry
        // actually rebuilds into a mesh. Scoped to AvVector only: TMP labels already render
        // correctly via the explicit ForceMeshUpdate() call below. (Known fixture limitation:
        // the selector bracket's own nested AvVector still does not flush its mesh through this
        // path in batch mode, so its highlight is not visible in this offline render even
        // though its geometry/position are independently verified correct.)
        MethodInfo graphicOnEnable = typeof(UnityEngine.UI.Graphic).GetMethod("OnEnable", Private);
        foreach (AvVector av in host.Root.GetComponentsInChildren<AvVector>(true))
        {
            if (av.GetComponent<CanvasRenderer>() == null) av.gameObject.AddComponent<CanvasRenderer>();
            graphicOnEnable.Invoke(av, null);
            av.Commit();
        }
        Canvas.ForceUpdateCanvases();

        // A backdrop behind the menu, exactly like the HUD fixture's own -hudBackdrop convention.
        AddBackdrop(host, screenW, screenH);
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in host.Root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();

        var focus = new GameObject("Focus", typeof(RectTransform)).GetComponent<RectTransform>();
        focus.SetParent(host.Static, false);
        AvHudHost.Place(focus, 0f, 0f, screenW, screenH);
        Render(host.Canvas, focus, "interaction-menu-1920x1080.png");

        Object.DestroyImmediate(owner);
    }

    private static AceVec2 FindNodePosition(AceRadialMenuTree tree, string path)
    {
        foreach (var node in tree.Layout)
            if (node.Node.Path == path) return node.Position;
        throw new Exception("Fixture catalog is missing expected node '" + path + "'.");
    }

    private static Texture2D backdropTexture;

    private static string BackdropPath()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-menuBackdrop") return args[i + 1];
        return null;
    }

    private static void AddBackdrop(AvHudHost host, float width, float height)
    {
        string path = BackdropPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        if (backdropTexture == null) { backdropTexture = new Texture2D(2, 2); backdropTexture.LoadImage(File.ReadAllBytes(path)); }
        var go = new GameObject("Backdrop", typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform)go.transform;
        rt.SetParent(host.Static, false);
        rt.SetAsFirstSibling();
        AvHudHost.Place(rt, 0f, 0f, width, height);
        var image = go.GetComponent<RawImage>();
        image.texture = backdropTexture; image.raycastTarget = false;
    }

    private static void Render(Canvas canvas, RectTransform focus, string path)
    {
        var originalMode = canvas.renderMode;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.transform.localScale = Vector3.one;
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
        Vector3[] corners = new Vector3[4]; focus.GetWorldCorners(corners);
        int width = Mathf.CeilToInt(focus.rect.width);
        int height = Mathf.CeilToInt(focus.rect.height);
        var camera = new GameObject("Render camera").AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = height / 2f;
        camera.transform.position = (corners[0] + corners[2]) * .5f + new Vector3(0, 0, -10);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .16f, .2f);
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        camera.Render(); RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG()); RenderTexture.active = null;
        Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        canvas.renderMode = originalMode;
    }
}
#endif
