using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>Place-named fronts: keys, display names and the compass words that replace "FRONT 3/-2" (annex 01 E1).</summary>
    internal static class PlaceNamesTests
    {
        public static void Run()
        {
            NearestResolvesTiesById();
            NearestSkipsBadInput();
            DescribeIsBareInsideAndCompassOutside();
            CompassOctantsWithNorthAsPositiveZ();
            StripTagsDropsMarkupAndControls();
            LooksAuthoredRejectsIdentifiers();
            KeysAreStableAndBounded();
            ClipNeverExceedsOrEndsInADot();
        }

        private static Place At(string id, float x, float z, string name = null) => new Place(id, name ?? id, x, z, false);

        private static void NearestResolvesTiesById()
        {
            var places = new List<Place> { At("zulu", 1000f, 0f), At("alpha", -1000f, 0f), At("mike", 0f, 5000f) };
            int index = PlaceNames.Nearest(places, 0f, 0f, out float distance);
            TestAssert.Eq(places[index].Id, "alpha", "a tie goes to the lower ordinal id whatever the list order");
            TestAssert.Near(distance, 1000f, "the distance is the straight line to the winner");
            places.Reverse();
            TestAssert.Eq(places[PlaceNames.Nearest(places, 0f, 0f, out _)].Id, "alpha", "the winner does not depend on list order");
            TestAssert.Eq(places[PlaceNames.Nearest(places, 0f, 4900f, out _)].Id, "mike", "a nearer place wins over the id order");
        }

        private static void NearestSkipsBadInput()
        {
            TestAssert.Eq(PlaceNames.Nearest(null, 0f, 0f, out float d0), -1, "no list is no place");
            TestAssert.Eq(d0, -1f, "no place reads distance -1, not zero");
            TestAssert.Eq(PlaceNames.Nearest(new List<Place>(), 0f, 0f, out _), -1, "an empty book is no place");
            var places = new List<Place> { At("bad", float.NaN, 0f), At("far", 9000f, 0f) };
            TestAssert.Eq(PlaceNames.Nearest(places, 0f, 0f, out _), 1, "a place with a non-finite position is skipped");
            TestAssert.Eq(PlaceNames.Nearest(places, float.NaN, 0f, out _), -1, "a non-finite query finds nothing");
        }

        private static void DescribeIsBareInsideAndCompassOutside()
        {
            TestAssert.Eq(PlaceNames.Describe("PORT AURELIA", 0f, 0f, 3000f, 4000f), "PORT AURELIA", "5 km out is still the bare name");
            TestAssert.Eq(PlaceNames.Describe("PORT AURELIA", 0f, 0f, 8000f, 0f), "PORT AURELIA", "exactly 8 km is still the bare name");
            TestAssert.Eq(PlaceNames.Describe("PORT AURELIA", 0f, 0f, 9000f, 9000f), "NE OF PORT AURELIA", "past 8 km the word names the side");
            TestAssert.Eq(PlaceNames.Describe("PORT AURELIA", 0f, 0f, -12000f, 0f), "W OF PORT AURELIA", "west of the place");
            TestAssert.Eq(PlaceNames.Describe("PORT AURELIA", 100f, 100f, 100f, -20000f), "S OF PORT AURELIA", "south is negative z");
            string longName = PlaceNames.Clip("THE VERY LONG NAMED AIRBASE OF NOWHERE");
            string described = PlaceNames.Describe(longName, 0f, 0f, 20000f, 20000f);
            TestAssert.That(described.Length <= PlaceNames.MaximumName, "the compass prefix never pushes a name past the display clip");
            TestAssert.That(described.StartsWith("NE OF "), "a long name keeps its compass prefix");
            TestAssert.Eq(PlaceNames.Describe("PORT AURELIA", 0f, 0f, float.NaN, 0f), "PORT AURELIA", "a non-finite point falls back to the bare name");
            TestAssert.Eq(PlaceNames.Describe(null, 0f, 0f, 0f, 0f), "", "no name is empty");
        }

        private static void CompassOctantsWithNorthAsPositiveZ()
        {
            TestAssert.Eq(PlaceNames.Compass(0f, 1f), "N", "north is +z");
            TestAssert.Eq(PlaceNames.Compass(1f, 1f), "NE", "east and north");
            TestAssert.Eq(PlaceNames.Compass(1f, 0f), "E", "east is +x");
            TestAssert.Eq(PlaceNames.Compass(1f, -1f), "SE", "east and south");
            TestAssert.Eq(PlaceNames.Compass(0f, -1f), "S", "south");
            TestAssert.Eq(PlaceNames.Compass(-1f, -1f), "SW", "west and south");
            TestAssert.Eq(PlaceNames.Compass(-1f, 0f), "W", "west");
            TestAssert.Eq(PlaceNames.Compass(-1f, 1f), "NW", "west and north");
            TestAssert.Eq(PlaceNames.Compass(0.4f, 1f), "N", "a shallow angle stays in its octant");
            TestAssert.Eq(PlaceNames.Compass(1f, 0.4f), "E", "a shallow angle from east stays east");
            TestAssert.Eq(PlaceNames.Compass(0f, 0f), "N", "the origin has no direction and does not throw");
            TestAssert.Eq(PlaceNames.Compass(float.NaN, 1f), "", "a non-finite offset has no compass word");
        }

        private static void StripTagsDropsMarkupAndControls()
        {
            TestAssert.Eq(PlaceNames.StripTags("<color=red>Port</color> Aurelia"), "Port Aurelia", "rich text goes");
            TestAssert.Eq(PlaceNames.StripTags(null), "", "null is empty");
            TestAssert.Eq(PlaceNames.StripTags("  PORT \t\n  AURELIA  "), "PORT AURELIA", "whitespace collapses to single spaces and trims");
            TestAssert.Eq(PlaceNames.StripTags("A\u0001B"), "AB", "a control character is dropped");
            TestAssert.Eq(PlaceNames.StripTags("A < B"), "A < B", "a lone angle bracket is text, not markup");
            TestAssert.Eq(PlaceNames.StripTags("<b></b>"), "", "only markup leaves nothing");
        }

        private static void LooksAuthoredRejectsIdentifiers()
        {
            TestAssert.That(!PlaceNames.LooksAuthored("HoldKeyAirbases", "HoldKeyAirbases"), "a display name equal to the unique name was never typed");
            TestAssert.That(PlaceNames.LooksAuthored("Port Aurelia", "port_aurelia"), "a spaced name that differs is authored");
            TestAssert.That(!PlaceNames.LooksAuthored("hold_key_airbases", "x"), "an underscore marks an identifier");
            TestAssert.That(!PlaceNames.LooksAuthored("", "x"), "empty is not authored");
            TestAssert.That(!PlaceNames.LooksAuthored(null, "x"), "null is not authored");
            TestAssert.That(PlaceNames.LooksAuthored("Outpost", null), "a missing unique name does not block a real one");
            TestAssert.That(PlaceNames.LooksAuthored("OUTPOST ALPHA", "o1"), "a spaced upper-case name is authored");
            TestAssert.That(!PlaceNames.LooksAuthored("OUTPOST", "o1"), "one upper-case token has no space or lower case, so it reads as an id");
        }

        private static void KeysAreStableAndBounded()
        {
            TestAssert.Eq(PlaceNames.SectorKey("air_7"), "@air_7", "a sector key is the prefix and the place id");
            TestAssert.Eq(PlaceNames.BaseKey("air_7"), "#air_7", "a base key is the prefix and the place id");
            TestAssert.Eq(PlaceNames.SectorKey(""), "", "no id, no key");
            TestAssert.Eq(PlaceNames.SectorKey(null), "", "null id, no key");
            TestAssert.Eq(PlaceNames.BaseKey(null), "", "null id, no base key");
            TestAssert.Eq(PlaceNames.SectorKey(new string('x', 200)).Length, 96, "a key is at most the wire's 96 characters");
            TestAssert.Eq(PlaceNames.BaseKey(new string('x', 200)).Length, 96, "a base key is bounded the same way");
            var a = new List<Place> { At("one", 0f, 0f), At("two", 5f, 5f), At("three", 9f, 9f) };
            var b = new List<Place> { a[2], a[0], a[1] };
            TestAssert.Eq(PlaceNames.SectorKey(a[PlaceNames.Nearest(a, 4f, 4f, out _)].Id),
                PlaceNames.SectorKey(b[PlaceNames.Nearest(b, 4f, 4f, out _)].Id), "the key does not depend on the order of the place list");
        }

        private static void ClipNeverExceedsOrEndsInADot()
        {
            TestAssert.Eq(PlaceNames.Clip("PORT AURELIA"), "PORT AURELIA", "a short name is untouched");
            TestAssert.Eq(PlaceNames.Clip(null), "", "null clips to empty");
            string clipped = PlaceNames.Clip("NORTHERN FORWARD OPERATING BASE KESTREL");
            TestAssert.That(clipped.Length <= PlaceNames.MaximumName, "a long name fits the display clip");
            TestAssert.That(!clipped.EndsWith(" ") && !clipped.EndsWith("."), "a cut never ends in a space or a dot");
            TestAssert.Eq(clipped, "NORTHERN FORWARD OPERATING", "the cut falls on a word boundary at or after half the limit");
            string hard = PlaceNames.Clip("ABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGH");
            TestAssert.Eq(hard.Length, PlaceNames.MaximumName, "one long word is hard cut at the limit");
            TestAssert.That(!hard.Contains("."), "a cut never adds an ellipsis");
            TestAssert.Eq(PlaceNames.Clip("AB CDEFGHIJKLMNOPQRSTUVWXYZABCDEFGH", 20), "AB CDEFGHIJKLMNOPQRS", "a space before half the limit is not a boundary, so the cut is hard");
            TestAssert.Eq(PlaceNames.Clip("PORT AURELIA.", 13), "PORT AURELIA", "a trailing dot is removed even when the name fits");
            TestAssert.Eq(PlaceNames.Clip("FT. SMITH NORTH FIELD.. ", 10), "FT. SMITH", "dots and spaces left at the cut are trimmed");
            for (int max = 4; max <= 28; max++)
            {
                string text = PlaceNames.Clip("ST. MARY OF THE LONG GRASS AIRFIELD NORTH", max);
                TestAssert.That(text.Length <= max && !text.EndsWith(".") && !text.EndsWith(" "), "clip at " + max + " obeys the bound and the end rule");
            }
        }
    }
}
