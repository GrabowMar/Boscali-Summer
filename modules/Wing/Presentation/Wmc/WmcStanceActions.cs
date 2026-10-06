using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Configuration;

namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Applying a stance (spec 2026-10-04 §4.1): the existing SetDoctrine order for the scope; the wing-wide follow-ons
    /// (fall back, winchester, bingo) only when the scope is the whole wing, because they are wing-wide config. Shared by ORDERS'
    /// stance slots, BEHAVIOUR › STANCES and the wing-key chord, so the three cannot drift apart.</summary>
    internal static class WmcStanceActions
    {
        public static StanceBook Book => WmcStanceFiles.Book;

        /// <summary>Applies slot <paramref name="slot"/> (0-5) to the WMC's scope; false when the slot is empty.</summary>
        public static bool ApplySlot(WmcContext c, int slot)
        {
            Stance s = Book.Slot(slot);
            if (s == null) return false;
            Apply(c, s);
            return true;
        }

        public static void Apply(WmcContext c, Stance s)
        {
            if (c == null || s == null) return;
            WmcUi.Order(c, () => Run(c.Scope, s));
        }

        /// <summary>The order itself, for callers outside the WMC (the wing-key chord): host only.</summary>
        public static OrderResult Run(WingScope scope, Stance s)
        {
            OrderResult r = WingOrders.Run(new WingOrder
            {
                Kind = OrderKind.SetDoctrine, Text = StanceDoctrine.ToDoctrine(s.Axes).ToString(), Scope = scope,
            });
            if (r.Accepted && scope.Kind == ScopeKind.Wing)
            {
                WingConfig w = WingSettings.Instance;
                if (s.FallBack >= 0 && s.FallBack < WmcPostureActions.FallBackRatios.Length)
                    w.FallBackRatio.Value = WmcPostureActions.FallBackRatios[s.FallBack];
                if (s.Winchester >= 0 && s.Winchester <= (int)WinchesterAction.Refit) w.AfterWinchester.Value = (WinchesterAction)s.Winchester;
                if (s.Bingo >= 0 && s.Bingo <= (int)BingoAction.Refit) w.AfterBingo.Value = (BingoAction)s.Bingo;
            }
            return r;
        }

        /// <summary>The stance whose axes the scope flies now, or null when none matches exactly (then ORDERS shows the closest
        /// slot with a * and its changed axes).</summary>
        public static Stance Matching(byte[] liveAxes)
        {
            foreach (Stance s in Book.All)
                if (StanceDiff.Mask(liveAxes, s.Axes) == 0) return s;
            return null;
        }
    }
}
