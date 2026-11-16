using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Command.Runtime
{
#if !PUBLIC_RELEASE
    /// <summary>
    /// Sim readback for EVN LOG: what the game log holds, and LOCATE on its newest placed line through the same
    /// <see cref="IMapProjection.Focus"/> the EVN button calls (<c>offset</c> on the next read: how far that point sits from the map's centre,
    /// a fraction of its height). <c>{action: kill, unit: id}</c> wrecks a spawned unit through its parts, so the game writes a real kill line
    /// with a place. Refuses outside a nomodkit scenario; dev tooling, compiled out of public releases.
    /// </summary>
    public static class TacticalLogProbe
    {
        private static bool InSimRun => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO"));
        private static float locatedX = float.NaN, locatedZ = float.NaN;

        /// <summary>{action: read (default) | locate | kill, unit}: lines, placed, a count per category, newest, offset, and the map's
        /// field log (shown, text).</summary>
        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (!InSimRun) return Failure("TacticalLogProbe", "Step", "requires a nomodkit scenario run");
            string action = Text(args, "action") ?? "read";
            if (action == "kill")
            {
                if (!(Arg(args, "unitUnit") is Unit unit) || unit == null) return Failure("TacticalLogProbe", "kill", "no such unit");
                // The seat's aircraft takes the credit, so the game's kill feed filter shows the line whatever its settings.
                PersistentID by = GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player) && player?.Aircraft != null
                    ? player.Aircraft.persistentID : PersistentID.None;
                foreach (UnitPart part in unit.GetComponentsInChildren<UnitPart>()) part.TakeDamage(0f, 0f, 1f, 0f, 100000f, by);
                bool wrecked = unit.disabled;
                if (!wrecked) unit.ReportKilled();   // still flying on dead parts: the game's own kill report writes the line now
                return new Dictionary<string, object> { { "ok", true }, { "wrecked", wrecked } };
            }

            // A typed line (category, text), for looks: the feed patch that files real lines is proven by kill.
            if (action == "add") TacticalLog.Add(Text(args, "category") ?? TacticalLogEntry.News, Text(args, "text") ?? "");
            if (!ModuleServices.TryGet(out ITacticalLog log)) return Failure("TacticalLogProbe", action, "no game log");
            var reply = new Dictionary<string, object> { { "ok", true }, { "lines", log.Entries.Count } };
            foreach (string category in TacticalLogEntry.Categories) reply[category] = 0;
            int placed = 0;
            TacticalLogEntry first = null;
            foreach (TacticalLogEntry entry in log.Entries)
            {
                reply[entry.Category] = (int)reply[entry.Category] + 1;
                if (!entry.HasPlace) continue;
                placed++;
                if (first == null) first = entry;
            }
            reply["placed"] = placed;
            reply["newest"] = log.Entries.Count > 0 ? log.Entries[0].Category + " " + log.Entries[0].Text : "";
            reply["offset"] = float.IsNaN(locatedX) ? -1f : MapFocus.CentreOffset(locatedX, locatedZ);
            reply["evnHeight"] = NOAvionics.AvConsole.Find("EVN")?.Root.rect.height ?? -1f;   // the left bay, under the ticker
            reply["fieldLog"] = MfdFieldLog.Shown;   // the log above the open panel column and what it prints
            reply["fieldLogText"] = MfdFieldLog.BodyText ?? "";
            if (action != "locate") return reply;

            if (first == null) return Failure("TacticalLogProbe", "locate", "no line with a place");
            reply["flew"] = ModuleServices.TryGet(out IMapProjection map) && map.Focus(first.X, first.Z);
            reply["located"] = first.Category + " " + first.Text;
            locatedX = first.X;
            locatedZ = first.Z;
            return reply;
        }
    }
#endif
}
