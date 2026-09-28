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
    /// SPACE actions: host-authorized abilities under persistent coverage. Position control opens
    /// the station room for explicit sector selection and fitting. Ability readiness uses the
    /// shared presenter (<see cref="AbilityStatus"/>) on every surface.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private sealed class SpaceStrikeRow
        {
            public SupportActionDefinition Action;
            public AvRow Row;
            public AvControl Button;
        }

        private HintLine spaceHint;
        private AvRow spaceUplinkRow, spaceRephaseRow;
        private AvControl spaceUplinkOpen, spaceUplinkAim, spaceRephaseOpen;
        private readonly List<SpaceStrikeRow> spaceStrikeRows = new List<SpaceStrikeRow>(4);

        private void ResetSpaceOpsPage()
        {
            spaceHint = null;
            spaceUplinkRow = spaceRephaseRow = null;
            spaceUplinkOpen = spaceUplinkAim = spaceRephaseOpen = null;
            spaceStrikeRows.Clear();
        }

        // ---- Build -----------------------------------------------------------------------------

        private void BuildSpaceOpsPage(AvFlow actions)
        {
            actions.Section(AvIcon.Bolt, "STATION ABILITIES", "");
            spaceHint = actions.Add(new HintLine(actions.Content));

            spaceUplinkRow = actions.Add(new AvRow(actions.Content));
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
                var row = new SpaceStrikeRow { Action = action, Row = actions.Add(new AvRow(actions.Content)) };
                row.Button = row.Row.AddTrailing(new AvControl.Spec("ARM", () =>
                {
                    if (support.ArmedAction.HasValue && support.ArmedAction.Value == id) support.Disarm();
                    else support.Arm(id);
                    nextRefresh = 0f;
                }));
                spaceStrikeRows.Add(row);
            }

            actions.Section(AvIcon.CurrentLocation, "POSITION CONTROL", "CHOOSE A SECTOR");
            spaceRephaseRow = actions.Add(new AvRow(actions.Content));
            spaceRephaseOpen = spaceRephaseRow.AddTrailing(new AvControl.Spec("OPEN", OpenStationConsole));
        }

        // ---- Refresh ---------------------------------------------------------------------------

        private void RefreshSpaceOpsPage(bool bypass, OrbitalPlatform platform, double now)
        {
            if (spaceUplinkRow == null) return;
            bool station = platform != null && platform.Exists;
            PlatformHold hold = station ? platform.HoldAt(now) : PlatformHold.None;

            string hint;
            AvState tone;
            if (!station) { hint = "NO STATION · LAUNCH A CORE IN ENGINEERING"; tone = AvState.Inert; }
            else if (platform.Brownout) { hint = "BROWNOUT · ABILITIES REFUSED UNTIL CELLS RECHARGE"; tone = AvState.Danger; }
            else if (hold != PlatformHold.None)
                hint = PlatformWords.Hold(hold) + " · ABILITIES OFFLINE UNTIL T-" + PlatformWords.Clock(platform.CycleStart - now);
            else hint = "ON STATION · ARM, THEN RIGHT-CLICK THE MAP";
            tone = !station ? AvState.Inert : platform.Brownout ? AvState.Danger : hold != PlatformHold.None ? AvState.Info : AvState.Ready;
            spaceHint.Set(hint, tone);

            PlatformDenial denial = support.PlatformCheck(PlatformAbility.Uplink);
            bool fitted = station && platform.Fitted(ModuleKind.Imager);
            string uplinkWord = denial == PlatformDenial.None ? "READY · FEED LIVE"
                : PlatformWords.Denial(denial, platform, PlatformAbility.Uplink, now);
            AvState uplinkState = denial == PlatformDenial.None ? AvState.Ready : fitted ? AvState.Info : AvState.Inert;
            spaceUplinkRow.Set("SENSOR UPLINK", uplinkWord, "2.0 KW", uplinkState);
            spaceUplinkOpen.Interactable = fitted;
            spaceUplinkAim.Interactable = fitted;
            SetRowHelp(spaceUplinkRow, spaceUplinkOpen,
                "Open the sensor feed: drag or WASD to slew, wheel to zoom, 1-5 to task at the crosshair. " + uplinkWord + ".");

            foreach (SpaceStrikeRow row in spaceStrikeRows) PaintSpaceStrikeRow(row, bypass);

            string rword = !station ? "OPEN STATION CONTROL · LAUNCH A CORE"
                : platform.FittedOnline(ModuleKind.Propulsion, now) ? "CHOOSE DESTINATION ON THE TASK MAP"
                : "OPEN STATION CONTROL · FIT PROPULSION TO MOVE";
            spaceRephaseRow.Set("RELOCATE", rword, "", AvState.Info);
            SetRowHelp(spaceRephaseRow, spaceRephaseOpen,
                "Open station control to choose a destination sector or fit propulsion. Relocation uses " +
                PlatformWords.Whole(PlatformAbilities.Info(PlatformAbility.Rephase).Fuel) + " fuel. " + rword + ".");
        }

        private void PaintSpaceStrikeRow(SpaceStrikeRow row, bool bypass)
        {
            AbilityFacts facts = AbilityStatus.For(support, row.Action, bypass);
            string name = row.Action.Id == SupportActionId.Emp ? row.Action.Name + " · FRIENDLY FIRE" : row.Action.Name;
            row.Row.Set(name, facts.Readiness, facts.CostText, ToState(facts.Tone));
            row.Button.Interactable = facts.Enabled;
            row.Button.Latched = facts.Armed;
            row.Button.Label = facts.Armed ? "ABORT" : "ARM";
            SetRowHelp(row.Row, row.Button, row.Action.Name + " — " + row.Action.Description +
                " Arm, then right-click the map or fire from the feed's crosshair. " + facts.Readiness + ".");
        }
    }
}
