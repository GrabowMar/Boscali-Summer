using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The client side of SOF: the mirror of the local player's own faction and the verbs the SOF page sends. SOF request ids share the SPACE request counter, so they
    /// can never collide with CALLS ids; the verdicts arrive through <see cref="SpaceReplied"/> like every SPACE reply. A press is a request, never an effect.
    /// </summary>
    internal sealed partial class SupportManager
    {
        private readonly SofMirror sofMirror = new SofMirror();
        private MirrorFeed<SofStateData> sofFeed;

        /// <summary>The local player's faction SOF view as the host last told it (own faction only).</summary>
        internal SofMirror SofMirror => sofMirror;

        /// <summary>The SOF page (or an armed AIM: TEAM call) is on screen: while it is, the feed asks the host for a state.</summary>
        internal MirrorFeed<SofStateData> SofFeed => sofFeed ?? (sofFeed = new MirrorFeed<SofStateData>(sofMirror, SpaceCommandKind.SofSync));

        /// <summary>RAISE a team at a standing camp. Returns the request id (0 when not sent).</summary>
        internal int SofRaise() => SendSpace(SpaceCommandKind.SofRaise, 0, null);

        /// <summary>PUSH, HOLD, EXFIL, LIFT or CANCEL for one team.</summary>
        internal int SofOrder(int slot, TeamVerb verb) => slot < 0 || slot >= SofRules.MaxTeams ? 0 : SendSpace(SpaceCommandKind.SofOrder, SofCodes.PackOrder(slot, verb), null);

        /// <summary>Start a mission. RECON takes the point (<paramref name="x"/>, <paramref name="z"/>); every other kind takes the target id the mirror listed.</summary>
        internal int SofMission(int slot, MissionKind kind, int targetId, float x, float z) =>
            slot < 0 || slot >= SofRules.MaxTeams || kind == MissionKind.None ? 0 :
            SendSpace(SpaceCommandKind.SofMission, 0, new[] { SofCodes.PackMission(slot, kind), kind == MissionKind.Recon ? SofCodes.PackPoint(x, z) : targetId });

        /// <summary>DIVERT a team to a new destination (cancels its mission).</summary>
        internal int SofDivert(int slot, float x, float z) =>
            slot < 0 || slot >= SofRules.MaxTeams ? 0 : SendSpace(SpaceCommandKind.SofDivert, 0, new[] { slot, SofCodes.PackPoint(x, z) });
    }
}
