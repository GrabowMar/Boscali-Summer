using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.Support.Configuration
{
    /// <summary>
    /// The host-only support knobs the SET SERVER page exposes. Kept beside the settings it
    /// writes and free of runtime types, so the declaration can be checked without the
    /// module's managers.
    ///
    /// <para>What earns a row here is a decision a host makes while a mission is running:
    /// which call-ins exist, and what they cost in allocation. The ordnance
    /// dimensions behind them - blast radii, flare counts and similar tuning - are balance, set once and left alone, and live in the
    /// config file and the F1 window's advanced half instead of on a page a host scrolls
    /// mid-sortie.</para>
    /// </summary>
    internal static class SupportHostSettings
    {
        public static HostSettingsTable Build(SupportSettings settings) =>
            new HostSettingsTable("SUPPORT CALL-INS")
                .Toggle(1, settings.ReconEnabled, "RECON PASS",
                    "Radar scan and MTI sweep in one: reveals static and moving ground contacts in a scene. Spawns nothing.")
                .Toggle(2, settings.FortifyEnabled, "FORTIFY",
                    "Reinforce a friendly controlled zone. Refused, and nothing charged, when defenders cannot be placed.")
                .Toggle(3, settings.ArtilleryEnabled, "ORBITAL ROD",
                    "Orbital kinetic strike: one high-velocity projectile onto the mark.")
                .Toggle(4, settings.EmpEnabled, "EMP SHOCK",
                    "High-altitude airburst: the prompt pulse upsets electronics, the geomagnetic phase jams radars across a wide area.")
                .Toggle(5, settings.ElintEnabled, "ELINT SWEEP",
                    "Locates emitting enemy ground and ship radars near the mark.")
                .Toggle(6, settings.FlareBarrageEnabled, "DECOY BARRAGE",
                    "An airburst countermeasure rocket that disperses intense flares, seducing and misguiding hostile IR missiles in the area.")
                .Toggle(16, settings.SatCameraEnabled, "SAT CAMERA",
                    "The OPTICAL bird images the mark by day and reveals ground units in its window. Refuses at night.")
                .Toggle(17, settings.WatchOfficerEnabled, "WATCH OFFICER",
                    "OVERLORD directs SPACE, CYBER and SOF for a faction with humans, always, following the front posture: scans and TASKED calls from revealed contacts, intrusions on revealed nodes, one SOF team on revealed targets. It spends nothing.")
                .Toggle(18, settings.CyberEnabled, "CYBER / EW",
                    "EW trucks and data centers, node intrusion (hop, hold, burn, drop) and the CYBER BURN packages on the TASKED board.")
                .Toggle(19, settings.SofEnabled, "SOF / JTAC",
                    "A camp, abstract teams (raise, route, push, hold, divert, exfil), the five odds-resolved missions, held buildings and the helicopter lift.")
                .Toggle(20, settings.OpsEnabled, "OPERATIONS",
                    "The effects behind the ASAT, ZERO-DAY and FORWARD OPERATING BASE programmes. The programmes themselves are paid from the fronts; needs CYBER or SOF.")
                .Toggle(21, settings.AiFactionsEnabled, "AI FACTIONS",
                    "A faction with no humans works CYBER and SOF itself (one domain action every 30 s) and queues its own front programmes from its share of the treasury. Never targets what it has not revealed.")
                .Number(14, settings.PerkPriceScale, "PERK PRICES",
                    "Scales every perk price in allocation. 1.0 charges the rung table (4 / 6 / 10 / 16 / 30).",
                    0.05f, v => v.ToString("0.00") + "x")
                .Number(22, settings.FrontShare, "FRONT SHARE",
                    "The share of each faction's funds that flows into its three fronts every minute. 0 % leaves fronts to donations only.",
                    0.005f, v => (v * 100f).ToString("0.0") + " %")
                .Number(24, settings.FrontShareCap, "FRONT SHARE CAP",
                    "The most funds per minute a faction's fronts take, however large its treasury. 250 raises a front from READINESS 1 to 5 in about 40 minutes.",
                    25f, v => v.ToString("0") + " / MIN")
                .Number(23, settings.ProgrammeCostScale, "PROGRAMME COSTS",
                    "Scales every front programme's cost. 1.0 charges the table (READINESS 150 x rung, LAUNCH SATELLITE 300, ASAT 900 ...).",
                    0.05f, v => v.ToString("0.00") + "x");
    }
}
