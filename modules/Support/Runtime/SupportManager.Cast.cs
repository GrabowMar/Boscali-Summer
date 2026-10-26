using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Support.Runtime
{
    internal sealed partial class SupportManager : ISupportCast
    {
        /// <summary>ARES's free cast: the same request the OPS console sends, its answer read off the status line.</summary>
        string ISupportCast.CastAt(byte actionId, GlobalPosition target)
        {
            RequestAt((SupportActionId)actionId, target);
            return Status;
        }
    }
}
