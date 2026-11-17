using System;
using BoscaliSummer.Modules.Vanguard.Domain;

internal static class Sa74FlashTests
{
    public static void Run(Action<bool, string> check)
    {
        check(Sa74Flash.Pulse(-1f) == 0f, "Future events must not flash.");
        check(Sa74Flash.Pulse(0f) > .99f, "The prompt light pulse must be immediate.");
        check(Sa74Flash.Pulse(.18f) < .1f, "The shock-layer dip must separate the two peaks.");
        check(Sa74Flash.Pulse(1.1f) > .8f, "The longer thermal pulse must follow the dip.");
        check(Sa74Flash.Pulse(60f) < .002f, "Thermal light must cool out rather than glow forever.");
        check(Sa74Flash.Winter(10f) == 0f && Sa74Flash.Winter(120f) == 1f, "Winter must develop after the brilliant phase.");
        check(Sa74Flash.Range(5000f) > Sa74Flash.Range(80000f), "Flash glare must attenuate with distance.");
        check(Sa74Flash.Facing(1f) > Sa74Flash.Facing(-1f) * 5f, "Direct source glare must exceed looking away.");
        check(Sa74Flash.GlareGain(.03f) > 15f && Sa74Flash.GlareGain(1.1f) >= 10f && Sa74Flash.GlareGain(12f) < .2f,
            "Blinding early glare must recover quickly enough to reveal the cloud and shock front.");
    }
}
