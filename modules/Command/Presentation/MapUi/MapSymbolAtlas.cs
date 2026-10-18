using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>A contact's platform class, as the map legend names it.</summary>
    internal enum MapGlyph
    {
        /// <summary>Tracked or wheeled armour: main battle tanks and fighting vehicles.</summary>
        Armor,
        /// <summary>Light or unarmoured combat vehicles.</summary>
        Light,
        /// <summary>Trucks and unmanned ground vehicles: logistics.</summary>
        Supply,
        /// <summary>Self-propelled artillery and rocket launchers.</summary>
        Artillery,
        /// <summary>Anti-aircraft guns.</summary>
        AntiAir,
        /// <summary>Surface-to-air missile launchers.</summary>
        Sam,
        /// <summary>Search and tracking radars.</summary>
        Radar,
        /// <summary>Rotorcraft.</summary>
        Helicopter,
        /// <summary>Fixed-wing aircraft.</summary>
        Aircraft,
        /// <summary>A fixed site or emplacement.</summary>
        Contact,
        /// <summary>Surface ship.</summary>
        Hull
    }

    /// <summary>Which side a contact is on; the map marks it with colour.</summary>
    internal enum MapSide { Friendly, Hostile, Neutral }

    /// <summary>
    /// One representative map icon per platform class, for the legend and for vehicles the baked sheet
    /// does not know. Airbases keep the game's own base icon.
    ///
    /// <para>The map's icons are the game's own plan-view silhouettes wherever the game has one
    /// (aircraft, helicopters, ships, emplacements) and <see cref="MapVehicleIcons"/> for ground
    /// vehicles, which the game draws as one capsule. Nothing here is drawn by hand: a class stands in
    /// as one typical member of it.</para>
    /// </summary>
    internal static class MapSymbolAtlas
    {
        private static readonly Dictionary<string, Sprite> vanilla =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static bool vanillaScanned;

        /// <summary>A typical icon of the class; null when neither the sheet nor the game has it loaded.</summary>
        internal static Sprite Glyph(MapGlyph glyph)
        {
            switch (glyph)
            {
                case MapGlyph.Armor: return MapVehicleIcons.For("MBT1");
                case MapGlyph.Light: return MapVehicleIcons.For("LCV45");
                case MapGlyph.Supply: return MapVehicleIcons.For("Truck2-L");
                case MapGlyph.Artillery: return MapVehicleIcons.For("HLT-MArt");
                case MapGlyph.AntiAir: return MapVehicleIcons.For("SPAAG1");
                case MapGlyph.Sam: return MapVehicleIcons.For("Linebreaker_SAM");
                case MapGlyph.Radar: return MapVehicleIcons.For("RadarSAM1");
                case MapGlyph.Helicopter: return Vanilla("mapIcon_attackHelo1");
                case MapGlyph.Aircraft: return Vanilla("mapIcon_multirole1");
                case MapGlyph.Hull: return Vanilla("frigate1_mapIcon");
                default: return Vanilla("mapIcon_emplacement1");
            }
        }

        /// <summary>One of the game's own map icons by sprite name. The loaded sprites are scanned once.</summary>
        private static Sprite Vanilla(string name)
        {
            if (!vanillaScanned)
            {
                vanillaScanned = true;
                foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (sprite != null && sprite.name.IndexOf("mapIcon", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        !vanilla.ContainsKey(sprite.name)) vanilla.Add(sprite.name, sprite);
            }
            return vanilla.TryGetValue(name, out Sprite found) ? found : null;
        }
    }
}
