using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>One load aboard: a cargo, troop or glider station, numbered as the WEAPONS menu numbers it.</summary>
    internal struct CargoLoad
    {
        public CargoKind Kind;
        /// <summary>The load's short name, upper case (HAMILCAR, PARATROOPERS).</summary>
        public string Name;
        /// <summary>The station's number: 1 is the aircraft's first station (the WEAPONS menu's STA 1).</summary>
        public int Station;
        public int Ammo, Max;
        public bool Selected;
    }

    /// <summary>The jump light: RED holds the drop, AMBER releases without a drop zone (the load follows its designation), GREEN is clear to drop.</summary>
    internal enum CargoLight : byte { Red, Amber, Green }

    /// <summary>One line of the release checklist: passed, failed, or not this load's business.</summary>
    internal enum CargoCheck : byte { NotApplicable, Fail, Pass }

    /// <summary>The local aircraft's manifest and the focused load's way to its drop zone. No drop commands, no flight state.</summary>
    internal struct CargoScope
    {
        /// <summary>The focused load: the selected station when it carries cargo, else the first that still has some aboard.</summary>
        public CargoKind Kind;
        public string Name;
        public int Station, Ammo, Max, Troops, Cargo, Gliders;
        public bool Selected, Safe, Reloading, ZoneAvailable, UsesLandingZone, HasZone, HasReach;
        /// <summary>Flat metres to the zone and how far a release from here reaches (the HALO glide or the HAMILCAR glide).</summary>
        public float DistanceM, ReachM;
        /// <summary>With a zone: its bearing off the nose (degrees, + right, -180..180), the height above it, the ground speed and how fast the zone comes closer (negative: it falls behind).</summary>
        public float BearingDeg, HeightM, GroundMps, ClosingMps;
        /// <summary>Every load aboard, in station order (provider-owned, filled in place: read it during the call only).</summary>
        public CargoLoad[] Loads;
        public int LoadCount;
    }

    internal static class CargoManifestRules
    {
        /// <summary>The manifest lists this many loads at most.</summary>
        public const int MaxLoads = 4;
        /// <summary>A HALO stick exits in fireteams of four.</summary>
        public const int TeamSize = 4;

        /// <summary>A cargo station is fitted and loads are still aboard: a spent kit has nothing to show.</summary>
        public static bool Present(in CargoScope d) => d.Kind != CargoKind.None && d.Troops + d.Cargo + d.Gliders > 0;
        public static bool InRange(in CargoScope d) => d.HasZone && d.HasReach && d.DistanceM >= 0f && d.ReachM >= 0f && d.DistanceM <= d.ReachM;

        // Reach alone cannot authorize a drop: the selected station and native safety still apply.
        public static string Status(in CargoScope d)
        {
            if (d.Reloading) return "RELOADING";
            if (d.Ammo <= 0) return "LOAD EXPENDED";
            if (!d.Selected) return "SELECT LOAD";
            if (d.Safe) return "SAFETY ON";
            if (!d.UsesLandingZone) return "NATIVE RELEASE";
            if (!d.ZoneAvailable && !d.HasZone) return "NATIVE TARGET";
            if (!d.HasZone) return "SET DROP ZONE";
            if (!d.HasReach) return "ZONE ASSIGNED";
            return InRange(d) ? "IN DROP RANGE" : "OUT OF REACH";
        }

        /// <summary>The jump light, by the same chain as <see cref="Status"/>: red while anything holds the release (the load, its safety, a zone out of reach), amber with no zone set, green otherwise.</summary>
        public static CargoLight Light(in CargoScope d)
        {
            if (d.Reloading || d.Ammo <= 0 || !d.Selected || d.Safe) return CargoLight.Red;
            if (!d.UsesLandingZone || !d.ZoneAvailable && !d.HasZone) return CargoLight.Green;   // the game's own release and aim
            if (!d.HasZone) return CargoLight.Amber;
            if (!d.HasReach) return CargoLight.Green;
            return InRange(d) ? CargoLight.Green : CargoLight.Red;
        }

        /// <summary>What to do about the light, under it (the header already says the state): the same chain as <see cref="Status"/>, worded as the next action.</summary>
        public static string Hint(in CargoScope d)
        {
            if (d.Reloading) return "WAIT FOR THE RELOAD";
            if (d.Ammo <= 0) return "THIS STATION IS EMPTY";
            if (!d.Selected) return "SELECT IT IN WEAPONS";
            if (d.Safe) return "TURN THE SAFETY OFF";
            if (!d.UsesLandingZone) return "FIRE THE STATION TO DROP";
            if (!d.ZoneAvailable && !d.HasZone) return "IT AIMS AT ITS TARGET";
            if (!d.HasZone) return "DROPS FOLLOW THE TARGET";
            if (!d.HasReach) return "IT FLIES TO THE ZONE";
            return InRange(d) ? "IN THE WINDOW - DROP" : "FLY TOWARD THE DZ";
        }

        /// <summary>The light's word: GREEN LIGHT, RED LIGHT, AMBER LIGHT.</summary>
        public static string LightWord(CargoLight light) => light == CargoLight.Green ? "GREEN LIGHT" : light == CargoLight.Amber ? "AMBER LIGHT" : "RED LIGHT";

        public static CargoCheck LoadCheck(in CargoScope d) => d.Selected && d.Ammo > 0 && !d.Reloading ? CargoCheck.Pass : CargoCheck.Fail;
        public static CargoCheck SafetyCheck(in CargoScope d) => d.Safe ? CargoCheck.Fail : CargoCheck.Pass;
        /// <summary>A load that aims at a drop zone, with the zone owner present: is a zone set.</summary>
        public static CargoCheck ZoneCheck(in CargoScope d) => !d.UsesLandingZone || !d.ZoneAvailable && !d.HasZone ? CargoCheck.NotApplicable : d.HasZone ? CargoCheck.Pass : CargoCheck.Fail;
        /// <summary>A zone and a known reach: is the zone inside it.</summary>
        public static CargoCheck ReachCheck(in CargoScope d) => !d.HasZone || !d.HasReach ? CargoCheck.NotApplicable : InRange(d) ? CargoCheck.Pass : CargoCheck.Fail;

        /// <summary>Seconds until the zone comes into reach at the present closure: 0 inside it, infinity while it is not coming closer, NaN without a zone and a reach.</summary>
        public static float WindowSeconds(in CargoScope d)
        {
            if (!d.HasZone || !d.HasReach || float.IsNaN(d.DistanceM) || float.IsNaN(d.ReachM)) return float.NaN;
            if (d.DistanceM <= d.ReachM) return 0f;
            return d.ClosingMps > 1f ? (d.DistanceM - d.ReachM) / d.ClosingMps : float.PositiveInfinity;
        }

        private static readonly float[] PlotSteps = { 500f, 1000f, 2000f, 3000f, 5000f, 7500f, 10000f, 15000f, 20000f, 30000f, 50000f, 75000f, 100000f };

        /// <summary>
        /// The drop plot's rim in metres: the smallest step that holds the reach ring with room and the zone, but never more than four reaches out, so the ring stays legible; a zone beyond the rim rides it.
        /// Without a reach (a powered glider) the zone sets the scale.
        /// </summary>
        // ponytail: no hysteresis, so a zone sitting right on a step's edge can flip the scale; add a band if that is seen in game.
        public static float PlotRangeM(float distanceM, float reachM)
        {
            float d = float.IsNaN(distanceM) || distanceM < 0f ? 0f : distanceM, r = float.IsNaN(reachM) || reachM < 0f ? 0f : reachM;
            float need = r > 0f ? Math.Max(r * 1.3f, Math.Min(d * 1.1f, r * 4f)) : d * 1.1f;
            foreach (float step in PlotSteps) if (step >= need) return step;
            return PlotSteps[PlotSteps.Length - 1];
        }

        public static string KindWord(CargoKind kind) => kind == CargoKind.Paratroopers ? "HALO" : kind == CargoKind.Troops ? "TROOPS" : kind == CargoKind.Glider ? "GLIDER" : "CARGO";
    }
}
