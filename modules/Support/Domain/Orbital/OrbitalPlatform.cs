using System;
using BoscaliSummer.Core;

namespace BoscaliSummer.Features.Support.Domain.Orbital
{
    /// <summary>Why the station is not on its pass cycle. Wire-stable.</summary>
    internal enum PlatformHold : byte
    {
        None = 0,
        Insertion = 1,
        Transfer = 2,
        Rephase = 3,
        SafeMode = 4
    }

    internal enum PlacementFailure : byte
    {
        None = 0,
        NoPlatform = 1,
        PlatformExists = 2,
        LaunchInFlight = 3,
        UnknownModule = 4,
        OutsideGrid = 5,
        CellOccupied = 6,
        NotAttached = 7,
        OverMass = 8,
        CopyLimit = 9,
        UnknownOrbit = 10,
        WouldStrand = 11,
        EmptyCell = 12
    }

    /// <summary>Why an ability cannot run now, in check order.</summary>
    internal enum PlatformDenial : byte
    {
        None = 0,
        NoPlatform = 1,
        NotFitted = 2,
        Offline = 3,
        Brownout = 4,
        Holding = 5,
        NotOverhead = 6,
        Overhead = 7,
        LowEnergy = 8,
        Recharging = 9,
        Expended = 10,
        NoFuel = 11,
        SameOrbit = 12
    }

    /// <summary>The last station event worth a voice-loop line. Wire-stable.</summary>
    internal enum PlatformNotice : byte
    {
        None = 0,
        DebrisHit = 1,
        DebrisDeflected = 2,
        SafeMode = 3,
        Docked = 4
    }

    /// <summary>Totals derived from the docked modules at one moment.</summary>
    internal readonly struct PlatformStats
    {
        public readonly int Modules;
        public readonly int Online;
        public readonly float Mass;
        public readonly float SolarKw;
        public readonly float SteadyKw;
        public readonly float LoadKw;
        public readonly float StorageKj;
        public readonly float FuelCapacity;
        public readonly int RodCapacity;
        public readonly bool Crewed;
        public readonly bool Stabilised;
        public readonly bool Overheating;

        public PlatformStats(int modules, int online, float mass, float solarKw, float steadyKw, float loadKw,
                             float storageKj, float fuelCapacity, int rodCapacity, bool crewed, bool stabilised,
                             bool overheating)
        {
            Modules = modules;
            Online = online;
            Mass = mass;
            SolarKw = solarKw;
            SteadyKw = steadyKw;
            LoadKw = loadKw;
            StorageKj = storageKj;
            FuelCapacity = fuelCapacity;
            RodCapacity = rodCapacity;
            Crewed = crewed;
            Stabilised = stabilised;
            Overheating = overheating;
        }

        public float NetSunKw => SolarKw + SteadyKw - LoadKw;
        public float NetEclipseKw => SteadyKw - LoadKw;
    }

    /// <summary>
    /// Everything a client needs to rebuild a station, with clocks relative to the moment the
    /// host took it. The host fills one with <see cref="OrbitalPlatform.Export"/>; a client
    /// hands one to <see cref="OrbitalPlatform.Mirror"/>.
    /// </summary>
    internal sealed class PlatformSnapshot
    {
        public readonly byte[] Modules = new byte[OrbitalPlatform.CellCount];
        public readonly byte[] Offline = new byte[OrbitalPlatform.CellCount];
        public readonly float[] Recharge = new float[PlatformAbilities.Count];
        public bool Active;
        public byte Regime;
        public int Seed;
        public float CycleClock;
        public byte Hold;
        public float Energy;
        public float Fuel;
        public byte Rods;
        public bool Brownout;
        public byte Pending;
        public byte PendingCell;
        public float DockIn;
        public float Elapsed;
        public byte Notice;
        public byte NoticeCell;
        public byte NoticeSerial;

        public void Clear()
        {
            Array.Clear(Modules, 0, Modules.Length);
            Array.Clear(Offline, 0, Offline.Length);
            Array.Clear(Recharge, 0, Recharge.Length);
            Active = Brownout = false;
            Regime = Hold = Rods = Pending = PendingCell = Notice = NoticeCell = NoticeSerial = 0;
            Seed = 0;
            CycleClock = Energy = Fuel = DockIn = Elapsed = 0f;
        }
    }

    /// <summary>
    /// One faction's orbital station: a 5 × 3 truss of modules around a core, flown on one
    /// orbit band. Pure C#, so layout, power, holds, recharge and debris are testable without
    /// the game. The host launches, jettisons, burns and ticks; a client only mirrors. All
    /// clocks are the caller's scene clock in seconds.
    /// </summary>
    internal sealed class OrbitalPlatform
    {
        public const int Columns = 5;
        public const int Rows = 3;
        public const int CellCount = Columns * Rows;
        public const int CoreCell = 7;
        public const float MassLimit = 40f;
        public const float BrownoutRecovery = 0.25f;
        public const double RephaseLeadSeconds = 10.0;
        public const double TransferSeconds = 30.0;
        public const double MirrorClockTolerance = 0.5;
        public const float HotRechargePenalty = 2f;
        public const float UncooledReactorOutput = 0.5f;
        public const float CrewRechargeFactor = 0.75f;
        public const float RelayBoost = 1.35f;
        public const float GyroScatterFactor = 0.5f;
        public const float StepLimitSeconds = 5f;
        public const string Callsign = "BASTION";

        private readonly ModuleKind[] cells = new ModuleKind[CellCount];
        private readonly double[] offlineUntil = new double[CellCount];
        private readonly float[] paid = new float[CellCount];
        private readonly double[] readyAt = new double[PlatformAbilities.Count];
        private float pendingPaid;
        private byte noticeSerial;

        public byte Regime { get; private set; } = OrbitRegimes.Standard;
        public int Seed { get; private set; }
        public int PositionIndex => StationKeeping.Target(Seed);

        /// <summary>Scene time at which pass zero's slot begins; earlier times are a hold.</summary>
        public double CycleStart { get; private set; }

        public PlatformHold Hold { get; private set; }
        public double LaunchTime { get; private set; }
        public float Energy { get; private set; }
        public float Fuel { get; private set; }
        public int Rods { get; private set; }
        public bool Brownout { get; private set; }
        public ModuleKind Pending { get; private set; }
        public int PendingCell { get; private set; }
        public double DockAt { get; private set; }
        public PlatformNotice Notice { get; private set; }
        public int NoticeCell { get; private set; }
        public byte NoticeSerial => noticeSerial;

        /// <summary>Host scheduling only: next debris roll, scene time. Zero until scheduled.</summary>
        public double NextDebris { get; set; }

        public bool Exists => cells[CoreCell] == ModuleKind.Core;
        public OrbitRegime Orbit => OrbitRegimes.Get(Regime);

        public static bool InGrid(int cell) => cell >= 0 && cell < CellCount;
        public static int Column(int cell) => cell % Columns;
        public static int Row(int cell) => cell / Columns;

        /// <summary>Row letter and column number, e.g. the core is B3.</summary>
        public static string CellName(int cell) =>
            InGrid(cell) ? ((char)('A' + Row(cell))).ToString() + (Column(cell) + 1) : "--";

        public ModuleKind Cell(int cell) => InGrid(cell) ? cells[cell] : ModuleKind.None;

        public float TotalPaid
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < CellCount; i++) total += paid[i];
                return total;
            }
        }

        public double Elapsed(double now) => Exists ? Math.Max(0.0, now - LaunchTime) : 0.0;

        /// <summary>The hold in force at <paramref name="now"/>, or none once the cycle has begun.</summary>
        public PlatformHold HoldAt(double now) => Exists && now < CycleStart ? Hold : PlatformHold.None;

        public OrbitState State(double now) =>
            StationKeeping.State(Seed, Orbit, now - CycleStart);

        // ---- Grid --------------------------------------------------------------------------

        /// <summary>Orthogonal neighbours; unused slots are -1.</summary>
        public static void Neighbours(int cell, int[] into)
        {
            int column = Column(cell), row = Row(cell);
            into[0] = column > 0 ? cell - 1 : -1;
            into[1] = column < Columns - 1 ? cell + 1 : -1;
            into[2] = row > 0 ? cell - Columns : -1;
            into[3] = row < Rows - 1 ? cell + Columns : -1;
        }

        private readonly int[] scratch = new int[4];

        private bool HasNeighbour(int cell, ModuleKind kind, double now, bool onlineOnly)
        {
            Neighbours(cell, scratch);
            for (int i = 0; i < 4; i++)
            {
                int n = scratch[i];
                if (n < 0 || cells[n] == ModuleKind.None) continue;
                if (kind != ModuleKind.None && cells[n] != kind) continue;
                if (onlineOnly && now < offlineUntil[n]) continue;
                return true;
            }
            return false;
        }

        public int Count(ModuleKind kind)
        {
            int count = Pending == kind ? 1 : 0;
            for (int i = 0; i < CellCount; i++)
                if (cells[i] == kind) count++;
            return count;
        }

        public bool IsOnline(int cell, double now) =>
            InGrid(cell) && cells[cell] != ModuleKind.None && now >= offlineUntil[cell];

        public double OfflineRemaining(int cell, double now) =>
            InGrid(cell) && cells[cell] != ModuleKind.None ? Math.Max(0.0, offlineUntil[cell] - now) : 0.0;

        public bool IsCooled(int cell, double now) => InGrid(cell) && HasNeighbour(cell, ModuleKind.Radiator, now, true);
        public bool IsBoosted(int cell, double now) => InGrid(cell) && HasNeighbour(cell, ModuleKind.Relay, now, true);

        public bool IsShielded(int cell) =>
            InGrid(cell) && (cells[cell] == ModuleKind.Shield || HasNeighbour(cell, ModuleKind.Shield, 0.0, false));

        /// <summary>An empty cell a module could dock to: next to a docked module and not taken by a launch.</summary>
        public bool CanAttach(int cell) =>
            Exists && InGrid(cell) && cells[cell] == ModuleKind.None &&
            !(Pending != ModuleKind.None && Pending != ModuleKind.Cargo && PendingCell == cell) &&
            HasNeighbour(cell, ModuleKind.None, 0.0, false);

        public bool Fitted(ModuleKind kind)
        {
            for (int i = 0; i < CellCount; i++)
                if (cells[i] == kind) return true;
            return false;
        }

        public bool FittedOnline(ModuleKind kind, double now)
        {
            for (int i = 0; i < CellCount; i++)
                if (cells[i] == kind && now >= offlineUntil[i]) return true;
            return false;
        }

        /// <summary>One ready magazine releases one rod, up to the remaining ammunition.</summary>
        public int RodSalvoCount(double now)
        {
            int magazines = 0;
            for (int i = 0; i < CellCount; i++)
                if (cells[i] == ModuleKind.Rods && now >= offlineUntil[i]) magazines++;
            return Math.Min(magazines, Rods);
        }

        /// <summary>A hot module kind with no online, cooled copy runs degraded.</summary>
        public bool RunsHot(ModuleKind kind, double now)
        {
            if (!PlatformModules.Info(kind).Hot) return false;
            for (int i = 0; i < CellCount; i++)
                if (cells[i] == kind && now >= offlineUntil[i] && IsCooled(i, now)) return false;
            return true;
        }

        private bool SensorBoosted(ModuleKind kind, double now)
        {
            for (int i = 0; i < CellCount; i++)
                if (cells[i] == kind && now >= offlineUntil[i] && IsBoosted(i, now)) return true;
            return false;
        }

        /// <summary>Bit per occupied cell, for foreign silhouettes.</summary>
        public ushort LayoutMask()
        {
            int mask = 0;
            for (int i = 0; i < CellCount; i++)
                if (cells[i] != ModuleKind.None) mask |= 1 << i;
            return (ushort)mask;
        }

        public PlatformStats Stats(double now)
        {
            int modules = 0, online = 0, rods = 0;
            float mass = 0f, solar = 0f, steady = 0f, load = 0f, storage = 0f, fuel = 0f;
            bool crewed = false, stabilised = false, overheating = false;
            for (int i = 0; i < CellCount; i++)
            {
                ModuleKind kind = cells[i];
                if (kind == ModuleKind.None) continue;
                ModuleInfo info = PlatformModules.Info(kind);
                modules++;
                mass += info.Mass;
                storage += info.StorageKj;
                fuel += info.Fuel;
                rods += info.Rods;
                if (now < offlineUntil[i]) continue;
                online++;
                bool uncooled = info.Hot && !IsCooled(i, now);
                overheating |= uncooled;
                solar += info.SolarKw;
                steady += info.SteadyKw * (uncooled ? UncooledReactorOutput : 1f);
                load += info.LoadKw;
                crewed |= kind == ModuleKind.Habitat;
                stabilised |= kind == ModuleKind.Gyro;
            }
            return new PlatformStats(modules, online, mass, solar, steady, load, storage, fuel, rods, crewed,
                stabilised, overheating);
        }

        // ---- Launch, dock, jettison ----------------------------------------------------------

        public PlacementFailure CheckPlacement(ModuleKind kind, int cell, byte regime, double now)
        {
            if (kind == ModuleKind.Core)
            {
                if (Exists) return PlacementFailure.PlatformExists;
                if (!OrbitRegimes.Valid(regime)) return PlacementFailure.UnknownOrbit;
                return PlacementFailure.None;
            }
            if (!PlatformModules.Placeable((int)kind)) return PlacementFailure.UnknownModule;
            if (!Exists) return PlacementFailure.NoPlatform;
            if (Pending != ModuleKind.None) return PlacementFailure.LaunchInFlight;
            if (!InGrid(cell)) return PlacementFailure.OutsideGrid;
            if (cells[cell] != ModuleKind.None) return PlacementFailure.CellOccupied;
            if (!CanAttach(cell)) return PlacementFailure.NotAttached;
            ModuleInfo info = PlatformModules.Info(kind);
            if (Count(kind) >= info.MaxCopies) return PlacementFailure.CopyLimit;
            if (Stats(now).Mass + info.Mass > MassLimit + 0.001f) return PlacementFailure.OverMass;
            return PlacementFailure.None;
        }

        /// <summary>
        /// Launch the core (onto <paramref name="regime"/>, charged and in insertion hold) or a
        /// module that docks at <paramref name="cell"/> after <paramref name="dockSeconds"/>.
        /// </summary>
        public PlacementFailure TryLaunch(ModuleKind kind, int cell, byte regime, int seed, double now, float price,
                                          double insertionSeconds, double dockSeconds)
        {
            PlacementFailure failure = CheckPlacement(kind, cell, regime, now);
            if (failure != PlacementFailure.None) return failure;

            if (kind == ModuleKind.Core)
            {
                Clear();
                cells[CoreCell] = ModuleKind.Core;
                paid[CoreCell] = Math.Max(0f, price);
                Regime = regime;
                Seed = StationKeeping.Route(StationKeeping.Centre, StationKeeping.Centre);
                LaunchTime = now;
                CycleStart = now + Math.Max(0.0, insertionSeconds);
                Hold = PlatformHold.Insertion;
                Energy = PlatformModules.Info(ModuleKind.Core).StorageKj;
                return PlacementFailure.None;
            }

            Pending = kind;
            PendingCell = cell;
            DockAt = now + Math.Max(0.0, dockSeconds);
            pendingPaid = Math.Max(0f, price);
            return PlacementFailure.None;
        }

        /// <summary>A cargo vehicle that refills fuel and rods when it docks at the core.</summary>
        public PlacementFailure TryResupply(double now, double dockSeconds)
        {
            if (!Exists) return PlacementFailure.NoPlatform;
            if (Pending != ModuleKind.None) return PlacementFailure.LaunchInFlight;
            Pending = ModuleKind.Cargo;
            PendingCell = CoreCell;
            DockAt = now + Math.Max(0.0, dockSeconds);
            pendingPaid = 0f;
            return PlacementFailure.None;
        }

        /// <summary>
        /// Remove one module, or deorbit the whole station when the core is jettisoned.
        /// <paramref name="refundBase"/> is what was paid for what left.
        /// </summary>
        public PlacementFailure TryJettison(int cell, out ModuleKind removed, out float refundBase)
        {
            removed = ModuleKind.None;
            refundBase = 0f;
            if (!Exists) return PlacementFailure.NoPlatform;
            if (!InGrid(cell) || cells[cell] == ModuleKind.None) return PlacementFailure.EmptyCell;
            if (cell == CoreCell)
            {
                removed = ModuleKind.Core;
                refundBase = TotalPaid;
                Clear();
                return PlacementFailure.None;
            }
            if (WouldStrand(cell)) return PlacementFailure.WouldStrand;

            removed = cells[cell];
            refundBase = paid[cell];
            cells[cell] = ModuleKind.None;
            paid[cell] = 0f;
            offlineUntil[cell] = 0.0;
            ClampResources();
            return PlacementFailure.None;
        }

        /// <summary>True when removing <paramref name="cell"/> would leave a module (or the module in
        /// flight) with no path back to the core.</summary>
        public bool WouldStrand(int cell)
        {
            if (!InGrid(cell) || cell == CoreCell || cells[cell] == ModuleKind.None) return false;
            bool[] reached = new bool[CellCount];
            int[] queue = new int[CellCount];
            int head = 0, tail = 0;
            queue[tail++] = CoreCell;
            reached[CoreCell] = true;
            var around = new int[4];
            while (head < tail)
            {
                int current = queue[head++];
                Neighbours(current, around);
                for (int i = 0; i < 4; i++)
                {
                    int n = around[i];
                    if (n < 0 || n == cell || reached[n] || cells[n] == ModuleKind.None) continue;
                    reached[n] = true;
                    queue[tail++] = n;
                }
            }
            for (int i = 0; i < CellCount; i++)
                if (i != cell && cells[i] != ModuleKind.None && !reached[i]) return true;

            if (Pending != ModuleKind.None && Pending != ModuleKind.Cargo)
            {
                Neighbours(PendingCell, around);
                for (int i = 0; i < 4; i++)
                    if (around[i] >= 0 && around[i] != cell && reached[around[i]]) return false;
                return true;
            }
            return false;
        }

        private void Dock(double now)
        {
            ModuleKind kind = Pending;
            int cell = PendingCell;
            Pending = ModuleKind.None;
            if (kind == ModuleKind.Cargo)
            {
                PlatformStats full = Stats(now);
                Fuel = full.FuelCapacity;
                Rods = full.RodCapacity;
            }
            else if (InGrid(cell) && cells[cell] == ModuleKind.None)
            {
                cells[cell] = kind;
                paid[cell] = pendingPaid;
                offlineUntil[cell] = 0.0;
                ModuleInfo info = PlatformModules.Info(kind);
                Fuel += info.Fuel;
                Rods += info.Rods;
                ClampResources();
            }
            pendingPaid = 0f;
            Note(PlatformNotice.Docked, cell);
        }

        private void ClampResources()
        {
            PlatformStats stats = Stats(double.MaxValue);
            Fuel = Math.Min(Fuel, stats.FuelCapacity);
            Rods = Math.Min(Rods, stats.RodCapacity);
            Energy = Math.Min(Energy, stats.StorageKj);
        }

        // ---- Host tick ----------------------------------------------------------------------

        /// <summary>
        /// Dock arrivals, end holds, run the power budget and burn drag fuel. Solar works out of
        /// the theatre arc or over a theatre in daylight; loads are shed during a brownout.
        /// </summary>
        public void Tick(double now, float deltaTime, bool theaterDaylight)
        {
            if (!Exists || float.IsNaN(deltaTime) || deltaTime <= 0f) return;
            float dt = Math.Min(deltaTime, StepLimitSeconds);
            if (Pending != ModuleKind.None && now >= DockAt) Dock(now);
            if (Hold != PlatformHold.None && now >= CycleStart) Hold = PlatformHold.None;

            PlatformStats stats = Stats(now);
            bool sunlit = State(now).Phase != OrbitPhase.InPass || theaterDaylight;
            float generation = (sunlit ? stats.SolarKw : 0f) + stats.SteadyKw;
            float load = Brownout ? 0f : stats.LoadKw;
            Energy = Math.Max(0f, Math.Min(stats.StorageKj, Energy + (generation - load) * dt));
            if (!Brownout && Energy <= 0f && stats.LoadKw > generation) Brownout = true;
            else if (Brownout && Energy >= stats.StorageKj * BrownoutRecovery) Brownout = false;

            float drag = Orbit.DragFuelPerSecond;
            if (drag > 0f && Hold != PlatformHold.Transfer && Hold != PlatformHold.SafeMode)
            {
                Fuel = Math.Max(0f, Fuel - drag * dt);
                if (Fuel <= 0f) EnterSafeMode(now);
            }
        }

        private void EnterSafeMode(double now)
        {
            Regime = OrbitRegimes.Standard;
            Seed = StationKeeping.Route(PositionIndex, PositionIndex);
            CycleStart = now + TransferSeconds;
            Hold = PlatformHold.SafeMode;
            Note(PlatformNotice.SafeMode, CoreCell);
        }

        /// <summary>Host debris roll: <paramref name="pick"/> chooses a docked module other than the core.
        /// A shielded module deflects it; otherwise it drops offline.</summary>
        public bool StrikeDebris(int pick, double now, double offlineSeconds)
        {
            int candidates = 0;
            for (int i = 0; i < CellCount; i++)
                if (i != CoreCell && cells[i] != ModuleKind.None) candidates++;
            if (candidates == 0) return false;

            int nth = Math.Abs(pick % candidates);
            for (int i = 0; i < CellCount; i++)
            {
                if (i == CoreCell || cells[i] == ModuleKind.None) continue;
                if (nth-- > 0) continue;
                if (IsShielded(i))
                {
                    Note(PlatformNotice.DebrisDeflected, i);
                }
                else
                {
                    offlineUntil[i] = Math.Max(offlineUntil[i], now + Math.Max(0.0, offlineSeconds));
                    Note(PlatformNotice.DebrisHit, i);
                }
                return true;
            }
            return false;
        }

        private void Note(PlatformNotice notice, int cell)
        {
            Notice = notice;
            NoticeCell = cell;
            noticeSerial = unchecked((byte)(noticeSerial + 1));
        }

        // ---- Abilities ----------------------------------------------------------------------

        public PlatformDenial Check(PlatformAbility ability, double now)
        {
            if (!Exists) return PlatformDenial.NoPlatform;
            AbilityInfo info = PlatformAbilities.Info(ability);
            if (!Fitted(info.Module)) return PlatformDenial.NotFitted;
            if (!FittedOnline(info.Module, now)) return PlatformDenial.Offline;
            if (Brownout) return PlatformDenial.Brownout;
            if (now < CycleStart) return PlatformDenial.Holding;
            if (info.Window != AbilityWindow.Any)
            {
                bool overhead = State(now).InPass;
                if (info.Window == AbilityWindow.Overhead && !overhead) return PlatformDenial.NotOverhead;
                if (info.Window == AbilityWindow.Away && overhead) return PlatformDenial.Overhead;
            }
            if (Energy + 0.001f < info.EnergyKj) return PlatformDenial.LowEnergy;
            if (now < readyAt[(int)ability]) return PlatformDenial.Recharging;
            if (ability == PlatformAbility.RodStrike && Rods <= 0) return PlatformDenial.Expended;
            if (ability != PlatformAbility.OrbitShift && Fuel + 0.001f < info.Fuel) return PlatformDenial.NoFuel;
            return PlatformDenial.None;
        }

        public float ShiftFuel(byte target) =>
            Math.Abs(target - Regime) * PlatformAbilities.ShiftFuelPerBand;

        public PlatformDenial CheckShift(byte target, double now)
        {
            if (!OrbitRegimes.Valid(target) || target == Regime) return PlatformDenial.SameOrbit;
            PlatformDenial denial = Check(PlatformAbility.OrbitShift, now);
            if (denial != PlatformDenial.None) return denial;
            return Fuel + 0.001f < ShiftFuel(target) ? PlatformDenial.NoFuel : PlatformDenial.None;
        }

        public float RechargeSeconds(PlatformAbility ability, double now)
        {
            AbilityInfo info = PlatformAbilities.Info(ability);
            float seconds = info.RechargeSeconds;
            if (Stats(now).Crewed) seconds *= CrewRechargeFactor;
            if (RunsHot(info.Module, now)) seconds *= HotRechargePenalty;
            return seconds;
        }

        public double RechargeRemaining(PlatformAbility ability, double now) =>
            PlatformAbilities.Valid((int)ability) ? Math.Max(0.0, readyAt[(int)ability] - now) : 0.0;

        /// <summary>Spend an accepted ability's energy, fuel and ammunition, and start its recharge.</summary>
        public void Consume(PlatformAbility ability, double now, int rodShots = 1)
        {
            AbilityInfo info = PlatformAbilities.Info(ability);
            Energy = Math.Max(0f, Energy - info.EnergyKj);
            if (ability != PlatformAbility.OrbitShift) Fuel = Math.Max(0f, Fuel - info.Fuel);
            if (ability == PlatformAbility.RodStrike) Rods = Math.Max(0, Rods - Math.Max(1, rodShots));
            if (info.RechargeSeconds > 0f) readyAt[(int)ability] = now + RechargeSeconds(ability, now);
        }

        public PlatformDenial CheckRelocate(int sector, double now)
        {
            if (!StationKeeping.Valid(sector) || sector == PositionIndex) return PlatformDenial.SameOrbit;
            return Check(PlatformAbility.Rephase, now);
        }

        public bool TryRelocate(int sector, double now)
        {
            if (CheckRelocate(sector, now) != PlatformDenial.None) return false;
            Consume(PlatformAbility.Rephase, now);
            Seed = StationKeeping.Route(PositionIndex, sector);
            CycleStart = now + RephaseLeadSeconds;
            Hold = PlatformHold.Rephase;
            return true;
        }

        public bool TryShift(byte target, double now, int seed)
        {
            if (CheckShift(target, now) != PlatformDenial.None) return false;
            Fuel = Math.Max(0f, Fuel - ShiftFuel(target));
            Regime = target;
            Seed = seed;
            CycleStart = now + TransferSeconds;
            Hold = PlatformHold.Transfer;
            return true;
        }

        public float ScanScale(double now) =>
            Orbit.ScanScale * (SensorBoosted(ModuleKind.Imager, now) ? RelayBoost : 1f);

        public float ElintScale(double now) =>
            Orbit.ScanScale * (SensorBoosted(ModuleKind.Sigint, now) ? RelayBoost : 1f);

        public float RodScatter(double now) =>
            Orbit.RodScatter * (Stats(now).Stabilised ? GyroScatterFactor : 1f);

        /// <summary>The first battery enables EMP; each additional online bank widens the pulse by 25%.</summary>
        public float EmpScaleAt(double now)
        {
            int banks = 0;
            for (int i = 0; i < CellCount; i++)
                if (cells[i] == ModuleKind.Battery && now >= offlineUntil[i]) banks++;
            return Orbit.EmpScale * (1f + 0.25f * Math.Max(0, banks - 1));
        }

        // ---- Snapshot -----------------------------------------------------------------------

        public void Export(double now, PlatformSnapshot into)
        {
            into.Clear();
            into.Active = Exists;
            if (!Exists) return;
            for (int i = 0; i < CellCount; i++)
            {
                into.Modules[i] = (byte)cells[i];
                into.Offline[i] = (byte)Math.Min(255.0, Math.Ceiling(OfflineRemaining(i, now)));
            }
            for (int i = 0; i < PlatformAbilities.Count; i++)
                into.Recharge[i] = (float)Math.Min(3600.0, Math.Max(0.0, readyAt[i] - now));
            into.Regime = Regime;
            into.Seed = Seed;
            into.CycleClock = (float)(now - CycleStart);
            into.Hold = (byte)HoldAt(now);
            into.Energy = Energy;
            into.Fuel = Fuel;
            into.Rods = (byte)Math.Min(255, Rods);
            into.Brownout = Brownout;
            into.Pending = (byte)Pending;
            into.PendingCell = (byte)(InGrid(PendingCell) ? PendingCell : 0);
            into.DockIn = Pending != ModuleKind.None ? (float)Math.Max(0.0, DockAt - now) : 0f;
            into.Elapsed = (float)Elapsed(now);
            into.Notice = (byte)Notice;
            into.NoticeCell = (byte)(InGrid(NoticeCell) ? NoticeCell : 0);
            into.NoticeSerial = noticeSerial;
        }

        /// <summary>
        /// Client mirror. Relative clocks are rebased onto the local scene clock; a clock that
        /// moved less than <see cref="MirrorClockTolerance"/> keeps its epoch so snapshot latency
        /// does not jitter the sky. An inconsistent snapshot is ignored whole.
        /// </summary>
        public void Mirror(PlatformSnapshot from, double now)
        {
            if (from == null) return;
            if (!from.Active)
            {
                Clear();
                return;
            }
            if (!Valid(from)) return;

            bool sameOrbit = Exists && Seed == from.Seed && Regime == from.Regime;
            for (int i = 0; i < CellCount; i++)
            {
                cells[i] = (ModuleKind)from.Modules[i];
                offlineUntil[i] = from.Offline[i] > 0 ? now + from.Offline[i] : 0.0;
            }
            Regime = from.Regime;
            Seed = from.Seed;
            CycleStart = Rebase(CycleStart, now - from.CycleClock, sameOrbit);
            LaunchTime = Rebase(LaunchTime, now - Math.Max(0f, from.Elapsed), sameOrbit);
            Hold = (PlatformHold)from.Hold;
            Energy = Math.Max(0f, from.Energy);
            Fuel = Math.Max(0f, from.Fuel);
            Rods = from.Rods;
            Brownout = from.Brownout;

            var pending = (ModuleKind)from.Pending;
            bool samePending = Pending == pending && PendingCell == from.PendingCell;
            Pending = pending;
            PendingCell = from.PendingCell;
            DockAt = pending == ModuleKind.None ? 0.0 : Rebase(DockAt, now + Math.Max(0f, from.DockIn), samePending);

            for (int i = 0; i < PlatformAbilities.Count; i++)
                readyAt[i] = Rebase(readyAt[i], now + Math.Max(0f, from.Recharge[i]), true);

            Notice = (PlatformNotice)from.Notice;
            NoticeCell = from.NoticeCell;
            noticeSerial = from.NoticeSerial;
        }

        private static double Rebase(double known, double reported, bool keep) =>
            keep && Math.Abs(known - reported) <= MirrorClockTolerance ? known : reported;

        private static bool Valid(PlatformSnapshot from)
        {
            if (!StationKeeping.ValidRoute(from.Seed)) return false;
            if (from.Modules[CoreCell] != (byte)ModuleKind.Core) return false;
            for (int i = 0; i < CellCount; i++)
            {
                byte kind = from.Modules[i];
                if (kind != 0 && !PlatformModules.Placeable(kind)) return false;
                if (kind == (byte)ModuleKind.Core && i != CoreCell) return false;
            }
            if (!OrbitRegimes.Valid(from.Regime) || from.Hold > (byte)PlatformHold.SafeMode) return false;
            if (from.Notice > (byte)PlatformNotice.Docked || !InGrid(from.NoticeCell)) return false;
            if (from.Pending != 0 && from.Pending != (byte)ModuleKind.Cargo && !PlatformModules.Placeable(from.Pending))
                return false;
            if (!InGrid(from.PendingCell)) return false;
            if (!Finite(from.CycleClock) || !Finite(from.Energy) || !Finite(from.Fuel) ||
                !Finite(from.DockIn) || !Finite(from.Elapsed)) return false;
            for (int i = 0; i < from.Recharge.Length; i++)
                if (!Finite(from.Recharge[i])) return false;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void Clear()
        {
            Array.Clear(cells, 0, cells.Length);
            Array.Clear(offlineUntil, 0, offlineUntil.Length);
            Array.Clear(paid, 0, paid.Length);
            Array.Clear(readyAt, 0, readyAt.Length);
            Regime = OrbitRegimes.Standard;
            Seed = 0;
            CycleStart = 0.0;
            Hold = PlatformHold.None;
            LaunchTime = 0.0;
            Energy = Fuel = 0f;
            Rods = 0;
            Brownout = false;
            Pending = ModuleKind.None;
            PendingCell = 0;
            DockAt = 0.0;
            pendingPaid = 0f;
            Notice = PlatformNotice.None;
            NoticeCell = 0;
            NextDebris = 0.0;
        }

        public void Reset() => Clear();
    }

    /// <summary>Another faction's station: orbit and silhouette only, modules undisclosed.</summary>
    internal sealed class ForeignPlatform
    {
        public byte Regime { get; private set; }
        public int Seed { get; private set; }
        public double CycleStart { get; private set; }
        public ushort Layout { get; private set; }

        public int ModuleCount
        {
            get
            {
                int count = 0;
                for (int bits = Layout; bits != 0; bits &= bits - 1) count++;
                return count;
            }
        }

        public bool Occupied(int cell) => OrbitalPlatform.InGrid(cell) && ((Layout >> cell) & 1) != 0;

        public void Mirror(byte regime, int seed, double cycleStart, ushort layout)
        {
            if (!OrbitRegimes.Valid(regime) || !StationKeeping.ValidRoute(seed) ||
                double.IsNaN(cycleStart) || double.IsInfinity(cycleStart)) return;
            bool known = Regime == regime && Seed == seed;
            Regime = regime;
            Seed = seed;
            Layout = layout;
            if (!known || Math.Abs(cycleStart - CycleStart) > OrbitalPlatform.MirrorClockTolerance) CycleStart = cycleStart;
        }

        public OrbitState State(double now) =>
            StationKeeping.State(Seed, OrbitRegimes.Get(Regime), now - CycleStart);
    }
}
