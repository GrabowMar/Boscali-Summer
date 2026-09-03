using System;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Spawns authentic vanilla soldier models from GameAssets.i.pilotDismounted,
    /// stripping game logic and physics while retaining complete 3D meshes, textures,
    /// helmets, uniforms, and gear.
    /// </summary>
    internal static class VanillaSoldierFactory
    {
        public static GameObject CreateVisualSoldier(Vector3 position, Quaternion rotation, Transform parent)
        {
            if (GameAssets.i == null || GameAssets.i.pilotDismounted == null)
                return null;

            GameObject go = UnityEngine.Object.Instantiate(GameAssets.i.pilotDismounted, position, rotation, parent);
            go.name = "BoscaliSummer.Soldier";

            // Strip unit and damage logic so it acts purely as visual infantry
            PilotDismounted pd = go.GetComponent<PilotDismounted>();
            if (pd != null) UnityEngine.Object.Destroy(pd);

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }

            Collider[] cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] != null) cols[i].enabled = false;

            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = true;
                    renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }

            return go;
        }
    }
}