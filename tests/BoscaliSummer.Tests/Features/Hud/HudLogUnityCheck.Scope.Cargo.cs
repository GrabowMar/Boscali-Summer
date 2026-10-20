#if UNITY_EDITOR
using System;
using UnityEngine;
using static UnityCheckHarness;

// CARGO fixture: a HALO run. t 2: safety on, the zone 14 km off the right of the nose, out of reach; t 6: safety off, still short (IN REACH IN ...); t 10: inside the reach, GREEN; t 14: two
// fireteams gone. A HAMILCAR rides along on STA 4. Variants: nozone (zone-capable, nothing set: AMBER), glider (the HAMILCAR focused, a long glide reach), native (crates on the game's own release),
// far (the zone 80 km behind: it rides the rim). Reach is the production rule's numbers (LandingZoneReach: HALO 2.5 x (height - 150) + 150, glider 15 x height).
public static partial class HudLogUnityCheck
{
    private static float HaloReach(float h) => h < 400f ? 300f : 2.5f * Mathf.Max(0f, h - 150f) + 150f;

    static partial void FixtureCargo(float t, ScopeScene s)
    {
        Array loads = s.DomainArray("CargoLoad", 4);
        string kinds = "BoscaliSummer.Core.Contracts.CargoKind";
        object Kind(string k) => Enum.Parse(Type(kinds), k);
        int jumpers = t >= 13f ? 8 : 16;
        bool glider = s.Variant == "glider", native = s.Variant == "native";
        int n = 0;
        if (native)
        {
            loads.SetValue(s.Fill(s.Domain("CargoLoad"), "Kind", Kind("Cargo"), "Name", "SUPPLY CRATES", "Station", 2, "Ammo", 2, "Max", 4, "Selected", true), n++);
            loads.SetValue(s.Fill(s.Domain("CargoLoad"), "Kind", Kind("Troops"), "Name", "RIFLE SQUAD", "Station", 5, "Ammo", 0, "Max", 8, "Selected", false), n++);
        }
        else
        {
            loads.SetValue(s.Fill(s.Domain("CargoLoad"), "Kind", Kind("Paratroopers"), "Name", "PARATROOPERS", "Station", 3, "Ammo", jumpers, "Max", 16, "Selected", !glider), n++);
            loads.SetValue(s.Fill(s.Domain("CargoLoad"), "Kind", Kind("Glider"), "Name", "HAMILCAR", "Station", 4, "Ammo", 1, "Max", 1, "Selected", glider), n++);
        }

        float height = glider ? 2500f : 3200f;
        float distance = s.Variant == "far" ? 80000f : t < 4f ? 14000f : t < 8f ? 11000f : t < 12f ? 7000f : 4000f;
        float bearing = s.Variant == "far" ? 150f : t < 4f ? 35f : t < 8f ? 12f : t < 12f ? 3f : -20f;
        object d = s.Domain("CargoScope");
        if (native)
            s.Fill(d, "Kind", Kind("Cargo"), "Name", "SUPPLY CRATES", "Station", 2, "Ammo", 2, "Max", 4, "Cargo", 2, "Troops", 0, "Selected", true, "Safe", false,
                "UsesLandingZone", false, "ZoneAvailable", true);
        else
        {
            s.Fill(d, "Kind", Kind(glider ? "Glider" : "Paratroopers"), "Name", glider ? "HAMILCAR" : "PARATROOPERS", "Station", glider ? 4 : 3, "Ammo", glider ? 1 : jumpers,
                "Max", glider ? 1 : 16, "Troops", jumpers, "Gliders", 1, "Selected", true, "Safe", !glider && t < 4f, "UsesLandingZone", true, "ZoneAvailable", true,
                "HasZone", s.Variant != "nozone", "HasReach", true, "DistanceM", glider ? 30000f : distance, "ReachM", glider ? Mathf.Min(15f * height, 45000f) : HaloReach(height),
                "BearingDeg", glider ? -8f : bearing, "HeightM", height, "GroundMps", 118f, "ClosingMps", s.Variant == "far" ? -100f : 115f);
        }
        s.Fill(d, "Loads", loads, "LoadCount", n);
        s.Publish(d);
        s.SetFrame("Systems", s.Fill(s.Domain("SystemStates"), "Gear", s.DomainEnum("GearLamp", "Up"), "FlightAssist", true, "AutoHover", false,
            "RadarEmitting", false, "NightVision", false, "WeaponSafe", !glider && !native && t < 4f, "EngineRunning", true));
    }
}
#endif
