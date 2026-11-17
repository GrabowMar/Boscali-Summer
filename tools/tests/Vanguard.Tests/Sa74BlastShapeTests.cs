using System;
using BoscaliSummer.Modules.Vanguard.Domain;

internal static class Sa74BlastShapeTests
{
    public static void Run(Action<bool, string> check)
    {
        float radius = Sa74BlastShape.Radius(163840f, 81920f);
        check(Math.Abs(radius - 90112f) < .1f, "Reach must exceed half the standard map's longest side.");
        check(Sa74BlastShape.Radius(20000f, 30000f) == 75000f, "Small maps keep the requested 75 km minimum.");
        check(Sa74BlastShape.Radius(float.NaN, float.PositiveInfinity) == 75000f, "Invalid map dimensions must not poison physics.");
        check(Sa74BlastShape.Radius(300000f, 100000f) >= 150000f, "Large maps must retain half-map reach.");
        check(Sa74BlastShape.FrontRadius(-5f, radius) == 2100f, "Future effects cannot reverse the front.");
        check(Sa74BlastShape.FrontRadius(120f, radius) > 42000f, "The front must pass the old cloud-clearance cap.");
        check(Sa74BlastShape.FrontRadius(600f, radius) == radius, "The front must stop at its final reach.");
        check(Math.Abs(Sa74BlastShape.FrontRadius(Sa74BlastShape.ArrivalSeconds(radius), radius) - radius) < .1f,
            "Audio, damage and cloud clearance must share one arrival time.");
        float initialized = (float)Math.Pow(Sa74BlastShape.InitializationYield(radius) * 1000000f, .3333f) * 13f;
        check(Math.Abs(initialized - radius) < 2f, "Native initialization must cover the full requested collider/decal reach.");
        check(Sa74BlastShape.MaximumVisualAge == 600f, "The mushroom volume expires by ten minutes.");
    }
}
