using System;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.TheaterOps.Configuration
{
    /// <summary>
    /// The two things a host decides about the staff while a mission runs, declared beside the settings they write so PatchProbe can check
    /// the table without the module's managers. This is the one THEATER OPS table: HostSettingsTable.Toggle throws on a duplicate id.
    /// </summary>
    internal static class TheaterOpsHostSettings
    {
        internal const string RtsOwnsOrders = "GROUND CONTROL RTS OWNS GROUND ORDERS";

        internal static HostSettingsTable Build(TheaterOpsSettings settings, Func<bool> rtsOwnsOrders) =>
            new HostSettingsTable("THEATER OPS")
                .Toggle(1, settings.FrontlineTacticsEnabled, "FRONTLINE TACTICS",
                    "AI ground groups follow the staff's operation: they form up behind the line, then advance. Off: vehicles keep the game's own orders; the staff still plans.",
                    () => rtsOwnsOrders() ? RtsOwnsOrders : null)
                .Toggle(2, settings.AiStaffConvoys, "AI STAFF CONVOYS",
                    "Not active in this build: the reinforcement desk that would spend it is not built, so this switch changes nothing. Planned: AI-only factions buy one combat convoy every 2 minutes while their operation runs, and only when their pool holds at least twice the price. Off (default).");
    }
}
