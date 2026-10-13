using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Patches
{
    /// <summary>DL EVT LOG's one ledger (client-local, presentation only): every line the game adds to its message feed lands here from the patch below, with the game's own removal time.</summary>
    internal static class EventFeed
    {
        public static readonly EventLog Log = new EventLog();

        /// <summary>Keep one line: parse it (the pilot's own name and aircraft marked) and add it. A line with nothing to show (tags only) is not kept.</summary>
        public static void Capture(string rich, float arrival, float removeAt, string stamp, string ownPlayer, string ownAircraft)
        {
            EventLine line = EventParser.Parse(rich, ownPlayer, ownAircraft);
            if (line.Spans.Length > 0) Log.Add(line, rich, stamp, arrival, removeAt);
        }

        /// <summary>The pilot's name as chat writes it: <c>Player.GetDisplayName(ChatOrLeaderboard)</c> of the local player.</summary>
        public static string OwnPlayer()
        {
            try { return GameManager.GetLocalPlayer<Player>(out Player player) && player != null ? player.GetDisplayName(PlayerNameContext.ChatOrLeaderboard) : null; }
            catch (Exception) { return null; }
        }

        /// <summary>The pilot's aircraft as the kill feed writes it: its own <c>unitName</c> ("Name [Type]" for a player's).</summary>
        public static string OwnAircraft()
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            Aircraft aircraft = hud != null ? hud.aircraft : null;
            return aircraft != null ? aircraft.unitName : null;
        }
    }

    /// <summary>
    /// Captures every line the game puts in its feed. <c>MessageFeed.Enqueue(string, float)</c> is the one place they all pass: <c>MessageUI.GameMessage</c> and <c>KillFeed</c> call it once per line, and so does
    /// <c>DelayedGameMessage</c> (the game's HQ messages), which does NOT go through GameMessage; chat, join and leave messages, kill lines and the mod's own radio subtitles (WingToast.Show, which calls
    /// <c>GameplayUI.GameMessage</c>) all arrive here. <c>text</c> is one line with its rich text, <c>removeTime</c> the <c>Time.timeSinceLevelLoad</c> the game takes it off the feed at, so a row lives exactly as long
    /// as the line does. Read-only: nothing the game does is changed. Tiny, but over the 20 IL bytes Mono inlines at, so callers reach the patched body (tests/BoscaliSummer.PatchProbe pins the name; and
    /// StatusPanel fails open, leaving the game's feed alone, if lines arrive that this never saw).
    /// </summary>
    [HarmonyPatch(typeof(MessageFeed), nameof(MessageFeed.Enqueue))]
    internal static class EventFeedPatch
    {
        private static void Postfix(string text, float removeTime)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return;
                EventFeed.Capture(text, Time.timeSinceLevelLoad, removeTime, EventLog.Stamp(MissionTimeSource.Now()), EventFeed.OwnPlayer(), EventFeed.OwnAircraft());
            }
            catch (Exception e)
            {
                PatchGuard.Report("Hud.EventFeed", e);
            }
        }
    }
}
