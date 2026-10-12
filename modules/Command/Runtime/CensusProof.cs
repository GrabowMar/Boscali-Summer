using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
#if !PUBLIC_RELEASE
    /// <summary>
    /// Host-truth readback for the unattended census proof (<c>tests/ingame/str-2-war-tc.json</c>, <c>str-3-reduced-ff.json</c>): what the theater bag the news ticker reads holds,
    /// the census it was applied from, a fresh measurement taken now, and the control field's own counts, with the cross-checks computed game-side. Read only; it refuses unless a nomodkit
    /// scenario is running. Dev tooling, compiled out of public releases. Results are nested dictionaries without dots in key names; an unknown number is -1, never NaN.
    /// </summary>
    public static class CensusProof
    {
        private static bool InSimRun => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"));

        /// <summary>{faction (default: the local one)}: <c>census</c> (the applied census), <c>fresh</c> (measured now), <c>ticker</c>, <c>grid</c> and <c>check</c>.</summary>
        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("CensusProof", "Read", "requires a nomodkit scenario run");
            CommandManager command = CommandManager.Active;
            if (command == null) return Failure("CensusProof", "Read", "the Command module is not running");
            if (!TryFaction(Text(args, "faction"), out FactionHQ hq)) return Failure("CensusProof", "Read", "no such faction");

            TheaterCensus applied = command.LastCensus;
            bool measured = command.TryMeasure(hq, out TheaterCensus fresh);
            TacticalSectorGrid grid = UnityEngine.Object.FindObjectOfType<TerritoryControlView>()?.Read(hq);
            int gridBases = -1;
            if (grid != null)
            {
                gridBases = 0;
                foreach (TacticalSectorGrid.TacticalNode node in grid.GetNodes())
                    if (node.IsAirbase && node.IsContested && node.Faction == SectorControl.Friendly) gridBases++;
            }

            var state = command.TheaterState;
            float now = Time.unscaledTime;
            return new Dictionary<string, object>
            {
                { "ok", true }, { "t", Time.timeSinceLevelLoad },
                { "census", Describe(applied, command.LastCensusAt >= 0f ? now - command.LastCensusAt : -1f) },
                { "fresh", Describe(fresh, measured ? 0f : -1f) },
                {
                    "ticker", new Dictionary<string, object>
                    {
                        { "level", state.DefconLevel }, { "territory", Finite(state.TerritoryControlRatio) }, { "airRatio", Finite(state.AirSuperiorityRatio) },
                        { "clashes", state.ActiveClashesCount }, { "contestedBases", state.ContestedAirbaseCount }, { "warning", state.ActiveThreatWarning ?? "" },
                    }
                },
                {
                    "grid", new Dictionary<string, object>
                    {
                        { "available", grid != null ? 1 : 0 },
                        { "sectorsContested", grid != null ? grid.ContestedSectorCount : -1 }, { "sectorsFriendly", grid != null ? grid.FriendlySectorCount : -1 },
                        { "sectorsHostile", grid != null ? grid.HostileSectorCount : -1 }, { "sectorsNeutral", grid != null ? grid.NeutralSectorCount : -1 },
                        { "cells", grid != null ? grid.TotalSectors : -1 }, { "contestedBases", gridBases },
                    }
                },
                {
                    "check", new Dictionary<string, object>
                    {
                        // The census counts friendly bases with hostile presence from the same field the game-side loop reads; a stale or miswired figure shows here.
                        { "contestedBasesDelta", measured && gridBases >= 0 ? Math.Abs(fresh.BasesContested - gridBases) : -1 },
                        { "sectorsDelta", measured && grid != null ? Math.Abs(fresh.SectorsContested - grid.ContestedSectorCount) : -1 },
                        // The ticker bag is the applied census, one writer: its level can differ from the applied threat only by a tick in flight.
                        { "tickerLevelDelta", command.LastCensusAt >= 0f ? Math.Abs(state.DefconLevel - applied.Threat) : -1 },
                    }
                },
            };
        }

        private static Dictionary<string, object> Describe(in TheaterCensus c, float ageSeconds) => new Dictionary<string, object>
        {
            { "known", c.Known ? 1 : 0 }, { "ageSeconds", ageSeconds },
            { "airFriendly", c.AirFriendly }, { "airParked", c.AirParked }, { "airHostileTracked", c.AirHostileTracked },
            { "groundFriendly", c.GroundFriendly }, { "groundHostileTracked", c.GroundHostileTracked },
            { "shipsFriendly", c.ShipsFriendly }, { "shipsHostileTracked", c.ShipsHostileTracked },
            { "basesFriendly", c.BasesFriendly }, { "basesHostile", c.BasesHostile }, { "basesNeutral", c.BasesNeutral }, { "basesContested", c.BasesContested },
            { "radars", c.Radars },
            { "sectorsFriendly", c.SectorsFriendly }, { "sectorsHostile", c.SectorsHostile }, { "sectorsContested", c.SectorsContested }, { "sectorsNeutral", c.SectorsNeutral },
            { "sortieCap", c.SortieCap }, { "sortieSead", c.SortieSead }, { "sortieCas", c.SortieCas }, { "sortieStrike", c.SortieStrike }, { "sortieTransit", c.SortieTransit },
            { "frontMetres", c.FrontMetres }, { "threat", (int)c.Threat }, { "threatWhy", c.ThreatWhy.ToString() },
        };

        // A scenario check compares numbers and the sim writes NaN as null: an unknown ratio is -1.
        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? -1f : value;

        private static bool TryFaction(string name, out FactionHQ hq)
        {
            hq = null;
            if (string.IsNullOrEmpty(name)) return GameAccess.TryGetLocalFaction(out hq) && hq != null;
            foreach (FactionHQ candidate in FactionRegistry.GetAllHQs())
                if (candidate?.faction != null && string.Equals(candidate.faction.factionName, name, StringComparison.OrdinalIgnoreCase)) { hq = candidate; return true; }
            return false;
        }
    }
#endif
}
