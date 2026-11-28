using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>Orbital commands and maps shared by the host and the ORBIT view.</summary>
    internal static class GeoSpace
    {
        /// <summary>Footprint radius per bird (OPTICAL, RADAR, KINETIC) as a share of the theatre width (optical / radar swath, kinetic strike reach).</summary>
        public static readonly float[] Reach = { 0.14f, 0.20f, 0.10f };
        public static readonly string[] Names = { "OPTICAL", "RADAR", "KINETIC" };
        /// <summary>The bird must have this much fuel (percent) for the SPACE director to start a burn.</summary>
        public const float DirectorMinFuel = 30f;
        /// <summary>Quiet time between two director burns of one bird (mission seconds).</summary>
        public const float DirectorGap = 180f;
        private const int Grid = 16383;

        public static float Footprint(BirdKind bird, SatelliteBus bus) => Reach[(int)bird] * (bus switch
        {
            SatelliteBus.Agile => 0.82f,
            SatelliteBus.Wideband => 1.25f,
            _ => 1f
        });

        public static byte PackLaunch(BirdKind bird, SatelliteBus bus) => (byte)((byte)bird | ((byte)bus << 2));

        public static bool TryUnpackLaunch(byte packed, out BirdKind bird, out SatelliteBus bus)
        {
            bird = (BirdKind)(packed & 3);
            bus = (SatelliteBus)(packed >> 2);
            return (byte)bird < SpaceRules.BirdCount && bus <= SatelliteBus.Wideband;
        }

        public static float U(float x, float width) => Scalar.Clamp01(x / Math.Max(1000f, width) + 0.5f);
        public static float V(float z, float height) => Scalar.Clamp01(0.5f - z / Math.Max(1000f, height));

        /// <summary>Wire form of a map point: two 14-bit cells in one non-negative int.</summary>
        public static int Pack(float u, float v) => (int)Math.Round(Scalar.Clamp01(u) * Grid) | ((int)Math.Round(Scalar.Clamp01(v) * Grid) << 14);

        public static bool TryUnpack(int packed, out float u, out float v)
        {
            u = v = 0f;
            if (packed < 0) return false;
            u = (packed & Grid) / (float)Grid; v = ((packed >> 14) & Grid) / (float)Grid;
            return true;
        }

        /// <summary>Why a burn was refused, or None when it started.</summary>
        public enum Refusal : byte { None, Dead, Moving, NoMove, NoFuel }

        public static Refusal Check(in GeoBird bird, bool alive, float now, float u, float v) =>
            !alive ? Refusal.Dead : PlanBurn(bird, now, u, v, out _);

        public static float BurnFuelCost(in GeoBird bird, float u, float v)
        {
            NodeChange(bird, GeoBird.ClampStation(u), GeoBird.ClampStation(v), out _, out float across);
            return 100f * GeoOrbit.NodeDeltaV(bird.Payload, Math.Abs(across)) / bird.DeltaVCapacity;
        }

        public static Refusal PlanBurn(in GeoBird bird, float now, float u, float v, out GeoBird after)
        {
            after = bird;
            if (!bird.Orbital || !SpaceRules.MissionTime(now)) return Refusal.Dead;
            if (bird.Moving(now)) return Refusal.Moving;
            if (!float.IsFinite(u) || !float.IsFinite(v)) return Refusal.NoMove;
            u = GeoBird.ClampStation(u); v = GeoBird.ClampStation(v);
            NodeChange(bird, u, v, out float along, out float across);
            float cost = 100f * GeoOrbit.NodeDeltaV(bird.Payload, Math.Abs(across)) / bird.DeltaVCapacity;
            if (cost <= 0f) return Refusal.NoMove;
            if (bird.Fuel < cost) return Refusal.NoFuel;
            float arrival = now + (12f + 54f * Math.Abs(across)) / GeoOrbit.TransferRate(bird.Bus);
            after = Transfer(bird, now, u, v, bird.EpochAt + along / bird.TrackSpeed, arrival, cost);
            return Refusal.None;
        }

        public static float RephaseFuelCost(in GeoBird bird, float shift) => 100f * GeoOrbit.PhaseDeltaV(bird.Payload, shift) / bird.DeltaVCapacity;

        public static Refusal PlanRephase(in GeoBird bird, float now, float shift, out GeoBird after)
        {
            after = bird;
            if (!bird.Orbital || !SpaceRules.MissionTime(now)) return Refusal.Dead;
            if (bird.Moving(now)) return Refusal.Moving;
            if (!float.IsFinite(shift) || Math.Abs(shift) < 1f || Math.Abs(shift) > GeoOrbit.RephaseStepSeconds) return Refusal.NoMove;
            float cost = RephaseFuelCost(bird, shift);
            if (bird.Fuel < cost) return Refusal.NoFuel;
            float arrival = now + 14f / GeoOrbit.TransferRate(bird.Bus);
            after = Transfer(bird, now, bird.ToU, bird.ToV, bird.EpochAt + shift, arrival, cost);
            return Refusal.None;
        }

        private static void NodeChange(in GeoBird bird, float u, float v, out float along, out float across) =>
            GeoOrbit.Components(bird.Payload, u - bird.ToU, (v - bird.ToV) * GeoBird.Aspect, out along, out across);

        private static GeoBird Transfer(in GeoBird bird, float now, float u, float v, float epoch, float arrival, float cost) =>
            new GeoBird(bird.ToU, bird.ToV, u, v, now, bird.Fuel - cost, bird.Bus, bird.Payload, bird.EpochAt, epoch, arrival,
                bird.OrbitRevision == int.MaxValue ? 1 : bird.OrbitRevision + 1);

        /// <summary>
        /// The director spends on an uncovered sector only if the bird's established track has no pass there.
        /// </summary>
        public static int PickBird(SpaceState state, float now, float[] restUntil, float u, float v)
        {
            int best = -1; float bestCost = float.MaxValue;
            for (int i = 0; i < SpaceRules.BirdCount; i++)
            {
                var kind = (BirdKind)i;
                GeoBird g = state.Geo(kind);
                if (!state.HasBird(kind) || g.Moving(now) || g.Fuel <= DirectorMinFuel || now < restUntil[i] ||
                    g.NextPass(now, u, v, Footprint(kind, g.Bus), out _)) continue;
                float cost = BurnFuelCost(g, u, v);
                if (cost > 0f && cost < bestCost && cost < g.Fuel - DirectorMinFuel * 0.5f) { bestCost = cost; best = i; }
            }
            return best;
        }

        /// <summary>Parked points over the faction's side of the front: from the own airbase centroid toward the enemy's, a different share per bird.</summary>
        public static void Defaults(float ownU, float ownV, float foeU, float foeV, float[] u, float[] v)
        {
            float[] share = { 0.40f, 0.30f, 0.50f };
            float du = foeU - ownU, dv = foeV - ownV, len = (float)Math.Sqrt(du * du + dv * dv);
            float pu = len > 0.001f ? -dv / len : 0f, pv = len > 0.001f ? du / len : 0f; // spread across the front, never stacked
            float[] side = { -0.08f, 0.08f, 0f };
            for (int i = 0; i < 3; i++)
            {
                u[i] = Math.Clamp(ownU + du * share[i] + pu * side[i], 0.05f, 0.95f);
                v[i] = Math.Clamp(ownV + dv * share[i] + pv * side[i], 0.05f, 0.95f);
            }
        }
    }
}
