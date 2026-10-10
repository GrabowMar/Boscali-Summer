using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// All-station countermeasure counts for the board's CM tiles (2026-10-10): flares, chaff and jammers with their IR /
    /// RDR / JAM tag, rounds left and full load, grouped by display name exactly as the game groups its stations. Vanilla
    /// shows only the ACTIVE type. The station list lives in a private nested class, so this reads the public
    /// <see cref="Countermeasure"/> components instead: the grouping is rescanned every few seconds (pods come and go),
    /// the rounds are summed every tick. Read-only: never selects a station (<c>ChooseCountermeasure</c> would change the
    /// pilot's active one).
    /// </summary>
    internal sealed class CountermeasureWatch
    {
        private const float RescanSeconds = 5f;

        private static readonly Comparison<Countermeasure> ByName = (a, b) =>
            a == null || b == null ? (a == null ? (b == null ? 0 : 1) : -1) : string.CompareOrdinal(a.displayName, b.displayName);

        private static readonly AccessTools.FieldRef<RadarJammer, float> JamIntensity = NativeRefs.Field<RadarJammer, float>("jammingIntensity");
        /// <summary>A jammer is jamming while its intensity is above this.</summary>
        private const float JamOn = 0.01f;

        private readonly CmStation[] stations = new CmStation[HudCm.MaxStations];
        private readonly string[] names = new string[HudCm.MaxStations];
        private Aircraft watched;
        private float nextScan;
        private Countermeasure[] parts = Array.Empty<Countermeasure>();
        private int[] partStation = Array.Empty<int>();
        private int stationCount;

        public void Read(Aircraft aircraft, BoardFrame frame)
        {
            frame.CmCount = 0;
            if (aircraft == null)
            {
                watched = null;
                stationCount = 0;
                return;
            }
            float now = Time.unscaledTime;
            if (aircraft != watched || now >= nextScan) Scan(aircraft, now);
            for (int i = 0; i < stationCount; i++)
            {
                frame.Cm[i] = stations[i];
                frame.Cm[i].Ammo = 0;
                frame.Cm[i].Active = false;
            }
            for (int i = 0; i < parts.Length; i++)
            {
                if (partStation[i] < 0 || parts[i] == null) continue;
                frame.Cm[partStation[i]].Ammo += parts[i].ammo;
                if (parts[i] is RadarJammer jammer && NativeRefs.Get(JamIntensity, jammer) > JamOn) frame.Cm[partStation[i]].Active = true;
            }
            // A jammer's rounds are one charge; its readiness is the aircraft's capacitor.
            float charge = Mathf.Clamp01(aircraft.GetPowerSupply() != null ? aircraft.GetPowerSupply().GetCharge() : 0f);
            for (int i = 0; i < stationCount; i++)
                if (frame.Cm[i].Chargeable) frame.Cm[i].Charge = charge;
            frame.CmCount = stationCount;
        }

        private void Scan(Aircraft aircraft, float now)
        {
            watched = aircraft;
            nextScan = now + RescanSeconds;
            parts = aircraft.GetComponentsInChildren<Countermeasure>(true);
            Array.Sort(parts, ByName);
            if (partStation.Length < parts.Length) partStation = new int[parts.Length];
            stationCount = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                partStation[i] = -1;
                Countermeasure part = parts[i];
                if (part == null) continue;
                int s = stationCount - 1;
                if (s < 0 || names[s] != part.displayName)
                {
                    if (stationCount == HudCm.MaxStations) continue;
                    s = stationCount++;
                    names[s] = part.displayName;
                    stations[s] = default;
                }
                stations[s].Max += MaxOf(part);
                stations[s].Chargeable |= part.chargeable;
                stations[s].Mask |= MaskOf(part.GetThreatTypes());
                partStation[i] = s;
            }
            for (int s = 0; s < stationCount; s++)
                stations[s].Tag = HudCm.TagOf(stations[s].Mask, stations[s].Chargeable, names[s]);
        }

        /// <summary>The full load: the ejectors remember theirs; a jammer is one charge; anything else is unknown (0).</summary>
        private static int MaxOf(Countermeasure part) =>
            part is FlareEjector flares ? flares.GetMaxAmmo()
            : part is ChaffEjector chaff ? chaff.GetMaxAmmo()
            : part.chargeable ? 1 : 0;

        private static byte MaskOf(List<string> threatTypes)
        {
            bool ir = false, radar = false;
            for (int i = 0; threatTypes != null && i < threatTypes.Count; i++)
            {
                string t = threatTypes[i];
                if (t == "IR") ir = true;
                else if (t == "ARH" || t == "SARH") radar = true;
            }
            return HudCm.MaskOf(ir, radar);
        }
    }
}
