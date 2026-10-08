using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Cinematography.Domain;
using BoscaliSummer.Modules.Cinematography.Presentation;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Cinematography.Runtime
{
    internal sealed partial class CinematicDirector
    {
        internal Dictionary<string,object> Command(Dictionary<string,object> args)
        {
            if(System.Threading.Thread.CurrentThread.ManagedThreadId!=mainThreadId)
                return new Dictionary<string,object> { ["ok"]=false,["api_version"]=1,["error"]="MAIN_THREAD_REQUIRED; use the nomodkit runtime call dispatcher" };
            string action=Text(args,"action");
            foreach(string field in new[] {"expected_revision","seconds","x","y","z","radius","height","duration","from","to","page"})
                if(Has(args,field) && (Arg(args,field) is bool || !ShotPlan.Finite(Number(args,field,float.NaN))))
                    return Reply(false,action,"INVALID NUMERIC FIELD: "+field);
            foreach(string field in new[] {"id","json","actor","label"})
                if(Has(args,field) && !(Arg(args,field) is string)) return Reply(false,action,"INVALID TEXT FIELD: "+field);
            if(action=="status") return Reply(true,action,null);
            if(action=="stop") { Stop();return Reply(true,action,null); }
            if(!ContextAllowed()) return Reply(false,action,"PRIVATE HOST SPECTATOR REQUIRED");
            if(Has(args,"expected_revision") && Number(args,"expected_revision",float.NaN)!=Revision)
                return Reply(false,action,"REVISION CONFLICT");
            if(action=="get") { var r=Reply(true,action,null);r["shot_json"]=ShotStore.Encode(Plan);r["edit_json"]=ShotStore.Encode(Edit);return r; }
            if(action=="pose")
            {
                var rig=SceneSingleton<CameraStateManager>.i;
                if(rig==null) return Reply(false,action,"CAMERA UNAVAILABLE");
                rig.GetCameraPosition(out GlobalPosition p,out Quaternion q);
                var r=Reply(true,action,null);r["x"]=p.x;r["y"]=p.y;r["z"]=p.z;
                r["qx"]=q.x;r["qy"]=q.y;r["qz"]=q.z;r["qw"]=q.w;r["fov"]=rig.mainCamera.fieldOfView;return r;
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
                var r=Reply(true,action,null);r["actors"]=actors.ToArray();r["truncated"]=actors.Count==64 || UnitRegistry.allUnits.Count>4096;return r;
            }
            if(action=="list") { var r=Reply(true,action,null);r["shots"]=store.ListShots();return r; }
            if(action=="console")
            {
                if(Has(args,"visible") && !(Arg(args,"visible") is bool)) return Reply(false,action,"visible must be boolean");
                bool visible=!Has(args,"visible") || Bool(args,"visible");
                if(!visible) {console?.Close();return Reply(true,action,null);}
                float page=Number(args,"page",0);
                if(page<0 || page>2 || page!=(int)page) return Reply(false,action,"Console page must be 0, 1 or 2");
                if(!CanUseConsole(out string reason)) return Reply(false,action,reason);
                console??=CinematicConsole.Create(this);console.Show();console.SwitchPage((int)page);
                return Reply(console.IsOpen,action,console.IsOpen?null:"Another input owner prevents opening the console");
            }
            if(action=="detach")
            {
                if(Playing) return Reply(false,action,"STOP PLAYBACK FIRST");
                var rig=SceneSingleton<CameraStateManager>.i;
                if(rig==null || rig.currentState==null || rig.currentState.GetType().Assembly!=typeof(CameraBaseState).Assembly)
                    return Reply(false,action,"NATIVE OBSERVER CAMERA REQUIRED");
                rig.SetFollowingUnit(null);Revision++;return Reply(true,action,null);
            }
            if(action=="pause" || action=="resume") return Reply(PauseCamera(action=="pause"),action,Playing?null:Message);
            if(action=="seek") { bool ok=Seek(Number(args,"seconds",float.NaN));return Reply(ok,action,ok?null:Message); }
            if(action=="bookmark")
            {
                bool ok=Playing && !pendingSeek.HasValue && Take.Mark(Text(args,"label") ?? "operator",Elapsed);
                if(ok) Revision++;return Reply(ok,action,ok?null:"NO EVALUATED TAKE, PENDING SEEK OR INVALID BOOKMARK");
            }
            if(!CanEdit(out string error)) return Reply(false,action,error);
            bool accepted=true;
            switch(action)
            {
                case "new":
                    string id=Text(args,"id") ?? "shot01";
                    if(!ShotPlan.SafeId(id)) { accepted=false;error="Invalid shot ID";break; }
                    NewShot();Plan.id=id;break;
                case "capture":
                    int count=Plan.keys.Length;Capture();accepted=Plan.keys.Length==count+1;if(!accepted) error=Message;break;
                case "set_shot": accepted=ShotStore.DecodeShot(Text(args,"json"),out var shot,out error) && SetShot(shot,out error);break;
                case "options":
                    accepted=ShotStore.DecodeOptions(Text(args,"json"),Plan.duration,out var options,out error);
                    if(accepted) Plan.options=options;break;
                case "orbit":
                    float radius=Number(args,"radius",50),height=Number(args,"height",10),from=Number(args,"from",0),to=Number(args,"to",180);
                    if(!ShotPlan.Finite(radius) || radius<1 || radius>10000 || !ShotPlan.Finite(height) || Math.Abs(height)>5000 ||
                        !ShotPlan.Finite(from) || !ShotPlan.Finite(to) || Math.Abs(from)>7200 || Math.Abs(to)>7200)
                    { accepted=false;error="Invalid orbit radius/height/angle";break; }
                    var center=new System.Numerics.Vector3(Number(args,"x",float.NaN),Number(args,"y",float.NaN),Number(args,"z",float.NaN));
                    var orbit=ShotRigs.Orbit(Plan.id,MissionIdentity,SceneIdentity,center,radius,height,Number(args,"duration",10),from,to);
                    orbit.anchorId=ActorArgument(args);accepted=SetShot(orbit,out error);break;
                case "follow":
                    string actor=ActorArgument(args);
                    if(string.IsNullOrWhiteSpace(actor)) { accepted=false;error="Exact actor ID required";break; }
                    var follow=ShotRigs.Follow(Plan.id,MissionIdentity,SceneIdentity,actor,new System.Numerics.Vector3(
                        Number(args,"x",-20),Number(args,"y",8),Number(args,"z",-30)),Number(args,"duration",10));accepted=SetShot(follow,out error);break;
                case "demo":
                    string subject=ActorArgument(args);
                    if(string.IsNullOrWhiteSpace(subject)) {accepted=false;error="Exact actor ID required";break;}
                    accepted=SetEdit(ShotRigs.Demo(MissionIdentity,SceneIdentity,subject),out error);break;
                case "save": accepted=store.Save(Plan,out error);break;
                case "load": accepted=store.Load(Text(args,"id") ?? Plan.id,out var loaded,out error) && SetShot(loaded,out error);break;
                case "set_edit": accepted=ShotStore.DecodeSequence(Text(args,"json"),out var edit,out error) && SetEdit(edit,out error);break;
                case "append_clip":
                    int before=Edit.clips.Length;AppendClip();accepted=Edit.clips.Length==before+1;if(!accepted) error=Message;break;
                case "clear_edit": ClearEdit();break;
                case "save_edit": accepted=store.SaveSequence(Edit,out error);break;
                case "load_edit": accepted=store.LoadSequence(Text(args,"id") ?? Edit.id,out var loadedEdit,out error) && SetEdit(loadedEdit,out error);break;
                case "play": accepted=PlayCurrent();if(!accepted) error=Message;break;
                case "play_edit": accepted=PlayEdit();if(!accepted) error=Message;break;
                default: accepted=false;error="Unknown command; call schema";break;
            }
            if(accepted) Revision++;
            return Reply(accepted,action,error);
        }
        private static string ActorArgument(Dictionary<string,object> args)
        { return Arg(args,"actorUnit") is Unit unit && unit!=null ? unit.NetworkUniqueName : Text(args,"actor"); }
        private Dictionary<string,object> Reply(bool ok,string action,string error) => new Dictionary<string,object>
        {
            ["ok"]=ok,["api_version"]=1,["action"]=action ?? "",["error"]=error ?? "",["status"]=Status,["message"]=Message,
            ["revision"]=Revision,["playing"]=Playing,["camera_paused"]=CameraPaused,["playhead"]=Elapsed,
            ["seek_pending"]=pendingSeek.HasValue,["requested_playhead"]=pendingSeek ?? Elapsed,
            ["shot_id"]=Plan?.id ?? "",["take_id"]=Take?.id ?? "",["clip_index"]=clipIndex,["edit_clips"]=Edit.clips.Length,
            ["mission"]=MissionIdentity,["scene"]=SceneIdentity,["simulation_rate_actual"]=Time.timeScale,
            ["camera_rate"]=Plan?.options?.playbackRate ?? 1,["clock"]=Plan?.options?.realtimeClock==true?"realtime":"mission",
            ["recorder"]="UNKNOWN",["world_rewound"]=false,
            ["console_open"]=console?.IsOpen ?? false
        };
    }
}
