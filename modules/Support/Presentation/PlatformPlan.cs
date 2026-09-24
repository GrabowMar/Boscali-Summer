using BoscaliSummer.Features.Support.Domain.Orbital;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// Client-local design intent: which mission the player is fitting for, which cell is
    /// selected and which module is queued for the next launch.
    ///
    /// <para>The OPS › SPACE › STATUS page reads it to say what the station still needs; the
    /// full-screen station console writes it. Keeping it in one object means the two surfaces
    /// cannot disagree, and it is never sent to the host — every launch is validated there
    /// on its own merits.</para>
    /// </summary>
    internal sealed class PlatformPlan
    {
        public PlatformMission Mission { get; set; } = PlatformMission.Recon;

        /// <summary>The selected truss cell, or -1 before the console has picked one.</summary>
        public int Cell { get; set; } = -1;

        /// <summary>The module queued for the next launch, or <see cref="ModuleKind.None"/>.</summary>
        public ModuleKind Module { get; set; } = ModuleKind.None;

        public void Reset()
        {
            Mission = PlatformMission.Recon;
            Cell = -1;
            Module = ModuleKind.None;
        }

        /// <summary>Adopt the next step of the current mission fit, so a click on ADVISE agrees
        /// with what the launch card is about to send.</summary>
        public PlatformFitStep Adopt(OrbitalPlatform platform, double now)
        {
            PlatformFitStep step = PlatformMissions.Next(platform, Mission, now);
            if (step.Complete) return step;
            Module = step.Module;
            Cell = step.Cell;
            return step;
        }
    }
}
