using System;
using System.Collections.Generic;
using System.Text;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    internal readonly struct Place
    {
        internal readonly string Id, Name;     // Id = SavedAirbase.UniqueName, Name = display name already clipped
        internal readonly float X, Z;          // global metres
        internal readonly bool Owned;          // the viewing faction holds it
        internal Place(string id, string name, float x, float z, bool owned) { Id = id; Name = name; X = x; Z = z; Owned = owned; }
    }

    /// <summary>Pure naming of fronts after places: keys that survive a contour moving, and short names a pilot can read.</summary>
    internal static class PlaceNames
    {
        internal const char SectorPrefix = '@', BasePrefix = '#';
        internal const int MaximumName = 28;                                   // display clip; the wire cap is 32
        internal const float DescribeRadius = 8000f;
        private const int MaximumId = 95;                                      // prefix plus id fits the wire's 96-character key

        private static readonly string[] Octants = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        internal static string SectorKey(string placeId) => Key(SectorPrefix, placeId);

        internal static string BaseKey(string placeId) => Key(BasePrefix, placeId);

        private static string Key(char prefix, string placeId)
        {
            if (string.IsNullOrEmpty(placeId)) return "";
            return prefix + (placeId.Length > MaximumId ? placeId.Substring(0, MaximumId) : placeId);
        }

        /// <summary>Drops &lt;...&gt; rich text and control characters and collapses runs of white space. A '&lt;' with no closing '&gt;' is text.</summary>
        internal static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var builder = new StringBuilder(text.Length);
            bool gap = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    if (close >= 0) { i = close; continue; }
                }
                if (char.IsWhiteSpace(c)) { gap = builder.Length > 0; continue; }
                if (char.IsControl(c)) continue;
                if (gap) { builder.Append(' '); gap = false; }
                builder.Append(c);
            }
            return builder.ToString();
        }

        /// <summary>Cuts at a word boundary at or after half the limit, else hard; never adds an ellipsis and never leaves a dot or a space at the end.</summary>
        internal static string Clip(string name, int max = MaximumName)
        {
            if (string.IsNullOrEmpty(name) || max <= 0) return "";
            string text = name.Trim();
            if (text.Length > max)
            {
                int cut = max;
                for (int i = max - 1; i >= max / 2 && i > 0; i--)
                    if (text[i] == ' ') { cut = i; break; }
                text = text.Substring(0, cut);
            }
            return text.TrimEnd(' ', '.');
        }

        /// <summary>True for a name a mission author plausibly typed: not empty, not the unique name, no underscore, and a space or a lower-case letter.</summary>
        internal static bool LooksAuthored(string displayName, string uniqueName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return false;
            if (string.Equals(displayName, uniqueName, StringComparison.Ordinal) || displayName.IndexOf('_') >= 0) return false;
            for (int i = 0; i < displayName.Length; i++)
                if (displayName[i] == ' ' || char.IsLower(displayName[i])) return true;
            return false;
        }

        /// <summary>Index of the nearest place with a finite position, ties by ordinal Id; -1 (and distance -1) when none.</summary>
        internal static int Nearest(IReadOnlyList<Place> places, float x, float z, out float distance)
        {
            distance = -1f;
            if (places == null || !IsFinite(x) || !IsFinite(z)) return -1;
            int best = -1;
            double bestSquared = 0d;
            for (int i = 0; i < places.Count; i++)
            {
                Place p = places[i];
                if (!IsFinite(p.X) || !IsFinite(p.Z)) continue;
                double dx = p.X - x, dz = p.Z - z, squared = dx * dx + dz * dz;
                if (best < 0 || squared < bestSquared ||
                    squared == bestSquared && string.CompareOrdinal(p.Id, places[best].Id) < 0)
                {
                    best = i;
                    bestSquared = squared;
                }
            }
            if (best >= 0) distance = (float)Math.Sqrt(bestSquared);
            return best;
        }

        /// <summary>The bare name within DescribeRadius of the place, otherwise "NE OF name"; always within MaximumName.</summary>
        internal static string Describe(string placeName, float placeX, float placeZ, float x, float z)
        {
            string name = Clip(StripTags(placeName));
            if (name.Length == 0 || !IsFinite(placeX) || !IsFinite(placeZ) || !IsFinite(x) || !IsFinite(z)) return name;
            double east = (double)x - placeX, north = (double)z - placeZ;
            if (east * east + north * north <= (double)DescribeRadius * DescribeRadius) return name;
            string prefix = Compass((float)east, (float)north) + " OF ";
            return prefix + Clip(name, MaximumName - prefix.Length);
        }

        /// <summary>One of eight words for the side a point lies on. +z is north (modules/Hud/Domain/Scope/ObjectiveMath.cs); "" for a non-finite offset.</summary>
        internal static string Compass(float eastMeters, float northMeters)
        {
            if (!IsFinite(eastMeters) || !IsFinite(northMeters)) return "";
            double degrees = Math.Atan2(eastMeters, northMeters) * 180d / Math.PI;      // clockwise from north
            int octant = (int)Math.Floor((degrees + 22.5d) / 45d);
            return Octants[((octant % 8) + 8) % 8];
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
