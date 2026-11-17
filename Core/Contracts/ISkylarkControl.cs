namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The VS-4 Skylark's own controls on the local aircraft: drive mode, fan tilt mode, the C2 mast. Skylark owns the state and its
    /// host message; the C menu only reads it and asks for a change. <see cref="Active"/> is false for any other aircraft.
    /// </summary>
    internal interface ISkylarkControl
    {
        /// <summary>The local player flies a Skylark.</summary>
        bool Active { get; }

        /// <summary>0 AUTO, 1 EV (electric stealth), 2 HYBRID (turbine on).</summary>
        int Drive { get; }

        /// <summary>0 AUTO (tilt by airspeed), 1 DRONE (fans down, auto-hover), 2 PLANE (fans aft), 3 MANUAL (the pilot's own tilt axis).</summary>
        int Vector { get; }

        /// <summary>Battery charge for the DRIVE row, e.g. "BAT 78%", with HEAVY / BOOST / DRY when one applies.</summary>
        string Battery { get; }

        void SetDrive(int mode);
        void SetVector(int mode);

        bool MastDeployed { get; }
        void ToggleMast();

        /// <summary>Set by the autopilot while it lands the local aircraft: the Skylark's fan modes then leave the fans and hover to it.</summary>
        bool AutopilotOwnsFans { get; set; }
    }
}
