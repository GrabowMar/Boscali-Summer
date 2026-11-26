using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>
    /// Internal airframe and propulsion hardware revealed by damaged panels or open doors:
    /// canted AESA radar array, HV traction-battery busbars, avionics LRUs, turbogenerator stator
    /// and thermal exhaust core, intake splitter vanes, fan trunnions, and C2 mast well/head.
    /// </summary>
    internal static class SkylarkInternals
    {
        internal static void Refit(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            BuildRadarArray(aircraft, nodes, size);
            BuildBatteryAndAvionics(aircraft, nodes, size);
            BuildTurbogeneratorAndExhaust(aircraft, nodes, size);
            BuildIntakeVanes(aircraft, nodes, size);
            BuildFanTrunnions(aircraft, nodes, size);
            BuildMastDetail(aircraft, nodes, size);
        }

        private static void BuildRadarArray(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            if (!nodes.TryGetValue("Internal_Radar", out Transform radar)) return;
            var dish = new SkylarkMeshKit.Builder();
            Quaternion cant = Quaternion.Euler(-16f, 0f, 0f);
            dish.Cylinder(cant * new Vector3(0f, 0f, -.03f), cant * new Vector3(0f, 0f, .025f), .145f, .140f, 8);
            dish.AttachInAircraftFrame("Internal_AesaArray", aircraft, radar, radar.position + aircraft.forward * (.06f * size), size, SkylarkMeshKit.CopperCoil, nodes, castShadows: false);

            var frame = new SkylarkMeshKit.Builder();
            frame.HollowRing(cant * new Vector3(0f, 0f, -.01f), cant * Vector3.forward, .135f, .165f, .04f, 8);
            frame.Box(new Vector3(0f, -.02f, -.09f), new Vector3(.20f, .14f, .08f)); // T/R feed chassis
            frame.AttachInAircraftFrame("Internal_AesaChassis", aircraft, radar, radar.position + aircraft.forward * (.06f * size), size, SkylarkMeshKit.DarkAlloy, nodes, castShadows: false);
        }

        private static void BuildBatteryAndAvionics(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            if (nodes.TryGetValue("Internal_Battery", out Transform battery))
            {
                var bus = new SkylarkMeshKit.Builder();
                bus.Box(new Vector3(-.18f, .18f, .05f), new Vector3(.032f, .026f, .62f));
                bus.Box(new Vector3(.18f, .18f, .05f), new Vector3(.032f, .026f, .62f));
                bus.Box(new Vector3(0f, .19f, .28f), new Vector3(.38f, .028f, .036f)); // cross-tie busbar
                bus.AttachInAircraftFrame("Internal_HvBusbars", aircraft, battery, battery.position, size, SkylarkMeshKit.HvOrange, nodes, castShadows: false);

                var cells = new SkylarkMeshKit.Builder();
                for (int i = -1; i <= 1; i++)
                    cells.Box(new Vector3(i * .16f, .04f, 0f), new Vector3(.12f, .22f, .44f));
                cells.AttachInAircraftFrame("Internal_BatteryCells", aircraft, battery, battery.position, size, SkylarkMeshKit.DarkAlloy, nodes, castShadows: false);
            }

            if (nodes.TryGetValue("Internal_Avionics", out Transform avionics))
            {
                var lrus = new SkylarkMeshKit.Builder();
                for (int i = 0; i < 4; i++)
                {
                    float z = -.35f + i * .22f;
                    lrus.Box(new Vector3(-.22f, .02f, z), new Vector3(.18f, .09f, .17f));
                    lrus.Box(new Vector3(.22f, .02f, z), new Vector3(.18f, .09f, .17f));
                }
                lrus.AttachInAircraftFrame("Internal_AvionicsLrus", aircraft, avionics, avionics.position, size, SkylarkMeshKit.DarkAlloy, nodes, castShadows: false);

                var conduit = new SkylarkMeshKit.Builder();
                conduit.Cylinder(new Vector3(-.12f, .04f, -.45f), new Vector3(-.12f, .04f, .52f), .016f, .016f, 6);
                conduit.Cylinder(new Vector3(.12f, .04f, -.45f), new Vector3(.12f, .04f, .52f), .016f, .016f, 6);
                conduit.AttachInAircraftFrame("Internal_AvionicsHvFeed", aircraft, avionics, avionics.position, size, SkylarkMeshKit.HvOrange, nodes, castShadows: false);
            }
        }

        private static void BuildTurbogeneratorAndExhaust(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            if (nodes.TryGetValue("Internal_Turbogenerator", out Transform gen))
            {
                var stator = new SkylarkMeshKit.Builder();
                stator.Cylinder(new Vector3(0f, .02f, -.32f), new Vector3(0f, .02f, .38f), .24f, .22f, 12);
                for (int i = 0; i < 4; i++)
                    stator.HollowRing(new Vector3(0f, .02f, -.22f + i * .18f), Vector3.forward, .23f, .27f, .04f, 12);
                stator.AttachInAircraftFrame("Internal_GeneratorStator", aircraft, gen, gen.position, size, SkylarkMeshKit.DarkAlloy, nodes, castShadows: false);

                var coils = new SkylarkMeshKit.Builder();
                coils.HollowRing(new Vector3(0f, .02f, .26f), Vector3.forward, .20f, .265f, .14f, 12);
                coils.AttachInAircraftFrame("Internal_GeneratorWindings", aircraft, gen, gen.position, size, SkylarkMeshKit.CopperCoil, nodes, castShadows: false);
            }

            if (nodes.TryGetValue("Fuselage_Tail", out Transform tail))
            {
                foreach (string side in new[] { "L", "R" })
                {
                    if (!nodes.TryGetValue("Exhaust_Shutter_" + side, out Transform shutter)) continue;
                    float sign = side == "L" ? -1f : 1f;
                    Vector3 corePos = shutter.position + aircraft.TransformVector(new Vector3(-sign * .06f, 0f, .24f) * size);
                    var duct = new SkylarkMeshKit.Builder();
                    duct.HollowRing(Vector3.zero, Vector3.forward, .085f, .125f, .34f, 10);
                    duct.AttachInAircraftFrame("Internal_ExhaustDuct_" + side, aircraft, tail, corePos, size, SkylarkMeshKit.Ceramic, nodes, castShadows: false);

                    var glow = new SkylarkMeshKit.Builder();
                    glow.Cylinder(new Vector3(0f, 0f, .06f), new Vector3(0f, 0f, .16f), .078f, .070f, 10);
                    glow.AttachInAircraftFrame("ExhaustGlow_" + side, aircraft, tail, corePos, size, SkylarkMeshKit.LampBase, nodes, castShadows: false);
                }
            }
        }

        private static void BuildIntakeVanes(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            foreach (string side in new[] { "L", "R" })
            {
                if (!nodes.TryGetValue("Intake_" + side, out Transform intake) || !nodes.TryGetValue("Intake_Door_" + side, out Transform door)) continue;
                float sign = side == "L" ? -1f : 1f;
                Vector3 throat = door.position + aircraft.TransformVector(new Vector3(sign * .02f, -.16f, -.28f) * size);
                var vanes = new SkylarkMeshKit.Builder();
                for (int i = -1; i <= 1; i++)
                    vanes.Box(new Vector3(i * .048f, 0f, 0f), new Vector3(.012f, .24f, .32f), Quaternion.Euler(0f, sign * 18f, 0f));
                vanes.AttachInAircraftFrame("Internal_IntakeVanes_" + side, aircraft, intake, throat, size, SkylarkMeshKit.Ceramic, nodes, castShadows: false);
            }
        }

        private static void BuildFanTrunnions(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            foreach (string suffix in new[] { "FL", "FR", "RL", "RR" })
            {
                if (!nodes.TryGetValue("Duct_" + suffix, out Transform duct)) continue;
                int layer = duct.gameObject.layer;
                float sign = suffix.EndsWith("L") ? -1f : 1f;
                // Duct local Z is the thrust axis (aircraft up when in hover), local X is lateral tilt axis.
                var trunnion = new SkylarkMeshKit.Builder();
                trunnion.HollowRing(new Vector3(0f, 0f, -.04f * size), Vector3.forward, .18f * size, .25f * size, .09f * size, 12);
                trunnion.Cylinder(new Vector3(-sign * 1.05f * size, 0f, 0f), new Vector3(-sign * .90f * size, 0f, 0f), .095f * size, .085f * size, 10);
                trunnion.Attach("Fittings_DuctTrunnion_" + suffix, duct, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, layer, castShadows: false);
            }
        }

        private static void BuildMastDetail(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            if (!nodes.TryGetValue("C2_Mast", out Transform mast)) return;
            // Well collar on parent spine + blade antenna fins & active-TX beacon on the mast head.
            var collar = new SkylarkMeshKit.Builder();
            collar.HollowRing(Vector3.zero, Vector3.up, .078f, .115f, .045f, 10);
            collar.AttachInAircraftFrame("Fittings_MastCollar", aircraft, mast.parent, mast.position + aircraft.up * (.01f * size), size, SkylarkMeshKit.Ceramic, castShadows: false);

            var head = new SkylarkMeshKit.Builder();
            head.Box(new Vector3(0f, .18f, 0f), new Vector3(.024f, .10f, .14f)); // blade antenna fin
            head.Box(new Vector3(0f, .10f, -.03f), new Vector3(.13f, .035f, .07f)); // cross-arm datalink bar
            head.AttachInAircraftFrame("Fittings_MastHead", aircraft, mast, mast.position, size, SkylarkMeshKit.DarkAlloy, castShadows: false);

            var beacon = new SkylarkMeshKit.Builder();
            beacon.Cylinder(new Vector3(0f, .235f, 0f), new Vector3(0f, .255f, 0f), .024f, .018f, 8);
            beacon.AttachInAircraftFrame("C2_Mast_Beacon", aircraft, mast, mast.position, size, SkylarkMeshKit.LampBase, nodes, castShadows: false);
        }
    }
}
