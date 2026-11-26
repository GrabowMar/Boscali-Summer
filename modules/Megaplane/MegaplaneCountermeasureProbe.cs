#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace BoscaliSummer.Modules.Megaplane
{
    public static class MegaplaneCountermeasureProbe
    {
        public static object Observe(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object unit) && unit is Aircraft aircraft) || !MegaplaneModule.Is(aircraft))
                return new { ok = false };
            Countermeasure[] countermeasures = aircraft.GetComponentsInChildren<Countermeasure>(true);
            FlareEjector flare = countermeasures.OfType<FlareEjector>().FirstOrDefault();
            if (flare == null) return new { ok = false, types = countermeasures.Select(c => c.GetType().Name).ToArray() };

            var manager = aircraft.countermeasureManager;
            if (args.TryGetValue("trigger", out object value))
            {
                if (manager.activeIndex == byte.MaxValue) manager.NextCountermeasure();
                for (int i = 0; i < countermeasures.Length && manager.GetActiveCountermeasure() != flare; i++)
                    manager.NextCountermeasure();
                if (manager.GetActiveCountermeasure() != flare) return new { ok = false, flareSelected = false };
                aircraft.Countermeasures(Convert.ToBoolean(value), manager.activeIndex);
            }
            if (args.ContainsKey("rearm")) manager.Rearm(new RearmEventArgs { Rearmer = aircraft });

            var points = (FlareEjector.EjectionPoint[])AccessTools.Field(typeof(FlareEjector), "ejectionPoints").GetValue(flare);
            var irSources = (List<IRSource>)AccessTools.Field(typeof(Unit), "IRSources").GetValue(aircraft);
            int donorCapacity = Encyclopedia.Lookup.TryGetValue("Darkreach", out var definition) && definition is AircraftDefinition donor
                ? donor.unitPrefab.GetComponentsInChildren<FlareEjector>(true).Sum(ejector => ejector.ammo) : 0;
            return new
            {
                ok = true, ammo = flare.GetAmmo(), capacity = flare.GetMaxAmmo(), donorCapacity,
                decoys = irSources.Count(source => source.flare && source.transform != null),
                outlets = points.Select(p => p.transform.name).ToArray(),
                attached = points.All(p => p.part != null && p.transform != null && p.transform.IsChildOf(p.part.transform)),
            };
        }
    }
}
#endif
