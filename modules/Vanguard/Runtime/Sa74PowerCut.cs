using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// After an SA-74N burst the map's power is gone: the game's own building lights (street lamps, windows, hangar
    /// floods) and airfield runway lights are switched off and held off. The game turns them back on at dusk
    /// (BuildingLights) and after a tower repair (Airbase), so the cut is re-applied every two seconds.
    /// The mod's own city and airfield lights read the same blackout (RadioAftermath.Active) in Immersion.
    /// ponytail: lights stay off after a reset that keeps the scene until the next dusk toggle; restore if that matters.
    /// </summary>
    internal static class Sa74PowerCut
    {
        private static readonly AccessTools.FieldRef<BuildingLights, Renderer[]> BuildingRenderers = Ref<BuildingLights, Renderer[]>("renderers");
        private static readonly AccessTools.FieldRef<BuildingLights, Light[]> BuildingLamps = Ref<BuildingLights, Light[]>("lights");
        private static readonly AccessTools.FieldRef<Airbase, Renderer[]> RunwayLights = Ref<Airbase, Renderer[]>("runwayLights");
        private static readonly List<Renderer> renderers = new List<Renderer>();
        private static readonly List<Light> lamps = new List<Light>();
        private static float next = float.NegativeInfinity;
        private static bool listed;

        internal static void Tick()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 2f;
            if (!listed) List();
            for (int i = 0; i < renderers.Count; i++) if (renderers[i] != null && renderers[i].enabled) renderers[i].enabled = false;
            for (int i = 0; i < lamps.Count; i++) if (lamps[i] != null && lamps[i].enabled) lamps[i].enabled = false;
        }

        internal static void Reset()
        {
            renderers.Clear();
            lamps.Clear();
            listed = false;
            next = float.NegativeInfinity;
        }

        private static void List()
        {
            listed = true;
            foreach (BuildingLights b in Object.FindObjectsOfType<BuildingLights>())
            {
                if (BuildingRenderers != null && BuildingRenderers(b) is Renderer[] r) renderers.AddRange(r);
                if (BuildingLamps != null && BuildingLamps(b) is Light[] l) lamps.AddRange(l);
            }
            foreach (Airbase a in Object.FindObjectsOfType<Airbase>())
                if (RunwayLights != null && RunwayLights(a) is Renderer[] r) renderers.AddRange(r);
        }

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (System.Exception) { return null; }
        }
    }
}
