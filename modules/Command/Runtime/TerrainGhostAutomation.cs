#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// Explicit nomodkit single-player test hook (tests/ingame/terrain-ghost.json); inert outside a sim run. Sets the visor terrain
    /// ghost's strength in memory and reports what the overlay is doing.
    /// </summary>
    public static class TerrainGhostAutomation
    {
        /// <summary>
        /// <c>{strength: 0..1}</c> sets the ghost's strength in place of the saved TerrainGhost setting; <c>{restore: true}</c> gives the saved
        /// setting back. IN MEMORY ONLY: the config file is never written. Returns the overlay: <c>strength</c> (what applies now),
        /// <c>supportedMap</c> (a baked heightfield was found), <c>materialFound</c> (the line shader exists), <c>vertexCount</c> of the mesh
        /// (0 until it first draws), <c>lastRebuildMs</c>, <c>fade</c> (0..1, the slow in and out), <c>cautionVertices</c> (vertices now drawn in the
        /// caution colour). The overlay takes a change on its next frame and fades in over 0.4 s: wait before a capture.
        /// </summary>
        public static Dictionary<string, object> Configure(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("TerrainGhostAutomation", "guard", "requires a hosted single-player nomodkit scenario");
            TerrainGhostOverlay overlay = TerrainGhostOverlay.Live;
            if (overlay == null) return Failure("TerrainGhostAutomation", "Configure", "no terrain ghost overlay");
            if (Bool(args, "restore")) overlay.StrengthOverride = null;
            else if (Has(args, "strength")) overlay.StrengthOverride = Mathf.Clamp01(Number(args, "strength", 0f));
            Dictionary<string, object> state = overlay.State();
            Debug.Log("[TerrainGhostAutomation] Configure: " + Describe(state));
            return state;
        }
    }
}
#endif
