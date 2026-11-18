using System;
using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The mod's scenery kit (mapkit.bskit): trees, rocks and props by kind name and variant. The Maps module fills both
    /// lookups and loads the kit on the first call, on any map including vanilla ones.
    /// </summary>
    internal static class MapKit
    {
        /// <summary>The inactive template of a kind's variant, to Instantiate; null when the kit lacks it.</summary>
        internal static Func<string, int, GameObject> Prototype = (kind, variant) => null;

        /// <summary>Every kind name in the kit; empty when no kit is loaded.</summary>
        internal static Func<string[]> Kinds = () => new string[0];
    }
}
