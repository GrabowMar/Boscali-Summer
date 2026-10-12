using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// What the local faction knows about a clicked map point, as ready-to-print rows for the
    /// context card. Every figure comes from the faction's own picture (control field, Intel
    /// threat picture) or the player's own aircraft; one that could not be established reads as
    /// a dash, and a service that is not installed adds no row.
    /// </summary>
    internal static class MapContextRead
    {
        internal struct Row { internal string Key, Value; internal Color Tone; }

        /// <summary>Height the air-defence read assumes when the player is not flying.</summary>
        private const float DefaultAgl = 1000f;
        private const float ReconRadius = 1500f;

        /// <summary>Fills <paramref name="rows"/> and returns the tone of the ground's holder for the card's rail.</summary>
        internal static Color Fill(DynamicMap map, GlobalPosition ground, GlobalPosition? mark, List<Row> rows)
        {
            Color ink = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            Color muted = AvStyleHost.FuiColor("muted", AvTheme.Dim);
            Color danger = AvStyleHost.FuiColor("danger", AvTheme.Alert);
            Color ready = AvStyleHost.FuiColor("ready", AvTheme.Accent);
            rows.Clear();

            FactionHQ hq = map != null ? map.HQ : null;
            int observer = hq != null ? hq.GetInstanceID() : 0;
            float strength = 0f;
            bool read = hq != null && ModuleServices.TryGet(out ITerritoryIngress territory) &&
                territory.TryGetHoldStrength(observer, ground.x, ground.z, out strength);
            Color tone;
            string holder;
            switch (MapFix.Classify(read, strength))
            {
                case MapFix.Hold.Friendly: holder = "FRIENDLY HELD"; tone = AvStyleHost.FuiColor("friendly", AvTheme.Friendly); break;
                case MapFix.Hold.Hostile: holder = "HOSTILE HELD"; tone = danger; break;
                case MapFix.Hold.Contested: holder = "CONTESTED  ·  FRONT LINE"; tone = AvStyleHost.FuiColor("caution", AvTheme.Warning); break;
                case MapFix.Hold.Unclaimed: holder = "UNCLAIMED"; tone = muted; break;
                default: holder = "—"; tone = muted; break;
            }
            rows.Add(new Row { Key = "GROUND", Value = holder, Tone = tone });

            string fix = "X " + AvNum.Fixed(ground.x / 1000f, 1) + "  Z " + AvNum.Fixed(ground.z / 1000f, 1) + " KM";
            rows.Add(new Row { Key = "FIX", Value = fix, Tone = ink });
            if (MfdTerrainRelief.TryElevation(ground, out float elevation))
                rows.Add(new Row { Key = "TERRAIN", Value = "~" + AvNum.Fixed(elevation, 0) + " M", Tone = ink });

            float agl = DefaultAgl;
            if (GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null && !aircraft.disabled)
            {
                GlobalPosition own = aircraft.GlobalPosition();
                rows.Add(new Row { Key = "FROM YOU", Value = MfdMapInteractions.LegText(own, ground), Tone = ink });
                int eta = MapFix.EtaSeconds(MapFix.Range(own.x, own.z, ground.x, ground.z), aircraft.speed);
                if (eta >= 0)
                    rows.Add(new Row { Key = "ETA", Value = MfdChromeLay.TwoDigits(eta / 60) + ":" +
                        MfdChromeLay.TwoDigits(eta % 60) + "  AT PRESENT SPEED", Tone = ink });
                agl = Mathf.Max(0f, aircraft.radarAlt);
            }
            if (mark.HasValue)
                rows.Add(new Row { Key = "FROM MARK", Value = MfdMapInteractions.LegText(mark.Value, ground),
                    Tone = AvStyleHost.FuiColor("select", AvTheme.Accent) });

            if (hq == null || !ModuleServices.TryGet(out IThreatPicture picture)) return tone;
            if (!picture.IsReady(observer))
            {
                rows.Add(new Row { Key = "AIR DEF", Value = "—", Tone = muted });
                return tone;
            }
            // The height is printed with the answer: coverage is a cone, so the read is only true at that height.
            string at = "  ·  " + AvNum.Fixed(Mathf.Round(agl / 10f) * 10f, 0) + " M AGL";
            if (picture.TryGetCoverage(observer, ground.x, ground.z, agl, out _, out int rings) && rings >= 0)
                rows.Add(rings > 0
                    ? new Row { Key = "AIR DEF", Value = rings + (rings == 1 ? " RING" : " RINGS") + at, Tone = danger }
                    : new Row { Key = "AIR DEF", Value = "NONE KNOWN" + at, Tone = ready });
            else rows.Add(new Row { Key = "AIR DEF", Value = "—", Tone = muted });

            if (!picture.TryGetAreaIntel(observer, ground.x, ground.z, ReconRadius, out AreaIntel intel)) return tone;
            rows.Add(intel.Scouted
                ? new Row { Key = "RECON", Value = "EYES ON  ·  " + TheaterReadout.Age(intel.LastObservedAgeSeconds) + " AGO", Tone = ready }
                : float.IsNaN(intel.LastObservedAgeSeconds)
                    ? new Row { Key = "RECON", Value = "NEVER OBSERVED", Tone = muted }
                    : new Row { Key = "RECON", Value = "LAST SEEN " + TheaterReadout.Age(intel.LastObservedAgeSeconds) + " AGO", Tone = ink });
            if (intel.Power > 0f)
                rows.Add(new Row { Key = "ENEMY", Value = "GROUND FORCE" + (intel.AntiTank > 0f ? " · AT" : "") +
                    (intel.AirDefence > 0f ? " · AD" : "") + (intel.Artillery > 0f ? " · ARTY" : ""), Tone = danger });
            return tone;
        }
    }
}
