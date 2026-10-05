using System;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    internal enum SpaceNoticeKind : byte { None, Tasked, Intent }

    internal readonly struct SpaceNotice
    {
        public readonly SpaceNoticeKind Kind;
        public readonly string Text;
        public SpaceNotice(SpaceNoticeKind kind, string text) { Kind = kind; Text = text ?? ""; }
        public static readonly SpaceNotice None = new SpaceNotice(SpaceNoticeKind.None, "");
    }

    /// <summary>The words of a TASKED post notice: the call, how many targets, and who posted it (OVERLORD or an OPERATOR).</summary>
    internal static class SpaceNoticeWords
    {
        public static string Tasked(SupportActionId action, int targets, bool watchOfficer, string maker)
        {
            string label = TaskedKinds.Label(action);
            int n = Math.Max(1, targets);
            string source = watchOfficer ? "OVERLORD" : string.IsNullOrEmpty(maker) ? "OPERATOR" : "OPERATOR " + maker;
            return "TASKED: " + label + " · " + n + (n == 1 ? " TARGET" : " TARGETS") + " · " + source;
        }

        public static string Coalesced(int calls) => "TASKED: " + Math.Max(2, calls) + " NEW CALLS";
    }

    /// <summary>
    /// ENEMY INTENT words. The host takes the enemy faction's named main effort (a public map objective the AI is steered at, read
    /// through a Core contract) and says only its name: never a coordinate, never a unit, and UNKNOWN when nothing is exposed.
    /// Only an AI-led enemy has an intent to read: a faction with a human in it is steered by that human, so it is UNKNOWN.
    /// The word is FOCUSING, not attacking: the director's target may be a point it defends.
    /// </summary>
    internal static class IntentWords
    {
        public const string Unknown = "ENEMY INTENT: UNKNOWN";
        private const string Lead = "ENEMY INTENT: FOCUSING ";

        /// <summary>The words for an enemy faction with <paramref name="enemyHumans"/> humans and the director's named objective.</summary>
        public static string ForEnemy(int enemyHumans, string objectiveLabel) => enemyHumans > 0 ? Unknown : Line(objectiveLabel);

        public static string Line(string objectiveLabel)
        {
            if (string.IsNullOrWhiteSpace(objectiveLabel)) return Unknown;
            string clean = SpaceWire.Clean(objectiveLabel.Trim(), SpaceWire.MaxIntent - Lead.Length).ToUpperInvariant().Trim();
            return clean.Length == 0 ? Unknown : Lead + clean;
        }

        public static bool IsKnown(string line) => !string.IsNullOrEmpty(line) && line != Unknown;
    }

    /// <summary>
    /// What the local pilot is told, derived only from the faction mirror: a new TASKED post (not their own) gets one short line,
    /// at most one every 20 s (a burst coalesces into "N NEW CALLS"); a changed ENEMY INTENT at most once every 120 s. QUIET mode
    /// drops both and queues nothing: inbound warnings are not this class's business and stay audible elsewhere. The first sight of
    /// a mirror is silent, and a lost or switched mirror starts over (ids restart with the scene).
    /// </summary>
    internal sealed class SpaceNoticeTracker
    {
        public const float PostGapSeconds = 20f, IntentGapSeconds = 120f, ToastSeconds = 4f;
        private const int MaxPending = 9;

        private bool primed;
        private int lastSeen, pending;
        private float lastPostAt = float.NegativeInfinity, lastIntentAt = float.NegativeInfinity;
        private string lastIntent = "";
        private SupportActionId pendingAction;
        private int pendingTargets;
        private bool pendingWatchOfficer;
        private string pendingMaker = "";

        public void Reset()
        {
            primed = false; lastSeen = 0; pending = 0;
            lastPostAt = lastIntentAt = float.NegativeInfinity;
            lastIntent = ""; pendingMaker = "";
        }

        public SpaceNotice Observe(bool known, SpaceFeedState s, float now, bool quiet)
        {
            if (!known || s == null || !s.Active || !SpaceRules.MissionTime(now)) { Reset(); return SpaceNotice.None; }
            if (!primed)
            {
                primed = true;
                lastSeen = s.NewestPost;
                lastIntent = s.Intent ?? "";
                return SpaceNotice.None;
            }

            if (s.NewestPost > lastSeen)
            {
                int fresh = Math.Min(MaxPending, s.NewestPost - lastSeen);
                lastSeen = s.NewestPost;
                // Their own post is theirs: no notice for it (and the posts before it in the same step are not guessed at).
                if (!s.NewestOwn && !quiet)
                {
                    pending = Math.Min(MaxPending, pending + fresh);
                    pendingAction = s.NewestAction; pendingTargets = s.NewestTargets;
                    pendingWatchOfficer = s.NewestWatchOfficer; pendingMaker = s.NewestMaker ?? "";
                }
            }
            if (quiet) pending = 0;
            if (pending > 0 && now - lastPostAt >= PostGapSeconds)
            {
                string text = pending == 1 ? SpaceNoticeWords.Tasked(pendingAction, pendingTargets, pendingWatchOfficer, pendingMaker)
                    : SpaceNoticeWords.Coalesced(pending);
                pending = 0;
                lastPostAt = now;
                return new SpaceNotice(SpaceNoticeKind.Tasked, text);
            }

            string intent = s.Intent ?? "";
            if (intent != lastIntent && IntentWords.IsKnown(intent))
            {
                if (quiet) lastIntent = intent;
                else if (now - lastIntentAt >= IntentGapSeconds)
                {
                    lastIntent = intent;
                    lastIntentAt = now;
                    return new SpaceNotice(SpaceNoticeKind.Intent, intent);
                }
            }
            return SpaceNotice.None;
        }
    }
}
