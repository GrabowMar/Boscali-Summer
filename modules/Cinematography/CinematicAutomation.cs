using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Cinematography.Runtime;

namespace BoscaliSummer.Cinematics
{
    /// <summary>The nomodkit entry point (a sim <c>call</c> op, or <c>nomod live call</c>). <c>{"action":"schema"}</c> lists the commands.</summary>
    public static class CinematicAutomation
    {
        public static Dictionary<string,object> Step(Dictionary<string,object> args)
        {
            var director=CinematicDirector.Active;
            if(ReferenceEquals(director,null))
                return new Dictionary<string,object> { ["ok"]=false,["error"]="No shot camera: it exists only in a mission, in a windowed (not batch) game" };
            try { return director.Command(args ?? new Dictionary<string,object>()); }
            catch(Exception e) { return new Dictionary<string,object> { ["ok"]=false,["error"]="Command failed: "+e.GetType().Name+": "+e.Message }; }
        }
    }
}
