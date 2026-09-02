using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// Selects vanilla unit definitions for support actions. The encyclopedia is classified
    /// once per scene and every later lookup reads the cached table; the previous version
    /// rescanned the whole list with a <c>GetComponent</c> per entry on every request.
    /// </summary>
    internal sealed class VanillaSupportCatalog
    {
        private static readonly FieldInfo ParachuteSystem =
            AccessTools.Field(typeof(GroundVehicle), "parachuteSystem");

        /// <summary>
        /// The reveal seam recon needs. Private in the game, so it is probed once and the whole
        /// action is left out of the catalogue when it cannot be resolved, rather than failing
        /// at request time.
        /// </summary>
        internal static readonly MethodInfo SetTrackingState =
            AccessTools.Method(typeof(FactionHQ), "SetTrackingState",
                new[] { typeof(PersistentID), typeof(GlobalPosition), typeof(float) });

        private readonly struct Candidate
        {
            public readonly VehicleDefinition Definition;
            public readonly bool Parachute;
            public readonly bool AntiAir;

            public Candidate(VehicleDefinition definition, bool parachute, bool antiAir)
            {
                Definition = definition;
                Parachute = parachute;
                AntiAir = antiAir;
            }
        }

        private readonly List<Candidate> vehicles = new List<Candidate>(64);
        private bool classified;

        public static bool ReconAvailable => SetTrackingState != null;

        public void Reset()
        {
            vehicles.Clear();
            classified = false;
        }

        /// <summary>Parachute-capable ground unit whose role is primarily anti-surface.</summary>
        public VehicleDefinition Armour(string key, FactionHQ owner, int needed) =>
            Pick(key, true, false, owner, needed);

        /// <summary>Parachute-capable ground unit whose role is primarily anti-air.</summary>
        public VehicleDefinition AirDefence(string key, FactionHQ owner, int needed) =>
            Pick(key, true, true, owner, needed);

        /// <summary>Any anti-surface ground unit; a convoy is driven in, not dropped.</summary>
        public VehicleDefinition Convoy(string key, FactionHQ owner, int needed) =>
            Pick(key, false, false, owner, needed);

        public MissileDefinition Artillery(string key)
        {
            if (string.IsNullOrEmpty(key) || Encyclopedia.i == null || Encyclopedia.i.missiles == null)
                return null;
            for (int i = 0; i < Encyclopedia.i.missiles.Count; i++)
            {
                MissileDefinition definition = Encyclopedia.i.missiles[i];
                if (definition == null || definition.unitPrefab == null) continue;
                if (!string.Equals(definition.jsonKey, key.Trim(), StringComparison.Ordinal)) continue;
                Missile missile = definition.unitPrefab.GetComponent<Missile>();
                float yield = missile != null ? missile.GetYield() : 0f;
                return yield > 0f && yield <= 200f ? definition : null;
            }
            return null;
        }

        /// <summary>
        /// Picks the first matching definition the faction can actually deliver. Selecting the
        /// first match regardless of stock is what made every drop come back NoStock on a map
        /// where the leading candidate happened to be depleted; a definition with stock is
        /// always preferred, and the first match is only used as a fallback so the caller
        /// still gets a typed NoStock rather than CapabilityUnavailable.
        /// </summary>
        private VehicleDefinition Pick(
            string key, bool requireParachute, bool antiAir, FactionHQ owner, int needed)
        {
            Classify();
            string wanted = string.IsNullOrEmpty(key) ? null : key.Trim();
            VehicleDefinition fallback = null;
            for (int i = 0; i < vehicles.Count; i++)
            {
                Candidate candidate = vehicles[i];
                if (requireParachute && !candidate.Parachute) continue;
                if (candidate.AntiAir != antiAir) continue;
                if (wanted != null &&
                    !string.Equals(candidate.Definition.jsonKey, wanted, StringComparison.Ordinal))
                    continue;
                if (fallback == null) fallback = candidate.Definition;
                if (owner == null || owner.GetUnitSupply(candidate.Definition) >= needed)
                    return candidate.Definition;
            }
            return fallback;
        }

        /// <summary>
        /// Roles come from the vanilla <c>roleIdentity</c>, so "armour" and "air defence" stay
        /// correct if the game rebalances its ground units or adds new ones.
        /// </summary>
        private void Classify()
        {
            if (classified) return;
            if (Encyclopedia.i == null || Encyclopedia.i.vehicles == null) return;
            classified = true;
            for (int i = 0; i < Encyclopedia.i.vehicles.Count; i++)
            {
                VehicleDefinition definition = Encyclopedia.i.vehicles[i];
                if (definition == null || definition.unitPrefab == null) continue;
                GroundVehicle vehicle = definition.unitPrefab.GetComponent<GroundVehicle>();
                if (vehicle == null) continue;
                vehicles.Add(new Candidate(
                    definition,
                    ParachuteSystem != null && ParachuteSystem.GetValue(vehicle) != null,
                    definition.roleIdentity.antiAir > definition.roleIdentity.antiSurface));
            }
        }
    }
}
