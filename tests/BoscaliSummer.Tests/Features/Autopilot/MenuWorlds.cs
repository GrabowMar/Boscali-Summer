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
                HasCountermeasures = true, HasFlares = true, Flares = 48, FlaresMax = 48, Countermeasure = "IR FLARES", NextCountermeasure = "RADAR JAMMER", HasRadar = true, RadarOn = true,
                HasDrones = true, DronesStriking = false, DronesCanStrike = true, Drones = 2,
                CameraFollowing = true, Camera = ViewKind.Cockpit, HasCockpitPoint = true, MapAvailable = true,
                MissileViewAvailable = true, CanEnterMissileView = true, Missiles = 2, MissileStatus = "2 MSL · CLOSEST 1.1 KM",
                MarkAvailable = true, HasMark = true, MarkAgeSeconds = 42f, CanCapture = true, CanCallAtMark = true, Armed = "SAT CAMERA",
                PerksAvailable = true, SkywellCarried = true, SkywellStock = "FUEL 5.9T · AMMO 1.20T", CanExfil = false,
                DeckAvailable = true, DeckPlaying = false, DeckTitle = "Salt Flats at Dawn", ReceiverAvailable = true, ReceiverOn = true,
                Station = "Boscali Republic Radio", CanStopAll = true,
                CallsAvailable = true, CanCall = true, Wingmen = 3,
            };
            w.Stations.AddRange(new[]
            {
                S("12.7MM GUN", 1000, StationClass.Gun), S("AAM-4 SKYWAY", 2, StationClass.Missile), S("AGM-48", 2, StationClass.Agm),
                S("CBU-12", 0, StationClass.Bomb), S("AAM-4 SKYWAY", 2, StationClass.Missile), S("MALD-X", 0, StationClass.Missile),
                S("23MM TURRET", 300, StationClass.Turret), S("SKYWELL KIT", 1, StationClass.Other),
            });
            string[] calls = { "ON MY WAY", "NEED SUPPORT", "ENGAGING", "SPIKE", "WINCHESTER", "BINGO FUEL", "RTB", "SPLASH ONE", "COPY", "NEGATIVE", "THANKS", "GG" };
            w.CallLabels.AddRange(calls);
            // eight perks (IOpsPerks.MaxPerks) grouped by front, one armed, one cooling down
            w.Perks.AddRange(new[]
            {
                new WorldPerk { Label = "RECON PASS", Note = "SPACE R1 · 4 ALLOC", Front = "SPACE" },
                new WorldPerk { Label = "SAT CAMERA", Note = "SPACE R2 · 6 ALLOC", Front = "SPACE", Armed = true },
                new WorldPerk { Label = "PRSM", Note = "SPACE R3 · 10 ALLOC", Front = "SPACE", Cooldown = 24 },
                new WorldPerk { Label = "ELINT SWEEP", Note = "CYBER R1 · 4 ALLOC", Front = "CYBER" },
                new WorldPerk { Label = "SAM NET DOWN", Note = "CYBER R4 · 16 ALLOC", Front = "CYBER" },
                new WorldPerk { Label = "JTAC LASE", Note = "SOF R1 · 4 ALLOC", Front = "SOF" },
                new WorldPerk { Label = "RECON TEAM", Note = "SOF R2 · 6 ALLOC", Front = "SOF" },
                new WorldPerk { Label = "FORTIFY", Note = "SOF R3 · 10 ALLOC", Front = "SOF" },
            });
            w.Filters.AddRange(new[]
            {
                new WorldFilter { Name = "Air threats", Active = true, Slot = 1 }, new WorldFilter { Name = "SAM sites", Slot = 2 }, new WorldFilter { Name = "SEAD package", Slot = 3 },
            });
            AddShapes(w);
            return w;
        }

        /// <summary>formations.json: 22 shapes in 4 families (the families in the order the prototype lists them), finger four strong right flying.</summary>
        private static void AddShapes(MenuWorld w)
        {
            string[][] shapes =
            {
                new[] { "CLASSIC", "ECHELON RIGHT" }, new[] { "CLASSIC", "ECHELON LEFT" }, new[] { "CLASSIC", "LINE ABREAST" }, new[] { "CLASSIC", "TRAIL" }, new[] { "CLASSIC", "VIC" },
                new[] { "CLASSIC", "FINGER FOUR (STRONG RIGHT)" }, new[] { "CLASSIC", "FINGER FOUR (STRONG LEFT)" }, new[] { "CLASSIC", "DIAMOND" }, new[] { "CLASSIC", "BOX" }, new[] { "CLASSIC", "LADDER" },
                new[] { "ESCORT", "HIGH COVER" }, new[] { "ESCORT", "LOW COVER" }, new[] { "ESCORT", "SWEEP AHEAD" }, new[] { "ESCORT", "CLOSE ESCORT" },
                new[] { "ROTARY", "STAGGERED TRAIL" }, new[] { "ROTARY", "ECHELON (STAGGERED)" }, new[] { "ROTARY", "HEAVY STREAM" },
                new[] { "TACTICAL", "COMBAT SPREAD" }, new[] { "TACTICAL", "FLUID FOUR" }, new[] { "TACTICAL", "WALL" }, new[] { "TACTICAL", "OFFSET BOX" }, new[] { "TACTICAL", "CARD" },
            };
            foreach (string[] s in shapes) w.Shapes.Add(new WorldShape { Family = s[0], Name = s[1], Active = s[1] == "FINGER FOUR (STRONG RIGHT)" });
            w.Shape = "FINGER FOUR (STRONG RIGHT)";
        }

        public static MenuWorld NoWing() { MenuWorld w = Default(); w.Wingmen = 0; w.Shapes.Clear(); w.Shape = ""; return w; }

        /// <summary>The second countermeasure (the jammer) is active.</summary>
        public static MenuWorld Jammer()
        {
            MenuWorld w = Default();
            w.Countermeasure = "RADAR JAMMER"; w.NextCountermeasure = "IR FLARES"; w.CountermeasureIndex = 1;
            return w;
        }

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
            yield return new KeyValuePair<string, MenuWorld>("jammer", Jammer());
        }
    }
}
