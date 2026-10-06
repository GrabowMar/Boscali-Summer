using System.Collections.Generic;
using UnityEngine;

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
    /// <summary>Single entry point other modules use to reach Personnel. Nested classes mirror the
    /// internal subsystem they forward to one-for-one (no renaming, no behavior change) so Personnel's
    /// internals can be restructured without touching callers in Core/Flight/Combat/Economy/Comms/Ui.</summary>
    internal static class PersonnelFacade
    {
        internal static class Roster
        {
            public static void Reset() => WingPilotRoster.Reset();
            public static WingPilot Of(Aircraft aircraft) => WingPilotRoster.Of(aircraft);
            public static WingPilot Of(WingMember member) => WingPilotRoster.Of(member);
            public static WingPilot Assign(Aircraft aircraft, WingPilot preferred = null) =>
                WingPilotRoster.Assign(aircraft, preferred);
            public static WingPilot Selected => WingPilotRoster.Selected;
            public static void Select(WingPilot pilot) => WingPilotRoster.Select(pilot);
            public static bool Contains(WingPilot pilot) => WingPilotRoster.Contains(pilot);
        }




        internal static class SearchAndRescue
        {
            public static void Tick() => WingSearchAndRescue.Tick();
            public static WingPilot PilotOf(PilotDismounted native) => WingSearchAndRescue.PilotOf(native);
            public static void CollectDowned(List<Unit> into) => WingSearchAndRescue.CollectDowned(into);
            public static string Status(WingPilot pilot) => WingSearchAndRescue.Status(pilot);
        }

        internal static class Portraits
        {
            public static void Reset() => PilotPortrait.Reset();
            public static Sprite Sprite => PilotPortrait.Sprite;
            public static Sprite For(WingPilot pilot) => PilotPortrait.For(pilot);
            public static Sprite ForSelection(PortraitSelection selection) => PilotPortrait.ForSelection(selection);
        }

        internal static class KillCredit
        {
            public static void Reset() => WingKillCredit.Reset();
            public static void Tick() => WingKillCredit.Tick();
        }


    }
}
