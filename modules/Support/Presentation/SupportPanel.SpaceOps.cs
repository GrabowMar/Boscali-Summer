using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Orbital;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Runtime;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// SPACE actions: the armed banner, the readiness strip, the power-focus strip with the
    /// targeting solution and retask clocks, then one column of ability tiles (uplink, the station
    /// abilities, relocation), the offboard FIRES rows with the strike strip and cruise tasking,
    /// and the allocation history. Position control opens the station room for explicit sector
    /// selection and fitting. Ability readiness uses the shared presenter
    /// (<see cref="AbilityStatus"/>) on every surface. With no station the page is the banner,
    /// a LAUNCH CORE button, the offboard fires and the history, not a list of locked tiles.
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
        private AvSegmented spaceFocus;
        private AvHazardBar spaceSolutionBar, spaceRetaskBar;
        private AvLineChart spaceAllocChart;
        private static readonly string[] FocusWords = { "SURVEY", "STRIKE", "SCREEN" };
        private static readonly string[] FocusTips =
        {
            "SURVEY: wide radar, ELINT and MTI scans, 1.2x reach, 0.75x recharge. Route power here to find targets.",
            "STRIKE: work the fire-control desk to bank tighter rod accuracy. Baseline rods work with a magazine.",
            "SCREEN: balance tracking, charge and heat to bank a stronger EMP. Baseline EMP works with its hardware.",
        };
        private ActionTile spaceUplinkRow, spaceRephaseRow;
        private AvControl spaceUplinkOpen, spaceUplinkAim, spaceRephaseOpen;
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
            spaceFocus = null;
            spaceSolutionBar = spaceRetaskBar = null;
            spaceAllocChart = null;
            spaceUplinkRow = spaceRephaseRow = null;
            spaceUplinkOpen = spaceUplinkAim = spaceRephaseOpen = null;
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

            AddReadyStrip(actions, TabSpace, CountSpaceStrikes());

            spaceFocus = actions.Add(AvSegmented.Strip(actions.Content, FocusWords, FocusIndex, SelectFocus));
            for (int i = 0; i < spaceFocus.Options.Length; i++) spaceFocus.Options[i].Help = FocusTips[i];
            spaceSolutionBar = new AvHazardBar(actions.Content, "SOLUTION") { Help =
                "Fresh radar, ELINT, MTI or field recon improves each TRACK correction in the fire-control desk. Optional preparation; expires after 75 s." };
            spaceRetaskBar = new AvHazardBar(actions.Content, "RETASK") { Help =
                "Mission profile changes settle in 2 seconds. A banked package holds its profile until used or expired." };
            actions.Row(spaceSolutionBar, spaceRetaskBar);

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
            spaceRephaseRow = actions.Add(new ActionTile(actions.Content, AvIcon.CurrentLocation));
            spaceRephaseOpen = spaceRephaseRow.AddTrailing(new AvControl.Spec("OPEN", OpenStationConsole));
            spaceAllocChart = AddTrend(actions);
        }

        private int FocusIndex()
        {
            OrbitalPlatform platform = support != null ? support.LocalPlatform : null;
            return platform != null && platform.Exists ? (int)platform.Focus : -1;
        }

        private void SelectFocus(int index)
        {
            OrbitalPlatform platform = support != null ? support.LocalPlatform : null;
            PlatformFocus focus = (PlatformFocus)Mathf.Clamp(index, 0, 2);
            if (platform == null || !platform.Exists || support.CommandPending || support.RequestPending ||
                !support.LocalMayStationControl || platform.Focus == focus || support.OrbitNow < platform.RetaskUntil) return;
            support.RequestPlatformFocus(focus);
            nextRefresh = 0f;
        }

        private int CountSpaceStrikes()
        {
            int n = 0;
            foreach (SupportActionDefinition action in support.Actions)
                if (SupportManager.OrbitalAbility(action.Id).HasValue) n++;
            return n;
        }

        private void SetSpaceActionParts(bool station)
        {
            if (readySummaries[TabSpace] != null) { readySummaries[TabSpace].SetShown(station); readyBars[TabSpace].SetShown(station); }
            spaceLaunch.SetShown(!station);
            spaceAbilitiesSection.SetShown(station);
            spaceFiresSection.SetShown(true);
            spaceFocus.SetShown(station);
            spaceSolutionBar.SetShown(station);
            spaceRetaskBar.SetShown(station);
            spaceUplinkRow.SetShown(station);
            foreach (SpaceStrikeRow row in spaceStrikeRows)
                row.Row.SetShown(station || !SupportManager.OrbitalAbility(row.Action.Id).HasValue);
            spaceRephaseRow.SetShown(station);
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
                text = "RECHARGING";
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
                text = "ARM · RIGHT-CLICK MAP";
                tone = AvState.Ready;
            }
            PaintBanner(spaceBanner, TabSpace, headline, text, tone);
            PaintTrend(spaceAllocChart, allocationTrend, "");
            ReadyBegin(TabSpace);
            for (int i = 0; i < spaceStrikeRows.Count; i++)
                if (station || !SupportManager.OrbitalAbility(spaceStrikeRows[i].Action.Id).HasValue)
                    ReadyCell(TabSpace, i, PaintSpaceStrikeRow(spaceStrikeRows[i], bypass));
            ReadyEnd(TabSpace);
            RefreshStrikeStrip();
            RefreshTasking((float)now);
            if (!station) return;

            PaintFocus(platform, now);

            PlatformDenial denial = support.PlatformCheck(PlatformAbility.Uplink);
            bool fitted = platform.Fitted(ModuleKind.Imager);
            string uplinkWord = denial == PlatformDenial.None ? "READY · FEED LIVE"
                : PlatformWords.Denial(denial, platform, PlatformAbility.Uplink, now);
            AvState uplinkState = denial == PlatformDenial.None ? AvState.Ready : fitted ? AvState.Info : AvState.Inert;
            spaceUplinkRow.Set("SENSOR UPLINK", uplinkWord, "2.0 KW", "", uplinkState, AvIcon.Camera);
            spaceUplinkRow.Dim = !fitted;
            spaceUplinkOpen.Interactable = fitted;
            spaceUplinkAim.Interactable = fitted;
            SetTileHelp(spaceUplinkRow, spaceUplinkOpen,
                "Open the sensor feed: drag or WASD to slew, wheel to zoom, 1-5 to task at the crosshair. " + uplinkWord + ".");


            bool propulsion = platform.FittedOnline(ModuleKind.Propulsion, now);
            string rword = propulsion ? "PICK A SECTOR" : "FIT PROPULSION";
            string fuel = PlatformWords.Whole(PlatformAbilities.Info(PlatformAbility.Rephase).Fuel);
            spaceRephaseRow.Set("RELOCATE", rword, fuel + " FUEL", "", propulsion ? AvState.Ready : AvState.Inert, AvIcon.CurrentLocation);
            spaceRephaseRow.Dim = !propulsion;
            SetTileHelp(spaceRephaseRow, spaceRephaseOpen,
                "Open station control to choose a destination sector or fit propulsion. Relocation uses " + fuel + " fuel. " + rword + ".");
        }

        /// <summary>The power-focus strip and its two clocks: how long the targeting solution holds, and the retask lock.</summary>
        private void PaintFocus(OrbitalPlatform platform, double now)
        {
            spaceFocus.Refresh();
            double retask = Math.Max(0.0, platform.RetaskUntil - now);
            foreach (AvControl option in spaceFocus.Options) option.Interactable = retask <= 0.0 && !support.CommandPending &&
                support.LocalMayStationControl && platform.BoostRemaining(now) <= 0;

            double solution = platform.SolutionRemaining(now);
            if (solution > 0.0)
                spaceSolutionBar.Set((float)(solution / OrbitalPlatform.SolutionSeconds),
                    PlatformWords.Clock(solution) + " · " + Mathf.RoundToInt(platform.SolutionRadius / 1000f) + " KM", AvState.Ready);
            else spaceSolutionBar.Set(0f, "NONE", AvState.Inert);

            if (retask > 0.0) spaceRetaskBar.Set((float)(1.0 - retask / OrbitalPlatform.RetaskSeconds), "T-" + Mathf.CeilToInt((float)retask) + "s", AvState.Caution);
            else spaceRetaskBar.Set(1f, "READY", AvState.Ready);
        }

        private AbilityFacts PaintSpaceStrikeRow(SpaceStrikeRow row, bool bypass)
        {
            AbilityFacts facts = AbilityStatus.For(support, row.Action, bypass);
            string name = row.Action.Id == SupportActionId.Emp ? row.Action.Name + " · HOSTILES ONLY" : row.Action.Name;
            PaintAbilityTile(row.Row, row.Button, row.Action, facts, facts.Readiness, "ARM", name);
            SetTileHelp(row.Row, row.Button, row.Action.Name + " — " + row.Action.Description +
                " Arm, then right-click the map or fire from the feed's crosshair. " + facts.Readiness + ".");
            return facts;
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
