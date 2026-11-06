using NOAvionics;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>TACTICAL › ORDERS › STANCE (spec 2026-10-04 §4.1; mockup board/orders.html .slots): six stance slots (W+1 … W+6, the same
    /// stances as BEHAVIOUR › STANCES and the wing-key chord). The slot whose stance the scope flies exactly is lit; a mixed scope, or
    /// one with settings changed on BEHAVIOUR, lights none.</summary>
    internal sealed partial class WmcTactical
    {
        private AvSection stanceSection;
        private WmcStanceSlots slots;
        private int stanceKey = int.MinValue;

        private void BuildStance(AvFlow f)
        {
            stanceSection = f.Section(AvIcon.Shield, "STANCE", "");
            slots = f.Add(new WmcStanceSlots(f.Content, PickSlot, null));
            for (int i = 0; i < WmcStanceSlots.Count; i++)
            {
                slots.SetHelp(i, "Wing key + " + (i + 1) + ": fly this stance with the scope.");
            }
        }

        private void PickSlot(int i)
        {
            if (last == null) return;
            WmcMotion.Punch(slots.Rect);
            if (!WmcStanceActions.ApplySlot(last, i))
            {
                WingToast.Show("Slot " + (i + 1) + " is empty: assign a stance on BEHAVIOUR › STANCES");
                return;
            }
            FlashScope();
        }

        private void RefreshStance(WmcContext c)
        {
            WingDoctrine doctrine = default;
            byte[] live = c.CanOrder && c.ScopeDoctrine(out doctrine) ? StanceDoctrine.Axes(doctrine) : null;
            StanceBook book = WmcStanceActions.Book;
            int lit = -1, key = c.ScopeLabel.GetHashCode() + book.All.Count * 17;
            for (int i = 0; i < StanceBook.Slots; i++)
            {
                Stance s = book.Slot(i);
                if (lit < 0 && live != null && s != null && StanceDiff.Mask(live, s.Axes) == 0) lit = i;
                key = key * 13 + (s?.Id?.GetHashCode() ?? 0);
                // A rename or an edit on BEHAVIOUR shows here too.
                if (s == null) continue;
                key = key * 31 + (s.Name?.GetHashCode() ?? 0);
                foreach (byte axis in s.Axes) key = key * 31 + axis;
            }
            key = key * 7 + lit;
            if (key == stanceKey) return;
            stanceKey = key;
            stanceSection.SetCaption(c.ScopeLabel + " · TAP OR WING + 1-6");
            for (int i = 0; i < StanceBook.Slots; i++)
            {
                Stance s = book.Slot(i);
                slots.Set(i, s != null ? s.Name : "", s != null ? StanceSlotWords.Summary(s.Axes) : "EMPTY", s != null, i == lit);
            }
        }
    }
}
