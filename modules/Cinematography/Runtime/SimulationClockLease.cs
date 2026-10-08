using BoscaliSummer.Modules.Cinematography.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Cinematography.Runtime
{
    internal sealed class SimulationClockLease
    {
        private float previousScale,previousFixed,writtenScale,writtenFixed;
        private bool held;
        internal bool StillOwned => !held || (Time.timeScale==writtenScale || Time.timeScale==0) && Time.fixedDeltaTime==writtenFixed;
        internal bool Acquire(float rate,bool allowed,out string error)
        {
            error=null;
            if(held) { error="Clock lease already held";return false; }
            if(!ShotPlan.Finite(rate) || rate<.05f || rate>1) { error="Invalid simulation rate";return false; }
            if(rate==1) return true;
            if(!allowed) { error="Simulation slowmo requires opted-in single-player production";return false; }
            if(Time.timeScale<=0 || !ShotPlan.Finite(Time.timeScale) || !ShotPlan.Finite(Time.fixedDeltaTime) || Time.fixedDeltaTime<=0)
            { error="Cannot acquire a paused/invalid native clock";return false; }
            previousScale=Time.timeScale;previousFixed=Time.fixedDeltaTime;
            writtenScale=rate;writtenFixed=previousFixed*rate/previousScale;
            if(!ShotPlan.Finite(writtenFixed) || writtenFixed<=0) { error="Invalid scaled physics interval";return false; }
            held=true;Time.timeScale=writtenScale;Time.fixedDeltaTime=writtenFixed;return true;
        }
        internal void Release()
        {
            if(!held) return;held=false;
            if(Time.timeScale==writtenScale) Time.timeScale=previousScale;
            if(Time.fixedDeltaTime==writtenFixed) Time.fixedDeltaTime=previousFixed;
        }
    }
}
