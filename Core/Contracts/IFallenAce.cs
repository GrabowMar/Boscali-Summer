namespace BoscaliSummer.Core.Contracts
{
    /// <summary>The Spooktober FALLEN ACE boss, summoned and sent away from outside the module. Host only; each call answers with a UI line.</summary>
    internal interface IFallenAce
    {
        bool Alive { get; }
        string Summon();
        string Dismiss();
    }
}
