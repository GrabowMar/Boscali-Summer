using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Progression.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        private const int StudioRowsPerPage = 2;
        private const float StudioPortraitHeight = 112f;
        private const float StudioEmblemHeight = 96f;

        /// <summary>How a studio message reads: a success, a caution, or a refusal.</summary>
        private enum StudioTone : byte { Info, Ok, Warn, Bad }

        private readonly List<WingPilotRecord> studioPilots = new List<WingPilotRecord>(64);
        private readonly SqdRosterRow[] studioRows = new SqdRosterRow[StudioRowsPerPage];
        private PilotDraft studioDraft = PilotDraft.New();
        private int studioPage;
        private int studioSelected = -1;
        private string studioSelectedCallsign;
        private bool studioDirty = true;
        private bool studioArtDirty = true;
        private AvFlow studioPageFlow;
        private bool studioPageBuilt;
        private string studioMessage = string.Empty;
        private StudioTone studioMessageTone = StudioTone.Info;
        private float studioMessageUntil;
        private string studioPortraitKey;
        private Sprite studioPortraitSprite;
        private string studioEditorKey;
        private string studioDeleteArmedCallsign;
        private float studioDeleteArmedUntil;

        private AvSection studioSection;
        private SqdEmptyCard studioUnavailable;
        private SqdEmptyCard studioEmpty;
        private AvButtons studioPager;
        private AvControl studioPagerPrev;
        private AvControl studioPagerNext;
        private AvTextBlock studioMessageText;
        private AvStepper studioBody, studioFace, studioHair, studioSuit, studioBack, studioStyle;
        private AvStepper studioShape, studioCharge, studioPalette, studioArt;
        private AvPortrait studioPortrait;
        private AvPortrait studioEmblem;
        private AvField studioCallsignField;
        private AvField studioNameField;
        private AvField studioBioField;
        private AvField studioSquadronField;
        private AvControl studioDeleteButton;
        private AvControl studioRecruitButton;
        private AvControl studioProfileButton;

        private void ResetStudioPage()
        {
            studioPilots.Clear();
            studioDraft = PilotDraft.New();
            studioPage = 0;
            studioSelected = -1;
            studioSelectedCallsign = null;
            studioDirty = true;
            studioArtDirty = true;
            studioPageFlow = null;
            studioPageBuilt = false;
            studioMessage = string.Empty;
            studioMessageTone = StudioTone.Info;
            studioMessageUntil = 0f;
            studioPortraitKey = null;
            studioPortraitSprite = null;
            studioEditorKey = null;
            studioDeleteArmedCallsign = null;
            studioDeleteArmedUntil = 0f;
            studioSection = null;
            studioUnavailable = null;
            studioEmpty = null;
            studioPager = null;
            studioPagerPrev = null;
            studioPagerNext = null;
            studioMessageText = null;
            studioBody = studioFace = studioHair = studioSuit = studioBack = studioStyle = null;
            studioShape = studioCharge = studioPalette = studioArt = null;
            studioPortrait = null;
            studioEmblem = null;
            studioCallsignField = null;
            studioNameField = null;
            studioBioField = null;
            studioSquadronField = null;
            studioDeleteButton = null;
            studioRecruitButton = null;
            studioProfileButton = null;
            Array.Clear(studioRows, 0, studioRows.Length);
        }

        // ---- STUDIO page -------------------------------------------------------------------

        private void BuildStudioPage(AvFlow p)
        {
            if (!WingLink.PilotStudioAvailable)
            {
                p.Section(AvIcon.Pencil, "PILOT STUDIO", "UNAVAILABLE");
                studioUnavailable = p.Add(new SqdEmptyCard(p.Content, AvIcon.Unlink, "WING COMMAND NOT CONNECTED",
                    WingLink.PilotStudioUnavailableReason));
                return;
            }

            float half = AvFlowMath.ColumnWidth(p.Inner, 2, AvGridTokens.Gap);

            studioSection = p.Section(AvIcon.UsersGroup, "CUSTOM PILOTS", "WING COMMAND CONNECTED");

            AvButtons actions = p.Buttons(
                new AvControl.Spec("NEW", NewStudioDraft, AvButtonStyle.Quiet, AvIcon.Plus),
                new AvControl.Spec("CLONE", CloneStudioDraft, AvButtonStyle.Quiet),
                new AvControl.Spec("DELETE", DeleteStudioDraft, AvButtonStyle.Danger, AvIcon.X),
                new AvControl.Spec("IMPORT ALL", ImportAllCustomPilots, AvButtonStyle.Quiet));
            actions.Controls[0].Help = "Start a new custom pilot draft.";
            actions.Controls[1].Help = "Copy the selected pilot into a new draft.";
            studioDeleteButton = actions.Controls[2];
            studioDeleteButton.Help = "Delete the selected custom pilot file. Press twice to confirm.";
            actions.Controls[3].Help = "Recruit every custom pilot not already in the Wing Command squadron.";

            studioEmpty = p.Add(new SqdEmptyCard(p.Content, AvIcon.UsersGroup, "NO CUSTOM PILOTS YET",
                "Press NEW to draft one, or IMPORT ALL to recruit the files already in the Wing Command folder."));
            for (int i = 0; i < StudioRowsPerPage; i++)
            {
                int index = i;
                studioRows[i] = p.Add(new SqdRosterRow(p.Content, false, () => SelectStudio(index)));
                studioRows[i].Help = "Open this custom pilot in the editor. Saving is local-only.";
            }

            studioPager = p.Buttons(
                new AvControl.Spec("PREVIOUS", () => ChangeStudioPage(-1), AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec("NEXT", () => ChangeStudioPage(1), AvButtonStyle.Quiet, AvIcon.ChevronRight, true));
            studioPagerPrev = studioPager.Controls[0];
            studioPagerNext = studioPager.Controls[1];

            studioPortrait = new AvPortrait(p.Content, "StudioPilot", "NO PORTRAIT", StudioPortraitHeight);
            AvCard who = new AvCard(p.Content, console.Ticker, half);
            studioCallsignField = who.Flow.Add(new AvField(who.Flow.Content, "CALLSIGN", PilotDraft.MaxCallsign,
                value => { studioDraft.Callsign = value; studioDraft.Normalize(); }));
            FieldHelp(studioCallsignField, "Pilot callsign, up to 14 characters. Press Enter to keep an edit.");
            studioNameField = who.Flow.Add(new AvField(who.Flow.Content, "NAME", PilotDraft.MaxName,
                value => { studioDraft.Name = value; studioDraft.Normalize(); }));
            FieldHelp(studioNameField, "Pilot name, up to 24 characters. Press Enter to keep an edit.");
            AvButtons rolls = who.Flow.Buttons(
                new AvControl.Spec("CALL", RandomizeCallsign, AvButtonStyle.Quiet, AvIcon.Refresh),
                new AvControl.Spec("NAME", RandomizeName, AvButtonStyle.Quiet, AvIcon.Refresh));
            rolls.Controls[0].Help = "Roll a new random callsign.";
            rolls.Controls[1].Help = "Roll a new random name.";
            studioPortrait.Help = "The portrait Wing Command will draw for this pilot. Change it with the six steppers below.";
            p.Row(studioPortrait, who);

            studioBody = new AvStepper(p.Content, "BODY", () => WingLink.PortraitBodyLabel(studioDraft.Body),
                () => CycleDraft(d => d.CycleBody(-1, BodyCount())), () => CycleDraft(d => d.CycleBody(1, BodyCount())));
            studioFace = new AvStepper(p.Content, "FACE",
                () => "FACE " + AvNum.Thousands(studioDraft.Face + 1) + "/" + AvNum.Thousands(FaceCount()),
                () => CycleDraft(d => d.CycleFace(-1, FaceCount())), () => CycleDraft(d => d.CycleFace(1, FaceCount())));
            studioHair = new AvStepper(p.Content, "HAIR",
                () => studioDraft.Hair == 0 ? "BALD" : "HAIR " + AvNum.Thousands(studioDraft.Hair) + "/" + AvNum.Thousands(HairCount() - 1),
                () => CycleDraft(d => d.CycleHair(-1, HairCount())), () => CycleDraft(d => d.CycleHair(1, HairCount())));
            studioSuit = new AvStepper(p.Content, "SUIT", () => WingLink.PortraitUniformLabel(studioDraft.Uniform),
                () => CycleDraft(d => d.CycleUniform(-1, UniformCount())), () => CycleDraft(d => d.CycleUniform(1, UniformCount())));
            studioBack = new AvStepper(p.Content, "BACK",
                () => "BACK " + AvNum.Thousands(studioDraft.Backdrop + 1) + "/" + AvNum.Thousands(BackdropCount()),
                () => CycleDraft(d => d.CycleBackdrop(-1, BackdropCount())), () => CycleDraft(d => d.CycleBackdrop(1, BackdropCount())));
            studioStyle = new AvStepper(p.Content, "STYLE", () => WingLink.PersonaLabel(studioDraft.Persona).ToUpperInvariant(),
                () => CycleDraft(d => d.CyclePersona(-1)), () => CycleDraft(d => d.CyclePersona(1)));
            StepperHelp(studioBody, "body type"); StepperHelp(studioFace, "face"); StepperHelp(studioHair, "hair");
            StepperHelp(studioSuit, "flight suit"); StepperHelp(studioBack, "portrait backdrop"); StepperHelp(studioStyle, "radio style");
            p.Row(studioBody, studioFace);
            p.Row(studioHair, studioSuit);
            p.Row(studioBack, studioStyle);

            studioBioField = p.Add(new AvField(p.Content, "Service background…", PilotDraft.MaxBackground,
                value => { studioDraft.Background = value; studioDraft.Normalize(); }));
            FieldHelp(studioBioField, "Free-form service record shown on the pilot status page.");

            AvButtons saveRow = p.Buttons(
                new AvControl.Spec("SAVE", SaveStudioDraft, AvButtonStyle.Primary, AvIcon.CircleCheck),
                new AvControl.Spec("PROFILE", SetAsLocalProfile),
                new AvControl.Spec("RECRUIT", ToggleRecruit),
                new AvControl.Spec("BIO", GenerateBio, AvButtonStyle.Quiet, AvIcon.Refresh));
            saveRow.Controls[0].Help = "Write this pilot to the Wing Command custom pilots folder.";
            saveRow.Controls[3].Help = "Write a service background for this pilot from the chosen radio style.";
            studioProfileButton = saveRow.Controls[1];
            studioProfileButton.Help = "Use this pilot's name, callsign, background and portrait as your local pilot profile.";
            studioRecruitButton = saveRow.Controls[2];
            studioRecruitButton.Help = "Add or remove this pilot from the Wing Command squadron.";

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
            studioMessageText.Set("Custom pilots are local files read by Wing Command; nothing is uploaded.");
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
            if (settings == null) return;
            if (studioSection == null)
            {
                // Wing Command companion API absent: the card built above explains why.
                studioUnavailable?.Set("WING NOT CONNECTED", WingLink.PilotStudioUnavailableReason);
                return;
            }

            if (studioArtDirty)
            {
                studioArtDirty = false;
                emblemFiles = WingLink.PilotStudioAvailable ? EmblemRenderer.ScanFiles() : Array.Empty<string>();
            }
            if (studioDirty)
            {
                studioDirty = false;
                ReloadStudioPilots();
            }

            int pageCount = Math.Max(1, (studioPilots.Count + StudioRowsPerPage - 1) / StudioRowsPerPage);
            studioPage = Mathf.Clamp(studioPage, 0, pageCount - 1);
            studioPager.SetShown(pageCount > 1);
            studioPagerPrev.Interactable = studioPage > 0;
            studioPagerNext.Interactable = studioPage < pageCount - 1;
            studioPagerPrev.Help = studioPage > 0
                ? "Previous page of custom pilots." : "Already on the first page.";
            studioPagerNext.Help = studioPage < pageCount - 1
                ? "Next page of custom pilots." : "Already on the last page.";
            int first = studioPage * StudioRowsPerPage;
            studioSection.SetCaption(studioPilots.Count == 0 ? "NO FILES"
                : AvNum.Thousands(first + 1) + "–" + AvNum.Thousands(Math.Min(first + StudioRowsPerPage, studioPilots.Count)) +
                  " OF " + AvNum.Thousands(studioPilots.Count) + " · WING COMMAND");
            studioEmpty.SetShown(studioPilots.Count == 0);

            for (int i = 0; i < studioRows.Length; i++)
            {
                SqdRosterRow row = studioRows[i];
                int index = first + i;
                bool visible = index < studioPilots.Count;
                row.SetShown(visible);
                if (!visible) continue;
                WingPilotRecord record = studioPilots[index];
                bool inSquad = WingLink.IsPilotRecruited(record.Callsign);
                bool selected = studioSelected == index;
                row.Set(record.Callsign, record.Name + "   ·   " + WingLink.RankNameForXp(record.Xp),
                    inSquad ? "IN SQUADRON" : "READY", selected ? "EDITING" : null,
                    inSquad ? AvState.Ready : AvState.Info);
                row.Armed = selected;
            }

            RefreshStudioEditor();
            if (studioMessageText != null)
            {
                bool quiet = string.IsNullOrEmpty(studioMessage);
                studioMessageText.Set(quiet
                    ? "Custom pilots are local files read by Wing Command; nothing is uploaded."
                    : studioMessage);
                studioMessageText.Color = quiet ? AvTheme.Dim
                    : studioMessageTone == StudioTone.Ok ? AvTheme.RailReady
                    : studioMessageTone == StudioTone.Warn ? AvTheme.Warning
                    : studioMessageTone == StudioTone.Bad ? AvTheme.Alert
                    : AvTheme.Dim;
            }
        }

        private void RefreshStudioEditor()
        {
            studioDraft.Normalize();

            string editorKey = studioDraft.Body + "|" + studioDraft.Face + "|" + studioDraft.Hair + "|" +
                studioDraft.Uniform + "|" + studioDraft.Backdrop + "|" + studioDraft.Accessory + "|" +
                (studioDraft.HasPortrait ? 1 : 0) + "|" + studioDraft.Persona + "|" +
                studioDraft.Name + "|" + studioDraft.Callsign;
            if (!string.Equals(editorKey, studioEditorKey, StringComparison.Ordinal))
            {
                studioEditorKey = editorKey;
                studioBody.Refresh(); studioFace.Refresh(); studioHair.Refresh();
                studioSuit.Refresh(); studioBack.Refresh(); studioStyle.Refresh();

                string portraitKey = studioDraft.HasPortrait
                    ? "p|" + studioDraft.Body + "|" + studioDraft.Face + "|" + studioDraft.Hair + "|" +
                      studioDraft.Uniform + "|" + studioDraft.Accessory + "|" + studioDraft.Backdrop
                    : "i|" + studioDraft.Name + "|" + studioDraft.Callsign;
                if (!string.Equals(portraitKey, studioPortraitKey, StringComparison.Ordinal))
                {
                    studioPortraitKey = portraitKey;
                    studioPortraitSprite = studioDraft.HasPortrait
                        ? WingLink.PilotPortraitForSelection(studioDraft.Body, studioDraft.Face, studioDraft.Hair,
                            studioDraft.Uniform, studioDraft.Accessory, studioDraft.Backdrop)
                        : WingLink.PilotPortrait(studioDraft.Name, studioDraft.Callsign);
                }
                studioPortrait.Set(studioPortraitSprite);
            }

            // Kit v2's AvField exposes no focus accessor, so the callsign/name/bio/squadron
            // fields are pushed from the draft only right after a select/new/clone action
            // (PushDraftToFields) rather than every refresh tick, or a live edit in progress
            // would be overwritten out from under the player. The field itself pushes edits
            // into the draft on submit (Enter), not per keystroke (kit v2 AvField contract).

            studioEmblem.Set(emblemSprite);
            studioShape.Refresh(); studioCharge.Refresh(); studioPalette.Refresh(); studioArt.Refresh();

            bool valid = studioDraft.IsValid;
            bool recruited = valid && WingLink.IsPilotRecruited(studioDraft.Callsign);
            studioRecruitButton.Label = recruited ? "DISCHARGE" : "RECRUIT";
            studioRecruitButton.Interactable = valid;
            studioRecruitButton.Latched = recruited;

            bool profileActive = valid &&
                string.Equals(settings.PilotProfile.Value, studioDraft.Callsign, StringComparison.OrdinalIgnoreCase);
            studioProfileButton.Latched = profileActive;
            studioProfileButton.Interactable = valid;

            bool deleteArmed = valid && studioSelected >= 0 && Time.unscaledTime < studioDeleteArmedUntil &&
                string.Equals(studioDeleteArmedCallsign, studioDraft.Callsign, StringComparison.OrdinalIgnoreCase);
            studioDeleteButton.Interactable = valid && studioSelected >= 0;
            studioDeleteButton.Latched = deleteArmed;
        }

        private void ReloadStudioPilots()
        {
            studioPilots.Clear();
            if (WingLink.TryListCustomPilots(out WingPilotRecord[] records))
            {
                studioPilots.AddRange(records);
                studioPilots.Sort((a, b) => string.Compare(a.Callsign, b.Callsign, StringComparison.OrdinalIgnoreCase));
            }
            studioSelected = -1;
            if (!string.IsNullOrEmpty(studioSelectedCallsign))
            {
                for (int i = 0; i < studioPilots.Count; i++)
                {
                    if (!string.Equals(studioPilots[i].Callsign, studioSelectedCallsign,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    studioSelected = i;
                    studioPage = i / StudioRowsPerPage;
                    break;
                }
            }
        }

        // ---- STUDIO actions --------------------------------------------------------------

        private void ChangeStudioPage(int delta)
        {
            int pageCount = Math.Max(1, (studioPilots.Count + StudioRowsPerPage - 1) / StudioRowsPerPage);
            int wanted = Mathf.Clamp(studioPage + delta, 0, pageCount - 1);
            if (wanted == studioPage) return;
            studioPage = wanted;
            nextRefresh = 0f;
        }

        /// <summary>
        /// Kit v2's AvField has no focus accessor (spec kit gap), so the draft only ever
        /// overwrites the callsign/name/bio boxes right after a select/new/clone action —
        /// never on a refresh tick, or a live edit would be overwritten out from under the
        /// player mid-keystroke.
        /// </summary>
        private void PushDraftToFields()
        {
            if (studioCallsignField == null) return;
            studioCallsignField.Text = studioDraft.Callsign;
            studioNameField.Text = studioDraft.Name;
            studioBioField.Text = studioDraft.Background;
        }

        private void SelectStudio(int index)
        {
            int absolute = studioPage * StudioRowsPerPage + index;
            if (absolute < 0 || absolute >= studioPilots.Count) return;
            studioSelected = absolute;
            studioSelectedCallsign = studioPilots[absolute].Callsign;
            studioDraft = DraftFrom(studioPilots[absolute]);
            studioPortraitKey = null;
            studioEditorKey = null;
            studioDeleteArmedCallsign = null;
            PushDraftToFields();
            nextRefresh = 0f;
        }

        private void NewStudioDraft()
        {
            studioSelected = -1;
            studioSelectedCallsign = null;
            studioDraft = PilotDraft.New();
            studioPortraitKey = null;
            studioEditorKey = null;
            RandomizeLooks();
            PushDraftToFields();
            NextMessage("New pilot draft — set a callsign and save.", StudioTone.Info);
            nextRefresh = 0f;
        }

        private void CloneStudioDraft()
        {
            if (studioSelected < 0 || studioSelected >= studioPilots.Count)
            {
                NextMessage("Select a custom pilot to clone first.", StudioTone.Warn);
                return;
            }
            studioDraft = DraftFrom(studioPilots[studioSelected]);
            string baseCallsign = studioDraft.Callsign;
            int room = PilotDraft.MaxCallsign - 2;
            if (baseCallsign.Length > room) baseCallsign = baseCallsign.Substring(0, room);
            studioDraft.Callsign = baseCallsign + "-2";
            studioDraft.Normalize();
            studioSelected = -1;
            studioSelectedCallsign = null;
            studioPortraitKey = null;
            studioEditorKey = null;
            PushDraftToFields();
            NextMessage("Cloned " + baseCallsign + " — adjust the callsign and save.", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void DeleteStudioDraft()
        {
            if (studioSelected < 0 || studioSelected >= studioPilots.Count)
            {
                NextMessage("Select a custom pilot file to delete first.", StudioTone.Warn);
                return;
            }
            string callsign = studioPilots[studioSelected].Callsign;
            if (!string.Equals(studioDeleteArmedCallsign, callsign, StringComparison.OrdinalIgnoreCase) ||
                Time.unscaledTime >= studioDeleteArmedUntil)
            {
                studioDeleteArmedCallsign = callsign;
                studioDeleteArmedUntil = Time.unscaledTime + 4f;
                NextMessage("Delete " + callsign + "? Press DELETE again within a moment to confirm.",
                    StudioTone.Warn);
                nextRefresh = 0f;
                return;
            }
            studioDeleteArmedCallsign = null;
            studioDeleteArmedUntil = 0f;
            if (WingLink.DeleteCustomPilot(callsign))
            {
                RemoveLocalProfileIf(callsign);
                if (string.Equals(studioSelectedCallsign, callsign, StringComparison.OrdinalIgnoreCase))
                    studioSelectedCallsign = null;
                studioDirty = true;
                NextMessage("Deleted custom pilot " + callsign + ".", StudioTone.Ok);
            }
            else NextMessage("Wing Command could not delete " + callsign + ".", StudioTone.Bad);
            nextRefresh = 0f;
        }

        private void ImportAllCustomPilots()
        {
            int imported = WingLink.ImportAllCustomPilots();
            studioDirty = true;
            NextMessage(imported > 0
                ? "Recruited " + AvNum.Thousands(imported) + " custom pilot(s) to the squadron."
                : "No new custom pilots to recruit.",
                imported > 0 ? StudioTone.Ok : StudioTone.Info);
            nextRefresh = 0f;
        }

        private void SaveStudioDraft()
        {
            studioDraft.Normalize();
            if (!studioDraft.IsValid)
            {
                NextMessage("Enter a callsign before saving.", StudioTone.Warn);
                return;
            }
            if (WingLink.SaveCustomPilot(RecordFrom(studioDraft)))
            {
                studioSelectedCallsign = studioDraft.Callsign;
                studioDirty = true;
                localProfileKey = null; // reload the local profile from the saved file
                NextMessage("Saved " + studioDraft.Callsign + " to the custom pilots folder.", StudioTone.Ok);
            }
            else NextMessage("Wing Command rejected the save — check the BepInEx log.", StudioTone.Bad);
            nextRefresh = 0f;
        }

        private void SetAsLocalProfile()
        {
            studioDraft.Normalize();
            if (!studioDraft.IsValid)
            {
                NextMessage("Enter a callsign before setting a profile.", StudioTone.Warn);
                return;
            }
            settings.PilotProfile.Value = studioDraft.Callsign;
            NextMessage(studioDraft.Callsign + " is now your local pilot profile.", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void ToggleRecruit()
        {
            studioDraft.Normalize();
            if (!studioDraft.IsValid)
            {
                NextMessage("Select a pilot with a callsign first.", StudioTone.Warn);
                return;
            }
            bool recruited = WingLink.IsPilotRecruited(studioDraft.Callsign);
            bool changed = recruited
                ? WingLink.DischargeCustomPilot(studioDraft.Callsign)
                : WingLink.RecruitCustomPilot(studioDraft.Callsign);
            NextMessage(changed
                ? (recruited ? "Discharged " + studioDraft.Callsign + " from the squadron."
                             : "Recruited " + studioDraft.Callsign + " to the squadron.")
                : "Wing Command did not change the squadron roster.",
                changed ? StudioTone.Ok : StudioTone.Warn);
            nextRefresh = 0f;
        }

        private void RandomizeCallsign()
        {
            if (!WingLink.TryCreatePilot(NextSeed(), out _, out string callsign, out _, out _)) return;
            studioDraft.Callsign = callsign;
            studioDraft.Normalize();
            PushDraftToFields();
            NextMessage("Callsign rolled: " + studioDraft.Callsign + ".", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void RandomizeName()
        {
            if (!WingLink.TryCreatePilot(NextSeed(), out string name, out _, out _, out _)) return;
            studioDraft.Name = name;
            studioDraft.Normalize();
            PushDraftToFields();
            NextMessage("Name rolled: " + studioDraft.Name + ".", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void GenerateBio()
        {
            if (!WingLink.TryCreatePilot(NextSeed(), out _, out _, out string background, out int persona)) return;
            studioDraft.Background = background;
            studioDraft.Persona = persona;
            studioDraft.Normalize();
            PushDraftToFields();
            NextMessage("Generated a service background.", StudioTone.Ok);
            nextRefresh = 0f;
        }

        private void RandomizeLooks()
        {
            studioDraft.HasPortrait = true;
            studioDraft.Body = UnityEngine.Random.Range(0, Math.Max(1, BodyCount()));
            studioDraft.Face = UnityEngine.Random.Range(0, Math.Max(1, FaceCount()));
            studioDraft.Hair = UnityEngine.Random.Range(0, Math.Max(1, HairCount()));
            studioDraft.Uniform = UnityEngine.Random.Range(0, Math.Max(1, UniformCount()));
            studioDraft.Backdrop = UnityEngine.Random.Range(0, Math.Max(1, BackdropCount()));
            studioDraft.Accessory = 0;
            studioPortraitKey = null;
            nextRefresh = 0f;
        }

        private void CycleDraft(Func<PilotDraft, PilotDraft> change)
        {
            studioDraft = change(studioDraft);
            studioDraft.Normalize();
            studioPortraitKey = null;
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

        private void RemoveLocalProfileIf(string callsign)
        {
            if (string.Equals(settings.PilotProfile.Value, callsign, StringComparison.OrdinalIgnoreCase))
                settings.PilotProfile.Value = string.Empty;
        }

        private void NextMessage(string message, StudioTone tone)
        {
            studioMessage = message ?? string.Empty;
            studioMessageTone = tone;
            studioMessageUntil = Time.unscaledTime + 8f;
        }

        private string StudioStatusLine()
        {
            if (!WingLink.PilotStudioAvailable) return WingLink.PilotStudioUnavailableReason;
            if (!string.IsNullOrEmpty(studioMessage) && Time.unscaledTime < studioMessageUntil)
                return studioMessage;
            return studioSelected >= 0 && studioSelected < studioPilots.Count
                ? "Editing " + studioPilots[studioSelected].Callsign + "."
                : "Select a custom pilot or start a new draft.";
        }

        private static WingPilotRecord RecordFrom(PilotDraft draft) => new WingPilotRecord
        {
            Name = draft.Name,
            Callsign = draft.Callsign,
            DialogueTag = draft.DialogueTag,
            Background = draft.Background,
            Persona = draft.Persona,
            Xp = draft.Xp,
            Kills = draft.Kills,
            Sorties = draft.Sorties,
            HasPortrait = draft.HasPortrait,
            Body = draft.Body,
            Face = draft.Face,
            Hair = draft.Hair,
            Uniform = draft.Uniform,
            Accessory = draft.Accessory,
            Backdrop = draft.Backdrop,
        };

        private static PilotDraft DraftFrom(WingPilotRecord record) => new PilotDraft
        {
            Name = record.Name,
            Callsign = record.Callsign,
            DialogueTag = record.DialogueTag,
            Background = record.Background,
            Persona = record.Persona,
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

        private static int BodyCount() => Math.Max(2, WingLink.PortraitBodyCount);
        private static int FaceCount() => Math.Max(1, WingLink.PortraitFaceCount);
        private static int HairCount() => Math.Max(1, WingLink.PortraitHairCount);
        private static int UniformCount() => Math.Max(1, WingLink.PortraitUniformCount);
        private static int BackdropCount() => Math.Max(1, WingLink.PortraitBackdropCount);

        private static int NextSeed() =>
            unchecked(Environment.TickCount ^ (Time.frameCount * 7919) ^ (int)Time.unscaledTime);
    }
}
