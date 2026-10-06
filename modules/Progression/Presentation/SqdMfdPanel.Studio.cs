using NOAvionics;
using System;
using BoscaliSummer.Modules.Progression.Runtime;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    /// <summary>
    /// STUDIO tab: the local squadron emblem editor, plus a launcher for the Wing Command studio
    /// (WMC WING > STUDIO), which owns custom-pilot editing.
    /// </summary>
    internal sealed partial class SqdMfdPanel
    {
        private const float StudioEmblemHeight = 96f;
        private const string StudioQuietLine = "The emblem and squadron name are local cosmetics; nothing is uploaded.";

        /// <summary>How a studio message reads: a success, a caution, or a refusal.</summary>
        private enum StudioTone : byte { Info, Ok, Warn, Bad }

        private bool studioArtDirty = true;
        private AvFlow studioPageFlow;
        private bool studioPageBuilt;
        private string studioMessage = string.Empty;
        private StudioTone studioMessageTone = StudioTone.Info;
        private float studioMessageUntil;

        private SqdEmptyCard studioLauncher;
        private AvTextBlock studioMessageText;
        private AvStepper studioShape, studioCharge, studioPalette, studioArt;
        private AvPortrait studioEmblem;
        private AvField studioSquadronField;

        private void ResetStudioPage()
        {
            studioArtDirty = true;
            studioPageFlow = null;
            studioPageBuilt = false;
            studioMessage = string.Empty;
            studioMessageTone = StudioTone.Info;
            studioMessageUntil = 0f;
            studioLauncher = null;
            studioMessageText = null;
            studioShape = studioCharge = studioPalette = studioArt = null;
            studioEmblem = null;
            studioSquadronField = null;
        }

        // ---- STUDIO page -------------------------------------------------------------------

        private void BuildStudioPage(AvFlow p)
        {
            float half = AvFlowMath.ColumnWidth(p.Inner, 2, AvGridTokens.Gap);

            p.Section(AvIcon.UsersGroup, "CUSTOM PILOTS", "WING COMMAND");
            studioLauncher = p.Add(new SqdEmptyCard(p.Content, AvIcon.Pencil, "PILOTS ARE EDITED IN WING COMMAND",
                "The WMC WING tab has the pilot studio: write, clone, recruit and delete pilots.",
                "OPEN WMC STUDIO", OpenWmcStudio, AvIcon.Pencil));

            p.Section(AvIcon.Flag, "SQUADRON IDENTITY", "LOCAL COSMETIC");
            studioEmblem = new AvPortrait(p.Content, "Emblem", "NO ART", StudioEmblemHeight);
            AvCard squadronCard = new AvCard(p.Content, console.Ticker, half);
            studioSquadronField = squadronCard.Flow.Add(new AvField(squadronCard.Flow.Content, "SQUADRON NAME", 24,
                value => settings.SquadronName.Value = value ?? string.Empty));
            studioSquadronField.Text = squadronName;
            FieldHelp(studioSquadronField, "Local squadron name shown on the pilot status page.");
            AvButtons emblemButtons = squadronCard.Flow.Buttons(
                new AvControl.Spec("ROLL", RandomizeEmblem, AvButtonStyle.Quiet, AvIcon.Refresh),
                new AvControl.Spec("NO ART", ClearArtFile, AvButtonStyle.Quiet));
            emblemButtons.Controls[0].Help = "Roll a new local emblem: shape, mark and palette together.";
            emblemButtons.Controls[1].Help = "Use the procedural emblem instead of a PNG.";
            studioEmblem.Help = "Your squadron emblem, shown on the pilot card. Local cosmetic only; nothing is uploaded.";
            p.Row(studioEmblem, squadronCard);

            studioShape = new AvStepper(p.Content, "SHAPE", () => EmblemDesign.ShapeNames[emblem.Shape],
                () => CycleEmblem(-1, 0, 0), () => CycleEmblem(1, 0, 0));
            studioCharge = new AvStepper(p.Content, "MARK", () => EmblemDesign.ChargeNames[emblem.Charge],
                () => CycleEmblem(0, -1, 0), () => CycleEmblem(0, 1, 0));
            studioPalette = new AvStepper(p.Content, "COLOR",
                () => "PALETTE " + AvNum.Thousands(emblem.Palette + 1) + "/" + AvNum.Thousands(EmblemDesign.PaletteCount),
                () => CycleEmblem(0, 0, -1), () => CycleEmblem(0, 0, 1));
            studioArt = new AvStepper(p.Content, "ART",
                () => emblemFileIndex >= 0 && emblemFileIndex < emblemFiles.Length
                    ? System.IO.Path.GetFileNameWithoutExtension(emblemFiles[emblemFileIndex])
                    : "NONE · PROCEDURAL",
                () => CycleArtFile(-1), () => CycleArtFile(1));
            StepperHelp(studioShape, "emblem shape"); StepperHelp(studioCharge, "emblem mark"); StepperHelp(studioPalette, "palette");
            StepperHelp(studioArt, "PNG in the emblem folder (NONE keeps the procedural emblem)");
            p.Row(studioShape, studioCharge);
            p.Row(studioPalette, studioArt);

            studioMessageText = p.Add(new AvTextBlock(p.Content, AvTextRole.ProseSmall), 1f);
            studioMessageText.Set(StudioQuietLine);
        }

        private static void StepperHelp(AvStepper stepper, string what)
        {
            stepper.Minus.Help = "Previous " + what + ".";
            stepper.Plus.Help = "Next " + what + ".";
        }

        /// <summary>Kit v2's <see cref="AvField"/> has no help setter; the field root receives hover from its frame.</summary>
        private static void FieldHelp(AvField field, string text) => AvHelpTip.Attach(field.Rect.gameObject, text);

        // ---- STUDIO refresh ------------------------------------------------------------------

        private void RefreshStudioPage()
        {
            if (settings == null || studioEmblem == null) return;

            if (studioArtDirty)
            {
                studioArtDirty = false;
                emblemFiles = EmblemRenderer.ScanFiles();
            }

            studioLauncher.Set(WingLink.SquadAvailable ? "PILOTS ARE EDITED IN WING COMMAND" : "WING COMMAND NOT CONNECTED",
                WingLink.SquadAvailable
                    ? "The WMC WING tab has the pilot studio: write, clone, recruit and delete pilots."
                    : WingLink.SquadUnavailableReason);
            studioEmblem.Set(emblemSprite);
            studioShape.Refresh(); studioCharge.Refresh(); studioPalette.Refresh(); studioArt.Refresh();

            bool quiet = string.IsNullOrEmpty(studioMessage);
            studioMessageText.Set(quiet ? StudioQuietLine : studioMessage);
            studioMessageText.Color = quiet ? AvTheme.Dim
                : studioMessageTone == StudioTone.Ok ? AvTheme.RailReady
                : studioMessageTone == StudioTone.Warn ? AvTheme.Warning
                : studioMessageTone == StudioTone.Bad ? AvTheme.Alert
                : AvTheme.Dim;
        }

        // ---- STUDIO actions --------------------------------------------------------------

        private void OpenWmcStudio()
        {
            if (WingLink.OpenPilotStudio()) NextMessage("Opening the Wing Command pilot studio.", StudioTone.Ok);
            else NextMessage("Wing Command could not open the pilot studio.", StudioTone.Warn);
            nextRefresh = 0f;
        }

        private void CycleEmblem(int shape, int charge, int palette)
        {
            emblem = emblem.Cycle(shape, charge, palette);
            settings.Emblem.Value = emblem.Encode();
            NextMessage("Emblem updated: " + EmblemDesign.ShapeNames[emblem.Shape] + " / " +
                EmblemDesign.ChargeNames[emblem.Charge] + ".", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void RandomizeEmblem()
        {
            emblem = EmblemDesign.Random(NextSeed());
            settings.Emblem.Value = emblem.Encode();
            settings.EmblemFile.Value = string.Empty;
            NextMessage("Rolled a new emblem.", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void CycleArtFile(int direction)
        {
            if (emblemFiles.Length == 0)
            {
                NextMessage("No PNG files in the BoscaliSummer\\Emblems config folder.", StudioTone.Warn);
                return;
            }
            int index = emblemFileIndex;
            if (index < 0) index = direction > 0 ? 0 : emblemFiles.Length - 1;
            else index = ((index + direction) % emblemFiles.Length + emblemFiles.Length) % emblemFiles.Length;
            settings.EmblemFile.Value = emblemFiles[index];
            NextMessage("Custom emblem art: " + System.IO.Path.GetFileName(emblemFiles[index]), StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void ClearArtFile()
        {
            settings.EmblemFile.Value = string.Empty;
            NextMessage("Using the procedural emblem.", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void NextMessage(string message, StudioTone tone)
        {
            studioMessage = message ?? string.Empty;
            studioMessageTone = tone;
            studioMessageUntil = Time.unscaledTime + 8f;
        }

        private string StudioStatusLine()
        {
            if (!string.IsNullOrEmpty(studioMessage) && Time.unscaledTime < studioMessageUntil)
                return studioMessage;
            return WingLink.SquadAvailable
                ? "Pilot editing lives in Wing Command; the emblem is edited here."
                : WingLink.SquadUnavailableReason;
        }

        private static int NextSeed() =>
            unchecked(Environment.TickCount ^ (Time.frameCount * 7919) ^ (int)Time.unscaledTime);
    }
}
