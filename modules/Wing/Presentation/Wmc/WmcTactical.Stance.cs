using NOAvionics;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>TACTICAL › ORDERS › STANCE (spec 2026-10-04 §4.1; mockup board/orders.html .slots and .tune): six stance slots (W+1 … W+6,
    /// the same stances as BEHAVIOUR › STANCES and the wing-key chord), then a fine-tune strip for the four combat settings — TARGETS,
    /// RANGE, RADAR, WEAPONS — that marks what the scope flies differently from the slot it matches (an amber * and the stance's own
    /// value outlined) with RESET (apply the stance again), SAVE AS… (the live settings as a new stance in the next free slot) and EDIT ›
    /// (BEHAVIOUR, where stances are written). A mixed scope matches nothing.</summary>
    internal sealed partial class WmcTactical
    {
        private static readonly DoctrineAxis[] TuneAxes = { DoctrineAxis.Targets, DoctrineAxis.Reach, DoctrineAxis.Radar, DoctrineAxis.Weapons };
        private static readonly string[] TuneKeys = { "targets", "reach", "radar", "weapons" };
        private static readonly string[][] TuneChoices =
        {
            new[] { "HOLD", "AIR", "GND", "BOTH", "COVER" }, new[] { "6 KM", "12 KM" }, new[] { "ON", "SILENT", "OFF" }, new[] { "AUTO", "MSL", "GUN", "NO A-G" },
        };
        private static readonly string[][] TuneOptionKeys =
        {
            new[] { "hold", "air", "ground", "both", "cover" }, new[] { "slot", "long" }, new[] { "on", "silent", "off" }, new[] { "auto", "missiles", "guns", "noag" },
        };
        private static readonly string[][] TuneTips =
        {
            new[]
            {
                "Hold fire: shoot only when ordered.", "Shoot at enemy aircraft in reach on their own.",
                "Shoot at ground targets in reach on their own.", "Shoot at air and ground targets in reach on their own.",
                "Cover: take the one air threat nearest the protected aircraft.",
            },
            new[] { "Close: shoot at what comes within 6 km on their own.", "Long: reach out to 12 km on their own." },
            new[]
            {
                "Radar on: the scope sees and shares its picture.", "Silent: radar off until engaged, then on (enemy warners stay quiet).",
                "Off: no radar, no radar-guided (SARH) shots.",
            },
            new[] { "Every weapon aboard.", "Missiles only: no guns, no bombs.", "Guns only.", "Every weapon, at air targets only." },
        };
        private static readonly string[] TuneNames = { "TARGETS", "RANGE", "RADAR", "WEAPONS" };

        private AvSection stanceSection;
        private WmcStanceSlots slots;
        private WmcTuneBox tuneBox;
        private readonly WmcTuneRow[] tuneRows = new WmcTuneRow[4];
        private readonly int[] tuneValues = { -1, -1, -1, -1 };
        private int stanceKey = int.MinValue, stanceSlot = -1, stanceMask;
        private byte[] liveAxes;
        private string stanceNoteShown;

        private void BuildStance(AvFlow f)
        {
            stanceSection = f.Section(AvIcon.Shield, "STANCE", "");
            slots = f.Add(new WmcStanceSlots(f.Content, PickSlot, null));
            for (int i = 0; i < WmcStanceSlots.Count; i++)
            {
                ids.Add("tac.stance.slot" + i, slots.Target(i));
                slots.SetHelp(i, "Wing key + " + (i + 1) + ": fly this stance with the scope.");
            }
            for (int a = 0; a < tuneRows.Length; a++)
            {
                int k = a;
                tuneRows[a] = new WmcTuneRow(f.Content, TuneNames[a], TuneChoices[a], v => WmcPostureActions.Axis(last, TuneAxes[k], v));
                for (int i = 0; i < TuneChoices[a].Length; i++)
                {
                    tuneRows[a].Options[i].Help = TuneTips[a][i];
                    ids.Add("tac.posture." + TuneKeys[a] + "." + TuneOptionKeys[a][i], tuneRows[a].Options[i]);
                }
            }
            tuneBox = f.Add(new WmcTuneBox(f.Content, ResetStance, SaveStance, EditStance, tuneRows));
            tuneBox.Reset.Help = "Fly the stance's own settings again.";
            tuneBox.Save.Help = "Keep these settings as a new stance in the next free slot.";
            tuneBox.Edit.Help = "BEHAVIOUR: write and edit stances.";
            ids.Add("tac.stance.reset", tuneBox.Reset);
            ids.Add("tac.stance.save", tuneBox.Save);
            ids.Add("tac.stance.edit", tuneBox.Edit);
            // The 0.9 PROFILE picker's id cycles RESERVE, ESCORT, SWEEP for the scope.
            ids.Add("tac.posture.profile", CycleProfile);
        }

        private void CycleProfile()
        {
            if (last == null || !last.CanOrder) return;
            string now = last.ScopeDoctrine(out WingDoctrine d) ? d.PatternName : null;
            int at = -1;
            for (int i = 0; i < WmcPostureActions.Profiles.Length; i++)
                if (WmcPostureActions.Profiles[i].PatternName == now) at = i;
            WmcPostureActions.Profile(last, (at + 1) % WmcPostureActions.Profiles.Length);
            FlashScope();
        }

        private void PickSlot(int i)
        {
            if (last == null) return;
            WmcMotion.Punch(slots.Rect);
            if (!WmcStanceActions.ApplySlot(last, i))
            {
                WingToast.Show("Slot " + (i + 1) + " is empty: SAVE AS… keeps the settings in the next free one");
                return;
            }
            FlashScope();
        }

        private void ResetStance()
        {
            Stance s = stanceSlot >= 0 ? WmcStanceActions.Book.Slot(stanceSlot) : null;
            if (s == null || last == null) return;
            WmcStanceActions.Apply(last, s);
            FlashScope();
        }

        private void SaveStance()
        {
            if (last == null || liveAxes == null) return;
            StanceBook book = WmcStanceActions.Book;
            int free = StanceMatch.NextFreeSlot(book);
            if (free < 0)
            {
                WingToast.Show("All six slots are used: free one on BEHAVIOUR › STANCES");
                return;
            }
            Stance s = StanceMatch.SaveAs(book, liveAxes);
            if (s == null)
            {
                WingToast.Show("The stance list is full: delete one on BEHAVIOUR › STANCES");
                return;
            }
            WingToast.Show(WmcStanceFiles.Save() ? "Saved " + s.Name + " as W+" + (free + 1) : s.Name + " added for this session (the file could not be written)");
            stanceKey = int.MinValue;
        }

        /// <summary>EDIT ›: BEHAVIOUR › STANCES, its editor opened on the scope's matched (or closest) stance.</summary>
        private void EditStance()
        {
            WmcPanel panel = WmcPanel.Instance;
            if (panel == null) return;
            panel.Show(WmcTabs.Behaviour);
            WmcPlan plan = panel.Plan;
            if (plan == null) return;
            plan.ShowSub(WmcPlan.SubStances);
            Stance s = stanceSlot >= 0 ? WmcStanceActions.Book.Slot(stanceSlot) : null;
            if (s != null) plan.Options.EditStance(s.Id);
        }

        private void RefreshStance(WmcContext c)
        {
            bool host = c.CanOrder;
            WingDoctrine doctrine = default;
            bool same = host && c.ScopeDoctrine(out doctrine);
            liveAxes = same ? StanceDoctrine.Axes(doctrine) : null;
            StanceBook book = WmcStanceActions.Book;
            stanceMask = 0;
            stanceSlot = liveAxes != null ? StanceMatch.Closest(book, liveAxes, out stanceMask) : -1;
            for (int a = 0; a < TuneAxes.Length; a++) tuneValues[a] = host ? c.ScopeValue(TuneAxes[a]) : -1;
            int key = stanceSlot * 7 + stanceMask * 131 + (host ? 1 : 0) + (same ? 2 : 0) + c.ScopeLabel.GetHashCode() + book.All.Count * 17;
            for (int a = 0; a < tuneValues.Length; a++) key = key * 31 + tuneValues[a];
            for (int i = 0; i < StanceBook.Slots; i++) key = key * 13 + (book.Slot(i)?.Id?.GetHashCode() ?? 0);
            bool changed = StanceDiff.Count(stanceMask) > 0;
            bool canSave = same && StanceMatch.NextFreeSlot(book) >= 0;
            tuneBox.Reset.Interactable = changed && host;
            tuneBox.Save.Interactable = canSave;
            if (key == stanceKey) return;
            stanceKey = key;
            for (int i = 0; i < StanceBook.Slots; i++)
            {
                Stance s = book.Slot(i);
                bool on = i == stanceSlot && same;
                slots.Set(i, s != null ? s.Name : "", s != null ? StanceSlotWords.Summary(s.Axes) : "EMPTY · SAVE AS…", s != null, on, on && changed);
            }
            Stance matched = stanceSlot >= 0 ? book.Slot(stanceSlot) : null;
            int diff = StanceDiff.Count(stanceMask);
            string note = !host ? WmcPostureActions.Cannot(c).ToUpperInvariant() : !same ? "MIXED · THE SCOPE FLIES DIFFERENT SETTINGS"
                : matched == null ? "NO STANCE · SAVE AS… KEEPS THESE SETTINGS"
                : !changed ? "MATCHES " + matched.Name
                : "* " + AvNum.Fixed(diff, 0) + (diff == 1 ? " CHANGE" : " CHANGES") + " FROM " + matched.Name;
            tuneBox.SetStatus(note, changed && same);
            string caption = c.ScopeLabel + " · TAP OR WING + 1-6";
            if (caption != stanceNoteShown)
            {
                stanceNoteShown = caption;
                stanceSection.SetCaption(caption);
            }
            for (int a = 0; a < TuneAxes.Length; a++)
            {
                int axis = (int)TuneAxes[a];
                bool edited = same && matched != null && StanceDiff.Changed(stanceMask, axis);
                tuneRows[a].Show(tuneValues[a], edited ? matched.Axes[axis] : -1, edited);
                foreach (AvControl o in tuneRows[a].Options)
                    if (o.Interactable != host) o.Interactable = host;
            }
            relayout = true;
        }
    }
}
