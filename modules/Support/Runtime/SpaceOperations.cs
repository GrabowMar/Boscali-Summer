using System;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    internal enum OpsCommand : byte
    {
        Launch = 0,
        Move = 1,
        Recall = 2,
        Upgrade = 3,
        EwDeploy = 4,
        EwReposition = 5
    }

    /// <summary>
    /// Per-faction orbital and cyber systems. The host owns every mutation and charge; a
    /// client keeps the same model objects and rebuilds them from snapshots, so station
    /// coverage is computed locally while authority stays with the server. At most eight
    /// factions, four satellites and four facilities are tracked; scene teardown clears it.
    /// </summary>
    internal sealed class SpaceOperations
    {
        public const int MaximumFactions = 8;
        public const int MaximumSatellites = Constellation.MaximumSatellites;

        private sealed class FactionSystems
        {
            public Constellation Constellation;
            public readonly InfoNetwork Info = new InfoNetwork();
        }

        private readonly Dictionary<FactionHQ, FactionSystems> systems = new Dictionary<FactionHQ, FactionSystems>();

        public Constellation ConstellationFor(FactionHQ hq)
        {
            FactionSystems state = Get(hq);
            return state == null ? null : state.Constellation;
        }

        public InfoNetwork InfoFor(FactionHQ hq)
        {
            FactionSystems state = Get(hq);
            return state == null ? null : state.Info;
        }

        public void Tick(float deltaTime, bool hostAuthoritative)
        {
            foreach (FactionSystems state in systems.Values)
                state.Constellation.Tick(deltaTime, hostAuthoritative);
        }

        public void Clear()
        {
            systems.Clear();
        }

        // ---- Host commands -----------------------------------------------------------------

        public OrbitalFailure Deploy(FactionHQ hq, SatelliteRole role, byte altitude, float x, float z,
                                     int capacity, float launchTransitSeconds, out Satellite satellite)
        {
            satellite = null;
            FactionSystems state = Get(hq);
            if (state == null) return OrbitalFailure.UnknownSatellite;
            return state.Constellation.TryDeploy(role, altitude, x, z, capacity, launchTransitSeconds,
                out satellite, out OrbitalFailure failure) ? OrbitalFailure.None : failure;
        }

        public OrbitalFailure Retask(FactionHQ hq, byte satelliteId, float x, float z, out float fuelCost)
        {
            fuelCost = 0f;
            FactionSystems state = Get(hq);
            if (state == null) return OrbitalFailure.UnknownSatellite;
            return state.Constellation.TryRetask(satelliteId, x, z, out _, out fuelCost,
                out OrbitalFailure failure) ? OrbitalFailure.None : failure;
        }

        public bool Recall(FactionHQ hq, byte satelliteId, out Satellite satellite)
        {
            satellite = null;
            FactionSystems state = Get(hq);
            return state != null && state.Constellation.TryRecall(satelliteId, out satellite);
        }

        public bool Upgrade(FactionHQ hq, FacilityId facility)
        {
            FactionSystems state = Get(hq);
            return state != null && state.Info.TryUpgrade(facility);
        }

        // ---- Client mirror -----------------------------------------------------------------

        public void Mirror(FactionHQ hq, byte sigint, byte crypto, byte disrupt, byte ew)
        {
            FactionSystems state = Get(hq);
            if (state != null) state.Info.Mirror(sigint, crypto, disrupt, ew);
        }

        public void Mirror(FactionHQ hq, byte id, SatelliteRole role, byte altitude,
                           float stationX, float stationZ, float originX, float originZ,
                           float transitLeft, float transitTotal, float fuel, SatelliteState satelliteState)
        {
            FactionSystems state = Get(hq);
            if (state == null) return;
            if (state.Constellation.Satellites.Count >= MaximumSatellites &&
                state.Constellation.Find(id) == null) return;
            state.Constellation.Mirror(id, role, altitude, stationX, stationZ, originX, originZ,
                transitLeft, transitTotal, fuel, satelliteState);
        }

        public void RemoveById(FactionHQ hq, byte id)
        {
            FactionSystems state = Get(hq);
            if (state != null) state.Constellation.RemoveById(id);
        }

        /// <summary>Mirror teardown: drop satellites the host no longer lists.</summary>
        public void RemoveUnlisted(FactionHQ hq, byte[] ids, int count)
        {
            Constellation constellation = ConstellationFor(hq);
            if (constellation == null) return;
            for (int i = constellation.Satellites.Count - 1; i >= 0; i--)
            {
                byte id = constellation.Satellites[i].Id;
                bool listed = false;
                for (int j = 0; j < count && ids != null && j < ids.Length; j++)
                {
                    if (ids[j] != id) continue;
                    listed = true;
                    break;
                }
                if (!listed) constellation.RemoveById(id);
            }
        }

        private FactionSystems Get(FactionHQ hq)
        {
            if (hq == null) return null;
            if (systems.TryGetValue(hq, out FactionSystems state)) return state;
            if (systems.Count >= MaximumFactions) return null;
            float radius = OrbitalBounds.Radius();
            if (radius <= 0f) return null;
            state = new FactionSystems { Constellation = new Constellation(radius) };
            systems.Add(hq, state);
            return state;
        }
    }

    /// <summary>Map extents used to size the schematic. Zero when no map is loaded.</summary>
    internal static class OrbitalBounds
    {
        public static float Radius()
        {
            Extents(out float halfWidth, out float halfHeight);
            return Math.Max(halfWidth, halfHeight);
        }

        public static void Extents(out float halfWidth, out float halfHeight)
        {
            halfWidth = halfHeight = 0f;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            MapSettings settings = level != null ? level.LoadedMapSettings : null;
            if (settings == null) return;
            Vector2 size = settings.MapSize;
            if (size.x <= 0f || size.y <= 0f) return;
            halfWidth = size.x * 0.5f;
            halfHeight = size.y * 0.5f;
        }
    }
}
