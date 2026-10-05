namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The one thing about an AI faction's intended policy the SPACE feed may say: the name of the public map objective its
    /// director has made the main effort. Host-only and read-only; it carries no coordinate, unit or order. Published by the
    /// TheaterOps module through <c>ModuleContext.AddService</c>; a caller treats false as "unknown", never as "none".
    /// </summary>
    internal interface IEnemyIntentSource
    {
        /// <param name="factionName">The faction's own name (<c>FactionHQ.faction.factionName</c>).</param>
        /// <param name="objectiveLabel">The objective's display name when the director has set a main effort.</param>
        bool TryGetMainEffort(string factionName, out string objectiveLabel);
    }
}
