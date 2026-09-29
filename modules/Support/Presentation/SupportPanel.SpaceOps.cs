using System.Collections.Generic;
using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPACE actions: host-authorized abilities under persistent coverage, as one column of tiles.
    /// Position control opens the station room for explicit sector selection and fitting. Ability
    /// readiness uses the shared presenter (<see cref="AbilityStatus"/>) on every surface. With no
    /// station the page is one card and a LAUNCH CORE button, not a list of locked tiles.
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
        private AvSection spaceAbilitiesSection, spaceRelocateSection;
        private ActionTile spaceUplinkRow, spaceRephaseRow;
        private AvControl spaceUplinkOpen, spaceUplinkAim, spaceRephaseOpen;
        private readonly List<SpaceStrikeRow> spaceStrikeRows = new List<SpaceStrikeRow>(4);

        private void ResetSpaceOpsPage()
        {
            spaceBanner = null;
            spaceLaunch = null;
            spaceAbilitiesSection = spaceRelocateSection = null;
            spaceUplinkRow = spaceRephaseRow = null;
            spaceUplinkOpen = spaceUplinkAim = spaceRephaseOpen = null;
            spaceStrikeRows.Clear();
        }

        // ---- Build -----------------------------------------------------------------------------

        private void BuildSpaceOpsPage(AvFlow actions)
        {
            spaceBanner = BuildArmedBanner(actions);
            spaceLaunch = actions.Buttons(new AvControl.Spec("LAUNCH CORE", OpenEngineering, AvButtonStyle.Primary, AvIcon.Satellite));
            spaceLaunch.Controls[0].Help = "Open Engineering and launch " + OrbitalPlatform.Callsign + "'s core.";

            spaceAbilitiesSection = actions.Section(AvIcon.Bolt, "STATION ABILITIES", "");

            spaceUplinkRow = actions.Add(new ActionTile(actions.Content, AvIcon.Camera));
            spaceUplinkAim = spaceUplinkRow.AddTrailing(new AvControl.Spec("AIM", () =>
            {
                support.ArmLocalPick("UPLINK AIM", point => OpenUplink(point));
                nextRefresh = 0f;
            }, AvButtonStyle.Quiet));
            spaceUplinkAim.Help = "Right-click the map where the sensor should look, then the feed opens there.";
            spaceUplinkOpen = spaceUplinkRow.AddTrailing(new AvControl.Spec("OPEN", () => OpenUplink(null)));

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

            spaceRelocateSection = actions.Section(AvIcon.CurrentLocation, "POSITION CONTROL", "CHOOSE A SECTOR");
            spaceRephaseRow = actions.Add(new ActionTile(actions.Content, AvIcon.CurrentLocation));
            spaceRephaseOpen = spaceRephaseRow.AddTrailing(new AvControl.Spec("OPEN", OpenStationConsole));
        }

        private void SetSpaceActionParts(bool station)
        {
            spaceLaunch.SetShown(!station);
            spaceAbilitiesSection.SetShown(station);
            spaceUplinkRow.SetShown(station);
            foreach (SpaceStrikeRow row in spaceStrikeRows) row.Row.SetShown(station);
            spaceRelocateSection.SetShown(station);
            spaceRephaseRow.SetShown(station);
        }

        // ---- Refresh ---------------------------------------------------------------------------

        private void RefreshSpaceOpsPage(bool bypass, OrbitalPlatform platform, double now)
        {
            if (spaceUplinkRow == null) return;
            bool station = platform != null && platform.Exists;
            SetSpaceActionParts(station);
            PlatformHold hold = station ? platform.HoldAt(now) : PlatformHold.None;

            string headline, text;
            AvState tone;
            if (!station)
            {
                headline = "NO STATION ON ORBIT";
                text = "Station abilities need " + OrbitalPlatform.Callsign + "'s core in orbit. Launch it from Engineering.";
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
            if (!station) return;

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

            foreach (SpaceStrikeRow row in spaceStrikeRows) PaintSpaceStrikeRow(row, bypass);

            bool propulsion = platform.FittedOnline(ModuleKind.Propulsion, now);
            string rword = propulsion ? "CHOOSE DESTINATION ON THE TASK MAP" : "FIT PROPULSION TO MOVE";
            string fuel = PlatformWords.Whole(PlatformAbilities.Info(PlatformAbility.Rephase).Fuel);
            spaceRephaseRow.Set("RELOCATE", rword, fuel + " FUEL", "", propulsion ? AvState.Ready : AvState.Inert, AvIcon.CurrentLocation);
            spaceRephaseRow.Dim = !propulsion;
            SetTileHelp(spaceRephaseRow, spaceRephaseOpen,
                "Open station control to choose a destination sector or fit propulsion. Relocation uses " + fuel + " fuel. " + rword + ".");
        }

        private void PaintSpaceStrikeRow(SpaceStrikeRow row, bool bypass)
        {
            AbilityFacts facts = AbilityStatus.For(support, row.Action, bypass);
            string name = row.Action.Id == SupportActionId.Emp ? row.Action.Name + " · FRIENDLY FIRE" : row.Action.Name;
            PaintAbilityTile(row.Row, row.Button, row.Action, facts, facts.Readiness, "ARM", name);
            SetTileHelp(row.Row, row.Button, row.Action.Name + " — " + row.Action.Description +
                " Arm, then right-click the map or fire from the feed's crosshair. " + facts.Readiness + ".");
        }
    }
}
