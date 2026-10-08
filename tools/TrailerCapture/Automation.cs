using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using BepInEx;
using BoscaliSummer.Cinematics;
using BoscaliSummer.Modules.Cinematography.Domain;
using UnityEngine;
using UnityEngine.UI;
using NVector = System.Numerics.Vector3;

namespace TrailerCapture;

// Separate production tool: never active in an ordinary player session.
[BepInPlugin("com.marci.trailercapture", "Trailer Capture", "0.1.0")]
public sealed class CapturePlugin : BaseUnityPlugin
{
    internal static CapturePlugin Active;
    internal string Folder;
    internal StreamWriter Times;
    internal int Frames;
    internal float Started, Next;
    int width, height;
    FullScreenMode mode;
    readonly List<Canvas> hidden = new();

    void Awake()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"))) { enabled=false;return; }
        Active=this; width=Screen.width;height=Screen.height;mode=Screen.fullScreenMode;
        Screen.SetResolution(1280,720,FullScreenMode.Windowed);
        StartCoroutine(Record());
    }

    IEnumerator Record()
    {
        var end = new WaitForEndOfFrame();
        while (true)
        {
            yield return end;
            if (Times==null || Time.realtimeSinceStartup < Next) continue;
            float t=Time.realtimeSinceStartup;
            Next=t+1f/24;
            Texture2D texture=ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                File.WriteAllBytes(Path.Combine(Folder,$"{Frames:D6}.jpg"), ImageConversion.EncodeToJPG(texture,92));
                Times.WriteLine((t-Started).ToString("R",CultureInfo.InvariantCulture));
                Frames++;
            }
            finally { Destroy(texture); }
        }
    }

    internal void Stop()
    {
        Times?.Dispose();Times=null;
        foreach(var canvas in hidden) if(canvas!=null) canvas.enabled=true;
        hidden.Clear();
    }

    internal void HideUi()
    {
        foreach (var canvas in FindObjectsOfType<Canvas>())
            if (canvas.enabled && !canvas.name.StartsWith("BoscaliSummer.Cinematic"))
            { hidden.Add(canvas);canvas.enabled=false; }
    }

    void OnDestroy() { Stop();Screen.SetResolution(width,height,mode);if(Active==this) Active=null; }
}

public static class Automation
{
    static string Text(Dictionary<string,object> a,string key,string fallback="") => a.TryGetValue(key,out var v)?Convert.ToString(v,CultureInfo.InvariantCulture):fallback;
    static float Number(Dictionary<string,object> a,string key,float fallback) => a.TryGetValue(key,out var v)?Convert.ToSingle(v,CultureInfo.InvariantCulture):fallback;
    static Dictionary<string,object> Reply(bool ok,string error="") => new() { ["ok"]=ok,["error"]=error };
    static string Encode(object o) { using var s=new MemoryStream();new DataContractJsonSerializer(o.GetType()).WriteObject(s,o);return Encoding.UTF8.GetString(s.ToArray()); }

    public static Dictionary<string,object> Step(Dictionary<string,object> a)
    {
        if(CapturePlugin.Active==null || GameManager.gameState!=GameState.SinglePlayer) return Reply(false,"Dedicated single-player sim required");
        var p=CapturePlugin.Active;
        switch(Text(a,"action"))
        {
            case "start":
                if(p.Times!=null) return Reply(false,"Already recording");
                string id=Text(a,"id");if(!ShotPlan.SafeId(id)) return Reply(false,"Invalid take ID");
                string root=Environment.GetEnvironmentVariable("TRAILER_CAPTURE_ROOT");
                if(string.IsNullOrWhiteSpace(root)) return Reply(false,"TRAILER_CAPTURE_ROOT missing");
                p.Folder=Path.Combine(root,id);
                if(Directory.Exists(p.Folder)) return Reply(false,"Existing take preserved; use a fresh ID");
                Directory.CreateDirectory(p.Folder);p.Frames=0;p.Started=p.Next=Time.realtimeSinceStartup;
                p.Times=new StreamWriter(Path.Combine(p.Folder,"timestamps.txt"));
                if(a.TryGetValue("hide_ui",out var hide) && hide is bool b && b) p.HideUi();
                return new() { ["ok"]=true,["folder"]=p.Folder,["width"]=Screen.width,["height"]=Screen.height,["target_capture_hz"]=24 };
            case "stop":
                float duration=Time.realtimeSinceStartup-p.Started;p.Stop();
                return new() { ["ok"]=true,["frames"]=p.Frames,["duration"]=duration,["folder"]=p.Folder };
            case "panel":
                var map=SceneSingleton<DynamicMap>.i;
                if(map==null) return Reply(false,"Map missing");
                if(!DynamicMap.mapMaximized) map.Maximize();
                string label=Text(a,"label");
                foreach(var button in map.maximizedMapCanvas.GetComponentsInChildren<Button>(true))
                {
                    // Existing bezel callback opens the real screen, with its own data and rules.
                    var texts=button.GetComponentsInChildren<TMPro.TMP_Text>(true);
                    foreach(var text in texts) if(text.text.Trim()==label) { button.onClick.Invoke();return Reply(true); }
                }
                return Reply(false,"No installed bezel: "+label);
            case "ui_catalog":
                var labels=new List<string>();
                foreach(var button in UnityEngine.Object.FindObjectsOfType<Button>(true))
                    foreach(var text in button.GetComponentsInChildren<TMPro.TMP_Text>(true))
                        if(!string.IsNullOrWhiteSpace(text.text)) labels.Add(text.text.Trim());
                return new() { ["ok"]=true,["labels"]=labels.ToArray() };
            case "minimize": SceneSingleton<DynamicMap>.i?.Minimize();return Reply(true);
            case "shot": return Shot(a);
            default: return Reply(false,"Unknown capture action");
        }
    }

    static Dictionary<string,object> Shot(Dictionary<string,object> a)
    {
        var status=CinematicAutomation.Step(new() { ["action"]="status" });
        if(!(bool)status["ok"] || (bool)status["playing"]) return Reply(false,"Stopped cinematics required");
        string id=Text(a,"id");float duration=Number(a,"duration",8);
        Unit actor=a.TryGetValue("actorUnit",out var u)?u as Unit:null;
        string actorId=actor!=null?actor.NetworkUniqueName:Text(a,"actor");
        string mission=(string)status["mission"],scene=(string)status["scene"];
        ShotPlan shot;
        if(Text(a,"rig")=="orbit")
        {
            shot=ShotRigs.Orbit(id,mission,scene,new NVector(0,0,0),Number(a,"radius",65),Number(a,"height",18),duration,Number(a,"from",-140),Number(a,"to",-40));
            shot.anchorId=actorId;
        }
        else shot=ShotRigs.Follow(id,mission,scene,actorId,new NVector(Number(a,"x",-35),Number(a,"y",12),Number(a,"z",-50)),duration);
        shot.options.realtimeClock=true;shot.options.letterbox=.08f;shot.options.stabilizeAnchor=false;
        shot.options.title=Text(a,"title");shot.options.simulationRate=Number(a,"simulation_rate",1);
        foreach(var key in shot.keys) key.fov=Number(a,"fov",42);
        if(!ShotPlan.Validate(shot,out var error)) return Reply(false,error);
        var set=CinematicAutomation.Step(new() { ["action"]="set_shot",["json"]=Encode(shot),["expected_revision"]=status["revision"] });
        if(!(bool)set["ok"]) return set;
        var saved=CinematicAutomation.Step(new() { ["action"]="save",["expected_revision"]=set["revision"] });
        if(!(bool)saved["ok"]) return saved;
        return CinematicAutomation.Step(new() { ["action"]="play",["expected_revision"]=saved["revision"] });
    }
}
