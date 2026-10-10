#if UNITY_EDITOR
using System;
using UnityEngine;

// SYSTEMS fixture: pages/systems.js sim(t), the 16 s loop. Fuel drains through JOKER; a hit at 4.5 s (FUEL LEAK, STR 88%); engine 1 out at
// 8.5 s (STR 70%); fire 8.7 to 12.5 s; flares and chaff at 9 s; the jammer runs 9 to 11.5 s and recharges; BINGO at about 11 s while the
// RTB turn brings the base arrow to the nose; the aircraft passes through a cloud deck at 14 s. The default gauges are the prototype's
// (lance, perk, radio, ap); the other pages' pips and the ticker are drawn by their own fixtures / the notice that is live.
public static partial class HudLogUnityCheck
{
    private static float Seg01(float t, float a, float b) => Mathf.Clamp01((t - a) / (b - a));
    private static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

    static partial void FixtureSystems(float t, ScopeScene s)
    {
        const float hitAt = 4.5f, engAt = 8.5f, fire0 = 8.7f, fire1 = 12.5f;
        bool hit = t >= hitAt, eng = t >= engAt, fire = t >= fire0 && t < fire1;
        float fuel = 34f - 1.9f * Mathf.Min(t, hitAt);
        if (t > hitAt) fuel -= 2.4f * (Mathf.Min(t, engAt) - hitAt);
        if (t > engAt) fuel -= 1.8f * (t - engAt);
        fuel = Mathf.Max(1.2f, fuel);
        float dist = Mathf.Max(4f, 23f - 0.7f * t), need = dist * 0.45f + 4f, margin = fuel - need;
        float rate = !hit ? 2.2f : eng ? 4.6f : 5.8f;                      // % of the tanks per minute
        float hdg = 80f + 134f * Smooth(Seg01(t, 6f, 14f)), rel = (((214f - hdg) % 360f) + 360f) % 360f;
        int structure = t < hitAt ? 100 : t < engAt ? 88 : 70;
        float[] flares = { 9.0f, 9.15f, 9.3f, 9.45f }, chaff = { 9.2f, 9.5f };
        int Used(float[] a) { int n = 0; foreach (float x in a) if (t >= x) n++; return n; }
        float jo = Seg01(t, 9f, 11.5f);
        float jamCharge = t < 9f ? 1f : t < 11.5f ? 1f - 0.45f * jo : 0.55f + 0.45f * Seg01(t, 11.5f, 16f);
        bool jamOn = t >= 9f && t < 11.5f;
        float altitude = 5.0f - 0.07f * t;
        bool bingo = margin <= 0f, joker = margin > 0f && margin <= 15f;

        // The airframe: a twin jet. Damage zones are sides of the airframe seen from above (1 left, 2 centre, 4 right).
        object damage = s.Domain("DamageState");
        string flags = (eng ? "EngineDamaged, " : "") + (hit ? "FuelLeak, " : "") + (fire ? "Fire, " : "");
        s.Fill(damage, "Flags", s.DomainEnum("DamageFlags", flags.Length == 0 ? "None" : flags.TrimEnd(',', ' ')),
            "Engines", 2, "EnginesOut", eng ? 1 : 0, "DetachedRatio", (100 - structure) / 100f,
            "EngineSlots", (byte)5, "EngineOut", (byte)(eng ? 1 : 0), "Leak", (byte)(hit ? 1 : 0), "Fire", (byte)(fire ? 1 : 0), "WingLoss", (byte)(structure < 100 ? 1 : 0));

        object fuelState = s.Domain("FuelState");
        s.Fill(fuelState, "Valid", true, "Level", fuel / 100f, "EnduranceS", fuel / rate * 60f, "Bingo", bingo, "Joker", joker,
            "Field", "K92", "FieldM", dist * 1000f, "FieldRelDeg", rel, "HasNeed", true, "Need", need / 100f,
            "Tone", s.ContractEnum("HudTone", bingo || fuel <= 10f ? "Warning" : joker || fuel <= 25f ? "Caution" : "Info"));

        Array stations = s.DomainArray("CmStation", 4);
        object ir = s.Fill(s.Domain("CmStation"), "Tag", "IR", "Ammo", 48 - Used(flares), "Max", 48, "Mask", (byte)1);
        object rdr = s.Fill(s.Domain("CmStation"), "Tag", "RDR", "Ammo", 30 - Used(chaff), "Max", 30, "Mask", (byte)2);
        object jam = s.Fill(s.Domain("CmStation"), "Tag", "JAM", "Ammo", 1, "Max", 1, "Chargeable", true, "Mask", (byte)2, "Charge", jamCharge, "Active", jamOn);
        stations.SetValue(ir, 0); stations.SetValue(rdr, 1); stations.SetValue(jam, 2);

        object systems = s.Domain("SystemsScope");
        s.Fill(systems, "Rotary", s.Variant == "rotary", "Damage", damage, "Fuel", fuelState, "Cm", stations, "CmCount", 3,
            "HasWind", true, "WindFromDeg", 270f, "WindMps", 18f / 1.94384f, "HasCloud", true, "InCloud", altitude < 4.1f && altitude > 3.2f,
            "Units", NOAvionics.AvUnits.Metric, "G", 1f, "GLimit", 0f);
        s.Publish(systems);

        // Lamps: FA lit, own radar emitting (a caution); the ENG lamp is lit by the page when an engine is out.
        s.SetFrame("Systems", s.Fill(s.Domain("SystemStates"), "Gear", s.DomainEnum("GearLamp", "Up"), "FlightAssist", true, "AutoHover", false,
            "RadarEmitting", true, "NightVision", false, "WeaponSafe", false, "EngineRunning", true));

        // The ticker: what the module notices would put there at this moment of the scenario.
        string tone, chip, text; int count = 1;
        if (bingo) { tone = "Warning"; chip = "fuel"; text = "BINGO - RTB K92 - " + Mathf.RoundToInt(dist) + " KM"; }
        else if (fire) { tone = "Warning"; chip = "fire"; text = "FIRE - LEFT TANK"; }
        else if (eng) { tone = "Warning"; chip = "dmg"; text = "ENGINE FLAMEOUT"; }
        else if (hit) { tone = "Caution"; chip = "dmg"; text = "FUEL LEAK - LEFT TANK"; }
        else if (joker) { tone = "Caution"; chip = "fuel"; text = "JOKER - " + Mathf.RoundToInt(fuel) + "%"; }
        else { tone = "Info"; chip = "nav"; text = "RTB K92 HIGHWAY STRIP - " + Mathf.RoundToInt(dist) + " KM"; }
        object line = s.Fill(s.Domain("TickerLine"), "Tone", s.ContractEnum("HudTone", tone), "Channel", chip, "Text", text, "Count", count, "Age", 2f);
        s.SetFrame("Ticker", line);
        s.SetFrame("HasTicker", true);

        // The prototype's default gauges (HUD.defaultGauges).
        float lv = (float)(0.6 + 0.4 * ((t * 0.075) % 1.0)), cdLeft = (float)(45.0 - ((t * 3.1) % 45.0));   // double maths, as the prototype's JS
        object lance = s.Fill(s.Contract("GaugeSpec"), "Id", "lance", "Label", "LANCE", "Kind", s.ContractEnum("GaugeKind", "Arc"), "Value", lv, "Text", Mathf.RoundToInt(lv * 100f) + "%", "Sub", "RG-12",
            "Tone", s.ContractEnum("ScopeTone", "Ink"), "Ready", lv > 0.98f);
        object perk = s.Fill(s.Contract("GaugeSpec"), "Id", "perk", "Label", "PERK", "Kind", s.ContractEnum("GaugeKind", "Ring"), "Value", 1f - cdLeft / 45f, "Inner", Mathf.CeilToInt(cdLeft).ToString(),
            "Text", "KINETIC", "Sub", "SPACE", "Tone", s.ContractEnum("ScopeTone", "Dim"));
        bool fade = t % 9f > 6.5f;
        object radio = s.Fill(s.Contract("GaugeSpec"), "Id", "radio", "Label", "RADIO", "Kind", s.ContractEnum("GaugeKind", "Bars"), "Value", 0.75f - 0.25f * (fade ? 1f : 0f), "Text", "CHAPEL",
            "Sub", fade ? "SIG 2/4" : "SIG 3/4", "Tone", s.ContractEnum("ScopeTone", "Ink"));
        object ap = s.Fill(s.Contract("GaugeSpec"), "Id", "ap", "Label", "AUTOPILOT", "Kind", s.ContractEnum("GaugeKind", "Mode"), "Text", "NAV", "Sub", "K92 STRIP",
            "Value", (t * 0.5f) % 1f, "Tone", s.ContractEnum("ScopeTone", "Ink"));
        s.Gauge("lance", lance); s.Gauge("perk", perk); s.Gauge("radio", radio); s.Gauge("ap", ap);
    }

}
#endif
