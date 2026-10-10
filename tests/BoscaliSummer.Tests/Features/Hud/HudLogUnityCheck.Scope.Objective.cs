#if UNITY_EDITOR
using System;
using UnityEngine;

// OBJECTIVE fixture: pages/objective.js sim(t), the 20 s loop. The contract is a DAMAGE ASSESSMENT (STRIKE then SURVEY) on a 1.5 km zone. The pilot flies a
// leg and an orbit, dives from 1800 m to 690 m AGL (inside the SA-19's envelope until 3.6 s, masked after), the target falls at 3.2, the ZSU's guns reach him
// at 4.8, he enters the zone at 5.9 (low: no sightline until 7.2) and the SURVEY runs from 7.2. The numbers are what DynamicOperations' provider would send;
// the zone sits at an arbitrary global point so the plot's mapping from global coordinates is exercised.
// Variants (1080p, t = 6): "far" = the pilot 11 km out, joining; "bare" = a single-phase contract with no air defence known (the host's instruction fills the column).
public static partial class HudLogUnityCheck
{
    private const float ObjZoneX = 21500f, ObjZoneZ = -8300f;

    private static object RingOf(float x, float z, float range, float minAlt, string kind, bool radar) =>
        Activator.CreateInstance(Type("BoscaliSummer.Core.Contracts.AirDefenceRing"), x, z, range, minAlt, 12000f, Enum.Parse(Type("BoscaliSummer.Core.Contracts.AirDefenceKind"), kind), radar, radar,
            Enum.Parse(Type("BoscaliSummer.Core.Contracts.RingSource"), "Tracked"), true, 2f, false, 2, 1, 0);

    private static object PhaseOf(string title, string status, float progress) =>
        Activator.CreateInstance(Type("BoscaliSummer.Core.Contracts.ObjectivePhase"), (byte)0, title, "", Enum.Parse(Type("BoscaliSummer.Core.Contracts.ObjectivePhaseStatus"), status), progress);

    static partial void FixtureObjective(float t, ScopeScene s)
    {
        const float rho = 0.8f, l1 = 3.3f, holdSeconds = 20f, tKill = 3.2f, tLos = 7.2f, deadline = 480f;
        float s2 = Mathf.Sqrt(0.5f), ocx = 0.35f, ocy = -0.2f;
        float t0x = ocx - rho * s2, t0y = ocy + rho * s2, p0x = t0x - s2 * l1, p0y = t0y - s2 * l1;
        float travelled = t <= 6f ? 0.3f * t : t <= 8f ? 1.8f + 0.3f * (t - 6f) - 0.0325f * (t - 6f) * (t - 6f) : 2.27f + 0.17f * (t - 8f);
        float ownX, ownY, hdg;
        if (travelled <= l1) { ownX = p0x + s2 * travelled; ownY = p0y + s2 * travelled; hdg = 45f; }
        else
        {
            float phi = (315f + (travelled - l1) / rho * (180f / Mathf.PI)) * (Mathf.PI / 180f);
            ownX = ocx + rho * Mathf.Sin(phi); ownY = ocy + rho * Mathf.Cos(phi); hdg = phi * 180f / Mathf.PI + 90f;
        }
        float agl = t < 3.7f ? 1800f - 300f * t : 690f - 10f * (t - 3.7f);
        string variant = s.Variant;
        bool far = variant == "far", bare = variant == "bare";
        if (far) { ownX = -7.9f; ownY = -7.9f; hdg = 45f; agl = 620f; }

        float dz = Mathf.Sqrt(ownX * ownX + ownY * ownY);
        bool killed = !far && !bare && t >= tKill, inZone = dz < (bare ? 0.4f : 1.5f), los = !(inZone && t < tLos), ticking = killed && inZone && los && t >= tLos - 0.01f;
        float sec = ticking ? Mathf.Min(holdSeconds, t - tLos) : 0f;
        bool gates = far || killed;                                      // JOIN / SURVEY are the phases the gates decide; STRIKE is not

        // the air defence: an SA-19 and a ZSU pit near the zone (none known in the "bare" shot)
        Type ringType = Type("BoscaliSummer.Core.Contracts.AirDefenceRing");
        Array rings = Array.CreateInstance(ringType, bare ? 0 : 2);
        if (!bare)
        {
            rings.SetValue(RingOf(ObjZoneX + (far ? 4300f : -3900f), ObjZoneZ + 700f, 6000f, 1500f, "RadarSam", true), 0);
            rings.SetValue(RingOf(ObjZoneX + 400f, ObjZoneZ + 600f, 2500f, 0f, "Gun", false), 1);
        }

        Type phaseType = Type("BoscaliSummer.Core.Contracts.ObjectivePhase");
        Array phases = Array.CreateInstance(phaseType, bare ? 1 : 2);
        if (bare) phases.SetValue(PhaseOf("STRIKE", "Current", 0f), 0);
        else if (far) { phases.SetValue(PhaseOf("JOIN", "Current", 0f), 0); phases.SetValue(PhaseOf("SURVEY", "Pending", 0f), 1); }
        else { phases.SetValue(PhaseOf("STRIKE", killed ? "Done" : "Current", killed ? 1f : 0f), 0); phases.SetValue(PhaseOf("SURVEY", killed ? "Current" : "Pending", sec / holdSeconds), 1); }

        string title = bare ? "INTERDICT" : far ? "DAMAGE ASSESSMENT" : "DAMAGE ASSESSMENT";
        object scope = s.Fill(s.Contract("ObjectiveScope"), "Id", 7, "Title", title, "Target", bare ? "SUPPLY TRUCK 12" : "FUEL DEPOT 3", "Contact", s.ContractEnum("ObjectiveContact", bare ? "Known" : "Fixed"), "HasMarker", true,
            "X", ObjZoneX, "Z", ObjZoneZ, "Radius", bare ? 400f : 1500f,
            "OwnX", ObjZoneX + ownX * 1000f, "OwnZ", ObjZoneZ + ownY * 1000f, "OwnHeading", hdg, "OwnAgl", agl,
            "Left", far ? 701f : 342f - t, "Total", far ? 720f : deadline, "Money", bare ? 8900 : 14200, "Xp", bare ? 210 : 340, "Multiplier", bare ? 0f : 1.15f,
            "Phases", phases, "PhaseCount", bare ? 1 : 2, "Current", bare ? 0 : far ? 0 : killed ? 1 : 0,
            "NextAction", bare ? "Neutralize the marked hostile target before the deadline." : "Survey the strike site for 20 continuous seconds.",
            "Blocker", inZone && !los ? "No eligible aircraft on station. Check range, height and sightline." : "",
            "HasGates", gates, "GateRange", inZone, "GateAlt", agl >= 50f, "GateLos", los, "GateTrack", true,
            "RingCount", rings.Length, "Rings", rings);
        s.Publish(scope);

        s.SetFrame("Systems", s.Fill(s.Domain("SystemStates"), "Gear", s.DomainEnum("GearLamp", "Up"), "FlightAssist", true, "AutoHover", false,
            "RadarEmitting", true, "NightVision", false, "WeaponSafe", false, "EngineRunning", true));

        // the registered AUTOPILOT gauge fills the fourth slot (the page brings PHASE, SAM and AGL)
        s.Gauge("ap", s.Fill(s.Contract("GaugeSpec"), "Id", "ap", "Label", "AUTOPILOT", "Kind", s.ContractEnum("GaugeKind", "Mode"), "Text", inZone ? "ORBIT" : "NAV", "Sub", "DEPOT 3",
            "Value", t * 0.06f % 1f, "Tone", s.ContractEnum("ScopeTone", "Ink")));

        // the ticker as the prototype's scenario has it: the page's own envelope notice, the module's zone and phase notices
        string chip, text, tone = "Info";
        float rs = Mathf.Sqrt((ownX + 3.9f) * (ownX + 3.9f) + (ownY - 0.7f) * (ownY - 0.7f)), rg = Mathf.Sqrt((ownX - 0.4f) * (ownX - 0.4f) + (ownY - 0.6f) * (ownY - 0.6f));
        if (t < 3.2f) { chip = "sam"; text = "IN SAM ENVELOPE - " + rs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KM"; tone = "Caution"; }
        else if (t < 4.8f) { chip = "contracts"; text = "TARGET NEUTRALIZED - FUEL DEPOT 3"; }
        else if (t < 5.9f) { chip = "gun"; text = "IN GUN ENVELOPE - " + rg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " KM"; tone = "Caution"; }
        else if (t < tLos) { chip = "contracts"; text = "ENTERING AREA - #7 DAMAGE ASSESSMENT"; }
        else { chip = "contracts"; text = "SURVEY  -  KEEP THE SITE IN VIEW"; }
        if (far) { chip = "contracts"; text = "JOIN  -  FLY TO THE MARKED AREA"; }
        if (bare) { chip = "contracts"; text = "INTERDICT  -  NEUTRALIZE THE MARKED TARGET"; }
        s.SetFrame("Ticker", s.Fill(s.Domain("TickerLine"), "Tone", s.ContractEnum("HudTone", tone), "Channel", chip, "Text", text, "Count", 1, "Age", 2f));
        s.SetFrame("HasTicker", true);
    }
}
#endif
