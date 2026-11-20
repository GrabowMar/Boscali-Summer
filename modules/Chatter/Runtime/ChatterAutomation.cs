#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Chatter;
using BoscaliSummer.Core.Contracts;
using UnityEngine;

namespace BoscaliSummer.Modules.Chatter.Runtime
{
    /// <summary>
    /// Hooks for unattended in-game tests (nomodkit's <c>call</c> op, tests/ingame/chatter.json): put known lines of every net on the radio so a capture shows the strip,
    /// and read back what the director did. Nothing here runs unless a nomodkit scenario calls it.
    /// </summary>
    public static class ChatterAutomation
    {
        private static readonly string[] Personas = { "veteran", "rookie", "hothead", "ice", "joker", "book" };
        private static readonly string[] Callsigns = { "COBALT", "MAGPIE", "FLINT", "FROST", "GADFLY", "LATCH" };
        private static ChatterSpeaker[] pilots;

        private static bool Guard(out ChatterHost host, out Dictionary<string, object> failure)
        {
            host = ChatterHost.Instance;
            failure = null;
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) || host == null)
                failure = new Dictionary<string, object> { ["ok"] = false, ["error"] = "requires a nomodkit scenario with the Chatter module installed" };
            return failure == null;
        }

        /// <summary><c>step</c>: wing, exchange, stations, enemy or notice. Returns how many of the step's calls the radio accepted.</summary>
        public static Dictionary<string, object> Demo(Dictionary<string, object> args)
        {
            if (!Guard(out ChatterHost host, out Dictionary<string, object> failure)) return failure;
            if (pilots == null)
            {
                pilots = new ChatterSpeaker[Personas.Length];
                for (int i = 0; i < pilots.Length; i++) pilots[i] = new ChatterSpeaker("demo:" + i, ChatterNet.Wing, Callsigns[i], Personas[i]);
            }
            string step = args != null && args.TryGetValue("step", out object value) ? value as string : null;
            int accepted = 0;
            void Count(bool ok) { if (ok) accepted++; }
            string stamp = Time.frameCount.ToString();
            switch (step)
            {
                case "wing":
                    Count(host.Raise("wing.fox3", pilots[2], "demo-fox:" + stamp, ("target", "Vortex")));
                    Count(host.Raise("wing.splash", pilots[2], "demo-splash:" + stamp, ("target", "Vortex")));
                    Count(host.Raise("wing.joker", pilots[1], "demo-joker:" + stamp, ("n", "6")));
                    Count(host.Raise("wing.bandit", pilots[3], "demo-bandit:" + stamp, ("bra", "090 for 40, angels 15, hot"), ("type", "Vortex")));
                    break;
                case "exchange":
                    Count(host.Director.StartExchange(host.Now, pilots));
                    break;
                case "stations":
                    Count(host.Raise("awacs.checkin", host.Station(ChatterNet.Awacs, FlightNets.Awacs), "demo-checkin:" + stamp));
                    Count(host.Raise("tower.landing", host.Station(ChatterNet.Tower, "HALDEN TOWER"), "demo-landing:" + stamp, ("runway", "Runway 21")));
                    Count(host.Raise("command.operation", host.Station(ChatterNet.Command, "COMMAND"), "demo-op:" + stamp, ("name", "Capture Airstrip"), ("phase", "ADVANCING")));
                    break;
                case "enemy":
                    Count(host.Raise("enemy.fox", new ChatterSpeaker("demo:e1", ChatterNet.Enemy, "VOLK 2", "zealot"), "demo-efox:" + stamp));
                    Count(host.Raise("enemy.lastwords", new ChatterSpeaker("demo:e2", ChatterNet.Enemy, "TALON 1", "conscript"), "demo-elast:" + stamp));
                    Count(host.Raise("awacs.missile", host.Station(ChatterNet.Awacs, FlightNets.Awacs), "demo-msl:" + stamp, ("clock", "three o'clock")));
                    break;
                case "notice":
                    AircraftActionsReport report = SceneSingleton<AircraftActionsReport>.i;
                    if (report == null) return new Dictionary<string, object> { ["ok"] = false, ["error"] = "no centre notice box in this cockpit" };
                    report.ReportText("Stability Assist Enabled", 5f);
                    report.ReportText("Cleared to taxi to Runway 21", 10f);
                    accepted = 2;
                    break;
                default:
                    return new Dictionary<string, object> { ["ok"] = false, ["error"] = "step is wing, exchange, stations, enemy or notice" };
            }
            return new Dictionary<string, object> { ["ok"] = true, ["step"] = step, ["accepted"] = accepted };
        }

        /// <summary>Hold the comms strip dark (<c>on</c>, the default) or let it show again (<c>off</c>): spoken lines still reach the voices, prompts and the MFD log. Capture prep, the same idea as Hud's option hook.</summary>
        public static Dictionary<string, object> Quiet(Dictionary<string, object> args)
        {
            if (!Guard(out ChatterHost host, out Dictionary<string, object> failure)) return failure;
            bool on = args == null || !(args.TryGetValue("off", out object off) && off is bool offB && offB);
            host.StripSuppressed = on;
            return new Dictionary<string, object> { ["ok"] = true, ["quiet"] = on };
        }

        private static int simYes, simNo;
        private static int acceptanceSerial;

        /// <summary>Live acceptance of the battlefield receiver. cold restarts the bank and immediately queues a required base report.</summary>
        public static Dictionary<string, object> Acceptance(Dictionary<string, object> args)
        {
            if (!Guard(out ChatterHost host, out Dictionary<string, object> failure)) return failure;
            string step = args != null && args.TryGetValue("step", out object raw) ? raw as string : "state";
            int accepted = 0;
            bool second = false;
            string key = "acceptance:" + ++acceptanceSerial;
            ChatterSpeaker Speaker(string role, string callsign)
            {
                ChatterSpeaker speaker = host.Station(ChatterNet.Command, callsign);
                speaker.Character = role;
                return speaker;
            }
            void Raise(string evt, ChatterSpeaker speaker, string suffix, params (string Name, string Value)[] facts)
            { if (host.Raise(evt, speaker, key + suffix, facts)) accepted++; }
            switch (step)
            {
                case "burst":
                    host.Director.Reset(17);
                    ChatterSpeaker ambient = Speaker("command", "COMMAND");
                    for (int i = 0; i < 32; i++) host.Say(ambient, key + ":ambient:" + i, "Routine traffic awaiting the channel.", ChatterPriority.Ambient);
                    Raise("command.baselost", Speaker("ground", "GROUND CONTROL"), ":lost", ("base", "HALDEN"));
                    Raise("command.basetaken", Speaker("ground", "GROUND CONTROL"), ":taken", ("base", "NORTH FIELD"));
                    Raise("command.enemybase", Speaker("command", "COMMAND"), ":enemy", ("base", "WEST FIELD"));
                    Raise("command.superevent", Speaker("command", "COMMAND"), ":super1", ("title", "PRIORITY TRAFFIC ONE"));
                    Raise("command.superevent", Speaker("command", "COMMAND"), ":super2", ("title", "PRIORITY TRAFFIC TWO"));
                    break;
                case "cold":
                    host.ResetForScene();
                    Raise("command.baselost", Speaker("ground", "GROUND CONTROL"), ":cold", ("base", "COLD START FIELD"));
                    break;
                case "reset": host.Director.Reset(17); break;
                case "nuclear": RadioAftermath.Observe(1, 1, Time.time, 0f); break;
                case "nuclearrelease": RadioAftermath.Release(1, 1, Time.time, 0f); break;
                case "nuclearlate": RadioAftermath.Reset(); RadioAftermath.Observe(1, 1, Time.time, 120f); break;
                case "nuclearsilence": RadioAftermath.Reset(); RadioAftermath.Observe(1, 1, Time.time, RadioAftermath.PanicSeconds + 1f); break;
                case "nuclearsurvivor": RadioAftermath.Reset(); RadioAftermath.Observe(1, 1, Time.time, RadioAftermath.PanicSeconds + RadioAftermath.SilenceSeconds + 1f); break;
                case "nuclearreset": RadioAftermath.Reset(); break;
                case "direct":
                    second = host.Director.Say(Time.time, Speaker("command", "COMMAND"), key + ":direct", "Direct normal traffic must be silent.", ChatterPriority.Emergency);
                    accepted = host.Director.StartExchange(Time.time, new[] { new ChatterSpeaker("direct:1", ChatterNet.Wing, "ONE", "veteran"), new ChatterSpeaker("direct:2", ChatterNet.Wing, "TWO", "rookie") }) ? 1 : 0;
                    break;
                case "bases":
                    Raise("command.baselost", Speaker("ground", "GROUND CONTROL"), ":lost", ("base", "HALDEN"));
                    Raise("command.basetaken", Speaker("ground", "GROUND CONTROL"), ":taken", ("base", "NORTH FIELD"));
                    Raise("command.enemybase", Speaker("command", "COMMAND"), ":enemy", ("base", "WEST FIELD"));
                    break;
                case "super":
                    Raise("command.superevent", Speaker("command", "COMMAND"), ":super1", ("title", "PRIORITY TRAFFIC ONE"));
                    Raise("command.superevent", Speaker("command", "COMMAND"), ":super2", ("title", "PRIORITY TRAFFIC TWO"));
                    break;
                case "ship":
                    Raise("command.carrierevac", Speaker("captain", "CARRIER CAPTAIN"), ":carrier", ("target", "FRIENDLY CARRIER"));
                    second = host.Raise("command.shiplost", Speaker("captain", "SHIP CAPTAIN"), key + ":ship");
                    break;
                case "event":
                    Raise("command.event", Speaker("command", "COMMAND"), ":event1", ("title", "ORDINARY EVENT ONE"));
                    second = host.Raise("command.event", Speaker("command", "COMMAND"), key + ":event2");
                    break;
                case "insertion": Raise("command.insertion", Speaker("paratrooper", "DROP LEAD"), ":insertion"); break;
                case "aircraft":
                    Raise("awacs.splash", host.Station(ChatterNet.Awacs, ChatterStations.Awacs), ":air1", ("target", "HOSTILE AIRCRAFT"));
                    second = host.Raise("awacs.splash", host.Station(ChatterNet.Awacs, ChatterStations.Awacs), key + ":air2");
                    break;
            }
            var recent = new List<object>();
            var eventCounts = new Dictionary<string, object>();
            var characterCounts = new Dictionary<string, object>();
            for (int i = 0; i < host.Director.Log.Count; i++)
            {
                ChatterAired line = host.Director.Log[i];
                string evt = (line.Event ?? "").Replace('.', '_'), character = line.Character ?? "";
                eventCounts[evt] = eventCounts.TryGetValue(evt, out object events) ? (int)events + 1 : 1;
                characterCounts[character] = characterCounts.TryGetValue(character, out object characters) ? (int)characters + 1 : 1;
            }
            for (int i = Math.Max(0, host.Director.Log.Count - 8); i < host.Director.Log.Count; i++)
            {
                ChatterAired line = host.Director.Log[i];
                recent.Add(new Dictionary<string, object> { ["event"] = line.Event, ["role"] = line.Role ?? "", ["character"] = line.Character ?? "", ["text"] = line.Text });
            }
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["step"] = step, ["accepted"] = accepted, ["secondAccepted"] = second,
                ["receiving"] = ChatterHost.Receiving, ["loading"] = host.Voices?.Loading ?? false,
                ["clips"] = host.Voices?.ClipsReady ?? 0, ["builtInPacks"] = host.Voices?.BuiltInPacks ?? 0,
                ["voiced"] = host.Voices?.Voiced ?? 0, ["sounding"] = host.Voices?.Sounding ?? false,
                ["fxSounding"] = ChatterAudio.Sounding,
                ["nuclearBlackout"] = RadioAftermath.Active,
                ["nuclearDrop"] = RadioAftermath.Drop,
                ["nuclearBlocking"] = RadioAftermath.Blocking,
                ["nuclearSilence"] = RadioAftermath.Silent(Time.time),
                ["nuclearEmergencyCalls"] = RadioAftermath.EmergencyCalls,
                ["nuclearSilenceUntil"] = RadioAftermath.SilenceUntil,
                ["level"] = host.Director.Level.ToString(),
                ["survivors"] = RadioAftermath.SurvivorCalls,
                ["nextSurvivor"] = RadioAftermath.NextSurvivorAt,
                ["aired"] = host.Director.AiredCount, ["dropped"] = host.Director.DroppedCount, ["pending"] = host.Director.Pending, ["last"] = recent,
                ["eventCounts"] = eventCounts, ["characterCounts"] = characterCounts,
            };
        }

        /// <summary>
        /// The YES/NO questions (2026-11-15). <c>step</c>: ask (COMMAND asks about a made-up contract; its answer only counts), allied (an allied pilot's splash call),
        /// yes / no (the pilot's key), state. Every step returns the state: open, asked, answered, last answer, the counted yes/no, and the voices (built-in packs,
        /// clips decoded, lines voiced).
        /// </summary>
        public static Dictionary<string, object> Question(Dictionary<string, object> args)
        {
            if (!Guard(out ChatterHost host, out Dictionary<string, object> failure)) return failure;
            string step = args != null && args.TryGetValue("step", out object value) ? value as string : "state";
            bool ok = true;
            switch (step)
            {
                case "ask":
                    ok = host.Ask(host.Station(ChatterNet.Command, "COMMAND"), "command.askcontract", "sim-contract:" + Time.frameCount, () => simYes++, () => simNo++,
                        ("title", "Strike the radar site"));
                    break;
                case "allied":
                    ok = host.Raise("allied.splash", new ChatterSpeaker("allied:sim", ChatterNet.Allied, "VIPER 2"), "sim-allied:" + Time.frameCount, ("type", "Vortex"));
                    break;
                case "yes": ok = host.Answer(true); break;
                case "no": ok = host.Answer(false); break;
            }
            ChatterQuestions q = host.Questions;
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["step"] = step, ["accepted"] = ok, ["open"] = q.Open, ["asked"] = q.Asked, ["answered"] = q.Answered, ["last"] = q.LastAnswer,
                ["yes"] = simYes, ["no"] = simNo, ["builtInPacks"] = host.Voices?.BuiltInPacks ?? 0, ["clips"] = host.Voices?.ClipsReady ?? 0,
                ["voiced"] = host.Voices?.Voiced ?? 0,
            };
        }

        /// <summary>The radio's state: packs, lines aired and dropped, what waits, and the last lines as the log has them.</summary>
        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            if (!Guard(out ChatterHost host, out Dictionary<string, object> failure)) return failure;
            ChatterDirector d = host.Director;
            var last = new List<object>();
            for (int i = Math.Max(0, d.Log.Count - 12); i < d.Log.Count; i++) last.Add(d.Log[i].Net + " | " + d.Log.TextAt(i));
            var nets = new Dictionary<string, object>();
            for (int i = 0; i < d.Log.Count; i++)
            {
                string net = d.Log[i].Net.ToString();
                nets[net] = nets.TryGetValue(net, out object n) ? (int)n + 1 : 1;
            }
            return new Dictionary<string, object>
            {
                ["ok"] = true, ["events"] = d.Library.Events.Count, ["exchanges"] = d.Library.Exchanges.Count, ["packErrors"] = d.Library.Errors.Count,
                ["unknown"] = d.Unknown.Count, ["aired"] = d.AiredCount, ["dropped"] = d.DroppedCount, ["pending"] = d.Pending, ["level"] = d.Level.ToString(),
                ["nets"] = nets, ["last"] = last,
            };
        }
    }
}
#endif
