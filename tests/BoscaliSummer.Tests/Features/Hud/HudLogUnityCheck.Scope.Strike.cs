#if UNITY_EDITOR
using System;
using UnityEngine;

// STRIKE fixture: pages/strike.js, the 18 s loop. A KINETIC ROD (warned at T-15, released at T-8, lands at 12.0), a PRSM strike launched at 2.0 (a six-round salvo
// in the prototype, 0.9 km from us = DANGER CLOSE, first splash at 8.5) and a CRUISE strike already on its way (lands at 16.0); a RADAR bird's RECON PASS window
// open until 18.0 with four contacts; a CYBER intrusion hopping to its third node, holding, then hopping to the fourth, the trace burning on; the SAM NET perk
// ready, armed at 10.5 and used at 13.2. The numbers are what Support's provider would send at that moment of the scenario (the prototype's tracks, windows and
// perks restated as the feed's fields); the page draws only from them.
// Variants (1080p, t = 6): "solo" = one rod and nothing else (the track takes the room); "sat" = a PRSM and a SAT CAMERA window, an armed perk, no intrusion.
public static partial class HudLogUnityCheck
{
    private static object StrikeTrackOf(ScopeScene s, string kind, int id, float eta, float lead, float rangeM, float bearing, string target, int legs = 0, float radius = 420f) =>
        s.Fill(s.Contract("StrikeTrack"), "Id", id, "Kind", s.ContractEnum("StrikeKind", kind), "Eta", eta, "Lead", lead, "RangeM", rangeM, "BearingDeg", bearing, "Target", target,
            "Legs", legs, "RadiusM", radius);

    private static object PerkTileOf(ScopeScene s, string name, string front, string state, float cooldown, float total) =>
        s.Fill(s.Contract("PerkTile"), "Name", name, "Front", s.ContractEnum("PerkFront", front), "State", s.ContractEnum("PerkPhase", state), "Cooldown", cooldown, "Total", total);

    private static void StrikeVariant(float t, ScopeScene s)
    {
        bool solo = s.Variant == "solo";
        Array tracks = Array.CreateInstance(Type("BoscaliSummer.Core.Contracts.StrikeTrack"), 1);
        tracks.SetValue(solo ? StrikeTrackOf(s, "Rod", 1, 14f - t, 15f, 6400f, 47f, "GRID 12.4/-3.1") : StrikeTrackOf(s, "Prsm", 2, 12f - t, 14f, 4200f, 262f, "GRID -3.9/0.7"), 0);
        Array perks = Array.CreateInstance(Type("BoscaliSummer.Core.Contracts.PerkTile"), solo ? 1 : 2);
        perks.SetValue(solo ? PerkTileOf(s, "ROD", "Space", "Cooling", 300f - t, 300f) : PerkTileOf(s, "SAM NET", "Cyber", "Armed", 0f, 120f), 0);
        if (!solo) perks.SetValue(PerkTileOf(s, "PRSM", "Space", "Cooling", 60f - t, 60f), 1);
        object scope = s.Fill(s.Contract("StrikeScope"), "TrackCount", 1, "Tracks", tracks, "PerkCount", perks.Length, "Perks", perks,
            "SatOpen", !solo, "SatCamera", !solo, "SatLeft", 14f - t, "SatTotal", 20f, "SatContacts", 3,
            "HasAlloc", true, "Alloc", 26f, "RodCost", 30, "AllocCosts", new[] { 10f, 16f, 30f }, "AllocCostCount", 3, "HasBird", true, "BirdFuel", 0.22f);
        s.Publish(scope);
        s.SetFrame("Systems", s.Fill(s.Domain("SystemStates"), "Gear", s.DomainEnum("GearLamp", "Up"), "FlightAssist", true, "AutoHover", false,
            "RadarEmitting", true, "NightVision", false, "WeaponSafe", false, "EngineRunning", true));
        s.SetFrame("Ticker", s.Fill(s.Domain("TickerLine"), "Tone", s.ContractEnum("HudTone", "Info"), "Channel", "ops", "Text", solo ? "ROD RELEASED - 8 S TO IMPACT" : "SAT CAMERA - 3 CONTACTS", "Count", 1, "Age", 2f));
        s.SetFrame("HasTicker", true);
    }

    static partial void FixtureStrike(float t, ScopeScene s)
    {
        if (s.Variant != null) { StrikeVariant(t, s); return; }

        // the three strikes, in the order they were called
        Type track = Type("BoscaliSummer.Core.Contracts.StrikeTrack");
        Array tracks = Array.CreateInstance(track, 3);
        tracks.SetValue(StrikeTrackOf(s, "Rod", 1, 12f - t, 15f, 6400f, 47f, "GRID 12.4/-3.1"), 0);
        tracks.SetValue(StrikeTrackOf(s, "Prsm", 2, 8.5f - t, 6.5f, 900f, 312f, "GRID -0.6/0.9"), 1);
        tracks.SetValue(StrikeTrackOf(s, "Cruise", 3, 16f - t, 22f, 31000f, 12f, "GRID -8.2/30.4", legs: 2), 2);
        s.Expect(t < 20f, "The fixture's clock stays inside the loop");

        // perks: ROD cooling, SAM NET ready / armed / cooling, JTAC LASE finishing its cooldown
        float samCool = 120f - (t - 13.2f), jtac = 30f - (t + 26f);
        Array perks = Array.CreateInstance(Type("BoscaliSummer.Core.Contracts.PerkTile"), 3);
        perks.SetValue(PerkTileOf(s, "ROD", "Space", "Cooling", 300f - (t + 3f), 300f), 0);
        perks.SetValue(t < 10.5f ? PerkTileOf(s, "SAM NET", "Cyber", "Ready", 0f, 120f) : t < 13.2f ? PerkTileOf(s, "SAM NET", "Cyber", "Armed", 0f, 120f) : PerkTileOf(s, "SAM NET", "Cyber", "Cooling", samCool, 120f), 1);
        perks.SetValue(jtac > 0f ? PerkTileOf(s, "JTAC LASE", "Sof", "Cooling", jtac, 30f) : PerkTileOf(s, "JTAC LASE", "Sof", "Ready", 0f, 30f), 2);

        // the CYBER chain: UPL RLY RDR C2 DC, two held and hopping to the third, three held, then hopping to the fourth
        bool hopping = t < 7f || t >= 9.5f;
        int held = t < 7f ? 2 : 3;
        float hopStart = t < 7f ? -5f : 9.5f, hopEnd = t < 7f ? 7f : 21.5f;
        float trace = t < 7f ? 38f + 1.5f * t : t < 9.5f ? 48.5f + 0.5f * (t - 7f) : 49.75f + 1.5f * (t - 9.5f);
        Type kinds = Type("BoscaliSummer.Core.Contracts.ChainNodeKind");
        Array chain = Array.CreateInstance(kinds, 5);
        string[] order = { "Uplink", "Relay", "Radar", "SamC2", "DataCenter" };
        for (int i = 0; i < 5; i++) chain.SetValue(Enum.Parse(kinds, order[i]), i);

        float alloc = 42f - (t >= 2f ? 10f : 0f) - (t >= 13.2f ? 16f : 0f), sofLeft = Mathf.Max(0f, 52f - (t + 8f));
        object scope = s.Fill(s.Contract("StrikeScope"), "TrackCount", 3, "Tracks", tracks, "PerkCount", 3, "Perks", perks,
            "SatOpen", t < 18f, "SatCamera", false, "SatLeft", 18f - t, "SatTotal", 20f, "SatContacts", 4,
            "CyberLive", true, "CyberHopping", hopping, "ChainCount", held + (hopping ? 1 : 0), "ChainHeld", held, "Chain", chain,
            "HopProgress", hopping ? Mathf.Clamp01((t - hopStart) / (hopEnd - hopStart)) : 0f, "HopLeft", hopEnd - t, "Trace", trace, "TraceRate", (hopping ? 1.5f : 0f) + 0.5f * held,
            "HasAlloc", true, "Alloc", alloc, "RodCost", 30, "AllocCosts", new[] { 10f, 16f, 30f }, "AllocCostCount", 3,
            "HasBird", true, "BirdFuel", 0.71f - 0.004f * t,
            "HasSof", true, "SofLeft", sofLeft, "SofTotal", 52f, "SofName", "ALPHA", "SofMission", "RECON");
        s.Publish(scope);

        // lamps as the SYSTEMS fixture has them: FA lit, own radar emitting
        s.SetFrame("Systems", s.Fill(s.Domain("SystemStates"), "Gear", s.DomainEnum("GearLamp", "Up"), "FlightAssist", true, "AutoHover", false,
            "RadarEmitting", true, "NightVision", false, "WeaponSafe", false, "EngineRunning", true));

        // the ticker: what the strike page's own notices and the module notices put there at this moment
        string chip, text, tone = "Info";
        int count = 1;
        if (t < 2f) { chip = "ops"; text = "ROD WARN - RELEASE T-" + Mathf.Max(1, Mathf.CeilToInt(4f - t)) + " S"; }
        else if (t < 4f) { chip = "ops"; text = "PRSM LAUNCH - 0.9 KM"; tone = "Caution"; }
        else if (t < 4.9f) { chip = "ops"; text = "ROD RELEASED - 8 S TO IMPACT"; }
        else if (t < 7f) { chip = "ops"; text = "DANGER CLOSE - PRSM IMPACT T-" + Mathf.Max(1, Mathf.CeilToInt(8.5f - t)) + " S"; tone = "Warning"; }
        else if (t < 8.5f) { chip = "cyber"; text = "TRACE " + Mathf.RoundToInt(trace) + "% - HOLD OR BURN AT 100"; tone = trace >= 60f ? "Caution" : "Info"; }
        else if (t < 10.9f) { chip = "ops"; text = "PRSM IMPACT"; count = Mathf.Min(6, Mathf.FloorToInt((t - 8.5f) / 0.35f) + 1); }
        else if (t < 12f) { chip = "ops"; text = "SAM NET ARMED - PRESS AGAIN"; }
        else if (t < 13.2f) { chip = "ops"; text = "ROD IMPACT"; }
        else if (t < 15f) { chip = "ops"; text = "SAM NET DOWN - CD 2:00"; }
        else { chip = "cyber"; text = "TRACE " + Mathf.RoundToInt(trace) + "% - HOLD OR BURN AT 100"; }
        s.SetFrame("Ticker", s.Fill(s.Domain("TickerLine"), "Tone", s.ContractEnum("HudTone", tone), "Channel", chip, "Text", text, "Count", count, "Age", 2f));
        s.SetFrame("HasTicker", true);
    }
}
#endif
