using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The sky summary of one point (spec 8.2) as pure rules over the three layers the field reads from the tape row: which deck a pilot
    /// is under (<c>NativeDeck</c>, the one-way output to <c>level.cloudHeight</c> and the cloud base of <c>IWeatherView</c>) and which layer the
    /// rain falls from (<c>RainBase</c> / <c>RainTop</c>, what <c>FlightWeatherAirMass.VerticalRain</c> reads). Unity-free and allocation-free.
    /// </summary>
    internal static class SkySummary
    {
        /// <summary>A layer whose cover times opacity reaches this is opaque: a pilot below it is under cloud.</summary>
        public const float OpaqueEffective = 0.5f;
        /// <summary>Raw cover from which a layer counts as present.</summary>
        public const float PresentCover = 0.05f;
        /// <summary>The lifted clamp of the native deck (it was 250-3600 m for the low base; an opaque altostratus or a cirrostratus veil sits higher).</summary>
        public const float MinDeck = 250f, MaxDeck = 9500f;
        /// <summary>Stratiform or cell rain below this (mm/h) does not make a layer rain-bearing.</summary>
        public const float RainBearing = 0.1f;

        /// <summary>
        /// The base and top of the deck the native <c>cloudHeight</c> follows: the lowest layer with <c>cover * opacity &gt;= 0.5</c>; else the lowest
        /// layer with any cover (<see cref="PresentCover"/>); else the nominal low base with the low top. Layers run low, middle, high.
        /// </summary>
        public static void NativeDeck(float lowCover, float lowBase, float lowTop,
            float midCover, float midOpacity, float midBase, float midTop,
            float highCover, float highOpacity, float highBase, float highTop, out float deckBase, out float deckTop)
        {
            if (lowCover >= OpaqueEffective) { Pick(lowBase, lowTop, out deckBase, out deckTop); return; }
            if (midCover * midOpacity >= OpaqueEffective) { Pick(midBase, midTop, out deckBase, out deckTop); return; }
            if (highCover * highOpacity >= OpaqueEffective) { Pick(highBase, highTop, out deckBase, out deckTop); return; }
            if (lowCover >= PresentCover) { Pick(lowBase, lowTop, out deckBase, out deckTop); return; }
            if (midCover >= PresentCover) { Pick(midBase, midTop, out deckBase, out deckTop); return; }
            if (highCover >= PresentCover) { Pick(highBase, highTop, out deckBase, out deckTop); return; }
            Pick(lowBase, lowTop, out deckBase, out deckTop);
        }

        /// <summary>
        /// The layer the rain falls from. Cell rain and stratiform rain under a low deck come from the low layer; stratiform rain that only the middle
        /// layer carries (embedded showers in an altostratus with no low sheet) comes from the middle layer. A dry point reports the low layer, the old default.
        /// </summary>
        public static void RainLayer(float cellRain, float stratiformRain, float lowCover, float midEffective,
            float lowBase, float lowTop, float midBase, float midTop, out float rainBase, out float rainTop)
        {
            bool stratiformFromMid = stratiformRain >= RainBearing && cellRain < RainBearing && midEffective > lowCover;
            if (stratiformFromMid) { rainBase = midBase; rainTop = Math.Max(midBase, midTop); return; }
            rainBase = lowBase;
            rainTop = Math.Max(lowBase, lowTop);
        }

        private static void Pick(float layerBase, float layerTop, out float deckBase, out float deckTop)
        {
            deckBase = Scalar.Clamp(layerBase, MinDeck, MaxDeck);
            deckTop = Math.Max(deckBase, layerTop);
        }
    }
}
