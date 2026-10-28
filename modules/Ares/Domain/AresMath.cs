using System;

namespace BoscaliSummer.Modules.Ares.Domain
{
    /// <summary>What the SPAWN list shows.</summary>
    internal enum AresKind { Air, Ground, Naval, Structure, Kit }

    /// <summary>One member of a kit: a unit found by its preferred game key, else the first whose key or name holds the keyword,
    /// at an offset from the click (x right, z forward of the placement heading, metres). A key "#n" is the n-th ship in the catalog.</summary>
    internal readonly struct AresKitMember
    {
        public AresKitMember(string key, string keyword, float x, float z)
        {
            Key = key;
            Keyword = keyword;
            X = x;
            Z = z;
        }

        public readonly string Key, Keyword;
        public readonly float X, Z;
    }

    internal sealed class AresKit
    {
        public AresKit(string name, string blurb, bool naval, params AresKitMember[] members)
        {
            Name = name;
            Blurb = blurb;
            Naval = naval;
            Members = members;
        }

        public readonly string Name, Blurb;
        public readonly bool Naval;
        public readonly AresKitMember[] Members;
    }

    /// <summary>Pure geometry and data for ARES: where a group stands, which way a drag points, where a barrage falls, what a kit holds.</summary>
    internal static class AresMath
    {
        public static readonly AresKit[] Kits =
        {
            new AresKit("SAM BATTERY", "Search radar and three launchers.", false,
                new AresKitMember("RadarSAM1", "radar", 0f, 0f),
                new AresKitMember("SAMTrailer1", "sam", -45f, -25f),
                new AresKitMember("SAMTrailer1", "sam", 0f, -55f),
                new AresKitMember("SAMTrailer1", "sam", 45f, -25f)),
            new AresKit("ARMOUR PLATOON", "Four main battle tanks in line.", false,
                new AresKitMember("MBT1", "mbt", -36f, 0f),
                new AresKitMember("MBT1", "mbt", -12f, 0f),
                new AresKitMember("MBT1", "mbt", 12f, 0f),
                new AresKitMember("MBT1", "mbt", 36f, 0f)),
            new AresKit("MECH SECTION", "Two IFVs leading two APCs.", false,
                new AresKitMember("AFV8_IFV", "ifv", -15f, 0f),
                new AresKitMember("AFV8_IFV", "ifv", 15f, 0f),
                new AresKitMember("AFV8_APC", "apc", -15f, -28f),
                new AresKitMember("AFV8_APC", "apc", 15f, -28f)),
            new AresKit("AA NEST", "Two 23 mm guns and two MANPADS pits.", false,
                new AresKitMember("Emplacement1_23mm", "23mm", -22f, 0f),
                new AresKitMember("Emplacement1_23mm", "23mm", 22f, 0f),
                new AresKitMember("Emplacement1_MANPADS", "manpads", 0f, 22f),
                new AresKitMember("Emplacement1_MANPADS", "manpads", 0f, -22f)),
            new AresKit("FOB", "MG and ATGM pits, a radar shelter, two IFVs.", false,
                new AresKitMember("Emplacement1_MG", "_mg", -30f, 30f),
                new AresKitMember("Emplacement1_MG", "_mg", 30f, 30f),
                new AresKitMember("Emplacement1_ATGM", "atgm", 0f, 42f),
                new AresKitMember("RadarContainer1", "container", 0f, -8f),
                new AresKitMember("AFV8_IFV", "ifv", -22f, -32f),
                new AresKitMember("AFV8_IFV", "ifv", 22f, -32f)),
            new AresKit("NAVAL GROUP", "Three warships in a wedge.", true,
                new AresKitMember("#0", "", 0f, 0f),
                new AresKitMember("#1", "", -420f, -320f),
                new AresKitMember("#2", "", 420f, -320f))
        };

        /// <summary>Offset (right, forward) of unit <paramref name="index"/> of <paramref name="count"/>: rows of up to four, centred on
        /// the click, each row <paramref name="spacing"/> behind the one before.</summary>
        public static void Slot(int index, int count, float spacing, out float right, out float forward)
        {
            int perRow = Math.Clamp(count, 1, 4);
            int row = index / perRow, inRow = index % perRow;
            int rowCount = Math.Min(perRow, count - row * perRow);
            right = (inRow - (rowCount - 1) * 0.5f) * spacing;
            forward = -row * spacing;
        }

        /// <summary>A local (right, forward) offset turned to the world (x east, z north) for a heading in degrees from north.</summary>
        public static void Rotate(float right, float forward, float headingDeg, out float x, out float z)
        {
            double h = headingDeg * Math.PI / 180.0;
            double s = Math.Sin(h), c = Math.Cos(h);
            x = (float)(right * c + forward * s);
            z = (float)(-right * s + forward * c);
        }

        /// <summary>The heading (degrees from north, 0..360) a press-drag-release points; <paramref name="fallback"/> under 5 m of drag.</summary>
        public static float HeadingFromDrag(float dx, float dz, float fallback)
        {
            if (dx * dx + dz * dz < 25f) return fallback;
            double deg = Math.Atan2(dx, dz) * 180.0 / Math.PI;
            return (float)((deg + 360.0) % 360.0);
        }

        /// <summary>Barrage impact <paramref name="index"/>: a deterministic point spread evenly over a disk of <paramref name="radius"/>.</summary>
        public static void Barrage(int seed, int index, float radius, out float x, out float z)
        {
            uint h = Hash((uint)seed * 2654435761u ^ (uint)index * 40503u);
            double angle = (h & 0xffff) / 65536.0 * Math.PI * 2.0;
            double r = Math.Sqrt(((h >> 16) & 0xffff) / 65536.0) * radius;
            x = (float)(Math.Cos(angle) * r);
            z = (float)(Math.Sin(angle) * r);
        }

        /// <summary>"07:00" for an hour of the day; wraps past 24.</summary>
        public static string Hour(float hour)
        {
            float h = ((hour % 24f) + 24f) % 24f;
            int whole = (int)h;
            return whole.ToString("00") + ":" + ((int)((h - whole) * 60f)).ToString("00");
        }

        private static uint Hash(uint v)
        {
            v ^= v >> 16; v *= 0x7feb352d;
            v ^= v >> 15; v *= 0x846ca68b;
            return v ^ (v >> 16);
        }
    }
}
