using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Ops;
using NuclearOption.Networking;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>The seam between the manager and WATCH OFFICER OVERLORD for CYBER and SOF: where a human's accepted domain verb is told to it, and where the OPERATIONS mirror reads its log.</summary>
    internal sealed partial class SupportManager
    {
        private OverlordService overlord;

        internal OverlordService Overlord => overlord;

        internal void AttachOverlord(OverlordService service) => overlord = service;

        /// <summary>The faction's last OVERLORD actions (newest last) for its own console. Empty when OVERLORD has done nothing or is not running.</summary>
        internal int CopyOverlordLog(FactionHQ owner, List<WatchLogRow> into)
        {
            into.Clear();
            return overlord != null ? overlord.CopyLog(owner, into) : 0;
        }
    }
}
