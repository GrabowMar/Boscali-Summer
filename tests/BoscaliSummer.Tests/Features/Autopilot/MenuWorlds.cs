using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    /// <summary>
    /// The prototype's scenarios as <see cref="MenuWorld"/> snapshots: the real catalog builder runs over these in the unit tests
    /// and in the offline Unity render check (this file is copied into the scratch Unity project, so it may only use the Domain types).
    /// </summary>
    internal static class MenuWorlds
    {
        private static WorldStation S(string name, int ammo, StationClass cls) => new WorldStation { Name = name, Ammo = ammo, Class = cls };

        /// <summary>CI-22 CRICKET at cruise: the prototype's default catalog.</summary>
        public static MenuWorld Default()
        {
            var w = new MenuWorld
            {
                Aircraft = "CI-22 CRICKET", Airborne = true, Fuel = 1f, Targets = 2, NearestKm = 4.2f,
                Gear = GearPhase.Up, HasAssist = true, Assist = true, HasHover = true, Hover = false, Ignition = true, CanEject = true,
                HasNav = true, NavOn = true, HasLandingLight = true, LandingLight = 2, HasNvg = true, NvgOn = false,
                ActiveStation = 1, HasMultipleGuns = true, HasTurrets = true, TurretAuto = false,
                HasCountermeasures = true, HasFlares = true, Flares = 48, FlaresMax = 48, Countermeasure = "IR FLARES", HasRadar = true, RadarOn = true,
                HasDrones = true, DronesStriking = false, DronesCanStrike = true,
                CameraFollowing = true, Camera = ViewKind.Cockpit, HasCockpitPoint = true, MapAvailable = true,
                MissileViewAvailable = true, CanEnterMissileView = true, Missiles = 2, MissileStatus = "2 MSL · CLOSEST 1.1 KM",
                MarkAvailable = true, HasMark = true, MarkAgeSeconds = 42f, CanCapture = true, CanCallAtMark = true, Armed = "SAT CAMERA",
                PerksAvailable = true, SkywellCarried = true, SkywellStock = "FUEL 5.9T · AMMO 1.20T", CanExfil = false,
                DeckAvailable = true, DeckPlaying = false, DeckTitle = "Salt Flats at Dawn", ReceiverAvailable = true, ReceiverOn = true,
                Station = "Boscali Republic Radio", CanStopAll = true,
                CallsAvailable = true, CanCall = true, Wingmen = 3, Shape = "FINGER FOUR",
            };
            w.Stations.AddRange(new[]
            {
                S("12.7MM GUN", 1000, StationClass.Gun), S("AAM-4 SKYWAY", 2, StationClass.Missile), S("AGM-48", 2, StationClass.Missile),
                S("CBU-12", 0, StationClass.Bomb), S("AAM-4 SKYWAY", 2, StationClass.Missile), S("MALD-X", 0, StationClass.Missile),
                S("23MM TURRET", 300, StationClass.Turret), S("SKYWELL KIT", 1, StationClass.Other),
            });
            string[] calls = { "ON MY WAY", "NEED SUPPORT", "ENGAGING", "SPIKE", "WINCHESTER", "BINGO FUEL", "RTB", "SPLASH ONE", "COPY", "NEGATIVE", "THANKS", "GG" };
            w.CallLabels.AddRange(calls);
            w.Perks.AddRange(new[]
            {
                new WorldPerk { Label = "RECON PASS", Note = "SPACE R1 · 4 ALLOC" },
                new WorldPerk { Label = "SAT CAMERA", Note = "SPACE R2 · 6 ALLOC", Armed = true },
                new WorldPerk { Label = "ELINT SWEEP", Note = "CYBER R1 · 4 ALLOC" },
                new WorldPerk { Label = "SAM NET DOWN", Note = "CYBER R4 · 16 ALLOC" },
                new WorldPerk { Label = "JTAC LASE", Note = "SOF R1 · 4 ALLOC" },
                new WorldPerk { Label = "RECON TEAM", Note = "SOF R2 · 6 ALLOC" },
                new WorldPerk { Label = "FORTIFY", Note = "SOF R3 · 10 ALLOC" },
            });
            w.Filters.AddRange(new[]
            {
                new WorldFilter { Name = "Air threats", Active = true }, new WorldFilter { Name = "SAM sites" }, new WorldFilter { Name = "SEAD package" },
            });
            return w;
        }

        public static MenuWorld NoWing() { MenuWorld w = Default(); w.Wingmen = 0; return w; }

        /// <summary>On the apron: gear, autoland, eject and flares are absent, the engine is shut down.</summary>
        public static MenuWorld Ground()
        {
            MenuWorld w = Default();
            w.Airborne = false; w.Gear = GearPhase.Down; w.Ignition = false; w.CanEject = false;
            return w;
        }

        /// <summary>A missile of yours is in flight and the camera follows it.</summary>
        public static MenuWorld Missile()
        {
            MenuWorld w = Default();
            w.MissileViewActive = true; w.MissileCurrent = "MSL 1/2 AAM-4";
            return w;
        }

        /// <summary>UH-90 IBIS on the rope: door guns, auto-hover instead of assist, squad exfil offered.</summary>
        public static MenuWorld Ibis()
        {
            MenuWorld w = Default();
            w.Aircraft = "UH-90 IBIS"; w.HasAssist = false; w.Hover = true; w.CanExfil = true; w.Flares = 24; w.FlaresMax = 24;
            w.Stations = new List<WorldStation> { S("DOOR GUN L", 500, StationClass.Gun), S("DOOR GUN R", 500, StationClass.Gun) };
            w.ActiveStation = 0; w.HasTurrets = false; w.HasMultipleGuns = true;
            return w;
        }

        public static IEnumerable<KeyValuePair<string, MenuWorld>> All()
        {
            yield return new KeyValuePair<string, MenuWorld>("default", Default());
            yield return new KeyValuePair<string, MenuWorld>("nowing", NoWing());
            yield return new KeyValuePair<string, MenuWorld>("ground", Ground());
            yield return new KeyValuePair<string, MenuWorld>("missile", Missile());
            yield return new KeyValuePair<string, MenuWorld>("ibis", Ibis());
        }
    }
}
