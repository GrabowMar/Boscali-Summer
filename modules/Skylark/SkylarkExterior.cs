using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    internal static class SkylarkExterior
    {
        internal static void Refit(Transform aircraft, Dictionary<string, Transform> nodes)
        {
            // Fitting dimensions come from the current Blueprinter art, whose fan spacing is 1.15 times the original.
            float size = SkylarkTemplate.Scale / 1.15f;
            SkylarkCanopyProfile.Refit(aircraft, nodes, size);
            FitChin(aircraft, nodes, size);
            FitOptics(aircraft, nodes, size);
            FitEnginePanels(aircraft, nodes, size);
            FitAirbrakes(aircraft, nodes, size);
            FitFlapFairings(aircraft, nodes, size);
            FitPylons(aircraft, nodes, size);
            FitSeatHeadbox(aircraft, nodes, size);
            FitCanopyStruts(aircraft, nodes, size);
            SkylarkInternals.Refit(aircraft, nodes, size);
        }

        private static void FitChin(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            if (!nodes.TryGetValue("GunPosition", out Transform mount) ||
                !nodes.TryGetValue("GunTurret_Yaw", out Transform yaw) ||
                !nodes.TryGetValue("GunTurret_Pitch", out Transform pitch) ||
                !nodes.TryGetValue("GunBarrel", out Transform barrel) ||
                !nodes.TryGetValue("GunMuzzle", out Transform muzzle)) return;

            // Keep the visual gimbal centred on the native hardpoint. The muzzle moves with the shortened
            // barrel in its neutral pose; its authored transform stays outside the visual recoil stroke.
            Vector3 originalMount = mount.position;
            Vector3 barrelTip = barrel.InverseTransformPoint(muzzle.position);
            Vector3 raised = aircraft.TransformVector(new Vector3(0f, .052f, .035f) * size);
            mount.position += raised;
            yaw.position += raised;
            yaw.localScale *= .80f;
            pitch.localScale *= .92f;
            barrel.localScale = Vector3.Scale(barrel.localScale, new Vector3(.86f, .81f, .84f));
            muzzle.position = barrel.TransformPoint(barrelTip);

            // Two tapered cheeks leave a channel for the spring stow and clearance for the gimbal below.
            // The aft end follows the shallow nose-wheel enclosure and the front end meets the radome.
            var sections = new (float Z, float OuterX, float OuterY, float InnerX, float InnerY)[]
            {
                (-.28f, .257f, .056f, .141f, -.021f),
                (-.14f, .272f, .064f, .132f, -.075f),
                ( .05f, .247f, .081f, .114f, -.079f),
                ( .24f, .213f, .112f, .085f, -.041f),
                ( .47f, .158f, .128f, .063f,  .053f),
                ( .68f, .077f, .133f, .047f,  .093f)
            };
            var shell = new SkylarkMeshKit.Builder();
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 1; i < sections.Length; i++)
                {
                    var a = sections[i - 1];
                    var b = sections[i];
                    Vector3 outerA = new Vector3(side * a.OuterX, a.OuterY, a.Z);
                    Vector3 outerB = new Vector3(side * b.OuterX, b.OuterY, b.Z);
                    Vector3 edgeA = new Vector3(side * a.InnerX, a.InnerY, a.Z);
                    Vector3 edgeB = new Vector3(side * b.InnerX, b.InnerY, b.Z);
                    Vector3 lip = new Vector3(-side * .015f, .042f, 0);
                    shell.Quad(outerA, outerB, edgeB, edgeA, new Vector3(side, -1, 0));
                    shell.Quad(edgeA, edgeB, edgeB + lip, edgeA + lip, -side * Vector3.right);
                }
            }
            Transform fairing = shell.AttachInAircraftFrame("ChinFairing", aircraft, nodes["Fuselage_Nose"], originalMount, size, SkylarkMeshKit.Graphite, nodes);

            // Clamshell chin bay doors hinged along the lower cheek lips; they close flush under the stowed barrel and swing out to fire.
            int gunLayer = fairing.gameObject.layer;
            foreach (int side in new[] { -1, 1 })
            {
                string doorName = side < 0 ? "GunBayDoor_L" : "GunBayDoor_R";
                if (nodes.ContainsKey(doorName)) continue;
                var door = new SkylarkMeshKit.Builder();
                Vector3 hingeA = new Vector3(0f, 0f, -.25f);
                Vector3 hingeB = new Vector3(0f, 0f, .54f);
                Vector3 edgeA = new Vector3(-side * .126f, -.018f, -.25f);
                Vector3 edgeB = new Vector3(-side * .082f, .036f, .54f);
                Vector3 thick = new Vector3(0f, .012f, 0f);
                door.Quad(hingeA, hingeB, edgeB, edgeA, Vector3.down);
                door.Quad(edgeA + thick, edgeB + thick, hingeB + thick, hingeA + thick, Vector3.up);
                door.Quad(edgeA, edgeB, edgeB + thick, edgeA + thick, -side * Vector3.right);
                door.Quad(hingeA + thick, hingeB + thick, hingeB, hingeA, side * Vector3.right);
                door.Quad(hingeA, edgeA, edgeA + thick, hingeA + thick, Vector3.back);
                door.Quad(edgeB, hingeB, hingeB + thick, edgeB + thick, Vector3.forward);
                nodes[doorName] = door.Attach(doorName, fairing, new Vector3(side * .124f, -.066f, .10f), Quaternion.identity, Vector3.one, SkylarkMeshKit.Ceramic, gunLayer);
            }

            // Halberd electromagnetic accelerator collars (GunCoil0..5), side busbars, breech block, and muzzle ring in GunBarrel's local Y-axis frame.
            for (int i = 0; i < 6; i++)
            {
                string coilName = "GunCoil" + i;
                if (nodes.ContainsKey(coilName)) continue;
                float y = .22f + i * .13f;
                var coil = new SkylarkMeshKit.Builder();
                coil.HollowRing(Vector3.zero, Vector3.up, .078f, .114f, .042f, 14);
                nodes[coilName] = coil.Attach(coilName, barrel, new Vector3(0f, y, 0f), Quaternion.identity, Vector3.one, SkylarkMeshKit.CopperCoil, gunLayer);
            }

            var barrelKit = new SkylarkMeshKit.Builder();
            barrelKit.Box(new Vector3(0f, .08f, .01f), new Vector3(.175f, .16f, .135f)); // breech capacitor block
            barrelKit.Box(new Vector3(-.112f, .54f, 0f), new Vector3(.020f, .78f, .036f)); // port busbar rail
            barrelKit.Box(new Vector3(.112f, .54f, 0f), new Vector3(.020f, .78f, .036f)); // starboard busbar rail
            barrelKit.HollowRing(new Vector3(0f, .96f, 0f), Vector3.up, .054f, .086f, .055f, 12); // muzzle accelerator ring
            barrelKit.Attach("Fittings_HalberdBarrel", barrel, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, gunLayer);

            var gimbalKit = new SkylarkMeshKit.Builder();
            gimbalKit.Cylinder(new Vector3(-.155f, 0f, 0f), new Vector3(-.11f, 0f, 0f), .078f, .078f, 12);
            gimbalKit.Cylinder(new Vector3(.11f, 0f, 0f), new Vector3(.155f, 0f, 0f), .078f, .078f, 12);
            gimbalKit.Box(new Vector3(0f, -.13f, .01f), new Vector3(.14f, .12f, .11f)); // rear feed housing
            gimbalKit.Attach("Fittings_HalberdGimbal", pitch, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.DarkAlloy, gunLayer);

            var opticsKit = new SkylarkMeshKit.Builder();
            opticsKit.Box(new Vector3(-.105f, .11f, -.035f), new Vector3(.048f, .062f, .048f));
            opticsKit.Attach("Fittings_HalberdOptic", pitch, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.Optical, gunLayer, castShadows: false);
        }

        private static void FitOptics(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            if (!nodes.TryGetValue("Sensor_IRST", out Transform sensor)) return;
            var seat = new SkylarkMeshKit.Builder();
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 trailing = new Vector3(side * .015f, .037f, -.208f);
                Vector3 shoulder = new Vector3(side * .149f, .014f, -.073f);
                Vector3 lower = new Vector3(side * .112f, -.023f, .156f);
                Vector3 leading = new Vector3(0f, -.031f, .185f);
                Vector3 aperture = new Vector3(side * .061f, .054f, -.018f);
                seat.Triangle(trailing, shoulder, aperture, Vector3.up);
                seat.Quad(shoulder, lower, leading, aperture, Vector3.up);
            }
            seat.AttachInAircraftFrame("Fittings_OpticalSeat", aircraft, nodes["Fuselage_Nose"], sensor.position, size, SkylarkMeshKit.Ceramic, nodes);
            sensor.position -= aircraft.up * (.023f * size);
            // This node was exported with local Z up. Retain its faceted lens while sinking its tall base.
            sensor.localScale = Vector3.Scale(sensor.localScale, new Vector3(.92f, 1f, .78f));
            if (sensor.TryGetComponent(out Renderer lens)) lens.sharedMaterial = SkylarkMeshKit.Optical;
        }

        private static void FitEnginePanels(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            // Shallow dielectric covers and narrow cooling slots on the turbine shoulders.
            Transform engine = nodes["Fuselage_Engine"];
            var covers = new SkylarkMeshKit.Builder();
            var slots = new SkylarkMeshKit.Builder();
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 upperRear = new Vector3(side * .292f, .413f, -1.23f);
                Vector3 upperFront = new Vector3(side * .349f, .498f, -.55f);
                Vector3 lowerFront = new Vector3(side * .506f, .352f, -.60f);
                Vector3 lowerRear = new Vector3(side * .432f, .293f, -1.27f);
                covers.Quad(upperRear, upperFront, lowerFront, lowerRear, Vector3.up);
                for (int i = 0; i < 3; i++)
                {
                    float station = .23f + .24f * i;
                    Vector3 start = Vector3.Lerp(upperRear, upperFront, station);
                    Vector3 end = Vector3.Lerp(lowerRear, lowerFront, station);
                    Vector3 strip = Vector3.Lerp(end, start, .16f);
                    start = Vector3.Lerp(start, end, .17f);
                    Vector3 setback = new Vector3(side * .019f, -.013f, -.038f);
                    Vector3 raise = new Vector3(side * .004f, .005f, 0f);
                    slots.Quad(start + raise, strip + raise, strip + setback + raise, start + setback + raise, Vector3.up);
                }
            }
            covers.AttachInAircraftFrame("Fittings_TurbineCovers", aircraft, engine, engine.position, size, SkylarkMeshKit.Graphite, nodes);
            slots.AttachInAircraftFrame("Fittings_TurbineSlots", aircraft, engine, engine.position, size, SkylarkMeshKit.Ceramic, nodes);
        }

        private static void FitAirbrakes(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            if (!nodes.TryGetValue("Fuselage_Engine", out Transform engine)) return;
            // Flush dorsal speedbrake petals aft of the turbine covers on Fuselage_Engine (slope -13 deg aft).
            Quaternion slope = Quaternion.Euler(-13f, 0f, 0f);
            foreach (string side in new[] { "L", "R" })
            {
                float sign = side == "L" ? -1f : 1f;
                Vector3 hingeWorld = engine.position + aircraft.TransformVector(new Vector3(sign * .215f, .468f, -1.20f) * size);
                Transform hinge = SkylarkTemplate.Empty("Airbrake_" + side, engine, hingeWorld);
                hinge.rotation = aircraft.rotation * slope;
                hinge.localScale = Vector3.one * size;
                hinge.gameObject.layer = engine.gameObject.layer;

                var petal = new SkylarkMeshKit.Builder();
                petal.Box(new Vector3(0f, -.008f, -.22f), new Vector3(.155f, .016f, .42f));
                petal.Attach("AirbrakePetal_" + side, hinge, Vector3.zero, Quaternion.identity, Vector3.one, SkylarkMeshKit.Graphite, engine.gameObject.layer);
                nodes["Airbrake_" + side] = hinge;

                // Recessed bay floor & hydraulic piston under the petal.
                var well = new SkylarkMeshKit.Builder();
                well.Box(new Vector3(0f, -.024f, -.22f), new Vector3(.145f, .014f, .39f));
                well.Attach("Fittings_AirbrakeWell_" + side, engine, hinge.localPosition, hinge.localRotation, Vector3.one * size, SkylarkMeshKit.PrimerInterior, engine.gameObject.layer, castShadows: false);

                Transform basePin = SkylarkTemplate.Empty("AirbrakeBasePin_" + side, engine,
                    hingeWorld + aircraft.TransformVector(new Vector3(0f, -.035f, -.24f) * size));
                Transform lugPin = SkylarkTemplate.Empty("AirbrakeLugPin_" + side, hinge,
                    hingeWorld + aircraft.TransformVector(new Vector3(0f, -.018f, -.28f) * size));
                var ram = new SkylarkMeshKit.Builder();
                ram.Cylinder(Vector3.zero, Vector3.forward * .22f, .012f, .012f, 8);
                Transform piston = ram.Attach("AirbrakePiston_" + side, basePin, Vector3.zero, Quaternion.identity, Vector3.one * size, SkylarkMeshKit.Chrome, engine.gameObject.layer, castShadows: false);
                nodes["AirbrakePiston_" + side] = piston;
                nodes["AirbrakeLug_" + side] = lugPin;
            }
        }

        private static void FitFlapFairings(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            foreach (string side in new[] { "L", "R" })
            {
                if (!nodes.TryGetValue("Wing_" + side, out Transform wing) || !nodes.TryGetValue("Flap_" + side, out Transform flap)) continue;
                float sign = side == "L" ? -1f : 1f;
                var fairing = new SkylarkMeshKit.Builder();
                fairing.Box(new Vector3(sign * .16f, -.032f, -.04f), new Vector3(.052f, .048f, .38f));
                fairing.Box(new Vector3(-sign * .16f, -.032f, -.04f), new Vector3(.052f, .048f, .38f));
                fairing.AttachInAircraftFrame("Fittings_FlapTrack_" + side, aircraft, wing, flap.position, size, SkylarkMeshKit.Ceramic);
            }
        }

        private static void FitCanopyStruts(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            if (!nodes.TryGetValue("Fuselage_Cockpit", out Transform cockpit) || !nodes.TryGetValue("Canopy_Frame", out Transform canopyFrame)) return;
            int extLayer = cockpit.gameObject.layer;
            foreach (string side in new[] { "L", "R" })
            {
                float sign = side == "L" ? -1f : 1f;
                Vector3 sillPos = cockpit.position + aircraft.TransformVector(new Vector3(sign * .46f, .30f, .24f) * size);
                Vector3 bowPos = cockpit.position + aircraft.TransformVector(new Vector3(sign * .45f, .44f, .62f) * size);
                Transform sillPin = SkylarkTemplate.Empty("CanopyStrutBase_" + side, cockpit, sillPos);
                Transform bowPin = SkylarkTemplate.Empty("CanopyStrutLug_" + side, canopyFrame, bowPos);
                sillPin.gameObject.layer = extLayer;
                bowPin.gameObject.layer = extLayer;

                var cyl = new SkylarkMeshKit.Builder();
                cyl.Cylinder(Vector3.zero, Vector3.forward * .32f, .013f, .013f, 8);
                Transform barrel = cyl.Attach("CanopyStrutBarrel_" + side, sillPin, Vector3.zero, Quaternion.identity, Vector3.one * size, SkylarkMeshKit.DarkAlloy, extLayer, castShadows: false);
                barrel.gameObject.SetActive(false);

                var rod = new SkylarkMeshKit.Builder();
                rod.Cylinder(Vector3.zero, Vector3.forward * .30f, .008f, .008f, 8);
                Transform piston = rod.Attach("CanopyStrutRod_" + side, bowPin, Vector3.zero, Quaternion.identity, Vector3.one * size, SkylarkMeshKit.Chrome, extLayer, castShadows: false);
                piston.gameObject.SetActive(false);

                nodes["CanopyStrutBarrel_" + side] = barrel;
                nodes["CanopyStrutRod_" + side] = piston;
                nodes["CanopyStrutBasePin_" + side] = sillPin;
                nodes["CanopyStrutBowPin_" + side] = bowPin;
            }
        }

        private static void FitPylons(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            // Sway braces, ejector rack lugs and umbilical fairings on underwing/belly pylons and tip launch rails.
            void DressPylon(string pylonName, string anchorName, float halfSpan)
            {
                if (!nodes.TryGetValue(pylonName, out Transform pylon) || !nodes.TryGetValue(anchorName, out Transform anchor)) return;
                var rack = new SkylarkMeshKit.Builder();
                rack.Box(new Vector3(0f, .032f, 0f), new Vector3(.085f, .045f, halfSpan * 1.6f));
                rack.Box(new Vector3(0f, .012f, -halfSpan * .52f), new Vector3(.17f, .024f, .035f)); // aft sway brace
                rack.Box(new Vector3(0f, .012f, halfSpan * .52f), new Vector3(.17f, .024f, .035f)); // fwd sway brace
                rack.Cylinder(new Vector3(.036f, .015f, .06f), new Vector3(.036f, .055f, .16f), .010f, .010f, 8);
                rack.AttachInAircraftFrame("Fittings_" + pylonName + "_Rack", aircraft, pylon, anchor.position, size, SkylarkMeshKit.DarkAlloy);
            }
            DressPylon("Pylon_L", "Hardpoint_L1", .26f);
            DressPylon("Pylon_R", "Hardpoint_R1", .26f);
            DressPylon("Pylon_Mid_L", "Hardpoint_L3", .24f);
            DressPylon("Pylon_Mid_R", "Hardpoint_R3", .24f);
            DressPylon("Pylon_C", "Hardpoint_C", .30f);

            foreach (string side in new[] { "L", "R" })
            {
                if (!nodes.TryGetValue("Rail_" + side, out Transform rail) || !nodes.TryGetValue("Hardpoint_" + side + "2", out Transform hp)) continue;
                float sign = side == "L" ? 1f : -1f;
                var shoe = new SkylarkMeshKit.Builder();
                shoe.Box(new Vector3(sign * .018f, 0f, 0f), new Vector3(.036f, .054f, .62f));
                shoe.Box(new Vector3(sign * .022f, 0f, .33f), new Vector3(.028f, .040f, .06f)); // umbilical nose cap
                shoe.AttachInAircraftFrame("Fittings_Rail_" + side + "_Shoe", aircraft, rail, hp.position, size, SkylarkMeshKit.DarkAlloy);
            }
        }

        private static void FitSeatHeadbox(Transform aircraft, Dictionary<string, Transform> nodes, float size)
        {
            if (!nodes.TryGetValue("pilot", out Transform pilot) || !nodes.TryGetValue("Fuselage_Cockpit", out Transform cockpit)) return;
            int extLayer = cockpit.gameObject.layer;
            var seat = new SkylarkMeshKit.Builder();
            Vector3 headbox = new Vector3(0f, .47f, -.16f);
            seat.Box(headbox, new Vector3(.23f, .24f, .14f));
            seat.Box(new Vector3(-.10f, .59f, -.17f), new Vector3(.032f, .06f, .05f)); // canopy breaker horn L
            seat.Box(new Vector3(.10f, .59f, -.17f), new Vector3(.032f, .06f, .05f)); // canopy breaker horn R
            seat.Box(new Vector3(0f, .24f, -.18f), new Vector3(.28f, .36f, .09f)); // seat back
            seat.AttachInAircraftFrame("Cockpit_Ext_Headbox", aircraft, cockpit, pilot.position, size, SkylarkMeshKit.Ceramic);
            Transform node = cockpit.Find("Cockpit_Ext_Headbox");
            if (node != null) node.gameObject.layer = extLayer;
        }
    }
}
