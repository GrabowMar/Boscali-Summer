using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Hud
{
    internal static class MissileTracksTests
    {
        public static void Run()
        {
            var guided = new MissileTrack { Id = 1, Name = "AIM-120", OwnM = 800f, TargetM = 2100f, HasTarget = true, TargetName = "MiG-29", AgeS = 3f };
            string line = MissileTracks.LineText(guided, AvUnits.Metric);
            TestAssert.That(line == "MSL 800m · TGT 2.1km", "Missile line shows own and target distances: " + line);

            var blind = new MissileTrack { Id = 2, Name = "AIM-9", OwnM = 1200f, HasTarget = false, AgeS = 8f };
            string blindLine = MissileTracks.LineText(blind, AvUnits.Metric);
            TestAssert.That(blindLine == "MSL 1.2km · NO TGT", "Blind missile line says NO TGT: " + blindLine);
            TestAssert.That(MissileTracks.ToneFor(blind) == HudTone.Caution, "Old blind missile is caution");
            TestAssert.That(MissileTracks.ToneFor(guided) == HudTone.Info, "Guided missile is routine info");

            string detail = MissileTracks.DetailText(guided);
            TestAssert.That(detail == "AIM-120 → MIG-29", "Detail names weapon and target: " + detail);
            TestAssert.That(MissileTracks.DetailText(blind) == "AIM-9 · BLIND", "Blind detail says BLIND");

            float bar = MissileTracks.BarFor(guided);
            TestAssert.That(bar > 0.27f && bar < 0.28f, "Bar is ownship-to-target progress");
            TestAssert.That(MissileTracks.BarFor(blind) == 0f, "Blind missile has no progress bar");

            TestAssert.That(MissileTracks.Cycle(0, 1, 3) == 1, "Cycle steps forward");
            TestAssert.That(MissileTracks.Cycle(2, 1, 3) == 0, "Cycle wraps past the end");
            TestAssert.That(MissileTracks.Cycle(0, -1, 3) == 2, "Cycle wraps before the start");
            TestAssert.That(MissileTracks.Cycle(0, 1, 0) == -1, "Cycle with nothing is -1");

            string status = MissileTracks.Status(2, 800f, AvUnits.Metric);
            TestAssert.That(status == "2 MSL · CLOSEST 800m", "C-menu status counts and ranges: " + status);
            TestAssert.That(MissileTracks.Status(0, 0f, AvUnits.Metric) == string.Empty, "No missiles, no status");
        }
    }
}
