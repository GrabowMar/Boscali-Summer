using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Support.Configuration
{
    /// <summary>
    /// The host-only support knobs the SET SERVER page exposes. Kept beside the settings it
    /// writes and free of runtime types, so the declaration can be checked without the
    /// module's managers.
    ///
    /// <para>What earns a row here is a decision a host makes while a mission is running:
    /// which call-ins exist, and what they cost in allocation and in patience. The ordnance
    /// dimensions behind them - blast radii, flare counts and similar tuning - are balance, set once and left alone, and live in the
    /// config file and the F1 window's advanced half instead of on a page a host scrolls
    /// mid-sortie.</para>
    /// </summary>
    internal static class SupportHostSettings
    {
        public static HostSettingsTable Build(SupportSettings settings) =>
            new HostSettingsTable("SUPPORT CALL-INS")
                .Toggle(1, settings.ReconEnabled, "RADAR SCAN",
                    "Images a scene and reveals stationary ground contacts. Spawns nothing.")
                .Toggle(2, settings.FortifyEnabled, "ZONE FORTIFICATION",
                    "Reinforce a friendly controlled zone. Refused, and nothing charged, when defenders cannot be placed.")
                .Toggle(3, settings.ArtilleryEnabled, "ORBITAL ROD",
                    "Orbital kinetic strike: one high-velocity projectile onto the mark.")
                .Toggle(4, settings.EmpEnabled, "EMP SHOCK",
                    "High-altitude airburst: the prompt pulse upsets electronics, the geomagnetic phase jams radars across a wide area.")
                .Toggle(5, settings.ElintEnabled, "ELINT SWEEP",
                    "Locates emitting enemy ground and ship radars near the mark.")
                .Toggle(6, settings.FlareBarrageEnabled, "FLARE BARRAGE",
                    "An airburst countermeasure rocket that disperses intense flares, seducing and misguiding hostile IR missiles in the area.")
                .Number(11, settings.RequestCooldown, "REQUEST COOLDOWN",
                    "Cooldown after an accepted request, per player and shared across all actions.",
                    5f, v => v.ToString("0") + " s")
                .Toggle(13, settings.MtiEnabled, "MTI SWEEP",
                    "Tracks moving enemy ground contacts near the mark. Shares the radar scan recharge.")
                .Toggle(16, settings.SatCameraEnabled, "SAT CAMERA",
                    "The OPTICAL bird images the mark by day and reveals ground units in its window. Refuses at night.")
                .Toggle(17, settings.WatchOfficerEnabled, "WATCH OFFICER",
                    "OVERLORD staffs SPACE while no human is working it: it scans for contacts and posts TASKED calls from revealed ones.")
                .Toggle(18, settings.CyberEnabled, "CYBER / EW",
                    "EW trucks and data centers, node intrusion (hop, hold, burn, drop) and the CYBER BURN packages on the TASKED board.")
                .Toggle(19, settings.SofEnabled, "SOF / JTAC",
                    "A camp, abstract teams (raise, route, push, hold, divert, exfil), the five odds-resolved missions, held buildings and the helicopter lift.")
                .Number(14, settings.PriceKnob, "CALL PRICES",
                    "Scales every CALL price. 1.0 charges the spec prices.",
                    0.05f, v => v.ToString("0.00") + "x")
                .Number(15, settings.EarnKnob, "CREDIT EARNINGS",
                    "Scales every CR payout. 1.0 pays the spec rates.",
                    0.05f, v => v.ToString("0.00") + "x");
    }
}
