using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Domain.SpecOps;
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

        /// <summary>Buy a network-wide CYBER upgrade with allocation. Arg = <see cref="CyberUpgrade"/>.</summary>
        CyberUpgrade = 3,
        // 4 and 5 were the single EW truck's deploy and move; retired with the truck network.

        // 6 funded a SPEC OPS/INTEL program and 8 raised a doctrine rank; retired with the
        // detachment remake (2026-09-21). 7 was the EW truck's posture retune.

        /// <summary>Relocate to a fixed theatre sector. Protocol 18: Arg = sector 0..8.</summary>
        Rephase = 9,

        /// <summary>Raise or lower the station. Arg = target orbit band.</summary>
        OrbitShift = 10,

        /// <summary>Launch a cargo vehicle that refills fuel and rods.</summary>
        Resupply = 11,

        /// <summary>Breach a location. Arg = node slot, Arg2 = <see cref="BreachTool"/>.</summary>
        CyberBreach = 12,

        /// <summary>Choose a mastered location's capstone. Arg = <see cref="Capstone"/>.</summary>
        CyberChoice = 13,

        /// <summary>Console verb. Arg = slot or incident, Arg2 = <see cref="CyberVerb"/>.</summary>
        CyberVerb = 14,

        // 15 was the jammer posture retune and 16 the site move; retired with the truck network.

        // 18 and 19 started and played the retired SPEC OPS infiltration board.

        /// <summary>Raise an empty SPEC OPS team slot. Arg = team.</summary>
        SpecOpsRaise = 20,

        /// <summary>Send a team on a mission. Arg = team, Arg2 = <see cref="FieldMission"/>,
        /// Revision = the objective's anchor id, so a reshuffled list can never retarget it.</summary>
        SpecOpsLaunch = 21,

        /// <summary>Bring a deployed team home. Arg = team.</summary>
        SpecOpsRecall = 22
    }

    /// <summary>
    /// Per-faction orbital, CYBER network and SPEC OPS detachment systems. The host owns every
    /// mutation and charge; a client keeps the same model objects and rebuilds them from
    /// snapshots, so pass geometry is computed locally while authority stays with the server. At
    /// most eight factions, one station, one network and one detachment each, plus up to four
    /// foreign stations a client has been told about; scene teardown clears it.
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
            public readonly CyberNetwork Cyber = new CyberNetwork();
            public readonly SpecOpsDetachment Detachment = new SpecOpsDetachment();
        }

        private readonly Dictionary<FactionHQ, FactionSystems> systems = new Dictionary<FactionHQ, FactionSystems>();
        private readonly List<FactionHQ> order = new List<FactionHQ>(MaximumFactions);
        private float cyberReach = CyberLocations.DefaultReach;

        /// <summary>
        /// Base breach reach of every network, from <c>CyberReachMeters</c>. The host's value
        /// authorises breaches; a client's only changes what its own page predicts.
        /// </summary>
        public float CyberReach
        {
            get => cyberReach;
            set
            {
                if (cyberReach == value) return;
                cyberReach = value;
                foreach (FactionSystems state in systems.Values) state.Cyber.BaseReach = value;
            }
        }

        /// <summary>Factions with systems, in the order they were first seen.</summary>
        public int FactionCount => order.Count;

        public FactionHQ FactionAt(int index) => index >= 0 && index < order.Count ? order[index] : null;

        /// <summary>The faction's spectrum-defence network; the host owns it, clients mirror it.</summary>
        public CyberNetwork CyberFor(FactionHQ hq)
        {
            FactionSystems state = Get(hq);
            return state == null ? null : state.Cyber;
        }
        private readonly List<ForeignPlatform> foreign = new List<ForeignPlatform>(MaximumForeign);

        /// <summary>Other factions' stations as the last snapshot described them.</summary>
        public IReadOnlyList<ForeignPlatform> Foreign => foreign;

        public OrbitalPlatform PlatformFor(FactionHQ hq)
        {
            FactionSystems state = Get(hq);
            return state == null ? null : state.Platform;
        }

        /// <summary>The faction's SPEC OPS detachment; the host owns it, clients mirror it.</summary>
        public SpecOpsDetachment DetachmentFor(FactionHQ hq) => Get(hq)?.Detachment;

        /// <summary>
        /// Host tick: station docking, power, drag and debris, and the CYBER campaign. Clients only
        /// mirror all of it. Debris rolls use Unity's random source; the model decides the outcome.
        /// </summary>
        public void TickHost(double now, float deltaTime, bool theaterDaylight, in OrbitClock clock, bool debris,
                             float cyberIntensity, Action<FactionHQ, OrbitalPlatform> onDebris)
        {
            foreach (KeyValuePair<FactionHQ, FactionSystems> entry in systems)
            {
                OrbitalPlatform platform = entry.Value.Platform;
                platform.Tick(now, deltaTime, theaterDaylight, clock);
                entry.Value.Cyber.Tick(now, deltaTime, cyberIntensity);
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
            order.Clear();
            foreign.Clear();
        }

        // ---- Host commands -----------------------------------------------------------------

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

        public void MirrorDetachment(FactionHQ hq, SpecOpsSnapshot snapshot, double now)
        {
            FactionSystems state = Get(hq);
            if (state != null) state.Detachment.Mirror(snapshot, now);
        }

        public void MirrorCyber(FactionHQ hq, CyberSnapshot snapshot, double now)
        {
            FactionSystems state = Get(hq);
            if (state != null) state.Cyber.Mirror(snapshot, now);
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
            int accepted = 0;
            for (int i = 0; i < bounded; i++)
            {
                if (!OrbitRegimes.Valid(regimes[i]) || !StationKeeping.ValidRoute(seeds[i]) ||
                    float.IsNaN(clocks[i]) || float.IsInfinity(clocks[i])) continue;
                if (accepted >= foreign.Count) foreign.Add(new ForeignPlatform());
                foreign[accepted++].Mirror(regimes[i], seeds[i], now - clocks[i],
                    (ushort)(layouts[i] & ((1 << OrbitalPlatform.CellCount) - 1)));
            }
            while (foreign.Count > accepted) foreign.RemoveAt(foreign.Count - 1);
        }

        private FactionSystems Get(FactionHQ hq)
        {
            if (hq == null) return null;
            if (systems.TryGetValue(hq, out FactionSystems state)) return state;
            if (systems.Count >= MaximumFactions) return null;
            if (OrbitalBounds.Radius() <= 0f) return null;
            state = new FactionSystems();
            state.Cyber.BaseReach = cyberReach;
            state.Cyber.Seed(hq.GetInstanceID() ^ Environment.TickCount);
            systems.Add(hq, state);
            order.Add(hq);
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
