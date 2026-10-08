using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Cinematography.Presentation;
using BoscaliSummer.Modules.Cinematography.Runtime;
using UnityEngine;

namespace BoscaliSummer.Cinematics
{
    // Live test inspection only, not a gameplay/editor command. All measurements come from the native process.
    public static class CinematicTestAutomation
    {
        private static float baselineFixed,baselineScale;
        private static bool baselineInputs,baselinePause;
        public static Dictionary<string,object> Step(Dictionary<string,object> args)
        {
            if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState!=GameState.SinglePlayer || !GameAccess.IsServer())
                return new Dictionary<string,object>{["ok"]=false,["error"]="Requires hosted nomodkit single-player test"};
            var reply=CinematicAutomation.Step(new Dictionary<string,object>{{"action","status"}});
            var camera=SceneSingleton<CameraStateManager>.i;
            if(args!=null && args.TryGetValue("action",out var action) && action as string=="baseline")
            {baselineFixed=Time.fixedDeltaTime;baselineScale=Time.timeScale;baselineInputs=camera?.allowInputs ?? false;baselinePause=GameplayUI.AllowPauseKeybind;}
            reply["fixed_delta_actual"]=Time.fixedDeltaTime;
            reply["fixed_delta_ratio"]=baselineFixed>0 ? Time.fixedDeltaTime/baselineFixed : 0;
            reply["clock_restored"]=baselineFixed>0 && Time.fixedDeltaTime==baselineFixed && Time.timeScale==baselineScale;
            reply["inputs_restored"]=camera!=null && camera.allowInputs==baselineInputs && GameplayUI.AllowPauseKeybind==baselinePause;
            reply["native_free"]=camera!=null && camera.currentState==camera.freeState;
            reply["native_inputs"]=camera!=null && camera.allowInputs;
            reply["pause_key_allowed"]=GameplayUI.AllowPauseKeybind;
            reply["effect_roots"]=UnityEngine.Object.FindObjectsOfType<CinematicOverlay>().Length;
            if(Rewired.ReInput.isReady && Rewired.ReInput.controllers!=null)
            {
                reply["mouse_enabled"]=Rewired.ReInput.controllers.Mouse?.enabled ?? false;
                reply["keyboard_enabled"]=Rewired.ReInput.controllers.Keyboard?.enabled ?? false;
            }
            return reply;
        }
    }
}
