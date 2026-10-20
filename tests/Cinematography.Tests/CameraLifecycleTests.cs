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

        // The native camera uses velocity for real-time movement/audio, independently of the shot playhead.
        camera = new CameraStateManager();
        plan.options = new ShotOptions { playbackRate = .5f };
        clock = 0; Time.unscaledTime = 1000;
        state = new CinematicCameraState(camera, plan, () => clock, _ => { }, () => true, _ => true,
            _ => { }, () => { });
        camera.SwitchState(state); state.UpdateState(camera);
        check(camera.cameraVelocity.sqrMagnitude == 0, "first camera placement has no teleport velocity");
        clock = 1; Time.unscaledTime = 1001; state.UpdateState(camera);
        check(Math.Abs(camera.cameraVelocity.x - 25) < .001, "half-rate path reports metres per real second");
        Datum.offset = new Vector3(1000, 0, 0);
        clock = 2; Time.unscaledTime = 1002; state.UpdateState(camera);
        check(Math.Abs(camera.cameraVelocity.x - 25) < .001 && camera.transform.position.x == -850,
            "origin shifts do not create camera velocity spikes");
        state.ReturnToFree(camera); Datum.offset = Vector3.zero;

        var actorPosition = new System.Numerics.Vector3(100, 50, 0);
        var follow = ShotRigs.Follow("velocity-follow", "m", "s", "actor", new System.Numerics.Vector3(0, 10, -20), 4);
        clock = 10; Time.unscaledTime = 2000;
        state = new CinematicCameraState(camera, follow, () => clock, _ => { }, () => true, _ => true,
            _ => { }, () => { }, () => new ShotPose(actorPosition, System.Numerics.Quaternion.Identity, 45));
        camera.SwitchState(state); state.UpdateState(camera); state.ManualPaused = true;
        actorPosition.X = 130; clock = 11; Time.unscaledTime = 2001; state.UpdateState(camera);
        check(Math.Abs(camera.cameraVelocity.x - 30) < .001, "held follow camera reports moving actor velocity");
        check(state.Seek(3), "follow camera accepts explicit scrub");
        actorPosition.X = 160; Time.unscaledTime = 2002; state.UpdateState(camera);
        check(camera.cameraVelocity.sqrMagnitude == 0, "explicit seek never reports teleport velocity");
        actorPosition.X = 170; state.UpdateState(camera);
        check(camera.cameraVelocity.sqrMagnitude == 0, "two evaluations in one real frame have zero velocity");
        actorPosition.X = 190; Time.unscaledTime = 2003; state.UpdateState(camera);
        check(Math.Abs(camera.cameraVelocity.x - 20) < .001, "velocity resumes from the last evaluated pose after seeking");
        state.ReturnToFree(camera);
    }
}
