using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Explicit nomodkit single-player test hook (tests/ingame/urban-strongpoint.json); inert outside a sim
    /// run. Hits one occupied shell the way vanilla's FragTrace does (TakeShockwave, then TakeDamage on the
    /// same target in the same frame) and reports what the strongpoint rule made of it.
    /// </summary>
    public static class StrongpointAutomation
    {
        private static MapBuilding shell;
        private static bool picked;
        private static int blasts;

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("StrongpointAutomation", "Step", "requires a hosted single-player nomodkit scenario");
            string action = Text(args, "action");
            if (action == "pick")
            {
                shell = ZoneGarrisonManager.AnyStrongpointShell();
                picked = shell != null;
                blasts = 0;
            }
            if (!picked) return Failure("StrongpointAutomation", action, "no occupied shell with a live nest");
            // Destroy() is deferred, so a levelled shell reads as Unity-null from the next call on.
            if (shell == null) return new Dictionary<string, object> { { "ok", true }, { "destroyed", 1 }, { "hp_drop", 0f } };

            float before = GameAccess.GetMapBuildingHitPoints(shell);
            if (action == "bomb")
            {
                // A fresh origin per blast: the rule counts one explosion once per shell (frame + origin).
                Vector3 origin = shell.transform.position + Vector3.up * 20f + Vector3.right * (2f * blasts++);
                shell.TakeShockwave(origin, 1f, Number(args, "power", StrongpointHitPolicy.BombPower));
                shell.TakeDamage(0f, 1000f, 1f, 0f, 0f, PersistentID.None);
            }
            else if (action == "bullet") shell.TakeDamage(50f, 0f, 1f, 0f, 0f, PersistentID.None);
            else if (action != "pick" && action != "status")
                return Failure("StrongpointAutomation", "Step", "unknown action");

            float after = GameAccess.GetMapBuildingHitPoints(shell);
            var result = new Dictionary<string, object>
            {
                { "ok", true },
                { "shell", shell.name },
                { "hp", after },
                { "hp_drop", before - after },
                { "destroyed", after < 0f ? 1 : 0 },
            };
            Debug.Log("[StrongpointAutomation] " + action + ": " + Describe(result));
            return result;
        }
    }
}
