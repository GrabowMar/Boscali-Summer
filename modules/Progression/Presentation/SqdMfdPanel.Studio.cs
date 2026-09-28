using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Progression.Presentation
{
    internal sealed partial class SqdMfdPanel
    {
        private const int StudioRowsPerPage = 2;

        /// <summary>How a studio message reads: a success, a caution, or a refusal.</summary>
        private enum StudioTone : byte { Info, Ok, Warn, Bad }

        private readonly List<WingPilotRecord> studioPilots = new List<WingPilotRecord>(64);
        private readonly AvRow[] studioRows = new AvRow[StudioRowsPerPage];
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

        private AvTextBlock studioStatus;
        private AvTextBlock studioPagerLabel;
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
            studioStatus = null;
            studioPagerLabel = null;
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
            p.Section(AvIcon.Pencil, "PILOT STUDIO", "LOCAL FILES + HOST ROSTER");
            if (!WingLink.PilotStudioAvailable)
            {
                studioStatus = p.Add(new AvTextBlock(p.Content, AvTextRole.Prose));
                studioStatus.Set(WingLink.PilotStudioUnavailableReason);
                return;
            }

            p.Section(AvIcon.UsersGroup, "CUSTOM PILOTS", "WING COMMAND CONNECTED");
            studioStatus = p.Add(new AvTextBlock(p.Content, AvTextRole.ProseSmall));

            AvButtons actions = p.Buttons(
                new AvControl.Spec("+ NEW", NewStudioDraft, AvButtonStyle.Quiet, AvIcon.Plus),
                new AvControl.Spec("CLONE", CloneStudioDraft, AvButtonStyle.Quiet),
                new AvControl.Spec("DELETE", DeleteStudioDraft, AvButtonStyle.Danger, AvIcon.X),
                new AvControl.Spec("IMPORT ALL", ImportAllCustomPilots, AvButtonStyle.Quiet));
            studioDeleteButton = actions.Controls[2];

            for (int i = 0; i < StudioRowsPerPage; i++)
            {
                int index = i;
                studioRows[i] = p.Add(new AvRow(p.Content, () => SelectStudio(index)));
            }

            AvButtons pager = p.Buttons(
                new AvControl.Spec("PREVIOUS", () => ChangeStudioPage(-1), AvButtonStyle.Quiet, AvIcon.ChevronLeft),
                new AvControl.Spec("NEXT", () => ChangeStudioPage(1), AvButtonStyle.Quiet, AvIcon.ChevronRight));
            studioPagerPrev = pager.Controls[0];
            studioPagerNext = pager.Controls[1];
            studioPagerLabel = p.Add(new AvTextBlock(p.Content, AvTextRole.Label));

            p.Section(AvIcon.Pencil, "PILOT EDITOR", "LOCAL FILE");
            studioPortrait = p.Add(new AvPortrait(p.Content, "StudioPilot"));

            studioBody = p.Add(new AvStepper(p.Content, "BODY", () => WingLink.PortraitBodyLabel(studioDraft.Body),
                () => CycleDraft(d => d.CycleBody(-1, BodyCount())), () => CycleDraft(d => d.CycleBody(1, BodyCount()))));
            studioFace = p.Add(new AvStepper(p.Content, "FACE",
                () => "FACE " + AvNum.Thousands(studioDraft.Face + 1) + "/" + AvNum.Thousands(FaceCount()),
                () => CycleDraft(d => d.CycleFace(-1, FaceCount())), () => CycleDraft(d => d.CycleFace(1, FaceCount()))));
            studioHair = p.Add(new AvStepper(p.Content, "HAIR",
                () => studioDraft.Hair == 0 ? "BALD" : "HAIR " + AvNum.Thousands(studioDraft.Hair) + "/" + AvNum.Thousands(HairCount() - 1),
                () => CycleDraft(d => d.CycleHair(-1, HairCount())), () => CycleDraft(d => d.CycleHair(1, HairCount()))));
            studioSuit = p.Add(new AvStepper(p.Content, "SUIT", () => WingLink.PortraitUniformLabel(studioDraft.Uniform),
                () => CycleDraft(d => d.CycleUniform(-1, UniformCount())), () => CycleDraft(d => d.CycleUniform(1, UniformCount()))));
            studioBack = p.Add(new AvStepper(p.Content, "BACK",
                () => "BACK " + AvNum.Thousands(studioDraft.Backdrop + 1) + "/" + AvNum.Thousands(BackdropCount()),
                () => CycleDraft(d => d.CycleBackdrop(-1, BackdropCount())), () => CycleDraft(d => d.CycleBackdrop(1, BackdropCount()))));

            studioCallsignField = p.Add(new AvField(p.Content, "CALLSIGN", PilotDraft.MaxCallsign,
                value => { studioDraft.Callsign = value; studioDraft.Normalize(); }));
            p.Buttons(new AvControl.Spec("RANDOM CALLSIGN", RandomizeCallsign, AvButtonStyle.Quiet, AvIcon.Refresh));
            studioNameField = p.Add(new AvField(p.Content, "NAME", PilotDraft.MaxName,
                value => { studioDraft.Name = value; studioDraft.Normalize(); }));
            p.Buttons(new AvControl.Spec("RANDOM NAME", RandomizeName, AvButtonStyle.Quiet, AvIcon.Refresh));
            studioStyle = p.Add(new AvStepper(p.Content, "STYLE", () => WingLink.PersonaLabel(studioDraft.Persona).ToUpperInvariant(),
                () => CycleDraft(d => d.CyclePersona(-1)), () => CycleDraft(d => d.CyclePersona(1))));

            p.Section(AvIcon.Message2, "BACKGROUND / LORE", null);
            studioBioField = p.Add(new AvField(p.Content, "Service background…", PilotDraft.MaxBackground,
                value => { studioDraft.Background = value; studioDraft.Normalize(); }));
            p.Buttons(new AvControl.Spec("GENERATE", GenerateBio, AvButtonStyle.Quiet, AvIcon.Refresh));

            AvButtons saveRow = p.Buttons(
                new AvControl.Spec("SAVE PILOT", SaveStudioDraft, AvButtonStyle.Primary, AvIcon.CircleCheck),
                new AvControl.Spec("SET AS PROFILE", SetAsLocalProfile),
                new AvControl.Spec("RECRUIT", ToggleRecruit));
            studioProfileButton = saveRow.Controls[1];
            studioRecruitButton = saveRow.Controls[2];

            p.Section(AvIcon.Flag, "SQUADRON IDENTITY", "LOCAL COSMETIC");
            studioEmblem = p.Add(new AvPortrait(p.Content, "Emblem", "NO ART"));
            studioShape = p.Add(new AvStepper(p.Content, "SHAPE", () => EmblemDesign.ShapeNames[emblem.Shape],
                () => CycleEmblem(-1, 0, 0), () => CycleEmblem(1, 0, 0)));
            studioCharge = p.Add(new AvStepper(p.Content, "CHARGE", () => EmblemDesign.ChargeNames[emblem.Charge],
                () => CycleEmblem(0, -1, 0), () => CycleEmblem(0, 1, 0)));
            studioPalette = p.Add(new AvStepper(p.Content, "PALETTE",
                () => "PALETTE " + AvNum.Thousands(emblem.Palette + 1) + "/" + AvNum.Thousands(EmblemDesign.PaletteCount),
                () => CycleEmblem(0, 0, -1), () => CycleEmblem(0, 0, 1)));
            p.Buttons(new AvControl.Spec("RANDOMIZE EMBLEM", RandomizeEmblem, AvButtonStyle.Quiet, AvIcon.Refresh));

            studioSquadronField = p.Add(new AvField(p.Content, "SQUADRON NAME", 24,
                value => settings.SquadronName.Value = value ?? string.Empty));
            studioSquadronField.Text = squadronName;
            studioArt = p.Add(new AvStepper(p.Content, "ART",
                () => emblemFileIndex >= 0 && emblemFileIndex < emblemFiles.Length
                    ? System.IO.Path.GetFileNameWithoutExtension(emblemFiles[emblemFileIndex])
                    : "NONE · PROCEDURAL",
                () => CycleArtFile(-1), () => CycleArtFile(1)));
            p.Buttons(new AvControl.Spec("NO ART", ClearArtFile, AvButtonStyle.Quiet));

            studioMessageText = p.Add(new AvTextBlock(p.Content, AvTextRole.ProseSmall));
            studioMessageText.Set("Custom pilots are local files read by Wing Command; nothing is uploaded.");
        }

        // ---- STUDIO refresh ------------------------------------------------------------------

        private void RefreshStudioPage()
        {
            if (studioStatus == null || settings == null) return;

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
            if (studioPagerLabel == null) return; // Wing Command companion API absent: card above already explains why.

            bool available = WingLink.PilotStudioAvailable;
            studioStatus.Set(available
                ? "Wing Command companion API ready · " + AvNum.Thousands(studioPilots.Count) + " custom pilot file(s)."
                : WingLink.PilotStudioUnavailableReason);

            int pageCount = Math.Max(1, (studioPilots.Count + StudioRowsPerPage - 1) / StudioRowsPerPage);
            studioPage = Mathf.Clamp(studioPage, 0, pageCount - 1);
            studioPagerPrev.Interactable = studioPage > 0;
            studioPagerNext.Interactable = studioPage < pageCount - 1;
            int first = studioPage * StudioRowsPerPage;
            studioPagerLabel.Set(studioPilots.Count == 0 ? "NO PILOTS — PRESS NEW OR IMPORT ALL"
                : AvNum.Thousands(first + 1) + "–" + AvNum.Thousands(Math.Min(first + StudioRowsPerPage, studioPilots.Count)) +
                  " OF " + AvNum.Thousands(studioPilots.Count) + "   ·   PAGE " + AvNum.Thousands(studioPage + 1) + "/" + AvNum.Thousands(pageCount));

            for (int i = 0; i < studioRows.Length; i++)
            {
                AvRow row = studioRows[i];
                int index = first + i;
                bool visible = index < studioPilots.Count;
                row.Rect.gameObject.SetActive(visible);
                if (!visible) continue;
                WingPilotRecord record = studioPilots[index];
                bool inSquad = WingLink.IsPilotRecruited(record.Callsign);
                bool selected = studioSelected == index;
                row.Set(record.Callsign, record.Name + "   ·   " + WingLink.RankNameForXp(record.Xp),
                    inSquad ? "IN SQUADRON" : "READY", inSquad ? AvState.Ready : AvState.Info);
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
