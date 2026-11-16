using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>Rebind native cockpit systems before the inactive template is cloned.</summary>
    internal static class SkylarkCockpit
    {
        internal static void Refit(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            Cockpit cockpit = aircraft.GetComponentInChildren<Cockpit>(true);
            UnitPart cockpitPart = nodes["Fuselage_Cockpit"].GetComponentInParent<UnitPart>(true);
            if (cockpit == null || cockpitPart == null || aircraft.pilots.Length == 0)
                throw new InvalidOperationException("Skylark requires the native cockpit, damage part and pilot.");
            aircraft.cockpit = cockpitPart;

            Pilot pilot = aircraft.pilots[0];
            // Tandem: the donor's second crew takes the rear seat when the art supplies its anchor.
            Pilot[] crew = nodes.ContainsKey("pilot_R") ? aircraft.pilots.Take(2).ToArray() : new[] { pilot };
            foreach (Pilot extra in aircraft.pilots.Skip(crew.Length)) extra.gameObject.SetActive(false);
            aircraft.pilots = crew;
            for (int seat = 0; seat < crew.Length; seat++)
            {
                // Keep the native skeleton/seat, damage registration and dismounted-pilot path.
                Transform anchor = nodes[seat == 0 ? "pilot" : "pilot_R"];
                Pilot member = crew[seat];
                member.transform.SetParent(anchor, false);
                member.transform.position = anchor.position;
                member.transform.rotation = aircraft.transform.rotation;
                member.aircraft = aircraft;
                SkylarkTemplate.Set(member, "unitPart", cockpitPart);
                if (member.pilotCollider != null) member.pilotCollider.enabled = true;
                foreach (Renderer renderer in member.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
            }
            aircraft.cockpitViewPoint = nodes["CamPoint"];
            aircraft.cockpitViewPoint.rotation = aircraft.transform.rotation;

            Renderer oldScreen = SkylarkTemplate.Get<Renderer>(cockpit, "tacScreenRender");
            Renderer screen = nodes["Tacscreen"].GetComponent<Renderer>();
            if (oldScreen == null || screen == null || oldScreen.sharedMaterial == null)
                throw new InvalidOperationException("Skylark tactical display is missing its native material binding.");
            int cockpitLayer = oldScreen.gameObject.layer;
            // The donor material is connected to its native TacScreen render texture.
            // Do not leave the author's static DEMO image on an in-game instrument.
            screen.sharedMaterial = oldScreen.sharedMaterial;
            SkylarkTemplate.Set(cockpit, "tacScreenRender", screen);
            SkylarkTemplate.Set(cockpit, "aircraft", aircraft);

            Transform stick = MotionPivot(nodes["Stick"], aircraft, "SkylarkStickMotion");
            SingleRecord(cockpit, "joysticks", joystick =>
            {
                SkylarkTemplate.Set(joystick, "transform", stick);
                SkylarkTemplate.Set(joystick, "range", 12f);
            });
            Transform throttle = MotionPivot(nodes["Throttle"], aircraft, "SkylarkThrottleMotion");
            // The 0.20 m authored slot has 0.02 m clearance at each end.
            throttle.parent.position -= aircraft.transform.forward * .08f;
            SingleRecord(cockpit, "throttles", slider =>
            {
                SkylarkTemplate.Set(slider, "transform", throttle);
                SkylarkTemplate.Set(slider, "rotation", false);
                SkylarkTemplate.Set(slider, "motion", true);
                SkylarkTemplate.Set(slider, "range", .16f);
            });

            BindWarning(aircraft, nodes["MissileWarning_Lamp"].GetComponent<Renderer>());
            BindCanopy(aircraft, cockpitPart, nodes);
            BindLighting(cockpit, cockpitPart, nodes);

            // Native camera switching controls individual renderers, not their children.
            // The native warning driver owns its lamp; adding it here would turn it on
            // whenever the player changes camera even when there is no missile warning.
            var inside = nodes["Cockpit"].GetComponentsInChildren<Renderer>(true)
                .Where(r => !r.name.EndsWith("_LOD") && r.name != "Cockpit_Ext"
                    && r.name != "MissileWarning_Lamp" && !crew.Any(member => r.transform.IsChildOf(member.transform))).ToList();
            foreach (string name in new[] { "Canopy_Int", "Canopy_InternalView", "Canopy_Frame_Int" })
                inside.Add(nodes[name].GetComponent<Renderer>());
            Renderer[] exterior = new[] { "Canopy", "Canopy_Frame", "Cockpit_Ext" }
                .Select(name => nodes[name].GetComponent<Renderer>()).ToArray();
            // The native cockpit overlay camera uses its own render layer. A
            // correct material on an imported Default-layer screen is insufficient.
            foreach (Renderer renderer in inside) renderer.gameObject.layer = cockpitLayer;
            screen.gameObject.layer = cockpitLayer;
            nodes["MissileWarning_Lamp"].gameObject.layer = cockpitLayer;
            foreach (Renderer renderer in exterior) renderer.gameObject.layer = cockpitPart.gameObject.layer;
            SkylarkTemplate.Set(aircraft, "cockpitRenderers", inside.Distinct().ToArray());
            SkylarkTemplate.Set(aircraft, "exteriorRenderers", exterior);
            SkylarkTemplate.Set(aircraft, "groundEquipment", Array.Empty<GameObject>());
            foreach (Renderer renderer in inside) renderer.enabled = false;
            foreach (Renderer renderer in exterior) renderer.enabled = true;
        }

        private static Transform MotionPivot(Transform visual, Aircraft aircraft, string name)
        {
            Transform offset = new GameObject(name + "Offset").transform;
            offset.SetParent(visual.parent, false);
            offset.position = visual.position;
            offset.rotation = aircraft.transform.rotation;
            Transform motion = new GameObject(name).transform;
            motion.SetParent(offset, false);
            // The native controllers overwrite local angles/position. Keep imported
            // mesh orientation and placement on a child of an identity motion pivot.
            visual.SetParent(motion, true);
            return motion;
        }

        private static void SingleRecord(object owner, string field, Action<object> configure)
        {
            var member = AccessTools.Field(owner.GetType(), field);
            Type element = member.FieldType.GetElementType();
            Array records = Array.CreateInstance(element, 1);
            object record = Activator.CreateInstance(element, true);
            configure(record);
            records.SetValue(record, 0);
            member.SetValue(owner, records);
        }

        private static void BindWarning(Aircraft aircraft, Renderer lamp)
        {
            if (lamp == null) throw new InvalidOperationException("Skylark requires a separate missile-warning lens.");
            CockpitWarningLights[] warnings = aircraft.GetComponentsInChildren<CockpitWarningLights>(true);
            CockpitWarningLights warning = warnings.FirstOrDefault() ?? lamp.gameObject.AddComponent<CockpitWarningLights>();
            foreach (CockpitWarningLights extra in warnings.Skip(1)) Object.DestroyImmediate(extra);
            SkylarkTemplate.Set(warning, "aircraft", aircraft);
            SkylarkTemplate.Set(warning, "lightRenderers", new[] { lamp });
            lamp.enabled = false;
        }

        private static void BindCanopy(Aircraft aircraft, UnitPart cockpitPart, Dictionary<string, Transform> nodes)
        {
            Canopy[] donors = aircraft.GetComponentsInChildren<Canopy>(true);
            Canopy canopy = (SkylarkTemplate.Get<Canopy[]>(aircraft, "canopies") ?? Array.Empty<Canopy>())
                .FirstOrDefault(c => c != null) ?? donors.FirstOrDefault();
            if (canopy == null || !(cockpitPart is AeroPart part))
                throw new InvalidOperationException("Skylark requires the native canopy ejection system.");
            foreach (Canopy extra in donors.Where(c => c != canopy)) Object.DestroyImmediate(extra);
            Transform glass = nodes["Canopy"];
            glass.gameObject.layer = canopy.gameObject.layer;
            Transform hinge = MotionPivot(nodes["CanopyMount"], aircraft, "SkylarkCanopyHinge");
            // EjectionSequence derives the launch point from the component's own
            // transform. Move that hidden donor object to the custom canopy origin.
            canopy.transform.SetParent(glass, false);
            canopy.transform.localPosition = Vector3.zero;
            canopy.transform.rotation = aircraft.transform.rotation;
            foreach (Renderer hidden in canopy.GetComponentsInChildren<Renderer>(true)) hidden.enabled = false;
            canopy.ejectionTransform = glass;
            Vector3 launchForce = SkylarkTemplate.Get<Vector3>(canopy, "ejectionForce");
            SkylarkTemplate.Set(canopy, "ejectionForce", glass.InverseTransformDirection(aircraft.transform.TransformDirection(launchForce)));
            canopy.attachedPart = part;
            Renderer[] shell = new[] { "Canopy", "Canopy_Frame", "Canopy_Int", "Canopy_Frame_Int" }
                .Select(name => nodes[name].GetComponent<Renderer>()).ToArray();
            Bounds bounds = SkylarkTemplate.LocalBounds(glass, shell);
            BoxCollider collider = glass.gameObject.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;
            collider.enabled = false; // Native Canopy enables collision only after jettison.
            canopy.ejectionCollider = collider;
            SingleRecord(canopy, "canopyHinges", nativeHinge =>
            {
                SkylarkTemplate.Set(nativeHinge, "transform", hinge);
                SkylarkTemplate.Set(nativeHinge, "hingeAngle", 55f);
            });
            // Only shaders implementing the game's cracked-glass property can use it.
            SkylarkTemplate.Set(canopy, "glassRenderers", shell.Where(r => r.sharedMaterial != null
                && r.sharedMaterial.HasProperty("_Cracked_Amount")).ToArray());
            SkylarkTemplate.Set(aircraft, "canopies", new[] { canopy });
        }

        private static void BindLighting(Cockpit cockpit, UnitPart cockpitPart, Dictionary<string, Transform> nodes)
        {
            Transform[] anchors = nodes.Where(pair => pair.Key.StartsWith("CockpitLighting_"))
                .OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
            int next = 0;
            foreach (BuildingLights lighting in cockpit.GetComponentsInChildren<BuildingLights>(true))
            {
                // Daylight/damage callbacks must never re-enable hidden donor meshes.
                SkylarkTemplate.Set(lighting, "renderers", Array.Empty<Renderer>());
                SkylarkTemplate.Set(lighting, "dependentParts", new[] { cockpitPart });
                foreach (Light light in SkylarkTemplate.Get<Light[]>(lighting, "lights") ?? Array.Empty<Light>())
                {
                    if (light == null || anchors.Length == 0) continue;
                    light.transform.SetParent(anchors[next++ % anchors.Length], false);
                    light.transform.localPosition = Vector3.zero;
                    light.type = LightType.Point;
                    light.range = 1.1f;
                }
            }
        }
    }
}

