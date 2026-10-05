using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Configuration
{
    internal enum RadioLevel { Off, Essential, Full }

    internal enum RadioVoice { Off, FollowGame, On }

    /// <summary>What the map and HUD mark (spec WMC program §5).</summary>
    internal enum HighlightMode { Off, Wing, WingAndTargets }

    /// <summary>Wing Command 1.0 settings, owned by Boscali Summer's shared config file.
    /// The first launch after the merge imports a standalone 1.0 file's values once
    /// (0.9 values never migrate, per the 1.0 rule) and archives it beside itself.
    /// Data files (formations, profiles, roster) live under <see cref="DataRoot"/>.
    /// The Avionics keys are gone on purpose: Command owns that section and its
    /// bridge applies it; a second bind would share one entry and fight it.</summary>
    internal sealed class WingConfig
    {
        internal const int SchemaVersion = 1;
        private const string SchemaMarker = "SchemaVersion = 1";
        private const string LegacyConfigName = "com.marci.wingcommand.cfg";

        internal static string DataRoot => Path.Combine(Paths.ConfigPath, "WingCommand", "v1");

        private static bool? simRun;

        /// <summary>A nomodkit sim run (<c>NOMODKIT_SIM_SCENARIO</c> set in the game's environment). Audit 2026-09-28: every sim
        /// run's mission-end tally wrote the player's pilots.user.json, and its debriefs, plans, routes, telemetry and loadout
        /// templates landed in the player's files too. A sim run still reads the player's airframes and tuning (so it flies as the
        /// player's game does) but keeps every record it writes under <see cref="RecordsRoot"/>.</summary>
        internal static bool SimRun => simRun ?? (simRun = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"))).Value;

        /// <summary>Where the player's records live: <see cref="DataRoot"/>, or its <c>sim</c> folder in a sim run.</summary>
        internal static string RecordsRoot => SimRun ? Path.Combine(DataRoot, "sim") : DataRoot;

        public ConfigEntry<WingMode> Mode { get; }
        public ConfigEntry<string> DefaultFormation { get; }
        public ConfigEntry<SpacingPreset> DefaultSpacing { get; }
        public ConfigEntry<int> MaxWingmen { get; }
        public ConfigEntry<string> CallAirframe { get; }
        public ConfigEntry<bool> ShowHud { get; }
        public ConfigEntry<bool> ShowWmc { get; }
        public ConfigEntry<bool> ReduceMotion { get; }
        public ConfigEntry<HighlightMode> MapMarkers { get; }
        public ConfigEntry<float> HudX { get; }
        public ConfigEntry<float> HudY { get; }
        public ConfigEntry<KeyboardShortcut> KeyCallWingman { get; }
        public ConfigEntry<KeyboardShortcut> KeyFormUp { get; }
        public ConfigEntry<KeyboardShortcut> KeyNextShape { get; }
        public ConfigEntry<KeyboardShortcut> KeyNextSpacing { get; }
        public ConfigEntry<KeyboardShortcut> KeyDismiss { get; }
        public ConfigEntry<KeyboardShortcut> KeyWmc { get; }
        public ConfigEntry<KeyboardShortcut> KeyApLevel { get; }
        public ConfigEntry<KeyboardShortcut> KeyApHeading { get; }
        public ConfigEntry<KeyboardShortcut> KeyApAltitude { get; }
        public ConfigEntry<KeyboardShortcut> KeyApVerticalSpeed { get; }
        public ConfigEntry<KeyboardShortcut> KeyApSpeed { get; }
        public ConfigEntry<KeyboardShortcut> KeyApOff { get; }
        /// <summary>The wing key (spec 2026-10-04 §2): hold it for the Call Ladder; with 1-6 a stance, with a command-card letter an order.</summary>
        public ConfigEntry<KeyboardShortcut> KeyWing { get; }
        /// <summary>The wing key on a joystick button (device:button, as the Hotas section); empty is unbound.</summary>
        public ConfigEntry<string> HotasWingKey { get; }
        /// <summary>Spec M7 §4: joystick bindings by command name, and the button logger.</summary>
        public System.Collections.Generic.KeyValuePair<string, ConfigEntry<string>>[] Hotas { get; }
        public ConfigEntry<bool> HotasLogButtons { get; }

        /// <summary>The commands a joystick button can run (Hotas section order).</summary>
        internal static readonly string[] HotasCommands =
        {
            "CallWingman", "FormUp", "NextShape", "NextSpacing", "Dismiss", "Engage", "Disengage", "AttackTarget", "Splash",
            "ClearMySix", "BogeyDope", "Rtb", "OrbitHere", "GoHigh", "GoLow", "Level", "ApOff",
        };
        public ConfigEntry<bool> PilotProgression { get; }
        public ConfigEntry<float> RankEffect { get; }
        public ConfigEntry<bool> SandboxFreeCalls { get; }
        public ConfigEntry<OverLimitMode> OverLimit { get; }
        public ConfigEntry<bool> TakeoverOnDeath { get; }
        public ConfigEntry<float> RecruitmentCostRate { get; }
        public ConfigEntry<string> LoadoutTemplates { get; }
        public ConfigEntry<string> LoadoutLiveries { get; }
        public ConfigEntry<WinchesterAction> AfterWinchester { get; }
        public ConfigEntry<BingoAction> AfterBingo { get; }
        public ConfigEntry<float> FallBackRatio { get; }
        public ConfigEntry<string> Doctrine { get; }
        public ConfigEntry<RadioLevel> Radio { get; }
        public ConfigEntry<RadioVoice> RadioVoiceMode { get; }
        public ConfigEntry<bool> ContactCalls { get; }
        public ConfigEntry<string> VoicePacks { get; }
        public ConfigEntry<float> VoicePackVolume { get; }
        public ConfigEntry<bool> VerboseLogging { get; }
        public ConfigEntry<bool> DevTools { get; }
        public ConfigEntry<bool> Overlay { get; }
        public ConfigEntry<KeyboardShortcut> KeyDumpTelemetry { get; }
        public ConfigEntry<KeyboardShortcut> KeyStepTest { get; }

        public WingConfig(ConfigFile c) : this(c, false)
        {
            ImportLegacyOneOh(c, this);
        }

        private WingConfig(ConfigFile c, bool legacyKeys)
        {
            c.Bind("Meta", "SchemaVersion", SchemaVersion, new ConfigDescription(
                "Settings schema written by Wing Command. Do not edit.", null,
                new ConfigurationManagerAttributes { Browsable = false }));

            Mode = c.Bind("AI", "Mode", WingMode.Smart, new ConfigDescription(
                "Smart runs the full AI. Performance halves guidance rate and decision cadence for AI-led " +
                "wings (player-led formations always run at full rate). Applies at the next mission start.",
                null, new ConfigurationManagerAttributes { Order = 100 }));

            DefaultFormation = c.Bind("Wing", "DefaultFormation", "finger-four-right", new ConfigDescription(
                "Formation a new wing flies: an id from formations.json (for example finger-four-right, combat-spread).",
                null, new ConfigurationManagerAttributes { Order = 90 }));
            DefaultSpacing = c.Bind("Wing", "DefaultSpacing", SpacingPreset.Standard, new ConfigDescription(
                "Spacing a new wing flies: Close 40 m, Standard 80 m, Open 160 m, Spread 350 m (clamped to the shape).",
                null, new ConfigurationManagerAttributes { Order = 89 }));
            MaxWingmen = c.Bind("Wing", "MaxWingmen", 3, new ConfigDescription(
                "Most wingmen you can call (host only).", new AcceptableValueRange<int>(1, 3),
                new ConfigurationManagerAttributes { Order = 88 }));
            CallAirframe = c.Bind("Wing", "CallAirframe", "", new ConfigDescription(
                "Airframe to call, by unit name (for example FS-20). Empty calls your own type. Fixed-wing only in this build.",
                null, new ConfigurationManagerAttributes { Order = 87 }));

            PilotProgression = c.Bind("Squadron", "PilotProgression", true, new ConfigDescription(
                "Pilots earn XP, ranks and perks, and fly better with rank.", null, new ConfigurationManagerAttributes { Order = 85 }));
            RankEffect = c.Bind("Squadron", "RankEffect", 1f, new ConfigDescription(
                "How much rank changes how a pilot flies and fights (0 off, 1 normal, 2 double).",
                new AcceptableValueRange<float>(0f, 2f), new ConfigurationManagerAttributes { Order = 84 }));
            SandboxFreeCalls = c.Bind("Squadron", "SandboxFreeCalls", false, new ConfigDescription(
                "Calls cost nothing: no allocation is charged, no stock is checked, and the faction's stock never moves (a " +
                "hangar's draw is given back, and so is the game's restock when the wingman comes home).", null,
                new ConfigurationManagerAttributes { Order = 83 }));

            OverLimit = c.Bind("Supply", "OverLimit", OverLimitMode.Surcharge, new ConfigDescription(
                "A requisition over the faction's AI aircraft limit (the game's own; your wingmen count toward it): Surcharge costs " +
                "three times the airframe's value; MatchEnemy keeps the price but lets every enemy faction field one more AI " +
                "aircraft; RtbOne keeps the price and sends one of the faction's own AI (never a wingman) to land to make room.",
                null, new ConfigurationManagerAttributes { Order = 80 }));
            TakeoverOnDeath = c.Bind("Squadron", "TakeoverOnDeath", true, new ConfigDescription(
                "When you are shot down or eject, offer to fly on in one of your wingmen's aircraft (host or single player).",
                null, new ConfigurationManagerAttributes { Order = 82 }));
            RecruitmentCostRate = c.Bind("Squadron", "RecruitmentCostRate", 0.25f, new ConfigDescription(
                "Taking command of a faction aircraft already flying costs this share of its value, once per aircraft.",
                new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 81 }));
            AfterWinchester = c.Bind("Combat", "AfterWinchester", WinchesterAction.Rejoin, new ConfigDescription(
                "A wingman out of ammunition in a fight: Rejoin the formation, Rtb (land and return to the reserve), or Refit " +
                "(land, rearm and take off again).", null, new ConfigurationManagerAttributes { Order = 90 }));
            AfterBingo = c.Bind("Combat", "AfterBingo", BingoAction.Rtb, new ConfigDescription(
                "A wingman at bingo fuel: Rtb (land and return to the reserve) or Refit (land, refuel and take off again).",
                null, new ConfigurationManagerAttributes { Order = 89 }));
            FallBackRatio = c.Bind("Combat", "FallBackRatio", 2f, new ConfigDescription(
                "An engaged wing facing this many enemy aircraft per fighting wingman falls back into formation; Engage while " +
                "outnumbered asks you to press it again. 0 turns it off.",
                new AcceptableValueRange<float>(0f, 10f), new ConfigurationManagerAttributes { Order = 88 }));
            Doctrine = c.Bind("Combat", "Doctrine", "Reserve", new ConfigDescription(
                "What wingmen shoot at while holding formation: Reserve (hold fire), Escort (aircraft threatening you), Sweep " +
                "(targets of opportunity, long range), or a custom line guard,response,interval,spread,targets,reach. Cycle it " +
                "from the radial Combat page.", null, new ConfigurationManagerAttributes { Order = 87 }));
            Radio = c.Bind("Radio", "Level", RadioLevel.Full, new ConfigDescription(
                "Wingman radio calls: Off, Essential (emergencies, tactical and status calls) or Full (also chatter such as " +
                "touchdowns).", null, new ConfigurationManagerAttributes { Order = 80 }));
            RadioVoiceMode = c.Bind("Radio", "Voice", RadioVoice.FollowGame, new ConfigDescription(
                "Speak wingman calls with the game's text-to-speech: Off, On, or FollowGame (on when the game's chat " +
                "text-to-speech is on; its speed and volume are used either way).", null, new ConfigurationManagerAttributes { Order = 79 }));
            VoicePacks = c.Bind("Radio", "VoicePacks", "", new ConfigDescription(
                "Yappinator-format voice packs for wingman calls, comma-separated (wingman #2 uses the first, #3 the second, " +
                "round robin). Packs are folders under config/WingCommand/v1/voicepacks or Yappinator's plugins/WSOYappinator/audio. " +
                "Calls a pack has no clip for use the text-to-speech. Empty: no packs.", null, new ConfigurationManagerAttributes { Order = 77 }));
            VoicePackVolume = c.Bind("Radio", "VoicePackVolume", 0.8f, new ConfigDescription(
                "Voice pack volume.", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { Order = 76 }));
            ContactCalls = c.Bind("Radio", "ContactCalls", true, new ConfigDescription(
                "Wingmen call new enemy aircraft within 40 km with bearing, range, altitude and aspect from you. Scout Ahead " +
                "reports ground contacts either way.", null, new ConfigurationManagerAttributes { Order = 78 }));
            LoadoutTemplates = c.Bind("Loadout", "SavedTemplates", "", new ConfigDescription(
                "Saved per-pylon loadout templates (airframe|id|name|store keys; records separated by semicolons). " +
                "Clear it to delete every template.", null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 60 }));
            LoadoutLiveries = c.Bind("Loadout", "Liveries", "", new ConfigDescription(
                "The livery each airframe's wingmen wear (airframe|token; B = built in, A = app-data skin, W = workshop item). " +
                "Clear it for every faction's standard livery.", null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 59 }));

            ShowHud = c.Bind("Hud", "Show", true, new ConfigDescription(
                "Show the wing strip and autopilot annunciator.", null, new ConfigurationManagerAttributes { Order = 80 }));
            // Renamed from OffsetX/OffsetY: the shared file already has Hud/OffsetX as an int,
            // and a same-key re-bind with another type throws.
            HudX = c.Bind("Hud", legacyKeys ? "OffsetX" : "WingOffsetX", 0f, new ConfigDescription(
                "Move the wing strip right (+) or left (-), in HUD pixels.", new AcceptableValueRange<float>(-1500f, 1500f),
                new ConfigurationManagerAttributes { Order = 79 }));
            HudY = c.Bind("Hud", legacyKeys ? "OffsetY" : "WingOffsetY", 0f, new ConfigDescription(
                "Move the wing strip up (+) or down (-), in HUD pixels.", new AcceptableValueRange<float>(-1000f, 1000f),
                new ConfigurationManagerAttributes { Order = 78 }));
            ShowWmc = c.Bind("Wmc", "Show", true, new ConfigDescription(
                "Show the WMC panel on a map bezel button (maximized map).", null, new ConfigurationManagerAttributes { Order = 70 }));
            ReduceMotion = c.Bind("Wmc", "ReduceMotion", false, new ConfigDescription(
                "No blinking, punches or flashes on the WMC panel: every cue shows steady.", null,
                new ConfigurationManagerAttributes { Order = 68 }));
            MapMarkers = c.Bind("Wmc", "MapMarkers", HighlightMode.WingAndTargets, new ConfigDescription(
                "Mark wingmen (element colour and badge) and, with WingAndTargets, the wing's targets on map and HUD icons.",
                null, new ConfigurationManagerAttributes { Order = 69 }));

            KeyCallWingman = Key(c, "CallWingman", "Call one wingman (Wing/CallAirframe, or your type).", 50);
            KeyFormUp = Key(c, "FormUp", "Every wingman rejoins now.", 49);
            KeyNextShape = Key(c, "NextShape", "Next formation shape in the family.", 48);
            KeyNextSpacing = Key(c, "NextSpacing", "Next spacing preset (Close, Standard, Open, Spread).", 47);
            KeyDismiss = Key(c, "Dismiss", "Release every wingman to the game's AI.", 46);
            KeyWmc = Key(c, "Wmc", "Open the WMC on the map: maximizes the map and shows the WMC screen (planning is on the main map).", 45);
            KeyApLevel = Key(c, "AutopilotLevel", "Autopilot: wings level.", 45);
            KeyApHeading = Key(c, "AutopilotHeading", "Autopilot: hold the current heading.", 44);
            KeyApAltitude = Key(c, "AutopilotAltitude", "Autopilot: hold the current altitude.", 43);
            KeyApVerticalSpeed = Key(c, "AutopilotVerticalSpeed", "Autopilot: hold the current vertical speed.", 42);
            KeyApSpeed = Key(c, "AutopilotSpeed", "Autopilot: toggle speed hold at the current speed.", 41);
            KeyApOff = Key(c, "AutopilotOff", "Autopilot: all holds off.", 40);
            KeyWing = Key(c, "WingKey", "Hold for the Call Ladder on the HUD strip: then 1-6 picks a stance, a command-card letter (Q W E R / A S D F / ...) gives " +
                "that order to the wing, Enter opens the ladder and its digits walk WHO, DO, WHERE (0 is back). Keys are read only while this is held.", 51);
            HotasWingKey = c.Bind("Hotas", "WingKey", "", new ConfigDescription(
                "Joystick button for the wing key: device:button, e.g. \"T.16000M:6\" or \"any:6\". Empty: unbound. The keyboard digits and letters " +
                "still read the chords while it is held.", null, new ConfigurationManagerAttributes { Order = 32 }));

            Hotas = new System.Collections.Generic.KeyValuePair<string, ConfigEntry<string>>[HotasCommands.Length];
            for (int i = 0; i < HotasCommands.Length; i++)
                Hotas[i] = new System.Collections.Generic.KeyValuePair<string, ConfigEntry<string>>(HotasCommands[i],
                    c.Bind("Hotas", HotasCommands[i], "", new ConfigDescription(
                        "Joystick button for " + HotasCommands[i] + ": <device>:<button>, e.g. \"T.16000M:5\" or \"any:5\" " +
                        "(part of the joystick's name, button counted from 1). Empty: unbound. Turn on LogButtons to find them.",
                        null, new ConfigurationManagerAttributes { Order = 30 - i })));
            HotasLogButtons = c.Bind("Hotas", "LogButtons", false, new ConfigDescription(
                "Write every joystick button press to the log as \"[Hotas] <joystick>: button <n>\" (to find names and numbers).",
                null, new ConfigurationManagerAttributes { Order = 31 }));

            DevTools = c.Bind("Debug", "DevTools", false, new ConfigDescription(
                "Enable developer tools: debug overlay, telemetry recorder, step tests and calibration.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 70 }));

            Overlay = c.Bind("Debug", "Overlay", true, new ConfigDescription(
                "With DevTools on, draw each wingman's slot (green), tracked reference (yellow), velocity command (cyan) " +
                "and collision bias (red).", null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 69 }));
            KeyDumpTelemetry = c.Bind("Debug", "DumpTelemetry", KeyboardShortcut.Empty, new ConfigDescription(
                "With DevTools on, write the last 120 s of wing telemetry to v1/telemetry.", null,
                new ConfigurationManagerAttributes { IsAdvanced = true, Order = 68 }));

            KeyStepTest = c.Bind("Debug", "StepTest", KeyboardShortcut.Empty, new ConfigDescription(
                "With DevTools on, fly wingman #2 through a 38 s step test (above 1500 m; it recovers between short stick pulses) and calibrate its airframe.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 67 }));

            c.Bind("Debug", "ExportLogs", false, new ConfigDescription(
                "Export the latest Wing Command log events from this session beside dll " +
                "(WingCommand-logs.txt). Safe diagnostics only; no upload.", null,
                new ConfigurationManagerAttributes
                {
                    DispName = "Export logs",
                    CustomDrawer = WingLogExport.DrawButton,
                    HideDefaultButton = true,
                    Order = 65,
                }));

            // Renamed from VerboseLogging: the shared file already has that key, and a
            // second bind would silently share one entry between two features.
            VerboseLogging = c.Bind("Debug", legacyKeys ? "VerboseLogging" : "WingVerboseLogging", false, new ConfigDescription(
                "Log decisions, state transitions and flight diagnostics. Applies immediately.",
                null, new ConfigurationManagerAttributes { DispName = "Debug action logging", Order = 60 }));

            Directory.CreateDirectory(DataRoot);
        }

        private static ConfigEntry<KeyboardShortcut> Key(ConfigFile c, string name, string what, int order) =>
            c.Bind("Keys", name, KeyboardShortcut.Empty, new ConfigDescription(what + " Unbound by default.", null,
                new ConfigurationManagerAttributes { Order = order }));

        /// <summary>First-launch migration from a standalone Wing Command 1.0 file beside
        /// ours: bind its values through the legacy key names, copy them over, and archive
        /// the legacy file so it is never read twice. Anything unexpected fails closed to
        /// the 1.0 defaults above; the shared file is never archived.</summary>
        private static void ImportLegacyOneOh(ConfigFile live, WingConfig target)
        {
            string directory;
            try { directory = Path.GetDirectoryName(live.ConfigFilePath); }
            catch { return; }
            if (string.IsNullOrEmpty(directory)) return;
            string legacyPath = Path.Combine(directory, LegacyConfigName);
            if (!File.Exists(legacyPath)) return;
            try
            {
                if (File.ReadAllText(legacyPath).IndexOf(SchemaMarker, StringComparison.Ordinal) < 0) return;
            }
            catch { return; }
            WingConfig legacy;
            try { legacy = new WingConfig(new ConfigFile(legacyPath, false), true); }
            catch { return; }
            CopyEntries(legacy, target);
            try
            {
                string archived = legacyPath + ".migrated.bak";
                if (File.Exists(archived)) File.Delete(archived);
                File.Move(legacyPath, archived);
            }
            catch
            {
                // The archive failed; the values are already ours, so a re-import next
                // launch copies the same values again.
            }
        }

        private static void CopyEntries(WingConfig from, WingConfig to)
        {
            foreach (PropertyInfo property in typeof(WingConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(ConfigEntryBase).IsAssignableFrom(property.PropertyType)) continue;
                try
                {
                    var source = (ConfigEntryBase)property.GetValue(from, null);
                    var entry = (ConfigEntryBase)property.GetValue(to, null);
                    if (source != null && entry != null && source.SettingType == entry.SettingType)
                        entry.BoxedValue = source.BoxedValue;
                }
                catch { /* one stale value never blocks the rest */ }
            }
            if (from.Hotas == null || to.Hotas == null) return;
            for (int i = 0; i < from.Hotas.Length && i < to.Hotas.Length; i++)
            {
                try
                {
                    if (from.Hotas[i].Value != null && to.Hotas[i].Value != null)
                        to.Hotas[i].Value.Value = from.Hotas[i].Value.Value;
                }
                catch { /* one stale value never blocks the rest */ }
            }
        }
    }
}
