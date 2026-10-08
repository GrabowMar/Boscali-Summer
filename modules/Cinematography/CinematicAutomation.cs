using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Cinematography;
using BoscaliSummer.Modules.Cinematography.Runtime;

namespace BoscaliSummer.Cinematics
{
    /// <summary>Bounded LLM/nomodkit entry point. Commands use the same director, validation and production gates as the console.</summary>
    public static class CinematicAutomation
    {
        public static Dictionary<string,object> Step(Dictionary<string,object> args)
        {
            if(args==null || args.Count>16) return new Dictionary<string,object> { ["ok"]=false,["api_version"]=1,["error"]="Expected a command object with at most 16 fields" };
            if(args.TryGetValue("action",out object action) && action as string=="schema") return CinematicSchema.Describe();
            var director=CinematicDirector.Active;
            if(ReferenceEquals(director,null)) return new Dictionary<string,object> { ["ok"]=false,["api_version"]=1,["error"]="Cinematography module unavailable; enable it before startup" };
            try { return director.Command(args); }
            catch(Exception e) { return new Dictionary<string,object> { ["ok"]=false,["api_version"]=1,["error"]="Command failed: "+e.GetType().Name }; }
        }
    }
}
