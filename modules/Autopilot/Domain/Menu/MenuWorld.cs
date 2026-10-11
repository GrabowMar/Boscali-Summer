using System.Collections.Generic;

namespace BoscaliSummer.Modules.Autopilot.Domain
{
    internal enum GearPhase : byte { None, Up, Down, Extending, Retracting }

    internal enum ViewKind : byte { Other, Cockpit, Orbit }

    internal enum StationClass : byte { Other, Gun, Missile, Agm, Bomb, Turret }

    internal sealed class WorldStation
    {
        /// <summary>Upper-case short name, at most 14 characters.</summary>
        public string Name = "";
        public int Ammo;
        public StationClass Class;
    }

    internal sealed class WorldPerk
    {
        public string Label = "";
        /// <summary>Support's own note: "SPACE R2 · 6 ALLOC" while ready.</summary>
        public string Note = "";
        public bool Armed;
        /// <summary>SPACE, CYBER or SOF: the group the perk sits under and the glyph it wears. Empty = ungrouped.</summary>
        public string Front = "";
        /// <summary>Seconds left on its cooldown; 0 = ready or armed.</summary>
        public int Cooldown;
    }

    /// <summary>One formation the wing could fly.</summary>
    internal sealed class WorldShape
    {
        /// <summary>Upper-case game name, e.g. FINGER FOUR (STRONG RIGHT).</summary>
        public string Name = "";
        /// <summary>CLASSIC, ESCORT, ROTARY, TACTICAL: the dock group.</summary>
        public string Family = "";
        public bool Active;
    }

    internal sealed class WorldPitStation
    {
        public int Index;
        public string Label = "", State = "";
        public bool First;
    }

    internal sealed class WorldFilter
    {
        public string Name = "";
        public bool Active;
        public bool Allowed = true;
        /// <summary>1-based quick slot the page lists it under.</summary>
        public int Slot;
    }

    /// <summary>
    /// Plain data: everything the interaction menu reads from the game and from the other modules'
    /// contracts, in one snapshot. <see cref="MenuCatalog"/> turns it into the menu model, so the whole catalog
    /// (labels, state lines, hybrid availability, badges) is pure and testable; the Runtime adapter only fills this in.
    /// </summary>
    internal sealed class MenuWorld
    {
        public string Aircraft = "SELF";
        /// <summary>Radar altitude at or above the gear/flare threshold: not on the apron.</summary>
        public bool Airborne = true;
        /// <summary>Live <c>Aircraft.GetFuelLevel()</c>, 0..1.</summary>
        public float Fuel = 1f;
        public int Targets;
        /// <summary>Distance to the nearest selected target in kilometres, negative = none.</summary>
        public float NearestKm = -1f;
        public bool HudReady = true;

        // FLIGHT
        public GearPhase Gear = GearPhase.Up;
        public bool HasAssist, Assist, HasHover, Hover;
        public bool AutolandEngaged;
        /// <summary>Non-null: why the autopilot cannot take the approach now.</summary>
        public string AutolandBlock;
        public bool Ignition = true;
        public bool CanEject = true;

        // LIGHTS
        public bool HasNav, NavOn;
        public bool HasLandingLight;
        /// <summary>0 OFF, 1 ON, 2 AUTO.</summary>
        public int LandingLight;
        public bool HasNvg, NvgOn;

        // WEAPONS
        public List<WorldStation> Stations = new List<WorldStation>();
        public int ActiveStation;
        public bool HasMultipleGuns;
        public bool GunsLinked;
        public bool HasTurrets;
        public bool TurretAuto;

        // DEFENCE
        public bool HasCountermeasures;
        /// <summary>The airframe has a flare ejector (POP FLARES exists).</summary>
        public bool HasFlares;
        public int Flares, FlaresMax;
        /// <summary>Active countermeasure name, upper case.</summary>
        public string Countermeasure = "";
        /// <summary>The countermeasure a press switches to, upper case; empty when the airframe has only one.</summary>
        public string NextCountermeasure = "";
        /// <summary>Index of the active countermeasure in the game's own (alphabetical) list.</summary>
        public int CountermeasureIndex;
        public bool HasRadar, RadarOn;
        public bool HasDrones, DronesStriking, DronesCanStrike;
        /// <summary>REMORA drones flying now.</summary>
        public int Drones;

        // VIEW
        public bool CameraFollowing = true;
        public ViewKind Camera = ViewKind.Cockpit;
        public bool HasCockpitPoint = true;
        public bool MapAvailable = true;
        public bool MissileViewAvailable;
        public bool MissileViewActive;
        public bool CanEnterMissileView;
        public int Missiles;
        public string MissileStatus = "";
        public string MissileCurrent = "";
        /// <summary>The TAC BOARD options (<c>ITacBoardSettings</c>) are installed: VIEW lists the two board rows.</summary>
        public bool BoardAvailable;
        public bool BoardOn;
        /// <summary>Index into <see cref="MenuCatalog.BoardDeclutters"/>: FULL, LEAN, MIN.</summary>
        public int BoardDeclutter;

        // SUPPORT
        public bool MarkAvailable;
        public bool HasMark;
        public float MarkAgeSeconds;
        public bool CanCapture;
        public bool CanCallAtMark;
        /// <summary>Name of the armed support action, or empty.</summary>
        public string Armed = "";
        public bool PerksAvailable;
        public List<WorldPerk> Perks = new List<WorldPerk>();
        public bool SkywellCarried, SkywellDeployed;
        public string SkywellStock = "";
        public bool PitAvailable;
        public byte PitMode;
        public List<WorldPitStation> PitStations = new List<WorldPitStation>();
        public bool CanExfil;

        // RADIO
        public bool DeckAvailable, DeckPlaying;
        public string DeckTitle = "";
        public bool ReceiverAvailable, ReceiverOn;
        public string Station = "";
        public bool CanStopAll;

        // COMMS
        public bool CallsAvailable, CanCall;
        public List<string> CallLabels = new List<string>();

        // WING
        public int Wingmen;
        public string Shape = "";
        /// <summary>The formations that suit the wing now, in the game's order.</summary>
        public List<WorldShape> Shapes = new List<WorldShape>();

        // TARGET FILTERS (the contributed preset page)
        public List<WorldFilter> Filters = new List<WorldFilter>();
    }
}
