using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>SUPPORT > PIT STOP: the receiver's standing order and which pylon is served first (owner: Vanguard, through ISkywellControl).</summary>
    internal static partial class AceRadialCatalog
    {
        private const int MaxPitStations = 8;

        private static AceRadialAction PitStop() =>
            new AceRadialAction("pit", "PIT STOP", null, () => Service<ISkywellControl>()?.PitAvailable == true)
                .WithIcon(AceIcon.Support).WithStatus(PitStopStatus).WithChildren(PitStopLeaves);

        private static AceRadialStatus PitStopStatus()
        {
            ISkywellControl kit = Service<ISkywellControl>();
            if (kit == null) return AceRadialStatus.None;
            return new AceRadialStatus(kit.PitMode == 1 ? "FUEL ONLY" : kit.PitMode == 2 ? "MUNITIONS ONLY" : "BOTH", AceTone.Normal);
        }

        private static IEnumerable<AceRadialAction> PitStopLeaves()
        {
            if (Service<ISkywellControl>() == null) yield break;
            yield return PitOrderLeaf("pit-both", "BOTH", AceIcon.Support, 0);
            yield return PitOrderLeaf("pit-fuel", "FUEL ONLY", AceIcon.Engine, 1);
            yield return PitOrderLeaf("pit-mun", "MUNITIONS ONLY", AceIcon.Weapons, 2);
            yield return Branch("pit-first", "HANDOFF FIRST", AceIcon.Station).WithChildren(PitStationLeaves);
        }

        private static AceRadialAction PitOrderLeaf(string id, string label, AceIcon icon, byte mode) =>
            Leaf(id, label, icon, () => Service<ISkywellControl>()?.SetPitMode(mode),
                status: () => Service<ISkywellControl>()?.PitMode == mode ? new AceRadialStatus("ACTIVE", AceTone.Active) : AceRadialStatus.None);

        private static IEnumerable<AceRadialAction> PitStationLeaves()
        {
            ISkywellControl kit = Service<ISkywellControl>();
            if (kit == null) yield break;
            int count = System.Math.Min(kit.PitStationCount, MaxPitStations);
            for (int i = 0; i < count; i++)
            {
                int slot = i;
                string label = kit.PitStationLabel(slot);
                if (string.IsNullOrEmpty(label)) continue;
                yield return Leaf("pit-s" + slot, label, AceIcon.Station, () => kit.PrioritiseStation(slot),
                    status: () =>
                    {
                        string text = kit.PitStationStatus(slot);
                        return new AceRadialStatus(text, text.EndsWith("FIRST") ? AceTone.Active : AceTone.Normal);
                    });
            }
            yield return Leaf("pit-auto", "AUTO ORDER", AceIcon.Clear, () => kit.PrioritiseStation(-1));
        }
    }
}
