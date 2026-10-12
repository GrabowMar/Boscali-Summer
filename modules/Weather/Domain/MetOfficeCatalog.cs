using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    internal enum MetControl : byte
    {
        Hold, PaceDown, PaceUp, StoryAuto, StoryPrev, StoryNext,
        PinAuto, PinClass, PinPrev, PinNext, PinSelected,
        Front2, Front5, Front10, Front30, Reroll,
        SetSquall, SetSupercell, SetDistantCb, SetStormEye, SetLenticulars, SetSeaFog,
        AnchorHere, AnchorAhead, AnchorDefault,
        RainAuto, RainDry, RainLight, RainHeavy,
        ScenHurricane, ScenSquall, ScenMountain, ScenSeaFog, ScenFrontal, ScenReset,
    }

    /// <summary>What the window knows about one control before it looks at the world: its effect class, when it arrives, its key and its help sentence (one line of the footer, 150 characters at most).</summary>
    internal readonly struct MetControlInfo
    {
        public readonly MetControl Id;
        public readonly string Name;
        public readonly MetEffect Effect;
        public readonly MetAvail Avail;
        public readonly char Key;
        public readonly string Help;

        public MetControlInfo(MetControl id, string name, MetEffect effect, MetAvail avail, char key, string help)
        { Id = id; Name = name; Effect = effect; Avail = avail; Key = key; Help = help; }
    }

    /// <summary>The fixed data of the window: controls, set-pieces and scenarios, in the order the mockup draws them.</summary>
    internal static class MetCatalog
    {
        public const int HelpLimit = 150;
        public const char BackspaceKey = '\b';

        public static readonly byte[] SetBits =
        {
            Superstructures.SquallLineSet, Superstructures.SupercellSet, Superstructures.DistantCellSet,
            Superstructures.StormEyeSet, Superstructures.LenticularSet, Superstructures.FogBankSet,
        };
        public static readonly string[] SetNames = { "SQUALL LINE", "SUPERCELL", "DISTANT CB", "STORM EYE", "LENTICULARS", "SEA FOG" };
        /// <summary>The chart's short label of each set-piece, for when the full name has no free spot near its marker. A storm eye and a fog bank have no cover to print.</summary>
        public static readonly string[] SetShort = { "SQL", "SC", "CB", "EYE", "LENT", "FOG" };
        /// <summary>The cell's second line while nothing is drawn: where the piece stands.</summary>
        public static readonly string[] SetWhere =
        {
            "35-70 KM · TAPE", "30-120 KM · TAPE", "30-120 KM · TAPE", "ANCHOR · R 9-13 KM", "ANCHOR · +30 KM", "LOW LAYER · 300 M",
        };
        public static readonly WeatherScenario[] Scenarios =
        {
            WeatherScenario.HurricaneEye, WeatherScenario.SquallAssault, WeatherScenario.MountainWave,
            WeatherScenario.SeaFog, WeatherScenario.FrontalPassage, WeatherScenario.ResetAll,
        };
        public static readonly string[] ScenarioNames = { "HURRICANE EYE", "SQUALL ASSAULT", "MOUNTAIN WAVE", "SEA FOG", "FRONTAL PASSAGE", "RESET ALL" };
        /// <summary>The recipe each scenario applies on this build (WeatherScenarios.Apply), in the two lines the button shows.</summary>
        public static readonly string[] ScenarioRecipe =
        {
            "STORM · EYE HERE", "RAIN · SQL+CELL", "FAIR · LENT +25 KM", "CLEAR · FOG BANK", "OVERCAST", "AUTO · NO SETS",
        };
        /// <summary>The part of a scenario that arrives with tape motion; null when the whole recipe is live.</summary>
        public static readonly string[] ScenarioLater = { null, "F5", null, null, "F5", null };

        // Sentences of the parts that are not controls (each at most HelpLimit characters; MetOfficeRulesTests checks them all).
        public const string TagHelp = "TAGS: LIVE works now. 4B arrives with the tape console. S5 arrives with tape motion. R1 to R10 name the readouts of the brief.";
        /// <summary>The title-bar chips: SMOOTH, FADE 20 S, CUT.</summary>
        public static readonly string[] ChipHelp =
        {
            "SMOOTH: hold and pace rebase the tape; its position never jumps. INTERIM: until the tape lands the legacy sky step restarts.",
            "FADE 20 S: one set-piece or the anchor changes over twenty seconds; the rest of the sky stays as it is.",
            "CUT (\u25B2): one atomic hard swap of the whole sky: pin, re-roll, scenarios. The word and the glyph say it as well as the colour.",
        };
        public const string IdentityHelp = "IDENTITY: seed mode and seed of this weather, the layout salt and the tape id. RE-ROLL changes the layout only. CUT.";
        public const string LogHelp = "HOST LOG: the last four host actions, newest first, with mission time. Clients see none; the host keeps it.";
        public const string DtgHelp = "DTG is the real UTC clock (cosmetic). T+ is the mission clock. SN is the first four digits of the tape id.";
        public const string RoleHostHelp = "HOST // ALL PLAYERS: your changes reach every player at once. This window is the only way to steer the weather by hand.";
        public const string RoleClientHelp = "READ ONLY: only the host steers the weather. You see the same window with every control off.";
        public const string BarPaceHelp = "PACE: one step faster; from the fastest it wraps to the slowest. , and . step down and up. SMOOTH. INTERIM: restarts the sky step.";
        public const string BarRainHelp = "RAIN: cycles AUTO > DRY > LIGHT (40%) > HEAVY (100%). Same as the W key. No sky cut.";

        /// <summary>Every sentence above, for the length test and the window's hover gate.</summary>
        public static string[] AllPartHelp()
        {
            var all = new System.Collections.Generic.List<string>(ChipHelp) { TagHelp, IdentityHelp, LogHelp, DtgHelp, RoleHostHelp, RoleClientHelp, BarPaceHelp, BarRainHelp };
            return all.ToArray();
        }

        public static readonly float?[] RainValues = { null, 0f, 0.4f, 1f };
        public static readonly string[] RainNames = { "AUTO", "DRY", "LIGHT", "HEAVY" };

        /// <summary>Class codes of the pin row, in <see cref="WeatherRegimeType"/> order.</summary>
        public static readonly string[] ClassCodes = { "CLR", "FEW", "SCT", "BKN", "OVC", "RA+", "TS" };

        private static readonly MetControlInfo[] Table = BuildTable();

        public static MetControlInfo Info(MetControl id) => Table[(int)id];

        /// <summary>The control <paramref name="offset"/> places after <paramref name="first"/> (set-piece, rain and scenario controls are contiguous).</summary>
        public static MetControl From(MetControl first, int offset) => (MetControl)((int)first + offset);

        public static int ScenarioIndex(MetControl id) => id - MetControl.ScenHurricane;
        public static int SetIndex(MetControl id) => id - MetControl.SetSquall;

        /// <summary>The word under a control's name and on the legend: SMOOTH, FADE 20 S, ▲ CUT, or empty.</summary>
        public static string EffectWord(MetEffect effect)
        {
            switch (effect)
            {
                case MetEffect.Smooth: return "SMOOTH";
                case MetEffect.Fade: return "FADE 20 S";
                case MetEffect.Cut: return "▲ CUT";
                default: return "";
            }
        }

        public static string AvailTag(MetAvail avail) => avail == MetAvail.Live ? "LIVE" : avail == MetAvail.Tape4B ? "4B" : "S5";

        /// <summary>Key legend text for the command bar (the printable key, or BKSP).</summary>
        public static string KeyLabel(char key) => key == BackspaceKey ? "BKSP" : key == '\0' ? "" : key.ToString().ToUpperInvariant();

        private static MetControlInfo[] BuildTable()
        {
            var t = new MetControlInfo[Enum.GetValues(typeof(MetControl)).Length];
            void Add(MetControl id, string name, MetEffect fx, MetAvail avail, char key, string help) => t[(int)id] = new MetControlInfo(id, name, fx, avail, key, help);
            const MetAvail L = MetAvail.Live, B = MetAvail.Tape4B, F = MetAvail.Motion5;
            Add(MetControl.Hold, "HOLD TAPE", MetEffect.Smooth, L, 'h',
                "HOLD / RESUME freezes or releases the tape. Wind, rain and light follow the camera. SMOOTH. Same switch as SET SERVER. INTERIM: restarts sky step.");
            Add(MetControl.PaceDown, "PACE -", MetEffect.Smooth, L, ',',
                "PACE slower: v = clamp(speed x wind, 18, 45) m/s. SMOOTH. Shared with SET SERVER. INTERIM: restarts the sky step; sky follows pace with the tape.");
            Add(MetControl.PaceUp, "PACE +", MetEffect.Smooth, L, '.',
                "PACE faster: v = clamp(speed x wind, 18, 45) m/s. SMOOTH. Shared with SET SERVER. INTERIM: restarts the sky step; sky follows pace with the tape.");
            Add(MetControl.StoryAuto, "STORY AUTO", MetEffect.Cut, B, '\0',
                "STORY AUTO lets the mission class pick the story for the whole tape. CUT. Arrives with the tape console (4B).");
            Add(MetControl.StoryPrev, "STORY <", MetEffect.Cut, B, '\0',
                "STORY repeats the chosen story for the whole tape and ignores the mission class. CUT. Arrives with the tape console (4B).");
            Add(MetControl.StoryNext, "STORY >", MetEffect.Cut, B, 's',
                "STORY repeats the chosen story for the whole tape and ignores the mission class. CUT. Arrives with the tape console (4B).");
            Add(MetControl.PinAuto, "AUTO", MetEffect.Cut, L, '\0',
                "AUTO unpins the sky and resumes the changing weather (a held tape runs again) from the sky on screen. Rain override stays. CUT.");
            Add(MetControl.PinClass, "PIN CLASS", MetEffect.Cut, L, '\0',
                "A class pins its first sky; the arrows then walk that class in number order, one sky per press. CUT: the whole tape becomes that sky.");
            Add(MetControl.PinPrev, "SKY <", MetEffect.Cut, L, '\0', "Previous sky of the class: pins it at once. CUT.");
            Add(MetControl.PinNext, "SKY >", MetEffect.Cut, L, '\0', "Next sky of the class: pins it at once. CUT.");
            Add(MetControl.PinSelected, "PIN SKY", MetEffect.Cut, L, 'p',
                "PIN the sky shown in the stepper: the whole tape becomes this one sky, no story, no fronts. AUTO hands it back. CUT.");
            Add(MetControl.Front2, "FRONT IN 2", MetEffect.Cut, F, '\0', "FRONT IN 2 MIN. Disabled: arrives with tape motion (S5).");
            Add(MetControl.Front5, "FRONT IN 5", MetEffect.Cut, F, 'f',
                "FRONT IN 5 MIN: the next front reaches you then. Disabled until tape motion (S5); also needs a running tape, no pin and a front ahead.");
            Add(MetControl.Front10, "FRONT IN 10", MetEffect.Cut, F, '\0', "FRONT IN 10 MIN. Disabled: arrives with tape motion (S5).");
            Add(MetControl.Front30, "FRONT IN 30", MetEffect.Cut, F, '\0', "FRONT IN 30 MIN. Disabled: arrives with tape motion (S5).");
            Add(MetControl.Reroll, "RE-ROLL", MetEffect.Cut, L, 'r',
                "RE-ROLL: new layout salt, new cloud, cell and set-piece sites and sky variants. Class and pin stay. CUT: one hard swap.");
            string[] setHelp =
            {
                "SQUALL LINE: a storm wall on the upwind horizon with a shelf cloud on its leading edge. Click again to switch it off. FADE 20 S.",
                "SUPERCELL: a leaning cumulonimbus with a downwind anvil and mammatus, 30-120 km upwind. Click again to switch it off. FADE 20 S.",
                "DISTANT CB: a lone cumulonimbus far out on the horizon. Click again to switch it off. FADE 20 S.",
                "STORM EYE: a hurricane eyewall, clear upper eye, spiral bands. Placed by ANCHOR. Click again to remove it. FADE 20 S.",
                "LENTICULARS: stacked lens clouds in a mountain-wave train riding the wind. Placed by ANCHOR. Click again to remove them. FADE 20 S.",
                "SEA FOG: a fog bank over the sea and valleys up to about 300 m. A low layer, not placed. Click again to clear it. FADE 20 S.",
            };
            for (int i = 0; i < SetNames.Length; i++)
                Add(From(MetControl.SetSquall, i), SetNames[i], MetEffect.Fade, L, '\0', setHelp[i]);
            Add(MetControl.AnchorHere, "ANCHOR HERE", MetEffect.Fade, L, '\0',
                "HERE stands the storm eye and lenticulars on the camera. Only those two move: squall, supercell and distant Cb stay on the tape. FADE 20 S.");
            Add(MetControl.AnchorAhead, "30 KM AHEAD", MetEffect.Fade, L, '\0',
                "30 KM AHEAD stands the storm eye and lenticulars 30 km in front of the camera, along its level heading. FADE 20 S.");
            Add(MetControl.AnchorDefault, "DEFAULT", MetEffect.Fade, L, '\0',
                "DEFAULT puts the storm eye and lenticulars back at their default spots in the layout. FADE 20 S.");
            string[] rainHelp =
            {
                "AUTO: the sky decides the rain, as when nobody is steering. No sky cut.",
                "DRY: no rain whatever the sky says. The cloud stays. No sky cut.",
                "LIGHT: force a light rain (40%) under any sky. No sky cut.",
                "HEAVY: force the heaviest rain (100%) under any sky. No sky cut.",
            };
            for (int i = 0; i < RainNames.Length; i++)
                Add(From(MetControl.RainAuto, i), "RAIN " + RainNames[i], MetEffect.None, L, i == 0 ? 'w' : '\0', rainHelp[i]);
            string[] scenHelp =
            {
                "HURRICANE EYE: a storm sky with the eye on you, walls all around, clear sky above. Overwrites class, pin, sets, anchor and rain. CUT.",
                "SQUALL ASSAULT: rain squall with a squall line and a supercell on the horizon. Overwrites class, pin, sets, anchor, rain. Front-in-5 arrives S5.",
                "MOUNTAIN WAVE: fair sky with lenticular clouds 25 km ahead of you. Overwrites class, pin, sets, anchor and rain. CUT.",
                "SEA FOG: clear sky over a fog bank in the sea and valleys. Overwrites class, pin, sets, anchor and rain. CUT.",
                "FRONTAL PASSAGE: overcast with a front across the map. Overwrites class, pin, sets, anchor and rain. Front-in-5 arrives S5.",
                "RESET ALL: unpin, story auto, sets off, anchor off, rain auto, tape moving. CUT. Always one click, always here.",
            };
            for (int i = 0; i < Scenarios.Length; i++)
                Add(From(MetControl.ScenHurricane, i), ScenarioNames[i], MetEffect.Cut, L, i == 5 ? BackspaceKey : '\0', scenHelp[i]);
            return t;
        }
    }
}
