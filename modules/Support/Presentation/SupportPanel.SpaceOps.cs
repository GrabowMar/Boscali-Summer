using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPACE actions: host-authorized abilities under persistent coverage, as one column of tiles.
    /// PAW S1 deleted the rooms: core launch is a direct host request, the sensor feed and the
    /// task map return as Tier-2 tabs in S2. Ability readiness uses the shared presenter
    /// (<see cref="AbilityStatus"/>) on every surface. With no station the page is one card and
    /// a LAUNCH CORE button plus the offboard FIRES rows, not a list of locked tiles. The strike strip names live friendly
    /// fires near the player (S1 walking skeleton of the strike-vector band).
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private sealed class SpaceStrikeRow
        {
            public SupportActionDefinition Action;
            public ActionTile Row;
            public AvControl Button;
        }

        private BriefCard spaceBanner;
        private AvButtons spaceLaunch;
        private AvSection spaceAbilitiesSection;
        private AvSection spaceFiresSection;
        private BriefCard strikeStrip;
        private readonly List<SpaceStrikeRow> spaceStrikeRows = new List<SpaceStrikeRow>(4);
        private const string TaskingSectionTitle = "STRIKE TASKING";
        private readonly ActionTile[] taskingSlots = new ActionTile[3];
        private readonly AvControl[] taskingSelects = new AvControl[3];
        private readonly int[] taskingIds = new int[3];
        private readonly List<ActiveStrikeInfo> taskingLive = new List<ActiveStrikeInfo>(4);
        private AvButtons taskingButtons;
        private BriefCard taskingHint;
        private int taskingSelected = -1;

        private void ResetSpaceOpsPage()
        {
            spaceBanner = null;
            spaceLaunch = null;
            spaceAbilitiesSection = null;
            spaceFiresSection = null;
            strikeStrip = null;
            spaceStrikeRows.Clear();
            taskingButtons = null;
            taskingHint = null;
            taskingSelected = -1;
            taskingLive.Clear();
            for (int i = 0; i < taskingSlots.Length; i++) { taskingSlots[i] = null; taskingSelects[i] = null; taskingIds[i] = -1; }
        }

        // ---- Build -----------------------------------------------------------------------------

        private void BuildSpaceOpsPage(AvFlow actions)
        {
            spaceBanner = BuildArmedBanner(actions);
            spaceLaunch = actions.Buttons(new AvControl.Spec("LAUNCH CORE", () =>
            {
                support.RequestCoreLaunch();
                nextRefresh = 0f;
            }, AvButtonStyle.Primary, AvIcon.Satellite));
            spaceLaunch.Controls[0].Help = "Launch " + OrbitalPlatform.Callsign + "'s core directly; every other module docks to it.";

            spaceAbilitiesSection = actions.Section(AvIcon.Bolt, "STATION ABILITIES", "");

            var strikes = new List<SupportActionDefinition>(4);
            foreach (SupportActionDefinition action in support.Actions)
                if (SupportManager.OrbitalAbility(action.Id).HasValue) strikes.Add(action);
            foreach (SupportActionDefinition action in strikes)
            {
                SupportActionId id = action.Id;
                var row = new SpaceStrikeRow { Action = action, Row = actions.Add(new ActionTile(actions.Content, AbilityIcon(action))) };
                row.Button = row.Row.AddTrailing(new AvControl.Spec("ARM", () =>
                {
                    if (support.ArmedAction.HasValue && support.ArmedAction.Value == id) support.Disarm();
                    else support.Arm(id);
                    nextRefresh = 0f;
                }));
                spaceStrikeRows.Add(row);
            }

            spaceFiresSection = actions.Section(AvIcon.CurrentLocation, "OFFBOARD FIRES", "");
            foreach (SupportActionDefinition action in support.Actions)
            {
                if (action.Id != SupportActionId.Prsm && action.Id != SupportActionId.Cruise) continue;
                SupportActionId id = action.Id;
                var row = new SpaceStrikeRow { Action = action, Row = actions.Add(new ActionTile(actions.Content, AbilityIcon(action))) };
                row.Button = row.Row.AddTrailing(new AvControl.Spec("ARM", () =>
                {
                    if (support.ArmedAction.HasValue && support.ArmedAction.Value == id) support.Disarm();
                    else support.Arm(id);
                    nextRefresh = 0f;
                }));
                spaceStrikeRows.Add(row);
            }

            strikeStrip = actions.Add(new BriefCard(actions.Content));

            actions.Section(AvIcon.MapPin, TaskingSectionTitle, "");
            for (int i = 0; i < taskingSlots.Length; i++)
            {
                int slot = i;
                taskingSlots[i] = actions.Add(new ActionTile(actions.Content, AvIcon.Wind));
                taskingIds[i] = -1;
                taskingSelects[i] = taskingSlots[i].AddTrailing(new AvControl.Spec("SELECT", () =>
                {
                    taskingSelected = taskingIds[slot] != taskingSelected ? taskingIds[slot] : -1;
                    nextRefresh = 0f;
                }));
            }
            taskingButtons = actions.Buttons(
                new AvControl.Spec("ADD LEG", () =>
                {
                    if (taskingSelected == -1) return;
                    int strike = taskingSelected;
                    support.ArmLocalPick("CRUISE WAYPOINT",
                        point => support.SendWaypoint(strike, point, false));
                    nextRefresh = 0f;
                }),
                new AvControl.Spec("CLEAR", () =>
                {
                    if (taskingSelected == -1) return;
                    support.SendWaypoint(taskingSelected, default(GlobalPosition), true);
                    nextRefresh = 0f;
                }));
            taskingHint = actions.Add(new BriefCard(actions.Content));
        }

        private void SetSpaceActionParts(bool station)
        {
            spaceLaunch.SetShown(!station);
            spaceAbilitiesSection.SetShown(station);
            spaceFiresSection.SetShown(true);
            foreach (SpaceStrikeRow row in spaceStrikeRows)
                row.Row.SetShown(station || !SupportManager.OrbitalAbility(row.Action.Id).HasValue);
            strikeStrip.SetShown(true);
        }

        // ---- Refresh ---------------------------------------------------------------------------

        private void RefreshSpaceOpsPage(bool bypass, OrbitalPlatform platform, double now)
        {
            if (strikeStrip == null) return;
            bool station = platform != null && platform.Exists;
            SetSpaceActionParts(station);
            PlatformHold hold = station ? platform.HoldAt(now) : PlatformHold.None;

            string headline, text;
            AvState tone;
            if (!station)
            {
                headline = "NO STATION ON ORBIT";
                text = "Station abilities need " + OrbitalPlatform.Callsign + "'s core in orbit. Launch it with LAUNCH CORE. PRSM and cruise fire offboard and need no station.";
                tone = AvState.Inert;
            }
            else if (platform.Brownout)
            {
                headline = "✕ BROWNOUT";
                text = "Abilities are refused until the cells recharge.";
                tone = AvState.Danger;
            }
            else if (hold != PlatformHold.None)
            {
                headline = PlatformWords.Hold(hold);
                text = "Abilities offline until T-" + PlatformWords.Clock(platform.CycleStart - now) + ".";
                tone = AvState.Info;
            }
            else
            {
                headline = "ON STATION";
                text = "Arm an ability, then right-click the map.";
                tone = AvState.Ready;
            }
            PaintBanner(spaceBanner, TabSpace, headline, text, tone);
            // Offboard rows paint without a station; station rows are gated per row.

            foreach (SpaceStrikeRow row in spaceStrikeRows)
                if (station || !SupportManager.OrbitalAbility(row.Action.Id).HasValue)
                    PaintSpaceStrikeRow(row, bypass);
            RefreshStrikeStrip();
            RefreshTasking((float)now);
        }

        private void PaintSpaceStrikeRow(SpaceStrikeRow row, bool bypass)
        {
            AbilityFacts facts = AbilityStatus.For(support, row.Action, bypass);
            string name = row.Action.Id == SupportActionId.Emp ? row.Action.Name + " · FRIENDLY FIRE" : row.Action.Name;
            PaintAbilityTile(row.Row, row.Button, row.Action, facts, facts.Readiness, "ARM", name);
            SetTileHelp(row.Row, row.Button, row.Action.Name + " — " + row.Action.Description +
                " Arm, then right-click the map. " + facts.Readiness + ".");
        }

        /// <summary>Live friendly fires near the player, from the host's own strike picture.</summary>
        /// <summary>Tier-2 cruise routing: the three newest live strikes, leg counts, select/pick/clear.</summary>
        private void RefreshTasking(float now)
        {
            if (taskingHint == null) return;
            taskingLive.Clear();
            var strikes = support.ActiveStrikes;
            for (int i = strikes.Count - 1; i >= 0 && taskingLive.Count < taskingSlots.Length; i--)
            {
                ActiveStrikeInfo strike = strikes[i];
                if (strike.ActionId != SupportActionId.Cruise || !strike.IsActive(now)) continue;
                taskingLive.Add(strike);
            }
            bool selectedLive = false;
            for (int i = 0; i < taskingLive.Count; i++)
                if (taskingLive[i].RequestId == taskingSelected) selectedLive = true;
            if (!selectedLive) taskingSelected = -1;
            for (int i = 0; i < taskingSlots.Length; i++)
            {
                if (i >= taskingLive.Count)
                {
                    taskingSlots[i].SetShown(false);
                    taskingIds[i] = -1;
                    continue;
                }
                ActiveStrikeInfo strike = taskingLive[i];
                taskingSlots[i].SetShown(true);
                taskingIds[i] = strike.RequestId;
                PaintTaskingSlot(i, strike, now);
            }
            taskingButtons.SetShown(taskingSelected != -1);
            if (taskingSelected != -1) PaintTaskingHint(now);
            else if (taskingLive.Count == 0)
                taskingHint.Set("NO LIVE CRUISE STRIKES", "Launch a CRUISE SALVO, then shape its route here.", AvState.Inert);
            else taskingHint.Set("SELECT A STRIKE", "Tap SELECT, then ADD LEG and right-click the map for each waypoint.", AvState.Inert);
        }

        private void PaintTaskingSlot(int slot, ActiveStrikeInfo strike, float now)
        {
            CruiseLegMirror mirror = support.LegsFor(strike.RequestId);
            int legs = mirror != null ? mirror.Legs.Count : 0;
            int tti = Mathf.CeilToInt(strike.SecondsRemaining(now));
            bool selected = strike.RequestId == taskingSelected;
            string name = "CRUISE · T-" + tti + "s · " + legs + (legs == 1 ? " LEG" : " LEGS");
            string sub = mirror != null && mirror.Dive ? "DIVE · STEEP TERMINAL" : "SKIM · LOW INGRESS";
            taskingSlots[slot].Set(name, sub, "", "", selected ? AvState.Caution : AvState.Inert, AvIcon.Wind);
            taskingSlots[slot].Armed = selected;
            taskingSelects[slot].Latched = selected;
        }

        private void PaintTaskingHint(float now)
        {
            ActiveStrikeInfo? found = null;
            for (int i = 0; i < taskingLive.Count; i++)
                if (taskingLive[i].RequestId == taskingSelected) found = taskingLive[i];
            if (!found.HasValue) return;
            CruiseLegMirror mirror = support.LegsFor(taskingSelected);
            string profile = mirror != null && mirror.Dive
                ? "DIVE · DROPS THROUGH URBAN CANYONS, EXPOSED OVER OPEN GROUND"
                : "SKIM · HIDES UNDER THE RADAR HORIZON, BREAKS ON HIGH TERRAIN";
            if (mirror == null || mirror.Legs.Count == 0)
            {
                taskingHint.Set("DIRECT TO TARGET", profile + ".", AvState.Info);
                return;
            }
            Vector3 goal = found.Value.Target.ToLocalPosition();
            string chain = "";
            for (int i = 0; i < mirror.Legs.Count; i++)
            {
                Vector3 leg = mirror.Legs[i].ToLocalPosition();
                Vector3 next = i + 1 < mirror.Legs.Count ? mirror.Legs[i + 1].ToLocalPosition() : goal;
                float km = Vector3.Distance(leg, next) / 1000f;
                chain += (i == 0 ? "" : " → ") + "L" + (i + 1) + " " + km.ToString("0.0") + "KM";
            }
            int tti = mirror.Tti > 0f ? Mathf.CeilToInt(mirror.Tti) : Mathf.CeilToInt(found.Value.SecondsRemaining(now));
            taskingHint.Set(mirror.Legs.Count + (mirror.Legs.Count == 1 ? " LEG" : " LEGS") + " · T-" + tti + "s",
                chain + " → TGT · " + profile + ".", AvState.Info);
        }

        private void RefreshStrikeStrip()
        {
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null)
            {
                strikeStrip.Set("NO FIRES IN FLIGHT", "Strike telemetry needs the local player.", AvState.Inert);
                return;
            }
            UnityEngine.Vector3 at = player.transform.position;
            if (support.TryGetNear(at.x, at.z, 20000f, out string label, out float seconds))
                strikeStrip.Set("FIRES IN FLIGHT · " + label, "Impact in " + Mathf.CeilToInt(seconds) + " s near your position.", AvState.Info);
            else strikeStrip.Set("NO FIRES IN FLIGHT", "Nothing tasked within 20 km of you.", AvState.Inert);
        }
    }
}
