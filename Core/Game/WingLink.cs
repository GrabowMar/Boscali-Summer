using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
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

        private static bool studioResolved;
        private static string studioUnavailableReason = "The Wing feature is not installed.";

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

        // ---- Saved-pilot studio ------------------------------------------------------

        public static bool PilotStudioAvailable
        {
            get { ResolveStudio(); return string.IsNullOrEmpty(studioUnavailableReason); }
        }

        public static string PilotStudioUnavailableReason
        {
            get { ResolveStudio(); return studioUnavailableReason; }
        }

        public static int PortraitBodyCount => StudioCount(s => s.PortraitBodyCount);
        public static int PortraitFaceCount => StudioCount(s => s.PortraitFaceCount);
        public static int PortraitHairCount => StudioCount(s => s.PortraitHairCount);
        public static int PortraitUniformCount => StudioCount(s => s.PortraitUniformCount);
        public static int PortraitAccessoryCount => StudioCount(s => s.PortraitAccessoryCount);
        public static int PortraitBackdropCount => StudioCount(s => s.PortraitBackdropCount);

        public static string PortraitBodyLabel(int body)
        {
            if (!ResolveStudio()) return "BODY";
            try { return squad.PortraitBodyLabel(body) ?? "BODY"; }
            catch (Exception error) { FailStudio(error); return "BODY"; }
        }

        public static string PortraitUniformLabel(int uniform)
        {
            if (!ResolveStudio()) return "SUIT";
            try { return squad.PortraitUniformLabel(uniform) ?? "SUIT"; }
            catch (Exception error) { FailStudio(error); return "SUIT"; }
        }

        public static string PortraitAccessoryLabel(int accessory)
        {
            if (!ResolveStudio()) return "NONE";
            try { return squad.PortraitAccessoryLabel(accessory) ?? "NONE"; }
            catch (Exception error) { FailStudio(error); return "NONE"; }
        }

        public static string PortraitBackdropLabel(int backdrop)
        {
            if (!ResolveStudio()) return "BACKDROP";
            try { return squad.PortraitBackdropLabel(backdrop) ?? "BACKDROP"; }
            catch (Exception error) { FailStudio(error); return "BACKDROP"; }
        }

        public static string PersonaLabel(int persona)
        {
            if (!ResolveStudio()) return "PROFESSIONAL";
            try { return squad.PersonaLabel(persona) ?? "PROFESSIONAL"; }
            catch (Exception error) { FailStudio(error); return "PROFESSIONAL"; }
        }

        public static string RankNameForXp(int xp)
        {
            if (!ResolveStudio()) return "ROOKIE";
            try { return squad.RankNameForXp(xp) ?? "ROOKIE"; }
            catch (Exception error) { FailStudio(error); return "ROOKIE"; }
        }

        /// <summary>Borrow a Wing-owned portrait; preview is updated in place. Never destroy the sprite.</summary>
        public static Sprite PilotPortraitForSelection(
            int body, int face, int hair, int uniform, int accessory, int backdrop, bool preview = false)
        {
            if (!ResolveStudio()) return null;
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
            if (!ResolveStudio() || string.IsNullOrEmpty(callsign)) return false;
            try
            {
                if (!squad.TryGetCustomPilot(callsign, out CustomPilotView view)) return false;
                record = ToRecord(view);
                return true;
            }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static bool TryListCustomPilots(out WingPilotRecord[] records)
        {
            records = Array.Empty<WingPilotRecord>();
            if (!ResolveStudio()) return false;
            try
            {
                CustomPilotView[] raw = squad.ListCustomPilots();
                if (raw == null || raw.Length == 0) return true;
                var parsed = new List<WingPilotRecord>(System.Math.Min(raw.Length, 128));
                for (int i = 0; i < raw.Length && parsed.Count < 128; i++)
                    parsed.Add(ToRecord(raw[i]));
                records = parsed.ToArray();
                return true;
            }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static bool SaveCustomPilot(WingPilotRecord record)
        {
            if (!ResolveStudio()) return false;
            try { return squad.SaveCustomPilot(ToView(record)); }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static bool DeleteCustomPilot(string callsign)
        {
            if (!ResolveStudio()) return false;
            try { return squad.DeleteCustomPilot(callsign); }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static bool IsPilotRecruited(string callsign)
        {
            if (!ResolveStudio()) return false;
            try { return squad.IsPilotRecruited(callsign); }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static bool RecruitCustomPilot(string callsign)
        {
            if (!ResolveStudio()) return false;
            try { return squad.RecruitCustomPilot(callsign); }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static bool DischargeCustomPilot(string callsign)
        {
            if (!ResolveStudio()) return false;
            try { return squad.DischargeCustomPilot(callsign); }
            catch (Exception error) { FailStudio(error); return false; }
        }

        public static int ImportAllCustomPilots()
        {
            if (!ResolveStudio()) return 0;
            try { return squad.ImportAllCustomPilots(); }
            catch (Exception error) { FailStudio(error); return 0; }
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

        private static bool ResolveStudio()
        {
            if (studioResolved) return string.IsNullOrEmpty(studioUnavailableReason);
            if (!ResolveSquad()) return false;
            studioResolved = true;
            studioUnavailableReason = string.Empty;
            return true;
        }

        private static int StudioCount(Func<IWingSquad, int> read)
        {
            if (!ResolveStudio()) return 0;
            try { return read(squad); }
            catch (Exception error) { FailStudio(error); return 0; }
        }

        private static void FailStudio(Exception error)
        {
            bool firstFailure = string.IsNullOrEmpty(studioUnavailableReason);
            studioUnavailableReason = "Wing squad API failed; check the BepInEx log.";
            if (firstFailure) Plugin.Logger?.LogWarning("WingLink pilot studio API disabled: " + error.Message);
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

        private static CustomPilotView ToView(WingPilotRecord record) => new CustomPilotView
        {
            Name = record.Name,
            Callsign = record.Callsign,
            DialogueTag = record.DialogueTag,
            Persona = record.Persona,
            Background = record.Background,
            Xp = record.Xp,
            Kills = record.Kills,
            Sorties = record.Sorties,
            HasPortrait = record.HasPortrait,
            Body = record.Body,
            Face = record.Face,
            Hair = record.Hair,
            Uniform = record.Uniform,
            Accessory = record.Accessory,
            Backdrop = record.Backdrop,
        };
    }
}
