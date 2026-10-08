using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Cinematography.Configuration;
using BoscaliSummer.Modules.Cinematography.Domain;
using BoscaliSummer.Modules.Cinematography.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using NuclearOption.MissionEditorScripts;

namespace BoscaliSummer.Modules.Cinematography.Runtime
{
    internal sealed partial class CinematicDirector : MonoBehaviour, ISceneService
    {
        private CinematographySettings settings;
        private ManualLogSource logger;
        private ShotStore store;
        private CinematicConsole console;
        private CameraStateManager camera;
        private CinematicCameraState state;
        private CinematicOverlay overlay;
        private readonly SimulationClockLease slowMotion=new SimulationClockLease();
        private ShotSequence playingEdit;
        private int clipIndex;
        private int mainThreadId;
        private CinematicInbox inbox;
        private float nextInboxPoll;
        private float? pendingSeek;
        internal static CinematicDirector Active { get; private set; }
        internal ShotSequence Edit { get; private set; }=new ShotSequence();
        internal int Revision { get; private set; }
        internal bool CameraPaused => state?.ManualPaused ?? false;
        private object missionAtStart;
        private readonly CameraPathClearance clearance = new CameraPathClearance();
        private bool pauseWas, ownsPause;
        private int slot = 1;
        internal ShotPlan Plan { get; private set; }
        internal TakeLog Take { get; private set; }
        internal string Status { get; private set; } = "IDLE";
        internal string Message { get; private set; } = "Enable a private host production session; use native free spectator camera.";
        internal bool Playing => state != null;
        internal float Elapsed { get; private set; }

        internal void Configure(CinematographySettings configuration, ManualLogSource log)
        {
            settings = configuration; logger = log;
            Active=this;
            mainThreadId=System.Threading.Thread.CurrentThread.ManagedThreadId;
            store = new ShotStore(Path.Combine(Paths.ConfigPath, "BoscaliSummer", "Cinematics"));
            inbox=new CinematicInbox(Path.Combine(Paths.ConfigPath,"BoscaliSummer","Cinematics"),args=>BoscaliSummer.Cinematics.CinematicAutomation.Step(args));
            NewShot();
        }

        private void Update()
        {
            if (settings == null) return;
            if(settings.EnableScriptInbox.Value && Time.unscaledTime>=nextInboxPoll)
            {nextInboxPoll=Time.unscaledTime+.2f;inbox.Pump();if(inbox.LastError!=null) Message=inbox.LastError;}
            if (Input.GetKeyDown(KeyCode.Escape) && Playing) { Stop("Aborted", "OPERATOR ABORT"); return; }
            if (Playing && !ContextAllowed()) { Stop("Aborted", "PRODUCTION CONTEXT LOST"); return; }
            if (Playing && camera.currentState != state) { Stop("Aborted", "CAMERA OWNERSHIP LOST"); return; }
            if (Playing && !slowMotion.StillOwned) { Stop("Aborted","SIMULATION CLOCK OWNERSHIP LOST");return; }
            if(Playing && !PlaybackAllowed()) { Stop("Aborted","SLOWMO PERMISSION LOST");return; }
            if (Playing) Status = Time.timeScale == 0 || CameraPaused ? "HOLDING" : "PLAYING";
            if (InputFieldChecker.InsideInputField) return;
            if (settings.ConsoleKey.Value.IsDown())
            {
                if (console == null) console = CinematicConsole.Create(this);
                if (console.IsOpen) console.Close();
                else if (CanUseConsole(out string reason)) console.Show();
                else Message = reason;
            }
            if (console != null && console.IsOpen) return;
            if (settings.CaptureKey.Value.IsDown()) Capture();
            if (settings.PlayKey.Value.IsDown()) Play();
            if (settings.BookmarkKey.Value.IsDown()) Bookmark();
        }

        internal bool CanEdit(out string reason)
        {
            reason = null;
            if (!ContextAllowed()) reason = "PRIVATE HOST SPECTATOR REQUIRED";
            else if (Playing) reason = "STOP PLAYBACK FIRST";
            else
            {
                var rig = SceneSingleton<CameraStateManager>.i;
                if (rig == null || rig.mainCamera == null || rig.currentState != rig.freeState || rig.followingUnit != null)
                    reason = "USE DETACHED NATIVE FREE CAMERA";
            }
            return reason == null;
        }
        internal bool CanUseConsole(out string reason)
        {
            if(Playing) { reason=ContextAllowed()?null:"PRIVATE HOST SPECTATOR REQUIRED";return reason==null; }
            return CanEdit(out reason);
        }

        private bool ContextAllowed() => settings != null && settings.Enabled.Value && settings.PrivateProductionSession.Value &&
            !Application.isBatchMode && Application.isFocused && MissionManager.IsRunning && GameAccess.IsServer() &&
            (!GameManager.GetLocalAircraft(out Aircraft ownship) || ownship == null) &&
            (missionAtStart == null || ReferenceEquals(missionAtStart, MissionManager.CurrentMission));
        private bool PlaybackAllowed() => ContextAllowed() && slowMotion.StillOwned &&
            ((Plan.options?.simulationRate ?? 1)==1 || settings.AllowSimulationSlowMotion.Value && GameManager.gameState==GameState.SinglePlayer);

        private static string MissionIdentity => MissionManager.CurrentMission?.Name ?? "";
        private static string SceneIdentity => SceneManager.GetActiveScene().name;
        private static float MissionClock() => NetworkSceneSingleton<MissionManager>.i?.MissionTime ?? float.NaN;

        internal void NewShot()
        {
            if (Playing) return;
            Plan = new ShotPlan { id = "shot" + slot.ToString("00"), mission = MissionIdentity, scene = SceneIdentity };
            Status = "IDLE"; Message = "Capture free-camera poses; one key holds, more keys travel.";
            Revision++;
        }

        internal void NextSlot()
        { if (!Playing) { slot = slot % 8 + 1; NewShot(); } }

        internal void Capture()
        {
            if (!CanEdit(out string reason)) { Message = reason; return; }
            if(Plan.anchorId!=null) { Message="Actor-relative offsets are authored through the API; NEW starts a global shot.";return; }
            if (Plan.keys.Length >= ShotPlan.KeyLimit) { Message = "64 KEY LIMIT"; return; }
            var rig = SceneSingleton<CameraStateManager>.i;
            rig.GetCameraPosition(out GlobalPosition position, out Quaternion rotation);
            float fov = rig.mainCamera.fieldOfView;
            if (fov < 15 || fov > 80) { Message = "SET NATIVE VERTICAL FOV TO 15–80 DEGREES"; return; }
            if (Plan.keys.Length == 0) { Plan.mission = MissionIdentity; Plan.scene = SceneIdentity; }
            if (Plan.mission != MissionIdentity || Plan.scene != SceneIdentity) { Message = "SHOT BELONGS TO ANOTHER MISSION"; return; }
            var next = new ShotKey[Plan.keys.Length + 1];
            Plan.keys.CopyTo(next, 0);
            next[next.Length - 1] = new ShotKey { x = position.x, y = position.y, z = position.z,
                qX = rotation.x, qY = rotation.y, qZ = rotation.z, qW = rotation.w, fov = fov };
            Plan.keys = next; Retime();Revision++; Message = "POSE CAPTURED · " + next.Length + " KEYS";
        }

        internal void RemoveLast()
        {
            if (Playing || Plan.keys.Length == 0) return;
            var next = new ShotKey[Plan.keys.Length - 1]; Array.Copy(Plan.keys, next, next.Length);
            Plan.keys = next; Retime();Revision++; Message = "LAST POSE REMOVED";
        }

        internal void AdjustDuration(float seconds)
        {
            if (!Playing && ShotPlan.Finite(seconds))
            {
                Plan.duration = Mathf.Clamp(Plan.duration + seconds, .5f, 120);Retime();
                if(Plan.options!=null) { Plan.options.fadeIn=Mathf.Min(Plan.options.fadeIn,Plan.duration/2);Plan.options.fadeOut=Mathf.Min(Plan.options.fadeOut,Plan.duration/2); }
                Revision++;
            }
        }
        internal void ToggleSmooth() { if (!Playing) Plan.smooth = !Plan.smooth; }
        private void Retime()
        { for (int i = 0; i < Plan.keys.Length; i++) Plan.keys[i].time = Plan.keys.Length == 1 ? 0 : Plan.duration * i / (Plan.keys.Length - 1); }

        internal void Save()
        { if (!Playing) Message = store.Save(Plan, out string error) ? "SHOT SAVED" : error; }
        internal void Load()
        {
            if (Playing) return;
            if (!store.Load("shot" + slot.ToString("00"), out ShotPlan loaded, out string error)) { Message = error; return; }
            if (loaded.mission != MissionIdentity || loaded.scene != SceneIdentity) { Message = "SHOT MISSION/SCENE MISMATCH"; return; }
            if(!SetShot(loaded,out error)) { Message=error;return; }
            Message = "SHOT LOADED";
        }

        internal void Play()
        {
            PlayCurrent();
        }
        private bool PlayCurrent()
        {
            if (!CanEdit(out string reason)) { Message = reason; return false; }
            if (!ShotPlan.Validate(Plan, out reason)) { Message = reason; return false; }
            if (Plan.mission != MissionIdentity || Plan.scene != SceneIdentity) { Message = "SHOT MISSION/SCENE MISMATCH"; return false; }
            Func<float> clockSource=Plan.options?.realtimeClock==true ? (()=>Time.unscaledTime) : MissionClock;
            if (!ShotPlan.Finite(clockSource())) { Message = "CLOCK UNAVAILABLE"; return false; }
            camera = SceneSingleton<CameraStateManager>.i;
            Unit actor=null;
            if(Plan.anchorId!=null)
            {
                actor=FindActor(Plan.anchorId);
                if(actor==null) { Message="ACTOR MISSING OR AMBIGUOUS";return false; }
            }
            Func<ShotPose?> actorPose=()=>ActorPose(actor);
            // Bounded preflight catches blocked endpoints and intermediate samples before taking ownership.
            clearance.Reset();
            for (int i = 0; i <= 64; i++)
            {
                ShotPose pose = Plan.SamplePresentation(Plan.duration * i / 64,actorPose());
                var p = new GlobalPosition(pose.Position.X, pose.Position.Y, pose.Position.Z);
                if (!PathClear(p.ToLocalPosition())) { clearance.Reset(); Message = "CAMERA PATH BLOCKED"; return false; }
            }
            clearance.Reset();
            string takeId = "take-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            if(!ShotStore.DecodeShot(ShotStore.Encode(Plan),out ShotPlan snapshot,out reason)) { Message=reason;return false; }
            console?.Close();
            bool allowSlow=settings.AllowSimulationSlowMotion.Value && GameManager.gameState==GameState.SinglePlayer && ContextAllowed();
            if(!slowMotion.Acquire(Plan.options?.simulationRate ?? 1,allowSlow,out reason))
            { Message=reason;return false; }
            try
            {
                Take = new TakeLog(takeId, snapshot, Plugin.PluginVersion + "/" + typeof(CinematicDirector).Assembly.ManifestModule.ModuleVersionId)
                { width = Screen.width, height = Screen.height,sequenceId=playingEdit?.id,clipIndex=clipIndex };
                Elapsed = 0;missionAtStart = MissionManager.CurrentMission;
                pauseWas = GameplayUI.AllowPauseKeybind; GameplayUI.AllowPauseKeybind = false; ownsPause = true;
                state = new CinematicCameraState(camera, snapshot, clockSource, OnFrame, PlaybackAllowed, PathClear,
                    why => Stop("Aborted", why), OnClipComplete,actorPose);
                overlay=CinematicOverlay.Create(transform);
                camera.SwitchState(state);
                if(state==null || camera.currentState!=state) { Stop("Aborted","CAMERA OWNERSHIP LOST");return false; }
                state.UpdateState(camera);Revision++;
                if(state==null) return false;
                Status = "PLAYING"; Message = "RECORDER UNKNOWN · ESC ABORTS";return true;
            }
            catch (Exception e) { playingEdit=null;EndClip("Faulted", "CAMERA START FAILED: " + e.GetType().Name);return false; }
        }

        private bool PathClear(Vector3 position) => clearance.Clear(position);

        private void OnFrame(float elapsed)
        { Elapsed = elapsed;pendingSeek=null; Take.evaluatedFrames++;overlay?.Paint(Take.plan,elapsed); }
        internal void Bookmark()
        { Message = Playing && !pendingSeek.HasValue && Take.Mark("mark-" + (Take.BookmarkCount + 1), Elapsed) ? "OPERATOR BOOKMARK ADDED" : "NO EVALUATED TAKE, PENDING SEEK OR BOOKMARK LIMIT"; }

        internal void Stop(string outcome = "Aborted", string reason = "OPERATOR STOP")
        { playingEdit=null;console?.Close();if(Playing) EndClip(outcome,reason); }
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
                slowMotion.Release();overlay?.Release();overlay=null;
                if (ownsPause && !GameplayUI.AllowPauseKeybind) GameplayUI.AllowPauseKeybind = pauseWas;
                ownsPause = false; clearance.Reset(); missionAtStart = null;pendingSeek=null;
                if(Take!=null && !Take.IsFinished) Take.Finish(outcome, reason, Elapsed);
                Status = outcome.ToUpperInvariant(); Message = reason;
                Revision++;
                if (Take!=null && !store.SaveTake(Take, out string error)) { Message = "TAKE SAVE FAILED: " + error; logger.LogWarning(Message); }
            }
        }
        private void OnClipComplete()
        {
            EndClip("Completed","SHOT FINISHED");
            if(playingEdit==null) return;
            clipIndex++;
            if(clipIndex>=playingEdit.clips.Length) { playingEdit=null;Message="EDIT FINISHED";return; }
            Plan=playingEdit.clips[clipIndex];
            if(!PlayCurrent()) { playingEdit=null;Status="ABORTED"; }
        }
        internal bool PauseCamera(bool paused)
        {
            if(!Playing || !Take.CanRecordEdits) { Message="NO ACTIVE TAKE OR TAKE EDIT LIMIT";return false; }
            state.ManualPaused=paused;Take.RecordEdit(paused?"pause":"resume",Elapsed,"{}");Revision++;
            Status=paused || Time.timeScale==0 ? "HOLDING":"PLAYING";
            Message=paused ? "CAMERA TIMELINE PAUSED":"CAMERA TIMELINE RESUMED";
            if(!paused) console?.Close();return true;
        }
        internal bool Seek(float seconds)
        {
            if(!Playing || !Take.CanRecordEdits || !state.Seek(seconds)) { Message="Seek requires active take, valid shot time and free edit record";return false; }
            pendingSeek=seconds;Take.RecordEdit("seek",seconds,"{\"world_rewound\":false}");Revision++;return true;
        }
        internal bool SetShot(ShotPlan plan,out string error)
        {
            if(!CanEdit(out error)) return false;
            if(!ShotPlan.Validate(plan,out error)) return false;
            if(plan.mission!=MissionIdentity || plan.scene!=SceneIdentity) { error="Shot mission/scene mismatch";return false; }
            Plan=plan;Revision++;return true;
        }
        internal bool SetEdit(ShotSequence edit,out string error)
        {
            if(!CanEdit(out error) || !ShotSequence.Validate(edit,out error)) return false;
            if(edit.clips[0].mission!=MissionIdentity || edit.clips[0].scene!=SceneIdentity) { error="Edit mission/scene mismatch";return false; }
            Edit=edit;Revision++;return true;
        }
        internal bool PlayEdit()
        {
            if(!CanEdit(out string reason) || !ShotSequence.Validate(Edit,out reason)) { Message=reason;return false; }
            ShotStore.DecodeSequence(ShotStore.Encode(Edit),out playingEdit,out _);clipIndex=0;Plan=playingEdit.clips[0];
            if(PlayCurrent()) return true;playingEdit=null;return false;
        }
        internal void AppendClip()
        {
            if(Playing || Edit.clips.Length>=ShotSequence.ClipLimit || !ShotPlan.Validate(Plan,out string _)) { Message="STOP, VALID SHOT AND FREE CLIP SLOT REQUIRED";return; }
            ShotStore.DecodeShot(ShotStore.Encode(Plan),out var copy,out _);
            var clips=new ShotPlan[Edit.clips.Length+1];Edit.clips.CopyTo(clips,0);clips[clips.Length-1]=copy;
            var edit=new ShotSequence { id=Edit.id,clips=clips };
            if(SetEdit(edit,out string error)) Message="CLIP ADDED";else Message=error;
        }
        internal void ClearEdit() { if(!Playing) { Edit=new ShotSequence();Revision++; } }
        internal void SaveEdit() { if(!Playing) Message=store.SaveSequence(Edit,out string error)?"EDIT SAVED":error; }
        internal void LoadEdit()
        { if(!Playing) Message=store.LoadSequence(Edit.id,out var edit,out string error) && SetEdit(edit,out error)?"EDIT LOADED":error; }
        internal void CyclePath() { if(!Playing) { if(Plan.spline) { Plan.spline=false;Plan.smooth=false; }else if(Plan.smooth) { Plan.smooth=false;Plan.spline=true; }else Plan.smooth=true;Revision++; } }
        internal void SetPreset(string name)
        {
            if(Playing) { Message="STOP TO CHANGE EFFECTS";return; }
            var options=Plan.options ?? new ShotOptions();
            options.shakeMeters=options.shakeDegrees=options.rollDegrees=options.letterbox=options.fadeIn=options.fadeOut=0;
            options.title="";
            if(name=="film") { options.letterbox=.1f;options.fadeIn=options.fadeOut=Mathf.Min(1,Plan.duration/2); }
            if(name=="handheld") { options.shakeMeters=.12f;options.shakeDegrees=.6f; }
            Plan.options=options;Revision++;
        }
        internal void SetCameraRate(float rate)
        { if(!Playing && ShotPlan.Finite(rate) && rate>=.05f && rate<=4 && Plan.duration/rate<=600) { Plan.options??=new ShotOptions();Plan.options.playbackRate=rate;Revision++; } }
        internal void SetSimulationRate(float rate)
        { if(!Playing && ShotPlan.Finite(rate) && rate>=.05f && rate<=1) { Plan.options??=new ShotOptions();Plan.options.simulationRate=rate;Revision++; } }
        internal void EditKey(int index,float timeDelta,float fovDelta)
        {
            if(Playing || index<0 || index>=Plan.keys.Length) return;
            var key=Plan.keys[index];
            if(timeDelta!=0 && index>0 && index<Plan.keys.Length-1)
                key.time=Mathf.Clamp(key.time+timeDelta,Plan.keys[index-1].time+.001f,Plan.keys[index+1].time-.001f);
            key.fov=Mathf.Clamp(key.fov+fovDelta,15,80);Revision++;
        }
        internal void MoveClip(int index,int delta)
        {
            if(Playing || index<0 || index>=Edit.clips.Length || index+delta<0 || index+delta>=Edit.clips.Length) return;
            var other=Edit.clips[index+delta];Edit.clips[index+delta]=Edit.clips[index];Edit.clips[index]=other;Revision++;
        }
        internal void RemoveClip(int index)
        {
            if(Playing || index<0 || index>=Edit.clips.Length) return;
            var clips=new ShotPlan[Edit.clips.Length-1];Array.Copy(Edit.clips,0,clips,0,index);
            Array.Copy(Edit.clips,index+1,clips,index,clips.Length-index);Edit.clips=clips;Revision++;
        }
        internal void ToggleOption(string option)
        {
            if(Playing) return;Plan.options??=new ShotOptions();var o=Plan.options;
            switch(option)
            {
                case "clock": o.realtimeClock=!o.realtimeClock;break;
                case "bars": o.letterbox=o.letterbox>0?0:.1f;break;
                case "fade": o.fadeIn=o.fadeOut=o.fadeIn>0?0:Mathf.Min(1,Plan.duration/2);break;
                case "title": o.title=string.IsNullOrEmpty(o.title)?"BOSCALI SUMMER":"";break;
                case "dolly": if(o.lookAtTarget) o.dollyZoom=!o.dollyZoom;break;
                case "scripts": settings.EnableScriptInbox.Value=!settings.EnableScriptInbox.Value;Message=settings.EnableScriptInbox.Value?"SCRIPT INBOX ENABLED":"SCRIPT INBOX DISABLED";break;
                case "rollLeft": o.rollDegrees=Mathf.Clamp(o.rollDegrees-5,-20,20);break;
                case "rollRight": o.rollDegrees=Mathf.Clamp(o.rollDegrees+5,-20,20);break;
                case "anchor": o.stabilizeAnchor=!o.stabilizeAnchor;break;
            }
            Revision++;
        }
        private static Unit FindActor(string id)
        {
            Unit found=null;int count=Math.Min(4096,UnitRegistry.allUnits.Count);
            for(int i=0;i<count;i++) { Unit unit=UnitRegistry.allUnits[i];if(unit==null || unit.NetworkUniqueName!=id) continue;if(found!=null) return null;found=unit; }
            return found;
        }
        private static ShotPose? ActorPose(Unit unit)
        {
            if(unit==null || !unit.isActiveAndEnabled || unit.disabled) return null;
            GlobalPosition p=unit.transform.position.ToGlobalPosition();Quaternion q=unit.transform.rotation;
            return new ShotPose(new System.Numerics.Vector3(p.x,p.y,p.z),new System.Numerics.Quaternion(q.x,q.y,q.z,q.w),45);
        }

        private void OnApplicationFocus(bool focused)
        { if (!focused) { Stop("Aborted", "FOCUS LOST"); console?.Close(); } }
        public void ResetForScene()
        { Stop("Aborted", "SCENE RESET"); if (console != null) Destroy(console.gameObject); console = null; camera = null;Edit=new ShotSequence(); NewShot(); }
        private void OnDisable() { Stop("Aborted", "MODULE DISABLED"); console?.Close(); }
        private void OnDestroy() { ResetForScene();if(Active==this) Active=null; }
    }
}
