using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Features.Support.Configuration
{
    /// <summary>
    /// The host-only support knobs the SET SERVER page exposes. Kept beside the settings it
    /// writes and free of runtime types, so the declaration can be checked without the
    /// module's managers.
    ///
    /// <para>What earns a row here is a decision a host makes while a mission is running:
    /// which call-ins exist, and what they cost in allocation and in patience. The ordnance
    /// dimensions behind them - blast radii, flare counts, launch and docking clocks, orbit
    /// pacing, cyber upgrade prices - are balance, set once and left alone, and live in the
    /// config file and the F1 window's advanced half instead of on a page a host scrolls
    /// mid-sortie.</para>
    /// </summary>
    internal static class SupportHostSettings
    {
        public static HostSettingsTable Build(SupportSettings settings) =>
            new HostSettingsTable("SUPPORT CALL-INS")
                .Toggle(1, settings.ReconEnabled, "RADAR SCAN",
                    "A station with a spy imager, overhead, images a scene and reveals stationary ground contacts. Spawns nothing.")
                .Toggle(2, settings.FortifyEnabled, "ZONE FORTIFICATION",
                    "Reinforce a friendly controlled zone. Refused, and nothing charged, when defenders cannot be placed.")
                .Toggle(3, settings.ArtilleryEnabled, "ROD FROM GOD",
                    "Orbital kinetic strike from the station's rod magazine: one high-velocity projectile onto the mark.")
                .Toggle(4, settings.EmpEnabled, "EMP SHOCK",
                    "High-altitude airburst: the prompt pulse upsets electronics, the geomagnetic phase jams radars across a wide area.")
                .Toggle(5, settings.ElintEnabled, "ELINT SWEEP",
                    "A station with a SIGINT array, overhead, locates emitting enemy ground and ship radars near the mark.")
                .Toggle(6, settings.FlareBarrageEnabled, "FLARE BARRAGE",
                    "An airburst countermeasure rocket that disperses intense flares, seducing and misguiding IR missiles in the area.")
                .Toggle(7, settings.PlatformDebrisEvents, "DEBRIS STRIKES",
                    "A micrometeoroid strike hits a random station module every 6-10 minutes; unshielded modules go offline for 45 s.")
                .Toggle(8, settings.EwEnabled, "CYBER NETWORK",
                    "The CYBER network: airbase nodes that come up by themselves, hackable locations, the breach minigame and the console.")
                .Toggle(9, settings.CyberEnabled, "CYBER OPERATIONS",
                    "The map abilities a hacked location unlocks, and the adversary campaign.")
                .Toggle(12, settings.SpecOpsEnabled, "SPEC OPS DETACHMENT",
                    "Teams sent to real objectives: recon reveals, sabotage jams air defence, seizing occupies buildings. Their posts grant SPOT and SUPPRESS.")
                .Number(10, settings.CostMultiplier, "COST SCALE",
                    "Scales every support cost at once, orbital launches included. 1.0 charges roughly what the effect is worth.",
                    0.05f, v => v.ToString("0.00") + "x")
                .Number(11, settings.RequestCooldown, "REQUEST COOLDOWN",
                    "Cooldown after an accepted request, per player and shared across all actions.",
                    5f, v => v.ToString("0") + " s")
                .Toggle(13, settings.MtiEnabled, "MTI SWEEP",
                    "A station with a spy imager, overhead, tracks moving enemy ground contacts near the mark. Shares the radar scan recharge.");
    }
}
