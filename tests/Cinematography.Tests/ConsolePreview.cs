#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using BoscaliSummer.Modules.Cinematography.Domain;
using BoscaliSummer.Modules.Cinematography.Presentation;
using BoscaliSummer.Modules.Cinematography.Runtime;
using NOAvionics;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

public static class CinematicConsolePreview
{
    public static void Run()
    {
        if (Resources.Load<TMP_Settings>("TMP Settings") == null)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            AssetDatabase.importPackageCompleted += _ => EditorApplication.delayCall += Run;
            AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
            return;
        }
        string result;
        try
        {
            AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
            AvBundle.LoadFromBytes(File.ReadAllBytes("avionics-ui.bundle"), Debug.Log);
            Shader.SetGlobalFloat("_NOA_Now", 1e6f);
            AvFxDriver.Configure(AvFxTier.Off, false); AvStyleHost.SetTheme(AvThemeId.Steel);
            new GameObject("Events", typeof(EventSystem));
            CheckSweptClearance();
            var canvas = new GameObject("Canvas", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920, 1080);
            var owner = new GameObject("PreviewOwner").AddComponent<CinematicDirector>();
            owner.transform.SetParent(canvas.transform, false);
            var console = CinematicConsole.Create(owner);
            var window = (AvWindow)typeof(CinematicConsole).GetField("window", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(console);
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 540;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f,.09f,.11f);
            var target = new RenderTexture(1920, 1080, 24); camera.targetTexture = target;
            Directory.CreateDirectory("renders");window.Show();
            for(int page=0;page<3;page++)
            {
                console.SwitchPage(page);
                typeof(CinematicConsole).GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(console,null);
                window.Body.Relayout();Canvas.ForceUpdateCanvases();
                foreach(TMP_Text text in window.Root.GetComponentsInChildren<TMP_Text>())
                {text.ForceMeshUpdate();if(text.isTextOverflowing || text.fontSize<10)throw new Exception("Text overflow/floor: "+text.text);}
                if(window.Body.ContentHeight>window.Body.ViewportHeight+1)throw new Exception("Outer scroll page "+page+": "+window.Body.ContentHeight+" / "+window.Body.ViewportHeight);
                Save(camera,target,"console-"+new[]{"camera","effects","edit"}[page],new Rect(490,170,940,740));
            }
            window.Hide();
            var overlay=CinematicOverlay.Create(canvas.transform);
            overlay.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            overlay.transform.localPosition=Vector3.zero;overlay.transform.localRotation=Quaternion.identity;overlay.transform.localScale=Vector3.one;
            ((RectTransform)overlay.transform).sizeDelta=new Vector2(1920,1080);
            var film=new ShotPlan {duration=10,options=new ShotOptions {letterbox=.1f,fadeIn=1,fadeOut=1,title="OPERATION CLEAR WINDOW"}};
            overlay.Paint(film,2);Canvas.ForceUpdateCanvases();
            foreach(var text in overlay.GetComponentsInChildren<TMP_Text>())text.ForceMeshUpdate();
            foreach(var image in overlay.GetComponentsInChildren<UnityEngine.UI.Graphic>())if(image.raycastTarget)throw new Exception("Cinematic effect blocks input");
            Save(camera,target,"cinematic-overlay",new Rect(0,0,1920,1080));
            overlay.Paint(film,0);Save(camera,target,"cinematic-fade",new Rect(0,0,1920,1080));
            camera.targetTexture=null;Object.DestroyImmediate(target);
            result = "PASS: three real editor pages and cinematic letterbox/title/fade overlays rendered; text floor/overflow/scroll and input transparency checked. Native PhysX grazing regression passes. Director/game/time/input live behavior remains unverified.";
        }
        catch (Exception e) { result = "FAIL: " + e; }
        File.WriteAllText("result.txt", result);
        EditorApplication.Exit(result.StartsWith("PASS:") ? 0 : 1);
    }
    private static void Save(Camera camera,RenderTexture target,string name,Rect region)
    {
        camera.Render();RenderTexture.active=target;
        var png=new Texture2D((int)region.width,(int)region.height,TextureFormat.RGB24,false);png.ReadPixels(region,0,0);png.Apply();
        File.WriteAllBytes("renders/"+name+".png",png.EncodeToPNG());RenderTexture.active=null;Object.DestroyImmediate(png);
    }

    private static void CheckSweptClearance()
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        for (int layer = 0; layer < 32; layer++)
            if ((PhysicsLayers.StaticsMask & (1 << layer)) != 0) { wall.layer = layer; break; }
        wall.transform.position = new Vector3(0, 50, 0);
        wall.transform.localScale = new Vector3(.2f, 2, .1f);
        Physics.SyncTransforms();
        if (Physics.Linecast(new Vector3(-2, 50, .2f), new Vector3(2, 50, .2f), PhysicsLayers.StaticsMask))
            throw new Exception("Regression fixture must be clear for the old centerline-only check");
        var guard = new CameraPathClearance();
        if (!guard.Clear(new Vector3(-2, 50, .2f)) || guard.Clear(new Vector3(2, 50, .2f)))
            throw new Exception("Camera radius must hit wall grazed between clear endpoints");
        guard.Reset();
        if (!guard.Clear(new Vector3(-2, 50, 1)) || !guard.Clear(new Vector3(2, 50, 1)))
            throw new Exception("Clear camera corridor incorrectly refused");
        Object.DestroyImmediate(wall);
    }
}
#endif
