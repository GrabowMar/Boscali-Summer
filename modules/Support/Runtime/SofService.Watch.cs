using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The seams WATCH OFFICER OVERLORD and the AI doctrine read on SOF: the weight of a revealed ground contact, and the first building the faction holds.</summary>
    internal sealed partial class SofService
    {
        /// <summary>
        /// The vanilla value and weight class of a ground contact (the same classification SPACE's OVERLORD ranks a MARK by). OVERLORD asks only about targets the desk lists
        /// (a real sighting); a key the observations cannot resolve answers false.
        /// </summary>
        internal bool TryGroundInfo(FactionHQ owner, uint key, out float value, out WatchKind kind)
        {
            value = 0f; kind = WatchKind.Other;
            if (owner == null || !factions.TryGetValue(owner, out FactionSof f) || !f.Obs.TryUnit(key, out Unit unit) || unit == null || unit.disabled) return false;
            UnitDefinition definition = unit.definition;
            if (definition == null) return false;
            value = definition.value;
            kind = WatchOfficerPolicy.KindOf(definition.code, definition.roleIdentity.antiSurface, definition.roleIdentity.antiAir);
            return true;
        }

        /// <summary>The id of a building the faction holds (the FOB's target), 0 when it holds none.</summary>
        internal int FirstHeldId(FactionHQ owner) => TryDesk(owner, out SofDesk desk) && desk.Held.Count > 0 ? desk.Held[0].Id : 0;
    }
}
