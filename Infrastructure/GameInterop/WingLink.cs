using System;
using System.Reflection;
using BepInEx.Bootstrap;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Runtime
{
    /// <summary>
    /// Cached Wing Command public API adapter. Presence and map gestures retain the shared
    /// protocol; squad features require the versioned WingSquad façade.
    /// </summary>
    internal static class WingLink
    {
        private const string WingCommandGuid = "com.marci.wingcommand";
        private const string MembershipType = "WingCommand.Interop.WingMembership, WingCommand";
        private const string MapModeType = "WingCommand.Interop.WingMapMode, WingCommand";
        private const string SquadType = "WingCommand.Interop.WingSquad, WingCommand";

        private static bool membershipResolved;
        private static MethodInfo contains;
        private static PropertyInfo count;
        private static bool mapModeResolved;
        private static PropertyInfo gestureArmed;
        private static bool squadResolved;
        private static string squadUnavailableReason = "Wing Command squad API has not been checked.";
        private static MethodInfo createPilot, portrait, spawnWing, setTarget, releaseWing, chatter;
        private static MethodInfo survivorStatus, recoverSurvivor, abilityMask;

        public static int AceAbilityMask(Aircraft aircraft)
        {
            if (aircraft == null || !ResolveSquad()) return 0;
            try { return abilityMask.Invoke(null, new object[] { aircraft }) is int mask ? mask & 15 : 0; }
            catch (Exception error) { FailSquad(error); return 0; }
        }

        public static bool Available =>
            !string.IsNullOrEmpty(PresenceBoard.GetString(PresenceBoard.WingGuid)) ||
            Chainloader.PluginInfos.ContainsKey(WingCommandGuid);

        public static bool SquadAvailable => ResolveSquad();
        public static string SquadUnavailableReason
        {
            get { ResolveSquad(); return squadUnavailableReason; }
        }

        public static bool TryCreatePilot(int seed, out string name, out string callsign,
                                          out string background, out int persona)
        {
            name = callsign = background = string.Empty;
            persona = 0;
            if (!ResolveSquad()) return false;
            try
            {
                if (!(createPilot.Invoke(null, new object[] { seed }) is object[] values) ||
                    values.Length != 4 || !(values[0] is string pilotName) ||
                    !(values[1] is string pilotCallsign) || !(values[2] is string pilotBackground) ||
                    !(values[3] is int pilotPersona)) return false;
                name = pilotName;
                callsign = pilotCallsign;
                background = pilotBackground;
                persona = pilotPersona;
                return true;
            }
            catch (Exception error) { FailSquad(error); return false; }
        }

        /// <summary>Borrowed WC-owned portrait. Do not destroy the sprite or texture.</summary>
        public static Sprite PilotPortrait(string name, string callsign)
        {
            if (!ResolveSquad()) return null;
            try { return portrait.Invoke(null, new object[] { name, callsign }) as Sprite; }
            catch (Exception error) { FailSquad(error); return null; }
        }

        public static Aircraft[] SpawnAceWing(Aircraft target, FactionHQ enemyHq, int seed,
                                               int tier, int count, string callsign, float ingressX, float ingressZ)
        {
            if (!ResolveSquad()) return Array.Empty<Aircraft>();
            try
            {
                return spawnWing.Invoke(null, new object[] { target, enemyHq, seed, tier, count, callsign, ingressX, ingressZ })
                       as Aircraft[] ?? Array.Empty<Aircraft>();
            }
            catch (Exception error) { FailSquad(error); return Array.Empty<Aircraft>(); }
        }

        public static bool SetAceWingTarget(Aircraft[] wing, Aircraft target)
        {
            if (!ResolveSquad()) return false;
            try { return setTarget.Invoke(null, new object[] { wing, target }) is bool accepted && accepted; }
            catch (Exception error) { FailSquad(error); return false; }
        }

        public static void ReleaseAceWing(Aircraft[] wing, bool destroy)
        {
            // Preserve cleanup even if another API operation faulted after a successful spawn.
            ResolveSquad();
            if (releaseWing == null) return;
            try { releaseWing.Invoke(null, new object[] { wing, destroy }); }
            catch (Exception error) { FailSquad(error); }
        }

        public static void EnemyChatter(string callsign, string context, string message)
        {
            if (!ResolveSquad()) return;
            try { chatter.Invoke(null, new object[] { callsign, context, message }); }
            catch (Exception error) { FailSquad(error); }
        }

        public static int SurvivorStatus(PersistentID aircraftId)
        {
            if (!ResolveSquad()) return 0;
            try { return survivorStatus.Invoke(null, new object[] { aircraftId }) is int state ? state : 0; }
            catch (Exception error) { FailSquad(error); return 0; }
        }

        public static bool RecoverSurvivor(PersistentID aircraftId)
        {
            if (!ResolveSquad()) return false;
            try { return recoverSurvivor.Invoke(null, new object[] { aircraftId }) is bool success && success; }
            catch (Exception error) { FailSquad(error); return false; }
        }

        public static bool IsWingMember(int persistentIdHash)
        {
            int[] ids = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
            if (ids.Length > 0) return PresenceBoard.Contains(ids, persistentIdHash);

            MethodInfo method = ResolveMembership();
            if (method == null) return false;
            try { return method.Invoke(null, new object[] { persistentIdHash }) is bool hit && hit; }
            catch (Exception error) { FailMembership(error); return false; }
        }

        public static int WingCount
        {
            get
            {
                int[] ids = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
                if (ids.Length > 0) return ids.Length;
                if (ResolveMembership() == null || count == null) return 0;
                try { return count.GetValue(null) is int value ? value : 0; }
                catch (Exception error) { FailMembership(error); return 0; }
            }
        }

        public static bool WingMapGestureArmed
        {
            get
            {
                if (MapPicker.IsOwner(MapPicker.WingPoint)) return true;
                PropertyInfo property = ResolveMapMode();
                if (property == null) return false;
                try { return property.GetValue(null) is bool value && value; }
                catch (Exception error) { FailMapMode(error); return false; }
            }
        }

        private static MethodInfo ResolveMembership()
        {
            if (membershipResolved) return contains;
            if (!Chainloader.PluginInfos.ContainsKey(WingCommandGuid)) return null;
            membershipResolved = true;
            try
            {
                Type type = Type.GetType(MembershipType, throwOnError: false);
                contains = type?.GetMethod("Contains", BindingFlags.Public | BindingFlags.Static,
                                           null, new[] { typeof(int) }, null);
                count = type?.GetProperty("Count", BindingFlags.Public | BindingFlags.Static);
            }
            catch (Exception error) { FailMembership(error); }
            return contains;
        }

        private static bool ResolveSquad()
        {
            if (squadResolved) return string.IsNullOrEmpty(squadUnavailableReason);
            squadResolved = true;
            squadUnavailableReason = "Install the companion Wing Command build with WingSquad API 1.";
            if (!Chainloader.PluginInfos.ContainsKey(WingCommandGuid)) return false;
            try
            {
                Type type = Type.GetType(SquadType, throwOnError: false);
                if (!(type?.GetProperty("ApiVersion", BindingFlags.Public | BindingFlags.Static)
                          ?.GetValue(null) is int version) || version != 1) return false;
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                createPilot = type.GetMethod("CreatePilot", flags, null, new[] { typeof(int) }, null);
                portrait = type.GetMethod("Portrait", flags, null, new[] { typeof(string), typeof(string) }, null);
                spawnWing = type.GetMethod("SpawnWingAt", flags, null,
                    new[] { typeof(Aircraft), typeof(FactionHQ), typeof(int), typeof(int), typeof(int), typeof(string), typeof(float), typeof(float) }, null);
                setTarget = type.GetMethod("SetTarget", flags, null, new[] { typeof(Aircraft[]), typeof(Aircraft) }, null);
                releaseWing = type.GetMethod("ReleaseWing", flags, null, new[] { typeof(Aircraft[]), typeof(bool) }, null);
                chatter = type.GetMethod("Chatter", flags, null,
                    new[] { typeof(string), typeof(string), typeof(string) }, null);
                survivorStatus = type.GetMethod("SurvivorStatus", flags, null, new[] { typeof(PersistentID) }, null);
                recoverSurvivor = type.GetMethod("RecoverSurvivor", flags, null, new[] { typeof(PersistentID) }, null);
                abilityMask = type.GetMethod("AbilityMask", flags, null, new[] { typeof(Aircraft) }, null);
                if (createPilot == null || portrait == null || spawnWing == null || setTarget == null ||
                    releaseWing == null || chatter == null || survivorStatus == null || recoverSurvivor == null || abilityMask == null) return false;
                squadUnavailableReason = string.Empty;
                return true;
            }
            catch (Exception error) { FailSquad(error); return false; }
        }

        private static void FailSquad(Exception error)
        {
            bool firstFailure = string.IsNullOrEmpty(squadUnavailableReason);
            squadUnavailableReason = "Wing Command squad API failed; check the BepInEx log.";
            if (firstFailure) Plugin.Logger?.LogWarning("WingLink squad API disabled: " +
                (error.InnerException?.Message ?? error.Message));
        }

        private static PropertyInfo ResolveMapMode()
        {
            if (mapModeResolved) return gestureArmed;
            if (!Chainloader.PluginInfos.ContainsKey(WingCommandGuid)) return null;
            mapModeResolved = true;
            try
            {
                Type type = Type.GetType(MapModeType, throwOnError: false);
                gestureArmed = type?.GetProperty("GestureArmed",
                    BindingFlags.Public | BindingFlags.Static);
            }
            catch (Exception error) { FailMapMode(error); }
            return gestureArmed;
        }

        private static void FailMembership(Exception error)
        {
            contains = null;
            count = null;
            Plugin.Logger?.LogWarning(
                "WingLink membership fallback failed; continuing without wing awareness. " +
                error.Message);
        }

        private static void FailMapMode(Exception error)
        {
            gestureArmed = null;
            Plugin.Logger?.LogWarning(
                "WingLink map fallback failed; shared picker remains active. " + error.Message);
        }
    }
}
