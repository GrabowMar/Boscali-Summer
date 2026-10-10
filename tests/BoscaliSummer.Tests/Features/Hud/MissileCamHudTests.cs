using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The words of the missile-cam readout.</summary>
    internal static class MissileCamHudTests
    {
        public static void Run()
        {
            TestAssert.That(MissileCamText.StateOf(1) == SeekerState.Locked && MissileCamText.StateOf(2) == SeekerState.Search &&
                MissileCamText.StateOf(3) == SeekerState.Passive && MissileCamText.StateOf(0) == SeekerState.Unknown, "Seeker modes map one to one");

            var d = new MissileCamData
            {
                Valid = true, Index = 0, Count = 2, Weapon = "AIM-120", Seeker = "SARH", State = SeekerState.Locked,
                HasTarget = true, Target = "MiG-29", RangeM = 2400f, ClosureMps = 233.3f, TtiS = 3.4f,
            };
            TestAssert.That(MissileCamText.Header(d) == "MSL 1/2 · AIM-120", "Header: place in the list and the weapon: " + MissileCamText.Header(d));
            TestAssert.That(MissileCamText.Seeker(d) == "SARH · LOCKED", "A locked seeker reads LOCKED: " + MissileCamText.Seeker(d));
            TestAssert.That(MissileCamText.Range(d, AvUnits.Metric) == "2.4KM", "Range to go: " + MissileCamText.Range(d, AvUnits.Metric));
            TestAssert.That(MissileCamText.Closure(d, AvUnits.Metric) == "840KM/H", "Closure in the player's units: " + MissileCamText.Closure(d, AvUnits.Metric));
            TestAssert.That(MissileCamText.Tti(d) == "3S", "Time to impact: " + MissileCamText.Tti(d));
            TestAssert.That(MissileCamText.Tone(d) == HudTone.Info, "A locked seeker is routine");

            d.State = SeekerState.Search;
            TestAssert.That(MissileCamText.Seeker(d) == "SARH · SEARCH" && MissileCamText.Tone(d) == HudTone.Caution, "A searching seeker cautions");
            d.State = SeekerState.Passive;
            TestAssert.That(MissileCamText.Seeker(d) == "SARH · PASSIVE", "A passive seeker says so");

            d.HasTarget = false; d.Seeker = "INS"; d.RangeM = float.NaN; d.ClosureMps = float.NaN; d.TtiS = float.NaN;
            TestAssert.That(MissileCamText.Seeker(d) == "INS · NO TGT" && MissileCamText.Tone(d) == HudTone.Caution, "A blind missile says NO TGT");
            TestAssert.That(MissileCamText.Range(d, AvUnits.Metric) == "--" && MissileCamText.Closure(d, AvUnits.Metric) == "--" && MissileCamText.Tti(d) == "--",
                "Unknown numbers read --");

            d.ClosureMps = -40f;
            TestAssert.That(MissileCamText.Closure(d, AvUnits.Metric) == "OPENING", "A target pulling away is OPENING");
            d.TtiS = 0.2f;
            TestAssert.That(MissileCamText.Tti(d) == "1S", "The last instant reads 1 s");
            d.Weapon = null; d.Seeker = null; d.HasTarget = true; d.State = SeekerState.Unknown;
            TestAssert.That(MissileCamText.Header(d) == "MSL 1/2" && MissileCamText.Seeker(d) == "SEEKER · TRACK", "Missing names degrade to plain words");
        }
    }
}
