using NOAvionics;
using System;
using BoscaliSummer.Modules.Progression.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    /// <summary>
    /// STUDIO tab: the squadron (roster, personnel files and the pilot studio that owns custom-pilot editing; Wing builds it here
    /// through <see cref="IWingSquad.BuildSquadron"/>, it was WMC's WING tab), then the squadron insignia generator (a proof sheet of
    /// fresh rolls over eight hand-set layers), the kneeboard card picker and FLY AS.
    /// </summary>
    internal sealed partial class SqdMfdPanel
    {
        private const float StudioEmblemHeight = 136f;
        private const float StudioKneeboardHeight = 96f;
        private const int ProofCount = 4;
        private const string StudioQuietLine = "Insignia, squadron name and fin paint are local cosmetics; nothing is uploaded.";

        private static readonly string[] LayerLabels = { "SHAPE", "SPLIT", "MARK", "TRIM", "FIELD", "SECOND", "INK", "NUMBER" };
        private static readonly string[] LayerHelp =
        {
            "outer shape", "field split", "centre mark", "trim around the mark", "field colour",
            "second field colour (used by a split field)", "mark and rim colour", "squadron number (NONE leaves it off)",
        };
        private static readonly string[] PaintNames = { "OFF", "COLOUR", "LOW-VIS" };

        /// <summary>How a studio message reads: a success, a caution, or a refusal.</summary>
        private enum StudioTone : byte { Info, Ok, Warn, Bad }

        private bool studioArtDirty = true;
        private AvFlow studioPageFlow;
        private bool studioPageBuilt;
        private string studioMessage = string.Empty;
        private StudioTone studioMessageTone = StudioTone.Info;
        private float studioMessageUntil;

        private IWingSquad wingSquad;
        private AvNote studioBlazon, studioContrast;
        private AvStepper[] studioLayers;
        private AvStepper studioArt, studioPaint, studioFlyAs;
        private AvPortrait studioEmblem;
        private AvField studioSquadronField;
        private SqdProofStrip studioProofs;
        private readonly EmblemDesign[] proofDesigns = new EmblemDesign[ProofCount];
        private readonly EmblemCanvas[] proofCanvases = new EmblemCanvas[ProofCount];
        private AvStepper studioKneeboard;
        private AvPortrait studioKneeboardArt;
        private IKneeboardGallery kneeboardGallery;
        private EmblemDesign blazonFor;
        private bool blazonSet;
        private readonly EmblemDesign[] proofDrawn = new EmblemDesign[ProofCount];
        private readonly bool[] proofDrawnSet = new bool[ProofCount];

        private void ResetStudioPage()
        {
            studioArtDirty = true;
            studioPageFlow = null;
            studioPageBuilt = false;
            studioMessage = string.Empty;
            studioMessageTone = StudioTone.Info;
            studioMessageUntil = 0f;
            wingSquad = null;
            studioBlazon = studioContrast = null;
            studioLayers = null;
            studioArt = studioPaint = studioFlyAs = null;
            studioEmblem = null;
            studioSquadronField = null;
            studioProofs = null;
            studioKneeboard = null; studioKneeboardArt = null; kneeboardGallery = null;
            blazonSet = false;
            Array.Clear(proofDrawnSet, 0, proofDrawnSet.Length);
        }

        // ---- STUDIO page -------------------------------------------------------------------

        private void BuildStudioPage(AvFlow p)
        {
            // The squadron is Wing's; with Wing off it is one dim line.
            if (ModuleServices.TryGet(out wingSquad))
            {
                p.Section(AvIcon.UsersGroup, "SQUADRON", null);
                wingSquad.BuildSquadron(TabStudio, p, console);
            }
            else p.Add(new AvNote(p.Content, AvTextRole.Micro)).Set("SQUADRON · THE WING MODULE IS OFF");

            float half = AvFlowMath.ColumnWidth(p.Inner, 2, AvGridTokens.Gap);

            p.Section(AvIcon.Shield, "SQUADRON INSIGNIA", null);
            studioEmblem = new AvPortrait(p.Content, "Emblem", "NO ART", StudioEmblemHeight);
            studioEmblem.Help = "Your squadron insignia, shown on the pilot card. Local cosmetic only; nothing is uploaded.";
            AvCard squadronCard = new AvCard(p.Content, console.Ticker, half);
            studioSquadronField = squadronCard.Flow.Add(new AvField(squadronCard.Flow.Content, "SQUADRON NAME", 24,
                value => settings.SquadronName.Value = value ?? string.Empty));
            studioSquadronField.Text = squadronName;
            FieldHelp(studioSquadronField, "Local squadron name shown on the pilot status page.");
            studioBlazon = squadronCard.Flow.Add(new AvNote(squadronCard.Flow.Content, AvTextRole.ProseSmall));
            studioContrast = squadronCard.Flow.Add(new AvNote(squadronCard.Flow.Content, AvTextRole.Micro));
            AvButtons emblemButtons = squadronCard.Flow.Buttons(
                new AvControl.Spec("ROLL", RollProofs, AvButtonStyle.Quiet, AvIcon.Refresh),
                new AvControl.Spec("NO ART", ClearArtFile, AvButtonStyle.Quiet));
            emblemButtons.Controls[0].Help = "Print four fresh proofs below. Your insignia stays as it is until you tap one.";
            emblemButtons.Controls[1].Help = "Use the generated insignia instead of a PNG.";
            p.Row(studioEmblem, squadronCard);

            p.Section(AvIcon.Sticker, "PROOF SHEET", "TAP A PROOF TO ADOPT IT");
            studioProofs = p.Add(new SqdProofStrip(p.Content, ProofCount, AdoptProof,
                "Tap to adopt this proof. The insignia you had takes its place on the sheet, so a swap can always be undone."));
            int seed = NextSeed();
            for (int i = 0; i < ProofCount; i++)
            {
                if (proofCanvases[i] == null) proofCanvases[i] = new EmblemCanvas();
                proofDesigns[i] = EmblemDesign.Random(seed + i * 7919);
            }

            p.Section(AvIcon.Stack2, "LAYERS", null);
            studioLayers = new AvStepper[EmblemDesign.LayerCount];
            for (int i = 0; i < studioLayers.Length; i++)
            {
                var layer = (EmblemLayer)i;
                studioLayers[i] = new AvStepper(p.Content, LayerLabels[i], () => emblem.Name(layer),
                    () => StepLayer(layer, -1), () => StepLayer(layer, 1));
                StepperHelp(studioLayers[i], LayerHelp[i]);
                if ((i & 1) == 1) p.Row(studioLayers[i - 1], studioLayers[i]);
            }

            studioArt = new AvStepper(p.Content, "ART",
                () => emblemFileIndex >= 0 && emblemFileIndex < emblemFiles.Length
                    ? System.IO.Path.GetFileNameWithoutExtension(emblemFiles[emblemFileIndex])
                    : "GENERATED",
                () => CycleArtFile(-1), () => CycleArtFile(1));
            studioPaint = new AvStepper(p.Content, "PAINT", () => PaintNames[(int)settings.EmblemPaint.Value],
                () => CyclePaint(-1), () => CyclePaint(1));
            StepperHelp(studioArt, "PNG in the emblem folder (GENERATED keeps the insignia above)");
            StepperHelp(studioPaint, "tail-fin paint: OFF, full COLOUR, or two-grey LOW-VIS. Only you see it, on your own aircraft");
            p.Row(studioArt, studioPaint);

            if (ModuleServices.TryGet(out kneeboardGallery))
            {
                p.Section(AvIcon.Pencil, "KNEEBOARD", null);
                studioKneeboardArt = new AvPortrait(p.Content, "Kneeboard", "WRITTEN", StudioKneeboardHeight);
                studioKneeboard = new AvStepper(p.Content, "CARD", () => kneeboardGallery.Name(kneeboardGallery.Selected),
                    () => kneeboardGallery.Selected--, () => kneeboardGallery.Selected++);
                StepperHelp(studioKneeboard, "kneeboard card. MISSION CARD writes your sortie; the others are pictures strapped to your thigh");
                studioKneeboardArt.Help = "The card on your cockpit kneeboard (look down at your free-hand thigh). Local cosmetic.";
                p.Row(studioKneeboardArt, studioKneeboard);
            }

            p.Section(AvIcon.UsersGroup, "YOUR PILOT", null);
            studioFlyAs = p.Add(new AvStepper(p.Content, "FLY AS",
                () => string.IsNullOrEmpty(settings.PilotProfile.Value) ? "SQUADRON RECORD" : settings.PilotProfile.Value.ToUpperInvariant(),
                () => StepFlyAs(-1), () => StepFlyAs(1)));
            StepperHelp(studioFlyAs, "saved pilot to fly as (SQUADRON RECORD shows the host's generated pilot)");

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

            studioEmblem.Set(emblemSprite);
            if (!blazonSet || emblem != blazonFor)
            {
                blazonSet = true;
                blazonFor = emblem;
                studioBlazon.Set(emblem.Blazon());
                studioContrast.Set(emblem.Legible ? "CONTRAST OK · READS AT DISTANCE" : "CONTRAST LOW · MARK MAY NOT READ",
                    emblem.Legible ? AvState.Ready : AvState.Caution);
            }
            for (int i = 0; i < studioLayers.Length; i++) studioLayers[i].Refresh();
            studioArt.Refresh(); studioPaint.Refresh(); studioFlyAs.Refresh();
            for (int i = 0; i < ProofCount; i++)
            {
                if (proofDrawnSet[i] && proofDrawn[i] == proofDesigns[i]) continue;
                proofDrawnSet[i] = true;
                proofDrawn[i] = proofDesigns[i];
                studioProofs.Set(i, proofCanvases[i].Draw(proofDesigns[i]));
            }
            if (studioKneeboard != null && kneeboardGallery != null)
            {
                studioKneeboard.Refresh();
                studioKneeboardArt.Set(kneeboardGallery.Preview(kneeboardGallery.Selected));
            }
        }

        // ---- STUDIO actions --------------------------------------------------------------

        /// <summary>FLY AS: the squadron record, then each saved pilot in studio order.</summary>
        private void StepFlyAs(int direction)
        {
            string[] pilots = WingLink.SavedPilotCallsigns();
            if (pilots.Length == 0)
            {
                NextMessage("No saved pilots yet: open STUDIO in SQUADRON above and write one.", StudioTone.Warn);
                return;
            }
            int at = Array.FindIndex(pilots, c => string.Equals(c, settings.PilotProfile.Value, StringComparison.OrdinalIgnoreCase)) + 1;
            int count = pilots.Length + 1;
            at = ((at + direction) % count + count) % count;
            settings.PilotProfile.Value = at == 0 ? string.Empty : pilots[at - 1];
            NextMessage(at == 0 ? "You fly as the squadron record." : "You fly as " + pilots[at - 1] + ".", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void StepLayer(EmblemLayer layer, int delta)
        {
            SetEmblem(emblem.With(layer, delta));
            string set = LayerLabels[(int)layer] + ": " + emblem.Name(layer) + ".";
            if (emblem.Legible) NextMessage(set, StudioTone.Ok);
            else NextMessage(set + " The mark is close in tone to its field and may not read at distance.", StudioTone.Warn);
        }

        private void SetEmblem(EmblemDesign design)
        {
            emblem = design;
            settings.Emblem.Value = emblem.Encode();
            nextRefresh = 0f;
        }

        private void RollProofs()
        {
            int seed = NextSeed();
            for (int i = 0; i < ProofCount; i++) proofDesigns[i] = EmblemDesign.Random(seed + i * 7919);
            NextMessage("Four fresh proofs printed. Tap one to adopt it.", StudioTone.Ok);
            nextRefresh = 0f;
        }

        /// <summary>The adopted proof and the current insignia trade places, so the sheet doubles as undo.</summary>
        private void AdoptProof(int index)
        {
            if (index < 0 || index >= ProofCount) return;
            EmblemDesign previous = emblem;
            SetEmblem(proofDesigns[index]);
            proofDesigns[index] = previous;
            settings.EmblemFile.Value = string.Empty;
            NextMessage("Adopted. The insignia you had is on the sheet in its place.", StudioTone.Ok);
        }

        private void CyclePaint(int direction)
        {
            int count = PaintNames.Length;
            var next = (EmblemPaint)((((int)settings.EmblemPaint.Value + direction) % count + count) % count);
            settings.EmblemPaint.Value = next;
            NextMessage(next == EmblemPaint.Off ? "Tail-fin paint off."
                : "Tail-fin paint: " + PaintNames[(int)next] + ". Only you see it, on your own aircraft.",
                StudioTone.Ok);
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
            NextMessage("Using the generated insignia.", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void NextMessage(string message, StudioTone tone)
        {
            studioMessage = message ?? string.Empty;
            studioMessageTone = tone;
            studioMessageUntil = Time.unscaledTime + 8f;
        }

        private bool StudioMessageLive => !string.IsNullOrEmpty(studioMessage) && Time.unscaledTime < studioMessageUntil;

        private string StudioStatusLine()
        {
            if (StudioMessageLive) return studioMessage;
            // While the paint shop is on, its own words say whether the fin really carries the insignia.
            if (settings != null && settings.EmblemPaint.Value != EmblemPaint.Off && Visuals.TailPaint.Status.Length > 0)
                return Visuals.TailPaint.Status + ". Only you see it.";
            return wingSquad?.SquadronLine ?? StudioQuietLine;
        }

        private static int NextSeed() =>
            unchecked(Environment.TickCount ^ (Time.frameCount * 7919) ^ (int)Time.unscaledTime);
    }
}
