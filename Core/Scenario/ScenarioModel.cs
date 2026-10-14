using System;
using System.Collections.Generic;

namespace BoscaliSummer.Core.Scenario
{
    /// <summary>Which side a statement is for. <see cref="Any"/>: both, or the side does not matter.</summary>
    internal enum ScenarioSide : byte { Any, Bdf, Pala, Pmc }

    /// <summary>The faction name a side has in mission files. PMC is the third side added by the Pmc module, hostile to both others.</summary>
    internal static class SideNames
    {
        public const string Bdf = "Boscali", Pala = "Primeva", Pmc = "PMC";

        public static string Of(ScenarioSide side) =>
            side == ScenarioSide.Bdf ? Bdf : side == ScenarioSide.Pala ? Pala : side == ScenarioSide.Pmc ? Pmc : "";
    }

    /// <summary>Somebody who speaks in the script: a callsign on one of the radio nets.</summary>
    internal sealed class ScenarioCast
    {
        public string Id, Net = "command", Callsign, Role;
    }

    /// <summary>A unit the script adds to the mission as a copy of one the base mission has; it appears when an objective's <c>then spawn</c> names it.</summary>
    internal sealed class ScenarioUnit
    {
        public string Name, Like;
        /// <summary>For a unit made from nothing: aircraft, ship or vehicle, its type, an aircraft's loadout keys and a ship's or vehicle's destination.</summary>
        public string Kind, Type;
        public string[] Loadout = Array.Empty<string>();
        public bool Sails;
        public float SailX, SailZ;
        public ScenarioSide Side;
        public float X, Y, Z, Heading;
        /// <summary>Metres a second for an aircraft that appears in the air; negative leaves the copied value.</summary>
        public float Speed = -1f;
        /// <summary>Other number fields of the copied unit to change (skill, bravery, fuel).</summary>
        public readonly List<KeyValuePair<string, float>> Sets = new List<KeyValuePair<string, float>>();
        public int Line;
    }

    /// <summary>A group of units the script adds as copies of the ones a spawn outcome of the base mission brings in; they are there from the start.</summary>
    internal sealed class ScenarioCopy
    {
        public string Outcome, Prefix;
        /// <summary>Unit types left out of the copy.</summary>
        public string[] Without = Array.Empty<string>();
        public int Line;
    }

    /// <summary>A picture a cutscene can pin up: one of the mod's event paintings, or an image file next to the script.</summary>
    internal sealed class ScenarioPicture
    {
        public string Id, Poster, File;
    }

    internal enum ObjectiveKind : byte { Existing, Wait, Reach, Destroy, Capture, After, Sortie }

    internal enum ThenKind : byte { Start, Stop, Finish, Spawn, Message, Victory }

    /// <summary>What the game does when an objective completes.</summary>
    internal sealed class ScenarioThen
    {
        public ThenKind Kind;
        public string[] Names = Array.Empty<string>();
        public string Text;
        /// <summary>A message only the objective's own side reads.</summary>
        public bool SideOnly;
        /// <summary>Seconds before the end screen, for a victory.</summary>
        public float Seconds = 10f;
        public int Line;
    }

    /// <summary>One of the game's own objectives: made by the script, or (<see cref="ObjectiveKind.Existing"/>) one of the base mission's that the script hangs more on.</summary>
    internal sealed class ScenarioObjective
    {
        public string Id, Display;
        public ScenarioSide Side;
        public ObjectiveKind Kind;
        public float Seconds, X, Y, Z, Range = 2500f, Score = 1f;
        /// <summary>any, all, order, or some with <see cref="Percent"/>.</summary>
        public string Need = "all";
        public float Percent = 0.5f;
        public string[] Targets = Array.Empty<string>();
        public readonly List<ScenarioThen> Then = new List<ScenarioThen>();
        public int Line;
    }

    internal enum ShotKind : byte { OrbitAirbase, OrbitUnit, OrbitPoint, Fly, Chase }

    /// <summary>One camera move of a cutscene.</summary>
    internal sealed class ScenarioShot
    {
        public string Id, Target;
        public ShotKind Kind;
        public float X, Y, Z, ToX, ToY, ToZ, LookX, LookY, LookZ;
        public float Radius = 1500f, Height = 400f, From, To = 60f, Seconds = 8f, Fov = 45f;
        /// <summary>Handheld shake 0..1 and a held roll in degrees.</summary>
        public float Shake, Roll;
        public int Line;
    }

    /// <summary>A cut's screen effects (<c>fx=strike,lock</c>), layered over the surveillance feed for that one shot.</summary>
    [Flags]
    internal enum CutFx : ushort
    {
        None = 0,
        /// <summary>The feed keeps breaking up: interference bursts and torn words all through the shot.</summary>
        Glitch = 1,
        /// <summary>A blast as the shot opens: a white-hot flash and a long burst of interference.</summary>
        Strike = 2,
        /// <summary>A tracking box closes on the middle of the picture and locks.</summary>
        Lock = 4,
        /// <summary>The feed drops to static for the shot's last second.</summary>
        Lost = 8,
        /// <summary>The frame pulses red under a flash-traffic banner.</summary>
        Alert = 16,
        /// <summary>Colour grades: sunset gold, steel blue, night-vision green.</summary>
        Warm = 32, Cold = 64, Night = 128,
    }

    internal enum TriggerKind : byte { Start, Join, Spawn, Airborne, Landed, Time, Started, Done, Flag }

    internal enum ActionKind : byte { Cut, Card, Say, Hint, Wait, Flag, Unflag, Remember, Finish, Dip, Sky, Clock, Music, Enlist, Choose, Seat }

    internal struct ScenarioAction
    {
        public ActionKind Kind;
        /// <summary>The shot, the speaker, the flag or the card's title.</summary>
        public string Name;
        public string Text;
        /// <summary>A cut's large title, when it has one; <see cref="Text"/> is then the line under it.</summary>
        public string Title;
        /// <summary>A cut's picture and its place-and-date stamp, when it has them.</summary>
        public string Photo, Stamp;
        public CutFx Fx;
        /// <summary>A cut's board (<c>board=news</c>) and that board's own words (<c>headline=</c>, <c>ticker=</c>...); null for the surveillance feed.</summary>
        public string Board;
        public Dictionary<string, string> Words;
        /// <summary>A cut's narration: an .mp3 or .ogg spoken as the shot starts.</summary>
        public string Voice;
        public float Seconds;
        /// <summary>Where a <c>seat</c> puts the pilot: a point, a compass heading and a speed in metres a second.</summary>
        public float X, Y, Z, Heading, Speed;
    }

    /// <summary>
    /// One shot of a cutscene with its words: a caption, or a large <see cref="Title"/> with the caption under it; a place-and-date <see cref="Stamp"/>
    /// typed in the corner; and a <see cref="Photo"/> pinned up like a polaroid, which then joins the timeline of the ones before it.
    /// </summary>
    internal struct ScenarioCut
    {
        public ScenarioShot Shot;
        public string Title, Caption, Stamp;
        public ScenarioPicture Photo;
        public CutFx Fx;
        public string Board;
        public Dictionary<string, string> Words;
        public string Voice;

        /// <summary>One of the board's words, or <paramref name="otherwise"/>.</summary>
        public string Word(string key, string otherwise = null) => Words != null && Words.TryGetValue(key, out string value) ? value : otherwise;
    }

    /// <summary>A piece of the story: when <see cref="Trigger"/> happens for a pilot it is meant for, its actions play in order.</summary>
    internal sealed class ScenarioBeat
    {
        public string Id, Arg, If, Unless;
        public TriggerKind Trigger;
        public float Seconds;
        public ScenarioSide Side;
        public bool Repeat;
        public readonly List<ScenarioAction> Actions = new List<ScenarioAction>();
        public int Line;
    }

    /// <summary>A mission script (docs/SCENARIOS.md), read by <see cref="ScenarioParser"/>.</summary>
    internal sealed class ScenarioDef
    {
        public const string MarkerOpen = "[Boscali Summer scenario: ";

        public string Source, Id, Title, Base, Group = "Scenarios", Campaign, About = "";
        public int Chapter;
        /// <summary>Hour of the day and weather 0..1 the mission starts with; negative leaves the base mission's.</summary>
        public float Time = -1f, Weather = -1f;
        /// <summary>Seconds between the mission being decided and the game's end screen, so the last words can be said; negative leaves the base mission's.</summary>
        public float Ending = -1f;
        /// <summary>False keeps the game's own music silent for the whole mission (<c>gamemusic off</c>); the script's tracks still play.</summary>
        public bool GameMusic = true;
        public readonly List<string> Without = new List<string>(), Hold = new List<string>(), Begin = new List<string>();
        /// <summary>Sides nobody may join in this mission.</summary>
        public readonly List<ScenarioSide> Locked = new List<ScenarioSide>();
        /// <summary>The side a PMC mission works for. Parsed only: v1 keeps PMC hostile to everybody.</summary>
        public ScenarioSide Contract;

        /// <summary>True when any statement puts somebody on the PMC side; the mission then needs a PMC faction.</summary>
        public bool UsesPmc
        {
            get
            {
                if (Locked.Contains(ScenarioSide.Pmc)) return true;
                foreach (ScenarioUnit unit in Units) if (unit.Side == ScenarioSide.Pmc) return true;
                foreach (ScenarioObjective objective in Objectives) if (objective.Side == ScenarioSide.Pmc) return true;
                foreach (ScenarioBeat beat in Beats)
                    foreach (ScenarioAction action in beat.Actions)
                        if (action.Kind == ActionKind.Enlist && action.Name == "pmc") return true;
                return false;
            }
        }
        public readonly Dictionary<string, ScenarioCast> Cast = new Dictionary<string, ScenarioCast>(StringComparer.Ordinal);
        public readonly Dictionary<string, ScenarioShot> Shots = new Dictionary<string, ScenarioShot>(StringComparer.Ordinal);
        public readonly Dictionary<string, ScenarioPicture> Pictures = new Dictionary<string, ScenarioPicture>(StringComparer.Ordinal);
        public readonly List<ScenarioUnit> Units = new List<ScenarioUnit>();
        public readonly List<ScenarioCopy> Copies = new List<ScenarioCopy>();
        public readonly List<ScenarioObjective> Objectives = new List<ScenarioObjective>();
        public readonly List<ScenarioBeat> Beats = new List<ScenarioBeat>();
        public readonly List<string> Errors = new List<string>();

        /// <summary>The key campaign flags are kept under: the campaign, or the script alone.</summary>
        public string Saga => string.IsNullOrEmpty(Campaign) ? Id : Campaign;

        public void Error(int line, string text) => Errors.Add((Source ?? "script") + ":" + line + ": " + text);

        /// <summary>The line at the end of a scripted mission's description that names its script; it travels with the mission to every multiplayer client.</summary>
        public static string Marker(string id) => MarkerOpen + id + "]";

        /// <summary>The script a mission description names, or null for a mission without one.</summary>
        public static string IdOf(string description)
        {
            if (string.IsNullOrEmpty(description) || description[description.Length - 1] != ']') return null;
            int open = description.LastIndexOf(MarkerOpen, StringComparison.Ordinal);
            if (open < 0) return null;
            string id = description.Substring(open + MarkerOpen.Length, description.Length - open - MarkerOpen.Length - 1);
            return IsId(id) ? id : null;
        }

        /// <summary>Lower-case letters, digits, '-', '_' and '.', starting with a letter or digit.</summary>
        public static bool IsId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool word = c >= 'a' && c <= 'z' || c >= '0' && c <= '9';
                if (!word && (i == 0 || c != '-' && c != '_' && c != '.')) return false;
            }
            return true;
        }
    }
}
