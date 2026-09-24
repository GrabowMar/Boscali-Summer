#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NOAvionics.Ui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Renders the production MIS contract desk with synthetic host reports. This checks
// geometry and copy only; authority, replication and live interaction need game tests.
public static class MissionDeskUnityCheck
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run()
    {
        try
        {
            if (Shader.Find("TextMeshPro/Distance Field") == null)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                AssetDatabase.importPackageCompleted += _ => EditorApplication.delayCall += Run;
                AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath,
                    "Package Resources/TMP Essential Resources.unitypackage"), false);
                return;
            }
            MethodInfo paths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            ParameterInfo[] parameters = paths.GetParameters();
            var args = new object[parameters.Length];
            args[0] = Path.GetFullPath("MissionDeskCheck.exe");
            for (int i = 1; i < args.Length; i++)
                args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            paths.Invoke(null, args);
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvFont.Font = TMP_FontAsset.CreateFontAsset(new Font("C:/Windows/Fonts/consola.ttf"));
            new GameObject("Events", typeof(EventSystem));

            Assembly assembly = typeof(AvScreen).Assembly;
            Type type = assembly.GetType(
                "BoscaliSummer.Features.Command.Presentation.MissionContractWindow", true);
            Type cardType = assembly.GetType(
                "BoscaliSummer.Framework.Contracts.SecondaryObjectiveView", true);
            object desk = type.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null);
            type.GetMethod("Show", All).Invoke(desk, null);
            var view = (MonoBehaviour)desk;
            Capture(view, type, "mission-desk-empty-1920x1080.png", 1920f, 1080f);

            IList roster = (IList)type.GetField("roster", All).GetValue(desk);
            for (int i = 0; i < 11; i++)
            {
                bool active = i < 2;
                bool offered = i >= 2 && i < 5;
                string acceptedBy = active || i >= 5
                    ? (i == 0 ? "NIGHTFALL-2" : i == 1 ? "BLUEBIRD-17" : "MARINER-6") : "";
                object[] values =
                {
                    i + 26,
                    i == 0 ? "SURVEY THE AFTERMATH" : i == 1 ? "ROOFTOP INSERTION" :
                        "DISRUPT FORWARD SENSOR LINE " + (i + 1),
                    "Recon patrol requests a clear visual report near the northern depot. " +
                    "Approach from a safe angle and hold the mark while the faction tasking clock runs.",
                    "Northern Depot / Observation Sector", active ? "IN PROGRESS" :
                        offered ? "AWAITING ACCEPTANCE" : "RESULT POSTED",
                    "$1,500 + 125 XP", i == 0 ? .55f : i == 1 ? .2f : i >= 5 ? 1f : 0f,
                    i == 0 ? 192f : 82f + i * 19f,
                    1500, 125, i >= 5, offered, active, true, 0f, 0f, 1f, acceptedBy
                };
                roster.Add(Activator.CreateInstance(cardType, All, null, values, null));
            }
            ((TMP_Text)type.GetField("boardState", All).GetValue(desk)).text =
                "HOST LINK / SYNTHETIC TASKING";
            type.GetField("selectedId", All).SetValue(desk, 26);
            type.GetMethod("Render", All).Invoke(desk, null);
            Capture(view, type, "mission-desk-1920x1080.png", 1920f, 1080f);
            Capture(view, type, "mission-desk-1280x720.png", 1280f, 720f);
            type.GetField("page", All).SetValue(desk, 1);
            type.GetField("selectedId", All).SetValue(desk, 34);
            type.GetMethod("Render", All).Invoke(desk, null);
            Capture(view, type, "mission-desk-page2-1920x1080.png", 1920f, 1080f);
            Object.DestroyImmediate(view.gameObject);

            File.WriteAllText("result.txt",
                "PASS: mission contract desk rendered at 1920x1080 and 1280x720, including empty and second-page states. Synthetic reports only; no live game or multiplayer claim.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText("result.txt", "FAIL: " + error);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void Capture(MonoBehaviour view, Type type, string file,
        float width, float height)
    {
        Canvas canvas = view.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.GetComponent<CanvasScaler>().enabled = false;
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
        type.GetMethod("FitRoom", All).Invoke(view, null);
        Canvas.ForceUpdateCanvases();
        foreach (TMP_Text label in view.GetComponentsInChildren<TMP_Text>(true))
        {
            label.ForceMeshUpdate();
            if (!label.gameObject.activeInHierarchy) continue;
            if (label.text.Length > 0 && label.isTextOverflowing &&
                (label == (TMP_Text)type.GetField("detailTitle", All).GetValue(view) ||
                 label == (TMP_Text)type.GetField("detailAccepted", All).GetValue(view)))
                throw new Exception(file + ": priority text clips: " + label.text);
        }

        var cameraObject = new GameObject("MissionDeskCamera", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = height * .5f;
        camera.transform.position = new Vector3(canvas.transform.position.x,
            canvas.transform.position.y, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.04f, .07f, .06f);
        int pixelWidth = Mathf.RoundToInt(width * 1.5f);
        int pixelHeight = Mathf.RoundToInt(height * 1.5f);
        var target = new RenderTexture(pixelWidth, pixelHeight, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(pixelWidth, pixelHeight, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, pixelWidth, pixelHeight), 0, 0);
        image.Apply();
        File.WriteAllBytes(file, image.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(cameraObject);
        if (!File.Exists(file) || new FileInfo(file).Length == 0)
            throw new Exception("Render failed: " + file);
    }
}
#endif
