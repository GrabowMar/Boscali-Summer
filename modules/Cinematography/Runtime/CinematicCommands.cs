using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Cinematography.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;
using Vec3 = System.Numerics.Vector3;

namespace BoscaliSummer.Modules.Cinematography.Runtime
{
    internal sealed partial class CinematicDirector
    {
        private static readonly string[] Numbers={ "x","y","z","to_x","to_y","to_z","look_x","look_y","look_z","radius","height","duration","from","to","fov","shake","roll","slowmo" };

        private static Dictionary<string,object> Schema() => new Dictionary<string,object>
        {
            ["ok"]=true,
            ["use"]="Rig actions append one clip to the edit; play runs the edit on the detached free camera; film it with nomodkit's film op.",
            ["actions"]=new Dictionary<string,string>
            {
                ["status"]="playing, playhead, clip_index, edit_clips, message",
                ["actors"]="units with their ids and global positions (first 64)",
                ["pose"]="the free camera's global pose and FOV, for copying into a shot",
                ["detach"]="let go of the unit the spectator camera follows",
                ["clear"]="empty the edit",
                ["orbit"]="x,y,z | airbase | actor; radius,height,from,to (compass degrees)",
                ["follow"]="actor; x,y,z = metres right, up, ahead of it (default -20,8,-30); rolls with the actor",
                ["chase"]="follow with a level horizon",
                ["track"]="actor; stay at the free camera's pose as of this command and pan with it",
                ["flyby"]="actor; x,y,z = where to stand in its heading frame when the clip starts (default -80,10,300), then pan as it passes",
                ["fly"]="x,y,z to to_x,to_y,to_z, looking at look_x,look_y,look_z",
                ["set_edit"]="json = {\"clips\":[ShotPlan...]}: replaces the edit; every key and option by hand",
                ["play"]="play the edit from its first clip",
                ["stop"]="stop and give the camera back",
            },
            ["rig_options"]="duration 0.5-120 s (default 10), fov 15-80, shake 0-1, roll -20..20 degrees, slowmo 0.05-1 (real time scale; single player only)",
            ["shot_plan"]="duration, smooth, spline, anchorId, keys[{time,x,y,z,qX,qY,qZ,qW,fov}], options{playbackRate,simulationRate,realtimeClock,lookAtTarget," +
                "targetX/Y/Z,dollyZoom,referenceDistance,stabilizeAnchor,headingOnlyAnchor,worldSpacePath,frameX,frameY,shakeMeters,shakeDegrees,rollDegrees,seed}",
            ["positions"]="global sea-level metres; with an actor (anchorId) keys are metres from it",
        };

        internal Dictionary<string,object> Command(Dictionary<string,object> args)
        {
            string action=Text(args,"action");
            if(action=="schema") return Schema();
            foreach(string field in Numbers)
                if(Has(args,field) && (Arg(args,field) is bool || !ShotPlan.Finite(Number(args,field,float.NaN))))
                    return Reply(false,action,"INVALID NUMBER: "+field);
            if(action=="status") return Reply(true,action,null);
            if(action=="stop") { Stop();return Reply(true,action,null); }
            if(!ContextAllowed()) return Reply(false,action,"HOST SPECTATOR REQUIRED: a running mission you host, with no aircraft of your own");
            var rig=SceneSingleton<CameraStateManager>.i;
            if(rig==null || rig.mainCamera==null) return Reply(false,action,"CAMERA UNAVAILABLE");
            if(action=="pose")
            {
                rig.GetCameraPosition(out GlobalPosition p,out Quaternion q);
                var r=Reply(true,action,null);r["x"]=p.x;r["y"]=p.y;r["z"]=p.z;
                r["qX"]=q.x;r["qY"]=q.y;r["qZ"]=q.z;r["qW"]=q.w;r["fov"]=rig.mainCamera.fieldOfView;return r;
            }
            if(action=="actors")
            {
                var actors=new List<object>(64);int inspected=Math.Min(4096,UnitRegistry.allUnits.Count);
                for(int i=0;i<inspected && actors.Count<64;i++)
                {
                    Unit unit=UnitRegistry.allUnits[i];if(unit==null || unit.disabled || string.IsNullOrEmpty(unit.NetworkUniqueName)) continue;
                    GlobalPosition p=unit.transform.position.ToGlobalPosition();
                    actors.Add(new Dictionary<string,object> { ["id"]=unit.NetworkUniqueName,["type"]=unit.definition?.unitName ?? "UNIT",["x"]=p.x,["y"]=p.y,["z"]=p.z });
                }
                var r=Reply(true,action,null);r["actors"]=actors.ToArray();return r;
            }
            if(Playing) return Reply(false,action,"STOP PLAYBACK FIRST");
            string error=null;bool ok;
            switch(action)
            {
                case "detach":
                    ok=rig.currentState!=null && rig.currentState.GetType().Assembly==typeof(CameraBaseState).Assembly;
                    if(ok) rig.SetFollowingUnit(null);else error="THE GAME'S OWN SPECTATOR CAMERA REQUIRED";
                    break;
                case "clear": Edit=new ShotSequence();ok=true;break;
                case "set_edit":
                    ok=ShotStore.DecodeSequence(Text(args,"json"),out var edit,out error);
                    if(ok) Edit=edit;
                    break;
                case "play": ok=Play(out error);break;
                default: ok=AddRig(action,args,rig,out error);break;
            }
            return Reply(ok,action,error);
        }

        /// <summary>Builds one ready-made move and appends it to the edit.</summary>
        private bool AddRig(string action,Dictionary<string,object> args,CameraStateManager rig,out string error)
        {
            error=null;
            float duration=Number(args,"duration",10);
            string actor=Arg(args,"actorUnit") is Unit named && named!=null ? named.NetworkUniqueName : Text(args,"actor");
            ShotPlan shot;
            switch(action)
            {
                case "orbit":
                    var centre=new Vec3(Number(args,"x",0),Number(args,"y",0),Number(args,"z",0));
                    string airbase=Text(args,"airbase");
                    if(airbase!=null)
                    {
                        if(!FactionRegistry.airbaseLookup.TryGetValue(airbase,out Airbase field) || field==null || field.center==null)
                        { error="AIRBASE NOT FOUND: "+airbase;return false; }
                        GlobalPosition at=field.center.position.ToGlobalPosition();centre=new Vec3(at.x,at.y,at.z);
                    }
                    float radius=Number(args,"radius",50),height=Number(args,"height",10);
                    if(radius<1 || radius>100000 || Math.Abs(height)>50000) { error="Orbit radius 1-100000, height within 50000";return false; }
                    shot=ShotRigs.Orbit(centre,radius,height,duration,Number(args,"from",0),Number(args,"to",180));
                    shot.anchorId=actor;
                    // The orbit keeps its compass bearings while the unit turns under it.
                    shot.options.stabilizeAnchor=actor!=null;
                    break;
                case "follow":
                case "chase":
                    if(string.IsNullOrWhiteSpace(actor)) { error="actor required";return false; }
                    shot=ShotRigs.Follow(actor,new Vec3(Number(args,"x",-20),Number(args,"y",8),Number(args,"z",-30)),duration);
                    shot.options.headingOnlyAnchor=action=="chase";
                    break;
                case "track":
                case "flyby":
                    ShotPose? subject=actor==null ? null : ActorPose(FindActor(actor));
                    if(!subject.HasValue) { error="ACTOR MISSING, DISABLED OR AMBIGUOUS";return false; }
                    if(action=="track")
                    {
                        rig.GetCameraPosition(out GlobalPosition p,out Quaternion q);
                        shot=ShotRigs.Track(actor,new ShotPose(new Vec3(p.x,p.y,p.z),new System.Numerics.Quaternion(q.x,q.y,q.z,q.w),
                            Mathf.Clamp(rig.mainCamera.fieldOfView,15,80)),duration);
                    }
                    else shot=ShotRigs.Flyby(actor,new Vec3(Number(args,"x",-80),Number(args,"y",10),Number(args,"z",300)),duration);
                    break;
                case "fly":
                    foreach(string field in new[] {"x","y","z","to_x","to_y","to_z","look_x","look_y","look_z"})
                        if(!Has(args,field)) { error="fly needs "+field;return false; }
                    shot=ShotRigs.Fly(new Vec3(Number(args,"x",0),Number(args,"y",0),Number(args,"z",0)),
                        new Vec3(Number(args,"to_x",0),Number(args,"to_y",0),Number(args,"to_z",0)),
                        new Vec3(Number(args,"look_x",0),Number(args,"look_y",0),Number(args,"look_z",0)),duration);
                    break;
                default: error="Unknown action; call schema";return false;
            }
            if(Has(args,"fov")) foreach(ShotKey key in shot.keys) key.fov=Number(args,"fov",45);
            float shake=Mathf.Clamp01(Number(args,"shake",0));
            shot.options.shakeMeters=.75f*shake;shot.options.shakeDegrees=3*shake;
            shot.options.rollDegrees=Number(args,"roll",0);
            shot.options.simulationRate=Number(args,"slowmo",1);
            shot.options.seed=Edit.clips.Length;
            if(Edit.clips.Length>=ShotSequence.ClipLimit) { error="An edit holds at most 32 clips";return false; }
            if(!ShotPlan.Validate(shot,out error)) return false;
            var clips=new ShotPlan[Edit.clips.Length+1];Edit.clips.CopyTo(clips,0);clips[clips.Length-1]=shot;
            Edit=new ShotSequence { clips=clips };
            return true;
        }

        private Dictionary<string,object> Reply(bool ok,string action,string error) => new Dictionary<string,object>
        {
            ["ok"]=ok,["action"]=action ?? "",["error"]=error ?? "",["status"]=Status,["message"]=Message,
            ["playing"]=Playing,["playhead"]=Elapsed,["clip_index"]=clipIndex,["edit_clips"]=Edit.clips.Length,
            ["simulation_rate_actual"]=Time.timeScale
        };
    }
}
