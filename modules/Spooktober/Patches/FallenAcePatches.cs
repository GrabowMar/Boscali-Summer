using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Spooktober.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Spooktober.Patches
{
    /// <summary>The ace's hull: damage resistance, the blink window and three lives (a vital hit with lives to spare is skipped and
    /// reborn). Anyone else hit by the ace takes the gunnery perk. Hot path: one reference check while no ace is out.</summary>
    [HarmonyPatch(typeof(UnitPart), nameof(UnitPart.TakeDamage))]
    internal static class FallenAcePartDamagePatch
    {
        private static bool Prefix(UnitPart __instance, ref float pierceDamage, ref float blastDamage, float amountAffected,
                                   ref float fireDamage, ref float impactDamage, PersistentID dealerID, bool ___criticalPart)
        {
            Aircraft ace = FallenAce.Current;
            if (ReferenceEquals(ace, null) || ace == null || __instance == null) return true;
            try
            {
                if (__instance.parentUnit == ace)
                    return FallenAce.TakePartHit(__instance, ___criticalPart, ref pierceDamage, ref blastDamage, amountAffected, ref fireDamage, ref impactDamage);
                if (dealerID == ace.persistentID)
                    FallenAce.ScaleGunHit(__instance.GetArmorProperties(), ref pierceDamage, ref blastDamage, ref fireDamage, ref impactDamage);
            }
            catch (Exception e) { PatchGuard.Report("Spooktober.AceTakeDamage", e); }
            return true;
        }
    }

    /// <summary>The ace's pilot: the same resistance, and a killing wound with lives to spare is a rebirth.</summary>
    [HarmonyPatch(typeof(Pilot), nameof(Pilot.ApplyDamage))]
    internal static class FallenAcePilotDamagePatch
    {
        private static bool Prefix(Pilot __instance, ref float pierceDamage, ref float blastDamage, ref float fireDamage, ref float impactDamage,
                                   float ___hitPoints)
        {
            Aircraft ace = FallenAce.Current;
            if (ReferenceEquals(ace, null) || ace == null || __instance == null || __instance.aircraft != ace || !ace.IsServer) return true;
            try { return FallenAce.TakePilotHit(___hitPoints, ref pierceDamage, ref blastDamage, ref fireDamage, ref impactDamage); }
            catch (Exception e) { PatchGuard.Report("Spooktober.AcePilotDamage", e); }
            return true;
        }
    }

    /// <summary>The ace's rounds on an enemy pilot take the gunnery perk too (Pilot.ApplyDamage below carries no dealer).</summary>
    [HarmonyPatch(typeof(Pilot), nameof(Pilot.TakeDamage))]
    internal static class FallenAcePilotGunPatch
    {
        private static void Prefix(Pilot __instance, ref float pierceDamage, ref float blastDamage, ref float fireDamage, ref float impactDamage,
                                   PersistentID dealerID)
        {
            Aircraft ace = FallenAce.Current;
            if (ReferenceEquals(ace, null) || ace == null || __instance == null || dealerID != ace.persistentID || __instance.aircraft == ace) return;
            try { FallenAce.ScaleGunHit(__instance.GetArmorProperties(), ref pierceDamage, ref blastDamage, ref fireDamage, ref impactDamage); }
            catch (Exception e) { PatchGuard.Report("Spooktober.AcePilotGun", e); }
        }
    }

    /// <summary>The ace never bails out of a flyable aircraft, and a bail-out from a wreck is a rebirth while it has lives to spare.</summary>
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.StartEjectionSequence))]
    internal static class FallenAceEjectionPatch
    {
        private static bool Prefix(Aircraft __instance)
        {
            Aircraft ace = FallenAce.Current;
            if (ReferenceEquals(ace, null) || __instance != ace || !ace.IsServer) return true;
            try { return !FallenAce.SkipEjection(ace); }
            catch (Exception e) { PatchGuard.Report("Spooktober.AceEjection", e); }
            return true;
        }
    }

    /// <summary>Phantom: a guided missile launched at the ace from beyond the phantom range aims off to one side.</summary>
    [HarmonyPatch(typeof(Missile), nameof(Missile.SetAimpoint))]
    internal static class FallenAcePhantomPatch
    {
        private static void Prefix(Missile __instance, ref GlobalPosition aimPoint)
        {
            Aircraft ace = FallenAce.Current;
            if (ReferenceEquals(ace, null) || ace == null || __instance == null || __instance.targetID != ace.persistentID) return;
            try { FallenAce.Aim(__instance, ref aimPoint); }
            catch (Exception e) { PatchGuard.Report("Spooktober.AcePhantom", e); }
        }
    }

    /// <summary>The ace's combat AI hunts the director's target (vanilla never reads Pilot.SetPrimaryTarget) and breaks off in a Stalk.</summary>
    [HarmonyPatch(typeof(CombatAI), nameof(CombatAI.ChooseHQTarget))]
    internal static class FallenAceTargetPatch
    {
        private static void Postfix(Unit searcher, List<WeaponStation> stationList, ref CombatAI.TargetSearchResults __result)
        {
            Aircraft ace = FallenAce.Current;
            if (ReferenceEquals(ace, null) || searcher != ace) return;
            try { FallenAce.ChooseTarget(ace, stationList, ref __result); }
            catch (Exception e) { PatchGuard.Report("Spooktober.AceTarget", e); }
        }
    }
}
