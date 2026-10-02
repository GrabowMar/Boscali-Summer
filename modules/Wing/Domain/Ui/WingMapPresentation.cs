using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Persistent wing identity and command scope, independent of weapon targeting so clearing
    /// targets leaves both intact.</summary>
    internal readonly struct WingMapPresentation
    {
        public enum OutlineKind { None, Member, Target, Downed }

        public OutlineKind Outline { get; }
        public bool CommandBrackets { get; }

        private WingMapPresentation(OutlineKind outline, bool commandBrackets)
        {
            Outline = outline;
            CommandBrackets = commandBrackets;
        }

        public static WingMapPresentation Resolve(bool isWingMember, bool isWingTarget,
            bool highlightWing, bool highlightTargets, bool tacticalActive, bool commandSelected,
            bool isDowned = false)
        {
            // Wing membership takes precedence over stale weapon-target selection.
            OutlineKind outline = isDowned
                ? OutlineKind.Downed
                : isWingMember
                ? (highlightWing ? OutlineKind.Member : OutlineKind.None)
                : (isWingTarget && highlightTargets ? OutlineKind.Target : OutlineKind.None);
            return new WingMapPresentation(outline,
                isWingMember && tacticalActive && commandSelected);
        }
    }
}
