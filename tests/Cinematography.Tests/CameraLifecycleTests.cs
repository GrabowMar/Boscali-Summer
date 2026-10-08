using BoscaliSummer.Modules.Cinematography.Domain;
using BoscaliSummer.Modules.Cinematography.Runtime;
using UnityEngine;

static class CameraLifecycleTests
{
    internal static void Run(Action<bool, string> check)
    {
        float clock = 20;
        string fault = null;
        bool complete = false;
        var camera = new CameraStateManager();
        camera.transform.position = new Vector3(20, 30, 40);
        camera.mainCamera.fieldOfView = 55; camera.desiredFOV = 60;
        var plan = new ShotPlan { id = "one", mission = "m", scene = "s", duration = 2,
            keys = new[] { new ShotKey { time = 0, x = 100, y = 50, qW = 1, fov = 40 },
                new ShotKey { time = 2, x = 200, y = 50, qW = 1, fov = 60 } } };
        var state = new CinematicCameraState(camera, plan, () => clock, _ => { }, () => true, _ => true,
            reason => fault = reason, () => complete = true);
        camera.SwitchState(state);
        state.UpdateState(camera);
        clock = 21; state.UpdateState(camera);
        check(camera.transform.position.x == 150 && !camera.allowInputs, "native state path/inputs");
        Datum.offset = new Vector3(1000, 0, 0);
        state.UpdateState(camera);
        check(camera.transform.position.x == -850, "global pose across origin shift");
        camera.SwitchState(camera.freeState);
        check(camera.transform.position.x == -980 && camera.mainCamera.fieldOfView == 55 && camera.allowInputs,
            "native takeover releases baseline");
        check(fault == "CAMERA OWNERSHIP LOST", "takeover did not end take");
        Datum.offset = new Vector3(0, 0, 0);
        fault = null;
        clock = 30;
        state = new CinematicCameraState(camera, plan, () => clock, _ => { }, () => true, _ => true,
            reason => fault = reason, () => complete = true);
        camera.SwitchState(state);
        state.RequestStop(); camera.SwitchState(camera.freeState);
        plan.options=new ShotOptions { playbackRate=.5f };
        clock=100;
        state=new CinematicCameraState(camera,plan,()=>clock,_=>{},()=>true,_=>true,_=>{},()=>{});
        camera.SwitchState(state);clock=101;state.UpdateState(camera);
        check(camera.transform.position.x==125,"camera playback speed independent of simulation rate");
        state.ManualPaused=true;clock=102;state.UpdateState(camera);
        check(camera.transform.position.x==125,"editor pause holds only camera");
        check(state.Seek(1.5f) && !state.Seek(float.NaN),"bounded camera scrub");
        state.UpdateState(camera);check(camera.transform.position.x==175,"paused scrub previews pose");
        state.ReturnToFree(camera);plan.options=null;
        clock = 70;
        state = new CinematicCameraState(camera, plan, () => clock, _ => { }, () => true, _ => true,
            _ => { }, () => { });
        camera.SwitchState(state); clock = 71; state.UpdateState(camera);
        Time.timeScale = 0; clock = 79; state.UpdateState(camera);
        check(camera.transform.position.x == 150, "network mission clock may advance during native pause; shot must hold");
        Time.timeScale = 1; clock = 80; state.UpdateState(camera);
        check(camera.transform.position.x == 200, "resume excludes paused network time");
        state.ReturnToFree(camera);
        camera.previousFollowingUnit = new Unit();
        var previous = camera.previousFollowingUnit;
        camera.transform.rotation = Quaternion.identity;
        camera.desiredFOV = 60;
        state = new CinematicCameraState(camera, plan, () => clock, _ => { }, () => true, _ => true,
            _ => { }, () => { });
        camera.SwitchState(state); state.UpdateState(camera);
        state.ReturnToFree(camera);
        check(camera.transform.rotation.w == 1 && camera.previousFollowingUnit == previous && camera.desiredFOV == 60,
            "owned return must preserve baseline despite stale native following history");
        check(fault == null, "normal stop misreported takeover");
        clock = 40;
        state = new CinematicCameraState(camera, plan, () => clock, _ => { }, () => true, _ => true,
            reason => fault = reason, () => complete = true);
        camera.SwitchState(state);
        clock = 39; state.UpdateState(camera);
        check(fault == "MISSION CLOCK CHANGED", "rewind accepted");
        state.RequestStop(); camera.SwitchState(camera.freeState);
        clock = 50; complete = false;
        state = new CinematicCameraState(camera, plan, () => clock, _ => { }, () => true, _ => true,
            reason => fault = reason, () => complete = true);
        camera.SwitchState(state);
        clock = 52; state.UpdateState(camera);
        check(!complete && camera.transform.position.x == 200, "last pose must survive one frame");
        state.UpdateState(camera);
        check(complete, "finite shot completion");
        state.RequestStop(); camera.SwitchState(camera.freeState);
        fault = null;
        state = new CinematicCameraState(camera, plan, () => clock, _ => { }, () => true, _ => false,
            reason => fault = reason, () => { });
        camera.SwitchState(state); state.UpdateState(camera);
        check(fault == "CAMERA PATH BLOCKED", "obstruction not rejected");
        state.RequestStop(); camera.SwitchState(camera.freeState);
    }
}
