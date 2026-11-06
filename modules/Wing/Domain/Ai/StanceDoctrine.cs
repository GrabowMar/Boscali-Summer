using BoscaliSummer.Modules.Wing.Domain.Pure;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Stance ⇄ WingDoctrine (spec 2026-10-04 §4.1): a stance's 8 axes in DoctrineAxis order. Applying a stance stays the
    /// existing SetDoctrine order; this only converts.</summary>
    internal static class StanceDoctrine
    {
        public static byte[] Axes(WingDoctrine d)
        {
            var a = new byte[Stance.AxisCount];
            for (int i = 0; i < Stance.AxisCount; i++) a[i] = d.Get((DoctrineAxis)i);
            return a;
        }

        public static WingDoctrine ToDoctrine(byte[] axes)
        {
            WingDoctrine d = WingDoctrine.Reserve;
            if (axes == null || axes.Length < Stance.AxisCount) return d;
            for (int i = 0; i < Stance.AxisCount; i++) d = d.With((DoctrineAxis)i, axes[i]);
            return d;
        }

        /// <summary>The built-in scenarios, slots 1-6 of a fresh book: RESERVE, ESCORT and SWEEP (the doctrine's own presets), then
        /// INTERCEPT (air only, long, missiles, press), STRIKE (ground, long, radar silent, RTB at winchester) and SILENT (hold fire,
        /// radar off, hold the slot: slip through).</summary>
        public static Stance[] BuiltIns => new[]
        {
            new Stance { Id = "reserve", Name = "RESERVE", Axes = Axes(WingDoctrine.Reserve), BuiltIn = true },
            new Stance { Id = "escort", Name = "ESCORT", Axes = Axes(WingDoctrine.Escort), BuiltIn = true },
            new Stance { Id = "sweep", Name = "SWEEP", Axes = Axes(WingDoctrine.Sweep), BuiltIn = true },
            new Stance
            {
                Id = "intercept", Name = "INTERCEPT", BuiltIn = true,
                Axes = Axes(new WingDoctrine(MissileGuard.Self, MissileResponse.Press, FormationInterval.Standard, true,
                    TargetPolicy.Air, EngagementReach.Long, WeaponsPolicy.Missiles, RadarPolicy.On)),
            },
            new Stance
            {
                Id = "strike", Name = "STRIKE", BuiltIn = true, Winchester = (sbyte)WinchesterAction.Rtb,
                Axes = Axes(new WingDoctrine(MissileGuard.Self, MissileResponse.Break, FormationInterval.Open, true,
                    TargetPolicy.Ground, EngagementReach.Long, WeaponsPolicy.Auto, RadarPolicy.Silent)),
            },
            new Stance
            {
                Id = "silent", Name = "SILENT", BuiltIn = true,
                Axes = Axes(new WingDoctrine(MissileGuard.Off, MissileResponse.Break, FormationInterval.Close, false,
                    TargetPolicy.Hold, EngagementReach.Slot, WeaponsPolicy.Auto, RadarPolicy.Off)),
            },
        };
    }
}
