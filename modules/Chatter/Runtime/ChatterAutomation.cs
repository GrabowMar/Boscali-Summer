using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Chatter;
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
