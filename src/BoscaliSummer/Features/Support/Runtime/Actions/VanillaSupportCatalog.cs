using System;
using System.Reflection;
using BoscaliSummer.Features.Support.Configuration;
using HarmonyLib;

namespace BoscaliSummer.Features.Support.Runtime.Actions
{
    internal static class VanillaSupportCatalog
    {
        private static readonly FieldInfo ParachuteSystem =
            AccessTools.Field(typeof(GroundVehicle), "parachuteSystem");

        public static VehicleDefinition ResolveAirdropVehicle(SupportSettings settings)
        {
            if (Encyclopedia.i?.vehicles == null || ParachuteSystem == null) return null;
            string requested = settings.VehicleDefinitionKey.Value?.Trim();
            for (int i = 0; i < Encyclopedia.i.vehicles.Count; i++)
            {
                VehicleDefinition definition = Encyclopedia.i.vehicles[i];
                if (definition?.unitPrefab == null) continue;
                if (!string.IsNullOrEmpty(requested) &&
                    !string.Equals(definition.jsonKey, requested, StringComparison.Ordinal)) continue;
                GroundVehicle vehicle = definition.unitPrefab.GetComponent<GroundVehicle>();
                if (vehicle != null && ParachuteSystem.GetValue(vehicle) != null) return definition;
            }
            return null;
        }

        public static MissileDefinition ResolveArtilleryOrdnance(SupportSettings settings)
        {
            if (Encyclopedia.i?.missiles == null) return null;
            string requested = settings.ArtilleryDefinitionKey.Value?.Trim();
            if (string.IsNullOrEmpty(requested)) return null;
            for (int i = 0; i < Encyclopedia.i.missiles.Count; i++)
            {
                MissileDefinition definition = Encyclopedia.i.missiles[i];
                if (definition?.unitPrefab == null || definition.jsonKey != requested) continue;
                Missile missile = definition.unitPrefab.GetComponent<Missile>();
                float yield = missile != null ? missile.GetYield() : 0f;
                return yield > 0f && yield <= 200f ? definition : null;
            }
            return null;
        }
    }
}
