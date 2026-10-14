using System;
using System.Collections.Generic;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The wing's part of the big map's right-click card (2026-10-14): rows about a clicked wingman and the wing orders
    /// that fit the click (the wingman's own, ATTACK on an enemy track, GO / ORBIT on bare ground). Implemented by the
    /// Wing module over its own order path; Command only prints the rows and presses the actions. Absent (Wing off) or
    /// with no wing flying it adds nothing.
    /// </summary>
    internal interface IMapWingMenu
    {
        /// <param name="clicked">The track under the cursor, or null for bare ground.</param>
        void Fill(Unit clicked, GlobalPosition ground, List<MapMenuRow> rows, List<MapMenuAction> actions);
    }

    internal struct MapMenuRow { internal string Key, Value; internal Color Tone; }

    /// <summary>Two neighbouring <see cref="Half"/> actions share one line.</summary>
    internal struct MapMenuAction { internal string Label; internal AvIcon Icon; internal Action Invoke; internal bool Half; }
}
