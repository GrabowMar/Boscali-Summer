namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// Requests one OPS action at a world point as the local pilot, exactly as the OPS console does (cost, cooldown and every gate
    /// apply unless the [Debug] bypass switches are on). Published by Support.
    /// </summary>
    internal interface ISupportCast
    {
        /// <summary>Submits <paramref name="actionId"/> (a <c>SupportActionId</c> value) at <paramref name="target"/>; the answer is the
        /// console's status line after the request (a host evaluates it inside this call).</summary>
        string CastAt(byte actionId, GlobalPosition target);
    }
}
