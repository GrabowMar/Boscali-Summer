using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The native-number hider rediscovers for a new aircraft or HUD, never for a cockpit/orbit flip.</summary>
    internal static class DiscoveryGateTests
    {
        public static void Run()
        {
            var gate = new DiscoveryGate();
            TestAssert.That(gate.Needs(10, 5), "The first sight of an aircraft discovers");
            TestAssert.That(!gate.Needs(10, 5), "The same aircraft and HUD do not rediscover");
            // Orbit to cockpit hands the hider no aircraft; back to orbit hands it the same one again.
            TestAssert.That(!gate.Needs(0, 5) && !gate.Needs(10, 5), "A camera flip is not a new aircraft");
            TestAssert.That(gate.Needs(11, 5), "A respawned aircraft discovers again");
            TestAssert.That(!gate.Needs(11, 5), "...once");
            TestAssert.That(gate.Needs(11, 6), "HUDExtras spawning late (a new app manager) discovers again");
            TestAssert.That(!gate.Needs(11, 0), "No app manager is not a new one");
            gate.Reset();
            TestAssert.That(gate.Needs(11, 6), "A scene reset forgets everything");
        }
    }
}
