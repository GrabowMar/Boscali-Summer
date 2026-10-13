using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// Explicit nomodkit single-player test hook for the C-key menu (called from tests/ingame/cmenu-v7.json and missile-hud.json); inert outside a sim run.
    /// Reports one category's rows exactly as the menu builds them from the live game right now.
    /// </summary>
    public static class AceRadialAutomation
    {
        /// <summary>Args: <c>category</c> (default <c>view</c>). Returns ok, category_found, available, badge, count, categories (in the whole menu) and one entry per row.</summary>
        public static Dictionary<string, object> Menu(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                return Failure("AceRadialAutomation", "Menu", "requires a nomodkit scenario");
            string id = Text(args, "category") ?? "view";
            MenuModel model;
            try { model = MenuGame.Build(); }
            catch (Exception e) { return Failure("AceRadialAutomation", "Menu", "catalog threw: " + e.Message); }
            MenuCategory category = model.Category(id);
            var entries = new List<object>();
            if (category != null)
                foreach (MenuRow row in category.Rows)
                    entries.Add(new Dictionary<string, object>
                    {
                        { "id", row.Id },
                        { "label", row.Label },
                        { "state", row.State },
                        { "blocked", row.IsBlocked },
                        { "hold", row.NeedsHold },
                    });
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "category_found", category != null },
                { "available", category != null && category.Available },
                { "badge", category?.Badge ?? "" },
                { "count", entries.Count },
                { "categories", model.Categories.Count },
                { "entries", entries },
            };
            Debug.Log("[AceRadialAutomation] Menu: " + id + " rows=" + entries.Count);
            return result;
        }

        private static int chaffHeard, chaffDazzled, threatsGone;
        private static float threatMiss = -1f;
        private static bool listening;

        /// <summary>
        /// The chaff dispenser on the seated player (tests/ingame/chaff.json). Args: <c>action</c> = <c>status</c> (default), <c>pop</c> (one salvo), or
        /// <c>threat</c> (an enemy radar missile fired from <c>range</c> metres on the player's beam; needs one hostile aircraft in the mission).
        /// Returns fitted, ammo, max, bundles (chaff bundles alive in the scene), heard / dazzled (times a seeker reported a bundle, and reported itself
        /// jammed by one: the game's own log line), threatsGone and miss (metres between the last threat and the player when it ended).
        /// </summary>
        public static Dictionary<string, object> Chaff(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")))
                return Failure("AceRadialAutomation", "Chaff", "requires a nomodkit scenario");
            ChaffDispenser chaff = ChaffDispenser.Instance;
            if (chaff == null) return Failure("AceRadialAutomation", "Chaff", "no dispenser service");
            if (!listening)
            {
                listening = true;
                // both radar seekers log every bundle they weigh: "Target chaff angle: .. success True"
                Application.logMessageReceived += (text, _, __) =>
                {
                    if (text == null || !text.StartsWith("Target chaff angle", StringComparison.Ordinal)) return;
                    chaffHeard++;
                    if (text.EndsWith("True", StringComparison.Ordinal)) chaffDazzled++;
                };
            }
            string action = Text(args, "action") ?? "status";
            if (action == "pop") chaff.Pop();
            if (action == "threat")
            {
                string error = Threat(Number(args, "range", 6000f));
                if (error != null) return Failure("AceRadialAutomation", "Chaff", error);
            }
            return new Dictionary<string, object>
            {
                { "ok", true },
                { "fitted", chaff.Fitted },
                { "ammo", chaff.Ammo },
                { "max", chaff.MaxAmmo },
                { "linked", chaff.Linked },
                { "bundles", UnityEngine.Object.FindObjectsOfType<RadarChaff>().Length },
                { "heard", chaffHeard },
                { "dazzled", chaffDazzled },
                { "threatsGone", threatsGone },
                { "miss", threatMiss },
            };
        }

        private static string Threat(float range)
        {
            if (!GameManager.GetLocalAircraft(out Aircraft player) || player == null) return "no seated player";
            Aircraft bandit = null;
            foreach (Aircraft a in UnitRegistry.allAircraft)
                if (a != null && !a.disabled && a.NetworkHQ != null && a.NetworkHQ != player.NetworkHQ) { bandit = a; break; }
            if (bandit == null) return "no hostile aircraft to own the missile";
            MissileDefinition radar = null;
            foreach (MissileDefinition m in Resources.FindObjectsOfTypeAll<MissileDefinition>())
                if (m != null && m.unitPrefab != null && m.jsonKey != null && m.jsonKey.StartsWith("AAM", StringComparison.Ordinal) && m.unitPrefab.GetComponent<ARHSeeker>() != null) { radar = m; break; }
            if (radar == null) return "no radar air-to-air missile";
            Transform t = player.transform;
            Vector3 from = t.position + t.right * range + Vector3.up * 100f;      // on the beam: where chaff counts
            Missile shot = NetworkSceneSingleton<Spawner>.i.SpawnMissile(radar, from, Quaternion.LookRotation(t.position - from), (t.position - from).normalized * 600f, player, bandit);
            shot.onDisableUnit += _ =>
            {
                threatsGone++;
                if (player != null) threatMiss = FastMath.Distance(shot.GlobalPosition(), player.GlobalPosition());
            };
            return null;
        }
    }
}
