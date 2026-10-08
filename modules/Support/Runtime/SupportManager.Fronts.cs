using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// OPS FRONTS on the manager. Host side: the seam to the <see cref="FrontService"/>. Client side: the mirror of the local player's own faction's three fronts and the five verbs a window
    /// sends (a press is a request, never an effect; the verdict arrives through <see cref="SpaceReplied"/> like every SPACE reply and is worded with <see cref="FrontWords"/>).
    /// </summary>
    internal sealed partial class SupportManager
    {
        private FrontService frontService;
        private readonly FrontMirror frontMirror = new FrontMirror();
        private MirrorFeed<FrontStateData> frontFeed;

        internal FrontService Fronts => frontService;

        internal void AttachFronts(FrontService service) => frontService = service;

        /// <summary>The local player's faction front view as the host last told it (own faction only).</summary>
        internal FrontMirror FrontMirror => frontMirror;

        /// <summary>A window or card that reads the mirror is on screen: while it is, the feed asks the host for a state until one arrives.</summary>
        internal MirrorFeed<FrontStateData> FrontFeed => frontFeed ?? (frontFeed = new MirrorFeed<FrontStateData>(frontMirror, SpaceCommandKind.FrontSync));

        /// <summary>One front command for a transport-authenticated player (host only; the service judges it).</summary>
        internal FrontResult RunFrontVerb(Player player, in SpaceCommand command) =>
            frontService != null ? frontService.Verb(player, command) : new FrontResult(FrontOutcome.Unavailable);

        // ---- Client verbs (each returns the request id, 0 when not sent) -----------------------------------

        private static int Pack(Front front, int value) => (int)front | (value << 2);

        /// <summary>Set a front's posture (any member; a 60 s lock follows).</summary>
        internal int FrontSetDirective(Front front, FrontDirective directive) =>
            FrontRules.IsValid(front, directive) ? SendSpace(SpaceCommandKind.FrontDirective, Pack(front, (int)directive), null) : 0;

        /// <summary>Set the faction-wide funding weights for SPACE, CYBER and SOF (any member; a 60 s lock follows).</summary>
        internal int FrontSetPriority(int spaceWeight, int cyberWeight, int sofWeight) =>
            spaceWeight < 0 || cyberWeight < 0 || sofWeight < 0 || spaceWeight + cyberWeight + sofWeight <= 0 ? 0 : SendSpace(SpaceCommandKind.FrontPriority, 0, new[] { spaceWeight, cyberWeight, sofWeight });

        /// <summary>Pin a front's focus to a map point.</summary>
        internal int FrontSetFocus(Front front, float x, float z) =>
            SendSpace(SpaceCommandKind.FrontFocus, 0, new[] { Pack(front, 0), SofCodes.PackPoint(x, z) });

        internal int FrontClearFocus(Front front) => SendSpace(SpaceCommandKind.FrontFocus, 0, new[] { Pack(front, 1), 0 });

        /// <summary>Queue a programme on a front.</summary>
        internal int FrontQueue(Front front, ProgrammeId programme) =>
            FrontRules.BelongsTo(front, programme) ? SendSpace(SpaceCommandKind.FrontQueue, Pack(front, (int)programme), null) : 0;

        /// <summary>Donate allocation to a queued programme (the host takes only what the bar still needs and your balance allows).</summary>
        internal int FrontDonate(Front front, ProgrammeId programme, int amount) =>
            amount <= 0 || !FrontRules.BelongsTo(front, programme) ? 0 : SendSpace(SpaceCommandKind.FrontDonate, 0, new[] { Pack(front, (int)programme), amount });

        /// <summary>Burn one of the faction's satellites to a map point (u,v in 0..1); the host judges fuel and burn state.</summary>
        internal int RelocateBird(int bird, float u, float v) =>
            bird < 0 || bird >= SpaceRules.BirdCount ? 0 : SendSpace(SpaceCommandKind.RelocateBird, 0, new[] { bird, GeoSpace.Pack(u, v) });

        /// <summary>The "ALL" amount of a donation button: the host clamps it to the bar's remaining cost and the pilot's balance.</summary>
        internal const int DonateAll = 100000;
    }
}
