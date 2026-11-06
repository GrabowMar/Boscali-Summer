using NOAvionics;
using System;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    // SQUADRON › STUDIO's view on kit v2 (spec bezel v2 §5 SQUADRON › STUDIO; the 2026-10-05 Personnel File redesign), one flow: PILOT — the
    // picker with ‹ › and the draft's chip, NEW · CLONE · DELETE · IMPORT · EXPORT · FOLDER in one row; the live aircrew ID card (the
    // portrait, callsign, name, rank, radio and bio as they are edited); LOOK — the layer steppers, RANDOM LOOK; IDENTITY — CALLSIGN and
    // NAME with RANDOM, RADIO; BIO with its counter, GENERATE and the field; the problem alert; SAVE · REVERT and RECRUIT / DISCHARGE; the
    // SERVICE line. The portrait is art (a data image): it keeps its sprite inside the card's kit frame. No HEAR button: no voice-line
    // player takes a draft's persona (VoicePacks plays a flying member's call), so a sample would be made up.
    internal sealed partial class WmcStudio
    {
        private static readonly LookLayer[] Layers = { LookLayer.Body, LookLayer.Face, LookLayer.Hair, LookLayer.Suit, LookLayer.Scene, LookLayer.Accessory };
        private static readonly string[] LayerKeys = { "BODY", "FACE", "HAIR", "SUIT", "SCENE", "GEAR" };
        private const float BioH = 72f, StudioKeyW = 84f;

        private PickerPart picker;
        private LookPart look;
        private WingIdCard idCard;
        private readonly IdCardFace idFace = new IdCardFace();
        private WmcNameField callsignField, nameField, bioField;
        private AvSection bioSection, lookSection;
        private AvStepper radioStepper;
        private WmcLines noteLines, serviceLines;
        private AvAlert problemAlert;
        private AvControl saveButton, revertButton, cloneButton, deleteButton, recruitButton, meButton, clearPngButton;
        private string radioText = WmcText.Unknown;
        private readonly string[] layerText = { WmcText.Unknown, WmcText.Unknown, WmcText.Unknown, WmcText.Unknown, WmcText.Unknown, WmcText.Unknown };
        private readonly List<AvControl> tracked = new List<AvControl>(20);
        private readonly List<AvPopupEntry> pickEntries = new List<AvPopupEntry>(16);
        private readonly ConfirmGate deleteGate = new ConfirmGate(), dischargeGate = new ConfirmGate();
        private int studioKey = int.MinValue, pickerKey = int.MinValue, recordKey = int.MinValue;
        private string recruitReason;

        private void BuildView()
        {
            AvFlow f = flow;

            // The pilot picker: ‹ NAME › and the draft's chip.
            f.Section(AvIcon.User, "PILOT");
            picker = f.Add(new PickerPart(f.Content, () => Step(-1), OpenPicker, () => Step(1)));
            picker.Prev.Help = "The previous pilot.";
            picker.Next.Help = "The next pilot.";
            picker.Pick.Help = "Pick a pilot: saved pilots first, then this mission's unsaved ones.";

            AvControl[] file = f.Buttons(
                new AvControl.Spec("NEW", NewPilot),
                new AvControl.Spec("CLONE", Clone),
                new AvControl.Spec("DELETE", Delete, AvButtonStyle.Danger),
                new AvControl.Spec("IMPORT", Import),
                new AvControl.Spec("EXPORT", Export),
                new AvControl.Spec("FOLDER", () => WingSavedPilots.OpenFolder())).Controls;
            file[0].Help = "A new pilot as a draft: a random identity, look and bio. Nothing is saved until SAVE.";
            cloneButton = file[1];
            cloneButton.Help = "Copy this pilot into a new draft under the next free callsign.";
            deleteButton = file[2];
            deleteButton.Help = "Delete this saved pilot (press twice). This mission keeps them.";
            file[3].Help = "Add pilots from the Pilots folder: new callsigns only, the files are left as they are.";
            file[4].Help = "Write every saved pilot to Pilots/exported_pilots.json.";
            file[5].Help = "Open the Pilots folder.";

            // The live aircrew ID card: it repaints as the draft is edited.
            idCard = f.Add(new WingIdCard(f.Content));

            // Why SAVE cannot go; SAVE · REVERT and RECRUIT / DISCHARGE; SERVICE.
            problemAlert = f.Add(new AvAlert(f.Content));
            noteLines = f.Add(new WmcLines(f.Content, 1));
            AvControl[] verbs = f.Buttons(
                new AvControl.Spec("SAVE", Save, AvButtonStyle.Primary, AvIcon.CircleCheck),
                new AvControl.Spec("REVERT", Revert, AvButtonStyle.Default, AvIcon.ArrowBackUp),
                new AvControl.Spec("RECRUIT", RecruitOrDischarge, AvButtonStyle.Default, AvIcon.UsersGroup),
                new AvControl.Spec("FLY AS", ToggleMe, AvButtonStyle.Default, AvIcon.User)).Controls;
            saveButton = verbs[0];
            revertButton = verbs[1];
            recruitButton = verbs[2];
            meButton = verbs[3];
            saveButton.Help = "Keep this pilot for every mission: identity, look and bio.";
            revertButton.Help = "Drop the edits since the last save or pick.";
            recruitButton.Help = StudioWords.RecruitTip;
            serviceLines = f.Add(new WmcLines(f.Content, 1));

            // The layer steppers (the card above shows the result) and RANDOM LOOK.
            lookSection = f.Section(AvIcon.MoodSmile, "LOOK", "STEPS THE PORTRAIT");
            look = f.Add(new LookPart(f.Content, layerText, StepLook));
            for (int i = 0; i < Layers.Length; i++)
            {
                AvStepper s = look.Steppers[i];
                s.Minus.Help = s.Plus.Help = "Step this layer of the portrait.";
                Track(s.Minus);
                Track(s.Plus);
            }
            AvControl[] lookVerbs = f.Buttons(
                new AvControl.Spec("RANDOM", RandomLook, AvButtonStyle.Default, AvIcon.Refresh),
                new AvControl.Spec("LOAD PNG", LoadPng),
                new AvControl.Spec("SAVE PNG", SavePng),
                new AvControl.Spec("CLEAR PNG", ClearPng, AvButtonStyle.Danger)).Controls;
            lookVerbs[0].Help = "A random face, hair, uniform, gear and scene.";
            lookVerbs[1].Help = "Pick a PNG or JPG for this pilot's portrait (cropped to 4:5). It shows at once, everywhere; the steppers wait under it.";
            lookVerbs[2].Help = "Write this portrait to a PNG file you choose.";
            clearPngButton = lookVerbs[3];
            clearPngButton.Help = "Drop the PNG portrait and go back to the stepped look (the file is kept as .png.old).";
            Track(lookVerbs[0]);
            Track(lookVerbs[1]);
            Track(lookVerbs[2]);

            // CALLSIGN and NAME with RANDOM, then RADIO.
            f.Section(AvIcon.Typography, StudioWords.IdentityTitle);
            callsignField = f.Add(new WmcNameField(f.Content, "CALLSIGN", PilotText.CallsignChars, CommitCallsign,
                "The callsign the wing and the radio use: letters, digits and hyphens, 14 at most. SAVE keeps it.", "CALLSIGN",
                keyWidth: StudioKeyW, button: new AvControl.Spec("RANDOM", RandomCallsign, AvButtonStyle.Default, AvIcon.Refresh)));
            callsignField.Button.Help = "A random callsign nobody in the squadron uses.";
            Track(callsignField.Button);
            nameField = f.Add(new WmcNameField(f.Content, "NAME", PilotText.NameChars, CommitName,
                "The pilot's name, 24 characters at most. SAVE keeps it.", "NAME",
                keyWidth: StudioKeyW, button: new AvControl.Spec("RANDOM", RandomName, AvButtonStyle.Default, AvIcon.Refresh)));
            nameField.Button.Help = "A random name.";
            Track(nameField.Button);
            radioStepper = f.Add(new AvStepper(f.Content, "RADIO", () => radioText, () => StepRadio(-1), () => StepRadio(1)));
            radioStepper.Minus.Help = radioStepper.Plus.Help = "How this pilot talks on the radio.";
            Track(radioStepper.Minus);
            Track(radioStepper.Plus);

            // BIO: its section with the counter, GENERATE and the field.
            bioSection = f.Section(AvIcon.Message2, StudioWords.BioTitle, StudioWords.BioCounter(0));
            AvControl generate = f.Buttons(new AvControl.Spec("GENERATE", GenerateBio, AvButtonStyle.Default, AvIcon.Refresh)).Controls[0];
            generate.Help = "A new bio in this pilot's radio voice.";
            Track(generate);
            bioField = f.Add(new WmcNameField(f.Content, null, PilotText.BioChars, CommitBio,
                "A few lines about the pilot, 280 characters at most. SAVE keeps it.", "BIO", multiline: true, height: BioH));
        }

        private void Track(AvControl b)
        {
            if (tracked.Count < 20) tracked.Add(b);
        }

        /// <summary>The fields take the draft's text when a new draft starts (never while typed in; never on the refresh).</summary>
        private void FillFields()
        {
            if (callsignField == null) return;
            string id = draft != null ? draftSerial.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            foreach (WmcNameField f in new[] { callsignField, nameField, bioField })
            {
                f.EditingId = id;
                f.SetInteractable(draft != null);
            }
            callsignField.SetText(draft?.Callsign ?? "");
            nameField.SetText(draft?.Name ?? "");
            bioField.SetText(draft?.Background ?? "");
            studioKey = int.MinValue;
        }

        // ---------------------------------------------------------------- the picker

        private void EnsurePopup()
        {
            if (popup != null) return;
            popupParent = flow.Content;
            popup = new AvPopup(flow.Content, flow.Width);
        }

        private void OpenPicker()
        {
            if (entries.Count == 0) return;
            EnsurePopup();
            int at = IndexOf(selected);
            pickEntries.Clear();
            foreach (Entry e in entries)
            {
                int n = 0;
                PilotStatus s = e.Live != null ? WmcPilots.StatusOf(e.Live, wing, out n) : PilotStatus.Free;
                string detail = (WingSavedPilots.IsMe(e.Saved?.Callsign) ? "YOU · " : "") + (e.Saved != null ? "SAVED" : "NOT SAVED") + " · "
                    + (client ? "" : e.Live != null ? SquadronWords.Row(s, false, n) : "NOT IN MISSION");
                pickEntries.Add(new AvPopupEntry(WmcText.Cut(e.Callsign, PilotText.CallsignChars), detail, pickEntries.Count == at));
            }
            popup.Show(WmcPopup.Area(popupParent, picker.Pick.Rect, pickEntries.Count), pickEntries, Open);
        }

        /// <summary>‹ and ›: the next pilot in the picker's order (asking before dropping edits, as a pick does).</summary>
        private void Step(int dir)
        {
            if (entries.Count == 0) return;
            int at = draftNew ? -1 : IndexOf(selected);
            int next = at < 0 ? (dir > 0 ? 0 : entries.Count - 1) : ((at + dir) % entries.Count + entries.Count) % entries.Count;
            Open(next);
        }

        private static void Enable(AvControl b, bool on)
        {
            if (b.Interactable != on) b.Interactable = on;
        }

        private void RefreshPicker()
        {
            int at = IndexOf(selected);
            // The gate runs out by itself (3 s): its state is part of the key, or DELETE? would stay on the label.
            bool deleting = draftOriginal != null && deleteGate.IsArmed(draftOriginal, Time.unscaledTime);
            int key;
            unchecked
            {
                key = storeVersion * 31 + rosterVersion * 17 + at * 131 + (client ? 3 : 0) + (draftNew ? 5 : 0) + draftRevision * 7 + entries.Count * 1009
                    + (deleting ? 11 : 0);
            }
            if (key == pickerKey) return;
            pickerKey = key;
            string name = draft == null ? (entries.Count == 0 ? (WingSavedPilots.Problem != null ? "SAVED PILOTS UNREADABLE" : "NO PILOTS · NEW STARTS ONE") : "PICK A PILOT")
                : WmcText.Cut(draft.Callsign, PilotText.CallsignChars);
            picker.Pick.Label = name + " ›";
            picker.Pick.Help = WingSavedPilots.Problem ?? "Pick a pilot: saved pilots first, then this mission's unsaved ones.";
            bool several = entries.Count > 1 || (draftNew && entries.Count > 0);
            Enable(picker.Prev, several);
            Enable(picker.Next, several);
            Enable(picker.Pick, entries.Count > 0);
            bool saved = !draftNew && draftOriginal != null;
            Enable(cloneButton, draft != null);
            Enable(deleteButton, saved);
            deleteButton.Label = saved && deleting ? "DELETE?" : "DELETE";
            deleteButton.Help = saved ? "Delete this saved pilot (press twice). This mission keeps them." : "Only a saved pilot can be deleted.";
        }

        // ---------------------------------------------------------------- the studio and the record

        private void RefreshStudio()
        {
            int key;
            unchecked
            {
                key = draftRevision * 31 + draftSerial * 7 + storeVersion * 131 + rosterVersion * 17 + (problem != null ? problem.GetHashCode() : 0)
                    + (FilePortrait()?.GetInstanceID() ?? 0) * 3 + (WingSavedPilots.IsMe(draftNew ? null : draftOriginal) ? 5 : 0);
            }
            if (key == studioKey) return;
            studioKey = key;
            layout = true;
            bool has = draft != null;
            DraftState state = has ? StateOf() : DraftState.Saved;
            string rail = "inert";
            picker.Chip.Set(has ? StudioWords.Chip(state, out rail) : WmcText.Unknown, has ? WmcState.Of(rail) : AvState.Inert);
            RefreshCard(has);
            PortraitSelection sel = has ? draft.Selection : PilotPortraitGenerator.DefaultSelection;
            layerText[0] = PilotPortraitGenerator.BodyLabel(sel.Body);
            layerText[1] = StudioWords.Face(sel.Face);
            layerText[2] = StudioWords.Hair(sel.Hair);
            layerText[3] = PilotPortraitGenerator.UniformLabel(sel.Uniform);
            layerText[4] = StudioWords.Scene(sel.Backdrop);
            layerText[5] = PilotPortraitGenerator.AccessoryLabel(sel.Accessory);
            look.RefreshValues();
            radioText = has ? StudioWords.Radio(draft.Persona) : WmcText.Unknown;
            radioStepper.Refresh();
            bioSection.SetCaption(StudioWords.BioCounter(has ? (draft.Background ?? "").Length : 0));
            foreach (AvControl b in tracked) Enable(b, has);
            bool png = has && FilePortrait() != null;
            Enable(clearPngButton, png);
            lookSection.SetCaption(png ? "PNG PORTRAIT · CLEAR PNG TO STEP" : "STEPS THE PORTRAIT");
            bool me = has && WingSavedPilots.IsMe(draftNew ? null : draftOriginal);
            meButton.Latched = me;
            meButton.Label = me ? "YOU" : "FLY AS";
            Enable(meButton, has && WingSavedPilots.Me != null && !me && !draftNew && draftOriginal != null);
            meButton.Help = WingSavedPilots.Me == null ? "The SQD dossier is not running."
                : me ? "You fly as this pilot: the SQD dossier shows them. FLY AS on another pilot changes it."
                : draftNew || draftOriginal == null ? "Save the pilot first: you can fly as a saved pilot."
                : "Fly as this pilot: the SQD dossier takes their callsign, name, bio and portrait.";
            Enable(saveButton, has && state != DraftState.Saved);
            saveButton.Help = !has ? StudioWords.NoPilot : state == DraftState.Saved ? "Saved: nothing to save."
                : "Keep this pilot for every mission: identity, look and bio.";
            Enable(revertButton, has && (state == DraftState.Edited || (draftNew && touched)));
            if (problem != null) problemAlert.Show(AvIcon.AlertTriangle, "PROBLEM", problem, AvState.Caution);
            else problemAlert.Hide();
            noteLines.Set(0, problem != null ? "" : !has ? StudioWords.NoPilot : state == DraftState.Edited ? "EDITED · SAVE keeps it, REVERT drops it" : "");
        }

        /// <summary>The ID card from the draft: its words as typed, the rank its pilot holds (a saved record's XP, a live pilot's rank, else
        /// ROOKIE), the barcode and number from the identity, and the bio's first lines.</summary>
        private void RefreshCard(bool has)
        {
            if (!has)
            {
                idFace.Callsign = idFace.Name = idFace.Number = idFace.Bio = "";
                idFace.Radio = WmcText.Unknown;
                idFace.RankWord = PilotPerks.RankName(WingRank.Rookie).ToUpperInvariant();
                idFace.Rank = WingRank.Rookie;
                idFace.Portrait = PilotPortrait.Preview(PilotPortraitGenerator.DefaultSelection);
                idCard.Show(idFace);
                return;
            }
            CustomPilotRecord stored = !draftNew && draftOriginal != null ? WingSavedPilots.Store.Find(draftOriginal) : null;
            WingRank rank = draftLive != null && !draftNew ? draftLive.Rank : stored != null ? PilotPerks.RankFor(stored.Xp) : WingRank.Rookie;
            idFace.Callsign = WmcText.Cut(draft.Callsign ?? "", PilotText.CallsignChars);
            idFace.Name = WmcText.Cut(draft.Name ?? "", PilotText.NameChars);
            idFace.Rank = rank;
            idFace.RankWord = PilotPerks.RankName(rank).ToUpperInvariant();
            idFace.Radio = StudioWords.Radio(draft.Persona);
            idFace.Number = PersonnelFile.Number(draft.Callsign, draft.Name);
            string excerpt = PersonnelFile.Excerpt(draft.Background, 150);
            idFace.Bio = excerpt.Length > 0 ? "\"" + excerpt + "\"" : "";
            idFace.Portrait = FilePortrait() ?? PilotPortrait.Preview(draft.Selection);
            idCard.Show(idFace);
        }

        /// <summary>The draft's PNG portrait: under its callsign, else (a rename not saved yet) under the saved one.</summary>
        private Sprite FilePortrait() => draft == null ? null
            : PilotPortrait.FromFile(draft.Callsign) ?? (draftNew ? null : PilotPortrait.FromFile(draftOriginal));

        private void RefreshRecord()
        {
            CustomPilotRecord stored = !draftNew && draftOriginal != null ? WingSavedPilots.Store.Find(draftOriginal) : null;
            WingPilot live = draftNew || client ? null : draftLive;
            int n = 0;
            PilotStatus s = live != null ? WmcPilots.StatusOf(live, wing, out n) : PilotStatus.Free;
            bool asking = live != null && dischargeGate.IsArmed(live.Callsign, Time.unscaledTime);
            int key;
            unchecked
            {
                key = storeVersion * 31 + rosterVersion * 17 + draftSerial * 7 + (int)s * 131 + n * 1009 + (asking ? 3 : 0) + (client ? 5 : 0)
                    + (draftNew ? 11 : 0) + (stored != null ? 13 : 0);
            }
            if (key == recordKey) return;
            recordKey = key;
            layout = true;
            // RECRUIT a saved pilot not in this mission; DISCHARGE a free pilot of this mission (press twice).
            recruitReason = client ? StudioWords.ClientRecord
                : draft == null ? StudioWords.NoPilot
                : live == null && stored == null ? "Save the pilot first: RECRUIT adds a saved pilot to this mission."
                : live != null ? StudioWords.DischargeWhy(s) : null;
            recruitButton.Label = StudioWords.RecruitLabel(live != null, asking);
            recruitButton.Latched = asking;
            Enable(recruitButton, recruitReason == null);
            recruitButton.Help = recruitReason ?? (live != null ? StudioWords.DischargeTip : StudioWords.RecruitTip);
            serviceLines.Set(0, stored != null
                ? StudioWords.Service(stored.Missions, stored.Sorties, stored.Kills) + " · " + StudioWords.BestRank(stored.Missions, stored.Xp)
                : draft == null ? "" : live != null ? SquadronWords.Row(s, ReferenceEquals(live, WingPilotRoster.Upcoming), n) + " · " + StudioWords.NotSaved
                : StudioWords.NotSaved);
        }

        // ---------------------------------------------------------------- parts

        /// <summary>‹ [pilot ›] › and the draft's state chip on one 32 px line.</summary>
        private sealed class PickerPart : AvPart
        {
            private const float StepW = 34f, ChipW = 104f, Gap = AvGridTokens.Gap / 2f;

            public readonly AvControl Prev, Pick, Next;
            public readonly AvChip Chip;

            public PickerPart(RectTransform parent, Action prev, Action pick, Action next)
            {
                Rect = AvLay.Child(parent, "Picker");
                Prev = AvControl.Make(Rect, new AvControl.Spec("‹", prev, AvButtonStyle.Quiet));
                Pick = AvControl.Make(Rect, new AvControl.Spec("", pick));
                Next = AvControl.Make(Rect, new AvControl.Spec("›", next, AvButtonStyle.Quiet));
                Chip = new AvChip(Rect);
                Chip.Set(WmcText.Unknown, AvState.Inert);
            }

            public override float Measure(float width) => AvGridTokens.Row;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float pickW = s.W - 2f * StepW - ChipW - 3f * Gap;
                AvLay.Place(Prev.Rect, 0f, 0f, StepW, s.H);
                AvLay.Place(Pick.Rect, StepW + Gap, 0f, pickW, s.H);
                AvLay.Place(Next.Rect, StepW + Gap + pickW + Gap, 0f, StepW, s.H);
                AvLay.Place(Chip.Rect, s.W - ChipW, (s.H - AvGridTokens.ChipStrip) * 0.5f, ChipW, AvGridTokens.ChipStrip);
            }

            public override void Restyle()
            {
                Prev.Restyle();
                Pick.Restyle();
                Next.Restyle();
                Chip.Restyle();
            }
        }

        /// <summary>The six layer steppers in two columns (the ID card above shows the portrait they change).</summary>
        private sealed class LookPart : AvPart
        {
            public readonly AvStepper[] Steppers = new AvStepper[6];

            public LookPart(RectTransform parent, string[] values, Action<LookLayer, int> step)
            {
                Rect = AvLay.Child(parent, "Look");
                for (int i = 0; i < Steppers.Length; i++)
                {
                    int k = i;
                    LookLayer layer = Layers[i];
                    Steppers[i] = new AvStepper(Rect, LayerKeys[i], () => values[k], () => step(layer, -1), () => step(layer, 1)) { Parent = this };
                }
                Restyle();
            }

            public void RefreshValues()
            {
                foreach (AvStepper s in Steppers) s.Refresh();
            }

            public override float Measure(float width)
            {
                float cw = AvFlowMath.ColumnWidth(width, 2, AvGridTokens.Gap), height = 0f;
                for (int i = 0; i < Steppers.Length; i += 2)
                    height += RowHeight(i, cw) + (i == 0 ? 0f : 2f);
                return height;
            }

            private float RowHeight(int index, float width) => Mathf.Max(Steppers[index].Measure(width), Steppers[index + 1].Measure(width));

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float cw = AvFlowMath.ColumnWidth(s.W, 2, AvGridTokens.Gap), y = 0f;
                for (int i = 0; i < Steppers.Length; i += 2)
                {
                    float height = RowHeight(i, cw);
                    Steppers[i].Place(new AvSlot(0f, y, cw, height));
                    Steppers[i + 1].Place(new AvSlot(cw + AvGridTokens.Gap, y, cw, height));
                    y += height + 2f;
                }
            }

            public override void Restyle()
            {
                foreach (AvStepper s in Steppers) s.Restyle();
            }
        }
    }
}
