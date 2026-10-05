using NOAvionics;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
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
        private static readonly LookLayer[] Layers = { LookLayer.Body, LookLayer.Face, LookLayer.Hair, LookLayer.Suit, LookLayer.Scene };
        private static readonly string[] LayerKeys = { "BODY", "FACE", "HAIR", "SUIT", "SCENE" };
        private static readonly string[] LayerIds = { "body", "face", "hair", "suit", "scene" };
        private const float BioH = 72f, StudioKeyW = 84f;

        private PickerPart picker;
        private LookPart look;
        private WingIdCard idCard;
        private readonly IdCardFace idFace = new IdCardFace();
        private WmcNameField callsignField, nameField, bioField;
        private AvSection bioSection;
        private AvStepper radioStepper;
        private WmcLines noteLines, serviceLines;
        private AvAlert problemAlert;
        private AvControl saveButton, revertButton, cloneButton, deleteButton, recruitButton;
        private string radioText = WmcText.Unknown;
        private readonly string[] layerText = { WmcText.Unknown, WmcText.Unknown, WmcText.Unknown, WmcText.Unknown, WmcText.Unknown };
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
            ids.Add("sq.pick.prev", picker.Prev);
            ids.Add("sq.pick", picker.Pick);
            ids.Add("sq.pick.next", picker.Next);

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
            file[3].Help = "Add pilots from the Pilots folder and 0.9's folder: new callsigns only, the files are left as they are.";
            file[4].Help = "Write every saved pilot to Pilots/exported_pilots.json.";
            file[5].Help = "Open the Pilots folder.";
            ids.Add("sq.new", file[0]);
            ids.Add("sq.clone", cloneButton);
            ids.Add("sq.delete", deleteButton);
            ids.Add("sq.import", file[3]);
            ids.Add("sq.export", file[4]);
            ids.Add("sq.folder", file[5]);

            // The live aircrew ID card: it repaints as the draft is edited.
            idCard = f.Add(new WingIdCard(f.Content));

            // The layer steppers (the card above shows the result) and RANDOM LOOK.
            f.Section(AvIcon.MoodSmile, "LOOK", "STEPS THE PORTRAIT");
            look = f.Add(new LookPart(f.Content, layerText, StepLook));
            for (int i = 0; i < Layers.Length; i++)
            {
                AvStepper s = look.Steppers[i];
                s.Minus.Help = s.Plus.Help = "Step this layer of the portrait.";
                ids.Add("sq.look." + LayerIds[i] + ".prev", s.Minus);
                ids.Add("sq.look." + LayerIds[i] + ".next", s.Plus);
                Track(s.Minus);
                Track(s.Plus);
            }
            AvControl random = f.Buttons(new AvControl.Spec("RANDOM LOOK", RandomLook, AvButtonStyle.Default, AvIcon.Refresh)).Controls[0];
            random.Help = "A random face, hair, uniform and scene.";
            ids.Add("sq.look.random", random);
            Track(random);

            // CALLSIGN and NAME with RANDOM, then RADIO.
            f.Section(AvIcon.Typography, StudioWords.IdentityTitle);
            callsignField = f.Add(new WmcNameField(f.Content, "CALLSIGN", PilotText.CallsignChars, CommitCallsign,
                "The callsign the wing and the radio use: letters, digits and hyphens, 14 at most. SAVE keeps it.", "CALLSIGN",
                keyWidth: StudioKeyW, button: new AvControl.Spec("RANDOM", RandomCallsign, AvButtonStyle.Default, AvIcon.Refresh)));
            callsignField.Button.Help = "A random callsign nobody in the squadron uses.";
            ids.Add("sq.callsign.random", callsignField.Button);
            Track(callsignField.Button);
            nameField = f.Add(new WmcNameField(f.Content, "NAME", PilotText.NameChars, CommitName,
                "The pilot's name, 24 characters at most. SAVE keeps it.", "NAME",
                keyWidth: StudioKeyW, button: new AvControl.Spec("RANDOM", RandomName, AvButtonStyle.Default, AvIcon.Refresh)));
            nameField.Button.Help = "A random name.";
            ids.Add("sq.name.random", nameField.Button);
            Track(nameField.Button);
            radioStepper = f.Add(new AvStepper(f.Content, "RADIO", () => radioText, () => StepRadio(-1), () => StepRadio(1)));
            radioStepper.Minus.Help = radioStepper.Plus.Help = "How this pilot talks on the radio.";
            ids.Add("sq.radio.prev", radioStepper.Minus);
            ids.Add("sq.radio.next", radioStepper.Plus);
            Track(radioStepper.Minus);
            Track(radioStepper.Plus);

            // BIO: its section with the counter, GENERATE and the field.
            bioSection = f.Section(AvIcon.Message2, StudioWords.BioTitle, StudioWords.BioCounter(0));
            AvControl generate = f.Buttons(new AvControl.Spec("GENERATE", GenerateBio, AvButtonStyle.Default, AvIcon.Refresh)).Controls[0];
            generate.Help = "A new bio in this pilot's radio voice.";
            ids.Add("sq.bio.generate", generate);
            Track(generate);
            bioField = f.Add(new WmcNameField(f.Content, null, PilotText.BioChars, CommitBio,
                "A few lines about the pilot, 280 characters at most. SAVE keeps it.", "BIO", multiline: true, height: BioH));

            // Why SAVE cannot go; SAVE · REVERT and RECRUIT / DISCHARGE; SERVICE.
            problemAlert = f.Add(new AvAlert(f.Content));
            noteLines = f.Add(new WmcLines(f.Content, 1));
            AvControl[] verbs = f.Buttons(
                new AvControl.Spec("SAVE", Save, AvButtonStyle.Primary, AvIcon.CircleCheck),
                new AvControl.Spec("REVERT", Revert, AvButtonStyle.Default, AvIcon.ArrowBackUp),
                new AvControl.Spec("RECRUIT", RecruitOrDischarge, AvButtonStyle.Default, AvIcon.UsersGroup)).Controls;
            saveButton = verbs[0];
            revertButton = verbs[1];
            recruitButton = verbs[2];
            saveButton.Help = "Keep this pilot for every mission: identity, look and bio.";
            revertButton.Help = "Drop the edits since the last save or pick.";
            recruitButton.Help = StudioWords.RecruitTip;
            ids.Add("sq.save", saveButton);
            ids.Add("sq.revert", revertButton);
            ids.Add("sq.recruit", recruitButton);
            serviceLines = f.Add(new WmcLines(f.Content, 1));
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
                string detail = (e.Saved != null ? "SAVED" : "NOT SAVED") + " · " + (client ? "" : e.Live != null ? SquadronWords.Row(s, false, n) : "NOT IN MISSION");
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
            int key;
            unchecked
            {
                key = storeVersion * 31 + rosterVersion * 17 + at * 131 + (client ? 3 : 0) + (draftNew ? 5 : 0) + draftRevision * 7 + entries.Count * 1009;
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
            deleteButton.Label = saved && deleteGate.IsArmed(draftOriginal, Time.unscaledTime) ? "DELETE?" : "DELETE";
            deleteButton.Help = saved ? "Delete this saved pilot (press twice). This mission keeps them." : "Only a saved pilot can be deleted.";
        }

        // ---------------------------------------------------------------- the studio and the record

        private void RefreshStudio()
        {
            int key;
            unchecked
            {
                key = draftRevision * 31 + draftSerial * 7 + storeVersion * 131 + rosterVersion * 17 + (problem != null ? problem.GetHashCode() : 0);
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
            look.RefreshValues();
            radioText = has ? StudioWords.Radio(draft.Persona) : WmcText.Unknown;
            radioStepper.Refresh();
            bioSection.SetCaption(StudioWords.BioCounter(has ? (draft.Background ?? "").Length : 0));
            foreach (AvControl b in tracked) Enable(b, has);
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
            idFace.Portrait = PilotPortrait.Preview(draft.Selection);
            idCard.Show(idFace);
        }

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

        /// <summary>The five layer steppers in two columns (the ID card above shows the portrait they change).</summary>
        private sealed class LookPart : AvPart
        {
            private const float StepPitch = AvGridTokens.Row + 2f;

            public readonly AvStepper[] Steppers = new AvStepper[5];

            public LookPart(RectTransform parent, string[] values, Action<LookLayer, int> step)
            {
                Rect = AvLay.Child(parent, "Look");
                for (int i = 0; i < Steppers.Length; i++)
                {
                    int k = i;
                    LookLayer layer = Layers[i];
                    Steppers[i] = new AvStepper(Rect, LayerKeys[i], () => values[k], () => step(layer, -1), () => step(layer, 1));
                }
                Restyle();
            }

            public void RefreshValues()
            {
                foreach (AvStepper s in Steppers) s.Refresh();
            }

            public override float Measure(float width) => 3 * StepPitch - 2f;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float cw = (s.W - AvGridTokens.Gap) * 0.5f;
                for (int i = 0; i < Steppers.Length; i++)
                    Steppers[i].Place(new AvSlot((i % 2) * (cw + AvGridTokens.Gap), (i / 2) * StepPitch, cw, AvGridTokens.Row));
            }

            public override void Restyle()
            {
                foreach (AvStepper s in Steppers) s.Restyle();
            }
        }
    }
}
