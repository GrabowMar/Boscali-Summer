namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// SKYWELL tanker kit on the local aircraft. Vanguard owns the kit, the host message and validation;
    /// the radial menu only reads state and asks for a deploy / stow.
    /// </summary>
    internal interface ISkywellControl
    {
        /// <summary>The local aircraft carries a SKYWELL kit.</summary>
        bool Carried { get; }

        /// <summary>The kit is deployed (ramp open, deck out).</summary>
        bool Deployed { get; }

        /// <summary>Remaining stock for the radial status, e.g. "FUEL 5.9T  AMMO 1.24T".</summary>
        string Stock { get; }

        void Toggle();
    }
}
