namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The local faction's nation (factories, policies, war support, doctrine, decisions, the cabinet) for the STR, FAC and EVN screens.
    /// Published by the Nation module; absent when the module is off. The host owns every number: a press is a request, and the answer
    /// shows in the next view.
    /// </summary>
    internal interface INationView
    {
        /// <summary>The layer runs in this mission and a view of the local faction has arrived.</summary>
        bool Active { get; }

        /// <summary>The local faction's nation, or null while <see cref="Active"/> is false. Timers are seconds left, kept current.</summary>
        NationView Local { get; }

        /// <summary>
        /// Asks the host (or applies at once when this peer is the host). The return value is the host's answer on the host and
        /// <see cref="NationOutcome.Ok"/> for "sent" on a client, whose real answer arrives through <see cref="LastOutcome"/>.
        /// </summary>
        NationOutcome Request(NationVerb verb, int a = 0, int b = 0);

        /// <summary>The newest answer to a request of the local player and the unscaled time it arrived (0 before the first).</summary>
        NationOutcome LastOutcome { get; }
        float LastOutcomeAt { get; }
    }

    /// <summary>The vehicle reserve the factories fill, for the convoy owners.</summary>
    internal interface INationModifiers
    {
        /// <summary>Host only: the vehicle reserve covers this convoy group (always true where the layer does not run).</summary>
        bool CanSupply(FactionHQ hq, Faction.ConvoyGroup group);
    }

    /// <summary>The one-line read for the convoy owners: true when the Nation module is absent or off.</summary>
    internal static class NationScale
    {
        public static bool CanConvoy(FactionHQ hq, Faction.ConvoyGroup group) =>
            !Services.ModuleServices.TryGet(out INationModifiers nation) || nation.CanSupply(hq, group);
    }

    /// <summary>What politics allows one faction's war right now. <see cref="Vanilla"/> is the game and the staff left alone.</summary>
    internal struct WarPolicy
    {
        /// <summary>The posture band (floor == ceiling forces it).</summary>
        public TheaterWarPosture Floor, Ceiling;
        /// <summary>What an enemy assault on this faction's fronts needs on top of its own posture ratio (1 = none).</summary>
        public float Defence;
        /// <summary>AI jets over (under) the mission's own limit.</summary>
        public int Jets;
        /// <summary>Multiplier on the skill of AI the faction spawns from now on (1 = the mission's).</summary>
        public float Skill;

        public static WarPolicy Vanilla => new WarPolicy { Floor = TheaterWarPosture.Cautious, Ceiling = TheaterWarPosture.Bold, Defence = 1f, Skill = 1f };
    }

    /// <summary>Host: politics' hold on a faction's war (TheaterOps implements). The Nation module sets it every second and hands back Vanilla when it stops.</summary>
    internal interface ITheaterWarPolicy
    {
        void Apply(FactionHQ hq, in WarPolicy policy);

        /// <summary>A staff offensive from the live front nearest a point toward it (GLOBAL metres), as a pilot's drawn one; false when none could start.</summary>
        bool LaunchOffensive(FactionHQ hq, float x, float z, string by);

        /// <summary>The faction's live main effort, for the enemy's intel line.</summary>
        bool TryMainEffort(FactionHQ hq, out string name);
    }
}
