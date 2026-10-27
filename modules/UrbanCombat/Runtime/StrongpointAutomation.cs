#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Explicit nomodkit single-player test hook (tests/ingame/urban-strongpoint.json); inert outside a sim
    /// run. Hits one occupied shell, its rooftop nest or an unrelated unit the way vanilla's FragTrace does
    /// (TakeShockwave, then TakeDamage on the same target in the same frame) and reports what the strongpoint
    /// rule made of it.
    /// </summary>
    public static class StrongpointAutomation
    {
        private static MapBuilding shell;
        private static Building nest;
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
                shell = ZoneGarrisonManager.AnyStrongpointShell(out nest);
                picked = shell != null;
                blasts = 0;
            }
            if (!picked) return Failure("StrongpointAutomation", action, "no occupied shell with a live nest");
            // Destroy() is deferred, so a levelled shell reads as Unity-null from the next call on.
            if (shell == null) return new Dictionary<string, object> { { "ok", true }, { "destroyed", 1 }, { "hp_drop", 0f } };

            float before = GameAccess.GetMapBuildingHitPoints(shell);
            // "nest" and "unit" blast a unit part instead of the shell: the nest takes the hit itself and leaves
            // its shell untouched, like any other unit.
            UnitPart part = null;
            string partName = null;
            float partBefore = 0f;
            if (action == "bomb") Blast(shell, Number(args, "power", StrongpointHitPolicy.BombPower));
            else if (action == "bullet") shell.TakeDamage(50f, 0f, 1f, 0f, 0f, PersistentID.None);
            else if (action == "nest" || action == "unit")
            {
                part = action == "nest" ? NestPart() : OrdinaryPart(Bool(args, "building"));
                if (part == null) return Failure("StrongpointAutomation", action, "no such unit part in the scene");
                partName = part.GetUnit().name + "/" + part.name;
                partBefore = part.hitPoints;
                Blast(part, StrongpointHitPolicy.BombPower);
            }
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
            if (part != null)
            {
                result["part"] = partName;
                result["part_drop"] = partBefore - part.hitPoints;
            }
            Debug.Log("[StrongpointAutomation] " + action + ": " + Describe(result));
            return result;
        }

        private static void Blast(IDamageable target, float power)
        {
            // A fresh origin per blast: the rule counts one explosion once per shell (frame + origin).
            Vector3 origin = shell.transform.position + Vector3.up * 20f + Vector3.right * (2f * blasts++);
            target.TakeShockwave(origin, 1f, power);
            target.TakeDamage(0f, 1000f, 1f, 0f, 0f, PersistentID.None);
        }

        // Not the dugout when there is a choice: the manager lowers that part on purpose to carry the stage.
        private static UnitPart NestPart()
        {
            if (nest == null) return null;
            UnitPart dugout = null;
            foreach (UnitPart part in nest.GetComponentsInChildren<UnitPart>(true))
            {
                if (part.name.IndexOf("dugout", StringComparison.OrdinalIgnoreCase) < 0) return part;
                dugout = part;
            }
            return dugout;
        }

        // A live part outside the garrison system: a plain building when asked, otherwise a vehicle, ship or aircraft.
        private static UnitPart OrdinaryPart(bool building)
        {
            foreach (UnitPart part in UnityEngine.Object.FindObjectsOfType<UnitPart>())
            {
                Unit unit = part.GetUnit();
                if (unit == null || unit.disabled || unit is Scenery || part.hitPoints <= 0f) continue;
                if (unit is Building candidate ? building && !NestRegistry.TryGetShell(candidate, out _) : !building)
                    return part;
            }
            return null;
        }
    }
}
#endif
