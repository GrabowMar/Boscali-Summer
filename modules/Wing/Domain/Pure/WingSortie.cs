using System;
using System.Globalization;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>The existing wing action a sortie intent maps to.</summary>
    internal enum SortieAction : byte { None, Attack, Cap, Scout }

    /// <summary>Pure SEND WING rules: the action table, the numbers, and every refusal and status word.</summary>
    internal static class WingSortieRules
    {
        /// <summary>Radius of the cover circle and of the CAP guard, metres: above the wing's orbit (about 8 km at the 200 m/s default cruise, 13 km at 250 m/s), inside the area guard's 2 000..40 000. Calibrated by the sim.</summary>
        public const float CoverRadius = 15000f;

        /// <summary>Share of the combat leash within which a tracked target may be attacked.</summary>
        public const float ReachShare = 0.9f;

        public const string HostOnly = "Wing orders are host-only: ask the host to send theirs, or fly this sortie yourself.";
        public const string NoTask = "No wing task fits this sortie: fly it yourself.";
        public const string NotReady = "Wing Command is not ready: wait for the mission to start.";
        public const string NotFlying = "You are not flying: the wing follows your aircraft.";
        public const string NoWing = "No wing: recruit wingmen first (C-key, WING).";
        public const string NoneFlying = "Your wingmen are on the ground or going home: take off or rejoin first.";
        public const string NotAccepted = "Accept this sortie first: the wing counts only on a sortie you hold.";
        public const string NoPosition = "This sortie has no marked position (contact lost): get eyes on it first.";
        public const string TargetDown = "Its target is already down.";
        public const string NotHostile = "Its target is not hostile any more: the card will end.";
        public const string NoWeapon = "No wingman has a weapon that fits it from here: check the wing's stores and doctrine.";
        public const string NotTracked = "NOT TRACKED YET - WING SCOUTING. SEND AGAIN WHEN IT SHOWS.";

        private const float MaximumKm = 99999f;

        /// <summary>The wing action for an intent; a Destroy target the faction does not track is scouted, not attacked.</summary>
        public static SortieAction ActionFor(WingSortieIntent intent, bool targetTracked)
        {
            switch (intent)
            {
                case WingSortieIntent.Destroy: return targetTracked ? SortieAction.Attack : SortieAction.Scout;
                case WingSortieIntent.Cover: return SortieAction.Cap;
                case WingSortieIntent.Observe: return SortieAction.Scout;
                default: return SortieAction.None;
            }
        }

        /// <summary>Metres within which a tracked target may be attacked: ReachShare x the combat leash. A leash that cannot be read gives 0 (nothing in reach), never an unlimited reach.</summary>
        public static float AttackReach(float leashMetres) =>
            float.IsFinite(leashMetres) && leashMetres > 0f ? ReachShare * leashMetres : 0f;

        /// <summary>"Target is 34 km away: the wing will not follow past 18 km. Close in first." (whole kilometres; a dash for a distance that cannot be read).</summary>
        public static string TooFar(float distanceMetres, float reachMetres) =>
            "Target is " + Km(distanceMetres) + " km away: the wing will not follow past " + Km(reachMetres) + " km. Close in first.";

        private static string Km(float metres) =>
            !float.IsFinite(metres) || metres < 0f ? "-" :
            ((int)Math.Round(Math.Min(metres / 1000f, MaximumKm), MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

        /// <summary>The status line for a card, at most 40 characters; empty for state None. A negative wingman count is unknown and prints no count.</summary>
        public static string Words(WingSortieState state, WingSortieIntent intent, int wingmen)
        {
            string flying = wingmen < 0 ? "" : " · " + wingmen.ToString(CultureInfo.InvariantCulture) + " FLYING";
            switch (state)
            {
                case WingSortieState.Tasked: return "WING TASKED" + flying;
                case WingSortieState.OnStation: return intent == WingSortieIntent.Destroy ? "WING ATTACKING · TARGET STILL UP" : "WING ON STATION" + flying;
                case WingSortieState.Done: return intent == WingSortieIntent.Destroy ? "WING: TARGET DOWN · FORMING UP" : "WING: SORTIE OVER · FORMING UP";
                case WingSortieState.Failed: return "WING: NONE LEFT FLYING THIS";
                case WingSortieState.Recalled: return "WING RECALLED";
                default: return "";
            }
        }
    }

    /// <summary>The fold of one sortie's tasking, stepped at 1 Hz. No allocation after construction.</summary>
    internal sealed class WingSortieTally
    {
        private float deadline;

        /// <summary>A tasking has been begun and not cleared. It may have Ended; its numbers stay readable.</summary>
        public bool Active { get; private set; }
        public int SortieId { get; private set; }
        public WingSortieIntent Intent { get; private set; }
        public WingSortieState State { get; private set; }
        /// <summary>A Destroy target was seen gone.</summary>
        public bool TargetDown { get; private set; }
        /// <summary>Wingmen flying it at the last step; -1 (unknown) until the first Step.</summary>
        public int Wingmen { get; private set; }
        /// <summary>The tasking has ended (Done, Failed or Recalled); its numbers stay readable.</summary>
        public bool Ended { get; private set; }

        /// <summary>Start a tasking at state Tasked, replacing any earlier one. deadline is on the wing clock; 0 (or not a positive number) = none.</summary>
        public void Begin(int sortieId, WingSortieIntent intent, float deadline)
        {
            Active = true; SortieId = sortieId; Intent = intent; State = WingSortieState.Tasked;
            TargetDown = false; Ended = false; Wingmen = -1;
            this.deadline = deadline > 0f ? deadline : 0f;
        }

        /// <summary>One 1 Hz step: Done when a Destroy target is gone or the deadline passed; Failed when no wingman flies; else OnStation or Tasked. An ended tasking never changes.</summary>
        public WingSortieState Step(float now, int wingmen, bool targetGone, bool onStation)
        {
            if (!Active || Ended) return State;
            Wingmen = wingmen < 0 ? 0 : wingmen;
            if (Intent == WingSortieIntent.Destroy && targetGone) { TargetDown = true; return Finish(WingSortieState.Done); }
            if (deadline > 0f && now >= deadline) return Finish(WingSortieState.Done);
            if (Wingmen == 0) return Finish(WingSortieState.Failed);
            State = onStation ? WingSortieState.OnStation : WingSortieState.Tasked;
            return State;
        }

        /// <summary>End the tasking as Done, Failed or Recalled. True only on the call that ended it, so the caller issues FormUp exactly once.</summary>
        public bool End(WingSortieState final)
        {
            if (!Active || Ended) return false;
            if (final != WingSortieState.Done && final != WingSortieState.Failed && final != WingSortieState.Recalled) return false;
            Finish(final);
            return true;
        }

        /// <summary>Forget everything (mission start and end).</summary>
        public void Clear()
        {
            Active = false; SortieId = 0; Intent = WingSortieIntent.None; State = WingSortieState.None;
            TargetDown = false; Ended = false; Wingmen = 0; deadline = 0f;
        }

        private WingSortieState Finish(WingSortieState final)
        {
            State = final;
            Ended = true;
            return final;
        }
    }
}
