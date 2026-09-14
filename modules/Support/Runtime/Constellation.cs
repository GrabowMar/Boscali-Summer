using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>Wire-stable satellite role bytes; one role backs one capability family.</summary>
    internal enum SatelliteRole : byte
    {
        Recon = 0,
        Strike = 1,
        Ew = 2
    }

    internal enum SatelliteState : byte
    {
        Stationed = 0,
        Transit = 1
    }

    internal enum OrbitalFailure : byte
    {
        None = 0,
        AtCapacity = 1,
        UnknownSatellite = 2,
        InsufficientFuel = 3,
        UnknownAltitude = 4
    }

    /// <summary>
    /// Station-keeping altitude. Low holds a small footprint but repositions quickly and
    /// cheaply; high covers a wide area but costs fuel and time to move.
    /// </summary>
    internal readonly struct SatelliteAltitude
    {
        public readonly byte Index;
        public readonly string Name;
        public readonly float Swath;
        public readonly float TransitSpeed;
        public readonly float FuelPerKm;

        public SatelliteAltitude(byte index, string name, float swath, float transitSpeed, float fuelPerKm)
        {
            Index = index;
            Name = name;
            Swath = swath;
            TransitSpeed = transitSpeed;
            FuelPerKm = fuelPerKm;
        }
    }

    /// <summary>Answer for one role over one point: covered, or how far away the nearest is.</summary>
    internal readonly struct StationCoverage
    {
        public readonly bool Covered;
        public readonly byte SatelliteId;
        public readonly float Distance;
        public readonly float NearestGap;
        public readonly bool HasSatellite;

        public StationCoverage(bool covered, byte satelliteId, float distance, float nearestGap, bool hasSatellite)
        {
            Covered = covered;
            SatelliteId = satelliteId;
            Distance = distance;
            NearestGap = nearestGap;
            HasSatellite = hasSatellite;
        }
    }

    internal sealed class Satellite
    {
        public byte Id;
        public SatelliteRole Role;
        public byte Altitude;

        /// <summary>Station the satellite is holding or transferring to.</summary>
        public float StationX, StationZ;

        /// <summary>Where the current transfer started; equals the station while stationed.</summary>
        public float OriginX, OriginZ;

        public float TransitLeft, TransitTotal;
        public float Fuel;

        /// <summary>Allocation actually charged at deploy, for an exact recall refund.</summary>
        public float Paid = 0f;

        public SatelliteState State;

        public SatelliteAltitude Orbit => Constellation.Altitude(Altitude);
    }

    /// <summary>
    /// Host-authoritative constellation for one faction: up to four satellites holding
    /// stations over the theatre. Any point is reachable — the player pays fuel and transit
    /// time to move one there. Clients mirror snapshots and advance transfers locally, so
    /// station coverage reads live without a per-frame host round trip. Pure C# so the
    /// movement and coverage math is testable without the game.
    /// </summary>
    internal sealed class Constellation
    {
        public const int MaximumSatellites = 4;
        public const float MaximumFuel = 100f;
        public const float FuelRegenPerSecond = 0.8f;
        public const float MinimumTransferDistance = 250f;

        private static readonly SatelliteAltitude[] altitudes =
        {
            new SatelliteAltitude(0, "LOW", 9000f, 6000f, 0.22f),
            new SatelliteAltitude(1, "MID", 13000f, 4500f, 0.32f),
            new SatelliteAltitude(2, "HIGH", 18000f, 3000f, 0.42f)
        };

        private readonly List<Satellite> satellites = new List<Satellite>(MaximumSatellites);
        private readonly float mapRadius;
        private byte nextId = 1;

        public Constellation(float mapRadius)
        {
            this.mapRadius = Math.Max(1000f, mapRadius);
        }

        public IReadOnlyList<Satellite> Satellites => satellites;
        public float MapRadius => mapRadius;
        public static int AltitudeCount => altitudes.Length;
        public static SatelliteAltitude Altitude(int index) =>
            altitudes[Math.Max(0, Math.Min(altitudes.Length - 1, index))];

        public Satellite Find(byte id)
        {
            for (int i = 0; i < satellites.Count; i++)
                if (satellites[i].Id == id) return satellites[i];
            return null;
        }

        /// <summary>
        /// Deploy a new satellite. It does not appear on station instantly — it launches into
        /// the same <see cref="SatelliteState.Transit"/> a repositioning satellite already
        /// uses, for <paramref name="launchTransitSeconds"/>, with no horizontal travel (a
        /// launch goes up, not sideways across the map). Reusing Transit rather than adding a
        /// new state means coverage is correctly denied for the whole launch window for free —
        /// <see cref="Query"/> and <see cref="SatelliteCovers"/> already refuse anything that
        /// isn't <see cref="SatelliteState.Stationed"/>.
        /// </summary>
        public bool TryDeploy(SatelliteRole role, byte altitude, float stationX, float stationZ,
                              int capacity, float launchTransitSeconds,
                              out Satellite satellite, out OrbitalFailure failure)
        {
            satellite = null;
            if (satellites.Count >= Math.Max(1, Math.Min(MaximumSatellites, capacity)))
            {
                failure = OrbitalFailure.AtCapacity;
                return false;
            }
            if (altitude >= altitudes.Length)
            {
                failure = OrbitalFailure.UnknownAltitude;
                return false;
            }
            float transit = Math.Max(0f, launchTransitSeconds);
            satellite = new Satellite
            {
                Id = nextId++,
                Role = role,
                Altitude = altitude,
                StationX = stationX,
                StationZ = stationZ,
                OriginX = stationX,
                OriginZ = stationZ,
                Fuel = MaximumFuel,
                TransitTotal = transit,
                TransitLeft = transit,
                State = transit > 0f ? SatelliteState.Transit : SatelliteState.Stationed
            };
            nextId = nextId == 0 ? (byte)1 : nextId;
            satellites.Add(satellite);
            failure = OrbitalFailure.None;
            return true;
        }

        /// <summary>Order a transfer to a new station. Fuel is charged up front.</summary>
        public bool TryRetask(byte id, float stationX, float stationZ, out Satellite satellite,
                              out float fuelCost, out OrbitalFailure failure)
        {
            satellite = Find(id);
            fuelCost = 0f;
            if (satellite == null)
            {
                failure = OrbitalFailure.UnknownSatellite;
                return false;
            }

            Position(satellite, out float fromX, out float fromZ);
            float distance = Distance(fromX, fromZ, stationX, stationZ);
            SatelliteAltitude altitude = satellite.Orbit;
            fuelCost = distance / 1000f * altitude.FuelPerKm;
            if (satellite.Fuel + 0.001f < fuelCost)
            {
                failure = OrbitalFailure.InsufficientFuel;
                return false;
            }

            satellite.Fuel = Math.Max(0f, satellite.Fuel - fuelCost);
            if (distance <= MinimumTransferDistance)
            {
                satellite.StationX = stationX;
                satellite.StationZ = stationZ;
                satellite.OriginX = stationX;
                satellite.OriginZ = stationZ;
                satellite.TransitLeft = 0f;
                satellite.TransitTotal = 0f;
                satellite.State = SatelliteState.Stationed;
                failure = OrbitalFailure.None;
                return true;
            }

            satellite.OriginX = fromX;
            satellite.OriginZ = fromZ;
            satellite.TransitTotal = distance / altitude.TransitSpeed;
            satellite.TransitLeft = satellite.TransitTotal;
            satellite.State = SatelliteState.Transit;
            satellite.StationX = stationX;
            satellite.StationZ = stationZ;
            failure = OrbitalFailure.None;
            return true;
        }

        public bool TryRecall(byte id, out Satellite satellite)
        {
            satellite = Find(id);
            if (satellite == null) return false;
            satellites.Remove(satellite);
            return true;
        }

        /// <summary>Advance transfers. Only the host regenerates fuel; clients only move.</summary>
        public void Tick(float deltaTime, bool regenerateFuel)
        {
            if (deltaTime <= 0f) return;
            for (int i = 0; i < satellites.Count; i++)
            {
                Satellite satellite = satellites[i];
                if (satellite.State == SatelliteState.Transit)
                {
                    satellite.TransitLeft -= deltaTime;
                    if (satellite.TransitLeft <= 0f)
                    {
                        satellite.TransitLeft = 0f;
                        satellite.OriginX = satellite.StationX;
                        satellite.OriginZ = satellite.StationZ;
                        satellite.State = SatelliteState.Stationed;
                    }
                }
                else if (regenerateFuel && satellite.Fuel < MaximumFuel)
                {
                    satellite.Fuel = Math.Min(MaximumFuel, satellite.Fuel + FuelRegenPerSecond * deltaTime);
                }
            }
        }

        /// <summary>Current position: the station, or the interpolated transfer point.</summary>
        public void Position(Satellite satellite, out float x, out float z)
        {
            if (satellite == null)
            {
                x = z = 0f;
                return;
            }
            if (satellite.State != SatelliteState.Transit || satellite.TransitTotal <= 0f)
            {
                x = satellite.StationX;
                z = satellite.StationZ;
                return;
            }
            float progress = 1f - satellite.TransitLeft / satellite.TransitTotal;
            x = satellite.OriginX + (satellite.StationX - satellite.OriginX) * progress;
            z = satellite.OriginZ + (satellite.StationZ - satellite.OriginZ) * progress;
        }

        /// <summary>Best stationed satellite of a role for a point, covered or not.</summary>
        public StationCoverage Query(SatelliteRole role, float x, float z)
        {
            Satellite best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < satellites.Count; i++)
            {
                Satellite satellite = satellites[i];
                if (satellite.Role != role || satellite.State != SatelliteState.Stationed) continue;
                float distance = Distance(satellite.StationX, satellite.StationZ, x, z);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = satellite;
                }
            }

            if (best == null) return new StationCoverage(false, 0, 0f, 0f, false);
            float gap = bestDistance - best.Orbit.Swath;
            return new StationCoverage(gap <= 0f, best.Id, bestDistance, gap > 0f ? gap : 0f, true);
        }

        public bool Covers(SatelliteRole role, float x, float z) => Query(role, x, z).Covered;

        /// <summary>True when this exact satellite's swath contains the point.</summary>
        public bool SatelliteCovers(Satellite satellite, float x, float z)
        {
            if (satellite == null || satellite.State != SatelliteState.Stationed) return false;
            float swath = satellite.Orbit.Swath;
            return Distance(satellite.StationX, satellite.StationZ, x, z) <= swath;
        }

        public void Mirror(byte id, SatelliteRole role, byte altitude, float stationX, float stationZ,
                           float originX, float originZ, float transitLeft, float transitTotal,
                           float fuel, SatelliteState state)
        {
            Satellite satellite = Find(id);
            if (satellite == null)
            {
                if (satellites.Count >= MaximumSatellites) return;
                if (nextId <= id) nextId = (byte)(id + 1);
                satellite = new Satellite { Id = id };
                satellites.Add(satellite);
            }
            satellite.Role = role;
            satellite.Altitude = altitude;
            satellite.StationX = stationX;
            satellite.StationZ = stationZ;
            satellite.OriginX = originX;
            satellite.OriginZ = originZ;
            satellite.TransitLeft = Math.Max(0f, transitLeft);
            satellite.TransitTotal = Math.Max(0f, transitTotal);
            satellite.Fuel = Math.Max(0f, Math.Min(MaximumFuel, fuel));
            satellite.State = state;
        }

        public void RemoveById(byte id)
        {
            for (int i = satellites.Count - 1; i >= 0; i--)
                if (satellites[i].Id == id) satellites.RemoveAt(i);
        }

        public void Clear()
        {
            satellites.Clear();
            nextId = 1;
        }

        /// <summary>Fuel percent a transfer between two points costs. Shared with UI previews.</summary>
        public static float TransferCost(float fromX, float fromZ, float toX, float toZ, byte altitude)
        {
            float distance = Distance(fromX, fromZ, toX, toZ);
            return distance / 1000f * Altitude(altitude).FuelPerKm;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
