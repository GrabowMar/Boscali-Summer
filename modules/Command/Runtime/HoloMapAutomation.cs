using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// Explicit nomodkit single-player test hook (tests/ingame/holo-map.json); inert outside a sim run. Opens and closes the HOLO MAP without the map key (a scenario cannot press it) and sets
    /// its range and orientation in memory, and reports what the holo is doing.
    /// </summary>
    public static class HoloMapAutomation
    {
        /// <summary>
        /// <c>{open: bool}</c> opens (true) or closes (false) the holo, the same way the map key does from the corner map, and draws the first picture at once; <c>{rangeKm: 10|20|40}</c> and
        /// <c>{northUp: bool}</c> set the range and the orientation IN MEMORY ONLY (the saved settings are never written); <c>{restore: true}</c> gives the saved settings back. Returns the holo:
        /// <c>active</c> (HoloMap.Active), <c>stage</c> (Mini, Holo or Mfd), <c>additiveMaterial</c> (the game's additive UI material was found), <c>renderTexture</c> (created),
        /// <c>vertexCount</c> of the mesh, <c>litVertices</c>, <c>marksDrawn</c> (units, airbases and the player's aircraft), <c>unitMarks</c>, <c>airbaseMarks</c>, <c>labelsUsed</c>,
        /// <c>lastRenderMs</c>, <c>mapHidden</c> (the corner map is faded out), <c>grow</c> (0..1, the 180 ms growth: wait before a capture), <c>rangeKm</c>, <c>northUp</c> and <c>failure</c>
        /// (why an open was refused, empty otherwise).
        /// </summary>
        public static Dictionary<string, object> Configure(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("HoloMapAutomation", "guard", "requires a hosted single-player nomodkit scenario");
            HoloMap holo = HoloMap.Live;
            if (holo == null) return Failure("HoloMapAutomation", "Configure", "no holo map");
            if (Bool(args, "restore"))
            {
                holo.RangeOverride = null;
                holo.NorthOverride = null;
            }
            if (Has(args, "rangeKm")) holo.RangeOverride = Mathf.RoundToInt(Number(args, "rangeKm", 20f));
            if (Has(args, "northUp")) holo.NorthOverride = Bool(args, "northUp");
            if (Has(args, "open")) holo.SimOpen(Bool(args, "open"));
            Dictionary<string, object> state = holo.State();
            Debug.Log("[HoloMapAutomation] Configure: " + Describe(state));
            return state;
        }
    }
}
