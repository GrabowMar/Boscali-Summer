using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Spooktober.Domain;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>The FALLEN ACE for ARES's SUMMON row, through <see cref="IFallenAce"/>: the same spawn the boss menu runs (host only,
    /// single player, airborne host), ignoring the chain the menu locks.</summary>
    internal sealed class SpookAce : IFallenAce
    {
        public bool Alive => FallenAce.Alive;
        public string Summon() => SpookBosses.Spawn(SpookBoss.FallenAce);
        public string Dismiss() => FallenAce.Alive ? FallenAce.Dismiss() : "NO ACE";
    }
}
