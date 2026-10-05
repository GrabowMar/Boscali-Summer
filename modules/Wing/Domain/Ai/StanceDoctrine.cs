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

        /// <summary>RESERVE, ESCORT and SWEEP: the doctrine's own presets, always in slots 1-3 of a fresh book.</summary>
        public static Stance[] BuiltIns => new[]
        {
            new Stance { Id = "reserve", Name = "RESERVE", Axes = Axes(WingDoctrine.Reserve), BuiltIn = true },
            new Stance { Id = "escort", Name = "ESCORT", Axes = Axes(WingDoctrine.Escort), BuiltIn = true },
            new Stance { Id = "sweep", Name = "SWEEP", Axes = Axes(WingDoctrine.Sweep), BuiltIn = true },
        };
    }
}
