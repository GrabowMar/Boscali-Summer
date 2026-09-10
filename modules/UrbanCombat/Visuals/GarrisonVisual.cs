using System;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    internal static class GarrisonVisual
    {
        public static void Apply(Building building)
        {
            if (building == null) return;
            Renderer[] renderers = building.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null) renderer.enabled = false;
            }

            // The spawned DEF object is a logic proxy anchored to a civilian building.
            // Hide every renderer—including turret meshes—so the civilian building remains
            // the only thing players see; its weapon/targeting logic still runs normally.
            if (!building.gameObject.name.StartsWith("BoscaliSummer.GarrisonLogic:", StringComparison.Ordinal))
                building.gameObject.name = "BoscaliSummer.GarrisonLogic:" + building.gameObject.name;
        }
    }
}
