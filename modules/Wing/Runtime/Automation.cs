using System;
using System.Collections.Generic;
using System.Globalization;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>Hooks for unattended in-game tests: nomodkit's <c>nomod sim run</c> calls them by name
    /// (<c>{"op": "call", "method": "Automation.Launch", "args": {...}}</c>).
    /// <list type="bullet">
    /// <item>Each takes and returns a <c>Dictionary&lt;string, object&gt;</c>. Numbers may arrive as doubles; a
    /// string arg naming a scenario unit also arrives as the unit itself under <c>&lt;key&gt;Unit</c>.</item>
    /// <item>A failure returns <c>{"ok": false, "error": ...}</c> and changes nothing; a returned <c>ids</c> map
    /// registers the aircraft with the harness for recording.</item>
    /// <item>Dev tooling: nothing here runs unless called, and every call is logged.</item>
    /// </list></summary>
    public static class Automation
    {
        /// <summary>Launches wingmen from a field (M3): <c>lead</c> (the anchor), <c>field</c> (an airbase name, or
        /// <c>nearest</c> to the lead), <c>count</c>, and optionally <c>type</c>, <c>shape</c>, <c>spacing</c>.</summary>
        public static Dictionary<string, object> Launch(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing?.Selection == null || SpawnService.Instance == null) return Fail("Launch", "the wing is not active (no mission, or no formations loaded)");
            if (!(Arg(args, "leadUnit") is Aircraft lead) || lead.disabled) return Fail("Launch", "'lead' does not name a live aircraft");
            AircraftDefinition type = lead.definition;
            string typeName = Text(args, "type");
            if (typeName != null && (type = FindType(typeName)) == null) return Fail("Launch", $"no aircraft type '{typeName}'");
            int count = Number(args, "count", 2);
            if (count < 1 || count > FormationCatalog.MaxSlots) return Fail("Launch", $"count must be 1 to {FormationCatalog.MaxSlots}");
            string fieldName = Text(args, "field") ?? "nearest";
            List<Airbase> fields = WingService.FriendlyFields(lead);
            Airbase field = fieldName == "nearest" ? wing.FieldFor(lead, type)
                : fields.Find(a => string.Equals(a.name, fieldName, StringComparison.OrdinalIgnoreCase));
            if (field == null) return Fail("Launch", $"no friendly field '{fieldName}'; friendly: {string.Join(", ", fields.ConvertAll(a => a.name))}");
            string shape = Text(args, "shape");
            if (shape != null && FormationCatalog.Find(WingData.Formations, shape) == null) return Fail("Launch", $"no formation '{shape}'");
            wing.SetAnchor(lead);
            if (shape != null) wing.SetShape(shape);
            string spacingName = Text(args, "spacing");
            if (spacingName != null)
            {
                if (!TryPreset(spacingName, out SpacingPreset spacing)) return Fail("Launch", $"no spacing '{spacingName}'");
                wing.SetSpacing(spacing);
            }
            // Scenarios test flight and ground behaviour: calls are free unless the scenario asks otherwise.
            bool sandbox = !args.TryGetValue("sandbox", out object free) || !(free is bool b) || b;
            int launched = SpawnService.Instance.LaunchFromField(field, type, count, sandbox);
            WingLog.Logger.LogInfo($"[Automation] Launch: {launched} × {type.unitName} from {field.name}");
            return launched > 0 ? Ok("launched", launched) : Fail("Launch", "nothing launched; see the log");
        }

        /// <summary>Ground operations so far: members still on the ground and airborne, and the ground events
        /// (relocations, reroutes, native ejections blocked).</summary>
        public static Dictionary<string, object> Ground(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Ground", "the wing is not active");
            // flying: really up (radar altitude above 30 m), not just off our ground phases (a jet left in the grass
            // after its roll once counted as airborne).
            int grounded = 0, airborne = 0, flying = 0;
            foreach (WingMember m in wing.Members)
            {
                if (m.OnGround) grounded++;
                else airborne++;
                if (!m.OnGround && m.Aircraft != null && m.Aircraft.radarAlt > 30f) flying++;
            }
            var result = new Dictionary<string, object>
            {
                { "ok", true }, { "grounded", grounded }, { "airborne", airborne }, { "flying", flying },
                { "relocated", wing.Events.CountOf(WingEventKind.Relocated) },
                { "rerouted", wing.Events.CountOf(WingEventKind.Rerouted) },
                { "rolled", wing.Events.CountOf(WingEventKind.Rolling) },
                { "liftoffs", wing.Events.CountOf(WingEventKind.Airborne) },
                { "aborted", wing.Events.CountOf(WingEventKind.DepartureAborted) },
                { "landed", wing.Events.CountOf(WingEventKind.Landed) },
                { "landing_failed", wing.Events.CountOf(WingEventKind.LandingFailed) },
                { "parked", wing.Events.CountOf(WingEventKind.Parked) },
                { "reserved", wing.Events.CountOf(WingEventKind.Reserved) },
                { "serviced", wing.Events.CountOf(WingEventKind.Serviced) },
                { "native_switches_redirected", SwitchStateGuard.Redirected },
                { "bounces_held", BounceGuard.Held },
                { "ejections_blocked", EjectGuard.Blocked },
            };
            var members = new List<object>();
            foreach (WingMember m in wing.Members)
                members.Add(new Dictionary<string, object>
                {
                    { "number", m.Number },
                    { "phase", m.Ground != null ? m.Ground.Phase.ToString() : "air" },
                    { "stop", m.Ground != null ? m.Ground.Stop.ToString() : "" },
                    { "recovery", m.Recovery != null ? m.Recovery.Phase.ToString() : "" },
                    { "x", m.Last.Pos.X }, { "z", m.Last.Pos.Z }, { "alt", m.Last.RadarAlt },
                });
            result["members"] = members;
            WingLog.Logger.LogInfo($"[Automation] Ground: {grounded} on the ground, {airborne} airborne, " +
                                  $"{result["relocated"]} relocated, {result["rerouted"]} rerouted, {EjectGuard.Blocked} ejections blocked");
            return result;
        }

        /// <summary>The wing's members as <c>{"ids": {"w2": aircraft, ...}}</c> (wingman numbers, #2 up), plus how many
        /// air-starts are still waiting to join.</summary>
        public static Dictionary<string, object> Members(Dictionary<string, object> args)
        {
            WingService wing = WingService.Instance;
            if (wing == null) return Fail("Members", "the wing is not active");
            var ids = new Dictionary<string, object>();
            foreach (WingMember m in wing.Members) ids["w" + m.Number] = m.Aircraft;
            WingLog.Logger.LogInfo($"[Automation] Members: {ids.Count} ({string.Join(", ", ids.Keys)})");
            // R5: how many members carry a LOADOUT template's stores as it would launch, and wear its airframe's saved livery.
            int matching = 0, liveryMatch = 0;
            string templateName = Text(args, "template");
            var carried = new List<string>();
            foreach (WingMember m in wing.Members)
            {
                if (m.Released || m.Aircraft == null) continue;
                AircraftDefinition def = m.Aircraft.definition;
                string token = WingLoadoutTemplates.LiveryTokenOf(def);
                if (token != null && WingLoadoutTemplates.TokenOf(m.Aircraft.NetworkLiveryKey) == token) liveryMatch++;
                if (templateName == null) continue;
                LoadoutTemplateRecord template = null;
                foreach (LoadoutTemplateRecord t in WingLoadoutTemplates.For(def))
                    if (string.Equals(t.Name, templateName, StringComparison.OrdinalIgnoreCase)) template = t;
                if (template == null) continue;
                WingLoadoutCatalog.KeysOf(m.Aircraft.Networkloadout, carried);
                bool same = true;
                for (int i = 0; i < carried.Count && same; i++)
                    if (carried[i] != null && carried[i] != template.KeyAt(i)) same = false;
                if (same && carried.Exists(k => k != null)) matching++;
            }
            return new Dictionary<string, object>
            {
                { "ok", true }, { "count", ids.Count }, { "pending", SpawnService.Instance?.Pending ?? 0 }, { "ids", ids },
                { "matching", matching }, { "livery_match", liveryMatch },
            };
        }

        /// <summary>Writes every field in use as the mod reads it (the nomodkit dump format) to <c>path</c> (default:
        /// <c>%TEMP%\wingcommand-fields.json</c>), for replay in the FlightSim.</summary>
        public static Dictionary<string, object> DumpFields(Dictionary<string, object> args)
        {
            string path = Text(args, "path") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wingcommand-fields.json");
            List<AirbaseSample> samples = FieldRegistry.Samples();
            FieldRegistry.LogStates();
            System.IO.File.WriteAllText(path, AirbaseSample.ToDumpJson(MissionManager.CurrentMission?.Name, samples));
            WingLog.Logger.LogInfo($"[Automation] DumpFields: {samples.Count} fields to {path}");
            return Ok("path", path);
        }

        private static AircraftDefinition FindType(string name)
        {
            foreach (AircraftDefinition d in Encyclopedia.i.aircraft)
                if (d != null && string.Equals(d.unitName, name, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        private static bool TryPreset(string name, out SpacingPreset preset)
        {
            foreach (SpacingPreset p in (SpacingPreset[])Enum.GetValues(typeof(SpacingPreset)))
                if (string.Equals(p.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    preset = p;
                    return true;
                }
            preset = SpacingPreset.Standard;
            return false;
        }

        private static object Arg(Dictionary<string, object> args, string key) =>
            args != null && args.TryGetValue(key, out object v) ? v : null;

        private static string Text(Dictionary<string, object> args, string key) =>
            Arg(args, key) is object v ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

        private static int Number(Dictionary<string, object> args, string key, int fallback) =>
            Arg(args, key) is object v ? (int)Math.Round(Convert.ToDouble(v, CultureInfo.InvariantCulture)) : fallback;

        private static Dictionary<string, object> Ok(string key, object value) =>
            new Dictionary<string, object> { { "ok", true }, { key, value } };

        private static Dictionary<string, object> Fail(string hook, string error)
        {
            WingLog.Logger.LogWarning($"[Automation] {hook}: {error}");
            return new Dictionary<string, object> { { "ok", false }, { "error", error } };
        }
    }
}
