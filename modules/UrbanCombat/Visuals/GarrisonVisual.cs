using System;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    internal static class GarrisonVisual
    {
        public static void Apply(Building building)
        {
            if (building == null) return;
            if (building.NetworkUniqueName?.StartsWith(RooftopPlacement.NamePrefix, StringComparison.Ordinal) == true)
            {
                // Vanilla emplacement prefabs include excavated earth and terrain grass
                // blockers. Keep their weapon, crew and hitbox; replace the earth with
                // rooftop cover. Inactive children also stay hidden through native LOD changes.
                Transform dugout = building.transform.Find("dugout");
                if (dugout != null) dugout.gameObject.SetActive(false);
                for (int i = 0; i < building.transform.childCount; i++)
                {
                    Transform child = building.transform.GetChild(i);
                    if (child.name.StartsWith("GrassBlocker_Proxy", StringComparison.Ordinal))
                        child.gameObject.SetActive(false);
                }
                OccupiedBuildingMarking.Apply(building.gameObject, building.NetworkHQ);
                return;
            }
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
