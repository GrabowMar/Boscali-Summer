using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    // Native jobs copy force offsets and collision extents once, at AeroPart_OnInitialize.
    internal sealed class SkylarkAerodynamics : MonoBehaviour
    {
        [SerializeField] private AeroPart[] parts;
        [SerializeField] private Collider[] skins;
        [SerializeField] private AeroPart[] skinOwners;

        internal static void Configure(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            var component = aircraft.gameObject.AddComponent<SkylarkAerodynamics>();
            component.parts = aircraft.GetComponentsInChildren<AeroPart>(true);
            component.skins = aircraft.GetComponentsInChildren<SkylarkHitbox>(true).Select(h => h.GetComponent<Collider>()).ToArray();
            component.skinOwners = component.skins.Select(c => c.GetComponent<SkylarkHitbox>().Owner).ToArray();
            foreach (AeroPart part in component.parts)
            {
                if (part.name == "rudder" || part.name == "elevator_L" || part.name == "aileron_L" || part.name == "aileron_R" || part.name == "vstab1" || part.name == "vstab2") continue;
                Transform lift = new GameObject(part.name + "_SkylarkLift").transform;
                lift.SetParent(part.transform, false);
                lift.position = part.transform.position;
                lift.rotation = aircraft.transform.rotation;
                SkylarkTemplate.Set(part, "liftNormal", lift);
                SkylarkTemplate.Set(part, "airflowChanneling", 0f);
                if (part.name == "cockpit_R") part.SetWingArea(0f); // No asymmetric donor side-lift on the single cockpit.
            }
            foreach (string side in new[] { "L", "R" })
            {
                float sign = side == "L" ? -1f : 1f;
                Surface("Wing_" + side, 2.1878808f, new Vector3(sign * 1.58968f, .27f, -.49087f));
                Surface("Flap_" + side, .1092f, new Vector3(sign * 1.095f, .27f, -1.35164f));
                Surface("Elevon_" + side, .4396f, new Vector3(sign * 2.115f, .27f, -1.53932f));
                Surface("Sidepod_" + side, 1.3502f, new Vector3(sign * 1.95497f, -.332f, -1.22615f));
            }
            void Surface(string name, float area, Vector3 center)
            {
                AeroPart part = nodes[name].GetComponentInParent<AeroPart>(true);
                part.SetWingArea(area * SkylarkTemplate.Scale * SkylarkTemplate.Scale);
                part.LiftNormal.position = aircraft.transform.TransformPoint(center * SkylarkTemplate.Scale);
            }
        }

        internal void Prepare(AeroPart part)
        {
            Vector3 center = part.rb.transform.TransformPoint(part.rb.centerOfMass);
            SkylarkTemplate.Set(part, "centerOfLift", part.LiftNormal.InverseTransformDirection(part.LiftNormal.position - center));
            // UnitPart.Awake sees a disabled donor collider and caches empty bounds. Native AeroJob
            // divides by collisionSize.y when evaluating water immersion even in flight: zero gives NaN
            // and silently discards every aerodynamic force. Supply the actual replacement bounds.
            Bounds bounds = new Bounds(part.transform.position, Vector3.one * .02f);
            for (int i = 0; i < skins.Length; i++)
                if (skinOwners[i] == part && skins[i] != null && skins[i].enabled) bounds.Encapsulate(skins[i].bounds);
            SkylarkTemplate.Set(part, "collisionSize", Vector3.Max(bounds.extents, Vector3.one * .01f));
        }
    }

    [HarmonyPatch(typeof(AeroPart), "AeroPart_OnInitialize")]
    internal static class SkylarkAeroInitializePatch
    {
        private static void Prefix(AeroPart __instance)
        {
            if (__instance.parentUnit != null && __instance.parentUnit.definition.jsonKey == SkylarkTemplate.Key)
                __instance.parentUnit.GetComponent<SkylarkAerodynamics>().Prepare(__instance);
        }
    }
}
