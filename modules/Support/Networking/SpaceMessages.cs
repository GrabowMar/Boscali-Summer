using System;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Domain.Orbital;
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

    /// <summary>One client intent: launch, jettison, burn, resupply, upgrade, invest, doctrine or a CYBER
    /// site order or console verb. The host validates everything.</summary>
    [NetworkMessage]
    internal struct OpsCommandMessage
    {
        public byte Protocol;
        public int RequestId;
        public byte Command;
        public byte Arg;
        public byte Arg2;
        public float X, Z;
    }

    /// <summary>
    /// Bounded faction snapshot: the faction's station (15 cells, 7 recharge timers), up to four
    /// foreign stations, four facility levels, the CYBER network (12 sites, 6 incidents, 6
    /// notices, up to 8 origin names), six program tiers and the base-of-operations ranks.
    /// Clocks are relative to the moment the host took the snapshot, so a client rebuilds the
    /// same passes and timers locally.
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
        public int PlatformSeed;

        /// <summary>Seconds since pass zero's slot began; negative during a hold.</summary>
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

        public byte Sigint, Crypto, Disrupt, Ew;

        /// <summary>The faction's spectrum-defence network; never the live vehicles behind it.</summary>
        public CyberSnapshot Cyber;

        /// <summary>Enemy faction names in the network's origin order.</summary>
        public byte CyberOriginCount;
        public string[] CyberOrigins;

        /// <summary>Funded tier per <c>OpsProgramId</c>, always six entries.</summary>
        public byte[] ProgramTiers;

        /// <summary>SPEC OPS / INTEL reserve tokens and progress toward the next (0..255).</summary>
        public byte SpecOpsTokens, IntelTokens, SpecOpsProgress, IntelProgress;

        /// <summary>Base-of-operations rank per <c>GarrisonUpgradeId</c>.</summary>
        public byte[] GarrisonLevels;
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
            ProgramTiers = new byte[OpsProgramLedger.ProgramCount],
            GarrisonLevels = new byte[OpsGarrison.UpgradeCount],
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
