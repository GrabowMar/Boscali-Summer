using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The one game log (<see cref="ITacticalLog"/>): every line the game puts in its message feed, caught where they all pass
    /// (<see cref="TacticalLogFeedPatch"/>), what the radio aired, and the wire's theater headlines. Client-local and presentation only; it
    /// keeps the mission's last lines for EVN LOG, in flight and on the map alike. A kill's line takes its category and place from the
    /// kill message it is written inside (<see cref="TacticalLogKillPatch"/>).
    /// </summary>
    internal sealed class TacticalLog : ITacticalLog
    {
        private const int MaximumEntries = 120;
        private static readonly List<TacticalLogEntry> entries = new List<TacticalLogEntry>(MaximumEntries + 1);
        private static int revision;
        private static string killCategory;
        private static bool killPlaced;
        private static Vector3 killAt;

        /// <summary>The game's own lines as it wrote them (rich text), for the news ticker's headline reader. Radio lines are not offered.</summary>
        internal static event Action<string> GameLine;

        public IReadOnlyList<TacticalLogEntry> Entries => entries;
        public int Revision => revision;
        public void Radio(string text) => Add(TacticalLogEntry.RadioTraffic, text);

        /// <summary>One line of the game's feed.</summary>
        internal static void Game(string rich)
        {
            string plain = MfdNewsFeed.CleanTags(rich).Trim();
            if (plain.Length == 0) return;
            Add(killCategory ?? Classify(plain), plain, killPlaced, killAt.x, killAt.z);
            EndKill();
            GameLine?.Invoke(rich);
        }

        internal static void Add(string category, string text, bool placed = false, float x = 0f, float z = 0f)
        {
            text = MfdNewsFeed.CleanTags(text).Trim();
            if (text.Length == 0) return;
            string stamp = MissionTimeSource.TryNow(out float time) ? AvNum.Clock(time) : "--:--";
            entries.Insert(0, new TacticalLogEntry(stamp, category, text, placed, x, z));
            if (entries.Count > MaximumEntries) entries.RemoveAt(entries.Count - 1);
            revision++;
        }

        /// <summary>A kill message is being written: AIR for aircraft and missiles, GROUND for the rest, and the victim's place (own side live,
        /// another side only as this side knows it).</summary>
        internal static void BeginKill(PersistentID killedId, KillType type)
        {
            killCategory = type == KillType.Aircraft || type == KillType.Missile ? TacticalLogEntry.Air : TacticalLogEntry.Ground;
            GameManager.GetLocalHQ(out FactionHQ hq);
            killPlaced = UnitRegistry.TryGetPersistentUnit(killedId, out PersistentUnit victim) && victim.unit != null &&
                         MapUiAccess.TryPosition(victim.unit, hq, out killAt);
        }

        internal static void EndKill()
        {
            killCategory = null;
            killPlaced = false;
        }

        internal static void Reset()
        {
            entries.Clear();
            EndKill();
            revision++;
        }

        private static readonly string[] AirWords = { " shot down ", " intercepted ", " crashed", " was rescued by ", " was captured by " };
        private static readonly string[] GroundWords = { " destroyed", " demolished ", " sank", " collapsed" };

        /// <summary>A line not written inside a kill message: "name: words" is somebody talking (RADIO); the game's kill and aircrew verbs
        /// (KillTypeExtensions.GetVerb, the pilot capture message) are AIR or GROUND; the rest is the game's news.</summary>
        private static string Classify(string plain)
        {
            int colon = plain.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0 && colon < 40) return TacticalLogEntry.RadioTraffic;
            if (Has(plain, AirWords)) return TacticalLogEntry.Air;
            return Has(plain, GroundWords) ? TacticalLogEntry.Ground : TacticalLogEntry.News;
        }

        private static bool Has(string line, string[] words)
        {
            foreach (string word in words)
                if (line.IndexOf(word, StringComparison.OrdinalIgnoreCase) > 0) return true;
            return false;
        }
    }

    /// <summary><c>MessageFeed.Enqueue</c> is where every line of the game's feed passes (game messages, HQ messages, chat, kill lines);
    /// read-only, the game's feed is untouched.</summary>
    [HarmonyPatch(typeof(MessageFeed), nameof(MessageFeed.Enqueue))]
    internal static class TacticalLogFeedPatch
    {
        private static void Postfix(string text)
        {
            try
            {
                if (!string.IsNullOrEmpty(text)) TacticalLog.Game(text);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Command.TacticalLog", e);
            }
        }
    }

    /// <summary>The kill message's Mirage RPC body (<c>UserCode_RpcKillMessage_*</c>, found by prefix so a new hash suffix does not break it)
    /// writes its line synchronously; the prefix hands that line the victim's category and place, the postfix clears them.</summary>
    [HarmonyPatch]
    internal static class TacticalLogKillPatch
    {
        private static MethodBase TargetMethod()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(MessageManager)))
                if (method.Name.StartsWith("UserCode_RpcKillMessage_", StringComparison.Ordinal)) return method;
            return null;
        }

        // After Chatter's ChatterKillPatch and Wing's kill prefixes: the scope opened here tags the next feed line, which must be the kill's.
        [HarmonyAfter("com.marci.boscalisummer.module.chatter", "com.marci.boscalisummer.module.wing")]
        private static void Prefix(PersistentID killedID, KillType killedType)
        {
            try
            {
                TacticalLog.BeginKill(killedID, killedType);
            }
            catch (Exception e)
            {
                TacticalLog.EndKill();
                PatchGuard.Report("Command.TacticalLogKill", e);
            }
        }

        private static void Postfix() => TacticalLog.EndKill();
    }
}
