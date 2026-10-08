using BoscaliSummer.Modules.Cinematography.Runtime;
using UnityEngine;

static class TimingTests
{
    internal static void Run(Action<bool,string> check)
    {
        Time.timeScale=1;Time.fixedDeltaTime=.02f;
        var lease=new SimulationClockLease();
        check(!lease.Acquire(.25f,false,out _) && Time.timeScale==1,"deny unapproved simulation slowmo");
        check(lease.Acquire(.25f,true,out _) && Time.timeScale==.25f && Math.Abs(Time.fixedDeltaTime-.005f)<.00001,"single-player clock lease");
        Time.timeScale=0;lease.Release();
        check(Time.timeScale==0 && Time.fixedDeltaTime==.02f,"cleanup must not unpause native pause");
        Time.timeScale=1;lease.Acquire(.5f,true,out _);
        Time.timeScale=.75f;Time.fixedDeltaTime=.015f;lease.Release();
        check(Time.timeScale==.75f && Time.fixedDeltaTime==.015f,"new owner clock preserved");
        Time.timeScale=1;Time.fixedDeltaTime=.02f;lease.Acquire(.2f,true,out _);lease.Release();lease.Release();
        check(Time.timeScale==1 && Time.fixedDeltaTime==.02f,"idempotent restoration");
    }
}
