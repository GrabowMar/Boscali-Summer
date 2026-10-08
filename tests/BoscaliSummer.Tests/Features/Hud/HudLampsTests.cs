using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The annunciator row lights the right lamps for the aircraft's switches.</summary>
    internal static class HudLampsTests
    {
        public static void Run()
        {
            var lamps = new Lamp[HudLamps.Count];

            HudLamps.Fill(new SystemStates(), lamps);
            foreach (Lamp l in lamps) TestAssert.That(!l.Present, "Unknown systems leave their lamp out: " + l.Legend);

            HudLamps.Fill(new SystemStates
            {
                Gear = GearLamp.Down, FlightAssist = true, AutoHover = false, RadarEmitting = true,
                NightVision = false, WeaponSafe = true, EngineRunning = true,
            }, lamps);
            TestAssert.That(lamps[0].State == LampState.On && !lamps[0].Flash, "Gear down and locked: GEAR lit steady");
            TestAssert.That(lamps[1].State == LampState.On && lamps[2].State == LampState.Dark, "FA on, HOVR off");
            TestAssert.That(lamps[3].State == LampState.Caution, "Radar emitting is a caution");
            TestAssert.That(lamps[5].State == LampState.Caution, "Weapon safety on is a caution");
            TestAssert.That(lamps[6].State == LampState.Dark && lamps[6].Present, "A running engine stays dark");

            HudLamps.Fill(new SystemStates { Gear = GearLamp.Moving, EngineRunning = false }, lamps);
            TestAssert.That(lamps[0].State == LampState.Caution && lamps[0].Flash, "Gear in transit flashes amber");
            TestAssert.That(lamps[6].State == LampState.Warning, "Engine not running is a warning");
            TestAssert.That(lamps[0].Legend == "GEAR" && lamps[6].Legend == "ENG", "Legends stay fixed in order");
        }
    }
}
