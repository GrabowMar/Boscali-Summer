#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

// The STORES HEADER's fixtures (takeover/stores.js and every page's stores(t) hook in takeover/pages/*.js): the default CI-22 CRICKET loadout (a gun with 500 rounds, two AAM-4,
// two AGM-48, a GBU-12, a MALD-X kit, an AAM-1 DART; 48 flares, 30 chaff, a jammer, a 500 kJ capacitor) and what each page's scenario does to it. The header is drawn on every page's shot
// (RenderScope calls StoresFixture after the page's own fixture), and on its own over the cruise strip for the variants of the "stores" page. Fixtures are the ONLY place scripted data lives.
public static partial class HudLogUnityCheck
{
    private struct LoadStation
    {
        public readonly string Cls, Name;
        public readonly int Max;
        public LoadStation(string cls, string name, int max) { Cls = cls; Name = name; Max = max; }
    }

    private static LoadStation[] CricketLoad() => new[]
    {
        new LoadStation("Gun", "20MM ROTARY CANNON", 500), new LoadStation("Aam", "AAM-4 SKYWAY", 1), new LoadStation("Aam", "AAM-4 SKYWAY", 1),
        new LoadStation("Agm", "AGM-48", 1), new LoadStation("Agm", "AGM-48", 1), new LoadStation("Bomb", "GBU-12", 1), new LoadStation("Kit", "MALD-X", 1),
        new LoadStation("Aam", "AAM-1 DART", 1),
    };

    /// <summary>What the stores feed says at one moment of a scenario (the prototype's <c>stores(t)</c> overrides over its defaults).</summary>
    private sealed class StoresSpec
    {
        public LoadStation[] Load = CricketLoad();
        public readonly Dictionary<int, int> Rounds = new Dictionary<int, int>();
        public int Sel;
        public bool Safe, Link = true, Trt;
        public int ReloadAt = -1, ReloadAt2 = -1;
        public float ReloadLeft, ReloadTotal = 1f, Reload2Left = 1f, Reload2Total = 1f;
        public int FiredAt = -1;
        public float FiredAge = 99f;
        public bool HasIr = true, HasRdr = true, HasJam = true;
        public int Ir = 48, IrMax = 48, Rdr = 30, RdrMax = 30, Active;       // Active: 0 = the flares are selected, 1 = the chaff
        public bool JamOn;
        public float JamCharge = 1f;
        public bool Cap = true;
        public float Kj = 500f, Rate;
        public byte Call;
    }

    private static object BuildStores(ScopeScene s, StoresSpec x)
    {
        Array stations = s.DomainArray("StoreStation", 12);
        int slot = 0;
        for (int i = 0; i < x.Load.Length; i++)
        {
            LoadStation st = x.Load[i];
            int n = x.Rounds.TryGetValue(i, out int r) ? r : st.Max;
            bool rel = x.ReloadAt == i || x.ReloadAt2 == i;
            float left = x.ReloadAt == i ? x.ReloadLeft : x.Reload2Left, total = x.ReloadAt == i ? x.ReloadTotal : x.Reload2Total;
            stations.SetValue(s.Fill(s.Domain("StoreStation"), "Class", s.DomainEnum("StoreClass", st.Cls), "Name", st.Name, "Ammo", n, "Max", st.Max, "Slot", st.Cls == "Gun" ? 0 : ++slot,
                "Reloading", rel, "ReloadElapsed", rel ? 1f - left / total : float.NaN, "ReloadLeftS", rel ? left : float.NaN, "FiredAgeS", x.FiredAt == i ? x.FiredAge : 99f), i);
        }
        Array cm = BuildCm(s, x, out int c);
        return s.Fill(s.Domain("StoresScope"), "Stations", stations, "StationCount", x.Load.Length, "Selected", x.Sel, "Safe", x.Safe, "Link", x.Link, "TurretAuto", x.Trt,
            "Cm", cm, "CmCount", c, "Call", x.Call, "HasCap", x.Cap, "CapKj", x.Kj, "CapMaxKj", 500f, "CapRate", x.Rate);
    }

    /// <summary>The countermeasure stations of the spec (flares, chaff, a jammer), the same array the production frame carries.</summary>
    private static Array BuildCm(ScopeScene s, StoresSpec x, out int count)
    {
        Array cm = s.DomainArray("CmStation", 4);
        int c = 0;
        if (x.HasIr) cm.SetValue(s.Fill(s.Domain("CmStation"), "Tag", "IR", "Ammo", x.Ir, "Max", x.IrMax, "Mask", (byte)1, "Selected", x.Active == 0), c++);
        if (x.HasRdr) cm.SetValue(s.Fill(s.Domain("CmStation"), "Tag", "RDR", "Ammo", x.Rdr, "Max", x.RdrMax, "Mask", (byte)2, "Selected", x.Active == 1), c++);
        if (x.HasJam) cm.SetValue(s.Fill(s.Domain("CmStation"), "Tag", "JAM", "Ammo", 1, "Max", 1, "Chargeable", true, "Mask", (byte)2, "Charge", x.JamCharge, "Active", x.JamOn), c++);
        count = c;
        return cm;
    }

    /// <summary>The earlier moments of a page's scenario its header (and the page) must have seen before <paramref name="t"/>.</summary>
    private static float[] PreRoll(string page, float t)
    {
        if (page != "threat") return new float[0];
        var list = new List<float>();
        foreach (float before in new[] { 0.9f, 8.5f }) if (t > before + 0.05f) list.Add(before);
        return list.ToArray();
    }

    private static float CapKjSystems(float t) => t < 9f ? 500f : t < 11.5f ? 500f - 14f * (t - 9f) : Mathf.Min(500f, 465f + 30f * (t - 11.5f));

    private static float CapKjThreat(float t) => t < 3f ? 500f : t < 8.4f ? 500f - 14f * (t - 3f) : Mathf.Min(500f, 500f - 14f * 5.4f + 30f * (t - 8.4f));

    /// <summary>The stores feed of <paramref name="page"/> at <paramref name="t"/>, published like the production provider does (<c>ScopeFeeds.Publish</c>).</summary>
    private static void StoresFixture(string page, float t, ScopeScene s) => s.Publish(BuildStores(s, StoresSpecFor(page, t, s.Variant)));

    /// <summary>What the page's scenario does to the default loadout at <paramref name="t"/> (the "stores" page: what its <paramref name="variant"/> changes).</summary>
    private static StoresSpec StoresSpecFor(string page, float t, string variant)
    {
        var x = new StoresSpec();
        float Rate(Func<float, float> kj) => (kj(t + 0.25f) - kj(t - 0.25f)) / 0.5f;
        switch (page)
        {
            case "systems":
                {
                    float[] flares = { 9.0f, 9.15f, 9.3f, 9.45f }, chaff = { 9.2f, 9.5f };
                    int used = 0, usedChaff = 0;
                    foreach (float f in flares) if (t >= f) used++;
                    foreach (float c in chaff) if (t >= c) usedChaff++;
                    x.Ir = 48 - used; x.Rdr = 30 - usedChaff;
                    bool jamOn = t >= 9f && t < 11.5f;
                    x.JamOn = jamOn;
                    x.JamCharge = t < 9f ? 1f : t < 11.5f ? 1f - 0.45f * Seg01(t, 9f, 11.5f) : 0.55f + 0.45f * Seg01(t, 11.5f, 16f);
                    x.Kj = CapKjSystems(t); x.Rate = Rate(CapKjSystems);
                    break;
                }
            case "threat":
                {
                    float[] chaff = { 6.6f, 6.9f, 7.2f, 7.5f, 7.8f }, flares = { 9.2f, 9.5f, 9.8f, 10.1f, 10.4f };
                    int usedChaff = 0, used = 0;
                    foreach (float c in chaff) if (t >= c) usedChaff++;
                    foreach (float f in flares) if (t >= f) used++;
                    x.Trt = true; x.Ir = 48 - used; x.Rdr = 30 - usedChaff;
                    x.Active = t >= 4.8f && t < 8.4f ? 1 : 0;
                    bool jamOn = t >= 3f && t < 8.4f;
                    x.JamOn = jamOn;
                    x.JamCharge = t < 3f ? 1f : t < 8.4f ? 1f - 0.5f * Seg01(t, 3f, 8.4f) : 0.5f + 0.5f * Seg01(t, 8.4f, 16f);
                    x.Kj = CapKjThreat(t); x.Rate = Rate(CapKjThreat);
                    break;
                }
            case "shot":          // the weapon cell follows the selected station, rounds step down at every launch and the fired station's pip drains (0.8 AAM-4 stn 1, 3.6 DART stn 7, 6.0 GBU-12 stn 5)
                {
                    float[][] launches = { new[] { 0.8f, 1f }, new[] { 3.6f, 7f }, new[] { 6.0f, 5f } };
                    x.Rounds[7] = t >= 3.6f ? 0 : 1;
                    foreach (float[] l in launches)
                        if (t >= l[0])
                        {
                            if (l[1] != 7f) x.Rounds[(int)l[1]] = 0;
                            if (t - l[0] < 1.5f) { x.FiredAt = (int)l[1]; x.FiredAge = t - l[0]; }
                        }
                    x.Sel = t < 1.8f ? 1 : t < 2.9f ? 2 : t < 4.6f ? 7 : t < 7.3f ? 5 : 2;
                    break;
                }
            case "missilecam":    // the cam rides the AAM-4 off station 1: that rail is spent, the next one is selected
                x.Sel = 2; x.Rounds[1] = 0;
                break;
            case "approach":      // landing = master arm SAFE; the end of the sortie, so the dispensers show their low states
                x.Safe = true; x.Ir = 9; x.Rdr = 0;
                break;
            case "wing":          // the wingmen engage air targets: AAM-4 selected
                x.Sel = 1;
                break;
            case "objective":     // a strafing burst on the fuel depot 1.7 -> 3.2 s, then the cannon reloads 3.3 -> 5.4 s
                {
                    x.Rounds[0] = 500 - Mathf.RoundToInt(128f * Seg01(t, 1.7f, 3.2f));
                    if (t >= 1.7f && t < 3.2f) { x.FiredAt = 0; x.FiredAge = 0.2f; }
                    if (t >= 3.3f && t < 5.4f) { x.ReloadAt = 0; x.ReloadLeft = 5.4f - t; x.ReloadTotal = 2.1f; }
                    break;
                }
            case "dock":          // a foreign loadout (SKYWELL rearm): six AAM-4 rails, master arm SAFE from ENGAGE on; each transferred round loads the next rail
                {
                    var load = new List<LoadStation> { new LoadStation("Gun", "20MM ROTARY CANNON", 500) };
                    for (int i = 0; i < 6; i++) load.Add(new LoadStation("Aam", "AAM-4 SKYWAY", 1));
                    load.Add(new LoadStation("Pod", "HYDRA-70 POD", 38));
                    x.Load = load.ToArray();
                    x.Sel = 1; x.Safe = t >= 6.3f;
                    float[] at = { 11.9f, 12.5f, 13.1f, 13.6f };
                    x.Rounds[1] = 1; x.Rounds[2] = 1;
                    for (int j = 0; j < at.Length; j++)
                    {
                        x.Rounds[3 + j] = t >= at[j] ? 1 : 0;
                        if (t >= at[j] - 0.6f && t < at[j]) { x.ReloadAt = 3 + j; x.ReloadLeft = at[j] - t; x.ReloadTotal = 0.6f; }
                    }
                    break;
                }
        }
        if (page == "stores") StoresVariant(x, variant);
        return x;
    }

    /// <summary>The header alone: what each variant of the "stores" page changes in the default loadout.</summary>
    private static void StoresVariant(StoresSpec x, string variant)
    {
        switch (variant)
        {
            case "low":           // the gun at a fifth, the flares low, the chaff gone: LOW tag, amber figure and silhouette, LOW and EMPTY bands
                x.Rounds[0] = 90; x.Ir = 9; x.Rdr = 0;
                break;
            case "empty":         // both AAM-4 spent, one selected: EMPTY tab, the pips hollow
                x.Sel = 1; x.Rounds[1] = 0; x.Rounds[2] = 0;
                break;
            case "reload":        // the gun reloads (RELOAD tab, the pip fills amber) while the AGM on station 3 is also reloading (the amber call in the name row)
                x.Rounds[0] = 0; x.ReloadAt = 0; x.ReloadLeft = 2.1f; x.ReloadTotal = 3.3f; x.ReloadAt2 = 3; x.Reload2Left = 1.4f; x.Reload2Total = 3f; x.Rounds[3] = 0;
                break;
            case "nocap":         // an airframe without a capacitor: the row is gone, the header is 27 units shorter
                x.Cap = false; x.HasJam = false;
                break;
            case "dispense":      // the THREAT page calls for flares: the IR dispenser pulses DISPENSE; the chaff is the selected one
                x.Call = 1; x.Active = 1; x.Sel = 1; x.Safe = false; x.Kj = 431f; x.Rate = -14f; x.JamOn = true; x.JamCharge = 0.7f;
                break;
            case "rotary":        // a helicopter: a gun pod and a rocket pod, one dispenser of flares and no capacitor
                x.Load = new[] { new LoadStation("Gun", "7.62MM DOOR GUN", 600), new LoadStation("Pod", "HYDRA-70 POD", 38), new LoadStation("Pod", "HYDRA-70 POD", 38), new LoadStation("Agm", "AGM-48", 1) };
                x.Sel = 1; x.Trt = true; x.HasRdr = false; x.HasJam = false; x.Cap = false; x.Ir = 60; x.IrMax = 60; x.Rounds[0] = 372 + 100;
                break;
            case "gunonly":       // a trainer: one gun, no countermeasures at all, no capacitor
                x.Load = new[] { new LoadStation("Gun", "20MM ROTARY CANNON", 500) };
                x.HasIr = x.HasRdr = x.HasJam = false; x.Cap = false; x.Rounds[0] = 372;
                break;
        }
    }

    static partial void FixtureStores(float t, ScopeScene s)
    {
        FixtureSystems(2f, s);      // the cruise strip under the header
    }
}
#endif
