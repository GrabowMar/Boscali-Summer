using System;
using Mirage;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Copies the vanilla dismounted-pilot mesh as a visual soldier, without seats or chairs.
    /// </summary>
    internal static class VanillaSoldierFactory
    {
        private static GameObject staging;

        public static GameObject CreateVisualSoldier(Vector3 position, Quaternion rotation, Transform parent)
        {
            if (GameAssets.i == null || GameAssets.i.pilotDismounted == null)
                return null;

            // Clone under an inactive parent so the networked PilotDismounted (a Unit) and its
            // NetworkIdentity never run Awake or OnDestroy; they are stripped before the copy
            // ever activates, leaving a purely local mesh and animator.
            if (staging == null)
            {
                staging = new GameObject("BoscaliSummer.SoldierStaging");
                staging.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(staging);
            }
            GameObject go = UnityEngine.Object.Instantiate(GameAssets.i.pilotDismounted, position, rotation, staging.transform);
            go.name = "BoscaliSummer.Soldier";

            // 1. Remove the networked unit and transform (PilotDismounted,
            // PilotDismountedNetworkTransform), then the identity, then the ejection seat.
            NetworkBehaviour[] behaviours = go.GetComponentsInChildren<NetworkBehaviour>(true);
            for (int i = behaviours.Length - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(behaviours[i]);
            foreach (NetworkIdentity identity in go.GetComponentsInChildren<NetworkIdentity>(true))
                UnityEngine.Object.DestroyImmediate(identity);

            EjectionSeat seat = go.GetComponentInChildren<EjectionSeat>(true);
            if (seat != null && seat.gameObject != go)
            {
                UnityEngine.Object.DestroyImmediate(seat.gameObject);
            }

            // 2. Remove any remaining seat, chair, or bench GameObjects in the hierarchy
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = go.transform.GetChild(i);
                string cName = child.name.ToLowerInvariant();
                if (cName.Contains("seat") || cName.Contains("eject") || cName.Contains("bench") || cName.Contains("chair"))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            go.transform.SetParent(parent, true);

            // 3. Configure physics (kinematic and non-colliding)
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }

            Collider[] cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) cols[i].enabled = false;
            }

            // 4. Ensure human body and gear renderers are enabled
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r != null)
                {
                    string rName = r.name.ToLowerInvariant();
                    if (rName.Contains("seat") || rName.Contains("bench") || rName.Contains("chair"))
                    {
                        UnityEngine.Object.Destroy(r.gameObject);
                        continue;
                    }

                    r.enabled = true;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

                    // Ensure shader is valid
                    if (r.sharedMaterial == null || r.sharedMaterial.shader == null ||
                        r.sharedMaterial.shader.name.IndexOf("InternalError", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // GetSandbagMaterial already falls back to concrete.
                        r.sharedMaterial = MaterialProvider.GetSandbagMaterial();
                    }
                }
            }

            // 5. Configure animator to play human standing/ready pose
            Animator anim = go.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.enabled = true;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (var p in anim.parameters)
                {
                    if (p.name.IndexOf("land", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        p.name.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        anim.SetBool(p.name, true);
                    }
                    else if (p.name.IndexOf("chute", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        anim.SetBool(p.name, false);
                    }
                }
            }

            return go;
        }
    }
}
