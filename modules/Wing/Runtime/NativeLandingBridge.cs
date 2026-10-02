using System.Collections.Generic;
using HarmonyLib;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>Hands a recovering member to the game's own landing (spec M3 §4, native §B3/§B6) and back. Planes get
    /// <c>AIPilotLandingState</c>, helicopters and tiltwings <c>AIHeloLandingState</c>, created as the game does when
    /// missing. The <see cref="SwitchStateGuard"/> turns every way out of those states into ours.</summary>
    internal static class NativeLandingBridge
    {
        // The jet airbase and helo pad resolve once in GameAccess; only the nearest
        // fallback below is read here.
        private static readonly AccessTools.FieldRef<PilotBaseState, Airbase> NearestField =
            AccessTools.FieldRefAccess<PilotBaseState, Airbase>("nearestAirbase");

        private static readonly System.Reflection.FieldInfo ModeField = AccessTools.Field(typeof(AIPilotLandingState), "landingMode");

        /// <summary>The game's landing mode (its private enum, by name) for the diagnostics; "" when not in the jet landing.</summary>
        public static string Mode(Pilot p) =>
            p != null && p.currentState is AIPilotLandingState s && ModeField != null ? ModeField.GetValue(s)?.ToString() ?? "" : "";

        public static bool Begin(WingMember m)
        {
            Pilot p = m.Pilot;
            PilotBaseState landing;
            if (p.pilotType == Pilot.PilotType.Plane) landing = p.AILandingState ?? (p.AILandingState = new AIPilotLandingState());
            else if (p.pilotType == Pilot.PilotType.Helo || p.pilotType == Pilot.PilotType.Tiltwing)
                landing = p.AIHeloLandingState ?? (p.AIHeloLandingState = new AIHeloLandingState());
            else return false;
            p.SwitchState(landing);
            return ReferenceEquals(p.currentState, landing);
        }

        /// <summary>The pilot is flying one of the game's landing states.</summary>
        public static bool Landing(Pilot p) =>
            p != null && p.currentState != null &&
            (ReferenceEquals(p.currentState, p.AILandingState) || ReferenceEquals(p.currentState, p.AIHeloLandingState));

        /// <summary>Takes the member back from the game's landing (it is overdue): our state flies it again.</summary>
        public static void TakeBack(WingMember m) => m.Pilot.SwitchState(m.State);

        /// <summary>The field the game's landing state picked for the pilot (null: none, or not landing).</summary>
        public static Airbase Field(Pilot p)
        {
            if (p == null) return null;
            if (p.AILandingState != null && ReferenceEquals(p.currentState, p.AILandingState))
                return GameAccess.TryGetLandingAirbase(p.AILandingState, out Airbase field) ? field : null;
            if (p.AIHeloLandingState != null && ReferenceEquals(p.currentState, p.AIHeloLandingState)) return NearestField(p.AIHeloLandingState);
            return null;
        }

        /// <summary>Out of the helicopter pad's landing queue (review M3b I4): the game only drops entries that became null,
        /// so an aircraft that landed and left, or gave up, would hold the pad for everyone for the rest of the mission.
        /// The queue keeps its order.</summary>
        public static void LeavePad(Pilot p, Aircraft a)
        {
            Airbase.VerticalLandingPoint pad = p?.AIHeloLandingState != null &&
                GameAccess.TryGetLandingPad(p.AIHeloLandingState, out Airbase.VerticalLandingPoint picked) ? picked : null;
            Queue<Aircraft> queue = pad?.GetLandingQueue();
            if (queue == null) return;
            for (int i = queue.Count; i > 0; i--)
            {
                Aircraft x = queue.Dequeue();
                if (!ReferenceEquals(x, a)) queue.Enqueue(x);
            }
        }

        /// <summary>Off the runway's landing list (the game leaves it there when it hands over at low speed, native §B3),
        /// so native departures are not held by a landing that is over.</summary>
        public static void Deregister(Airbase airbase, Aircraft a)
        {
            if (airbase?.runways == null) return;
            foreach (Airbase.Runway r in airbase.runways)
                if (r != null) r.DeregisterLanding(a);
        }
    }
}
