using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Support.Configuration
{
    /// <summary>
    /// The host-only support knobs the SET SERVER page exposes. Kept beside the settings it
    /// writes and free of runtime types, so the declaration can be checked without the
    /// module's managers.
    /// </summary>
    internal static class SupportHostSettings
    {
        public static HostSettingsTable Build(SupportSettings settings) =>
            new HostSettingsTable("SUPPORT CALL-INS")
                .Number(1, settings.CostMultiplier, "COST SCALE",
                    "Scales every support cost at once. 1.0 charges roughly what the effect is worth.",
                    0.05f, v => v.ToString("0.00") + "x")
                .Number(2, settings.RequestCooldown, "REQUEST COOLDOWN",
                    "Cooldown after an accepted request, per player and shared across all actions.",
                    5f, v => v.ToString("0") + " s")
                .Number(3, settings.MaximumRange, "MAXIMUM RANGE",
                    "Furthest a strike may be designated from your aircraft.",
                    1000f, v => (v / 1000f).ToString("0") + " km")
                .Toggle(4, settings.ReconEnabled, "RADAR SCAN",
                    "A station with a spy imager, overhead, images a scene and reveals stationary ground contacts. Spawns nothing.")
                .Toggle(5, settings.FortifyEnabled, "ZONE FORTIFICATION",
                    "Reinforce a friendly controlled zone. Refused, and nothing charged, when defenders cannot be placed.")
                .Toggle(6, settings.ArtilleryEnabled, "ROD FROM GOD",
                    "Orbital kinetic strike from the station's rod magazine: one high-velocity projectile onto the mark.")
                .Toggle(7, settings.EmpEnabled, "EMP SHOCK",
                    "High-altitude airburst: the prompt pulse upsets electronics, the geomagnetic phase jams radars across a wide area.")
                .Toggle(8, settings.ElintEnabled, "ELINT SWEEP",
                    "A station with a SIGINT array, overhead, locates emitting enemy ground and ship radars near the mark.")
                .Toggle(9, settings.FlareBarrageEnabled, "FLARE BARRAGE",
                    "An airburst countermeasure rocket that disperses intense flares, seducing and misguiding IR missiles in the area.")
                .Toggle(10, settings.EwEnabled, "ELECTRONIC WARFARE",
                    "Build a radar truck and unlock radar blackout, ghost shield and spoof contacts.")
                .Toggle(11, settings.CyberEnabled, "CYBER OPERATIONS",
                    "Infrastructure investment and the information operations it unlocks.")
                .Number(12, settings.PlatformCostScale, "PLATFORM COST SCALE",
                    "Scales every orbital station launch: core, modules and cargo.",
                    0.05f, v => v.ToString("0.00") + "x")
                .Number(13, settings.PlatformJettisonRefund, "JETTISON REFUND",
                    "Fraction of what was paid refunded when a station module is jettisoned; jettisoning the core deorbits the station.",
                    0.05f, v => v.ToString("P0"))
                .Number(14, settings.PlatformInsertionSeconds, "INSERTION TIME",
                    "Seconds from core liftoff to orbit insertion; the pass cycle begins at insertion.",
                    5f, v => v.ToString("0") + " s")
                .Number(15, settings.PlatformDockingSeconds, "DOCKING TIME",
                    "Seconds from a module or cargo liftoff to docking.",
                    5f, v => v.ToString("0") + " s")
                .Toggle(16, settings.PlatformDebrisEvents, "DEBRIS STRIKES",
                    "A micrometeoroid strike hits a random station module every 6-10 minutes; unshielded modules go offline for 45 s.")
                .Number(17, settings.EmpRadius, "EMP RADIUS",
                    "Radius around the mark whose radars are jammed by an EMP shock, friendly and hostile alike.",
                    1000f, v => (v / 1000f).ToString("0") + " km")
                .Number(18, settings.ElintRadius, "ELINT RADIUS",
                    "Radius around the mark searched for emitting enemy radars.",
                    1000f, v => (v / 1000f).ToString("0") + " km")
                .Number(19, settings.FlareBarrageRadius, "FLARE RADIUS",
                    "Radius around the mark in which IR missiles are seduced and misguided.",
                    500f, v => (v / 1000f).ToString("0.#") + " km")
                .Number(20, settings.FlareBarrageCount, "FLARE COUNT",
                    "Number of pyrotechnic flares dispersed in the initial airburst wave.", 2)
                .Number(21, settings.OrbitGapScale, "ORBIT GAP SCALE",
                    "Scales the simulated out-of-theatre arc between station passes. Passes themselves always fly at real orbital speed.",
                    0.25f, v => v.ToString("0.00") + "x");
    }
}
