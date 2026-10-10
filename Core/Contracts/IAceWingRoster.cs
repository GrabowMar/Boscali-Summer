using System.Collections.Generic;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The enemy ace wings this machine owns, as aircraft. Host-side knowledge: a remote client
    /// holds no ace aircraft handles, so its list stays empty.
    /// </summary>
    internal interface IAceWingRoster
    {
        /// <summary>Appends every live ace-wing aircraft with the identity its crest is derived
        /// from ("symbol|wing name", the same key the PILOT ACES page uses).</summary>
        void CopyAceAircraft(List<KeyValuePair<string, Aircraft>> destination);
    }
}
