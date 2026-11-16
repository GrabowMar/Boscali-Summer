using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Gamemodes.Domain
{
    internal enum GamemodePhase { Running, Intermission, Finished }

    /// <summary>
    /// Host-owned match rules. Team indices remain fixed across Heavyweight's role swap.
    /// The adapter supplies real kill attribution and actual source loading/unloading events.
    /// Positions, client claims and inventory displays alone are not delivery evidence.
    /// </summary>
    internal sealed class GamemodeMatch
    {
        private const int CargoLedgerLimit = 65536;
        private readonly int[] scores = new int[2];
        private readonly HashSet<string> kills = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> loads = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> receipts = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Cargo> cargo = new Dictionary<string, Cargo>(StringComparer.Ordinal);
        private double lastTime;

        private sealed class Cargo
        {
            public int Team;
            public int Remaining;
            public string Destination;
        }

        public GameModeKind Mode { get; }
        public GamemodePhase Phase { get; private set; }
        public int Leg { get; private set; }
        public double Deadline { get; private set; }
        public int TransportingTeam => Mode == GameModeKind.Heavyweight && Phase == GamemodePhase.Running ? Leg - 1 : -1;
        public double RemainingSeconds => Phase == GamemodePhase.Finished ? 0 : Math.Max(0, Deadline - lastTime);
        public int Winner => Phase != GamemodePhase.Finished || scores[0] == scores[1] ? -1 : scores[0] > scores[1] ? 0 : 1;
        public int ActiveManifests => cargo.Count;
        public int Score(int team) => IsTeam(team) ? scores[team] : 0;

        public GamemodeMatch(GameModeKind mode, double startTime)
        {
            if (mode < GameModeKind.None || mode > GameModeKind.Heavyweight) throw new ArgumentOutOfRangeException(nameof(mode));
            if (!Finite(startTime) || startTime < 0) throw new ArgumentOutOfRangeException(nameof(startTime));
            Mode = mode;
            lastTime = startTime;
            Leg = mode == GameModeKind.Heavyweight ? 1 : 0;
            Phase = mode == GameModeKind.None ? GamemodePhase.Finished : GamemodePhase.Running;
            Deadline = mode == GameModeKind.Fuzzball ? startTime + GamemodeRules.FuzzballSeconds :
                mode == GameModeKind.Heavyweight ? startTime + GamemodeRules.HeavyweightLegSeconds : double.PositiveInfinity;
        }

        /// <summary>Reject invalid/backwards clocks; advance at the original deadline even when the host tick is late.</summary>
        public bool Tick(double now)
        {
            if (!Finite(now) || now < lastTime) return false;
            lastTime = now;
            while (Phase != GamemodePhase.Finished && now >= Deadline)
            {
                if (Mode == GameModeKind.Fuzzball || Mode == GameModeKind.Heavyweight && Leg == 2)
                    Phase = GamemodePhase.Finished;
                else if (Mode == GameModeKind.Heavyweight && Phase == GamemodePhase.Running)
                {
                    ClearCargo();
                    Phase = GamemodePhase.Intermission;
                    Deadline += GamemodeRules.HeavyweightIntermissionSeconds;
                }
                else if (Mode == GameModeKind.Heavyweight)
                {
                    Leg = 2;
                    Phase = GamemodePhase.Running;
                    Deadline += GamemodeRules.HeavyweightLegSeconds;
                }
                else break;
            }
            if (Phase == GamemodePhase.Finished) ClearCargo();
            return true;
        }

        public bool TryCreditKill(double now, string victimId, int attackerTeam, int victimTeam, bool isAircraft)
        {
            if (!Tick(now) || Mode != GameModeKind.Fuzzball || Phase != GamemodePhase.Running ||
                !isAircraft || !Identity(victimId) || !IsTeam(attackerTeam) || !IsTeam(victimTeam) ||
                attackerTeam == victimTeam || !kills.Add(victimId)) return false;
            if (++scores[attackerTeam] >= GamemodeRules.FuzzballKillTarget)
            {
                Phase = GamemodePhase.Finished;
                Deadline = now;
            }
            return true;
        }

        /// <summary>Register an actual source-issued load. Each genuine reload needs a fresh id including a load nonce.</summary>
        public bool TryLoadCargo(double now, string manifestId, int team, string originId, string destinationId, int quantity)
        {
            if (!Tick(now) || !CanTransport(team) || !Identity(manifestId) || !Identity(originId) || !Identity(destinationId) ||
                string.Equals(originId, destinationId, StringComparison.Ordinal) || quantity <= 0 ||
                loads.Count >= CargoLedgerLimit || !loads.Add(manifestId)) return false;
            cargo.Add(manifestId, new Cargo { Team = team, Destination = destinationId, Remaining = quantity });
            return true;
        }

        /// <summary>Credit a real partial or full unload once. Rejected callbacks do not consume a valid receipt id.</summary>
        public bool TryDeliverCargo(double now, string manifestId, string receiptId, int team, string destinationId, int quantity)
        {
            if (!Tick(now) || !CanTransport(team) || !Identity(manifestId) || !Identity(receiptId) || quantity <= 0 ||
                !cargo.TryGetValue(manifestId, out Cargo loaded) || loaded.Team != team ||
                !string.Equals(destinationId, loaded.Destination, StringComparison.Ordinal) || quantity > loaded.Remaining ||
                quantity > int.MaxValue - scores[team] || receipts.Count >= CargoLedgerLimit || !receipts.Add(receiptId)) return false;
            loaded.Remaining -= quantity;
            scores[team] += quantity;
            if (loaded.Remaining == 0) cargo.Remove(manifestId);
            return true;
        }

        /// <summary>Destroyed, abandoned, wrongly unloaded or reassigned cargo loses eligibility without allowing load replay.</summary>
        public bool DiscardCargo(double now, string manifestId) => Tick(now) && Identity(manifestId) && cargo.Remove(manifestId);

        private bool CanTransport(int team) => Mode == GameModeKind.Heavyweight && Phase == GamemodePhase.Running && team == TransportingTeam;
        private static bool IsTeam(int team) => team == 0 || team == 1;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Identity(string value) => !string.IsNullOrWhiteSpace(value);
        private void ClearCargo() { cargo.Clear(); loads.Clear(); receipts.Clear(); }
    }
}