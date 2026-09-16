using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Orbital;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Runtime
{
    internal enum OpsCommand : byte
    {
        /// <summary>Launch the core (Arg = <see cref="ModuleKind.Core"/>, Arg2 = orbit band) or a
        /// module (Arg = <see cref="ModuleKind"/>, Arg2 = grid cell).</summary>
        Launch = 0,

        // 1 was the station-keeping transfer; retired with the constellation model.

        /// <summary>Jettison the module in a grid cell; the core deorbits the station. Arg = cell.</summary>
        Jettison = 2,

        Upgrade = 3,
        EwDeploy = 4,
        EwReposition = 5,

        /// <summary>Fund the next tier of a SPEC OPS or INTEL program. Arg = <see cref="OpsProgramId"/>.</summary>
        Invest = 6,

        /// <summary>Retune the faction's EW station. Arg = <see cref="EwPosture"/>.</summary>
        EwRetune = 7,

        /// <summary>Raise the base of operations one rank. Arg = <see cref="GarrisonUpgradeId"/>.</summary>
        GarrisonUpgrade = 8,

        /// <summary>Phasing burn: the station's next pass begins in seconds.</summary>
        Rephase = 9,

        /// <summary>Raise or lower the station. Arg = target orbit band.</summary>
        OrbitShift = 10,

        /// <summary>Launch a cargo vehicle that refills fuel and rods.</summary>
        Resupply = 11
    }

    /// <summary>
    /// Per-faction orbital, cyber and program systems. The host owns every mutation and
    /// charge; a client keeps the same model objects and rebuilds them from snapshots, so pass
    /// geometry is computed locally while authority stays with the server. At most eight
    /// factions, one station each, four facilities and six programs are tracked, plus up to
    /// four foreign stations a client has been told about; scene teardown clears it.
    /// </summary>
    internal sealed class SpaceOperations
    {
        public const int MaximumFactions = 8;
        public const int MaximumForeign = 4;
        public const double DebrisOfflineSeconds = 45.0;
        private const float DebrisMinimumSeconds = 360f;
        private const float DebrisMaximumSeconds = 600f;

        private sealed class FactionSystems
        {
            public readonly OrbitalPlatform Platform = new OrbitalPlatform();
            public readonly InfoNetwork Info = new InfoNetwork();
            public readonly OpsProgramLedger Programs = new OpsProgramLedger();
            public readonly OpsGarrison Garrison = new OpsGarrison();
        }

        private readonly Dictionary<FactionHQ, FactionSystems> systems = new Dictionary<FactionHQ, FactionSystems>();
        private readonly List<ForeignPlatform> foreign = new List<ForeignPlatform>(MaximumForeign);

        /// <summary>Other factions' stations as the last snapshot described them.</summary>
        public IReadOnlyList<ForeignPlatform> Foreign => foreign;

        public OrbitalPlatform PlatformFor(FactionHQ hq)
        {
            FactionSystems state = Get(hq);
            return state == null ? null : state.Platform;
        }

        public InfoNetwork InfoFor(FactionHQ hq)
        {
            FactionSystems state = Get(hq);
            return state == null ? null : state.Info;
        }

        public OpsProgramLedger ProgramsFor(FactionHQ hq)
        {
            FactionSystems state = Get(hq);
            return state == null ? null : state.Programs;
        }

        /// <summary>The faction's base-of-operations ranks; the host owns them.</summary>
        public OpsGarrison GarrisonFor(FactionHQ hq)
        {
            FactionSystems state = Get(hq);
            return state == null ? null : state.Garrison;
        }

        /// <summary>
        /// Host tick: station docking, power, drag and debris, and program accrual. Clients only
        /// mirror all of it. Debris rolls use Unity's random source; the model decides the outcome.
        /// </summary>
        public void TickHost(double now, float deltaTime, bool theaterDaylight, in OrbitClock clock, bool debris,
                             Action<FactionHQ, OrbitalPlatform> onDebris)
        {
            foreach (KeyValuePair<FactionHQ, FactionSystems> entry in systems)
            {
                OrbitalPlatform platform = entry.Value.Platform;
                platform.Tick(now, deltaTime, theaterDaylight, clock);
                entry.Value.Programs.Tick(deltaTime);
                if (!platform.Exists || !debris) continue;
                if (platform.NextDebris <= 0.0)
                {
                    platform.NextDebris = now + UnityEngine.Random.Range(DebrisMinimumSeconds, DebrisMaximumSeconds);
                    continue;
                }
                if (now < platform.NextDebris) continue;
                platform.NextDebris = now + UnityEngine.Random.Range(DebrisMinimumSeconds, DebrisMaximumSeconds);
                if (platform.StrikeDebris(UnityEngine.Random.Range(0, 1 << 16), now, DebrisOfflineSeconds))
                    onDebris?.Invoke(entry.Key, platform);
            }
        }

        public void Clear()
        {
            systems.Clear();
            foreign.Clear();
        }

        // ---- Host commands -----------------------------------------------------------------

        public bool Upgrade(FactionHQ hq, FacilityId facility)
        {
            FactionSystems state = Get(hq);
            return state != null && state.Info.TryUpgrade(facility);
        }

        public bool Invest(FactionHQ hq, OpsProgramId program)
        {
            FactionSystems state = Get(hq);
            return state != null && state.Programs.TryInvest(program);
        }

        public bool UpgradeGarrison(FactionHQ hq, GarrisonUpgradeId upgrade)
        {
            FactionSystems state = Get(hq);
            return state != null && state.Garrison.TryUpgrade(upgrade);
        }

        /// <summary>Host: up to <see cref="MaximumForeign"/> stations belonging to anyone but
        /// <paramref name="hq"/>, for the sky, the map and ENEMY ACTIVITY. Modules are not
        /// disclosed, only which cells are occupied.</summary>
        public int CollectForeign(FactionHQ hq, double now, byte[] regimes, int[] seeds, float[] clocks, int[] layouts)
        {
            int count = 0;
            foreach (KeyValuePair<FactionHQ, FactionSystems> entry in systems)
            {
                if (entry.Key == hq) continue;
                OrbitalPlatform platform = entry.Value.Platform;
                if (!platform.Exists) continue;
                if (count >= MaximumForeign || count >= regimes.Length || count >= seeds.Length ||
                    count >= clocks.Length || count >= layouts.Length) return count;
                regimes[count] = platform.Regime;
                seeds[count] = platform.Seed;
                clocks[count] = (float)(now - platform.CycleStart);
                layouts[count] = platform.LayoutMask();
                count++;
            }
            return count;
        }

        // ---- Client mirror -----------------------------------------------------------------

        public void Mirror(FactionHQ hq, byte sigint, byte crypto, byte disrupt, byte ew)
        {
            FactionSystems state = Get(hq);
            if (state != null) state.Info.Mirror(sigint, crypto, disrupt, ew);
        }

        public void MirrorPrograms(FactionHQ hq, byte[] tiers, byte specOpsTokens, byte intelTokens,
                                   byte specOpsProgress, byte intelProgress)
        {
            FactionSystems state = Get(hq);
            if (state != null) state.Programs.Mirror(tiers, specOpsTokens, intelTokens, specOpsProgress, intelProgress);
        }

        public void MirrorGarrison(FactionHQ hq, byte[] levels)
        {
            FactionSystems state = Get(hq);
            if (state != null) state.Garrison.Mirror(levels);
        }

        public void MirrorPlatform(FactionHQ hq, PlatformSnapshot snapshot, double now)
        {
            FactionSystems state = Get(hq);
            if (state != null) state.Platform.Mirror(snapshot, now);
        }

        public void MirrorForeign(byte[] regimes, int[] seeds, float[] clocks, int[] layouts, int count, double now)
        {
            int bounded = Math.Min(Math.Min(count, MaximumForeign), Math.Min(regimes?.Length ?? 0,
                Math.Min(seeds?.Length ?? 0, Math.Min(clocks?.Length ?? 0, layouts?.Length ?? 0))));
            bounded = Math.Max(0, bounded);
            while (foreign.Count > bounded) foreign.RemoveAt(foreign.Count - 1);
            for (int i = 0; i < bounded; i++)
            {
                if (!OrbitRegimes.Valid(regimes[i]) || float.IsNaN(clocks[i]) || float.IsInfinity(clocks[i])) continue;
                if (i >= foreign.Count) foreign.Add(new ForeignPlatform());
                foreign[i].Mirror(regimes[i], seeds[i], now - clocks[i],
                    (ushort)(layouts[i] & ((1 << OrbitalPlatform.CellCount) - 1)));
            }
        }

        private FactionSystems Get(FactionHQ hq)
        {
            if (hq == null) return null;
            if (systems.TryGetValue(hq, out FactionSystems state)) return state;
            if (systems.Count >= MaximumFactions) return null;
            if (OrbitalBounds.Radius() <= 0f) return null;
            state = new FactionSystems();
            systems.Add(hq, state);
            return state;
        }
    }

    /// <summary>Map extents. Zero when no map is loaded.</summary>
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
