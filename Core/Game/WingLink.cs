using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// In-tree Wing API adapter. Presence and map gestures retain the shared protocol;
    /// squad features resolve the Wing feature's contract and fail closed while it is
    /// missing. Same surface the external-plugin adapter had, so call sites are untouched.
    /// </summary>
    internal static class WingLink
    {
        private static bool squadResolved;
        private static string squadUnavailableReason = "The Wing feature is not installed.";
        private static IWingSquad squad;
        private static bool portraitFailureLogged;
        private static bool selectionPortraitFailureLogged;
        private static int cachedWingMemberFrame = int.MinValue;
        private static int[] cachedWingMemberIds = Array.Empty<int>();

        public static int AceAbilityMask(Aircraft aircraft)
        {
            if (aircraft == null || !ResolveSquad()) return 0;
            try { return squad.AbilityMask(aircraft) & 15; }
            catch (Exception error) { FailSquad(error); return 0; }
        }

        public static bool Available =>
            !string.IsNullOrEmpty(PresenceBoard.GetString(PresenceBoard.WingGuid)) || SquadAvailable;

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
            try { return squad.TryCreatePilot(seed, out name, out callsign, out background, out persona); }
            catch (Exception error) { FailSquad(error); return false; }
        }

        /// <summary>Borrowed Wing-owned portrait. Do not destroy the sprite or texture.</summary>
        public static Sprite PilotPortrait(string name, string callsign) =>
            PersonnelPortrait(name, callsign, PortraitRole.Pilot);

        /// <summary>Borrowed Wing-owned portrait with role-specific clothing and equipment.</summary>
        public static Sprite PersonnelPortrait(string name, string callsign, PortraitRole role, int faction = -1)
        {
            if (!ResolveSquad()) return null;
            try { return squad.PersonnelPortrait(name, callsign, role, faction); }
            catch (Exception error)
            {
                if (!portraitFailureLogged)
                {
                    portraitFailureLogged = true;
                    Plugin.Logger?.LogWarning("WingLink portrait failed: " + error.Message);
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
                return squad.SpawnWingAt(target, enemyHq, seed, tier, count, callsign, ingressX, ingressZ)
                       ?? Array.Empty<Aircraft>();
            }
            catch (Exception error) { FailSquad(error); return Array.Empty<Aircraft>(); }
        }

        public static bool SetAceWingTarget(Aircraft[] wing, Aircraft target)
        {
            if (!ResolveSquad()) return false;
            try { return squad.SetWingTarget(wing, target); }
            catch (Exception error) { FailSquad(error); return false; }
        }

        public static void ReleaseAceWing(Aircraft[] wing, bool destroy)
        {
            // Preserve cleanup even if another API operation faulted after a successful spawn.
            ResolveSquad();
            if (squad == null) return;
            try { squad.ReleaseWing(wing, destroy); }
            catch (Exception error) { FailSquad(error); }
        }

        public static void EnemyChatter(string callsign, string context, string message)
        {
            if (!ResolveSquad()) return;
            try { squad.Chatter(callsign, context, message); }
            catch (Exception error) { FailSquad(error); }
        }

        public static int SurvivorStatus(PersistentID aircraftId)
        {
            if (!ResolveSquad()) return 0;
            try { return squad.SurvivorStatus(aircraftId); }
            catch (Exception error) { FailSquad(error); return 0; }
        }

        public static bool RecoverSurvivor(PersistentID aircraftId)
        {
            if (!ResolveSquad()) return false;
            try { return squad.RecoverSurvivor(aircraftId); }
            catch (Exception error) { FailSquad(error); return false; }
        }

        // ---- Saved pilots ------------------------------------------------------------

        /// <summary>Borrow a Wing-owned portrait; preview is updated in place. Never destroy the sprite.</summary>
        public static Sprite PilotPortraitForSelection(
            int body, int face, int hair, int uniform, int accessory, int backdrop, bool preview = false)
        {
            if (!ResolveSquad()) return null;
            try { return squad.PortraitForSelection(body, face, hair, uniform, accessory, backdrop, preview); }
            catch (Exception error)
            {
                if (!selectionPortraitFailureLogged)
                {
                    selectionPortraitFailureLogged = true;
                    Plugin.Logger?.LogWarning("WingLink selection portrait failed: " + error.Message);
                }
                return null;
            }
        }

        public static bool TryGetCustomPilot(string callsign, out WingPilotRecord record)
        {
            record = default;
            if (!ResolveSquad() || string.IsNullOrEmpty(callsign)) return false;
            try
            {
                if (!squad.TryGetCustomPilot(callsign, out CustomPilotView view)) return false;
                record = ToRecord(view);
                return true;
            }
            catch (Exception error) { FailSquad(error); return false; }
        }

        /// <summary>Open the WMC SQUADRON > STUDIO page, where saved pilots are edited.</summary>
        public static bool OpenPilotStudio()
        {
            if (!ResolveSquad()) return false;
            try { return squad.OpenPilotStudio(); }
            catch (Exception error) { FailSquad(error); return false; }
        }

        public static bool IsWingMember(int persistentIdHash)
        {
            // The in-tree wing publishes its roster on the presence board on every
            // roster change; an empty board means no wing, with no fallback to call.
            return WingApiVersions.IsWingMember(WingMemberIdsThisFrame(), null, persistentIdHash);
        }

        /// <summary>Returns false when the wing's membership source is unavailable.</summary>
        public static bool TryIsWingMember(int persistentIdHash, out bool member)
        {
            member = false;
            if (AppDomain.CurrentDomain.GetData(PresenceBoard.WingMemberIds) is int[])
            {
                member = PresenceBoard.Contains(WingMemberIdsThisFrame(), persistentIdHash);
                return true;
            }
            return false;
        }

        private static int[] WingMemberIdsThisFrame()
        {
            int frame = Time.frameCount;
            if (cachedWingMemberFrame == frame) return cachedWingMemberIds;
            cachedWingMemberFrame = frame;
            cachedWingMemberIds = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
            return cachedWingMemberIds;
        }

        public static int WingCount => PresenceBoard.GetInts(PresenceBoard.WingMemberIds).Length;

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

        public static bool WingMapGestureArmed => MapPicker.IsOwner(MapPicker.WingPoint);

        private static bool ResolveSquad()
        {
            if (squadResolved) return string.IsNullOrEmpty(squadUnavailableReason);
            if (!ModuleServices.TryGet(out IWingSquad service) || service == null) return false;
            squadResolved = true;
            squad = service;
            squadUnavailableReason = string.Empty;
            return true;
        }

        private static void FailSquad(Exception error)
        {
            bool firstFailure = string.IsNullOrEmpty(squadUnavailableReason);
            squadUnavailableReason = "Wing squad API failed; check the BepInEx log.";
            if (firstFailure) Plugin.Logger?.LogWarning("WingLink squad API disabled: " + error.Message);
        }

        private static WingPilotRecord ToRecord(CustomPilotView view) => new WingPilotRecord
        {
            Name = view.Name,
            Callsign = view.Callsign,
            DialogueTag = view.DialogueTag,
            Persona = view.Persona,
            Background = view.Background,
            Xp = view.Xp,
            Kills = view.Kills,
            Sorties = view.Sorties,
            HasPortrait = view.HasPortrait,
            Body = view.Body,
            Face = view.Face,
            Hair = view.Hair,
            Uniform = view.Uniform,
            Accessory = view.Accessory,
            Backdrop = view.Backdrop,
        };
    }
}
