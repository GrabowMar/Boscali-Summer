using System;
using System.Collections.Generic;
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
        private static bool portraitFailureLogged;
        private static bool selectionPortraitFailureLogged;
        private static int cachedWingMemberFrame = int.MinValue;
        private static int[] cachedWingMemberIds = Array.Empty<int>();

        // Additive companion pilot API. Resolved separately from the squad API so an older
        // Wing Command build keeps every existing feature and only the SQD studio is disabled.
        private static bool studioResolved;
        private static string studioUnavailableReason = "Wing Command companion pilot API has not been checked.";
        private static MethodInfo portraitForSelection, getCustomPilot, getCustomPilots, saveCustomPilot;
        private static MethodInfo deleteCustomPilot, isPilotRecruited, recruitCustomPilot, dischargeCustomPilot;
        private static MethodInfo importAllCustomPilots, personaLabel, rankNameForXp, bodyLabel, uniformLabel;
        private static PropertyInfo bodyCount, faceCount, hairCount, uniformCount, backdropCount;

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
            catch (Exception error)
            {
                if (!portraitFailureLogged)
                {
                    portraitFailureLogged = true;
                    Plugin.Logger?.LogWarning("WingLink portrait failed: " +
                        (error.InnerException?.Message ?? error.Message));
                }
                return null;
            }
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

        // ---- Companion pilot profile and roster editor -----------------------------------

        public static bool PilotStudioAvailable
        {
            get { ResolveStudio(); return string.IsNullOrEmpty(studioUnavailableReason); }
        }

        public static string PilotStudioUnavailableReason
        {
            get { ResolveStudio(); return studioUnavailableReason; }
        }

        public static int PortraitBodyCount => StudioCount(bodyCount);
        public static int PortraitFaceCount => StudioCount(faceCount);
        public static int PortraitHairCount => StudioCount(hairCount);
        public static int PortraitUniformCount => StudioCount(uniformCount);
        public static int PortraitBackdropCount => StudioCount(backdropCount);

        public static string PortraitBodyLabel(int body)
        {
            if (!ResolveStudio()) return "BODY";
            try { return bodyLabel.Invoke(null, new object[] { body }) as string ?? "BODY"; }
            catch (Exception error) { FailStudio(error); return "BODY"; }
        }

        public static string PortraitUniformLabel(int uniform)
        {
            if (!ResolveStudio()) return "SUIT";
            try { return uniformLabel.Invoke(null, new object[] { uniform }) as string ?? "SUIT"; }
            catch (Exception error) { FailStudio(error); return "SUIT"; }
        }

        public static string PersonaLabel(int persona)
        {
            if (!ResolveStudio()) return "PROFESSIONAL";
            try { return personaLabel.Invoke(null, new object[] { persona }) as string ?? "PROFESSIONAL"; }
            catch (Exception error) { FailStudio(error); return "PROFESSIONAL"; }
        }

        public static string RankNameForXp(int xp)
        {
            if (!ResolveStudio()) return "ROOKIE";
            try { return rankNameForXp.Invoke(null, new object[] { xp }) as string ?? "ROOKIE"; }
            catch (Exception error) { FailStudio(error); return "ROOKIE"; }
        }

        /// <summary>Borrow a Wing Command-owned portrait; never destroy the sprite.</summary>
        public static Sprite PilotPortraitForSelection(
            int body, int face, int hair, int uniform, int accessory, int backdrop)
        {
            if (!ResolveStudio()) return null;
            try
            {
                return portraitForSelection.Invoke(null,
                    new object[] { body, face, hair, uniform, accessory, backdrop }) as Sprite;
            }
            catch (Exception error)
            {
                if (!selectionPortraitFailureLogged)
                {
                    selectionPortraitFailureLogged = true;
                    Plugin.Logger?.LogWarning("WingLink selection portrait failed: " +
                        (error.InnerException?.Message ?? error.Message));
                }
                return null;
            }
        }

        public static bool TryGetCustomPilot(string callsign, out WingPilotRecord record)
        {
            record = default;
            if (!ResolveStudio() || string.IsNullOrEmpty(callsign)) return false;
            try
            {
                return WingPilotRecord.TryParse(
                    getCustomPilot.Invoke(null, new object[] { callsign }) as object[], out record);
            }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static bool TryListCustomPilots(out WingPilotRecord[] records)
        {
            records = Array.Empty<WingPilotRecord>();
            if (!ResolveStudio()) return false;
            try
            {
                if (!(getCustomPilots.Invoke(null, null) is object[][] raw) || raw.Length == 0) return true;
                var parsed = new System.Collections.Generic.List<WingPilotRecord>(Math.Min(raw.Length, 128));
                for (int i = 0; i < raw.Length; i++)
                    if (WingPilotRecord.TryParse(raw[i], out WingPilotRecord record)) parsed.Add(record);
                records = parsed.ToArray();
                return true;
            }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static bool SaveCustomPilot(WingPilotRecord record)
        {
            if (!ResolveStudio()) return false;
            try
            {
                return saveCustomPilot.Invoke(null, new object[] { record.ToValues() }) is bool saved && saved;
            }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static bool DeleteCustomPilot(string callsign)
        {
            if (!ResolveStudio()) return false;
            try { return deleteCustomPilot.Invoke(null, new object[] { callsign }) is bool deleted && deleted; }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static bool IsPilotRecruited(string callsign)
        {
            if (!ResolveStudio()) return false;
            try { return isPilotRecruited.Invoke(null, new object[] { callsign }) is bool recruited && recruited; }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static bool RecruitCustomPilot(string callsign)
        {
            if (!ResolveStudio()) return false;
            try { return recruitCustomPilot.Invoke(null, new object[] { callsign }) is bool recruited && recruited; }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static bool DischargeCustomPilot(string callsign)
        {
            if (!ResolveStudio()) return false;
            try { return dischargeCustomPilot.Invoke(null, new object[] { callsign }) is bool removed && removed; }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static int ImportAllCustomPilots()
        {
            if (!ResolveStudio()) return 0;
            try { return importAllCustomPilots.Invoke(null, null) is int count ? count : 0; }
            catch (Exception error) { FailStudio(error); return 0; }
        }

        public static bool IsWingMember(int persistentIdHash)
        {
            int[] ids = WingMemberIdsThisFrame();
            if (ids.Length > 0) return PresenceBoard.Contains(ids, persistentIdHash);

            MethodInfo method = ResolveMembership();
            if (method == null) return false;
            try { return method.Invoke(null, new object[] { persistentIdHash }) is bool hit && hit; }
            catch (Exception error) { FailMembership(error); return false; }
        }

        private static int[] WingMemberIdsThisFrame()
        {
            int frame = Time.frameCount;
            if (cachedWingMemberFrame == frame) return cachedWingMemberIds;
            cachedWingMemberFrame = frame;
            cachedWingMemberIds = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
            return cachedWingMemberIds;
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

        /// <summary>Live friendly wingmen in published wing order; a null entry is a slot no
        /// longer in the scene. The destination list is reused by the caller.</summary>
        public static void ResolveWingAircraft(List<Aircraft> destination)
        {
            if (destination == null) return;
            destination.Clear();
            int[] ids = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
            if (ids.Length == 0) return;
            List<Aircraft> all = UnitRegistry.allAircraft;
            for (int i = 0; i < ids.Length && i < 16; i++)
            {
                Aircraft match = null;
                for (int j = 0; j < all.Count; j++)
                {
                    Aircraft aircraft = all[j];
                    if (aircraft != null && aircraft.persistentID.GetHashCode() == ids[i])
                    { match = aircraft; break; }
                }
                destination.Add(match);
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

        private static bool ResolveStudio()
        {
            if (studioResolved) return string.IsNullOrEmpty(studioUnavailableReason);
            studioResolved = true;
            studioUnavailableReason = "Update Wing Command to the companion build with the pilot studio API.";
            if (!Chainloader.PluginInfos.ContainsKey(WingCommandGuid)) return false;
            try
            {
                Type type = Type.GetType(SquadType, throwOnError: false);
                if (!(type?.GetProperty("ApiVersion", BindingFlags.Public | BindingFlags.Static)
                          ?.GetValue(null) is int version) || version != 1) return false;
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                portraitForSelection = type.GetMethod("PortraitForSelection", flags, null,
                    new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) }, null);
                getCustomPilot = type.GetMethod("GetCustomPilot", flags, null, new[] { typeof(string) }, null);
                getCustomPilots = type.GetMethod("GetCustomPilots", flags, null, Type.EmptyTypes, null);
                saveCustomPilot = type.GetMethod("SaveCustomPilot", flags, null, new[] { typeof(object[]) }, null);
                deleteCustomPilot = type.GetMethod("DeleteCustomPilot", flags, null, new[] { typeof(string) }, null);
                isPilotRecruited = type.GetMethod("IsPilotRecruited", flags, null, new[] { typeof(string) }, null);
                recruitCustomPilot = type.GetMethod("RecruitCustomPilot", flags, null, new[] { typeof(string) }, null);
                dischargeCustomPilot = type.GetMethod("DischargeCustomPilot", flags, null, new[] { typeof(string) }, null);
                importAllCustomPilots = type.GetMethod("ImportAllCustomPilots", flags, null, Type.EmptyTypes, null);
                personaLabel = type.GetMethod("PersonaLabel", flags, null, new[] { typeof(int) }, null);
                rankNameForXp = type.GetMethod("RankNameForXp", flags, null, new[] { typeof(int) }, null);
                bodyLabel = type.GetMethod("PortraitBodyLabel", flags, null, new[] { typeof(int) }, null);
                uniformLabel = type.GetMethod("PortraitUniformLabel", flags, null, new[] { typeof(int) }, null);
                bodyCount = type.GetProperty("PortraitBodyCount", flags);
                faceCount = type.GetProperty("PortraitFaceCount", flags);
                hairCount = type.GetProperty("PortraitHairCount", flags);
                uniformCount = type.GetProperty("PortraitUniformCount", flags);
                backdropCount = type.GetProperty("PortraitBackdropCount", flags);
                if (portraitForSelection == null || getCustomPilot == null ||
                    getCustomPilots == null || saveCustomPilot == null || deleteCustomPilot == null || isPilotRecruited == null ||
                    recruitCustomPilot == null || dischargeCustomPilot == null || importAllCustomPilots == null ||
                    personaLabel == null || rankNameForXp == null || bodyLabel == null || uniformLabel == null ||
                    bodyCount == null || faceCount == null || hairCount == null || uniformCount == null ||
                    backdropCount == null) return false;
                studioUnavailableReason = string.Empty;
                return true;
            }
            catch (Exception error) { FailStudio(error); return false; }
        }

        private static int StudioCount(PropertyInfo property)
        {
            if (property == null || !ResolveStudio()) return 0;
            try { return property.GetValue(null) is int value ? value : 0; }
            catch (Exception error) { FailStudio(error); return 0; }
        }

        private static void FailStudio(Exception error)
        {
            bool firstFailure = string.IsNullOrEmpty(studioUnavailableReason);
            studioUnavailableReason = "Wing Command pilot studio API failed; check the BepInEx log.";
            if (firstFailure) Plugin.Logger?.LogWarning("WingLink pilot studio API disabled: " +
                (error.InnerException?.Message ?? error.Message));
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
