using System;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using BoscaliSummer.Features.Support.Runtime;
using Mirage;

namespace BoscaliSummer.Features.Support.Networking
{
    /// <summary>Client poll for its faction's station and infrastructure state.</summary>
    [NetworkMessage]
    internal struct OpsQueryMessage
    {
        public byte Protocol;
    }

    /// <summary>One client intent: launch, jettison, burn, resupply, a CYBER upgrade, breach, choice
    /// or console verb, or a SPEC OPS raise, launch or recall. The host validates everything.</summary>
    [NetworkMessage]
    internal struct OpsCommandMessage
    {
        public byte Protocol;
        public int RequestId;
        public byte Command;
        public byte Arg;
        public byte Arg2;
        public float X, Z;

        /// <summary>The objective's anchor id for a SPEC OPS launch; zero otherwise.</summary>
        public uint Revision;
    }

    /// <summary>
    /// Bounded faction snapshot: the faction's station (15 cells, 7 recharge timers), up to four
    /// foreign stations, four facility levels, the CYBER network (12 sites, 6 incidents, 6
    /// notices, up to 8 origin names). The SPEC OPS detachment travels beside it in
    /// <see cref="SpecOpsStateMessage"/>: together they would not fit one datagram.
    /// Clocks are relative to the moment the host took the snapshot, so a client rebuilds the
    /// same fixed positions, relocation and timers locally.
    /// </summary>
    [NetworkMessage]
    internal struct OpsStateMessage
    {
        public byte Protocol;
        public int RequestId;
        public byte Result;

        public bool PlatformActive;

        /// <summary><c>ModuleKind</c> per grid cell, always fifteen entries.</summary>
        public byte[] PlatformModules;

        /// <summary>Seconds each cell stays offline after a debris strike.</summary>
        public byte[] PlatformOffline;

        public byte PlatformRegime;
        /// <summary>Protocol 18 sector route: origin * 9 + destination, both 0..8.</summary>
        public int PlatformSeed;

        /// <summary>Seconds since station arrival; negative during insertion or relocation.</summary>
        public float PlatformClock;

        public byte PlatformHold;
        public float PlatformEnergy;
        public float PlatformFuel;
        public byte PlatformRods;
        public bool PlatformBrownout;
        public byte PlatformPending;
        public byte PlatformPendingCell;
        public float PlatformDockIn;

        /// <summary>Seconds of recharge left per <c>PlatformAbility</c>, always seven entries.</summary>
        public float[] PlatformRecharge;

        /// <summary>Seconds since the core launched.</summary>
        public float PlatformElapsed;

        public byte PlatformNotice;
        public byte PlatformNoticeCell;
        public byte PlatformNoticeSerial;

        /// <summary>Other factions' stations: orbit and occupied-cell mask only.</summary>
        public byte ForeignCount;
        public byte[] ForeignRegimes;
        public int[] ForeignSeeds;
        public float[] ForeignClocks;
        public int[] ForeignLayouts;

        /// <summary>The faction's cyber network; never the live world behind it.</summary>
        public CyberSnapshot Cyber;

        /// <summary>Enemy faction names in the network's origin order.</summary>
        public byte CyberOriginCount;
        public string[] CyberOrigins;
    }

    /// <summary>
    /// The faction's SPEC OPS detachment — four teams, up to twelve objectives, two recharges and
    /// the notice ring — sent immediately before every <see cref="OpsStateMessage"/> on the same
    /// reliable channel, so a command's reply always finds the detachment already mirrored.
    /// </summary>
    [NetworkMessage]
    internal struct SpecOpsStateMessage
    {
        public byte Protocol;
        public SpecOpsSnapshot State;
    }

    /// <summary>Fixed-size arrays for a snapshot, so the writer, the reader and the host all
    /// agree on bounds and a short array can never throw mid-serialisation. Also the one
    /// mapping between the wire fields and the station model's snapshot.</summary>
    internal static class OpsStateMessageBuffers
    {
        /// <summary>Enemy faction names carried per snapshot. Beyond this an incident origin reads
        /// as a numbered hostile actor rather than growing the poll.</summary>
        public const int MaximumOriginNames = 4;

        public const int MaximumOriginLength = 20;

        public static OpsStateMessage Create() => new OpsStateMessage
        {
            PlatformModules = new byte[OrbitalPlatform.CellCount],
            PlatformOffline = new byte[OrbitalPlatform.CellCount],
            PlatformRecharge = new float[PlatformAbilities.Count],
            ForeignRegimes = new byte[SpaceOperations.MaximumForeign],
            ForeignSeeds = new int[SpaceOperations.MaximumForeign],
            ForeignClocks = new float[SpaceOperations.MaximumForeign],
            ForeignLayouts = new int[SpaceOperations.MaximumForeign],
            Cyber = new CyberSnapshot(),
            CyberOrigins = new string[MaximumOriginNames]
        };

        public static bool ValidCyber(in OpsStateMessage state) =>
            state.Cyber != null && state.CyberOrigins != null && state.CyberOriginCount <= MaximumOriginNames &&
            state.CyberOrigins.Length >= state.CyberOriginCount;

        public static bool ValidArrays(in OpsStateMessage state) =>
            state.PlatformModules != null && state.PlatformModules.Length >= OrbitalPlatform.CellCount &&
            state.PlatformOffline != null && state.PlatformOffline.Length >= OrbitalPlatform.CellCount &&
            state.PlatformRecharge != null && state.PlatformRecharge.Length >= PlatformAbilities.Count &&
            state.ForeignCount <= SpaceOperations.MaximumForeign &&
            state.ForeignRegimes != null && state.ForeignRegimes.Length >= state.ForeignCount &&
            state.ForeignSeeds != null && state.ForeignSeeds.Length >= state.ForeignCount &&
            state.ForeignClocks != null && state.ForeignClocks.Length >= state.ForeignCount &&
            state.ForeignLayouts != null && state.ForeignLayouts.Length >= state.ForeignCount;

        public static void Write(PlatformSnapshot from, ref OpsStateMessage into)
        {
            into.PlatformActive = from.Active;
            Array.Copy(from.Modules, into.PlatformModules, OrbitalPlatform.CellCount);
            Array.Copy(from.Offline, into.PlatformOffline, OrbitalPlatform.CellCount);
            Array.Copy(from.Recharge, into.PlatformRecharge, PlatformAbilities.Count);
            into.PlatformRegime = from.Regime;
            into.PlatformSeed = from.Seed;
            into.PlatformClock = from.CycleClock;
            into.PlatformHold = from.Hold;
            into.PlatformEnergy = from.Energy;
            into.PlatformFuel = from.Fuel;
            into.PlatformRods = from.Rods;
            into.PlatformBrownout = from.Brownout;
            into.PlatformPending = from.Pending;
            into.PlatformPendingCell = from.PendingCell;
            into.PlatformDockIn = from.DockIn;
            into.PlatformElapsed = from.Elapsed;
            into.PlatformNotice = from.Notice;
            into.PlatformNoticeCell = from.NoticeCell;
            into.PlatformNoticeSerial = from.NoticeSerial;
        }

        public static void Read(in OpsStateMessage from, PlatformSnapshot into)
        {
            into.Clear();
            into.Active = from.PlatformActive;
            Array.Copy(from.PlatformModules, into.Modules, OrbitalPlatform.CellCount);
            Array.Copy(from.PlatformOffline, into.Offline, OrbitalPlatform.CellCount);
            Array.Copy(from.PlatformRecharge, into.Recharge, PlatformAbilities.Count);
            into.Regime = from.PlatformRegime;
            into.Seed = from.PlatformSeed;
            into.CycleClock = from.PlatformClock;
            into.Hold = from.PlatformHold;
            into.Energy = from.PlatformEnergy;
            into.Fuel = from.PlatformFuel;
            into.Rods = from.PlatformRods;
            into.Brownout = from.PlatformBrownout;
            into.Pending = from.PlatformPending;
            into.PendingCell = from.PlatformPendingCell;
            into.DockIn = from.PlatformDockIn;
            into.Elapsed = from.PlatformElapsed;
            into.Notice = from.PlatformNotice;
            into.NoticeCell = from.PlatformNoticeCell;
            into.NoticeSerial = from.PlatformNoticeSerial;
        }
    }

    /// <summary>Host broadcast when a track-deception operation starts; all peers mirror it.</summary>
    [NetworkMessage]
    internal struct CyberEffectMessage
    {
        public byte Protocol;
        public byte Kind;
        public string FactionName;
        public float X, Z, Duration;
    }
}
