using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    internal static class MegaplaneCountermeasures
    {
        private const int FlareCapacity = 480;
        private const float FlareInterval = .32f;
        private const float FlareVelocity = 42f;

        internal static void Install(Aircraft aircraft, Dictionary<string, Transform> nodes)
        {
            FlareEjector[] flares = aircraft.GetComponentsInChildren<FlareEjector>(true);
            ChaffEjector[] chaff = aircraft.GetComponentsInChildren<ChaffEjector>(true);
            if (flares.Length == 0 && chaff.Length == 0) return;

            AeroPart bow = nodes["Fuselage_Forward"].GetComponentInParent<AeroPart>(true);
            AeroPart stern = nodes["Fuselage_Aft"].GetComponentInParent<AeroPart>(true);
            // The forward pair fans below the wing roots. The aft pair clears the hold ramp and tail booms.
            // These points sit on the two main hull sections, clear of the fighter bay and wing stores.
            Vector3 bowPosition = new Vector3(15f, -8.1f, 40f);
            Vector3 sternPosition = new Vector3(14f, -6.85f, -42f);
            Vector3 bowDirection = new Vector3(.84f, -.48f, -.25f);
            Vector3 sternDirection = new Vector3(.43f, -.35f, -.83f);
            Transform bowLeft = Outlet(aircraft.transform, bow, "Bow", -1, bowPosition, bowDirection);
            Transform bowRight = Outlet(aircraft.transform, bow, "Bow", 1, bowPosition, bowDirection);
            Transform sternLeft = Outlet(aircraft.transform, stern, "Stern", -1, sternPosition, sternDirection);
            Transform sternRight = Outlet(aircraft.transform, stern, "Stern", 1, sternPosition, sternDirection);

            var flarePoints = new[]
            {
                new FlareEjector.EjectionPoint { part = bow, transform = bowLeft },
                new FlareEjector.EjectionPoint { part = bow, transform = bowRight },
                new FlareEjector.EjectionPoint { part = stern, transform = sternLeft },
                new FlareEjector.EjectionPoint { part = stern, transform = sternRight },
            };
            foreach (FlareEjector ejector in flares)
            {
                if (!Refit(ejector, typeof(FlareEjector), flarePoints)) continue;
                // The stock ejector registers this capacity before the player or AI can fire it.
                ejector.ammo = FlareCapacity;
                AccessTools.Field(typeof(FlareEjector), "ejectionGrouping")?.SetValue(ejector, flarePoints.Length);
                AccessTools.Field(typeof(FlareEjector), "ejectionInterval")?.SetValue(ejector, FlareInterval);
                AccessTools.Field(typeof(FlareEjector), "ejectionVelocity")?.SetValue(ejector, FlareVelocity);
            }

            var chaffPoints = new[]
            {
                new ChaffEjector.EjectionPoint { part = stern, transform = sternLeft },
                new ChaffEjector.EjectionPoint { part = stern, transform = sternRight },
            };
            foreach (ChaffEjector ejector in chaff) Refit(ejector, typeof(ChaffEjector), chaffPoints);
        }

        private static bool Refit(Countermeasure ejector, Type type, Array outlets)
        {
            var points = AccessTools.Field(type, "ejectionPoints");
            if (points == null) return false;
            points.SetValue(ejector, outlets);
            var doors = AccessTools.Field(type, "flareDoors");
            if (doors != null) doors.SetValue(ejector, Array.CreateInstance(doors.FieldType.GetElementType(), 0));
            return true;
        }

        private static Transform Outlet(Transform root, AeroPart owner, string bank, int side, Vector3 position, Vector3 direction)
        {
            var outlet = new GameObject("GrozaCountermeasure_" + bank + (side < 0 ? "_L" : "_R")).transform;
            outlet.SetParent(owner.transform, false);
            position.x *= side;
            direction.x *= side;
            outlet.SetPositionAndRotation(root.TransformPoint(position), root.rotation * Quaternion.LookRotation(direction));
            return outlet;
        }
    }
}
