namespace BoscaliSummer.Features.Command.Domain
{
    /// <summary>
    /// What a friendly AI pilot is currently pointed at, reduced to the handful of kinds
    /// that change what the sortie <em>is</em>. Deliberately not the game's unit enum: the
    /// classifier is compiled into the test project, which has no game install.
    /// </summary>
    internal enum SortieTarget : byte
    {
        /// <summary>No target, or a pilot that is not in a combat state at all.</summary>
        None = 0,
        Aircraft = 1,

        /// <summary>A radar or SAM emitter. Hunting one is a different job from bombing it.</summary>
        Emitter = 2,
        Vehicle = 3,
        Infantry = 4,
        Ship = 5,
        Structure = 6,
    }

    /// <summary>The role a sortie reads as on the theater board.</summary>
    internal enum SortieRole : byte
    {
        /// <summary>Airborne with nothing assigned: taxi, climb-out, ferry, recovery.</summary>
        Transit = 0,
        Cap = 1,
        Sead = 2,
        Cas = 3,
        Strike = 4,
    }

    /// <summary>
    /// Turns "what is it chasing" into "what is it doing".
    ///
    /// <para>The alternative was the AI's own <c>attackMode</c>, which says how a pilot is
    /// attacking — missiles, guns, glide bombs — not what it is attacking. A jet strafing a
    /// tank and a jet strafing a MiG report the same mode and are not the same sortie.</para>
    /// </summary>
    internal static class SortieClassifier
    {
        public static SortieRole Classify(SortieTarget target)
        {
            switch (target)
            {
                case SortieTarget.Aircraft: return SortieRole.Cap;
                case SortieTarget.Emitter: return SortieRole.Sead;
                case SortieTarget.Vehicle:
                case SortieTarget.Infantry: return SortieRole.Cas;
                case SortieTarget.Ship:
                case SortieTarget.Structure: return SortieRole.Strike;
                default: return SortieRole.Transit;
            }
        }

        /// <summary>The three-letter code a row is labelled with. Kind on the code, state on the rail.</summary>
        public static string Code(SortieRole role)
        {
            switch (role)
            {
                case SortieRole.Cap: return "CAP";
                case SortieRole.Sead: return "SEA";
                case SortieRole.Cas: return "CAS";
                case SortieRole.Strike: return "STR";
                default: return "TRN";
            }
        }

        public static string Name(SortieRole role)
        {
            switch (role)
            {
                case SortieRole.Cap: return "COMBAT AIR PATROL";
                case SortieRole.Sead: return "SEAD";
                case SortieRole.Cas: return "CLOSE AIR SUPPORT";
                case SortieRole.Strike: return "STRIKE";
                default: return "TRANSIT";
            }
        }
    }

    /// <summary>
    /// A running count of friendly AI sorties by role.
    ///
    /// <para><see cref="Observed"/> is the honest denominator: when the AI pilot state cannot
    /// be reached at all, nothing is observed and the board must say so rather than print a
    /// confident row of zeros.</para>
    /// </summary>
    internal struct SortieTally
    {
        public int Cap;
        public int Sead;
        public int Cas;
        public int Strike;
        public int Transit;

        /// <summary>Aircraft the tally actually managed to look at.</summary>
        public int Observed;

        /// <summary>Sorties with an assigned target; transit is airborne, not tasked.</summary>
        public int Tasked => Cap + Sead + Cas + Strike;

        public void Add(SortieRole role)
        {
            Observed++;
            switch (role)
            {
                case SortieRole.Cap: Cap++; break;
                case SortieRole.Sead: Sead++; break;
                case SortieRole.Cas: Cas++; break;
                case SortieRole.Strike: Strike++; break;
                default: Transit++; break;
            }
        }

        public int Of(SortieRole role)
        {
            switch (role)
            {
                case SortieRole.Cap: return Cap;
                case SortieRole.Sead: return Sead;
                case SortieRole.Cas: return Cas;
                case SortieRole.Strike: return Strike;
                default: return Transit;
            }
        }

        public void Reset()
        {
            Cap = Sead = Cas = Strike = Transit = Observed = 0;
        }
    }
}
