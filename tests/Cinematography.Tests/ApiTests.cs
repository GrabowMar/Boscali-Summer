using BoscaliSummer.Cinematics;
using BoscaliSummer.Modules.Cinematography;
using BoscaliSummer.Modules.Cinematography.Configuration;
using BoscaliSummer.Modules.Cinematography.Domain;
using BoscaliSummer.Modules.Cinematography.Runtime;
using BoscaliSummer.Modules.Cinematography.Presentation;
using UnityEngine;

static class ApiTests
{
    static Dictionary<string,object> Call(string action,params (string,object)[] fields)
    {var args=new Dictionary<string,object>{{"action",action}};foreach(var f in fields)args[f.Item1]=f.Item2;return CinematicAutomation.Step(args);}
    static bool Ok(Dictionary<string,object> r)=>(bool)r["ok"];
    internal static void Run(Action<bool,string> check)
    {
        check(Ok(Call("schema")) && !Ok(Call("play")),"discovery available before module activation");
        var settings=new CinematographySettings(new BepInEx.Configuration.ConfigFile());
        settings.Enabled.Value=settings.PrivateProductionSession.Value=true;
        var director=new CinematicDirector();
        NetworkSceneSingleton<MissionManager>.i=new();SceneSingleton<CameraStateManager>.i=new();
        director.Configure(settings,new BepInEx.Logging.ManualLogSource());
        try
        {
            check(!Ok(Call("unknown")),"unknown command rejected");
            check(!Ok(Task.Run(()=>Call("status")).GetAwaiter().GetResult()),"background scripts must dispatch to Unity main thread");
            check(Ok(Call("detach")),"script can detach a native observer view");
            check(!Ok(Call("new",("id","../escape"))),"command path traversal rejected");
            check(Ok(Call("new",("id","apiShot"))),"new named shot");
            check(!Ok(Call("capture",("expected_revision",-1))),"stale command must not append keys");
            check(Ok(Call("capture")) && Ok(Call("save")),"capture/save through public API");
            check(Ok(Call("get")) && !Ok(Call("set_shot",("json","{"))),"invalid replacement rejected");
            check(director.Plan.id=="apiShot","failed replacement preserved current plan");
            GameManager.ownship=new Aircraft();check(!Ok(Call("play")),"pilot cannot start camera takeover");GameManager.ownship=null;
            BoscaliSummer.Core.Game.GameAccess.Server=false;check(!Ok(Call("follow",("actor","x"))),"remote actor access denied");BoscaliSummer.Core.Game.GameAccess.Server=true;
            UnitRegistry.allUnits.Add(new Unit {NetworkUniqueName="actor1"});
            check(Ok(Call("actors")) && Ok(Call("follow",("actor","actor1"))),"bounded actor rig authoring");
            check(Ok(Call("play")) && CinematicOverlay.ActiveCount==1,"actor-relative shot playback");
            check(Ok(Call("pause")) && director.CameraPaused && Time.timeScale==1,"camera pause does not freeze world");
            check(Ok(Call("seek",("seconds",3f))) && !Ok(Call("bookmark",("label","too-early"))),"scrub receipt precedes evaluated frame");
            SceneSingleton<CameraStateManager>.i.currentState.UpdateState(SceneSingleton<CameraStateManager>.i);
            check(Ok(Call("bookmark",("label","inspect"))) && !(bool)Call("status")["seek_pending"],"bookmark attaches to evaluated scrub pose");
            check(Ok(Call("stop")) && CinematicOverlay.ActiveCount==0 && GameplayUI.AllowPauseKeybind,"stop restores overlay/pause key");
            check(Ok(Call("options",("json","{\"simulationRate\":0.25}"))),"validated timing options");
            check(!Ok(Call("play")) && Time.timeScale==1,"slowmo requires independent opt-in");
            settings.AllowSimulationSlowMotion.Value=true;GameManager.gameState=GameState.Multiplayer;
            check(!Ok(Call("play")) && Time.timeScale==1,"multiplayer world slowmo refused");GameManager.gameState=GameState.SinglePlayer;
            check(Ok(Call("play")) && Time.timeScale==.25f,"single-player explicit world slowmo");
            Call("stop");check(Time.timeScale==1 && Time.fixedDeltaTime==.02f,"script stop restores simulation clock");
            check(Ok(Call("options",("json","{}"))) && Ok(Call("append_clip")) && Ok(Call("append_clip")),"two-clip edit");
            check(Ok(Call("save_edit")) && Ok(Call("play_edit")),"edit persistence and playback");Call("stop");
            check(Ok(Call("play_edit")),"restart sequence");
            NetworkSceneSingleton<MissionManager>.i.MissionTime+=10;SceneSingleton<CameraStateManager>.i.currentState.UpdateState(SceneSingleton<CameraStateManager>.i);
            NetworkSceneSingleton<MissionManager>.i.MissionTime+=.01f;SceneSingleton<CameraStateManager>.i.currentState.UpdateState(SceneSingleton<CameraStateManager>.i);
            check(director.Playing && director.Take.clipIndex==1 && CinematicOverlay.ActiveCount==1,"sequence advances without orphaned presentation lease");Call("stop");
            settings.AllowSimulationSlowMotion.Value=true;Call("options",("json","{\"simulationRate\":0.5}"));Call("play");
            settings.AllowSimulationSlowMotion.Value=false;
            SceneSingleton<CameraStateManager>.i.currentState.UpdateState(SceneSingleton<CameraStateManager>.i);
            check(!director.Playing && Time.timeScale==1,"revoking slowmo permission releases simulation clock");
            check(!Ok(Call("seek",("seconds",0))),"seek cannot pretend to rewind world without camera lease");
            Call("new",("id","shot01"));Call("capture");Call("save");Call("new",("id","other"));
            int beforeLoad=director.Revision;director.Load();
            check(director.Revision>beforeLoad && !Ok(Call("options",("json","{}"),("expected_revision",beforeLoad))),"console load invalidates stale automation revision");
        }
        finally { director.ResetForScene();if(Directory.Exists(BepInEx.Paths.ConfigPath))Directory.Delete(BepInEx.Paths.ConfigPath,true);UnitRegistry.allUnits.Clear(); }
    }
}
