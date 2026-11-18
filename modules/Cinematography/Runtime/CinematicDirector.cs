using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Cinematography.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Cinematography.Runtime
{
    /// <summary>
    /// Plays an edit (shots one after another) on the game's own free camera, for the host's spectator view only. It takes the camera from the detached free
    /// camera and gives it back there; a blocked path, a lost actor or Escape ends the edit. Driven by <see cref="Command"/> (CinematicCommands.cs); nomodkit
    /// films the result.
    /// </summary>
    internal sealed partial class CinematicDirector : MonoBehaviour, ISceneService
    {
        private readonly SimulationClockLease slowMotion=new SimulationClockLease();
        private readonly CameraPathClearance clearance = new CameraPathClearance();
        private CameraStateManager camera;
        private CinematicCameraState state;
        private ShotSequence playing;
        private int clipIndex;
        private object missionAtStart;
        private bool pauseWas, ownsPause;
        internal static CinematicDirector Active { get; private set; }
        internal ShotSequence Edit { get; private set; }=new ShotSequence();
        internal string Status { get; private set; } = "IDLE";
        internal string Message { get; private set; } = "";
        internal bool Playing => state != null;
        internal float Elapsed { get; private set; }

        private void Awake() { Active=this; }

        private void Update()
        {
            if (!Playing) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Stop("OPERATOR ABORT");
            else if (!ContextAllowed()) Stop("HOST SPECTATOR CONTEXT LOST");
            else if (camera.currentState != state) Stop("CAMERA OWNERSHIP LOST");
            else if (!slowMotion.StillOwned) Stop("SIMULATION CLOCK OWNERSHIP LOST");
        }

        /// <summary>A running mission, hosted here, with nobody in a cockpit. Slow motion is real, so it is the host's alone.</summary>
        private bool ContextAllowed() => MissionManager.IsRunning && GameAccess.IsServer() &&
            (!GameManager.GetLocalAircraft(out Aircraft ownship) || ownship == null) &&
            (missionAtStart == null || ReferenceEquals(missionAtStart, MissionManager.CurrentMission));

        private static float MissionClock() => NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? float.NaN;

        internal bool Play(out string error)
        {
            if (!ShotSequence.Validate(Edit,out error)) return false;
            var rig = SceneSingleton<CameraStateManager>.i;
            if (rig == null || rig.mainCamera == null || rig.currentState != rig.freeState || rig.followingUnit != null)
            { error = "USE THE DETACHED FREE CAMERA (action detach)"; return false; }
            // A copy: the edit can be rebuilt while this one plays out.
            ShotStore.DecodeSequence(ShotStore.Encode(Edit),out playing,out _);
            clipIndex=0;
            if (StartClip()) return true;
            playing=null;error=Message;return false;
        }

        private bool StartClip()
        {
            ShotPlan clip=playing.clips[clipIndex];
            Func<float> clockSource=clip.options?.realtimeClock==true ? (()=>Time.unscaledTime) : MissionClock;
            if (!ShotPlan.Finite(clockSource())) { Message = "CLOCK UNAVAILABLE"; return false; }
            camera = SceneSingleton<CameraStateManager>.i;
            Unit actor=null;
            if(clip.anchorId!=null)
            {
                actor=FindActor(clip.anchorId);
                if(actor==null) { Message="ACTOR MISSING OR AMBIGUOUS: "+clip.anchorId;return false; }
            }
            Func<ShotPose?> actorPose=()=>ActorPose(actor);
            if(clip.placeAtStart)
            {
                if(!actorPose().HasValue) { Message="ACTOR MISSING OR AMBIGUOUS: "+clip.anchorId;return false; }
                ShotRigs.Place(clip,actorPose().Value);
            }
            // A blocked path is found before the camera moves.
            clearance.Reset();
            for (int i = 0; i <= 64; i++)
            {
                ShotPose pose = clip.SamplePresentation(clip.duration * i / 64,actorPose(),camera.mainCamera.aspect);
                var p = new GlobalPosition(pose.Position.X, pose.Position.Y, pose.Position.Z);
                if (!clearance.Clear(p.ToLocalPosition())) { clearance.Reset(); Message = "CAMERA PATH BLOCKED (clip "+clipIndex+")"; return false; }
            }
            clearance.Reset();
            if(!slowMotion.Acquire(clip.options?.simulationRate ?? 1,GameManager.gameState==GameState.SinglePlayer,out string reason))
            { Message=reason;return false; }
            try
            {
                Elapsed = 0;missionAtStart = MissionManager.CurrentMission;
                pauseWas = GameplayUI.AllowPauseKeybind; GameplayUI.AllowPauseKeybind = false; ownsPause = true;
                state = new CinematicCameraState(camera, clip, clockSource, t => Elapsed = t, ContextAllowed, clearance.Clear,
                    Stop, OnClipComplete,actorPose);
                camera.SwitchState(state);
                if(state==null || camera.currentState!=state) { Stop("CAMERA OWNERSHIP LOST");return false; }
                state.UpdateState(camera);
                if(state==null) return false;
                Status = "PLAYING"; Message = "";return true;
            }
            catch (Exception e) { playing=null;EndClip("FAULTED", "CAMERA START FAILED: " + e.GetType().Name);return false; }
        }

        internal void Stop(string reason = "STOPPED")
        { playing=null;if(Playing) EndClip("ABORTED",reason); }

        private void EndClip(string outcome,string reason)
        {
            var ending = state; state = null;
            try
            {
                if (camera != null && ending!=null && ending.OwnsCamera && camera.currentState == ending)
                    ending.ReturnToFree(camera);
            }
            finally
            {
                slowMotion.Release();
                if (ownsPause && !GameplayUI.AllowPauseKeybind) GameplayUI.AllowPauseKeybind = pauseWas;
                ownsPause = false; clearance.Reset(); missionAtStart = null;
                Status = outcome; Message = reason;
            }
        }

        private void OnClipComplete()
        {
            EndClip("COMPLETED","SHOT FINISHED");
            if(playing==null) return;
            if(++clipIndex>=playing.clips.Length) { playing=null;Message="EDIT FINISHED";return; }
            if(!StartClip()) { playing=null;Status="ABORTED"; }
        }

        internal static Unit FindActor(string id)
        {
            Unit found=null;int count=Math.Min(4096,UnitRegistry.allUnits.Count);
            for(int i=0;i<count;i++) { Unit unit=UnitRegistry.allUnits[i];if(unit==null || unit.NetworkUniqueName!=id) continue;if(found!=null) return null;found=unit; }
            return found;
        }
        internal static ShotPose? ActorPose(Unit unit)
        {
            if(unit==null || !unit.isActiveAndEnabled || unit.disabled) return null;
            GlobalPosition p=unit.transform.position.ToGlobalPosition();Quaternion q=unit.transform.rotation;
            return new ShotPose(new System.Numerics.Vector3(p.x,p.y,p.z),new System.Numerics.Quaternion(q.x,q.y,q.z,q.w),45);
        }

        public void ResetForScene() { Stop("SCENE RESET"); camera = null;Edit=new ShotSequence(); }
        private void OnDisable() { Stop("MODULE DISABLED"); }
        private void OnDestroy() { Stop("MODULE DISABLED");if(Active==this) Active=null; }
    }
}
