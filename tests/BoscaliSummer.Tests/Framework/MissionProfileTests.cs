using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;

namespace BoscaliSummer.Tests.Framework
{
    internal static class MissionProfileTests
    {
        public static void Run()
        {
            TestAssert.That(!MissionProfile.FullExperience, "mission overhaul must start disabled before a mission is identified");
            const string marker = "Full Boscali Summer experience enabled for this dedicated Escalation scenario.";
            string special = "Escalation copy with the full Boscali Summer experience.\n\n" + marker;
            TestAssert.Eq(MissionProfile.DescriptionMarker, marker, "mission marker is the stable opt-in identity");
            TestAssert.That(MissionProfile.IsFullMission(special), "dedicated mission must enable the overhaul");
            foreach (string description in new[]
            {
                null, "", "Escalation", "Boscali Summer", "Workshop mission named Boscali Summer",
                marker, "Mission text\n" + marker, "Mission text\n\n" + marker.ToLowerInvariant(),
                special + "\n", special + " edited text", "Mission text mentioning " + marker + " mid-paragraph"
            })
                TestAssert.That(!MissionProfile.IsFullMission(description),
                    "a vanilla/workshop name or incidental/edited marker must not opt into mission changes: " + description);

            foreach (string id in new[]
            {
                "mission-profile", "session", "hud", "radio", "performance", "immersion", "effects", "qol",
                "autopilot", "chatter", "wing", "command", "comms", "cinematography"
            })
                TestAssert.That(MissionProfile.AllowsModule(id, false) && MissionProfile.AllowsModule(id, true),
                    "common module must stay available in every mission: " + id);
            foreach (string id in new[]
            {
                "fire-and-destruction", "urban-combat", "player-spawn-priority", "squad", "progression", "support",
                "dynamic-operations", "high-command", "intel", "theater-ops", "air-survival", "trenches", "events",
                "weather", "vanguard", "strategic-command"
            })
                TestAssert.That(!MissionProfile.AllowsModule(id, false) && MissionProfile.AllowsModule(id, true),
                    "mission-changing module must require the dedicated mission: " + id);
            foreach (string id in new[] { null, "", "unknown-module", "STR", "Support", "support " })
                TestAssert.That(!MissionProfile.AllowsModule(id, false) && !MissionProfile.AllowsModule(id, true),
                    "unclassified or mistyped module must fail closed: " + id);

            foreach (string key in new[] { "VG_Pod", "VG_MaldxInfo", "AB_Paratroopers_x16", "BS_ContainerFeeder" })
                TestAssert.That(MissionProfile.IsBoscaliContent(key),
                    "Boscali mission-only assets must be recognized for native content filtering: " + key);
            foreach (string key in new[]
            {
                null, "", "Aircraft", "Paratroopers_x16", "AB_Paratroopers_x160", "BS_ContainerFeederOther",
                "BS_NativeDefinition", "vg_Pod", "My_VG_Pod", "VG"
            })
                TestAssert.That(!MissionProfile.IsBoscaliContent(key),
                    "content filtering must preserve native and unrelated workshop definitions: " + key);

            var transitions = new List<bool>();
            Action<bool> changed = full =>
            {
                TestAssert.Eq(MissionProfile.FullExperience, full, "subscribers must observe the new profile before they change module scope");
                transitions.Add(full);
            };
            MissionProfile.Changed += changed;
            try
            {
                MissionProfile.SetDescription("Escalation");
                MissionProfile.SetDescription("Boscali Summer");
                TestAssert.That(transitions.Count == 0 && !MissionProfile.FullExperience,
                    "vanilla and same-named workshop missions must keep the common profile without redundant notifications");
                MissionProfile.SetDescription(special);
                MissionProfile.SetDescription(special);
                TestAssert.That(MissionProfile.FullExperience && transitions.Count == 1 && transitions[0],
                    "entering/restarting the dedicated mission must enable full features once");
                MissionProfile.SetDescription("Workshop mission named Boscali Summer");
                TestAssert.That(!MissionProfile.FullExperience && transitions.Count == 2 && !transitions[1],
                    "switching to workshop missions must remove the full experience");
                MissionProfile.SetDescription(special);
                MissionProfile.SetDescription(null);
                MissionProfile.SetDescription(null);
                TestAssert.That(!MissionProfile.FullExperience && transitions.Count == 4 && transitions[2] && !transitions[3],
                    "returning to the lobby/disconnecting must disable full features once");
            }
            finally
            {
                MissionProfile.Changed -= changed;
                MissionProfile.SetDescription(null);
            }

            // Reusing the same native map may skip sceneLoaded, even when the profile stays full.
            var lifecycle = new List<string>();
            Action<bool> profileChanged = full => lifecycle.Add(full ? "profile-full" : "profile-common");
            Action preparing = () => lifecycle.Add(MissionProfile.FullExperience ? "reset-full" : "reset-common");
            MissionProfile.Changed += profileChanged;
            MissionProfile.Preparing += preparing;
            try
            {
                MissionProfile.PrepareMission(special);
                MissionProfile.PrepareMission(special);
                MissionProfile.PrepareMission("Escalation");
                MissionProfile.PrepareMission("Workshop mission named Boscali Summer");
                MissionProfile.PrepareMission(special);
                TestAssert.Eq(string.Join(",", lifecycle),
                    "profile-full,reset-full,reset-full,profile-common,reset-common,reset-common,profile-full,reset-full",
                    "every mission preparation must reset services after selecting module scope, including same-map/same-profile reloads");
            }
            finally
            {
                MissionProfile.Changed -= profileChanged;
                MissionProfile.Preparing -= preparing;
                MissionProfile.SetDescription(null);
            }
        }
    }
}
