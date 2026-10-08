using System.Collections.Generic;
using BoscaliSummer.Modules.Cinematography.Domain;

namespace BoscaliSummer.Modules.Cinematography
{
    internal static class CinematicSchema
    {
        internal static readonly string[] Actions={ "schema","status","get","pose","actors","list","detach","new","capture","set_shot","options","orbit","follow","demo","console",
            "save","load","set_edit","append_clip","clear_edit","save_edit","load_edit","play","play_edit","pause","resume","seek","bookmark","stop" };
        internal static Dictionary<string,object> Describe() => new Dictionary<string,object>
        {
            ["ok"]=true,["api_version"]=1,["entry_point"]="BoscaliSummer.Cinematics.CinematicAutomation.Step",
            ["actions"]=(string[])Actions.Clone(),["max_fields"]=16,["max_json_bytes"]=ShotStore.FileLimit,["max_keys"]=64,["max_clips"]=32,
            ["max_edit_seconds"]=600,["rate_range"]=new[] {.05f,4f},["simulation_rate_range"]=new[] {.05f,1f},
            ["mutation_context"]="enabled private host spectator; real slowmo additionally opted-in single-player",
            ["replay"]=false,["recorder"]="UNKNOWN",["seek_scope"]="camera only; world never rewinds",
            ["file_transport"]="Opt-in Cinematics/Inbox/<unique-id>.json -> Outbox/<unique-id>.json; atomic request write; at most one request per 0.2 seconds; last 128 replies retained",
            ["mutation_history_limit"]=CinematicsHistoryLimit,
            ["shot_json_example"]=ShotStore.Encode(new ShotPlan { id="example",mission="REPLACE_WITH_STATUS_MISSION",scene="REPLACE_WITH_STATUS_SCENE",
                keys=new[] { new ShotKey { y=100,qW=1 } },options=new ShotOptions() }),
            ["options_json_example"]=ShotStore.Encode(new ShotOptions()),
            ["json_commands"]=new Dictionary<string,string> { ["set_shot"]="json: validated ShotPlan",["options"]="json: ShotOptions; stop before replacing",
                ["set_edit"]="json: ShotSequence with inline clips",["seek"]="seconds: camera playhead",["orbit"]="x,y,z,radius,height,duration,from,to; optional actor",
                ["follow"]="actor: exact NetworkUniqueName; optional x,y,z offset,duration",["demo"]="actor: exact identity or nomodkit resolved actorUnit; authors a 15s follow/orbit/dolly test edit",["new"]="optional id",["load"]="optional id",["bookmark"]="optional label" },
            ["expected_revision"]="Optional compare-and-reject guard for mutations; obtain revision from status. No automatic retry of capture/append/bookmark."
        };
        private const int CinematicsHistoryLimit=4096;
    }
}
