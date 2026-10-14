using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;

namespace BoscaliSummer.Tests.Framework
{
    internal static class MissionProfileTests
    {
        public static void Run()
        {
            foreach (string name in new[] { "Free Flight", "  free flight  ", "FREE FLIGHT", null, "", "  " })
                TestAssert.That(!MissionProfile.AllowsFortifications(name), "Free Flight or an unloaded mission cannot fortify: " + name);
            foreach (string name in new[] { "Escalation", "Boscali Summer", "Free Flight 2", "Workshop Free Flight" })
                TestAssert.That(MissionProfile.AllowsFortifications(name), "other named missions retain fortification: " + name);
            string[] optional =
            {
                "radio", "immersion", "effects", "qol", "autopilot", "chatter", "wing", "command", "comms",
                "cinematography", "fire-and-destruction", "urban-combat", "player-spawn-priority", "squad",
                "progression", "support", "dynamic-operations", "high-command", "intel", "theater-ops",
                "air-survival", "trenches", "events", "weather", "vanguard", "strategic-command"
            };
            string[] core = { "mission-profile", "session", "hud", "performance" };
            foreach (string id in optional)
                TestAssert.That(MissionProfile.AllowsModule(id) && !MissionProfile.IsCore(id),
                    "nothing is locked until the player switches it off: " + id);
            TestAssert.That(!MissionProfile.AllowsModule(null), "a null module id is never allowed");
            foreach (string id in new[] { "wing", "command", "radio", "immersion", "autopilot" })
                TestAssert.That(!MissionProfile.IsBattlefield(id), "the immersion-only preset must keep cockpit systems: " + id);
            foreach (string id in new[] { "support", "strategic-command", "weather", "theater-ops", "vanguard" })
                TestAssert.That(MissionProfile.IsBattlefield(id), "the immersion-only preset must drop mission-changing systems: " + id);

            int changes = 0;
            Action changed = () => changes++;
            MissionProfile.Changed += changed;
            try
            {
                MissionProfile.SetDisabled(MissionProfile.Parse("support, weather,session"));
                TestAssert.That(changes == 1 && !MissionProfile.AllowsModule("support") && !MissionProfile.AllowsModule("weather"),
                    "the player's choice switches exactly those modules off");
                TestAssert.That(MissionProfile.AllowsModule("wing") && MissionProfile.AllowsModule("progression"),
                    "modules the player left on stay on");
                foreach (string id in core)
                    TestAssert.That(MissionProfile.AllowsModule(id), "core plumbing cannot be switched off: " + id);
                TestAssert.That(!MissionProfile.BlocksContent("AB_Paratroopers_x16") && !MissionProfile.BlocksContent("VG_Pod"),
                    "Boscali content stays while its owning module is on");
                MissionProfile.SetDisabled(new[] { "weather", "support" });
                TestAssert.Eq(changes, 1, "the same selection must not reload modules");
                MissionProfile.SetDisabled(new[] { "vanguard", "urban-combat" });
                TestAssert.That(MissionProfile.BlocksContent("VG_Pod") && MissionProfile.BlocksContent("BS_ContainerFeeder") &&
                    MissionProfile.BlocksContent("AB_Paratroopers_x16"), "content follows its owning module's switch");
                foreach (string key in new[] { null, "", "Aircraft", "Paratroopers_x16", "AB_Paratroopers_x160", "vg_Pod", "My_VG_Pod" })
                    TestAssert.That(!MissionProfile.BlocksContent(key), "native and workshop content is never filtered: " + key);
                MissionProfile.SetDisabled(null);
                TestAssert.That(changes == 3 && MissionProfile.AllowsModule("vanguard"), "an empty selection runs everything");
            }
            finally
            {
                MissionProfile.Changed -= changed;
                MissionProfile.SetDisabled(null);
            }

            TestAssert.Eq(MissionProfile.Format(new[] { "weather", "support" }), "support,weather", "the saved list is stable");
            TestAssert.Eq(MissionProfile.Parse(null).Length + MissionProfile.Parse(" , ").Length, 0, "an empty saved list switches nothing off");

            var lifecycle = new List<string>();
            Action preparing = () => lifecycle.Add("reset");
            MissionProfile.Preparing += preparing;
            try
            {
                MissionProfile.PrepareMission();
                MissionProfile.PrepareMission();
                TestAssert.Eq(lifecycle.Count, 2, "every mission preparation resets scene services, including same-map reloads");
            }
            finally { MissionProfile.Preparing -= preparing; }
        }
    }
}
