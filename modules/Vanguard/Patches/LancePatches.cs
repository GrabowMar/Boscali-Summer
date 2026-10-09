using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Patches
{
    // Every pod on every peer: remember it (and dress it with the view) as soon as the mount is on the hardpoint.
    [HarmonyPatch(typeof(Gun), nameof(Gun.AttachToHardpoint))]
    internal static class LanceAttachPatch
    {
        private static void Postfix(Gun __instance, Aircraft aircraft)
        {
            if (__instance == null || aircraft == null || __instance.info == null || !__instance.info.name.Contains(VanguardKeys.LanceInfo)) return;
            LanceService.Register(__instance, aircraft);
        }
    }

    // Non-owner peers (and the server for client-owned aircraft) replay the owner's shot: tracer size and recoil follow
    // the power the server confirmed in LanceFx (default 0.6 when that message has not arrived yet).
    [HarmonyPatch(typeof(Gun), nameof(Gun.RemoteSingleFire))]
    internal static class LanceRemoteShotPatch
    {
        private const float FreshSeconds = 1.5f, DefaultPower = 0.6f;

        private static void Prefix(Gun __instance, out LancePod __state)
        {
            __state = null;
            if (!LanceService.Pods.TryGetValue(__instance, out LancePod pod) || pod.Owner || !LanceAccess.Ensure()) return;
            __state = pod;
            LanceAccess.SetTracerSize(__instance, LanceCapacitor.Tracer(PowerOf(pod)));
        }

        private static void Postfix(LancePod __state)
        {
            if (__state == null) return;
            float power = PowerOf(__state);
            __state.ShotAt = Time.time;
            __state.LastPower = power;
            __state.RemoteCharge = 0f;
            if (__state.View != null) __state.View.PlayShot(power);
        }

        private static float PowerOf(LancePod pod) => Time.time - pod.FxAt <= FreshSeconds ? pod.FxPower : DefaultPower;
    }

    // The single server-side damage choke point: both vanilla hit paths end in ArmorPenetrate (host/AI RegisterHit and the
    // client claim). A Lance slug is recognised by the off-round WeaponInfo triple and handed to the through-shot resolver.
    [HarmonyPatch(typeof(DamageEffects), nameof(DamageEffects.ArmorPenetrate))]
    internal static class LanceArmorPenetratePatch
    {
        private static bool Prefix(Vector3 position, Vector3 velocity, float muzzleVelocity, float pierceDamage, float blastDamage, PersistentID dealerID)
        {
            if (!LanceCapacitor.IsSignature(pierceDamage, muzzleVelocity, blastDamage) || !GameAccess.IsServer()) return true;
            LanceServer.Resolve(position, velocity, dealerID);
            return false;
        }
    }

    // Engine draw: the jets' parasitic loss and the turboprops' throttle. Both read LanceEngineDraw tables (empty = no-op).
    [HarmonyPatch(typeof(Turbofan), nameof(Turbofan.SetParasiticLoss))]
    internal static class LanceTurbofanLossPatch
    {
        private static void Prefix(Turbofan __instance, ref float loss) => loss += LanceEngineDraw.ExtraLoss(__instance);
    }

    [HarmonyPatch(typeof(Turbojet), nameof(Turbojet.SetParasiticLoss))]
    internal static class LanceTurbojetLossPatch
    {
        private static void Prefix(Turbojet __instance, ref float loss) => loss += LanceEngineDraw.ExtraLoss(__instance);
    }

    [HarmonyPatch(typeof(TurbineEngine), nameof(TurbineEngine.Throttle))]
    internal static class LanceTurbineThrottlePatch
    {
        private static void Postfix(TurbineEngine __instance)
        {
            float factor = LanceEngineDraw.ThrottleFactor(__instance);
            if (factor < 1f) __instance.throttle *= factor;
        }
    }
}
