#if UNITY_EDITOR
using UnityEngine;

// DOCK fixture: pages/dock.js sim(t), the 15 s loop. Closure 28 m/s (TOO FAST caution) -> CAPTURE at 90 m (4.7 s) -> ENGAGE at 60 m (6.3 s, HOLD BRAKE) -> contact 11.2 s
// -> TRANSFER 11.4-13.6 (fuel 34 -> 100 %, rounds 2 -> 6) -> SERVED 13.8 (RELEASE BRAKE). Range, closure and time to contact agree (the range is the closure's integral).
// The prototype fills the fuel gradually; in the game the fuel and rounds move in one step at the transfer's end, so the page holds the real value: the fixture keeps the
// prototype's gradual fill only to compare the look.
public static partial class HudLogUnityCheck
{
    private static readonly float[,] DockClosureKeys = { { 0f, 28f }, { 1.2f, 28f }, { 2.6f, 20f }, { 6.4f, 17f }, { 8.6f, 17f }, { 10.4f, 3f }, { 11.2f, 0.4f }, { 15f, 0f } };

    private static float DockClosure(float t)
    {
        for (int i = 0; i < DockClosureKeys.GetLength(0) - 1; i++)
            if (t <= DockClosureKeys[i + 1, 0]) return Mathf.Lerp(DockClosureKeys[i, 1], DockClosureKeys[i + 1, 1], Mathf.Clamp01((t - DockClosureKeys[i, 0]) / (DockClosureKeys[i + 1, 0] - DockClosureKeys[i, 0])));
        return 0f;
    }

    /// <summary>Range to contact: the closure's integral from here to the contact at 11.2 s, so range and closure agree (the prototype's table).</summary>
    private static float DockRange(float t)
    {
        const float dt = 0.05f, contact = 11.2f;
        float total = 0f, upTo = 0f;
        for (float x = dt; x <= contact + 1e-3f; x += dt) total += DockClosure(x - dt / 2f) * dt;
        for (float x = dt; x <= Mathf.Min(t, contact) + 1e-3f; x += dt) upTo += DockClosure(x - dt / 2f) * dt;
        return Mathf.Max(0f, total - upTo);
    }

    private static float DockEaseOut(float u) { u = 1f - Mathf.Clamp01(u); return 1f - u * u * u; }

    static partial void FixtureDock(float t, ScopeScene s)
    {
        const float tCap = 4.7f, tEng = 6.3f, tContact = 11.2f, tX0 = 11.4f, tX1 = 13.6f, tServed = 13.8f;
        float r = t >= tContact ? 0f : DockRange(t), c = DockClosure(t);
        float e = 1f - Seg01(t, 9f, 11f);
        float ex = -21f * (1f - DockEaseOut(Seg01(t, 0.2f, 5.5f))) + 0.9f * Mathf.Sin(t * 1.9f) * Seg01(t, 2f, 4f) * e;
        float ey = 13f * (1f - DockEaseOut(Seg01(t, 0.4f, 6f))) + 0.7f * Mathf.Sin(t * 2.3f + 1f) * Seg01(t, 2f, 4f) * e;
        int phase = t >= tServed ? 4 : t >= tX0 ? 3 : t >= tEng ? 2 : t >= tCap ? 1 : 0;
        float fx = Smooth(Seg01(t, tX0, tX1));
        float[] arrive = { 11.9f, 12.5f, 13.1f, 13.6f };
        int rounds = 0;
        foreach (float x in arrive) if (t >= x) rounds++;
        float fuelKg = Mathf.Round(1840f * fx / 10f) * 10f;

        object d = s.Contract("DockScope");
        s.Fill(d, "Phase", s.ContractEnum("DockPhase", new[] { "Cue", "Capture", "Engage", "Transfer", "Served" }[phase]), "Docked", phase >= 2, "Tanker", "SKYWELL 2",
            "RelX", ex, "RelY", ey, "RelZ", -r, "RangeM", r, "ClosureMps", c, "CueM", 800f, "CaptureM", 90f, "EngageM", 60f, "MaxClosingMps", 30f,
            "Brake", t >= tEng - 0.2f && t < tServed, "Fuel", 0.34f + 0.66f * fx, "FuelTarget", 1f, "FuelNeedKg", 1840f * (1f - fx), "Ammo", 2 + rounds, "AmmoFull", 6,
            "TransferT", fx, "MovedFuelKg", 1840f, "MovedRounds", 4, "HasStock", true, "StockFuelKg", 6200f - fuelKg, "StockFuelMaxKg", 8000f, "StockMunKg", 1500f);
        if (s.Variant == "imperial") s.SetFrame("Units", NOAvionics.AvUnits.Imperial);     // add ("dock", 6f, "imperial") to ScopeVariants to render it
        if (s.Variant == "service-zone")
        {
            s.Fill(d, "ServiceZone", true, "ZoneReady", true, "JobName", "HANDOFF STA 5", "StockRounds", 4,
                "Phase", s.ContractEnum("DockPhase", t < 11f ? "Engage" : t < 13.8f ? "Transfer" : "Served"),
                "ClosureMps", 2f, "RelX", 8f, "RelY", -3f, "RelZ", -12f, "RangeM", 15f, "CaptureM", 120f, "EngageM", 120f);
        }
        s.Publish(d);

        s.SetFrame("Systems", s.Fill(s.Domain("SystemStates"), "Gear", s.DomainEnum("GearLamp", "Up"), "FlightAssist", true, "AutoHover", false,
            "RadarEmitting", true, "NightVision", false, "WeaponSafe", t >= tEng, "EngineRunning", true));

        string tone = "Info", text;
        bool fast = c >= 25f && r > 0f;
        string[] word = { "CUE", "CAPTURE", "ENGAGE", "TRANSFER", "SERVED" };
        float err = Mathf.Sqrt(ex * ex + ey * ey), cone = Mathf.Clamp(0.12f * r, 6f, 90f);
        float ttc = c > 0.8f && r > 0f ? r / c : 0f;
        if (fast) { tone = "Caution"; text = "CLOSURE " + Mathf.RoundToInt(c) + " M/S - AFT, MAX 30"; }
        else if (phase == 4) text = "SERVED +1 840 KG +4 AAM - BREAK AWAY";
        else if (phase == 3) text = "TRANSFER - FUEL " + Mathf.RoundToInt((0.34f + 0.66f * fx) * 100f) + "% - AAM " + (2 + rounds) + "/6";
        else if (phase == 2) text = "ENGAGED - HOLD BRAKE - CONTACT " + Mathf.Max(1, Mathf.RoundToInt(ttc)) + " S";
        else if ((err > 0.75f * cone && r > 30f) || err > cone) { tone = "Caution"; text = "NEAR FUNNEL EDGE - " + (ex < 0f ? "RIGHT " : "LEFT ") + Mathf.RoundToInt(Mathf.Abs(ex)) + " M"; }
        else text = "SKYWELL 2 - " + word[phase] + " - " + Mathf.RoundToInt(r) + " M";
        s.SetFrame("Ticker", s.Fill(s.Domain("TickerLine"), "Tone", s.ContractEnum("HudTone", tone), "Channel", "skywell", "Text", text, "Count", 1, "Age", 2f));
        s.SetFrame("HasTicker", true);

        float cdLeft = (float)(45.0 - ((t * 3.1) % 45.0));
        s.Gauge("radio", s.Fill(s.Contract("GaugeSpec"), "Id", "radio", "Label", "RADIO", "Kind", s.ContractEnum("GaugeKind", "Bars"), "Value", 1f, "Text", "SKYWELL", "Sub", "SIG 4/4", "Tone", s.ContractEnum("ScopeTone", "Ink")));
        s.Gauge("ace", s.Fill(s.Contract("GaugeSpec"), "Id", "ace", "Label", "ACE HUNT", "Kind", s.ContractEnum("GaugeKind", "Needle"), "Angle", (140f + 5f * t) % 360f, "Text", "VOSS",
            "Sub", Mathf.RoundToInt(38f - 0.4f * t) + "KM", "Tone", s.ContractEnum("ScopeTone", "Ink")));
        s.Gauge("perk", s.Fill(s.Contract("GaugeSpec"), "Id", "perk", "Label", "PERK", "Kind", s.ContractEnum("GaugeKind", "Ring"), "Value", 1f - cdLeft / 45f, "Inner", Mathf.CeilToInt(cdLeft).ToString(),
            "Text", "KINETIC", "Sub", "SPACE", "Tone", s.ContractEnum("ScopeTone", "Dim")));
    }
}
#endif
