#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Hud.Configuration;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Presentation;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Explicit nomodkit single-player test hook (tests/ingame/missile-hud.json); inert outside a
    /// sim run. Fires the seated player's own missiles through the game's own
    /// <c>WeaponStation.Fire</c> and reports the tracker and the follow view
    /// through the same seams the HUD and the menu use.
    /// </summary>
    public static class HudAutomation
    {
        private static bool Guard(out Dictionary<string, object> failure)
        {
            failure = null;
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                failure = Failure("HudAutomation", "guard", "requires a hosted single-player nomodkit scenario");
            return failure == null;
        }

        private static object[] pilotOptions;

        /// <summary>
        /// Set any of the TAC BOARD's options through the settings contract (<see cref="ITacBoardSettings"/>, the same door the SET console and the C menu use) so ONE sim run can try several looks, and return every option as it
        /// now stands. The names are the contract's, in any case (enabled, layout, scale, shade, declutter, scopeMode, showStores, pageMissileCam, showLamps, showGauges, showTicker, showSystems, sway, corners,
        /// centreAlerts, arPath, arDomes, arMissiles, arBeacons, hudFx, brow, arcs, boot, reactive, visorSound, textEdge); an enum is given by name (wide, stack, full, auto, off). A name that is
        /// none of them fails the call, so a typo cannot waste a run, and so does a value the option does not take (a retired one: Declutter is always Full, ScopeMode Manual reads as Auto on the board). IN MEMORY ONLY: the contract's setters save the config file on every set, so the file's save-on-set is held off around them and put back; the file on
        /// disk is never written by this hook and the pilot's saved options come back with the next start. <c>restore</c> true first puts every option back to what it was at the first call of the run (the pilot's own values).
        /// The board takes a change on its next 10 Hz tick: wait before a capture.
        /// </summary>
        public static Dictionary<string, object> Configure(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            if (!ModuleServices.TryGet(out ITacBoardSettings options) || options == null)
                return Failure("HudAutomation", "Configure", "no TAC BOARD settings service");
            PropertyInfo[] properties = typeof(ITacBoardSettings).GetProperties();
            if (args != null)
                foreach (string key in args.Keys)
                    if (key != "restore" && Array.FindIndex(properties, p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)) < 0)
                        return Failure("HudAutomation", "Configure", "no TAC BOARD option called '" + key + "'");
            if (pilotOptions == null)
            {
                pilotOptions = new object[properties.Length];
                for (int i = 0; i < properties.Length; i++) pilotOptions[i] = properties[i].GetValue(options);
            }
            ConfigFile file = (options as HudSettings)?.Enabled.ConfigFile;
            bool save = file != null && file.SaveOnConfigSet;
            if (file != null) file.SaveOnConfigSet = false;
            var result = new Dictionary<string, object> { { "ok", true }, { "saved", false } };
            try
            {
                for (int i = 0; i < properties.Length; i++)
                {
                    PropertyInfo option = properties[i];
                    if (Bool(args, "restore")) option.SetValue(options, pilotOptions[i]);
                    object given = args != null ? FindIgnoringCase(args, option.Name) : null;
                    if (given == null) continue;
                    object wanted = OptionValue(option.PropertyType, given);
                    option.SetValue(options, wanted);
                    // A retired value (Declutter Lean/Minimal, ArPath, ArDomes) or one out of range is not taken: fail rather than capture a look that never applied.
                    object now = option.GetValue(options);
                    if (!Equals(now, wanted))
                        return Failure("HudAutomation", "Configure", "option '" + option.Name + "' did not take " + given + " (reads " + now + "): retired or out of range");
                }
            }
            catch (Exception e) { return Failure("HudAutomation", "Configure", "could not set an option: " + e.Message); }
            finally { if (file != null) file.SaveOnConfigSet = save; }
            foreach (PropertyInfo option in properties)
            {
                object value = option.GetValue(options);
                result[char.ToLowerInvariant(option.Name[0]) + option.Name.Substring(1)] = value is Enum ? value.ToString() : value;
            }
            Debug.Log("[HudAutomation] Configure: " + Describe(result));
            return result;
        }

        private static object FindIgnoringCase(Dictionary<string, object> args, string name)
        {
            foreach (KeyValuePair<string, object> pair in args)
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            return null;
        }

        /// <summary>A scenario's JSON value as the option's own type: a bool, a number, or an enum by name (any case).</summary>
        private static object OptionValue(Type type, object given)
        {
            if (type.IsEnum) return given is string name ? Enum.Parse(type, name, true) : Enum.ToObject(type, Convert.ToInt32(given, CultureInfo.InvariantCulture));
            if (type == typeof(bool)) return Convert.ToBoolean(given, CultureInfo.InvariantCulture);
            return Convert.ToSingle(given, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Pushes DL EVT LOG's test lines through the game's own feed (<c>MessageUI.KillFeed</c> and <c>GameMessage</c>, both ending in <c>MessageFeed.Enqueue</c>, the one place the log's patch listens): a kill line with colour tags, one in
        /// which the pilot's own aircraft is the killer, a plain game message and a "Name: text" chat line. Returns how many it pushed and the pilot's names as the log reads them. Wait two seconds, then capture.
        /// </summary>
        public static Dictionary<string, object> Feed(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            MessageUI ui = SceneSingleton<MessageUI>.i;
            if (ui == null) return Failure("HudAutomation", "Feed", "no MessageUI");
            string own = BoscaliSummer.Modules.Hud.Patches.EventFeed.OwnAircraft();
            ui.KillFeed("<color=#58A8FFFF>Left Hook</color> shot down <color=#FF4D4DFF>Bravo Two</color>");
            ui.KillFeed("<color=#58A8FFFF>" + (own ?? "Anumeric") + "</color> shot down <color=#FF4D4DFF>T9K41 Boltstrike</color>");
            ui.GameMessage("Enemy airbase captured");
            ui.GameMessage("<color=#AABBCCFF>Pilot One:</color> Rolling in on the airfield now");
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "pushed", 4 },
                { "ownPlayer", BoscaliSummer.Modules.Hud.Patches.EventFeed.OwnPlayer() ?? "" }, { "ownAircraft", own ?? "" },
            };
            Debug.Log("[HudAutomation] Feed: " + Describe(result));
            return result;
        }

        /// <summary>
        /// Pushes the CUE stack's test messages through the game's own calls: <c>AircraftActionsReport.ReportText</c> for an action report ("Weapon safety lock Disabled", armed: AMBER) and for a damage failure the way
        /// <c>StatusDisplay</c> writes one (alert-coloured: RED), <c>AllocationDisplay.Show</c> for a funds line and <c>KillDisplay.DisplayBonus</c> for a score line. The texts can be given (<c>action</c>, <c>failure</c>) and so
        /// can the amounts (<c>funds</c>, <c>score</c>); <c>push</c> false pushes nothing and only reports, <c>only</c> (a list of action, failure, funds, score) pushes just those (a later step reads the stack again). Returns the cue ring newest first (text, class, severity, source, size, life, age),
        /// how many cues the stack has drawn and which game boxes are in its charge; <c>gear</c> true / false lowers or raises the seated player's gear, which the game answers with a real weapon safety report. The action report is captured whether the game's own box takes it or the radio module sends it to the ticker; the funds line shows only while
        /// the game's allocation display is visible, the score line while the game's kill display is up. Cues last the game's time (five seconds for the reports and the score line): capture 0.15 s, 1 s and 4.8 s later.
        /// </summary>
        public static Dictionary<string, object> Cue(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            AircraftActionsReport report = SceneSingleton<AircraftActionsReport>.i;
            if (report == null) return Failure("HudAutomation", "Cue", "no AircraftActionsReport");
            var pushed = new List<object>();
            if (!Has(args, "push") || Bool(args, "push"))
            {
                string armed = Text(args, "action") ?? "Weapon safety lock <b>Disabled</b>", broke = Text(args, "failure") ?? "Hydraulic failure", only = Text(args, "only");
                bool Wants(string name) => string.IsNullOrEmpty(only) || only.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
                if (Wants("action"))
                {
                    report.ReportText(armed, 5f);
                    pushed.Add("action");
                }
                if (Wants("failure"))
                {
                    report.ReportText(StringHelper.AddColor(broke, NuclearOption.UIStyleSystem.ThemeManager.Active.ColorTheme.Alert), 5f);
                    pushed.Add("failure");
                }
                AllocationDisplay allocation = SceneSingleton<AllocationDisplay>.i;
                if (Wants("funds") && allocation != null && GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player) && player != null)
                {
                    allocation.Show(player, Number(args, "funds", 803700f));
                    pushed.Add("funds");
                }
                KillDisplay score = SceneSingleton<KillDisplay>.i;
                if (Wants("score") && score != null)
                {
                    score.DisplayBonus(Number(args, "score", 3.5f));
                    pushed.Add("score");
                }
            }
            // gear: true / false puts the seated player's landing gear down or up through the game's own call: the game then raises its own weapon safety report (Enabled while the gear is out, Disabled once it is up again).
            if (Has(args, "gear") && GameManager.GetLocalAircraft(out Aircraft own) && own != null)
            {
                own.SetGear(Bool(args, "gear"));
                pushed.Add("gear");
            }
            float now = Time.timeSinceLevelLoad;
            var cues = new List<object>();
            for (int i = BoscaliSummer.Modules.Hud.Patches.CueFeed.Log.Count - 1; i >= 0; i--)
            {
                Cue c = BoscaliSummer.Modules.Hud.Patches.CueFeed.Log[i];
                cues.Add(new Dictionary<string, object>
                {
                    { "text", c.Text }, { "class", CueWords.Of(c.Class) }, { "severity", c.Severity.ToString() }, { "source", c.Source.ToString() },
                    { "size", c.Size }, { "life", c.Life }, { "age", now - c.Start }, { "delay", c.Start - c.Arrival },
                });
            }
            HudBoard board = HudBoard.Instance;
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "pushed", pushed }, { "count", cues.Count }, { "cues", cues },
                { "drawn", board != null ? board.CueDrawn : -1 }, { "charge", board != null ? board.CueInCharge : -1 },
            };
            Debug.Log("[HudAutomation] Cue: " + Describe(result));
            return result;
        }

        /// <summary>
        /// The visor's power-on (tests/ingame/hud-boot.json). <c>mode</c> ("full" or "resync") starts it again now on the HUD's aircraft; <c>hold</c> freezes its clock at that many seconds (a negative number lets it run on), so each phase can be
        /// captured still; <c>abort</c> true ends it at once. Reads back whether it runs, its age, the self-test lines, how far each zone has come alive, the arcs' lit segments and the rim's etched text.
        /// </summary>
        public static Dictionary<string, object> Boot(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            HudBoard board = HudBoard.Instance;
            if (board == null) return Failure("HudAutomation", "Boot", "no HUD board");
            bool started = false;
            string mode = Text(args, "mode");
            if (!string.IsNullOrEmpty(mode)) started = board.RestartBoot(string.Equals(mode, "resync", StringComparison.OrdinalIgnoreCase) ? BootMode.Resync : BootMode.Full);
            if (Has(args, "hold")) board.BootHud.Hold(Number(args, "hold", 0f), Time.unscaledTime);
            if (Bool(args, "abort")) board.BootHud.Abort();
            VisorBootHud boot = board.BootHud;
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "started", started }, { "active", boot.Active }, { "mode", boot.Mode.ToString() }, { "ended", board.BootEnd }, { "age", boot.Age }, { "lines", boot.LineCount }, { "boot_shown", boot.Shown },
                { "brow", boot.Progress(BootGroup.Brow) }, { "rim", boot.Progress(BootGroup.Rim) }, { "left", boot.Progress(BootGroup.Left) }, { "board", boot.Progress(BootGroup.Board) }, { "arcs", boot.Progress(BootGroup.Arcs) },
                { "arcs_shown", board.Arcs != null && board.Arcs.Shown }, { "natives_hidden", board.Arcs != null && board.Arcs.NativesHidden }, { "fuel_lit", board.Arcs?.FuelLit ?? -1 },
                { "rim_shown", board.Rim != null && board.Rim.Shown }, { "etched", board.Rim?.EtchedText ?? "" }, { "brow_shown", board.Tape != null && board.Tape.Shown },
            };
            Debug.Log("[HudAutomation] Boot: " + Describe(result));
            return result;
        }

        /// <summary>
        /// The reactive visor (tests/ingame/hud-react.json): pin each reaction so a still can be taken of a moment that lasts a third of a second. <c>g</c> a load factor to use instead of the real one (a value under -50 gives the real one back);
        /// <c>hit</c> "round" or "blast" sends a hit through the same counter Immersion's patch writes (<see cref="VisorEvents"/>), <c>glitch</c> (weight 0..1) starts a glitch directly and <c>glitchHold</c> freezes it at that many seconds (negative: runs on);
        /// <c>emp</c> "on" / "off" / "real" forces the EMP flag and <c>empHold</c> freezes its clock; <c>flash</c> "warning" or "bright" starts a beat and <c>flashHold</c> freezes it. Reads back what the visor is doing.
        /// </summary>
        public static Dictionary<string, object> Visor(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            HudBoard board = HudBoard.Instance;
            if (board == null) return Failure("HudAutomation", "Visor", "no HUD board");
            VisorFx fx = board.Fx;
            if (Has(args, "g")) { float g = Number(args, "g", 1f); fx.GForce = g < -50f ? float.NaN : g; }
            if (Has(args, "seek")) fx.Seek(Number(args, "seek", 0f));
            string hit = Text(args, "hit");
            if (!string.IsNullOrEmpty(hit)) VisorEvents.Hit(string.Equals(hit, "blast", StringComparison.OrdinalIgnoreCase) ? 0f : 1f, string.Equals(hit, "blast", StringComparison.OrdinalIgnoreCase) ? 1f : 0f);
            if (Has(args, "glitch")) fx.StartGlitch(Number(args, "glitch", 0.5f));
            if (Has(args, "glitchHold")) { float h = Number(args, "glitchHold", -1f); fx.HoldGlitch = h < 0f ? float.NaN : h; }
            string emp = Text(args, "emp");
            if (!string.IsNullOrEmpty(emp)) fx.EmpForce = string.Equals(emp, "on", StringComparison.OrdinalIgnoreCase) ? 1 : string.Equals(emp, "off", StringComparison.OrdinalIgnoreCase) ? 0 : -1;
            if (Has(args, "empHold")) { float h = Number(args, "empHold", -1f); fx.HoldEmp = h < 0f ? float.NaN : h; }
            string flash = Text(args, "flash");
            if (!string.IsNullOrEmpty(flash)) fx.StartFlash(string.Equals(flash, "bright", StringComparison.OrdinalIgnoreCase) ? FlashKind.Bright : FlashKind.Warning);
            if (Has(args, "flashHold")) { float h = Number(args, "flashHold", -1f); fx.HoldFlash = h < 0f ? float.NaN : h; }
            // BROKEN HELMET: pin the damage level (degrade 0..1, under 0 the real one) and hold a damage burst at that many seconds in (burstHold, negative: the scheduler runs); jam 0..1 pins the jam level (negative: the game's real one).
            if (Has(args, "degrade")) { float d = Number(args, "degrade", -1f); fx.DegradeForce = d < 0f ? float.NaN : d; }
            if (Has(args, "burstHold")) { float h = Number(args, "burstHold", -1f); fx.BurstHold = h < 0f ? float.NaN : h; }
            if (Has(args, "jam")) { float j = Number(args, "jam", -1f); fx.JamForce = j < 0f ? float.NaN : j; }
            if (Bool(args, "empOn")) fx.EmpForce = 1;
            string shader = Text(args, "shader");      // "off": the stock UI and text shaders everywhere (the healthy-look comparison), "on": the glitch ones again
            if (!string.IsNullOrEmpty(shader)) BoscaliSummer.Modules.Hud.Presentation.VisorGlitch.SetDisabled(string.Equals(shader, "off", StringComparison.OrdinalIgnoreCase));
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "alpha", fx.Alpha }, { "shift_x", fx.ShiftX }, { "shift_y", fx.ShiftY }, { "tear_board", fx.TearBoard }, { "tear_left", fx.TearLeft },
                { "flash", fx.Flash }, { "flash_tone", fx.FlashTone.ToString() }, { "g_load", fx.GLoad }, { "glitch", fx.GlitchRunning }, { "emp", fx.EmpRunning }, { "reacting", fx.Reacting },
                { "breath_mark", fx.BreathMark }, { "scan_at", fx.ScanAt }, { "hit_serial", VisorEvents.HitSerial }, { "booting", board.BootHud.Active },
                { "scan_shown", board.Tape != null && board.Tape.Shown }, { "live", fx.Live }, { "warn_up", fx.WarnLevel },
                { "shader_ready", BoscaliSummer.Modules.Hud.Presentation.VisorGlitch.Ready }, { "glitching", fx.Glitching }, { "degrade", fx.DegradeLevel }, { "burst", fx.BurstRunning }, { "jam", fx.JamLevel },
                { "split", fx.Split }, { "drop", fx.DropDensity }, { "flick", fx.Flick }, { "bars", fx.BarStrength }, { "scramble", fx.Scramble }, { "tear0", fx.Tear[2] }, { "tear1", fx.Tear[6] }, { "tear2", fx.Tear[10] },
                { "caption", "" },
            };
            Debug.Log("[HudAutomation] Visor: " + Describe(result));
            return result;
        }

        /// <summary>
        /// The visor's sound journal (tests/ingame/hud-boot.json, hud-react.json): every visor clip asked for with its time, variation, gain and what became of it (played, ducked, limited), as the facade logged it.
        /// <c>clear</c> true empties it first; <c>play</c> a clip name (Tick, Wipe, Glitch...) asks for it <c>times</c> (default 1) times in this very frame, which shows the rate limit and the duck.
        /// Returns the lines joined with " | ", the count, whether a real alarm or voice would duck now, and the level.
        /// </summary>
        public static Dictionary<string, object> Sound(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            if (Bool(args, "clear")) BoscaliSummer.Core.Fx.VisorSound.Journal.Clear();
            string play = Text(args, "play");
            if (!string.IsNullOrEmpty(play))
            {
                if (!Enum.TryParse(play, true, out BoscaliSummer.Core.Fx.VisorClip clip)) return Failure("HudAutomation", "Sound", "no visor clip called '" + play + "'");
                int times = (int)Number(args, "times", 1f);
                for (int i = 0; i < times; i++) BoscaliSummer.Core.Fx.VisorSound.Play(clip);
            }
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "count", BoscaliSummer.Core.Fx.VisorSound.Journal.Count }, { "busy", BoscaliSummer.Core.Fx.VisorSound.Busy() },
                { "banner", BoscaliSummer.Core.Fx.VisorSound.Banner }, { "level", BoscaliSummer.Core.Fx.VisorSound.Level }, { "journal", string.Join(" | ", BoscaliSummer.Core.Fx.VisorSound.Journal) },
            };
            Debug.Log("[HudAutomation] Sound: " + Describe(result));
            return result;
        }

        /// <summary>
        /// The segmented fuel and throttle arcs as they stand (tests/ingame/hud-arcs.json): whether they are up, whether the game's own two gauges are hidden behind them, the lit segments, the measured circle
        /// and the game's gauges' own alpha. <c>fuel</c> (0..1) first sets every tank to that share of its capacity and <c>throttle</c> (0..1) the player's throttle input, for a capture of a low tank or a full throttle.
        /// </summary>
        public static Dictionary<string, object> Arcs(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            CombatHUD combat = SceneSingleton<CombatHUD>.i;
            Aircraft aircraft = combat != null ? combat.aircraft : null;
            if (aircraft == null) return Failure("HudAutomation", "Arcs", "no HUD aircraft");
            if (Has(args, "fuel"))
            {
                float share = Mathf.Clamp01(Number(args, "fuel", 1f));
                foreach (FuelTank tank in aircraft.GetFuelTanks())
                {
                    float want = tank.GetCapacity() * share, have = tank.GetLevel();
                    if (have > want) tank.UseFuel(have - want); else tank.Refuel(want - have);
                }
            }
            if (Has(args, "throttle") && aircraft.GetInputs() != null) aircraft.GetInputs().throttle = Mathf.Clamp01(Number(args, "throttle", 0f));
            ArcsHud arcs = HudBoard.Instance != null ? HudBoard.Instance.Arcs : null;
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "fuel", aircraft.GetFuelLevel() }, { "throttle", aircraft.GetInputs()?.throttle ?? -1f },
                { "shown", arcs != null && arcs.Shown }, { "natives_hidden", arcs != null && arcs.NativesHidden }, { "fuel_lit", arcs?.FuelLit ?? -1 }, { "throttle_lit", arcs?.ThrottleLit ?? -1 },
                { "native_texts", arcs?.NativeTexts ?? "" }, { "radius", arcs?.FuelRadius ?? -1f }, { "axis", arcs?.FuelAxis ?? 0f }, { "throttle_word", arcs?.ThrottleWord ?? "" },
            };
            Debug.Log("[HudAutomation] Arcs: " + Describe(result));
            return result;
        }

        /// <summary>
        /// The centre combat layer as it stands (tests/ingame/ace-alerts.json): whether it is up, the banner's word and detail, how many marks the THREAT RING draws, the shot callout and
        /// whether the game's own missile list is hidden behind it. <c>callout</c> (hit, destroyed, missed, decoyed) first pushes that call, <c>sub</c> its line, for a capture of the look.
        /// </summary>
        public static Dictionary<string, object> Alerts(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            string call = Text(args, "callout");
            if (!string.IsNullOrEmpty(call))
            {
                if (!Enum.TryParse(call, true, out CalloutKind kind) || kind == CalloutKind.None) return Failure("HudAutomation", "Alerts", "no callout called '" + call + "'");
                BoscaliSummer.Modules.Hud.Patches.CueFeed.Callouts.Push(kind, Text(args, "sub"), Time.unscaledTime);
            }
            CombatAlerts alerts = HudBoard.Instance != null ? HudBoard.Instance.Alerts : null;
            GameManager.GetLocalAircraft(out Aircraft own);
            float climb = own != null && own.rb != null ? own.rb.velocity.y : 0f;
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "shown", alerts != null && alerts.Shown }, { "banner", alerts?.LineWord ?? "" }, { "detail", alerts?.DetailText ?? "" },
                { "terrain_alert", alerts != null && alerts.TerrainAlert },
                { "terrain_would_warn", own != null && AlertBoard.PullUp(HudCues.Terrain(own.radarAlt, climb), climb, own.gearState == LandingGear.GearState.LockedExtended) },
                { "mission_notice", HudBoard.Instance != null && HudBoard.Instance.MissionNoticeShown },
                { "ring", alerts?.RingMarks ?? 0 }, { "callout", alerts?.CalloutText ?? "" }, { "native_list_hidden", alerts != null && alerts.NativeListHidden },
                { "brow_shown", HudBoard.Instance?.Tape != null && HudBoard.Instance.Tape.Shown }, { "brow_carets", HudBoard.Instance?.Tape?.MarkCount ?? 0 }, { "brow_caption", "" }, { "tape", HudBoard.Instance?.Tape?.Describe() ?? "" }, { "call", alerts?.CallText ?? "" },
            };
            Debug.Log("[HudAutomation] Alerts: " + Describe(result));
            return result;
        }

        /// <summary>
        /// The AR helmet layer as it stands (tests/ingame/ar-layer.json): what it drew on its last frame. Showcase switches for a capture: <c>pull</c> (g) pretends a hard pull so
        /// the path draws in level flight (0 or less gives it back to the measured pull); <c>home</c> stands the home beacon whatever the fuel; <c>glitch</c> starts a hit glitch;
        /// <c>select</c> selects the nearest enemy unit within <c>range</c> (default 20 km) through the game's own target list, which starts a lock spin-in.
        /// </summary>
        public static Dictionary<string, object> Ar(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            ArLayer ar = HudBoard.Instance != null ? HudBoard.Instance.Ar : null;
            if (ar == null) return Failure("HudAutomation", "Ar", "no AR layer yet (the board builds it on its first tick)");
            if (Has(args, "pull")) { float g = Number(args, "pull", 0f); ar.ForcePullG = g > 0f ? g : float.NaN; }
            if (Has(args, "home")) ar.ForceHome = Bool(args, "home");
            if (Has(args, "turn")) { float turn = Number(args, "turn", 0f); ar.ForceTurnDegS = turn != 0f ? turn : float.NaN; }      // deg/s right; 0 gives it back
            if (Has(args, "pitch")) { float pitch = Number(args, "pitch", 0f); ar.ForcePitchDeg = pitch != 0f ? pitch : float.NaN; }      // deg nose-down, prediction only
            if (Bool(args, "glitch")) ar.Glitch(Time.unscaledTime);
            // dive (degrees): pitch the seated aircraft and its velocity nose-down at once, so a scripted run can meet the ground with the pilot still flying
            // (the sim's own fly op hands the aircraft to an AI pilot, and the game drops the HUD with it).
            if (Has(args, "dive") && GameManager.GetLocalAircraft(out Aircraft diver) && diver != null && diver.rb != null)
            {
                Quaternion down = Quaternion.AngleAxis(Number(args, "dive", 30f), diver.transform.right);
                diver.rb.rotation = down * diver.rb.rotation;
                diver.rb.velocity = down * diver.rb.velocity;
            }
            string selected = "";
            if (Bool(args, "select") && GameManager.GetLocalAircraft(out Aircraft own) && own != null)
            {
                Unit target = NearestEnemy(own, Number(args, "range", 20000f));
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (target != null && hud != null) { hud.SelectUnit(target); selected = target.definition != null ? target.definition.unitName : "TGT"; }
            }
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "selected", selected }, { "path", ar.PathShown }, { "path_alert", ar.PathAlert }, { "domes", ar.DomesShown }, { "inside_dome", ar.InsideDome },
                { "own_missiles", ar.OwnMissilesShown }, { "inbound", ar.InboundShown }, { "home", ar.HomeShown }, { "objective", ar.ObjectiveShown },
                { "spins", ar.SpinsShown }, { "spins_active", ar.SpinsActive }, { "rings_known", ar.RingsKnown }, { "nearest_ring_km", ar.NearestRingKm }, { "glitching", ar.Glitching }, { "quads", ar.Quads },
            };
            Debug.Log("[HudAutomation] Ar: " + Describe(result));
            return result;
        }

        /// <summary>Fire one own missile station at the nearest enemy aircraft (blind if none).</summary>
        public static Dictionary<string, object> Fire(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            if (!GameManager.GetLocalAircraft(out Aircraft a) || a == null || a.disabled)
                return Failure("HudAutomation", "Fire", "no live local aircraft");
            WeaponStation station = null;
            if (a.weaponStations != null)
                foreach (WeaponStation s in a.weaponStations)
                {
                    if (s == null || s.WeaponInfo == null || !s.WeaponInfo.missile || s.WeaponInfo.bomb || s.Ammo <= 0)
                        continue;
                    station = s;
                    break;
                }
            if (station == null)
                return Failure("HudAutomation", "Fire", "no loaded missile station");
            Unit target = NearestEnemy(a, Number(args, "range", 15000f));
            int before = OwnMissiles(a);
            try { station.Fire(a, target); }
            catch (Exception e) { return Failure("HudAutomation", "Fire", "station threw: " + e.Message); }
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "station", station.WeaponInfo.shortName ?? station.WeaponInfo.weaponName ?? "MSL" },
                { "ammo_left", station.Ammo },
                { "target", target != null && target.definition != null ? target.definition.unitName : target != null ? "TGT" : "BLIND" },
                { "missiles_before", before },
                { "missiles_after", OwnMissiles(a) },
            };
            Debug.Log("[HudAutomation] Fire: " + Describe(result));
            return result;
        }

        /// <summary>
        /// Select the first loaded fixed gun, designate the nearest enemy aircraft and report the native
        /// lead pipper (tests/ingame/gun-pipper.json). Call it three times about a second apart: the first
        /// sights the target, the second designates it, the third reads the pipper.
        /// </summary>
        public static Dictionary<string, object> Gun(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            if (!GameManager.GetLocalAircraft(out Aircraft a) || a == null || a.disabled)
                return Failure("HudAutomation", "Gun", "no live local aircraft");
            WeaponStation gun = null;
            if (a.weaponStations != null)
                foreach (WeaponStation s in a.weaponStations)
                {
                    if (s == null || s.WeaponInfo == null || s.WeaponInfo.muzzleVelocity <= 0f || s.HasTurret() || s.Ammo <= 0)
                        continue;
                    gun = s;
                    break;
                }
            if (gun == null)
                return Failure("HudAutomation", "Gun", "no loaded fixed gun");
            if (Bool(args, "release"))      // drop the designation (the cockpit TARGET CAM scenario)
            {
                CombatHUD held = SceneSingleton<CombatHUD>.i;
                if (held != null) held.DeselectAll(false);
                return new Dictionary<string, object> { { "ok", true }, { "released", true } };
            }
            Unit target = NearestEnemy(a, Number(args, "range", 3000f));
            if (target == null)
                return Failure("HudAutomation", "Gun", "no enemy aircraft in range");
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || hud.aircraft != a)
                return Failure("HudAutomation", "Gun", "the combat HUD is not attached to the local aircraft");
            // A sighting keeps the track "accurate" for 4 s and spawns the map icon and HUD marker SelectUnit needs.
            a.NetworkHQ.RpcUpdateTrackingInfo(target.persistentID);
            if (a.weaponManager.currentWeaponStation != gun) a.SetActiveStation(gun.Number);
            if (!hud.GetTargetList().Contains(target)) hud.SelectUnit(target);
            HUDBoresightState sight = UnityEngine.Object.FindObjectOfType<HUDBoresightState>();
            var pipper = sight == null ? null
                : HarmonyLib.AccessTools.Field(typeof(HUDBoresightState), "projectedPosition")?.GetValue(sight) as UnityEngine.UI.Image;
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "gun", gun.WeaponInfo.shortName ?? gun.WeaponInfo.weaponName ?? "GUN" },
                { "range_m", Vector3.Distance(target.transform.position, a.transform.position) },
                { "designated", hud.GetTargetList().Contains(target) ? 1 : 0 },
                { "state", sight == null ? "none"
                    : HarmonyLib.AccessTools.Field(typeof(HUDBoresightState), "displayState")?.GetValue(sight)?.ToString() ?? "?" },
                { "pipper", pipper != null && pipper.enabled ? 1 : 0 },
            };
            Debug.Log("[HudAutomation] Gun: " + Describe(result));
            return result;
        }

        /// <summary>
        /// Select the first loaded anti-air missile station and designate the <c>count</c> (default 2) nearest enemy aircraft inside <c>range</c> (default 30000 m), then report what the WPN cell reads: the
        /// designated targets, salvo and IN RNG, computed as <see cref="StoresFeed"/> does (tests/ingame/hud-bar.json). Call it twice about a second apart: the first sights the targets, the second designates them.
        /// </summary>
        public static Dictionary<string, object> Lock(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            if (!GameManager.GetLocalAircraft(out Aircraft a) || a == null || a.disabled)
                return Failure("HudAutomation", "Lock", "no live local aircraft");
            WeaponStation station = null;
            if (a.weaponStations != null)
                foreach (WeaponStation s in a.weaponStations)
                {
                    WeaponInfo w = s != null ? s.WeaponInfo : null;
                    if (w == null || !w.missile || w.effectiveness.antiAir < w.effectiveness.antiSurface || s.Ammo <= 0) continue;
                    station = s;
                    break;
                }
            if (station == null)
                return Failure("HudAutomation", "Lock", "no loaded anti-air missile station");
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || hud.aircraft != a)
                return Failure("HudAutomation", "Lock", "the combat HUD is not attached to the local aircraft");
            if (a.weaponManager.currentWeaponStation != station) a.SetActiveStation(station.Number);
            int count = (int)Number(args, "count", 2f);
            float range = Number(args, "range", 30000f);
            var picked = new List<Unit>(hud.GetTargetList());
            // Keep the designated tracks fresh and only fill the list up to count: a repeat call must not add a farther target (it pushed the WPN cell OUT OF RANGE).
            foreach (Unit held in picked)
                if (held != null) a.NetworkHQ.RpcUpdateTrackingInfo(held.persistentID);
            for (int k = picked.Count; k < count; k++)
            {
                Unit target = NearestEnemy(a, range, picked);
                if (target == null) break;
                picked.Add(target);
                // A sighting keeps the track "accurate" for 4 s and spawns the HUD marker SelectUnit needs (the second call designates).
                a.NetworkHQ.RpcUpdateTrackingInfo(target.persistentID);
                if (!hud.GetTargetList().Contains(target)) hud.SelectUnit(target);
            }
            int targets = a.weaponManager.GetTargetList()?.Count ?? 0;
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "weapon", station.WeaponInfo.shortName ?? station.WeaponInfo.weaponName ?? "MSL" },
                { "targets", targets },
                { "salvo", StoresFeed.Salvo(station.WeaponInfo) },
                { "in_range", StoresFeed.InRange(station.SafetyIsOn(a), targets, hud) },
            };
            Debug.Log("[HudAutomation] Lock: " + Describe(result));
            return result;
        }

        /// <summary><see cref="Lock"/>'s IN RNG alone, for a <c>wait_until {call, truthy}</c> (tests/ingame/hud-bar.json): each poll re-sights the targets, the capture waits until the shot is really on.</summary>
        public static bool LockedInRange(Dictionary<string, object> args) => Lock(args).TryGetValue("in_range", out object v) && v is bool b && b;

        /// <summary>Tracker snapshot: preview lines exactly as the HUD publishes them.</summary>
        public static Dictionary<string, object> Missiles(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            GameManager.GetLocalAircraft(out Aircraft a);
            MissileTracker tracker = HudBoard.Instance != null ? HudBoard.Instance.Tracker : null;
            if (tracker == null) return Failure("HudAutomation", "Missiles", "no HudBoard");
            var tracks = new List<MissileTrack>(MissileTracks.MaxShown);
            tracker.Read(a, tracks);
            AvUnits units = PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric ? AvUnits.Metric : AvUnits.Imperial;
            var lines = new List<object>(tracks.Count);
            foreach (MissileTrack t in tracks)
                lines.Add(new Dictionary<string, object>
                {
                    { "line", MissileTracks.LineText(t, units) },
                    { "detail", MissileTracks.DetailText(t) },
                    { "bar", MissileTracks.BarFor(t) },
                    { "tone", MissileTracks.ToneFor(t).ToString() },
                });
            ModuleServices.TryGet(out IMissileView view);
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "count", tracker.Missiles.Count },
                { "shown", tracks.Count },
                { "tracks", lines },
                { "view_active", view?.Active ?? false },
                { "view_label", view?.CurrentLabel ?? "" },
                { "view_status", view?.Status ?? "" },
                { "following", cam != null && cam.followingUnit != null ? cam.followingUnit.GetType().Name : "none" },
            };
            Debug.Log("[HudAutomation] Missiles: " + Describe(result));
            return result;
        }

        /// <summary>Drive the follow view through IMissileView (the seam the C-menu uses).</summary>
        public static Dictionary<string, object> View(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            if (!ModuleServices.TryGet(out IMissileView view) || view == null)
                return Failure("HudAutomation", "View", "no IMissileView service");
            switch (Text(args, "action"))
            {
                case "enter": view.Enter(); break;
                case "next": view.Next(); break;
                case "prev": view.Prev(); break;
                case "exit": view.Exit(); break;
                default: return Failure("HudAutomation", "View", "action must be enter/next/prev/exit");
            }
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "active", view.Active },
                { "count", view.Count },
                { "label", view.CurrentLabel ?? "" },
                { "following", cam != null && cam.followingUnit != null ? cam.followingUnit.GetType().Name : "none" },
            };
            Debug.Log("[HudAutomation] View: " + Describe(result));
            return result;
        }

        /// <summary>Dock geometry probe: native container rect, children, and our board vs visible content.</summary>
        public static Dictionary<string, object> Dock(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null)
                return Failure("HudAutomation", "Dock", "no CombatHUD");
            var topField = HarmonyLib.AccessTools.Field(typeof(CombatHUD), "topRightPanel");
            var dock = (topField?.GetValue(hud) as GameObject)?.transform as RectTransform;
            if (dock == null)
                return Failure("HudAutomation", "Dock", "no topRightPanel");
            Canvas canvas = dock.GetComponentInParent<Canvas>();
            var kids = new List<object>();
            float cMinX = float.MaxValue, cMaxX = float.MinValue, cMinY = float.MaxValue, cMaxY = float.MinValue;
            string ours = null;
            var corners = new Vector3[4];
            for (int i = 0; i < dock.childCount; i++)
            {
                var child = dock.GetChild(i) as RectTransform;
                if (child == null) continue;
                bool isOurs = child.name == "BoscaliStatusPanel";
                if (isOurs) ours = RectIn(dock, child, corners);
                bool active = child.gameObject.activeInHierarchy;
                bool visual = active && child.GetComponentInChildren<UnityEngine.UI.Graphic>() != null;
                kids.Add(new Dictionary<string, object>
                {
                    { "name", child.name }, { "active", active }, { "visual", visual },
                    { "rect", RectIn(dock, child, corners) },
                    { "components", Components(child) },
                });
                if (!isOurs && visual)
                {
                    child.GetWorldCorners(corners);
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 p = dock.InverseTransformPoint(corners[k]);
                        if (p.x < cMinX) cMinX = p.x;
                        if (p.x > cMaxX) cMaxX = p.x;
                        if (p.y < cMinY) cMinY = p.y;
                        if (p.y > cMaxY) cMaxY = p.y;
                    }
                }
            }
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "dock_rect", $"x[{dock.rect.xMin:F1},{dock.rect.xMax:F1}] y[{dock.rect.yMin:F1},{dock.rect.yMax:F1}] pivot[{dock.pivot.x:F2},{dock.pivot.y:F2}]" },
                { "dock_components", Components(dock) },
                { "content", cMinX <= cMaxX ? $"x[{cMinX:F1},{cMaxX:F1}] y[{cMinY:F1},{cMaxY:F1}]" : "none" },
                { "ours", ours ?? "missing" },
                { "canvas_scale", canvas != null ? canvas.scaleFactor : 0f },
                { "screen", $"{UnityEngine.Screen.width}x{UnityEngine.Screen.height}" },
                { "children", kids },
            };
            Debug.Log("[HudAutomation] Dock: " + Describe(result));
            return result;
        }

        private static string RectIn(RectTransform dock, RectTransform child, Vector3[] corners)
        {
            child.GetWorldCorners(corners);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                Vector3 p = dock.InverseTransformPoint(corners[k]);
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            return $"x[{minX:F1},{maxX:F1}] y[{minY:F1},{maxY:F1}]";
        }

        private static string Components(RectTransform t)
        {
            var list = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c is RectTransform || c is Transform) continue;
                list.Add(c.GetType().Name);
            }
            return string.Join(",", list.ToArray());
        }

        // ---- AV LAYER probes (tests/ingame/hud-sync.json, hud-ink.json, hud-perf.json, hud-layout.json) ----

        /// <summary>
        /// The AV layer's A/B switch and counters. <c>enabled</c> false runs the game's own placement and every module's old path (the baseline half of hud-sync / hud-perf in the same run); true gives the late pass back.
        /// Reads back the switch and the layer's counters (late frames, forced rebuilds, the last frame the Repair phase finished clean).
        /// </summary>
        public static Dictionary<string, object> Layer(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            if (Has(args, "enabled")) BoscaliSummer.Core.Ui.HudLayer.Enabled = Bool(args, "enabled");
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "enabled", BoscaliSummer.Core.Ui.HudLayer.Enabled }, { "late_frames", BoscaliSummer.Core.Ui.HudLayer.LateFrames },
                { "forced_rebuilds", BoscaliSummer.Core.Ui.HudLayer.ForcedRebuilds }, { "repair_ok_frame", BoscaliSummer.Core.Ui.HudLayer.RepairOkFrame }, { "frame", Time.frameCount },
                { "roots", BoscaliSummer.Core.Ui.HudLayer.Under != null && BoscaliSummer.Core.Ui.HudLayer.Data != null },
            };
            Debug.Log("[HudAutomation] Layer: " + Describe(result));
            return result;
        }

        /// <summary>
        /// The footprint profiler for the HUD (the spec's watch list by default), without a map: <c>do</c> uncap (vsync off, no frame cap: a hidden sim window otherwise stalls), profile (<c>deep</c>, <c>watch</c>), reset
        /// (a fresh window), snapshot (the JSON, <c>snapshot</c> in the result). Same calls as the map sims' <c>MapInputAutomation.Step</c>.
        /// </summary>
        public static Dictionary<string, object> Profile(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            switch (Text(args, "do"))
            {
                case "uncap":
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = -1;
                    return new Dictionary<string, object> { { "ok", true } };
                case "profile":
                    BoscaliSummer.Core.Diagnostics.FootprintProfiler.Watch = Text(args, "watch") ?? HudProfileWatch;
                    return new Dictionary<string, object> { { "ok", true }, { "mode", BoscaliSummer.Core.Diagnostics.FootprintProfiler.Start(Bool(args, "deep")) } };
                case "reset":
                    BoscaliSummer.Core.Diagnostics.FootprintProfiler.ResetWindow();
                    return new Dictionary<string, object> { { "ok", true } };
                case "snapshot":
                    return new Dictionary<string, object> { { "ok", true }, { "snapshot", BoscaliSummer.Core.Diagnostics.FootprintProfiler.SnapshotJson() } };
                default: return Failure("HudAutomation", "Profile", "do must be uncap/profile/reset/snapshot");
            }
        }

        private const string HudProfileWatch = "HudLayer.OnLate;ThirdPersonHudCenter.RefreshProjection;ArLayer.Frame;CombatAlerts.Frame;StatusPanel.Repaint;StatusPanel.Animate;AvVector.UpdateGeometry;AvVector.OnPopulateMesh;ScopeText.Draw;VisorGlitch.Scan;HudBoard.LateUpdate";

        /// <summary>
        /// A deterministic stand-in for head shake and roll: yaw at a peak of <c>deg_per_s</c> (half a hertz sine), roll <c>roll</c> degrees, FOV breath <c>zoom</c> (fraction), added after the game's own cockpit camera update
        /// (<see cref="BoscaliSummer.Modules.Hud.Patches.SimSpinPatch"/>). <c>off</c> true (or <c>deg_per_s</c> 0 with no roll or zoom) stops it. Cockpit view only.
        /// </summary>
        public static Dictionary<string, object> Spin(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            if (Bool(args, "off") || (Has(args, "deg_per_s") && Number(args, "deg_per_s", 0f) == 0f && !Has(args, "roll") && !Has(args, "zoom")))
                BoscaliSummer.Modules.Hud.Patches.SimSpinPatch.Stop();
            else
                BoscaliSummer.Modules.Hud.Patches.SimSpinPatch.Start(Number(args, "deg_per_s", 90f), Number(args, "roll", 4f), Number(args, "zoom", 0.15f));
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "active", BoscaliSummer.Modules.Hud.Patches.SimSpinPatch.Active }, { "yaw_deg_s", BoscaliSummer.Modules.Hud.Patches.SimSpinPatch.YawDegS },
                { "roll", BoscaliSummer.Modules.Hud.Patches.SimSpinPatch.RollDeg }, { "zoom", BoscaliSummer.Modules.Hud.Patches.SimSpinPatch.Zoom },
            };
            Debug.Log("[HudAutomation] Spin: " + Describe(result));
            return result;
        }

        private static int faultFrames;

        private static void FaultOnce(BoscaliSummer.Core.Ui.HudView view)
        {
            if (faultFrames <= 0) return;
            faultFrames--;
            throw new InvalidOperationException("HudAutomation fault probe");
        }

        /// <summary>Makes the layer's Repair phase throw for the next <c>frames</c> late passes (default 3), through a subscriber of its own: the fail-open check (the repair-ok frame must lag, then recover, and the log must not spam).</summary>
        public static Dictionary<string, object> Fault(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            faultFrames = (int)Number(args, "frames", 3f);
            BoscaliSummer.Core.Ui.HudLayer.Repair += FaultOnce;
            var result = new Dictionary<string, object> { { "ok", true }, { "frames", faultFrames } };
            Debug.Log("[HudAutomation] Fault: " + Describe(result));
            return result;
        }

        /// <summary>
        /// The sync probe. <c>do</c> start (default; resets the window), stop, read. In every frame the main camera renders it takes, for each visible native marker (not selected, not jammed), the distance in screen pixels between where the game
        /// drew the marker and where the rendered camera puts the unit (<c>hq.TryGetKnownPosition</c> when the marker is outdated); the HUD centre (position and roll) against the aim point 4 km down the cockpit; the velocity vector against the velocity point.
        /// Returns max, p95 and mean of the marker error, the centre and velocity-vector error, the origin-shift frames and the worst error on them, the frames the Repair phase had finished clean, and the layer's counters over the window.
        /// </summary>
        public static Dictionary<string, object> Sync(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            switch (Text(args, "do") ?? "start")
            {
                case "start": SyncProbe.Start(); break;
                case "stop": SyncProbe.Stop(); break;
                case "read": break;
                default: return Failure("HudAutomation", "Sync", "do must be start/stop/read");
            }
            Dictionary<string, object> result = SyncProbe.Read();
            Debug.Log("[HudAutomation] Sync: " + Describe(result));
            return result;
        }

        private static class SyncProbe
        {
            private const int Cap = 60000;
            private static readonly MarkerRefs Refs = new MarkerRefs();
            private static readonly float[] errors = new float[Cap];
            private static int count, frames, okFrames, shiftFrames, fpmSamples, centreSamples;
            private static float max, sum, shiftMax, notOkMax, centreMax, centreSum, rollMax, fpmMax, fpmSum;
            private static int lateAt, forcedAt, lastFrame = -1, notOkSamples;
            private static Vector3 lastOrigin;
            private static bool on;

            public static void Start()
            {
                count = frames = okFrames = shiftFrames = fpmSamples = centreSamples = notOkSamples = 0;
                max = sum = shiftMax = notOkMax = centreMax = centreSum = rollMax = fpmMax = fpmSum = 0f;
                lateAt = BoscaliSummer.Core.Ui.HudLayer.LateFrames;
                forcedAt = BoscaliSummer.Core.Ui.HudLayer.ForcedRebuilds;
                lastOrigin = Datum.originPosition;
                lastFrame = -1;
                if (on) return;
                on = true;
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnCamera;
            }

            public static void Stop()
            {
                if (!on) return;
                on = false;
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnCamera;
            }

            private static void OnCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera camera)
            {
                CameraStateManager state = SceneSingleton<CameraStateManager>.i;
                if (state == null || camera != state.mainCamera || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                bool repairOk = BoscaliSummer.Core.Ui.HudLayer.RepairOkFrame == lastFrame;
                bool shifted = Datum.originPosition != lastOrigin;
                lastOrigin = Datum.originPosition;
                frames++;
                if (repairOk) okFrames++;
                if (shifted) shiftFrames++;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud == null || !GameManager.GetLocalAircraft(out Aircraft own) || own == null) return;
                List<HUDUnitMarker> markers = BoscaliSummer.Modules.Hud.Runtime.NativeRefs.Get(Refs.Markers, hud);
                if (markers != null && hud.jamAccumulation <= 0f && own.NetworkHQ != null)
                    for (int i = 0; i < markers.Count; i++)
                    {
                        HUDUnitMarker m = markers[i];
                        if (m == null || m.image == null || m.unit == null || m.selected || !m.image.enabled || BoscaliSummer.Modules.Hud.Runtime.NativeRefs.Get(Refs.Hidden, m)) continue;
                        GlobalPosition known = m.unit.GlobalPosition();
                        if (m.outdated && !own.NetworkHQ.TryGetKnownPosition(m.unit, out known)) continue;
                        Vector3 expect = camera.WorldToScreenPoint(known.ToLocalPosition());
                        // Only points well inside the screen and in front: the game pins and clamps the rest.
                        if (expect.z < 1f || expect.x < camera.pixelWidth * 0.05f || expect.x > camera.pixelWidth * 0.95f || expect.y < camera.pixelHeight * 0.05f || expect.y > camera.pixelHeight * 0.95f) continue;
                        Vector3 drawn = m.image.transform.position;
                        float error = Mathf.Sqrt((drawn.x - expect.x) * (drawn.x - expect.x) + (drawn.y - expect.y) * (drawn.y - expect.y));
                        if (count < Cap) errors[count] = error;
                        count++;
                        sum += error;
                        if (error > max) max = error;
                        if (shifted && error > shiftMax) shiftMax = error;
                        if (!repairOk) { notOkSamples++; if (error > notOkMax) notOkMax = error; }
                    }
                FlightHud flight = SceneSingleton<FlightHud>.i;
                if (flight == null || own.cockpit == null || own.cockpit.rb == null) return;
                Transform cockpit = own.cockpit.transform;
                Transform centre = flight.GetHUDCenter();
                if (centre != null)
                {
                    Vector3 aim = camera.WorldToScreenPoint(cockpit.position + cockpit.forward * 4000f);
                    if (aim.z > 0f)
                    {
                        Vector3 p = centre.position;
                        float e = Mathf.Sqrt((p.x - aim.x) * (p.x - aim.x) + (p.y - aim.y) * (p.y - aim.y));
                        centreSamples++;
                        centreSum += e;
                        if (e > centreMax) centreMax = e;
                    }
                    float wantRoll = -(camera.transform.eulerAngles.z - cockpit.eulerAngles.z);
                    float re = Mathf.Abs(Mathf.DeltaAngle(centre.eulerAngles.z, wantRoll));
                    if (re > rollMax) rollMax = re;
                }
                if (flight.velocityVector != null && flight.velocityVector.gameObject.activeSelf)
                {
                    Vector3 vel = camera.WorldToScreenPoint(cockpit.position + own.cockpit.rb.velocity * 1000f);
                    if (vel.z > 0f)
                    {
                        Vector3 p = flight.velocityVector.transform.position;
                        float e = Mathf.Sqrt((p.x - vel.x) * (p.x - vel.x) + (p.y - vel.y) * (p.y - vel.y));
                        fpmSamples++;
                        fpmSum += e;
                        if (e > fpmMax) fpmMax = e;
                    }
                }
            }

            public static Dictionary<string, object> Read()
            {
                int n = Mathf.Min(count, Cap);
                float p95 = 0f;
                if (n > 0)
                {
                    var copy = new float[n];
                    Array.Copy(errors, copy, n);
                    Array.Sort(copy);
                    p95 = copy[Mathf.Min(n - 1, (int)(n * 0.95f))];
                }
                int late = BoscaliSummer.Core.Ui.HudLayer.LateFrames - lateAt, forced = BoscaliSummer.Core.Ui.HudLayer.ForcedRebuilds - forcedAt;
                return new Dictionary<string, object>
                {
                    { "ok", true }, { "running", on }, { "layer", BoscaliSummer.Core.Ui.HudLayer.Enabled },
                    { "frames", frames }, { "samples", count }, { "err_max", max }, { "err_p95", p95 }, { "err_mean", count > 0 ? sum / count : 0f },
                    { "centre_samples", centreSamples }, { "centre_max", centreMax }, { "centre_mean", centreSamples > 0 ? centreSum / centreSamples : 0f },
                    { "roll_max_deg", rollMax }, { "fpm_samples", fpmSamples }, { "fpm_max", fpmMax }, { "fpm_mean", fpmSamples > 0 ? fpmSum / fpmSamples : 0f },
                    { "origin_shift_frames", shiftFrames }, { "err_max_on_shift", shiftMax },
                    { "repair_ok_frames", okFrames }, { "repair_not_ok_frames", frames - okFrames }, { "err_max_not_ok", notOkMax }, { "samples_not_ok", notOkSamples },
                    { "late_frames", late }, { "forced_rebuilds", forced }, { "forced_pct", late > 0 ? 100f * forced / late : 0f },
                };
            }
        }

        /// <summary>The game's private marker state the probes read (a renamed field gives a null ref and reads default, never an exception).</summary>
        private sealed class MarkerRefs
        {
            public readonly HarmonyLib.AccessTools.FieldRef<CombatHUD, List<HUDUnitMarker>> Markers = BoscaliSummer.Modules.Hud.Runtime.NativeRefs.Field<CombatHUD, List<HUDUnitMarker>>("markers");
            public readonly HarmonyLib.AccessTools.FieldRef<HUDUnitMarker, bool> Hidden = BoscaliSummer.Modules.Hud.Runtime.NativeRefs.Field<HUDUnitMarker, bool>("hidden");
            public readonly HarmonyLib.AccessTools.FieldRef<HUDUnitMarker, bool> Flashing = BoscaliSummer.Modules.Hud.Runtime.NativeRefs.Field<HUDUnitMarker, bool>("flashing");
            public readonly HarmonyLib.AccessTools.FieldRef<HUDUnitMarker, bool> Maximized = BoscaliSummer.Modules.Hud.Runtime.NativeRefs.Field<HUDUnitMarker, bool>("maximized");
            public readonly HarmonyLib.AccessTools.FieldRef<HUDUnitMarker, Color> Color = BoscaliSummer.Modules.Hud.Runtime.NativeRefs.Field<HUDUnitMarker, Color>("color");
            public readonly HarmonyLib.AccessTools.FieldRef<HUDUnitMarker, float> Opacity = BoscaliSummer.Modules.Hud.Runtime.NativeRefs.Field<HUDUnitMarker, float>("opacity");
        }

        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static float Chroma(Color c)
        {
            float l = Luma(c);
            return Mathf.Sqrt((c.r - l) * (c.r - l) + (c.g - l) * (c.g - l) + (c.b - l) * (c.b - l));
        }

        /// <summary>
        /// The range ink on the game's own markers (tests/ingame/hud-ink.json). For up to <c>max</c> (default 12) visible markers, nearest first: distance, the 16-step ramp index (<c>HudInk.RangeStep</c>), the observed saturation ratio
        /// (chroma of the drawn colour over chroma of the colour the game keeps for the marker, 1 = untouched) and alpha ratio (drawn alpha over the game's own opacity), both colours, and the flags that exempt a marker (selected, fresh, flashing,
        /// jammed, outdated). Also the number of distinct materials across the HUD canvas's renderers (a ramp that swaps materials would raise it). Lines read "name d=m k=step s=ratio a=ratio rgb=... native=... flags".
        /// </summary>
        public static Dictionary<string, object> Ink(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || !GameManager.GetLocalAircraft(out Aircraft own) || own == null) return Failure("HudAutomation", "Ink", "no HUD or aircraft");
            var refs = new MarkerRefs();
            List<HUDUnitMarker> markers = BoscaliSummer.Modules.Hud.Runtime.NativeRefs.Get(refs.Markers, hud);
            var rows = new List<KeyValuePair<float, string>>();
            Vector3 eye = SceneSingleton<CameraStateManager>.i.transform.position;
            if (markers != null)
                foreach (HUDUnitMarker m in markers)
                {
                    if (m == null || m.image == null || m.unit == null || !m.image.enabled) continue;
                    GlobalPosition known = m.unit.GlobalPosition();
                    if (m.outdated && own.NetworkHQ != null && !own.NetworkHQ.TryGetKnownPosition(m.unit, out known)) continue;
                    float d = Vector3.Distance(eye, known.ToLocalPosition());
                    Color native = BoscaliSummer.Modules.Hud.Runtime.NativeRefs.Get(refs.Color, m), drawn = m.image.color;
                    float opacity = BoscaliSummer.Modules.Hud.Runtime.NativeRefs.Get(refs.Opacity, m) * (m.outdated && BoscaliSummer.Modules.Hud.Runtime.NativeRefs.Get(refs.Maximized, m) ? 0.5f : 1f);
                    float cn = Chroma(native);
                    string flags = (m.selected ? "selected " : "") + (m.fresh ? "fresh " : "") + (BoscaliSummer.Modules.Hud.Runtime.NativeRefs.Get(refs.Flashing, m) ? "flashing " : "") +
                                   (hud.jamAccumulation > 0f ? "jammed " : "") + (m.outdated ? "outdated " : "");
                    string line = string.Format(CultureInfo.InvariantCulture, "{0} d={1:F0} k={2} s={3:F3} a={4:F3} rgb={5:F3},{6:F3},{7:F3} native={8:F3},{9:F3},{10:F3} {11}",
                        m.image.gameObject.name, d, BoscaliSummer.Core.Contracts.HudInk.RangeStep(d), cn > 1e-4f ? Chroma(drawn) / cn : 1f, opacity > 1e-4f ? drawn.a / opacity : 1f,
                        drawn.r, drawn.g, drawn.b, native.r, native.g, native.b, flags.Trim());
                    rows.Add(new KeyValuePair<float, string>(d, line));
                }
            rows.Sort((a, b) => a.Key.CompareTo(b.Key));
            int take = Mathf.Min(rows.Count, (int)Number(args, "max", 12f));
            var lines = new List<object>();
            for (int i = 0; i < take; i++) lines.Add(rows[i].Value);
            var materials = new HashSet<Material>();
            Canvas canvas = BoscaliSummer.Core.Ui.HudLayer.HudCanvas;
            if (canvas != null)
                foreach (UnityEngine.UI.Graphic g in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
                    if (g != null && g.canvasRenderer != null && g.canvasRenderer.materialCount > 0) materials.Add(g.canvasRenderer.GetMaterial(0));
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "layer", BoscaliSummer.Core.Ui.HudLayer.Enabled }, { "markers", rows.Count }, { "lines", lines }, { "materials", materials.Count },
                { "nearest_d", rows.Count > 0 ? rows[0].Key : -1f }, { "farthest_d", rows.Count > 0 ? rows[rows.Count - 1].Key : -1f },
            };
            Debug.Log("[HudAutomation] Ink: " + Describe(result));
            return result;
        }

        /// <summary>
        /// Where the HUD's blocks are on screen (tests/ingame/hud-layout.json), so a run before and after the re-parenting can be compared: for each top-level object the mod keeps in the game's HUD canvas whose name starts with "Boscali",
        /// the screen-pixel bounds of its drawn graphics ("x0,y0,x1,y1 (n graphics)"), plus the game's own dock (topRightPanel). Keys are <c>rect.name</c>; the name list is <c>names</c>.
        /// </summary>
        public static Dictionary<string, object> Layout(Dictionary<string, object> args)
        {
            if (!Guard(out Dictionary<string, object> failure)) return failure;
            if (!BoscaliSummer.Modules.Hud.Runtime.NativeRefs.HudCanvas(out CombatHUD hud, out Canvas canvas))
                return Failure("HudAutomation", "Layout", "no HUD canvas");
            var result = new Dictionary<string, object> { { "ok", true }, { "layer", BoscaliSummer.Core.Ui.HudLayer.Enabled }, { "screen", $"{Screen.width}x{Screen.height}" } };
            var names = new List<object>();
            var corners = new Vector3[4];
            var topRight = HarmonyLib.AccessTools.Field(typeof(CombatHUD), "topRightPanel")?.GetValue(hud) as GameObject;
            if (topRight != null) result["rect.dock"] = Bounds(topRight.transform, corners, false);
            LayoutWalk(canvas.transform, result, names, corners, 0);
            result["names"] = names;
            result["blocks"] = names.Count;
            Debug.Log("[HudAutomation] Layout: " + Describe(result));
            return result;
        }

        private static void LayoutWalk(Transform parent, Dictionary<string, object> result, List<object> names, Vector3[] corners, int depth)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (!child.gameObject.activeInHierarchy) continue;
                bool layerRoot = child.name == "BoscaliHudOver" || child.name == "BoscaliHudUnder";      // the AV layer roots hold blocks: walk into them
                if (!layerRoot && child.name.StartsWith("Boscali", StringComparison.Ordinal))
                {
                    string key = child.name;
                    for (int n = 2; result.ContainsKey("rect." + key); n++) key = child.name + "#" + n;
                    result["rect." + key] = Bounds(child, corners, true);
                    names.Add(key);
                }
                else if (depth < 3) LayoutWalk(child, result, names, corners, depth + 1);
            }
        }

        /// <summary>The screen-pixel bounds of the drawn graphics under a transform (or of its own rect when <paramref name="graphics"/> is false).</summary>
        private static string Bounds(Transform root, Vector3[] corners, bool graphics)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            int n = 0;
            if (graphics)
            {
                foreach (UnityEngine.UI.Graphic g in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
                {
                    if (g == null || !g.enabled || g.canvasRenderer == null || g.canvasRenderer.cull || g.canvasRenderer.GetInheritedAlpha() * g.color.a < 0.01f) continue;
                    g.rectTransform.GetWorldCorners(corners);
                    for (int k = 0; k < 4; k++) { x0 = Mathf.Min(x0, corners[k].x); x1 = Mathf.Max(x1, corners[k].x); y0 = Mathf.Min(y0, corners[k].y); y1 = Mathf.Max(y1, corners[k].y); }
                    n++;
                }
            }
            else if (root is RectTransform rect)
            {
                rect.GetWorldCorners(corners);
                for (int k = 0; k < 4; k++) { x0 = Mathf.Min(x0, corners[k].x); x1 = Mathf.Max(x1, corners[k].x); y0 = Mathf.Min(y0, corners[k].y); y1 = Mathf.Max(y1, corners[k].y); }
                n = 1;
            }
            return n == 0 ? "none" : string.Format(CultureInfo.InvariantCulture, "{0:F1},{1:F1},{2:F1},{3:F1} ({4} graphics)", x0, y0, x1, y1, n);
        }

        private static int OwnMissiles(Aircraft own)
        {
            int count = 0;
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null || own == null) return 0;
            for (int i = 0; i < all.Count; i++)
                if (all[i] is Missile m && m != null && !m.disabled && m.owner == own) count++;
            return count;
        }

        private static Unit NearestEnemy(Aircraft own, float maxM, List<Unit> skip = null)
        {
            Unit best = null;
            float bestSqr = maxM * maxM;
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null || own == null) return null;
            Vector3 p = own.transform.position;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is Aircraft a) || a == null || a.disabled || a == own) continue;
                if (a.NetworkHQ == null || own.NetworkHQ == null || a.NetworkHQ == own.NetworkHQ) continue;
                if (skip != null && skip.Contains(a)) continue;
                float d = (a.transform.position - p).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = a; }
            }
            return best;
        }
    }
}
#endif
