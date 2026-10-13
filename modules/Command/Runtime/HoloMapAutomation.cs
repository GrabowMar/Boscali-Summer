using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Command.Configuration;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// Explicit nomodkit single-player test hook (tests/ingame/holo-map.json); inert outside a sim run. Opens and closes the big HOLO MAP without the map key (a scenario cannot hold it: an opened holo stays up until <c>open: false</c>), sets
    /// the TAC MAP mode, the mini range, the big range and the orientation in memory, and reports what the holo is doing.
    /// </summary>
    public static class HoloMapAutomation
    {
        /// <summary>
        /// <c>{open: bool}</c> opens (true) or closes (false) the BIG map, with the same availability test the map key's hold uses, and draws the first picture at once (closing it leaves the standing MINI, or the game's corner map on
        /// CLASSIC); <c>{tacMode: "Holo"|"Classic"}</c>, <c>{miniRangeKm: 10|20|40}</c>, <c>{rangeKm: 20|40|80}</c> and <c>{northUp: bool}</c> set the TAC MAP mode, the MINI's range, the BIG one's range (kilometres from the
        /// aircraft to the frame's side) and the orientation IN MEMORY ONLY (the saved settings are never written); <c>{resetTimings: true}</c> clears the worst timings and the counts (after the warm-up); <c>{restore: true}</c> gives the
        /// saved settings back. Returns the holo: <c>active</c> (HoloMap.Active: the MINI or the BIG is up), <c>stage</c> (Mini = the game's corner map showing, Holo or Mfd), <c>presentation</c> ("mini", "big" or "none"), <c>tacMode</c>,
        /// <c>additiveMaterial</c> (the game's additive UI material was found), <c>renderTexture</c> (created), <c>depthPath</c> (the texture has a depth buffer: solid, hidden lines removed; false is the see-through fallback),
        /// <c>vertexCount</c> (nodes of the lattice), <c>litVertices</c>, <c>gridCellsX</c> / <c>gridCellsZ</c> / <c>gridCellMetres</c> (the lattice the frame needs and its cell), <c>reachKm</c>, <c>marksDrawn</c> (units, airbases and the
        /// player's aircraft), <c>unitMarks</c>, <c>unitCap</c>, <c>airbaseMarks</c>, <c>labelsUsed</c>, <c>wedgeDegrees</c> (the MINI's view wedge, 0 when it is not drawn), <c>gridRef</c> (the game's grid reference as the MINI shows
        /// it), <c>scaleKm</c> (the scale bar's length), <c>mapHidden</c> (the corner map is faded out; true once the presentation has faded in), <c>nativeMapAlpha</c> (1 when the game's corner map has not been touched), <c>grow</c>
        /// (0 MINI .. 1 BIG, the 180 ms change: wait before a capture), <c>presence</c> (0..1, the fade-in), the frame (<c>frameWidth</c> / <c>frameHeight</c> in canvas units, <c>frameUnitsWide</c> / <c>frameUnitsHigh</c> in design
        /// units, <c>frameAspect</c>, <c>frameHeightShare</c>, <c>frameCentreX</c> / <c>frameCentreY</c> as shares of the canvas, <c>frameWidthPixels</c> / <c>frameHeightPixels</c>, <c>miniFramePixels</c> / <c>bigFramePixels</c> as
        /// "WxH" in screen pixels, <c>renderWidth</c> / <c>renderHeight</c>: the part of the texture drawn), <c>tiltDegrees</c>, <c>aircraftUp</c>, <c>rangeKm</c> (of the picture shown), <c>miniRangeKm</c>, <c>bigRangeKm</c>,
        /// <c>northUp</c>, the timings (<c>lastRenderMs</c>, <c>worstRenderMs</c>, <c>renders</c>: a picture, without the scan; <c>lastFillMs</c>, <c>worstFillMs</c>, <c>fills</c>: the lattice fill; <c>lastScanMs</c>,
        /// <c>worstScanMs</c>, <c>scans</c>), <c>broken</c> (an exception took the holo down) and <c>failure</c> (why a presentation was refused, empty otherwise).
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
                holo.MiniRangeOverride = null;
                holo.NorthOverride = null;
                holo.TacOverride = null;
            }
            if (Has(args, "rangeKm")) holo.RangeOverride = Mathf.RoundToInt(Number(args, "rangeKm", 20f));
            if (Has(args, "miniRangeKm")) holo.MiniRangeOverride = Mathf.RoundToInt(Number(args, "miniRangeKm", 10f));
            if (Has(args, "northUp")) holo.NorthOverride = Bool(args, "northUp");
            if (Has(args, "tacMode"))
                holo.TacOverride = string.Equals(Text(args, "tacMode"), "Classic", StringComparison.OrdinalIgnoreCase) ? TacMapMode.Classic : TacMapMode.Holo;
            if (Bool(args, "resetTimings")) holo.ResetTimings();
            if (Has(args, "open")) holo.SimOpen(Bool(args, "open"));
            Dictionary<string, object> state = holo.State();
            Debug.Log("[HoloMapAutomation] Configure: " + Describe(state));
            return state;
        }
    }
}
